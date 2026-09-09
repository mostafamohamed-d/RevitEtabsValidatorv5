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

    // The identity gate (deciding whether a Revit/ETABS pair is a candidate for
    // the SAME physical element at all) is deliberately wider than the pass/fail
    // tolerances above. If it used the exact same tolerance, any element that
    // drifted even slightly past tolerance would never become a candidate pair,
    // so it would surface as two orphaned Missing-in-Revit/Missing-in-ETABS
    // entries instead of one linked PositionMismatch/RotationMismatch with an
    // actionable delta. This multiplier controls how far that identity search
    // window is widened relative to PositionToleranceMm/AngleToleranceDegrees.
    public double IdentityGateMultiplier { get; set; } = 4.0;

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
