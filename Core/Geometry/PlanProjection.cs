using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitEtabsValidator.Core.Geometry;

/// <summary>
/// Maps model plan coordinates (millimetres) into the normalized canvas space the
/// floor-plan view draws in.
///
/// Why normalize at all: the plan view draws each member with a glyph sized in
/// SCREEN PIXELS (column radius 6, beam stroke 3, label font 10) and then scales
/// the whole canvas to fit the viewport. If the canvas coordinate system were raw
/// millimetres, those numbers would mean 6 mm / 3 mm / 10 mm *in the building*:
/// fitting a real ~120 m floor into a ~1200 px viewport gives a fit scale near
/// 0.01, so the column circles render at 0.06 px and the beam lines at 0.03 px -
/// drawn correctly, and invisible at every zoom level. Normalizing the plan into a
/// fixed <see cref="CanvasExtent"/> keeps the fit scale near 1.0 regardless of how
/// large the real building is, so pixel sizes stay pixel sizes.
///
/// This lives in Core (no Revit/ETABS/WPF dependency) so the arithmetic that
/// decides whether members are visible at all can be regression-tested directly.
/// </summary>
public sealed class PlanProjection
{
    /// <summary>Canvas units spanned by the longer of the plan's two dimensions.</summary>
    public const double CanvasExtent = 1000.0;

    /// <summary>Canvas-unit padding kept around the drawn extent on every side.</summary>
    public const double Margin = 20.0;

    private PlanProjection(double minX, double maxY, double worldWidthMm, double worldHeightMm, double scale)
    {
        MinX = minX;
        MaxY = maxY;
        WorldWidthMm = worldWidthMm;
        WorldHeightMm = worldHeightMm;
        Scale = scale;
    }

    public double MinX { get; }
    public double MaxY { get; }

    /// <summary>Real plan extent in millimetres.</summary>
    public double WorldWidthMm { get; }
    public double WorldHeightMm { get; }

    /// <summary>Canvas units per millimetre.</summary>
    public double Scale { get; }

    public double CanvasWidth => WorldWidthMm * Scale + 2.0 * Margin;
    public double CanvasHeight => WorldHeightMm * Scale + 2.0 * Margin;

    /// <summary>
    /// A single NaN/Infinity coordinate from a failed read would propagate through
    /// Min/Max into the canvas size and leave the whole plan un-renderable, so such
    /// points are excluded from the bounds calculation.
    /// </summary>
    public static bool IsFinite(Point3D p)
        => !double.IsNaN(p.X) && !double.IsInfinity(p.X) &&
           !double.IsNaN(p.Y) && !double.IsInfinity(p.Y);

    /// <summary>
    /// Builds a projection covering every finite point supplied, or null when there
    /// is nothing finite to draw.
    /// </summary>
    public static PlanProjection? Create(IEnumerable<Point3D> points)
    {
        var finite = points.Where(IsFinite).ToList();
        if (finite.Count == 0)
            return null;

        var minX = finite.Min(p => p.X);
        var maxX = finite.Max(p => p.X);
        var minY = finite.Min(p => p.Y);
        var maxY = finite.Max(p => p.Y);

        // A single-point (or single-column-line) floor has zero extent; the floor of
        // 1 mm keeps the scale finite instead of dividing by zero.
        var worldWidth = Math.Max(1.0, maxX - minX);
        var worldHeight = Math.Max(1.0, maxY - minY);
        var scale = CanvasExtent / Math.Max(worldWidth, worldHeight);

        return new PlanProjection(minX, maxY, worldWidth, worldHeight, scale);
    }

    /// <summary>
    /// Model point -> canvas point. Y is flipped because model Y increases north
    /// while canvas Y increases downward.
    /// </summary>
    public (double X, double Y) Map(Point3D p)
        => ((p.X - MinX) * Scale + Margin, (MaxY - p.Y) * Scale + Margin);

    /// <summary>
    /// Plan distance between the centroids of two sets of members, in millimetres,
    /// or null when either side is empty.
    ///
    /// When the Revit and ETABS members on the same floor have centroids metres
    /// apart, no per-member tolerance can ever match them - the two models are not
    /// in a common plan coordinate system (Revit Internal Origin vs. ETABS Global,
    /// per the project's coordinate contract). That surfaces as "everything Missing,
    /// nothing Matched" no matter how far the tolerances are opened up, so the plan
    /// view reports it explicitly rather than leaving it to be inferred.
    /// </summary>
    public static (double OffsetMm, double DeltaXMm, double DeltaYMm)? CentroidOffset(
        IEnumerable<Point3D> revitPoints,
        IEnumerable<Point3D> etabsPoints)
    {
        var revit = revitPoints.Where(IsFinite).ToList();
        var etabs = etabsPoints.Where(IsFinite).ToList();
        if (revit.Count == 0 || etabs.Count == 0)
            return null;

        var dx = revit.Average(p => p.X) - etabs.Average(p => p.X);
        var dy = revit.Average(p => p.Y) - etabs.Average(p => p.Y);
        return (Math.Sqrt(dx * dx + dy * dy), dx, dy);
    }
}
