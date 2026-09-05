using Autodesk.Revit.DB;
using RevitEtabsValidator.Core.Comparison;
using RevitEtabsValidator.Core.Geometry;
using RevitEtabsValidator.Core.Models;
using RevitEtabsValidator.Revit.Services;

namespace RevitEtabsValidator.Revit;

/// <summary>
/// Reads structural columns and framing using Revit's native model XYZ values.
/// Revit API XYZ is the project's internal coordinate system; this validator
/// deliberately keeps those coordinates anchored to the Revit Internal Origin.
/// This matches the project's DXF workflow where Coordinate Base = Internal Origin.
/// No Project Base Point, Survey Point, shared-coordinate transform, or arbitrary
/// XY translation is applied.
/// </summary>
public sealed class RevitElementReader
{
    private readonly Document _doc;

    public RevitElementReader(Document doc) => _doc = doc;

    public List<ColumnElement> ReadColumns()
    {
        var list = new List<ColumnElement>();

        foreach (var e in new FilteredElementCollector(_doc)
                     .OfCategory(BuiltInCategory.OST_StructuralColumns)
                     .WhereElementIsNotElementType()
                     .OfType<FamilyInstance>())
        {
            var level = RevitLevelService.GetLevel(_doc, e);
            var topLevel = RevitLevelService.GetTopLevel(_doc, e);
            Point3D a, b;
            double rot = 0;

            if (e.Location is LocationCurve lc)
            {
                a = ToPoint(lc.Curve.GetEndPoint(0));
                b = ToPoint(lc.Curve.GetEndPoint(1));
            }
            else if (e.Location is LocationPoint lp)
            {
                var z0 = level?.Elevation ?? lp.Point.Z;
                var z1 = TryTopElevation(e, z0);
                a = new Point3D(RevitUnit.Mm(lp.Point.X), RevitUnit.Mm(lp.Point.Y), RevitUnit.Mm(z0));
                b = new Point3D(a.X, a.Y, RevitUnit.Mm(z1));
                rot = lp.Rotation * 180 / Math.PI;
            }
            else
            {
                continue;
            }

            var sec = SectionDimensionService.Get(_doc, e);

            // SectionDimensionService returns the Revit b-like value first and
            // h-like value second. The common Width/Depth fields are kept in
            // Revit-native semantic order here; ModelComparer applies the
            // project-specific mapping b -> ETABS Depth and h -> ETABS Width.
            list.Add(new ColumnElement
            {
                Id = e.Id.Value.ToString(),
                Name = e.Name,
                SectionName = e.Symbol?.Name ?? "",
                // For columns, use the TOP LEVEL as the displayed/coordination level.
                // Fall back to the base/reference level only when no top-level
                // parameter is available (for example, an unusual column family).
                LevelName = topLevel?.Name
                            ?? level?.Name
                            ?? RevitLevelService.Nearest(_doc, RevitUnit.MmToFt((a.Z + b.Z) / 2))?.Name
                            ?? "",
                Source = SourceApplication.Revit,
                CoordinateBasis = CoordinateReference.RevitInternalOrigin,
                StartPoint = a,
                EndPoint = b,
                BaseElevation = Math.Min(a.Z, b.Z),
                TopElevation = Math.Max(a.Z, b.Z),
                Width = sec.widthMm,
                Depth = sec.depthMm,
                Rotation = rot,
                BoundingBox = new BoundingBox3D(
                    new Point3D(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z)),
                    new Point3D(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z)))
            });
        }

        return list;
    }

    public List<BeamElement> ReadBeams()
    {
        var list = new List<BeamElement>();

        foreach (var e in new FilteredElementCollector(_doc)
                     .OfCategory(BuiltInCategory.OST_StructuralFraming)
                     .WhereElementIsNotElementType()
                     .OfType<FamilyInstance>())
        {
            if (e.Location is not LocationCurve lc)
                continue;

            var a = ToPoint(lc.Curve.GetEndPoint(0));
            var b = ToPoint(lc.Curve.GetEndPoint(1));
            var level = RevitLevelService.GetLevel(_doc, e)
                        ?? RevitLevelService.Nearest(_doc, RevitUnit.MmToFt((a.Z + b.Z) / 2));
            var sec = SectionDimensionService.Get(_doc, e);

            list.Add(new BeamElement
            {
                Id = e.Id.Value.ToString(),
                Name = e.Name,
                SectionName = e.Symbol?.Name ?? "",
                LevelName = level?.Name ?? "",
                Source = SourceApplication.Revit,
                CoordinateBasis = CoordinateReference.RevitInternalOrigin,
                StartPoint = a,
                EndPoint = b,
                Width = sec.widthMm,
                Depth = sec.depthMm
            });
        }

        return list;
    }

    private Point3D ToPoint(XYZ p) => new(RevitUnit.Mm(p.X), RevitUnit.Mm(p.Y), RevitUnit.Mm(p.Z));

    private static double TryTopElevation(FamilyInstance e, double baseZ)
    {
        var p = e.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM);
        var doc = e.Document;

        if (p != null && p.StorageType == StorageType.ElementId)
        {
            var l = doc.GetElement(p.AsElementId()) as Level;
            if (l != null)
            {
                var off = e.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM)?.AsDouble() ?? 0;
                return l.Elevation + off;
            }
        }

        var bb = e.get_BoundingBox(null);
        return bb?.Max.Z ?? baseZ;
    }
}
