namespace RevitEtabsValidator.Core.Models;
public sealed class BeamElement : ElementBase
{
    public double Width { get; set; }
    public double Depth { get; set; }
    public double Rotation => Geometry.AngleMath.PlanRotationDegrees(StartPoint, EndPoint);
}
