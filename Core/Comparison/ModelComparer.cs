using RevitEtabsValidator.Core.Geometry;
using RevitEtabsValidator.Core.Models;
using RevitEtabsValidator.Core.Validation;

namespace RevitEtabsValidator.Core.Comparison;

public sealed class ModelComparer
{
    public ValidationReport CompareColumns(IReadOnlyList<ColumnElement> revit, IReadOnlyList<ColumnElement> etabs, ValidationTolerance tol)
        => CompareColumnsInternal(revit, etabs, tol);

    public ValidationReport CompareBeams(IReadOnlyList<BeamElement> revit, IReadOnlyList<BeamElement> etabs, ValidationTolerance tol)
        => CompareBeamsInternal(revit, etabs, tol);

    private static ValidationReport CompareColumnsInternal(
        IReadOnlyList<ColumnElement> revit,
        IReadOnlyList<ColumnElement> etabs,
        ValidationTolerance tol)
    {
        var report = new ValidationReport();
        var remaining = new HashSet<string>(etabs.Select(x => x.Id), StringComparer.OrdinalIgnoreCase);

        foreach (var r in revit)
        {
            var candidates = etabs
                .Where(e => remaining.Contains(e.Id))
                // NOTE: deliberately not filtering by exact LevelName match here.
                // Revit level names and ETABS story names are not the same text
                // in general (confirmed for this project), so an exact-name
                // filter finds zero candidates and reports everything as
                // missing even when the model is correct. Elevation is already
                // part of ColumnMatchScore below and is a far better level
                // discriminator, since it compares real Z coordinates that ARE
                // shared between the two models.
                .Select(e => (Element: e, Score: ColumnMatchScore(r, e, tol)))
                .OrderBy(x => x.Score)
                .ToList();

            if (candidates.Count == 0)
            {
                report.Results.Add(new ValidationResult
                {
                    RevitElementId = r.Id,
                    RevitName = r.Name,
                    ElementType = "Column",
                    StoryOrLevel = r.LevelName,
                    Status = ValidationStatus.MissingInEtabs,
                    Severity = Severity.Critical,
                    Confidence = 0,
                    Message = $"Column exists in Revit on level '{r.LevelName}' but no ETABS column remains on the same level."
                });
                continue;
            }

            var best = candidates[0];
            if (candidates.Count > 1 && Math.Abs(candidates[1].Score - best.Score) < Math.Max(0, tol.AmbiguousScoreGap))
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
                    Message = $"Ambiguous column match on level '{r.LevelName}'. The two best ETABS candidates have nearly identical matching scores."
                });
                continue;
            }

            report.Results.Add(ColumnResult(r, best.Element, tol));
            remaining.Remove(best.Element.Id);
        }

        foreach (var e in etabs.Where(x => remaining.Contains(x.Id)))
        {
            report.Results.Add(new ValidationResult
            {
                EtabsElementId = e.Id,
                EtabsName = e.Name,
                ElementType = "Column",
                StoryOrLevel = e.LevelName,
                Status = ValidationStatus.MissingInRevit,
                Severity = Severity.Critical,
                Confidence = 0,
                Message = $"ETABS column exists on level '{e.LevelName}' with no corresponding Revit column."
            });
        }

        return report;
    }

    private static ValidationReport CompareBeamsInternal(
        IReadOnlyList<BeamElement> revit,
        IReadOnlyList<BeamElement> etabs,
        ValidationTolerance tol)
    {
        var report = new ValidationReport();
        var remaining = new HashSet<string>(etabs.Select(x => x.Id), StringComparer.OrdinalIgnoreCase);

        foreach (var r in revit)
        {
            var candidates = etabs
                .Where(e => remaining.Contains(e.Id))
                // Same reasoning as CompareColumnsInternal above - no exact
                // level-name filter; elevation inside BeamMatchScore does this
                // job correctly using real coordinates instead of text names.
                .Select(e => (Element: e, Score: BeamMatchScore(r, e, tol)))
                .OrderBy(x => x.Score)
                .ToList();

            if (candidates.Count == 0)
            {
                report.Results.Add(new ValidationResult
                {
                    RevitElementId = r.Id,
                    RevitName = r.Name,
                    ElementType = "Beam",
                    StoryOrLevel = r.LevelName,
                    Status = ValidationStatus.MissingInEtabs,
                    Severity = Severity.Critical,
                    Confidence = 0,
                    Message = $"Beam exists in Revit on level '{r.LevelName}' but no ETABS beam remains on the same level."
                });
                continue;
            }

            var best = candidates[0];
            if (candidates.Count > 1 && Math.Abs(candidates[1].Score - best.Score) < Math.Max(0, tol.AmbiguousScoreGap))
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
                    Message = $"Ambiguous beam match on level '{r.LevelName}'. The two best ETABS candidates have nearly identical matching scores."
                });
                continue;
            }

            report.Results.Add(BeamResult(r, best.Element, tol));
            remaining.Remove(best.Element.Id);
        }

        foreach (var e in etabs.Where(x => remaining.Contains(x.Id)))
        {
            report.Results.Add(new ValidationResult
            {
                EtabsElementId = e.Id,
                EtabsName = e.Name,
                ElementType = "Beam",
                StoryOrLevel = e.LevelName,
                Status = ValidationStatus.MissingInRevit,
                Severity = Severity.Critical,
                Confidence = 0,
                Message = $"ETABS beam exists on level '{e.LevelName}' with no corresponding Revit beam."
            });
        }

        return report;
    }

    private static double SafeTol(double value) => Math.Max(Math.Abs(value), 1e-9);

    private static double ColumnMatchScore(ColumnElement r, ColumnElement e, ValidationTolerance t)
    {
        var pos = r.CenterPoint.PlanDistanceTo(e.CenterPoint) / SafeTol(t.PositionToleranceMm);
        var elev = (Math.Abs(r.BaseElevation - e.BaseElevation) + Math.Abs(r.TopElevation - e.TopElevation))
                   / (2.0 * SafeTol(t.ElevationToleranceMm));
        var sec = (Math.Abs(r.Width - e.Width) + Math.Abs(r.Depth - e.Depth))
                  / (2.0 * SafeTol(t.DimensionToleranceMm));
        var rot = AngleMath.CircularDeltaDegrees(r.Rotation, e.Rotation, 180) / SafeTol(t.AngleToleranceDegrees);

        // Geometry/position dominates identity; section differences are deliberately lower weight
        // because a correctly located element with a wrong section is still the correct counterpart.
        return 4.0 * pos + 2.0 * elev + 0.5 * sec + 0.5 * rot;
    }

    private static double BeamMatchScore(BeamElement r, BeamElement e, ValidationTolerance t)
    {
        var pos = r.CenterPoint.PlanDistanceTo(e.CenterPoint) / SafeTol(t.PositionToleranceMm);
        var elev = Math.Max(Math.Abs(r.StartPoint.Z - e.StartPoint.Z), Math.Abs(r.EndPoint.Z - e.EndPoint.Z))
                   / SafeTol(t.ElevationToleranceMm);
        var len = Math.Abs(r.LengthMm - e.LengthMm) / SafeTol(t.LengthToleranceMm);
        var rot = AngleMath.CircularDeltaDegrees(r.Rotation, e.Rotation, 180) / SafeTol(t.AngleToleranceDegrees);
        var sec = (Math.Abs(r.Width - e.Width) + Math.Abs(r.Depth - e.Depth))
                  / (2.0 * SafeTol(t.DimensionToleranceMm));

        return 4.0 * pos + 1.5 * elev + 1.5 * len + 0.5 * rot + 0.5 * sec;
    }

    private static ValidationResult ColumnResult(ColumnElement r, ColumnElement e, ValidationTolerance t)
    {
        var p = r.CenterPoint.PlanDistanceTo(e.CenterPoint);
        var eb = Math.Abs(r.BaseElevation - e.BaseElevation);
        var et = Math.Abs(r.TopElevation - e.TopElevation);
        var wd = Math.Abs(r.Width - e.Width);
        var dd = Math.Abs(r.Depth - e.Depth);
        var rot = AngleMath.CircularDeltaDegrees(r.Rotation, e.Rotation, 180);

        var okP = p <= t.PositionToleranceMm;
        var okE = eb <= t.ElevationToleranceMm && et <= t.ElevationToleranceMm;
        var okS = wd <= t.DimensionToleranceMm && dd <= t.DimensionToleranceMm;
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
        res.Severity = res.Status == ValidationStatus.Matched
            ? Severity.Info
            : res.Status == ValidationStatus.SectionMismatch ? Severity.Error : Severity.Warning;
        res.Confidence = Confidence(new[]
        {
            p / SafeTol(t.PositionToleranceMm),
            Math.Max(eb, et) / SafeTol(t.ElevationToleranceMm),
            Math.Max(wd, dd) / SafeTol(t.DimensionToleranceMm),
            rot / SafeTol(t.AngleToleranceDegrees)
        });
        res.Message = res.Status == ValidationStatus.Matched
            ? "Column matches within configured tolerances."
            : $"{res.Status}: Revit {r.Width:F0}x{r.Depth:F0}, ETABS {e.Width:F0}x{e.Depth:F0}; Δpos {p:F1} mm; Δrot {rot:F1}°.";
        AddDiffs(res);
        return res;
    }

    private static ValidationResult BeamResult(BeamElement r, BeamElement e, ValidationTolerance t)
    {
        var p = r.CenterPoint.PlanDistanceTo(e.CenterPoint);
        var elev = Math.Max(Math.Abs(r.StartPoint.Z - e.StartPoint.Z), Math.Abs(r.EndPoint.Z - e.EndPoint.Z));
        var wd = Math.Abs(r.Width - e.Width);
        var dd = Math.Abs(r.Depth - e.Depth);
        var len = Math.Abs(r.LengthMm - e.LengthMm);
        var rot = AngleMath.CircularDeltaDegrees(r.Rotation, e.Rotation, 180);

        var okP = p <= t.PositionToleranceMm;
        var okE = elev <= t.ElevationToleranceMm;
        var okS = wd <= t.DimensionToleranceMm && dd <= t.DimensionToleranceMm;
        var okL = len <= t.LengthToleranceMm;
        var okR = rot <= t.AngleToleranceDegrees;

        var res = Base(r, e, "Beam");
        res.PositionDeltaMm = p;
        res.ElevationDeltaMm = elev;
        res.WidthDeltaMm = wd;
        res.DepthDeltaMm = dd;
        res.LengthDeltaMm = len;
        res.RotationDeltaDeg = rot;
        res.Status = okP && okE && okS && okL && okR
            ? ValidationStatus.Matched
            : !okS ? ValidationStatus.SectionMismatch
            : !okP ? ValidationStatus.PositionMismatch
            : !okL ? ValidationStatus.GeometryMismatch
            : !okE ? ValidationStatus.ElevationMismatch
            : ValidationStatus.RotationMismatch;
        res.Severity = res.Status == ValidationStatus.Matched
            ? Severity.Info
            : res.Status == ValidationStatus.SectionMismatch ? Severity.Error : Severity.Warning;
        res.Confidence = Confidence(new[]
        {
            p / SafeTol(t.PositionToleranceMm),
            elev / SafeTol(t.ElevationToleranceMm),
            Math.Max(wd, dd) / SafeTol(t.DimensionToleranceMm),
            len / SafeTol(t.LengthToleranceMm),
            rot / SafeTol(t.AngleToleranceDegrees)
        });
        res.Message = res.Status == ValidationStatus.Matched
            ? "Beam matches within configured tolerances."
            : $"{res.Status}: Revit {r.Width:F0}x{r.Depth:F0}, ETABS {e.Width:F0}x{e.Depth:F0}; Δpos {p:F1} mm; ΔL {len:F1} mm.";
        AddDiffs(res);
        return res;
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
