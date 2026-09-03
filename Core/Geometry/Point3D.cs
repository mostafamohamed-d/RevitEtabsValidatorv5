namespace RevitEtabsValidator.Core.Geometry;

public readonly record struct Point3D(double X, double Y, double Z)
{
    public double DistanceTo(Point3D other) => System.Math.Sqrt(System.Math.Pow(X-other.X,2)+System.Math.Pow(Y-other.Y,2)+System.Math.Pow(Z-other.Z,2));
    public double PlanDistanceTo(Point3D other) => System.Math.Sqrt(System.Math.Pow(X-other.X,2)+System.Math.Pow(Y-other.Y,2));
    public override string ToString() => $"({X:F1}, {Y:F1}, {Z:F1}) mm";
}
