namespace RevitEtabsValidator.Core.Comparison;

/// <summary>
/// Coordinate reference required by the Revit ↔ ETABS validator.
/// Revit geometry is read from the Revit API's internal XYZ coordinate system.
/// ETABS global XYZ is compared directly against that coordinate system after
/// unit normalization to millimetres.
/// </summary>
public enum CoordinateBasis
{
    RevitInternalOrigin = 0
}
