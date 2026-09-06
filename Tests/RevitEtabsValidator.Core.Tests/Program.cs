// Lightweight regression tests for the Core comparison engine (ModelComparer).
// No test framework dependency by design, so this always builds and runs with
// just the plain .NET SDK - no Windows Desktop workload, Revit, or ETABS
// installation required. Run with: dotnet run --project Tests/RevitEtabsValidator.Core.Tests
using RevitEtabsValidator.Core.Comparison;
using RevitEtabsValidator.Core.Geometry;
using RevitEtabsValidator.Core.Models;
using RevitEtabsValidator.Core.Validation;

int failures = 0;
int passed = 0;

void Check(string name, bool condition, string detail = "")
{
    if (condition) { passed++; Console.WriteLine($"PASS  {name}"); }
    else { failures++; Console.WriteLine($"FAIL  {name}  {detail}"); }
}

var tol = new ValidationTolerance();
var comparer = new ModelComparer();

// 1. Exact match column
{
    var r = new ColumnElement { Id = "R1", Name = "C1", LevelName = "L1", StartPoint = new Point3D(0, 0, 0), EndPoint = new Point3D(0, 0, 3000), Width = 400, Depth = 600 };
    // ETABS width/depth mapped per project convention: r.Depth -> e.Width, r.Width -> e.Depth
    var e = new ColumnElement { Id = "E1", Name = "C1", LevelName = "L1", StartPoint = new Point3D(0, 0, 0), EndPoint = new Point3D(0, 0, 3000), Width = 600, Depth = 400 };
    var report = comparer.CompareColumns(new[] { r }, new[] { e }, tol);
    Check("Column exact match -> Matched", report.Results.Count == 1 && report.Results[0].Status == ValidationStatus.Matched,
        report.Results.Count > 0 ? report.Results[0].Status.ToString() : "no results");
}

// 2. Column position mismatch beyond tolerance but within the identity window
//    (regression test: identity gate must be wider than the pass/fail tolerance,
//    otherwise this reports as two orphaned Missing entries instead of one
//    PositionMismatch with an actionable delta)
{
    var r = new ColumnElement { Id = "R2", Name = "C2", LevelName = "L1", StartPoint = new Point3D(0, 0, 0), EndPoint = new Point3D(0, 0, 3000), Width = 400, Depth = 600 };
    var e = new ColumnElement { Id = "E2", Name = "C2", LevelName = "L1", StartPoint = new Point3D(0, 60, 0), EndPoint = new Point3D(0, 60, 3000), Width = 600, Depth = 400 };
    var report = comparer.CompareColumns(new[] { r }, new[] { e }, tol);
    var res = report.Results.FirstOrDefault(x => x.RevitElementId == "R2");
    Check("Column offset 60mm (> 25mm tol, within identity window) -> PositionMismatch", res?.Status == ValidationStatus.PositionMismatch, res?.Status.ToString() ?? "null");
    Check("Column offset 60mm -> exactly one report row (not two orphaned Missing rows)", report.Results.Count == 1, report.Results.Count.ToString());
}

// 2b. Column far enough apart that even the widened identity window excludes it
//     -> must still correctly report as Missing on both sides (no false pairing)
{
    var r = new ColumnElement { Id = "R2b", Name = "C2b", LevelName = "L1", StartPoint = new Point3D(0, 0, 0), EndPoint = new Point3D(0, 0, 3000), Width = 400, Depth = 600 };
    var e = new ColumnElement { Id = "E2b", Name = "C2b", LevelName = "L1", StartPoint = new Point3D(0, 5000, 0), EndPoint = new Point3D(0, 5000, 3000), Width = 600, Depth = 400 };
    var report = comparer.CompareColumns(new[] { r }, new[] { e }, tol);
    Check("Column 5000mm apart -> still MissingInEtabs + MissingInRevit (no false pairing)",
        report.Results.Count == 2 &&
        report.Results.Any(x => x.Status == ValidationStatus.MissingInEtabs) &&
        report.Results.Any(x => x.Status == ValidationStatus.MissingInRevit),
        string.Join(",", report.Results.Select(x => x.Status)));
}

// 2c. Short beam rotated 2 deg about its midpoint (> 1 deg tol, but the resulting
//     line offset stays within both the position tolerance and the identity
//     window) -> RotationMismatch, isolated from position/overlap.
{
    const double thetaDeg = 2.0;
    var theta = thetaDeg * Math.PI / 180.0;
    var half = 500.0;
    var midY = 3000.0;
    var e0 = new Point3D(half - half * Math.Cos(theta), midY - half * Math.Sin(theta), 3000 - 250);
    var e1 = new Point3D(half + half * Math.Cos(theta), midY + half * Math.Sin(theta), 3000 - 250);

    var r = new BeamElement { Id = "RB4", Name = "B4", LevelName = "L1", StartPoint = new Point3D(0, midY, 3000), EndPoint = new Point3D(2 * half, midY, 3000), Width = 300, Depth = 500 };
    var e = new BeamElement { Id = "EB4", Name = "B4", LevelName = "L1", StartPoint = e0, EndPoint = e1, Width = 300, Depth = 500 };
    var report = comparer.CompareBeams(new[] { r }, new[] { e }, tol);
    var res = report.Results.FirstOrDefault(x => x.RevitElementId == "RB4");
    Check("Beam rotated 2 deg about midpoint (> 1 deg tol, within identity window) -> RotationMismatch", res?.Status == ValidationStatus.RotationMismatch, res?.Status.ToString() ?? "null");
}

// 3. Column elevation mismatch
{
    var r = new ColumnElement { Id = "R3", Name = "C3", LevelName = "L1", StartPoint = new Point3D(1000, 1000, 0), EndPoint = new Point3D(1000, 1000, 3000), Width = 400, Depth = 600 };
    var e = new ColumnElement { Id = "E3", Name = "C3", LevelName = "L1", StartPoint = new Point3D(1000, 1000, 100), EndPoint = new Point3D(1000, 1000, 3100), Width = 600, Depth = 400 };
    var report = comparer.CompareColumns(new[] { r }, new[] { e }, tol);
    var res = report.Results.FirstOrDefault(x => x.RevitElementId == "R3");
    Check("Column midpoint elevation offset 100mm -> ElevationMismatch", res?.Status == ValidationStatus.ElevationMismatch, res?.Status.ToString() ?? "null");
}

// 4. Ambiguous match: two ETABS columns equidistant from one Revit column
{
    var r = new ColumnElement { Id = "R4", Name = "C4", LevelName = "L1", StartPoint = new Point3D(0, 0, 0), EndPoint = new Point3D(0, 0, 3000), Width = 400, Depth = 400 };
    var e1 = new ColumnElement { Id = "E4a", Name = "C4a", LevelName = "L1", StartPoint = new Point3D(5, 0, 0), EndPoint = new Point3D(5, 0, 3000), Width = 400, Depth = 400 };
    var e2 = new ColumnElement { Id = "E4b", Name = "C4b", LevelName = "L1", StartPoint = new Point3D(-5, 0, 0), EndPoint = new Point3D(-5, 0, 3000), Width = 400, Depth = 400 };
    var report = comparer.CompareColumns(new[] { r }, new[] { e1, e2 }, tol);
    var res = report.Results.FirstOrDefault(x => x.RevitElementId == "R4");
    Check("Two near-identical ETABS candidates -> AmbiguousMatch", res?.Status == ValidationStatus.AmbiguousMatch, res?.Status.ToString() ?? "null");
}

// 5. Missing in ETABS / Missing in Revit
{
    var r = new ColumnElement { Id = "R5", Name = "C5", LevelName = "L1", StartPoint = new Point3D(9000, 9000, 0), EndPoint = new Point3D(9000, 9000, 3000), Width = 400, Depth = 400 };
    var e = new ColumnElement { Id = "E5", Name = "C5", LevelName = "L1", StartPoint = new Point3D(-9000, -9000, 0), EndPoint = new Point3D(-9000, -9000, 3000), Width = 400, Depth = 400 };
    var report = comparer.CompareColumns(new[] { r }, new[] { e }, tol);
    Check("Unrelated columns -> one MissingInEtabs + one MissingInRevit",
        report.Results.Any(x => x.Status == ValidationStatus.MissingInEtabs && x.RevitElementId == "R5") &&
        report.Results.Any(x => x.Status == ValidationStatus.MissingInRevit && x.EtabsElementId == "E5"),
        string.Join(",", report.Results.Select(x => x.Status)));
}

// 6. Beam exact match
{
    var r = new BeamElement { Id = "RB1", Name = "B1", LevelName = "L1", StartPoint = new Point3D(0, 0, 3000), EndPoint = new Point3D(5000, 0, 3000), Width = 300, Depth = 500 };
    var e = new BeamElement { Id = "EB1", Name = "B1", LevelName = "L1", StartPoint = new Point3D(0, 0, 3000 - 250), EndPoint = new Point3D(5000, 0, 3000 - 250), Width = 300, Depth = 500 };
    // ETABS frame reference line is typically at the top/centerline; centerpoint Z difference resolved via +-D/2 nearer convention.
    var report = comparer.CompareBeams(new[] { r }, new[] { e }, tol);
    var res = report.Results.FirstOrDefault(x => x.RevitElementId == "RB1");
    Check("Beam matched with ETABS depth offset resolved by nearer +-D/2", res?.Status == ValidationStatus.Matched, res?.Status.ToString() ?? "null");
}

// 7. Beam reversed endpoints should still match (direction independent)
{
    var r = new BeamElement { Id = "RB2", Name = "B2", LevelName = "L1", StartPoint = new Point3D(0, 1000, 3000), EndPoint = new Point3D(4000, 1000, 3000), Width = 300, Depth = 500 };
    var e = new BeamElement { Id = "EB2", Name = "B2", LevelName = "L1", StartPoint = new Point3D(4000, 1000, 3000 - 250), EndPoint = new Point3D(0, 1000, 3000 - 250), Width = 300, Depth = 500 };
    var report = comparer.CompareBeams(new[] { r }, new[] { e }, tol);
    var res = report.Results.FirstOrDefault(x => x.RevitElementId == "RB2");
    Check("Beam with reversed ETABS endpoints -> still Matched", res?.Status == ValidationStatus.Matched, res?.Status.ToString() ?? "null");
}

// 8. Beam insufficient overlap (50%, below the 80% pass/fail minimum but above the
//    identity gate's looser 40% floor) -> correctly recognized as the same beam
//    and reported as a diagnosable PositionMismatch, not an orphaned Missing pair.
{
    var r = new BeamElement { Id = "RB3", Name = "B3", LevelName = "L1", StartPoint = new Point3D(0, 2000, 3000), EndPoint = new Point3D(4000, 2000, 3000), Width = 300, Depth = 500 };
    var e = new BeamElement { Id = "EB3", Name = "B3", LevelName = "L1", StartPoint = new Point3D(2000, 2000, 3000 - 250), EndPoint = new Point3D(6000, 2000, 3000 - 250), Width = 300, Depth = 500 };
    var report = comparer.CompareBeams(new[] { r }, new[] { e }, tol);
    var res = report.Results.FirstOrDefault(x => x.RevitElementId == "RB3");
    Check("Beam with 50% overlap (< 80% min, within identity floor) -> PositionMismatch (overlap failure surfaced)", res?.Status == ValidationStatus.PositionMismatch, res?.Status.ToString() ?? "null");
}

// 8b. Beam overlap far below even the identity floor -> genuinely different beams,
//     must remain Missing/Missing rather than being falsely paired.
{
    var r = new BeamElement { Id = "RB3b", Name = "B3b", LevelName = "L1", StartPoint = new Point3D(0, 2500, 3000), EndPoint = new Point3D(4000, 2500, 3000), Width = 300, Depth = 500 };
    var e = new BeamElement { Id = "EB3b", Name = "B3b", LevelName = "L1", StartPoint = new Point3D(3900, 2500, 3000 - 250), EndPoint = new Point3D(7900, 2500, 3000 - 250), Width = 300, Depth = 500 };
    var report = comparer.CompareBeams(new[] { r }, new[] { e }, tol);
    Check("Beam with only 2.5% overlap -> still MissingInEtabs + MissingInRevit (no false pairing)",
        report.Results.Count == 2 &&
        report.Results.Any(x => x.Status == ValidationStatus.MissingInEtabs) &&
        report.Results.Any(x => x.Status == ValidationStatus.MissingInRevit),
        string.Join(",", report.Results.Select(x => x.Status)));
}

// 9. Angle wraparound sanity: two beams at 179 deg and -179 deg (period 180) should be near-identical
{
    var d1 = AngleMath.CircularDeltaDegrees(179, -179, 180);
    Check("CircularDeltaDegrees wraps at period boundary (179 vs -179, period 180) -> ~2 deg", Math.Abs(d1 - 2) < 1e-6, d1.ToString());
    var d2 = AngleMath.CircularDeltaDegrees(0, 180, 180);
    Check("CircularDeltaDegrees(0,180,180) -> 0 (collinear reversed)", Math.Abs(d2 - 0) < 1e-6, d2.ToString());
}

// 10. Column section mismatch
{
    var r = new ColumnElement { Id = "R6", Name = "C6", LevelName = "L1", StartPoint = new Point3D(2000, 2000, 0), EndPoint = new Point3D(2000, 2000, 3000), Width = 400, Depth = 600 };
    var e = new ColumnElement { Id = "E6", Name = "C6", LevelName = "L1", StartPoint = new Point3D(2000, 2000, 0), EndPoint = new Point3D(2000, 2000, 3000), Width = 800, Depth = 400 };
    var report = comparer.CompareColumns(new[] { r }, new[] { e }, tol);
    var res = report.Results.FirstOrDefault(x => x.RevitElementId == "R6");
    Check("Column section mismatch (200mm off) -> SectionMismatch", res?.Status == ValidationStatus.SectionMismatch, res?.Status.ToString() ?? "null");
}

// 12. Regression for a Codex review finding on the identity-gate widening fix:
//     an out-of-tolerance Revit column must not "steal" the single ETABS
//     candidate away from a different Revit column that is an EXACT match for
//     it. Both end up with exactly one candidate (the same ETABS id) once the
//     identity window is widened, so a naive "fewest candidates first, then by
//     name" processing order could let the drifted column (if it sorts first
//     alphabetically) claim the ETABS id and falsely report the true exact
//     match as missing. Global best-score-first ordering must prevent this.
{
    var exact = new ColumnElement { Id = "R8-Exact", Name = "ZZZ-Exact", LevelName = "L1", StartPoint = new Point3D(0, 0, 0), EndPoint = new Point3D(0, 0, 3000), Width = 400, Depth = 400 };
    var drifted = new ColumnElement { Id = "R8-Drift", Name = "AAA-Drift", LevelName = "L1", StartPoint = new Point3D(60, 0, 0), EndPoint = new Point3D(60, 0, 3000), Width = 400, Depth = 400 };
    var etabsOnly = new ColumnElement { Id = "E8", Name = "C8", LevelName = "L1", StartPoint = new Point3D(0, 0, 0), EndPoint = new Point3D(0, 0, 3000), Width = 400, Depth = 400 };

    // "drifted" sorts before "exact" alphabetically, so a name-tiebreak-only
    // ordering would let "drifted" claim the shared ETABS candidate first.
    var report = comparer.CompareColumns(new[] { drifted, exact }, new[] { etabsOnly }, tol);
    var exactResult = report.Results.FirstOrDefault(x => x.RevitElementId == "R8-Exact");
    var driftedResult = report.Results.FirstOrDefault(x => x.RevitElementId == "R8-Drift");
    Check("Exact match claims the shared ETABS candidate over a drifted competitor", exactResult?.Status == ValidationStatus.Matched, exactResult?.Status.ToString() ?? "null");
    Check("Drifted competitor correctly reported missing once the exact match wins", driftedResult?.Status == ValidationStatus.MissingInEtabs, driftedResult?.Status.ToString() ?? "null");
}

// 13. Regression for a Codex review finding: a zero PositionToleranceMm (a
//     valid, if extreme, coordination setting accepted by the UI) must not
//     collapse the identity window back to zero. A small drift should still
//     surface as PositionMismatch rather than as two orphaned Missing rows.
{
    var zeroTol = new ValidationTolerance { PositionToleranceMm = 0, AngleToleranceDegrees = 0 };
    var r = new ColumnElement { Id = "R9", Name = "C9", LevelName = "L1", StartPoint = new Point3D(0, 0, 0), EndPoint = new Point3D(0, 0, 3000), Width = 400, Depth = 400 };
    var e = new ColumnElement { Id = "E9", Name = "C9", LevelName = "L1", StartPoint = new Point3D(1, 0, 0), EndPoint = new Point3D(1, 0, 3000), Width = 400, Depth = 400 };
    var report = comparer.CompareColumns(new[] { r }, new[] { e }, zeroTol);
    var res = report.Results.FirstOrDefault(x => x.RevitElementId == "R9");
    Check("1mm drift at PositionToleranceMm=0 -> PositionMismatch (identity window has a floor)", res?.Status == ValidationStatus.PositionMismatch, res?.Status.ToString() ?? "null");
}

// 11. Zero/unknown section should not false-flag SectionMismatch
{
    var r = new ColumnElement { Id = "R7", Name = "C7", LevelName = "L1", StartPoint = new Point3D(3000, 3000, 0), EndPoint = new Point3D(3000, 3000, 3000), Width = 0, Depth = 0 };
    var e = new ColumnElement { Id = "E7", Name = "C7", LevelName = "L1", StartPoint = new Point3D(3000, 3000, 0), EndPoint = new Point3D(3000, 3000, 3000), Width = 0, Depth = 0 };
    var report = comparer.CompareColumns(new[] { r }, new[] { e }, tol);
    var res = report.Results.FirstOrDefault(x => x.RevitElementId == "R7");
    Check("Column with zero/unknown section dims -> Matched (section check skipped)", res?.Status == ValidationStatus.Matched, res?.Status.ToString() ?? "null");
}

Console.WriteLine();
Console.WriteLine($"TOTAL: {passed} passed, {failures} failed");
return failures == 0 ? 0 : 1;
