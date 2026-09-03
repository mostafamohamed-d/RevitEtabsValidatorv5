using RevitEtabsValidator.Core.Geometry;
namespace RevitEtabsValidator.Core.Models;

public abstract class ElementBase
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string SectionName { get; set; } = "";
    public string LevelName { get; set; } = "";
    public SourceApplication Source { get; set; }
    public string MaterialName { get; set; } = "";
    public Point3D StartPoint { get; set; }
    public Point3D EndPoint { get; set; }
    public Point3D CenterPoint => new((StartPoint.X + EndPoint.X) / 2.0, (StartPoint.Y + EndPoint.Y) / 2.0, (StartPoint.Z + EndPoint.Z) / 2.0);
    public double LengthMm => StartPoint.DistanceTo(EndPoint);
    public double PlanLengthMm => StartPoint.PlanDistanceTo(EndPoint);
    public double RotationDegrees => Geometry.AngleMath.PlanRotationDegrees(StartPoint, EndPoint);
}
