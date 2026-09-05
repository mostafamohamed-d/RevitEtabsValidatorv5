using Autodesk.Revit.DB;

namespace RevitEtabsValidator.Revit.Services;

public static class RevitLevelService
{
    public static Level? GetLevel(Document doc, Element e)
    {
        foreach (var bip in new[]
        {
            BuiltInParameter.FAMILY_BASE_LEVEL_PARAM,
            BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM,
            BuiltInParameter.LEVEL_PARAM
        })
        {
            try
            {
                var p = e.get_Parameter(bip);
                if (p != null && p.StorageType == StorageType.ElementId)
                {
                    var l = doc.GetElement(p.AsElementId()) as Level;
                    if (l != null) return l;
                }
            }
            catch { }
        }
        return null;
    }

    public static Level? GetTopLevel(Document doc, Element e)
    {
        try
        {
            var p = e.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM);
            if (p != null && p.StorageType == StorageType.ElementId)
                return doc.GetElement(p.AsElementId()) as Level;
        }
        catch { }
        return null;
    }

    public static IReadOnlyList<(string Name, double ElevationMm)> GetAll(Document doc)
        => new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .OrderBy(x => x.Elevation)
            .Select(x => (x.Name, RevitUnit.Mm(x.Elevation)))
            .ToList();

    public static Level? Nearest(Document doc, double zFt)
        => new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(x => Math.Abs(x.Elevation - zFt))
            .FirstOrDefault();
}
