using RevitEtabsValidator.Core.Geometry;
using RevitEtabsValidator.Core.Models;
using RevitEtabsValidator.Core.Validation;

namespace RevitEtabsValidator.Core.Comparison;

/// <summary>
/// Geometry-first one-to-one correspondence engine.
///
/// Columns are point-like objects in plan; beams are finite plan line segments.
/// Revit uses Internal Origin coordinates and ETABS uses Global coordinates, both
/// normalized to millimetres by their readers. The spatial index only reduces
/// candidate work; the exact geometry gates remain authoritative.
///
/// Column elevation validation compares the Revit column midpoint Z to the ETABS
/// column midpoint Z. Beam elevation validation is independent of beam identity
/// and compares the Revit beam midpoint Z to the ETABS frame reference Z with the
/// ETABS beam half-depth applied in either vertical direction (+D/2 or -D/2). The
/// nearer convention is the one used for validation, so the tool does not assume
/// a sign that may differ between analytical representations.
/// </summary>
public sealed class ModelComparer
{
    public ValidationReport CompareColumns(IReadOnlyList<ColumnElement> revit, IReadOnlyList<ColumnElement> etabs, ValidationTolerance tol)
    {
        var report = new ValidationReport();
        var remaining = new HashSet<string>(etabs.Select(x => x.Id), StringComparer.OrdinalIgnoreCase);
        var index = BuildIndex(etabs, tol);

        var pending = revit.Select(r =>
        {
            var candidates = index.Query(
                    r.CenterPoint.X - tol.PositionToleranceMm,
                    r.CenterPoint.Y - tol.PositionToleranceMm,
                    r.CenterPoint.X + tol.PositionToleranceMm,
                    r.CenterPoint.Y + tol.PositionToleranceMm)
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
                report.Results.Add(Ambiguous(item.r, best.e, "Column"));
                continue;
            }

            report.Results.Add(ColumnResult(item.r, best.e, tol));
            remaining.Remove(best.e.Id);
        }

        foreach (var e in etabs.Where(x => remaining.Contains(x.Id)))
            report.Results.Add(MissingEtabs(e, "Column"));

        return report;
    }

    public ValidationReport CompareBeams(IReadOnlyList<BeamElement> revit, IReadOnlyList<BeamElement> etabs, ValidationTolerance tol)
    {
        var report = new ValidationReport();
        var remaining = new HashSet<string>(etabs.Select(x => x.Id), StringComparer.OrdinalIgnoreCase);
        var index = BuildIndex(etabs, tol);
        var expand = Math.Max(0.0, tol.PositionToleranceMm + tol.LengthToleranceMm);

        var pending = revit.Select(r =>
        {
            var minX = Math.Min(r.StartPoint.X, r.EndPoint.X) - expand;
            var maxX = Math.Max(r.StartPoint.X, r.EndPoint.X) + expand;
            var minY = Math.Min(r.StartPoint.Y, r.EndPoint.Y) - expand;
            var maxY = Math.Max(r.StartPoint.Y, r.EndPoint.Y) + expand;

            var candidates = index.Query(minX, minY, maxX, maxY)
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
                report.Results.Add(Ambiguous(item.r, best.e, "Beam"));
                continue;
            }

            report.Results.Add(BeamResult(item.r, best.e, tol));
            remaining.Remove(best.e.Id);
        }

        foreach (var e in etabs.Where(x => remaining.Contains(x.Id)))
            report.Results.Add(MissingEtabs(e, "Beam"));

        return report;
    }

    private static SpatialGridIndex<T> BuildIndex<T>(IReadOnlyList<T> values, ValidationTolerance tol) where T : ElementBase
    {
        var cellSize = Math.Max(500.0, Math.Max(tol.PositionToleranceMm, 1.0) * 8.0);
        var index = new SpatialGridIndex<T>(cellSize);
        foreach (var value in values)
            index.Add(value);
        return index;
    }

    private static bool ColumnIdentityGate(ColumnElement r, ColumnElement e, ValidationTolerance t)
        => r.CenterPoint.PlanDistanceTo(e.CenterPoint) <= t.PositionToleranceMm;

    private static bool BeamIdentityGate(BeamElement r, BeamElement e, ValidationTolerance t)
    {
        var g = Geometry(r, e, t);
        return g.LineOffset <= t.PositionToleranceMm &&
               g.OverlapRatio >= Clamp01(t.BeamMinimumOverlapRatio) &&
               g.AngleDelta <= t.AngleToleranceDegrees;
    }

    private static double ColumnScore(ColumnElement r, ColumnElement e, ValidationTolerance t)
    {
        var plan = r.CenterPoint.PlanDistanceTo(e.CenterPoint) / Safe(t.PositionToleranceMm);
        var centerElevationDelta = Math.Abs((r.CenterPoint.Z + t.ColumnZOffsetMm) - e.CenterPoint.Z);
        var z = centerElevationDelta / Safe(t.ElevationToleranceMm);
        var widthDelta = Math.Abs(r.Depth - e.Width);
        var depthDelta = Math.Abs(r.Width - e.Depth);
        var section = SectionPenalty(widthDelta, depthDelta, t.DimensionToleranceMm);
        var rot = AngleMath.CircularDeltaDegrees(r.Rotation, e.Rotation, 180) / Safe(t.AngleToleranceDegrees);
        return 8.0 * plan + 1.5 * z + 1.0 * section + 0.25 * rot;
    }

    private static double BeamScore(BeamElement r, BeamElement e, ValidationTolerance t)
    {
        var g = Geometry(r, e, t);
        var line = g.LineOffset / Safe(t.PositionToleranceMm);
        var midpoint = g.MidpointDeviation / Safe(t.PositionToleranceMm);
        var overlap = 1.0 - g.OverlapRatio;
        var z = g.ExpectedRevitMidpointElevationDelta / Safe(t.ElevationToleranceMm);
        var len = g.LengthDelta / Safe(t.LengthToleranceMm);
        var angle = g.AngleDelta / Safe(t.AngleToleranceDegrees);
        var section = SectionPenalty(Math.Abs(r.Width - e.Width), Math.Abs(r.Depth - e.Depth), t.DimensionToleranceMm);
        return 8.0 * line + 2.0 * midpoint + 3.0 * overlap + 1.5 * z + 1.5 * len + 0.75 * angle + 0.5 * section;
    }

    private readonly record struct BeamGeometryResult(
        double LineOffset,
        double MidpointDeviation,
        double OverlapRatio,
        double EndpointPlanDeviation,
        double ExpectedRevitMidpointElevationDelta,
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
                ExpectedRevitBeamMidpointElevationDelta(r, e, t),
                Math.Max(
                    Math.Abs(r0.Z + t.BeamZOffsetMm - e0.Z),
                    Math.Abs(r1.Z + t.BeamZOffsetMm - e1.Z)),
                Math.Abs(r.LengthMm - e.LengthMm),
                180.0);
        }

        var urx = rdx / rLen;
        var ury = rdy / rLen;
        var uex = edx / eLen;
        var uey = edy / eLen;
        var lineOffset = Math.Max(
            Math.Max(PointToLineDistance(e0, r0, urx, ury), PointToLineDistance(e1, r0, urx, ury)),
            Math.Max(PointToLineDistance(r0, e0, uex, uey), PointToLineDistance(r1, e0, uex, uey)));

        var sameEndA = r0.PlanDistanceTo(e0);
        var sameEndB = r1.PlanDistanceTo(e1);
        var reverseEndA = r0.PlanDistanceTo(e1);
        var reverseEndB = r1.PlanDistanceTo(e0);
        var endpointPlanDeviation = Math.Min(Math.Max(sameEndA, sameEndB), Math.Max(reverseEndA, reverseEndB));

        var midX = (r0.X + r1.X) / 2.0;
        var midY = (r0.Y + r1.Y) / 2.0;
        var eMidX = (e0.X + e1.X) / 2.0;
        var eMidY = (e0.Y + e1.Y) / 2.0;
        var midpointDeviation = Math.Sqrt(Math.Pow(midX - eMidX, 2) + Math.Pow(midY - eMidY, 2));

        var ep0 = (e0.X - r0.X) * urx + (e0.Y - r0.Y) * ury;
        var ep1 = (e1.X - r0.X) * urx + (e1.Y - r0.Y) * ury;
        var eMin = Math.Min(ep0, ep1);
        var eMax = Math.Max(ep0, ep1);
        var overlap = Math.Max(0.0, Math.Min(rLen, eMax) - Math.Max(0.0, eMin));
        var overlapRatio = Clamp01(overlap / Math.Max(1e-9, Math.Min(rLen, eLen)));

        var angleDelta = AngleMath.CircularDeltaDegrees(r.Rotation, e.Rotation, 180);
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
            ExpectedRevitBeamMidpointElevationDelta(r, e, t),
            endpointElevationDeviation,
            Math.Abs(r.LengthMm - e.LengthMm),
            angleDelta);
    }

    private static double ExpectedRevitBeamMidpointElevationDelta(BeamElement revitBeam, BeamElement etabsBeam, ValidationTolerance t)
    {
        var revitMidZ = revitBeam.CenterPoint.Z + t.BeamZOffsetMm;
        var depth = Math.Max(0.0, etabsBeam.Depth) * 0.5;
        var expectedPlus = etabsBeam.CenterPoint.Z + depth;
        var expectedMinus = etabsBeam.CenterPoint.Z - depth;
        return Math.Min(
            Math.Abs(revitMidZ - expectedPlus),
            Math.Abs(revitMidZ - expectedMinus));
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
        var midpointElevationDelta = Math.Abs((r.CenterPoint.Z + t.ColumnZOffsetMm) - e.CenterPoint.Z);
        var widthDelta = Math.Abs(r.Depth - e.Width);
        var depthDelta = Math.Abs(r.Width - e.Depth);
        var rot = AngleMath.CircularDeltaDegrees(r.Rotation, e.Rotation, 180);
        var okP = p <= t.PositionToleranceMm;
        var okE = midpointElevationDelta <= t.ElevationToleranceMm;
        var okS = UnknownSection(r, e) || (widthDelta <= t.DimensionToleranceMm && depthDelta <= t.DimensionToleranceMm);
        var okR = rot <= t.AngleToleranceDegrees;
        var result = Base(r, e, "Column");
        result.PositionDeltaMm = p;
        result.ElevationDeltaMm = midpointElevationDelta;
        result.WidthDeltaMm = widthDelta;
        result.DepthDeltaMm = depthDelta;
        result.RotationDeltaDeg = rot;
        result.Status = okP && okE && okS && okR ? ValidationStatus.Matched : !okS ? ValidationStatus.SectionMismatch : !okP ? ValidationStatus.PositionMismatch : !okE ? ValidationStatus.ElevationMismatch : ValidationStatus.RotationMismatch;
        result.Severity = result.Status == ValidationStatus.Matched ? Severity.Info : Severity.Warning;
        result.Confidence = Confidence(new[]
        {
            p / Safe(t.PositionToleranceMm),
            midpointElevationDelta / Safe(t.ElevationToleranceMm),
            SectionRatio(widthDelta, depthDelta, r, e, t.DimensionToleranceMm),
            rot / Safe(t.AngleToleranceDegrees)
        });
        result.Message = result.Status == ValidationStatus.Matched
            ? "Column correspondence confirmed by plan point; midpoint elevation, section and orientation are within tolerance."
            : $"{result.Status}: Δpos {p:F1} mm; Δmid-elev {midpointElevationDelta:F1} mm; Δsection Width {widthDelta:F1} / Depth {depthDelta:F1} mm; Δrot {rot:F1}°.";
        AddDiffs(result);
        return result;
    }

    private static ValidationResult BeamResult(BeamElement r, BeamElement e, ValidationTolerance t)
    {
        var g = Geometry(r, e, t);
        var wd = Math.Abs(r.Width - e.Width);
        var dd = Math.Abs(r.Depth - e.Depth);
        var okP = g.LineOffset <= t.PositionToleranceMm && g.OverlapRatio >= Clamp01(t.BeamMinimumOverlapRatio);
        var okE = g.ExpectedRevitMidpointElevationDelta <= t.ElevationToleranceMm;
        var okS = UnknownSection(r, e) || (wd <= t.DimensionToleranceMm && dd <= t.DimensionToleranceMm);
        var okR = g.AngleDelta <= t.AngleToleranceDegrees;

        var result = Base(r, e, "Beam");
        result.PositionDeltaMm = g.LineOffset;
        result.ElevationDeltaMm = g.ExpectedRevitMidpointElevationDelta;
        result.WidthDeltaMm = wd;
        result.DepthDeltaMm = dd;
        result.LengthDeltaMm = g.LengthDelta;
        result.RotationDeltaDeg = g.AngleDelta;

        result.Status = okP && okE && okS && okR
            ? ValidationStatus.Matched
            : !okS ? ValidationStatus.SectionMismatch
            : !okP ? ValidationStatus.PositionMismatch
            : !okE ? ValidationStatus.ElevationMismatch
            : ValidationStatus.RotationMismatch;

        result.Severity = result.Status == ValidationStatus.Matched ? Severity.Info : Severity.Warning;
        result.Confidence = Confidence(new[]
        {
            g.LineOffset / Safe(t.PositionToleranceMm),
            g.MidpointDeviation / Safe(t.PositionToleranceMm),
            1.0 - g.OverlapRatio,
            g.ExpectedRevitMidpointElevationDelta / Safe(t.ElevationToleranceMm),
            g.LengthDelta / Safe(t.LengthToleranceMm),
            SectionRatio(wd, dd, r, e, t.DimensionToleranceMm),
            g.AngleDelta / Safe(t.AngleToleranceDegrees)
        });

        result.Message = result.Status == ValidationStatus.Matched
            ? $"Beam correspondence confirmed by plan line; midpoint elevation, line offset and overlap pass. Span ΔL = {g.LengthDelta:F1} mm is reported diagnostically."
            : $"{result.Status}: line offset {g.LineOffset:F1} mm; overlap {g.OverlapRatio:P0}; Δmid-elev {g.ExpectedRevitMidpointElevationDelta:F1} mm; span ΔL {g.LengthDelta:F1} mm; Δsection {wd:F1}x{dd:F1} mm; Δrot {g.AngleDelta:F1}°.";
        AddDiffs(result);
        return result;
    }

    private static bool UnknownSection(ElementBase r, ElementBase e)
        => r is not BeamElement && r is not ColumnElement ||
           (r is BeamElement rb && e is BeamElement eb && (rb.Width <= 0 || rb.Depth <= 0 || eb.Width <= 0 || eb.Depth <= 0)) ||
           (r is ColumnElement rc && e is ColumnElement ec && (rc.Width <= 0 || rc.Depth <= 0 || ec.Width <= 0 || ec.Depth <= 0));

    private static double SectionPenalty(double widthDelta, double depthDelta, double tolerance)
        => (widthDelta + depthDelta) / (2.0 * Safe(tolerance));

    private static double SectionRatio(double widthDelta, double depthDelta, ElementBase r, ElementBase e, double tolerance)
        => UnknownSection(r, e) ? 0.0 : Math.Max(widthDelta, depthDelta) / Safe(tolerance);

    private static double Safe(double value) => Math.Max(Math.Abs(value), 1e-9);
    private static double Clamp01(double value) => value < 0.0 ? 0.0 : value > 1.0 ? 1.0 : value;

    private static ValidationResult Ambiguous(ElementBase r, ElementBase e, string type) => new()
    {
        RevitElementId = r.Id,
        RevitName = r.Name,
        EtabsElementId = e.Id,
        EtabsName = e.Name,
        ElementType = type,
        StoryOrLevel = r.LevelName,
        Status = ValidationStatus.AmbiguousMatch,
        Severity = Severity.Error,
        Confidence = 0,
        Message = $"Ambiguous {type.ToLowerInvariant()} correspondence: more than one ETABS candidate has a nearly identical geometry score."
    };

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
