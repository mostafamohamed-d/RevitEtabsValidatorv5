using RevitEtabsValidator.Core.Geometry;
using RevitEtabsValidator.Core.Models;
using RevitEtabsValidator.Core.Validation;

namespace RevitEtabsValidator.Core.Comparison;

/// <summary>
/// Geometry-first one-to-one correspondence engine.
///
/// Columns are point-like objects in plan. Their XY location establishes identity;
/// base/top Z, section and rotation are validated after the counterpart is found.
///
/// Beams are finite plan line segments. Their plan-line direction, perpendicular
/// offset and segment overlap establish identity. Endpoint extension/retraction,
/// elevation, length and section are validated after the counterpart is found.
///
/// Coordinates are expected in millimetres. Revit uses its Internal Origin basis and
/// ETABS uses its Global basis, with the two models intentionally compared in the
/// same project XY coordinate system. Revit is converted from internal feet to mm
/// by the reader and ETABS is read after SetPresentUnits(kN-mm-C).
/// </summary>
public sealed class ModelComparer
{
    public ValidationReport CompareColumns(
        IReadOnlyList<ColumnElement> revit,
        IReadOnlyList<ColumnElement> etabs,
        ValidationTolerance tol)
    {
        var report = new ValidationReport();
        var remaining = new HashSet<string>(etabs.Select(x => x.Id), StringComparer.OrdinalIgnoreCase);

        var pending = revit.Select(r =>
        {
            var candidates = etabs
                .Where(e => remaining.Contains(e.Id) && ColumnIdentityGate(r, e, tol))
                .Select(e => (e, Score: ColumnScore(r, e, tol)))
                .OrderBy(x => x.Score)
                .ToList();
            return (r, candidates);
        })
        .OrderBy(x => x.candidates.Count)
        .ThenBy(x => x.r.LevelName, StringComparer.OrdinalIgnoreCase)
        .ThenBy(x => x.r.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();

        foreach (var item in pending)
        {
            var candidates = item.candidates.Where(x => remaining.Contains(x.e.Id)).ToList();
            if (candidates.Count == 0)
            {
                report.Results.Add(MissingRevit(item.r, "Column"));
                continue;
            }

            var best = candidates[0];
            if (candidates.Count > 1 &&
                Math.Abs(candidates[1].Score - best.Score) < Math.Max(0, tol.AmbiguousScoreGap))
            {
                report.Results.Add(new ValidationResult
                {
                    RevitElementId = item.r.Id,
                    RevitName = item.r.Name,
                    EtabsElementId = best.e.Id,
                    EtabsName = best.e.Name,
                    ElementType = "Column",
                    StoryOrLevel = item.r.LevelName,
                    Status = ValidationStatus.AmbiguousMatch,
                    Severity = Severity.Error,
                    Confidence = 0,
                    Message = "Ambiguous column correspondence: two ETABS point candidates have nearly identical plan/location scores."
                });
                continue;
            }

            report.Results.Add(ColumnResult(item.r, best.e, tol));
            remaining.Remove(best.e.Id);
        }

        foreach (var e in etabs.Where(x => remaining.Contains(x.Id)))
            report.Results.Add(MissingEtabs(e, "Column"));

        return report;
    }

    public ValidationReport CompareBeams(
        IReadOnlyList<BeamElement> revit,
        IReadOnlyList<BeamElement> etabs,
        ValidationTolerance tol)
    {
        var report = new ValidationReport();
        var remaining = new HashSet<string>(etabs.Select(x => x.Id), StringComparer.OrdinalIgnoreCase);

        var pending = revit.Select(r =>
        {
            var candidates = etabs
                .Where(e => remaining.Contains(e.Id) && BeamIdentityGate(r, e, tol))
                .Select(e => (e, Score: BeamScore(r, e, tol)))
                .OrderBy(x => x.Score)
                .ToList();
            return (r, candidates);
        })
        .OrderBy(x => x.candidates.Count)
        .ThenBy(x => x.r.LevelName, StringComparer.OrdinalIgnoreCase)
        .ThenBy(x => x.r.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();

        foreach (var item in pending)
        {
            var candidates = item.candidates.Where(x => remaining.Contains(x.e.Id)).ToList();
            if (candidates.Count == 0)
            {
                report.Results.Add(MissingRevit(item.r, "Beam"));
                continue;
            }

            var best = candidates[0];
            if (candidates.Count > 1 &&
                Math.Abs(candidates[1].Score - best.Score) < Math.Max(0, tol.AmbiguousScoreGap))
            {
                report.Results.Add(new ValidationResult
                {
                    RevitElementId = item.r.Id,
                    RevitName = item.r.Name,
                    EtabsElementId = best.e.Id,
                    EtabsName = best.e.Name,
                    ElementType = "Beam",
                    StoryOrLevel = item.r.LevelName,
                    Status = ValidationStatus.AmbiguousMatch,
                    Severity = Severity.Error,
                    Confidence = 0,
                    Message = "Ambiguous beam correspondence: two ETABS line candidates have nearly identical plan-line scores."
                });
                continue;
            }

            report.Results.Add(BeamResult(item.r, best.e, tol));
            remaining.Remove(best.e.Id);
        }

        foreach (var e in etabs.Where(x => remaining.Contains(x.Id)))
            report.Results.Add(MissingEtabs(e, "Beam"));

        return report;
    }

    private static bool ColumnIdentityGate(ColumnElement r, ColumnElement e, ValidationTolerance t)
        => r.CenterPoint.PlanDistanceTo(e.CenterPoint) <= t.PositionToleranceMm;

    /// <summary>
    /// Beam identity is the plan line, not the exact end coordinates.
    /// Direction must agree, the two finite segments must overlap materially,
    /// and the symmetric endpoint-to-opposite-line offset must be within the
    /// position tolerance. Beam Z and exact length are deliberately validated later.
    /// </summary>
    private static bool BeamIdentityGate(BeamElement r, BeamElement e, ValidationTolerance t)
    {
        var g = Geometry(r, e, t);
        return g.LineOffset <= t.PositionToleranceMm &&
               g.OverlapRatio >= Math.Clamp(t.BeamMinimumOverlapRatio, 0.0, 1.0) &&
               g.AngleDelta <= t.AngleToleranceDegrees;
    }

    private static double ColumnScore(ColumnElement r, ColumnElement e, ValidationTolerance t)
    {
        var plan = r.CenterPoint.PlanDistanceTo(e.CenterPoint) / Safe(t.PositionToleranceMm);
        var baseDelta = Math.Abs((r.BaseElevation + t.ColumnZOffsetMm) - e.BaseElevation);
        var topDelta = Math.Abs((r.TopElevation + t.ColumnZOffsetMm) - e.TopElevation);
        var z = (baseDelta + topDelta) / (2.0 * Safe(t.ElevationToleranceMm));
        var section = SectionPenalty(r.Width, r.Depth, e.Width, e.Depth, t.DimensionToleranceMm);
        var rot = AngleMath.CircularDeltaDegrees(r.Rotation, e.Rotation, 180) / Safe(t.AngleToleranceDegrees);

        return 8.0 * plan + 1.5 * z + 1.0 * section + 0.25 * rot;
    }

    private static double BeamScore(BeamElement r, BeamElement e, ValidationTolerance t)
    {
        var g = Geometry(r, e, t);
        var line = g.LineOffset / Safe(t.PositionToleranceMm);
        var midpoint = g.MidpointDeviation / Safe(t.PositionToleranceMm);
        var overlap = (1.0 - g.OverlapRatio);
        var z = g.EndpointElevationDeviation / Safe(t.ElevationToleranceMm);
        var len = g.LengthDelta / Safe(t.LengthToleranceMm);
        var angle = g.AngleDelta / Safe(t.AngleToleranceDegrees);
        var section = SectionPenalty(r.Width, r.Depth, e.Width, e.Depth, t.DimensionToleranceMm);

        // Plan-line geometry is primary. Overlap and midpoint distinguish nearby
        // parallel members. Elevation/length/section are secondary evidence.
        return 8.0 * line + 2.0 * midpoint + 3.0 * overlap +
               1.5 * z + 1.5 * len + 0.75 * angle + 0.5 * section;
    }

    private readonly record struct BeamGeometryResult(
        double LineOffset,
        double MidpointDeviation,
        double OverlapRatio,
        double EndpointPlanDeviation,
        double EndpointElevationDeviation,
        double LengthDelta,
        double AngleDelta);

    private static BeamGeometryResult Geometry(BeamElement r, BeamElement e, ValidationTolerance t)
    {
        var r0 = r.StartPoint;
        var r1 = r.EndPoint;
        var e0 = e.StartPoint;
        var e1 = e.EndPoint;

        var rdx = r1.X - r0.X;
        var rdy = r1.Y - r0.Y;
        var edx = e1.X - e0.X;
        var edy = e1.Y - e0.Y;
        var rLen = Math.Sqrt(rdx * rdx + rdy * rdy);
        var eLen = Math.Sqrt(edx * edx + edy * edy);

        if (rLen <= 1e-9 || eLen <= 1e-9)
        {
            var fallback = Math.Max(
                Math.Max(r0.PlanDistanceTo(e0), r0.PlanDistanceTo(e1)),
                Math.Max(r1.PlanDistanceTo(e0), r1.PlanDistanceTo(e1)));
            return new BeamGeometryResult(
                fallback,
                r.CenterPoint.PlanDistanceTo(e.CenterPoint),
                0.0,
                fallback,
                Math.Max(Math.Abs(r0.Z + t.BeamZOffsetMm - e0.Z), Math.Abs(r1.Z + t.BeamZOffsetMm - e1.Z)),
                Math.Abs(r.LengthMm - e.LengthMm),
                180.0);
        }

        var urx = rdx / rLen;
        var ury = rdy / rLen;
        var uex = edx / eLen;
        var uey = edy / eLen;

        // Symmetric line-to-line offset: every endpoint of one segment is
        // measured to the infinite centerline of the other segment.
        var lineOffset = Math.Max(
            Math.Max(PointToLineDistance(e0, r0, urx, ury), PointToLineDistance(e1, r0, urx, ury)),
            Math.Max(PointToLineDistance(r0, e0, uex, uey), PointToLineDistance(r1, e0, uex, uey)));

        var sameEndA = r0.PlanDistanceTo(e0);
        var sameEndB = r1.PlanDistanceTo(e1);
        var reverseEndA = r0.PlanDistanceTo(e1);
        var reverseEndB = r1.PlanDistanceTo(e0);
        var endpointPlanDeviation = Math.Min(
            Math.Max(sameEndA, sameEndB),
            Math.Max(reverseEndA, reverseEndB));

        var midX = (r0.X + r1.X) / 2.0;
        var midY = (r0.Y + r1.Y) / 2.0;
        var eMidX = (e0.X + e1.X) / 2.0;
        var eMidY = (e0.Y + e1.Y) / 2.0;
        var midpointDeviation = Math.Sqrt(Math.Pow(midX - eMidX, 2) + Math.Pow(midY - eMidY, 2));

        // Project ETABS onto the Revit beam axis and calculate finite-segment overlap.
        var ep0 = (e0.X - r0.X) * urx + (e0.Y - r0.Y) * ury;
        var ep1 = (e1.X - r0.X) * urx + (e1.Y - r0.Y) * ury;
        var eMin = Math.Min(ep0, ep1);
        var eMax = Math.Max(ep0, ep1);
        var overlap = Math.Max(0.0, Math.Min(rLen, eMax) - Math.Max(0.0, eMin));
        var overlapRatio = overlap / Math.Max(1e-9, Math.Min(rLen, eLen));
        overlapRatio = Math.Clamp(overlapRatio, 0.0, 1.0);

        var angleDelta = AngleMath.CircularDeltaDegrees(r.Rotation, e.Rotation, 180);

        // Find the endpoint pairing that best represents the same orientation,
        // then evaluate the corresponding Z deviations using that pairing.
        var samePlanMax = Math.Max(sameEndA, sameEndB);
        var reversePlanMax = Math.Max(reverseEndA, reverseEndB);
        var sameElevMax = Math.Max(
            Math.Abs(r0.Z + t.BeamZOffsetMm - e0.Z),
            Math.Abs(r1.Z + t.BeamZOffsetMm - e1.Z));
        var reverseElevMax = Math.Max(
            Math.Abs(r0.Z + t.BeamZOffsetMm - e1.Z),
            Math.Abs(r1.Z + t.BeamZOffsetMm - e0.Z));

        var endpointElevationDeviation = reversePlanMax < samePlanMax ? reverseElevMax : sameElevMax;

        return new BeamGeometryResult(
            lineOffset,
            midpointDeviation,
            overlapRatio,
            endpointPlanDeviation,
            endpointElevationDeviation,
            Math.Abs(r.LengthMm - e.LengthMm),
            angleDelta);
    }

    private static double PointToLineDistance(Point3D p, Point3D origin, double ux, double uy)
    {
        var dx = p.X - origin.X;
        var dy = p.Y - origin.Y;
        return Math.Abs(dx * uy - dy * ux);
    }

    private static ValidationResult ColumnResult(ColumnElement r, ColumnElement e, ValidationTolerance t)
    {
        var p = r.CenterPoint.PlanDistanceTo(e.CenterPoint);
        var baseDelta = Math.Abs((r.BaseElevation + t.ColumnZOffsetMm) - e.BaseElevation);
        var topDelta = Math.Abs((r.TopElevation + t.ColumnZOffsetMm) - e.TopElevation);
        var z = Math.Max(baseDelta, topDelta);
        var wd = Math.Abs(r.Width - e.Width);
        var dd = Math.Abs(r.Depth - e.Depth);
        var rot = AngleMath.CircularDeltaDegrees(r.Rotation, e.Rotation, 180);

        var okP = p <= t.PositionToleranceMm;
        var okE = z <= t.ElevationToleranceMm;
        var okS = UnknownSection(r, e) || (wd <= t.DimensionToleranceMm && dd <= t.DimensionToleranceMm);
        var okR = rot <= t.AngleToleranceDegrees;

        var result = Base(r, e, "Column");
        result.PositionDeltaMm = p;
        result.ElevationDeltaMm = z;
        result.WidthDeltaMm = wd;
        result.DepthDeltaMm = dd;
        result.RotationDeltaDeg = rot;
        result.Status = okP && okE && okS && okR
            ? ValidationStatus.Matched
            : !okS ? ValidationStatus.SectionMismatch
            : !okP ? ValidationStatus.PositionMismatch
            : !okE ? ValidationStatus.ElevationMismatch
            : ValidationStatus.RotationMismatch;
        result.Severity = result.Status == ValidationStatus.Matched ? Severity.Info : Severity.Warning;
        result.Confidence = Confidence(new[]
        {
            p / Safe(t.PositionToleranceMm),
            z / Safe(t.ElevationToleranceMm),
            SectionRatio(wd, dd, r, e, t.DimensionToleranceMm),
            rot / Safe(t.AngleToleranceDegrees)
        });
        result.Message = result.Status == ValidationStatus.Matched
            ? "Column correspondence confirmed by plan point; base/top elevation and orientation are within tolerance."
            : $"{result.Status}: Δpos {p:F1} mm; Δelev {z:F1} mm; Δsection {wd:F1}x{dd:F1} mm; Δrot {rot:F1}°.";
        AddDiffs(result);
        return result;
    }

    private static ValidationResult BeamResult(BeamElement r, BeamElement e, ValidationTolerance t)
    {
        var g = Geometry(r, e, t);
        var wd = Math.Abs(r.Width - e.Width);
        var dd = Math.Abs(r.Depth - e.Depth);
        var okP = g.LineOffset <= t.PositionToleranceMm &&
                  g.OverlapRatio >= Math.Clamp(t.BeamMinimumOverlapRatio, 0.0, 1.0);
        var okE = g.EndpointElevationDeviation <= t.ElevationToleranceMm;
        var okL = g.LengthDelta <= t.LengthToleranceMm;
        var okS = UnknownSection(r, e) || (wd <= t.DimensionToleranceMm && dd <= t.DimensionToleranceMm);
        var okR = g.AngleDelta <= t.AngleToleranceDegrees;

        var result = Base(r, e, "Beam");
        result.PositionDeltaMm = g.LineOffset;
        result.ElevationDeltaMm = g.EndpointElevationDeviation;
        result.WidthDeltaMm = wd;
        result.DepthDeltaMm = dd;
        result.LengthDeltaMm = g.LengthDelta;
        result.RotationDeltaDeg = g.AngleDelta;
        result.Status = okP && okE && okL && okS && okR
            ? ValidationStatus.Matched
            : !okS ? ValidationStatus.SectionMismatch
            : !okP ? ValidationStatus.PositionMismatch
            : !okE ? ValidationStatus.ElevationMismatch
            : !okL ? ValidationStatus.GeometryMismatch
            : ValidationStatus.RotationMismatch;
        result.Severity = result.Status == ValidationStatus.Matched ? Severity.Info : Severity.Warning;
        result.Confidence = Confidence(new[]
        {
            g.LineOffset / Safe(t.PositionToleranceMm),
            g.MidpointDeviation / Safe(t.PositionToleranceMm),
            1.0 - g.OverlapRatio,
            g.EndpointElevationDeviation / Safe(t.ElevationToleranceMm),
            g.LengthDelta / Safe(t.LengthToleranceMm),
            SectionRatio(wd, dd, r, e, t.DimensionToleranceMm),
            g.AngleDelta / Safe(t.AngleToleranceDegrees)
        });
        result.Message = result.Status == ValidationStatus.Matched
            ? "Beam correspondence confirmed by plan-line direction, line offset and finite-segment overlap; elevation, length and section are within tolerance."
            : $"{result.Status}: line offset {g.LineOffset:F1} mm; overlap {g.OverlapRatio:P0}; Δelev {g.EndpointElevationDeviation:F1} mm; ΔL {g.LengthDelta:F1} mm; Δsection {wd:F1}x{dd:F1} mm; Δrot {g.AngleDelta:F1}°.";
        AddDiffs(result);
        return result;
    }

    private static bool UnknownSection(ElementBase r, ElementBase e)
        => r is not BeamElement && r is not ColumnElement ||
           (r is BeamElement rb && e is BeamElement eb && (rb.Width <= 0 || rb.Depth <= 0 || eb.Width <= 0 || eb.Depth <= 0)) ||
           (r is ColumnElement rc && e is ColumnElement ec && (rc.Width <= 0 || rc.Depth <= 0 || ec.Width <= 0 || ec.Depth <= 0));

    private static double SectionPenalty(double rw, double rd, double ew, double ed, double tolerance)
        => rw <= 0 || rd <= 0 || ew <= 0 || ed <= 0
            ? 0.0
            : (Math.Abs(rw - ew) + Math.Abs(rd - ed)) / (2 * Safe(tolerance));

    private static double SectionRatio(double dw, double dd, ElementBase r, ElementBase e, double tolerance)
        => UnknownSection(r, e) ? 0.0 : Math.Max(dw, dd) / Safe(tolerance);

    private static double Safe(double value) => Math.Max(Math.Abs(value), 1e-9);

    private static ValidationResult MissingRevit(ElementBase r, string type) => new()
    {
        RevitElementId = r.Id,
        RevitName = r.Name,
        ElementType = type,
        StoryOrLevel = r.LevelName,
        Status = ValidationStatus.MissingInEtabs,
        Severity = Severity.Critical,
        Confidence = 0,
        Message = $"{type} exists in Revit but no ETABS counterpart passed the plan-geometry identity gate."
    };

    private static ValidationResult MissingEtabs(ElementBase e, string type) => new()
    {
        EtabsElementId = e.Id,
        EtabsName = e.Name,
        ElementType = type,
        StoryOrLevel = e.LevelName,
        Status = ValidationStatus.MissingInRevit,
        Severity = Severity.Critical,
        Confidence = 0,
        Message = $"ETABS {type.ToLowerInvariant()} remains unmatched; no Revit counterpart passed the plan-geometry identity gate."
    };

    private static ValidationResult Base(ElementBase r, ElementBase e, string type) => new()
    {
        RevitElementId = r.Id,
        RevitName = r.Name,
        EtabsElementId = e.Id,
        EtabsName = e.Name,
        ElementType = type,
        StoryOrLevel = string.IsNullOrWhiteSpace(r.LevelName) ? e.LevelName : r.LevelName
    };

    private static double Confidence(IEnumerable<double> ratios)
    {
        var values = ratios.Select(x => Math.Min(1.0, Math.Abs(x))).ToList();
        return values.Count == 0 ? 100.0 : Math.Max(0.0, Math.Min(100.0, 100.0 * (1.0 - values.Average())));
    }

    private static void AddDiffs(ValidationResult result)
    {
        result.Differences["Position"] = $"{result.PositionDeltaMm:F1} mm";
        result.Differences["Elevation"] = $"{result.ElevationDeltaMm:F1} mm";
        result.Differences["Width"] = $"{result.WidthDeltaMm:F1} mm";
        result.Differences["Depth"] = $"{result.DepthDeltaMm:F1} mm";
        result.Differences["Length"] = $"{result.LengthDeltaMm:F1} mm";
        result.Differences["Rotation"] = $"{result.RotationDeltaDeg:F1} deg";
    }
}
