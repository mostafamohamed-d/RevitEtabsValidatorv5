using RevitEtabsValidator.Core.Geometry;
using RevitEtabsValidator.Core.Models;

namespace RevitEtabsValidator.Core.Comparison;

/// <summary>
/// Places Revit and ETABS structural members in a common vertical datum before
/// correspondence is evaluated. Each model's lowest structural member Z is used
/// as its own base reference, so both structural bases become Z=0 mm.
/// XY coordinates are intentionally left unchanged: an arbitrary plan translation
/// must not be introduced without an explicit project survey/origin rule.
/// </summary>
public static class CoordinateNormalization
{
    public static IReadOnlyList<ColumnElement> NormalizeColumns(
        IReadOnlyList<ColumnElement> source,
        out double baseZMm)
    {
        baseZMm = FindBaseZ(source.SelectMany(x => new[] { x.BaseElevation, x.TopElevation }));

        return source.Select(x => new ColumnElement
        {
            Id = x.Id,
            Name = x.Name,
            SectionName = x.SectionName,
            LevelName = x.LevelName,
            Source = x.Source,
            MaterialName = x.MaterialName,
            StartPoint = new Point3D(x.StartPoint.X, x.StartPoint.Y, x.StartPoint.Z - baseZMm),
            EndPoint = new Point3D(x.EndPoint.X, x.EndPoint.Y, x.EndPoint.Z - baseZMm),
            BaseElevation = x.BaseElevation - baseZMm,
            TopElevation = x.TopElevation - baseZMm,
            Width = x.Width,
            Depth = x.Depth,
            Rotation = x.Rotation,
            BoundingBox = new BoundingBox3D(
                new Point3D(x.BoundingBox.Min.X, x.BoundingBox.Min.Y, x.BoundingBox.Min.Z - baseZMm),
                new Point3D(x.BoundingBox.Max.X, x.BoundingBox.Max.Y, x.BoundingBox.Max.Z - baseZMm))
        }).ToList();
    }

    public static IReadOnlyList<BeamElement> NormalizeBeams(
        IReadOnlyList<BeamElement> source,
        out double baseZMm)
    {
        baseZMm = FindBaseZ(source.SelectMany(x => new[] { x.StartPoint.Z, x.EndPoint.Z }));

        return source.Select(x => new BeamElement
        {
            Id = x.Id,
            Name = x.Name,
            SectionName = x.SectionName,
            LevelName = x.LevelName,
            Source = x.Source,
            MaterialName = x.MaterialName,
            StartPoint = new Point3D(x.StartPoint.X, x.StartPoint.Y, x.StartPoint.Z - baseZMm),
            EndPoint = new Point3D(x.EndPoint.X, x.EndPoint.Y, x.EndPoint.Z - baseZMm),
            Width = x.Width,
            Depth = x.Depth
        }).ToList();
    }

    private static double FindBaseZ(IEnumerable<double> values)
    {
        var finite = values.Where(double.IsFinite).ToList();
        return finite.Count == 0 ? 0.0 : finite.Min();
    }
}
