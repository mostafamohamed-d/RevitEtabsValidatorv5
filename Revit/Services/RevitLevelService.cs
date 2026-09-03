using Autodesk.Revit.DB;
namespace RevitEtabsValidator.Revit.Services;
public static class RevitLevelService
{
    public static Level? GetLevel(Document doc, Element e)
    {
        foreach(var bip in new[]{BuiltInParameter.FAMILY_BASE_LEVEL_PARAM, BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM, BuiltInParameter.LEVEL_PARAM})
        {
            try { var p=e.get_Parameter(bip); if(p!=null && p.StorageType==StorageType.ElementId){var l=doc.GetElement(p.AsElementId()) as Level; if(l!=null)return l;} } catch{}
        }
        return null;
    }
    public static Level? Nearest(Document doc,double zFt)
        => new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(x=>Math.Abs(x.Elevation-zFt)).FirstOrDefault();
}
