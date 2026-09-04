using RevitEtabsValidator.Core.Geometry;
using RevitEtabsValidator.Core.Models;
using RevitEtabsValidator.Core.Validation;

namespace RevitEtabsValidator.Core.Comparison;

/// <summary>
/// Geometry-first one-to-one correspondence engine.
///
/// Columns are treated as plan points with a vertical extent (base/top Z).
/// Beams are treated as finite line segments and are matched by both endpoints,
/// allowing ETABS start/end reversal.
///
/// All coordinates are expected in millimetres. Before comparing vertical geometry,
/// each model is placed on its own structural-base datum: the lowest structural
/// member Z in each model becomes Z=0. This implements the project rule that the
/// ETABS Base corresponds to the Revit model structural base. XY is intentionally
/// not translated because an arbitrary plan-origin shift would hide a real
/// coordination error.
/// </summary>
public sealed class ModelComparer
{
    public ValidationReport CompareColumns(
        IReadOnlyList<ColumnElement> revit,
        IReadOnlyList<ColumnElement> etabs,
        ValidationTolerance tol)
    {
        var report = new ValidationReport();
        var revitBaseZ = FindColumnBaseZ(revit);
        var etabsBaseZ = FindColumnBaseZ(etabs);
        var remaining = new HashSet<string>(etabs.Select(x => x.Id), StringComparer.OrdinalIgnoreCase);

        var pending = revit.Select(r =>
        {
            var candidates = etabs
                .Where(e => remaining.Contains(e.Id) && ColumnGate(r, e, tol, revitBaseZ, etabsBaseZ))
                .Select(e => (e, Score: ColumnScore(r, e, tol, revitBaseZ, etabsBaseZ)))
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
            if (candidates.Count > 1 && Math.Abs(candidates[1].Score - best.Score) < Math.Max(0, tol.AmbiguousScoreGap))
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
                    Message = "Ambiguous column correspondence: two ETABS point candidates have nearly identical geometry scores."
                });
                continue;
            }

            report.Results.Add(ColumnResult(item.r, best.e, tol, revitBaseZ, etabsBaseZ));
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
        var revitBaseZ = FindBeamBaseZ(revit);
        var etabsBaseZ = FindBeamBaseZ(etabs);
        var remaining = new HashSet<string>(etabs.Select(x => x.Id), StringComparer.OrdinalIgnoreCase);

        var pending = revit.Select(r =>
        {
            var candidates = etabs
                .Where(e => remaining.Contains(e.Id) && BeamGate(r, e, tol, revitBaseZ, etabsBaseZ))
                .Select(e => (e, Score: BeamScore(r, e, tol, revitBaseZ, etabsBaseZ)))
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
            if (candidates.Count > 1 && Math.Abs(candidates[1].Score - best.Score) < Math.Max(0, tol.AmbiguousScoreGap))
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
                    Message = "Ambiguous beam correspondence: two ETABS line candidates have nearly identical endpoint geometry scores."
                });
                continue;
            }

            report.Results.Add(BeamResult(item.r, best.e, tol, revitBaseZ, etabsBaseZ));
            remaining.Remove(best.e.Id);
        }

        foreach (var e in etabs.Where(x => remaining.Contains(x.Id)))
            report.Results.Add(MissingEtabs(e, "Beam"));

        return report;
    }

    private static bool ColumnGate(ColumnElement r, ColumnElement e, ValidationTolerance t, double rb, double eb)
    {
        if (r.CenterPoint.PlanDistanceTo(e.CenterPoint) > t.PositionToleranceMm)
            return false;

        var baseDelta = Math.Abs((r.BaseElevation - rb + t.ColumnZOffsetMm) - (e.BaseElevation - eb));
        var topDelta = Math.Abs((r.TopElevation - rb + t.ColumnZOffsetMm) - (e.TopElevation - eb));
        return baseDelta <= t.ElevationToleranceMm && topDelta <= t.ElevationToleranceMm;
    }

    private static bool BeamGate(BeamElement r, BeamElement e, ValidationTolerance t, double rb, double eb)
    {
        var g = Geometry(r, e, t, rb, eb);
        return g.EndpointPlanDeviation <= t.PositionToleranceMm &&
               g.EndpointElevationDeviation <= t.ElevationToleranceMm &&
               g.LengthDelta <= t.LengthToleranceMm &&
               g.AngleDelta <= t.AngleToleranceDegrees;
    }

    private static double ColumnScore(ColumnElement r, ColumnElement e, ValidationTolerance t, double rb, double eb)
    {
        var plan = r.CenterPoint.PlanDistanceTo(e.CenterPoint) / Safe(t.PositionToleranceMm);
        var z = (Math.Abs((r.BaseElevation - rb + t.ColumnZOffsetMm) - (e.BaseElevation - eb)) +
                 Math.Abs((r.TopElevation - rb + t.ColumnZOffsetMm) - (e.TopElevation - eb))) /
                (2 * Safe(t.ElevationToleranceMm));
        var section = SectionPenalty(r.Width, r.Depth, e.Width, e.Depth, t.DimensionToleranceMm);
        var rot = AngleMath.CircularDeltaDegrees(r.Rotation, e.Rotation, 180) / Safe(t.AngleToleranceDegrees);
        return 6 * plan + 3 * z + section + 0.5 * rot;
    }

    private static double BeamScore(BeamElement r, BeamElement e, ValidationTolerance t, double rb, double eb)
    {
        var g = Geometry(r, e, t, rb, eb);
        var endpoint = g.EndpointPlanDeviation / Safe(t.PositionToleranceMm);
        var z = g.EndpointElevationDeviation / Safe(t.ElevationToleranceMm);
        var len = g.LengthDelta / Safe(t.LengthToleranceMm);
        var angle = g.AngleDelta / Safe(t.AngleToleranceDegrees);
        var section = SectionPenalty(r.Width, r.Depth, e.Width, e.Depth, t.DimensionToleranceMm);
        return 7 * endpoint + 2 * z + 2 * len + angle + 0.5 * section;
    }

    private readonly record struct BeamGeometryResult(
        double EndpointPlanDeviation,
        double EndpointElevationDeviation,
        double LengthDelta,
        double AngleDelta);

    private static BeamGeometryResult Geometry(BeamElement r, BeamElement e, ValidationTolerance t, double rb, double eb)
    {
        var samePlanA = r.StartPoint.PlanDistanceTo(e.StartPoint);
        var samePlanB = r.EndPoint.PlanDistanceTo(e.EndPoint);
        var revPlanA = r.StartPoint.PlanDistanceTo(e.EndPoint);
        var revPlanB = r.EndPoint.PlanDistanceTo(e.StartPoint);

        var rz0 = r.StartPoint.Z - rb + t.BeamZOffsetMm;
        var rz1 = r.EndPoint.Z - rb + t.BeamZOffsetMm;
        var ez0 = e.StartPoint.Z - eb;
        var ez1 = e.EndPoint.Z - eb;

        var sameMax = Math.Max(samePlanA, samePlanB);
        var reverseMax = Math.Max(revPlanA, revPlanB);

        if (reverseMax < sameMax)
        {
            return new BeamGeometryResult(
                reverseMax,
                Math.Max(Math.Abs(rz0 - ez1), Math.Abs(rz1 - ez0)),
                Math.Abs(r.LengthMm - e.LengthMm),
                AngleMath.CircularDeltaDegrees(r.Rotation, e.Rotation, 180));
        }

        return new BeamGeometryResult(
            sameMax,
            Math.Max(Math.Abs(rz0 - ez0), Math.Abs(rz1 - ez1)),
            Math.Abs(r.LengthMm - e.LengthMm),
            AngleMath.CircularDeltaDegrees(r.Rotation, e.Rotation, 180));
    }

    private static ValidationResult ColumnResult(ColumnElement r, ColumnElement e, ValidationTolerance t, double rb, double eb)
    {
        var p = r.CenterPoint.PlanDistanceTo(e.CenterPoint);
        var baseDelta = Math.Abs((r.BaseElevation - rb + t.ColumnZOffsetMm) - (e.BaseElevation - eb));
        var topDelta = Math.Abs((r.TopElevation - rb + t.ColumnZOffsetMm) - (e.TopElevation - eb));
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
            ? "Column correspondence confirmed by plan point and normalized base/top elevation."
            : $"{result.Status}: Δpos {p:F1} mm; Δelev {z:F1} mm; Δsection {wd:F1}x{dd:F1} mm; Δrot {rot:F1}°.";
        AddDiffs(result);
        return result;
    }

    private static ValidationResult BeamResult(BeamElement r, BeamElement e, ValidationTolerance t, double rb, double eb)
    {
        var g = Geometry(r, e, t, rb, eb);
        var wd = Math.Abs(r.Width - e.Width);
        var dd = Math.Abs(r.Depth - e.Depth);
        var okP = g.EndpointPlanDeviation <= t.PositionToleranceMm;
        var okE = g.EndpointElevationDeviation <= t.ElevationToleranceMm;
        var okL = g.LengthDelta <= t.LengthToleranceMm;
        var okS = UnknownSection(r, e) || (wd <= t.DimensionToleranceMm && dd <= t.DimensionToleranceMm);
        var okR = g.AngleDelta <= t.AngleToleranceDegrees;

        var result = Base(r, e, "Beam");
        result.PositionDeltaMm = g.EndpointPlanDeviation;
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
            g.EndpointPlanDeviation / Safe(t.PositionToleranceMm),
            g.EndpointElevationDeviation / Safe(t.ElevationToleranceMm),
            g.LengthDelta / Safe(t.LengthToleranceMm),
            SectionRatio(wd, dd, r, e, t.DimensionToleranceMm),
            g.AngleDelta / Safe(t.AngleToleranceDegrees)
        });
        result.Message = result.Status == ValidationStatus.Matched
            ? "Beam correspondence confirmed by both endpoints, normalized elevation, length, direction and section."
            : $"{result.Status}: endpoint Δ {g.EndpointPlanDeviation:F1} mm; Δelev {g.EndpointElevationDeviation:F1} mm; ΔL {g.LengthDelta:F1} mm; Δsection {wd:F1}x{dd:F1} mm; Δrot {g.AngleDelta:F1}°.";
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

    private static double FindColumnBaseZ(IReadOnlyList<ColumnElement> values)
        => values.Count == 0 ? 0.0 : values.Min(x => Math.Min(x.BaseElevation, x.TopElevation));

    private static double FindBeamBaseZ(IReadOnlyList<BeamElement> values)
        => values.Count == 0 ? 0.0 : values.SelectMany(x => new[] { x.StartPoint.Z, x.EndPoint.Z }).Min();

    private static ValidationResult MissingRevit(ElementBase r, string type) => new()
    {
        RevitElementId = r.Id,
        RevitName = r.Name,
        ElementType = type,
        StoryOrLevel = r.LevelName,
        Status = ValidationStatus.MissingInEtabs,
        Severity = Severity.Critical,
        Confidence = 0,
        Message = $"{type} exists in Revit but no ETABS counterpart passed the geometric candidate gate."
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
        Message = $"ETABS {type.ToLowerInvariant()} remains unmatched; no Revit counterpart passed the geometric candidate gate."
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
