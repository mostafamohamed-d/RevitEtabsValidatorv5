namespace RevitEtabsValidator.Core.Models;
public sealed class BeamElement : ElementBase
{
    public override double Width { get; set; }
    public override double Depth { get; set; }
    public double Rotation => Geometry.AngleMath.PlanRotationDegrees(StartPoint, EndPoint);
}
