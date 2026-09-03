using RevitEtabsValidator.Core.Geometry;
namespace RevitEtabsValidator.Core.Models;

public sealed class ColumnElement : ElementBase
{
    public Point3D BasePoint { get => StartPoint; set => StartPoint = value; }
    public Point3D TopPoint { get => EndPoint; set => EndPoint = value; }
    public double BaseElevation { get; set; }
    public double TopElevation { get; set; }
    public double Width { get; set; }
    public double Depth { get; set; }
    public double Rotation { get; set; }
    public BoundingBox3D BoundingBox { get; set; }
}
