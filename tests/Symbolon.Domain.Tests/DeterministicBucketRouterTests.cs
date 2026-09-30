using FluentAssertions;
using Symbolon.Domain.Experiments;
using Xunit;

namespace Symbolon.Domain.Tests;

public sealed class DeterministicBucketRouterTests
{
    [Fact]
    public void CalculateBucket_ShouldBeStatisticallyUniform_Across10000Clients()
    {
        const int clientCount = 10000;
        const string salt = "exp_salt_v1";
        int[] buckets = new int[100];

        for (int i = 0; i < clientCount; i++)
        {
            string license = $"SYM-PRO-TEST-{i:D6}";
            string machine = $"mach-uuid-{i:D6}";
            int b = DeterministicBucketRouter.CalculateBucket(license, machine, salt);
            b.Should().BeInRange(0, 99);
            buckets[b]++;
        }

        // Chi-Square Goodness of Fit Test for Uniform Distribution across 100 buckets
        // Expected per bucket = 10000 / 100 = 100
        // Degrees of freedom = 99. Critical value for alpha = 0.05 is ~123.23
        double expected = clientCount / 100.0;
        double chiSquare = 0.0;
        for (int i = 0; i < 100; i++)
        {
            double diff = buckets[i] - expected;
            chiSquare += (diff * diff) / expected;
        }

        // Chi-square should be well within reasonable bounds
        chiSquare.Should().BeLessThan(140.0, "distribution should be uniform across buckets");
    }

    [Fact]
    public void Route_ShouldAdhereToStickySessionInvariant_ZeroVariantDrift()
    {
        var experiment = new Experiment
        {
            Id = "exp_sticky",
            Name = "Sticky session test",
            Status = ExperimentStatus.Active,
            TrafficAllocation = 100,
            Variants =
            [
                new ExperimentVariant { VariantId = "ctrl", Name = "Control", Weight = 50, IsControl = true },
                new ExperimentVariant { VariantId = "treat", Name = "Treatment", Weight = 50, IsControl = false }
            ]
        };

        const int clientCount = 1000;
        for (int i = 0; i < clientCount; i++)
        {
            string license = $"SYM-ENT-{i:D5}";
            string machine = $"mach-{i:D5}";

            // Simulate checkout
            var initialResult = DeterministicBucketRouter.Route(experiment, null, license, machine);

            // Simulate 5 renewal heartbeats and release
            for (int step = 0; step < 6; step++)
            {
                var repeatResult = DeterministicBucketRouter.Route(experiment, null, license, machine);
                repeatResult.VariantId.Should().Be(initialResult.VariantId, "variant must be sticky across lifetime");
                repeatResult.IsInExperiment.Should().Be(initialResult.IsInExperiment);
                repeatResult.IsControl.Should().Be(initialResult.IsControl);
            }
        }
    }

    [Fact]
    public void Route_ShouldRespectTrafficAllocationAndVariantWeights()
    {
        var experiment8020 = new Experiment
        {
            Id = "exp_80_20",
            Name = "80/20 Test",
            Status = ExperimentStatus.Active,
            TrafficAllocation = 100,
            Variants =
            [
                new ExperimentVariant { VariantId = "A", Name = "80%", Weight = 80, IsControl = true },
                new ExperimentVariant { VariantId = "B", Name = "20%", Weight = 20, IsControl = false }
            ]
        };

        int countA = 0;
        int countB = 0;
        const int samples = 5000;

        for (int i = 0; i < samples; i++)
        {
            var res = DeterministicBucketRouter.Route(experiment8020, null, $"KEY-{i:D5}", $"M-{i:D5}");
            res.IsInExperiment.Should().BeTrue();
            if (res.VariantId == "A") countA++;
            else if (res.VariantId == "B") countB++;
        }

        double ratioA = (double)countA / samples;
        double ratioB = (double)countB / samples;

        ratioA.Should().BeInRange(0.77, 0.83, "Variant A weight was 80%");
        ratioB.Should().BeInRange(0.17, 0.23, "Variant B weight was 20%");
    }

    [Fact]
    public void Route_ShouldRespectTargetingFilters()
    {
        var targetedExp = new Experiment
        {
            Id = "exp_target",
            Name = "Targeted experiment",
            Status = ExperimentStatus.Active,
            TrafficAllocation = 100,
            Targeting = new ExperimentTargeting
            {
                TenantIds = ["ten_target"],
                LicensePrefixes = ["SYM-TARGET-"],
                SdkLanguages = ["csharp"],
                OsPlatforms = ["linux"]
            },
            Variants =
            [
                new ExperimentVariant { VariantId = "v1", Name = "V1", Weight = 100 }
            ]
        };

        // Matching context
        var matchingContext = new ExperimentClientContext
        {
            SdkLanguage = "csharp",
            OsPlatform = "linux"
        };
        var resMatching = DeterministicBucketRouter.Route(
            targetedExp, "ten_target", "SYM-TARGET-1234", "mach-01", matchingContext);
        resMatching.IsInExperiment.Should().BeTrue();
        resMatching.VariantId.Should().Be("v1");

        // Non-matching tenant
        var resWrongTenant = DeterministicBucketRouter.Route(
            targetedExp, "other_tenant", "SYM-TARGET-1234", "mach-01", matchingContext);
        resWrongTenant.IsInExperiment.Should().BeFalse();

        // Non-matching prefix
        var resWrongPrefix = DeterministicBucketRouter.Route(
            targetedExp, "ten_target", "SYM-OTHER-1234", "mach-01", matchingContext);
        resWrongPrefix.IsInExperiment.Should().BeFalse();

        // Non-matching OS
        var wrongOsContext = new ExperimentClientContext { SdkLanguage = "csharp", OsPlatform = "windows" };
        var resWrongOs = DeterministicBucketRouter.Route(
            targetedExp, "ten_target", "SYM-TARGET-1234", "mach-01", wrongOsContext);
        resWrongOs.IsInExperiment.Should().BeFalse();
    }

    [Fact]
    public void Route_WhenCompletedWithPromotedVariant_AlwaysReturnsPromotedVariant()
    {
        var exp = new Experiment
        {
            Id = "exp_promoted",
            Name = "Completed Exp",
            Status = ExperimentStatus.Completed,
            PromotedVariantId = "v_winner",
            Variants =
            [
                new ExperimentVariant { VariantId = "ctrl", Name = "Control", Weight = 50, IsControl = true },
                new ExperimentVariant
                {
                    VariantId = "v_winner",
                    Name = "Winner",
                    Weight = 50,
                    Overrides = new ExperimentOverrides { LeaseTtlSeconds = 42 }
                }
            ]
        };

        for (int i = 0; i < 50; i++)
        {
            var res = DeterministicBucketRouter.Route(exp, null, $"LIC-{i}", $"M-{i}");
            res.IsInExperiment.Should().BeTrue();
            res.VariantId.Should().Be("v_winner");
            res.Overrides.LeaseTtlSeconds.Should().Be(42);
        }
    }
}
