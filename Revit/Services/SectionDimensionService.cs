using Autodesk.Revit.DB;
namespace RevitEtabsValidator.Revit.Services;
public static class SectionDimensionService
{
    private static readonly string[] WidthNames={"b","Width","width","B","Section Width","b1"};
    private static readonly string[] DepthNames={"h","Depth","depth","H","Section Depth","h1"};
    public static (double widthMm,double depthMm) Get(Document doc,FamilyInstance inst)
    {
        var type=inst.Symbol; var w=Find(type,WidthNames); var d=Find(type,DepthNames);
        if(w>0 && d>0) return (RevitUnit.Mm(w),RevitUnit.Mm(d));
        var bb=inst.get_BoundingBox(null);
        if(bb!=null)
        {
            var t=inst.GetTransform(); var sx=bb.Max.X-bb.Min.X; var sy=bb.Max.Y-bb.Min.Y;
            // Bounding box is already in model coordinates for instance.get_BoundingBox; use horizontal extents.
            return (RevitUnit.Mm(Math.Abs(sx)),RevitUnit.Mm(Math.Abs(sy)));
        }
        return (0,0);
    }
    private static double Find(Element type,string[] names)
    {
        foreach(var n in names){var p=type.LookupParameter(n); if(p!=null && p.StorageType==StorageType.Double && p.AsDouble()>0)return p.AsDouble();}
        return 0;
    }
}
