using Autodesk.Revit.DB;
namespace RevitEtabsValidator.Revit.Services;
public static class RevitUnit
{
    public static double Mm(double feet) => UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);
    public static double MmToFt(double mm) => UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
}
