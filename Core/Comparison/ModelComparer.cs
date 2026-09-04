using RevitEtabsValidator.Core.Geometry;
using RevitEtabsValidator.Core.Models;
using RevitEtabsValidator.Core.Validation;

namespace RevitEtabsValidator.Core.Comparison;

/// <summary>
/// Geometry-first one-to-one correspondence engine.
///
/// A beam is a LINE segment in plan/elevation, so its identity is based primarily
/// on the two endpoints (allowing endpoint reversal). A column is treated as a
/// POINT in plan, with base/top elevations defining its vertical extent.
/// Names are informational only and are never used as the correspondence key.
/// </summary>
public sealed class ModelComparer
{
    public ValidationReport CompareColumns(
        IReadOnlyList<ColumnElement> revit,
        IReadOnlyList<ColumnElement> etabs,
        ValidationTolerance tol)
        => CompareColumnsInternal(revit, etabs, tol);

    public ValidationReport CompareBeams(
        IReadOnlyList<BeamElement> revit,
        IReadOnlyList<BeamElement> etabs,
        ValidationTolerance tol)
        => CompareBeamsInternal(revit, etabs, tol);

    private static ValidationReport CompareColumnsInternal(
        IReadOnlyList<ColumnElement> revit,
        IReadOnlyList<ColumnElement> etabs,
        ValidationTolerance tol)
    {
        var report = new ValidationReport();
        var remaining = new HashSet<string>(etabs.Select(x => x.Id), StringComparer.OrdinalIgnoreCase);

        // Hard geometric gate first. This prevents a column on a different story
        // or at a distant location from ever being considered a counterpart.
        var pending = revit
            .Select(r =>
            {
                var candidates = etabs
                    .Where(e => remaining.Contains(e.Id) && ColumnWithinCandidateGate(r, e, tol))
                    .Select(e => (Element: e, Score: ColumnMatchScore(r, e, tol)))
                    .OrderBy(x => x.Score)
                    .ToList();
                return (Element: r, Candidates: candidates);
            })
            // Elements with the fewest valid candidates are solved first. This
            // is more stable than blindly following Revit's collector order.
            .OrderBy(x => x.Candidates.Count)
            .ThenBy(x => x.Element.LevelName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Element.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var item in pending)
        {
            var r = item.Element;
            var candidates = item.Candidates.Where(x => remaining.Contains(x.Element.Id)).ToList();

            if (candidates.Count == 0)
            {
                report.Results.Add(MissingRevitResult(r, "Column"));
                continue;
            }

            var best = candidates[0];
            if (candidates.Count > 1 &&
                Math.Abs(candidates[1].Score - best.Score) < Math.Max(0, tol.AmbiguousScoreGap))
            {
                report.Results.Add(new ValidationResult
                {
                    RevitElementId = r.Id,
                    RevitName = r.Name,
                    EtabsElementId = best.Element.Id,
                    EtabsName = best.Element.Name,
                    ElementType = "Column",
                    StoryOrLevel = r.LevelName,
                    Status = ValidationStatus.AmbiguousMatch,
                    Severity = Severity.Error,
                    Confidence = 0,
                    Message = "Ambiguous column correspondence: two ETABS point candidates have nearly identical geometry scores."
                });
                continue;
            }

            report.Results.Add(ColumnResult(r, best.Element, tol));
            remaining.Remove(best.Element.Id);
        }

        foreach (var e in etabs.Where(x => remaining.Contains(x.Id)))
            report.Results.Add(MissingEtabsResult(e, "Column"));

        return report;
    }

    private static ValidationReport CompareBeamsInternal(
        IReadOnlyList<BeamElement> revit,
        IReadOnlyList<BeamElement> etabs,
        ValidationTolerance tol)
    {
        var report = new ValidationReport();
        var remaining = new HashSet<string>(etabs.Select(x => x.Id), StringComparer.OrdinalIgnoreCase);

        // A beam is a line segment, not a point. Candidate selection therefore
        // requires both endpoints to be spatially close after allowing endpoint
        // reversal. Elevation, length and direction are secondary discriminators.
        var pending = revit
            .Select(r =>
            {
                var candidates = etabs
                    .Where(e => remaining.Contains(e.Id) && BeamWithinCandidateGate(r, e, tol))
                    .Select(e => (Element: e, Score: BeamMatchScore(r, e, tol)))
                    .OrderBy(x => x.Score)
                    .ToList();
                return (Element: r, Candidates: candidates);
            })
            .OrderBy(x => x.Candidates.Count)
            .ThenBy(x => x.Element.LevelName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Element.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var item in pending)
        {
            var r = item.Element;
            var candidates = item.Candidates.Where(x => remaining.Contains(x.Element.Id)).ToList();

            if (candidates.Count == 0)
            {
                report.Results.Add(MissingRevitResult(r, "Beam"));
                continue;
            }

            var best = candidates[0];
            if (candidates.Count > 1 &&
                Math.Abs(candidates[1].Score - best.Score) < Math.Max(0, tol.AmbiguousScoreGap))
            {
                report.Results.Add(new ValidationResult
                {
                    RevitElementId = r.Id,
                    RevitName = r.Name,
                    EtabsElementId = best.Element.Id,
                    EtabsName = best.Element.Name,
                    ElementType = "Beam",
                    StoryOrLevel = r.LevelName,
                    Status = ValidationStatus.AmbiguousMatch,
                    Severity = Severity.Error,
                    Confidence = 0,
                    Message = "Ambiguous beam correspondence: two ETABS line candidates have nearly identical endpoint geometry scores."
                });
                continue;
            }

            report.Results.Add(BeamResult(r, best.Element, tol));
            remaining.Remove(best.Element.Id);
        }

        foreach (var e in etabs.Where(x => remaining.Contains(x.Id)))
            report.Results.Add(MissingEtabsResult(e, "Beam"));

        return report;
    }

    private static ValidationResult MissingRevitResult(ElementBase r, string type) => new()
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

    private static ValidationResult MissingEtabsResult(ElementBase e, string type) => new()
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

    private static double SafeTol(double value) => Math.Max(Math.Abs(value), 1e-9);

    private static double AdjBeamZ(double revitZ, ValidationTolerance tol) => revitZ + tol.BeamZOffsetMm;
    private static double AdjColZ(double revitZ, ValidationTolerance tol) => revitZ + tol.ColumnZOffsetMm;

    private static bool ColumnWithinCandidateGate(ColumnElement r, ColumnElement e, ValidationTolerance t)
    {
        var plan = r.CenterPoint.PlanDistanceTo(e.CenterPoint);
        if (plan > t.PositionToleranceMm)
            return false;

        var baseDelta = Math.Abs(AdjColZ(r.BaseElevation, t) - e.BaseElevation);
        var topDelta = Math.Abs(AdjColZ(r.TopElevation, t) - e.TopElevation);
        if (baseDelta > t.ElevationToleranceMm || topDelta > t.ElevationToleranceMm)
            return false;

        return true;
    }

    private static bool BeamWithinCandidateGate(BeamElement r, BeamElement e, ValidationTolerance t)
    {
        var geom = BeamGeometry(r, e, t);

        // Both endpoints must be close in plan. Using max endpoint deviation
        // prevents a short/rotated line from being accepted merely because its
        // center point happens to be close.
        if (geom.EndpointPlanDeviationMm > t.PositionToleranceMm)
            return false;

        if (geom.EndpointElevationDeviationMm > t.ElevationToleranceMm)
            return false;

        // A large length discrepancy is not a genuine correspondence even if
        // the center happens to be near another beam.
        if (geom.LengthDeltaMm > t.LengthToleranceMm)
            return false;

        // Direction is a strong beam identity discriminator. Circular 180-degree
        // handling makes start/end reversal equivalent.
        if (geom.AngleDeltaDeg > t.AngleToleranceDegrees)
            return false;

        return true;
    }

    private static double ColumnMatchScore(ColumnElement r, ColumnElement e, ValidationTolerance t)
    {
        var plan = r.CenterPoint.PlanDistanceTo(e.CenterPoint) / SafeTol(t.PositionToleranceMm);
        var elev = (Math.Abs(AdjColZ(r.BaseElevation, t) - e.BaseElevation) +
                    Math.Abs(AdjColZ(r.TopElevation, t) - e.TopElevation)) /
                   (2.0 * SafeTol(t.ElevationToleranceMm));
        var sec = SectionPenalty(r.Width, r.Depth, e.Width, e.Depth, t.DimensionToleranceMm);
        var rot = AngleMath.CircularDeltaDegrees(r.Rotation, e.Rotation, 180) /
                  SafeTol(t.AngleToleranceDegrees);

        // Column identity is fundamentally point location + vertical extent.
        return 6.0 * plan + 3.0 * elev + 1.0 * sec + 0.5 * rot;
    }

    private static double BeamMatchScore(BeamElement r, BeamElement e, ValidationTolerance t)
    {
        var g = BeamGeometry(r, e, t);
        var endpoint = g.EndpointPlanDeviationMm / SafeTol(t.PositionToleranceMm);
        var elev = g.EndpointElevationDeviationMm / SafeTol(t.ElevationToleranceMm);
        var len = g.LengthDeltaMm / SafeTol(t.LengthToleranceMm);
        var angle = g.AngleDeltaDeg / SafeTol(t.AngleToleranceDegrees);
        var sec = SectionPenalty(r.Width, r.Depth, e.Width, e.Depth, t.DimensionToleranceMm);

        // Beam identity is fundamentally the line geometry. Section is lower
        // weight because the same physical beam may intentionally have a changed
        // section, while endpoint geometry still identifies its counterpart.
        return 7.0 * endpoint + 2.0 * elev + 2.0 * len + 1.0 * angle + 0.5 * sec;
    }

    private static double SectionPenalty(double rw, double rd, double ew, double ed, double tolerance)
    {
        // Unknown/non-rectangular sections should not attract or repel a match
        // simply because the dimensions were unavailable.
        if (rw <= 0 || rd <= 0 || ew <= 0 || ed <= 0)
            return 0.0;

        var dw = Math.Abs(rw - ew);
        var dd = Math.Abs(rd - ed);
        return (dw + dd) / (2.0 * SafeTol(tolerance));
    }

    private readonly record struct BeamGeometryResult(
        double EndpointPlanDeviationMm,
        double EndpointElevationDeviationMm,
        double LengthDeltaMm,
        double AngleDeltaDeg);

    private static BeamGeometryResult BeamGeometry(BeamElement r, BeamElement e, ValidationTolerance t)
    {
        // Compare both endpoint pairings so beam direction reversal does not
        // change correspondence.
        var samePlanA = r.StartPoint.PlanDistanceTo(e.StartPoint);
        var samePlanB = r.EndPoint.PlanDistanceTo(e.EndPoint);
        var sameElevA = Math.Abs(AdjBeamZ(r.StartPoint.Z, t) - e.StartPoint.Z);
        var sameElevB = Math.Abs(AdjBeamZ(r.EndPoint.Z, t) - e.EndPoint.Z);

        var revPlanA = r.StartPoint.PlanDistanceTo(e.EndPoint);
        var revPlanB = r.EndPoint.PlanDistanceTo(e.StartPoint);
        var revElevA = Math.Abs(AdjBeamZ(r.StartPoint.Z, t) - e.EndPoint.Z);
        var revElevB = Math.Abs(AdjBeamZ(r.EndPoint.Z, t) - e.StartPoint.Z);

        var sameMax = Math.Max(samePlanA, samePlanB);
        var reverseMax = Math.Max(revPlanA, revPlanB);

        if (reverseMax < sameMax)
        {
            return new BeamGeometryResult(
                reverseMax,
                Math.Max(revElevA, revElevB),
                Math.Abs(r.LengthMm - e.LengthMm),
                AngleMath.CircularDeltaDegrees(r.Rotation, e.Rotation, 180));
        }

        return new BeamGeometryResult(
            sameMax,
            Math.Max(sameElevA, sameElevB),
            Math.Abs(r.LengthMm - e.LengthMm),
            AngleMath.CircularDeltaDegrees(r.Rotation, e.Rotation, 180));
    }

    private static ValidationResult ColumnResult(ColumnElement r, ColumnElement e, ValidationTolerance t)
    {
        var p = r.CenterPoint.PlanDistanceTo(e.CenterPoint);
        var eb = Math.Abs(AdjColZ(r.BaseElevation, t) - e.BaseElevation);
        var et = Math.Abs(AdjColZ(r.TopElevation, t) - e.TopElevation);
        var wd = Math.Abs(r.Width - e.Width);
        var dd = Math.Abs(r.Depth - e.Depth);
        var rot = AngleMath.CircularDeltaDegrees(r.Rotation, e.Rotation, 180);

        var okP = p <= t.PositionToleranceMm;
        var okE = eb <= t.ElevationToleranceMm && et <= t.ElevationToleranceMm;
        var okS = (r.Width <= 0 || r.Depth <= 0 || e.Width <= 0 || e.Depth <= 0) ||
                  (wd <= t.DimensionToleranceMm && dd <= t.DimensionToleranceMm);
        var okR = rot <= t.AngleToleranceDegrees;

        var res = Base(r, e, "Column");
        res.PositionDeltaMm = p;
        res.ElevationDeltaMm = Math.Max(eb, et);
        res.WidthDeltaMm = wd;
        res.DepthDeltaMm = dd;
        res.RotationDeltaDeg = rot;
        res.Status = okP && okE && okS && okR
            ? ValidationStatus.Matched
            : !okS ? ValidationStatus.SectionMismatch
            : !okP ? ValidationStatus.PositionMismatch
            : !okE ? ValidationStatus.ElevationMismatch
            : ValidationStatus.RotationMismatch;
        res.Severity = res.Status == ValidationStatus.Matched ? Severity.Info : Severity.Warning;
        res.Confidence = Confidence(new[]
        {
            p / SafeTol(t.PositionToleranceMm),
            Math.Max(eb, et) / SafeTol(t.ElevationToleranceMm),
            SectionRatio(wd, dd, r.Width, r.Depth, e.Width, e.Depth, t.DimensionToleranceMm),
            rot / SafeTol(t.AngleToleranceDegrees)
        });
        res.Message = res.Status == ValidationStatus.Matched
            ? "Column correspondence confirmed by plan point, base/top elevation and orientation within tolerances."
            : $"{res.Status}: Δpos {p:F1} mm; Δelev {Math.Max(eb, et):F1} mm; Δsection {wd:F1}x{dd:F1} mm; Δrot {rot:F1}°.";
        AddDiffs(res);
        return res;
    }

    private static ValidationResult BeamResult(BeamElement r, BeamElement e, ValidationTolerance t)
    {
        var g = BeamGeometry(r, e, t);
        var wd = Math.Abs(r.Width - e.Width);
        var dd = Math.Abs(r.Depth - e.Depth);

        var okP = g.EndpointPlanDeviationMm <= t.PositionToleranceMm;
        var okE = g.EndpointElevationDeviationMm <= t.ElevationToleranceMm;
        var okS = (r.Width <= 0 || r.Depth <= 0 || e.Width <= 0 || e.Depth <= 0) ||
                  (wd <= t.DimensionToleranceMm && dd <= t.DimensionToleranceMm);
        var okL = g.LengthDeltaMm <= t.LengthToleranceMm;
        var okR = g.AngleDeltaDeg <= t.AngleToleranceDegrees;

        var res = Base(r, e, "Beam");
        // For beams, PositionDeltaMm now means the maximum plan endpoint deviation,
        // which is the right geometric quantity for a line correspondence.
        res.PositionDeltaMm = g.EndpointPlanDeviationMm;
        res.ElevationDeltaMm = g.EndpointElevationDeviationMm;
        res.WidthDeltaMm = wd;
        res.DepthDeltaMm = dd;
        res.LengthDeltaMm = g.LengthDeltaMm;
        res.RotationDeltaDeg = g.AngleDeltaDeg;

        res.Status = okP && okE && okS && okL && okR
            ? ValidationStatus.Matched
            : !okS ? ValidationStatus.SectionMismatch
            : !okP ? ValidationStatus.PositionMismatch
            : !okL ? ValidationStatus.GeometryMismatch
            : !okE ? ValidationStatus.ElevationMismatch
            : ValidationStatus.RotationMismatch;
        res.Severity = res.Status == ValidationStatus.Matched ? Severity.Info : Severity.Warning;
        res.Confidence = Confidence(new[]
        {
            g.EndpointPlanDeviationMm / SafeTol(t.PositionToleranceMm),
            g.EndpointElevationDeviationMm / SafeTol(t.ElevationToleranceMm),
            SectionRatio(wd, dd, r.Width, r.Depth, e.Width, e.Depth, t.DimensionToleranceMm),
            g.LengthDeltaMm / SafeTol(t.LengthToleranceMm),
            g.AngleDeltaDeg / SafeTol(t.AngleToleranceDegrees)
        });
        res.Message = res.Status == ValidationStatus.Matched
            ? "Beam correspondence confirmed by line endpoints, elevation, length and orientation within tolerances."
            : $"{res.Status}: endpoint Δ {g.EndpointPlanDeviationMm:F1} mm; Δelev {g.EndpointElevationDeviationMm:F1} mm; ΔL {g.LengthDeltaMm:F1} mm; Δrot {g.AngleDeltaDeg:F1}°.";
        AddDiffs(res);
        return res;
    }

    private static double SectionRatio(
        double dw,
        double dd,
        double rw,
        double rd,
        double ew,
        double ed,
        double tolerance)
    {
        if (rw <= 0 || rd <= 0 || ew <= 0 || ed <= 0)
            return 0.0;
        return Math.Max(dw, dd) / SafeTol(tolerance);
    }

    private static ValidationResult Base(ElementBase r, ElementBase e, string type) => new()
    {
        RevitElementId = r.Id,
        EtabsElementId = e.Id,
        RevitName = r.Name,
        EtabsName = e.Name,
        ElementType = type,
        StoryOrLevel = string.IsNullOrWhiteSpace(r.LevelName) ? e.LevelName : r.LevelName
    };

    private static double Confidence(IEnumerable<double> ratios)
    {
        var values = ratios.Select(x => Math.Min(1.0, Math.Abs(x))).ToList();
        if (values.Count == 0)
            return 100;
        return Math.Max(0, Math.Min(100, 100 * (1 - values.Average())));
    }

    private static void AddDiffs(ValidationResult r)
    {
        r.Differences["Position"] = $"{r.PositionDeltaMm:F1} mm";
        r.Differences["Elevation"] = $"{r.ElevationDeltaMm:F1} mm";
        r.Differences["Width"] = $"{r.WidthDeltaMm:F1} mm";
        r.Differences["Depth"] = $"{r.DepthDeltaMm:F1} mm";
        r.Differences["Length"] = $"{r.LengthDeltaMm:F1} mm";
        r.Differences["Rotation"] = $"{r.RotationDeltaDeg:F1} deg";
    }
}
