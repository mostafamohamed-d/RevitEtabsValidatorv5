namespace RevitEtabsValidator.Core.Comparison;

/// <summary>
/// Coordinate references used by the Revit-to-ETABS coordination contract.
/// The Revit side is intentionally the Revit Internal Origin because the project
/// coordination DXF is exported from Revit using Coordinate Base = Internal Origin.
/// The ETABS side is its Global coordinate system. For this validator those two
/// references are required to represent the same physical XY origin and axes.
/// No automatic XY translation or shared-coordinate transform is applied.
/// </summary>
public enum CoordinateReference
{
    Unknown = 0,
    RevitInternalOrigin = 1,
    EtabsGlobal = 2
}

public static class CoordinateReferenceContract
{
    public const CoordinateReference Revit = CoordinateReference.RevitInternalOrigin;
    public const CoordinateReference Etabs = CoordinateReference.EtabsGlobal;

    public static string Description =>
        "Revit Internal Origin ↔ ETABS Global. XY origin/axes must be aligned by the project coordination setup; no automatic XY translation is applied.";
}
