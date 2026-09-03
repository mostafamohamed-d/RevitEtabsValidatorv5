namespace RevitEtabsValidator.Core.Validation;

public sealed class ValidationTolerance
{
    public double PositionToleranceMm { get; set; } = 25;
    public double ElevationToleranceMm { get; set; } = 25;
    public double DimensionToleranceMm { get; set; } = 5;
    public double AngleToleranceDegrees { get; set; } = 1;
    public double LengthToleranceMm { get; set; } = 25;

    // Matching-score gap below this value is treated as an ambiguous match.
    // Matching scores are normalized and therefore dimensionless.
    public double AmbiguousScoreGap { get; set; } = 0.25;
}
