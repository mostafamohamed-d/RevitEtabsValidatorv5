using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
namespace RevitEtabsValidator.Revit.Services;
public static class SelectionService
{
    public static void Select(UIDocument uidoc,string id){if(long.TryParse(id,out var v)) uidoc.Selection.SetElementIds(new[]{new ElementId(v)}.ToList());}
}
