namespace Achilles.Domain.Experiments;

public static class ExperimentStatisticalEngine
{
    private const double Z95 = 1.959963984540054; // 95% two-tailed critical value

    /// <summary>
    /// Computes the Error Function erf(x) using the Abramowitz and Stegun 7.1.26 polynomial approximation (max error 1.5e-7).
    /// </summary>
    public static double Erf(double x)
    {
        double sign = x < 0 ? -1.0 : 1.0;
        x = Math.Abs(x);

        const double a1 = 0.254829592;
        const double a2 = -0.284496736;
        const double a3 = 1.421413741;
        const double a4 = -1.453152027;
        const double a5 = 1.061405429;
        const double p = 0.3275911;

        double t = 1.0 / (1.0 + p * x);
        double y = 1.0 - (((((a5 * t + a4) * t) + a3) * t + a2) * t + a1) * t * Math.Exp(-x * x);

        return sign * y;
    }

    /// <summary>
    /// Cumulative distribution function for standard normal distribution N(0,1).
    /// </summary>
    public static double NormalCdf(double z)
    {
        return 0.5 * (1.0 + Erf(z / Math.Sqrt(2.0)));
    }

    /// <summary>
    /// Computes a Two-Proportion Z-Test comparing success rates between Control (A) and Treatment (B).
    /// </summary>
    public static (double ZScore, double PValue, double CiLower, double CiUpper) CalculateTwoProportionZTest(
        long successesA, long trialsA,
        long successesB, long trialsB)
    {
        if (trialsA <= 0 || trialsB <= 0)
        {
            return (0.0, 1.0, 0.0, 0.0);
        }

        double pA = (double)successesA / trialsA;
        double pB = (double)successesB / trialsB;
        double diff = pB - pA;

        // Pooled proportion for hypothesis test under H0: pA == pB
        double pooledP = (double)(successesA + successesB) / (trialsA + trialsB);
        double pooledVariance = pooledP * (1.0 - pooledP) * ((1.0 / trialsA) + (1.0 / trialsB));

        double zScore = 0.0;
        if (pooledVariance > 0)
        {
            zScore = diff / Math.Sqrt(pooledVariance);
        }

        // Two-tailed p-value
        double pValue = 2.0 * (1.0 - NormalCdf(Math.Abs(zScore)));
        pValue = Math.Clamp(pValue, 0.0, 1.0);

        // Standard error for unpooled confidence interval
        double seDiff = Math.Sqrt((pA * (1.0 - pA) / trialsA) + (pB * (1.0 - pB) / trialsB));
        double ciLower = diff - (Z95 * seDiff);
        double ciUpper = diff + (Z95 * seDiff);

        return (zScore, pValue, ciLower, ciUpper);
    }

    /// <summary>
    /// Computes Welch's t-test for difference in continuous metric (e.g. latency) between Control (A) and Treatment (B).
    /// </summary>
    public static (double TScore, double PValue) CalculateWelchTTest(
        double meanA, double stdDevA, long nA,
        double meanB, double stdDevB, long nB)
    {
        if (nA <= 1 || nB <= 1)
        {
            return (0.0, 1.0);
        }

        double varA = stdDevA * stdDevA;
        double varB = stdDevB * stdDevB;
        double denom = Math.Sqrt((varA / nA) + (varB / nB));

        if (denom <= 0)
        {
            return (0.0, 1.0);
        }

        double tScore = (meanB - meanA) / denom;
        // Asymptotically normal for large N
        double pValue = 2.0 * (1.0 - NormalCdf(Math.Abs(tScore)));
        pValue = Math.Clamp(pValue, 0.0, 1.0);

        return (tScore, pValue);
    }

    /// <summary>
    /// Computes Chi-Square (χ²) Goodness-of-Fit test comparing observed sample counts against expected weights.
    /// Returns ChiSquare statistic, degrees of freedom, and p-value.
    /// Under H0: the observed sample distribution does not deviate significantly from expected distribution.
    /// </summary>
    public static (double ChiSquare, int DegreesOfFreedom, double PValue) CalculateChiSquareGoodnessOfFit(
        IReadOnlyList<long> observed,
        IReadOnlyList<double> expectedWeights)
    {
        ArgumentNullException.ThrowIfNull(observed);
        ArgumentNullException.ThrowIfNull(expectedWeights);

        if (observed.Count < 2 || observed.Count != expectedWeights.Count)
        {
            throw new ArgumentException("Observed and expected collections must have identical length >= 2.");
        }

        long totalObserved = observed.Sum();
        if (totalObserved <= 0)
        {
            return (0.0, observed.Count - 1, 1.0);
        }

        double totalWeight = expectedWeights.Sum();
        if (totalWeight <= 0)
        {
            throw new ArgumentException("Sum of expected weights must be strictly positive.");
        }

        double chiSquare = 0.0;
        for (int i = 0; i < observed.Count; i++)
        {
            double expected = totalObserved * (expectedWeights[i] / totalWeight);
            if (expected > 0)
            {
                double diff = observed[i] - expected;
                chiSquare += (diff * diff) / expected;
            }
        }

        int df = observed.Count - 1;
        double pValue = CalculateChiSquarePValue(chiSquare, df);

        return (chiSquare, df, pValue);
    }

    /// <summary>
    /// Computes upper-tail p-value P(X >= chiSquare) for Chi-Square distribution with degrees of freedom df.
    /// </summary>
    public static double CalculateChiSquarePValue(double chiSquare, int df)
    {
        if (chiSquare <= 0 || df <= 0)
        {
            return 1.0;
        }

        if (df == 1)
        {
            // For df = 1, chiSquare ~ Z^2 where Z ~ N(0,1)
            double z = Math.Sqrt(chiSquare);
            double p = 2.0 * (1.0 - NormalCdf(z));
            return Math.Clamp(p, 0.0, 1.0);
        }

        if (df == 2)
        {
            // For df = 2, chi-square is exponential with scale 2
            double p = Math.Exp(-0.5 * chiSquare);
            return Math.Clamp(p, 0.0, 1.0);
        }

        // Wilson-Hilferty transformation for df >= 3
        double d = df;
        double term = Math.Pow(chiSquare / d, 1.0 / 3.0);
        double mean = 1.0 - (2.0 / (9.0 * d));
        double stdDev = Math.Sqrt(2.0 / (9.0 * d));

        if (stdDev <= 0)
        {
            return 1.0;
        }

        double zScore = (term - mean) / stdDev;
        double pValue = 1.0 - NormalCdf(zScore);
        return Math.Clamp(pValue, 0.0, 1.0);
    }

    /// <summary>
    /// Evaluates an experiment's metrics and generates a complete statistical report.
    /// </summary>
    public static ExperimentStatisticalReport GenerateReport(
        Experiment experiment,
        IReadOnlyList<VariantMetrics> metrics)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        ArgumentNullException.ThrowIfNull(metrics);

        var controlVariant = experiment.Variants.FirstOrDefault(v => v.IsControl) ?? (experiment.Variants.Count > 0 ? experiment.Variants[0] : null);
        var treatmentVariant = experiment.Variants.FirstOrDefault(v => !v.IsControl && v.VariantId != controlVariant?.VariantId);

        string controlId = controlVariant?.VariantId ?? "A";
        string treatmentId = treatmentVariant?.VariantId ?? (experiment.Variants.Count > 1 ? experiment.Variants[1].VariantId : "B");

        var mControl = metrics.FirstOrDefault(m => m.VariantId == controlId) ?? new VariantMetrics { VariantId = controlId };
        var mTreatment = metrics.FirstOrDefault(m => m.VariantId == treatmentId) ?? new VariantMetrics { VariantId = treatmentId };

        var (z, pVal, ciLow, ciHigh) = CalculateTwoProportionZTest(
            mControl.SuccessfulCheckouts, mControl.TotalRequests,
            mTreatment.SuccessfulCheckouts, mTreatment.TotalRequests);

        var (tScore, tPVal) = CalculateWelchTTest(
            mControl.AverageLatencyMs, mControl.LatencyStdDev, mControl.TotalRequests,
            mTreatment.AverageLatencyMs, mTreatment.LatencyStdDev, mTreatment.TotalRequests);

        bool isSignificant = pVal < 0.05 && (mControl.TotalRequests >= 30 && mTreatment.TotalRequests >= 30);
        double diff = mTreatment.SuccessRate - mControl.SuccessRate;
        double latencyDiff = mTreatment.AverageLatencyMs - mControl.AverageLatencyMs;

        string recommendation;
        if (mControl.TotalRequests < 30 || mTreatment.TotalRequests < 30)
        {
            recommendation = $"Nedostatok vzoriek (Control: {mControl.TotalRequests}, Treatment: {mTreatment.TotalRequests}). Minimum je 30 požiadaviek na variant.";
        }
        else if (mTreatment.ErrorRate > mControl.ErrorRate && (mTreatment.ErrorRate - mControl.ErrorRate) > 0.03)
        {
            recommendation = $"VAROVANIE: Variant '{treatmentId}' vykazuje zvýšenú chybovosť ({mTreatment.ErrorRate:P1} vs. {mControl.ErrorRate:P1}). Odporúča sa rollback.";
        }
        else if (isSignificant && diff > 0)
        {
            recommendation = $"ODPORÚČANIE: Variant '{treatmentId}' vykazuje štatisticky signifikantné zlepšenie úspešnosti (+{diff:P1}, p={pVal:F4}). Bezpečné nasadiť na 100%.";
        }
        else if (tPVal < 0.05 && latencyDiff < -5.0)
        {
            recommendation = $"ODPORÚČANIE: Variant '{treatmentId}' signifikantne znižuje latenciu ({latencyDiff:F1} ms, p={tPVal:F4}).";
        }
        else if (isSignificant && diff < 0)
        {
            recommendation = $"VAROVANIE: Variant '{treatmentId}' vykazuje signifikantný pokles úspešnosti ({diff:P1}, p={pVal:F4}).";
        }
        else
        {
            recommendation = $"Medzi variantmi zatiaľ nebol preukázaný štatisticky signifikantný rozdiel (p = {pVal:F4}). Pokračujte v zbere dát.";
        }

        return new ExperimentStatisticalReport
        {
            ExperimentId = experiment.Id,
            Status = experiment.Status,
            ControlVariantId = controlId,
            TreatmentVariantId = treatmentId,
            ControlMetrics = mControl,
            TreatmentMetrics = mTreatment,
            ZScore = z,
            PValue = pVal,
            DifferenceRate = diff,
            ConfidenceIntervalLower = ciLow,
            ConfidenceIntervalUpper = ciHigh,
            IsStatisticallySignificant = isSignificant,
            LatencyTScore = tScore,
            LatencyPValue = tPVal,
            LatencyDifferenceMs = latencyDiff,
            Recommendation = recommendation
        };
    }
}
