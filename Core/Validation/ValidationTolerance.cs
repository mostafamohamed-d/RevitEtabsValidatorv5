using RevitEtabsValidator.Core.Comparison;

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

    // A beam is considered the same plan line only when this fraction of the
    // shorter projected segment overlaps the other segment.
    public double BeamMinimumOverlapRatio { get; set; } = 0.80;

    // Project coordinate rule: compare Revit internal coordinates directly with
    // ETABS global coordinates after unit normalization. Do not silently switch
    // to Revit shared coordinates, project base point coordinates, or an inferred
    // plan translation.
    public CoordinateBasis CoordinateBasis { get; set; } = CoordinateBasis.RevitInternalOrigin;

    // Optional, explicit systematic Z correction. These values are not datum
    // normalization and default to zero because ETABS Base is assumed to be the
    // same structural datum as the Revit model base.
    public double BeamZOffsetMm { get; set; } = 0;
    public double ColumnZOffsetMm { get; set; } = 0;
}
