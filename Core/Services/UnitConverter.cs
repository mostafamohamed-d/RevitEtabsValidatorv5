namespace RevitEtabsValidator.Core.Services;
public static class UnitConverter
{
    public const double FeetToMmFactor = 304.8;
    public static double FeetToMm(double value) => value * FeetToMmFactor;
    public static double MmToFeet(double value) => value / FeetToMmFactor;
}
