namespace RevitEtabsValidator.Core.Geometry;
public static class AngleMath
{
    public static double Normalize(double angleDegrees, double period = 360.0)
    {
        var result = angleDegrees % period;
        if (result < 0) result += period;
        return result;
    }
    public static double CircularDeltaDegrees(double a, double b, double period = 360.0)
    {
        var d = System.Math.Abs(Normalize(a, period) - Normalize(b, period));
        return System.Math.Min(d, period - d);
    }
    public static double PlanRotationDegrees(Point3D start, Point3D end)
        => Normalize(System.Math.Atan2(end.Y-start.Y, end.X-start.X) * 180.0 / System.Math.PI, 180.0);
}
