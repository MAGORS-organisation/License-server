using FluentAssertions;
using Xunit;

namespace Symbolon.Crypto.Tests;

public class PqcProfileTests
{
    [Theory]
    [InlineData(Alg.Es256, PqcProfiles.HybridV1, true)]
    [InlineData(Alg.MlDsa65, PqcProfiles.HybridV1, true)]
    [InlineData(Alg.MlKem768, PqcProfiles.HybridV1, true)]
    [InlineData(Alg.Es256, PqcProfiles.PqcStrict, false)] // Classical rejected in strict PQC
    [InlineData(Alg.MlDsa44, PqcProfiles.PqcStrict, true)]
    [InlineData(Alg.MlDsa65, PqcProfiles.PqcStrict, true)]
    [InlineData(Alg.MlDsa87, PqcProfiles.PqcStrict, true)]
    [InlineData(Alg.MlKem768, PqcProfiles.PqcStrict, true)]
    [InlineData(Alg.MlKem1024, PqcProfiles.PqcStrict, true)]
    public void IsAlgorithmPermitted_EnforcesProfileRules(string alg, string profile, bool expected)
    {
        bool actual = PqcProfileValidator.IsAlgorithmPermitted(alg, profile);
        actual.Should().Be(expected);
    }

    [Theory]
    [InlineData(Alg.Es256, false)]
    [InlineData(Alg.MlDsa65, true)]
    [InlineData(Alg.MlDsa87, true)]
    [InlineData(Alg.MlKem768, true)]
    [InlineData(Alg.MlKem1024, true)]
    [InlineData(Alg.SlhDsaSha2128s, true)]
    public void IsCnsa2Compliant_ValidatesAgainstNsaStandard(string alg, bool expected)
    {
        bool actual = PqcProfileValidator.IsCnsa2Compliant(alg);
        actual.Should().Be(expected);
    }
}
