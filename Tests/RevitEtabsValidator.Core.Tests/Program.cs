// Lightweight regression tests for the Core comparison engine (ModelComparer).
// No test framework dependency by design, so this always builds and runs with
// just the plain .NET SDK - no Windows Desktop workload, Revit, or ETABS
// installation required. Run with: dotnet run --project Tests/RevitEtabsValidator.Core.Tests
using System.Diagnostics;
using RevitEtabsValidator.Core.Comparison;
using RevitEtabsValidator.Core.Geometry;
using RevitEtabsValidator.Core.Models;
using RevitEtabsValidator.Core.Validation;
using RevitEtabsValidator.ETABS;

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

// 14. EtabsInstallationScanner: any installed ETABS version should be usable, not
//     just a hardcoded one. Simulate two Program Files roots holding ETABS 21 and
//     ETABS 24 and confirm the newest (24) wins even though 21 sorts first.
// Paths are built via Path.Combine throughout (not backslash literals) so this test
// is correct regardless of the host OS's directory separator.
{
    var progFiles = Path.Combine("fakeroot", "Program Files");
    var progFilesX86 = Path.Combine("fakeroot", "Program Files (x86)");
    var csiRoot = Path.Combine(progFiles, "Computers and Structures");
    var csiRootX86 = Path.Combine(progFilesX86, "Computers and Structures");

    var fakeFs = new Dictionary<string, string[]>
    {
        [csiRoot] = new[] { "ETABS 21", "ETABS 24" },
        [csiRootX86] = Array.Empty<string>()
    };
    string[] Dirs(string root) => (fakeFs.TryGetValue(root, out var names) ? names : Array.Empty<string>())
        .Select(n => Path.Combine(root, n)).ToArray();
    bool DirExists(string p) => fakeFs.ContainsKey(p);
    bool FileExists(string dllPath) => true; // every simulated version folder "has" ETABSv1.dll

    var roots = new[] { progFiles, progFilesX86 };
    var result = EtabsInstallationScanner.FindNewestApiDll(roots, DirExists, Dirs, FileExists);
    Check("EtabsInstallationScanner picks the newest of multiple installed versions",
        result == Path.Combine(csiRoot, "ETABS 24", "ETABSv1.dll"),
        result ?? "null");
}

// 15. EtabsInstallationScanner: a version folder without ETABSv1.dll actually present
//     (a partial/broken install) must be skipped in favor of one that has it.
{
    var progFiles = Path.Combine("fakeroot", "Program Files");
    var csiRoot = Path.Combine(progFiles, "Computers and Structures");
    var names = new[] { "ETABS 21", "ETABS 24" };
    string[] Dirs(string root) => names.Select(n => Path.Combine(root, n)).ToArray();
    bool DirExists(string p) => p == csiRoot;
    bool FileExists(string dllPath) => !dllPath.Contains("ETABS 24"); // 24's dll is "missing"

    var result = EtabsInstallationScanner.FindNewestApiDll(new[] { progFiles }, DirExists, Dirs, FileExists);
    Check("EtabsInstallationScanner skips a version folder with no ETABSv1.dll present",
        result == Path.Combine(csiRoot, "ETABS 21", "ETABSv1.dll"), result ?? "null");
}

// 16. EtabsInstallationScanner: nothing installed anywhere -> null, not an exception.
{
    var progFiles = Path.Combine("fakeroot", "Program Files");
    var result = EtabsInstallationScanner.FindNewestApiDll(new[] { progFiles }, _ => false, _ => Array.Empty<string>(), _ => false);
    Check("EtabsInstallationScanner returns null when no ETABS install is found", result is null, result ?? "non-null");
}

// 17. ParseVersion pulls the version number out of names CSI's installer uses.
{
    Check("ParseVersion('ETABS 22') -> 22", EtabsInstallationScanner.ParseVersion("ETABS 22") == 22);
    Check("ParseVersion('ETABS 22 Ultimate') -> 22", EtabsInstallationScanner.ParseVersion("ETABS 22 Ultimate") == 22);
    Check("ParseVersion('ETABS') -> 0 (no digits, safe fallback)", EtabsInstallationScanner.ParseVersion("ETABS") == 0);
}

// ===================================================================
// Engineering-logic verification requested directly: beam elevation both
// sign conventions, column midpoint (not base/top), section-axis swap
// direction, and proof that no coordinate is silently shifted by default.
// ===================================================================

// 18. Beam elevation: ETABS frame reference is ABOVE the Revit beam midpoint by
//     exactly depth/2 (the "+D/2" convention) -> must resolve to Matched, and the
//     reported ElevationDeltaMm must be ~0, not the full D/2 offset.
{
    var r = new BeamElement { Id = "RBe1", Name = "Be1", LevelName = "L1", StartPoint = new Point3D(0, 100, 3000), EndPoint = new Point3D(4000, 100, 3000), Width = 300, Depth = 600 };
    // Revit midpoint Z = 3000. ETABS reference Z + depth/2 (=300) must equal 3000 -> ETABS reference Z = 2700.
    var e = new BeamElement { Id = "EBe1", Name = "Be1", LevelName = "L1", StartPoint = new Point3D(0, 100, 2700), EndPoint = new Point3D(4000, 100, 2700), Width = 300, Depth = 600 };
    var report = comparer.CompareBeams(new[] { r }, new[] { e }, tol);
    var res = report.Results.FirstOrDefault(x => x.RevitElementId == "RBe1");
    Check("Beam elevation: ETABS Z + D/2 convention -> Matched with ~0 delta", res?.Status == ValidationStatus.Matched && res.ElevationDeltaMm < 1e-6, $"{res?.Status} delta={res?.ElevationDeltaMm}");
}

// 19. Beam elevation: ETABS frame reference is BELOW the Revit beam midpoint by
//     exactly depth/2 (the "-D/2" convention) -> must ALSO resolve to Matched.
//     Both sign conventions must be accepted since ETABS beams may be modeled at
//     either the top or the bottom reference line depending on the project.
{
    var r = new BeamElement { Id = "RBe2", Name = "Be2", LevelName = "L1", StartPoint = new Point3D(0, 200, 3000), EndPoint = new Point3D(4000, 200, 3000), Width = 300, Depth = 600 };
    // ETABS reference Z - depth/2 (=300) must equal 3000 -> ETABS reference Z = 3300.
    var e = new BeamElement { Id = "EBe2", Name = "Be2", LevelName = "L1", StartPoint = new Point3D(0, 200, 3300), EndPoint = new Point3D(4000, 200, 3300), Width = 300, Depth = 600 };
    var report = comparer.CompareBeams(new[] { r }, new[] { e }, tol);
    var res = report.Results.FirstOrDefault(x => x.RevitElementId == "RBe2");
    Check("Beam elevation: ETABS Z - D/2 convention -> Matched with ~0 delta", res?.Status == ValidationStatus.Matched && res.ElevationDeltaMm < 1e-6, $"{res?.Status} delta={res?.ElevationDeltaMm}");
}

// 20. Beam elevation: a genuinely wrong elevation (neither +D/2 nor -D/2 explains
//     it) must NOT be silently accepted as a false match.
{
    var r = new BeamElement { Id = "RBe3", Name = "Be3", LevelName = "L1", StartPoint = new Point3D(0, 300, 3000), EndPoint = new Point3D(4000, 300, 3000), Width = 300, Depth = 600 };
    // ETABS reference Z chosen so neither +300 nor -300 lands near 3000 (nearest candidate is 900mm off).
    var e = new BeamElement { Id = "EBe3", Name = "Be3", LevelName = "L1", StartPoint = new Point3D(0, 300, 3900), EndPoint = new Point3D(4000, 300, 3900), Width = 300, Depth = 600 };
    var report = comparer.CompareBeams(new[] { r }, new[] { e }, tol);
    var res = report.Results.FirstOrDefault(x => x.RevitElementId == "RBe3");
    Check("Beam elevation: neither +D/2 nor -D/2 explains a 900mm gap -> not falsely Matched", res?.Status != ValidationStatus.Matched, res?.Status.ToString() ?? "null");
}

// 21. Column elevation: must compare MIDPOINT to MIDPOINT, not base-to-base or
//     top-to-midpoint. Two columns with wildly different base/top elevations but
//     the SAME midpoint must Match; this specifically guards against a regression
//     to "compare base elevations" or "compare Revit top to ETABS midpoint".
{
    // Revit column: base=0, top=6000 -> midpoint=3000.
    var r = new ColumnElement { Id = "RCe1", Name = "Ce1", LevelName = "L1", StartPoint = new Point3D(500, 500, 0), EndPoint = new Point3D(500, 500, 6000), Width = 400, Depth = 400 };
    // ETABS column: base=2900, top=3100 -> midpoint=3000 too, even though base/top
    // individually differ from Revit's by thousands of mm.
    var e = new ColumnElement { Id = "ECe1", Name = "Ce1", LevelName = "L1", StartPoint = new Point3D(500, 500, 2900), EndPoint = new Point3D(500, 500, 3100), Width = 400, Depth = 400 };
    var report = comparer.CompareColumns(new[] { r }, new[] { e }, tol);
    var res = report.Results.FirstOrDefault(x => x.RevitElementId == "RCe1");
    Check("Column elevation: matching midpoints -> Matched despite very different base/top", res?.Status == ValidationStatus.Matched, res?.Status.ToString() ?? "null");
    Check("Column elevation: reported delta is the midpoint delta (~0), not a base/top delta", res != null && res.ElevationDeltaMm < 1e-6, res?.ElevationDeltaMm.ToString() ?? "null");
}

// 22. Section axis mapping: Revit b (Width) must compare against ETABS Depth, and
//     Revit h (Depth) against ETABS Width - the required "b=Depth, h=Width" swap.
//     This column is 300(b) x 600(h) in Revit and correctly modeled as
//     Width=600/Depth=300 in ETABS (the swapped axes) - if the swap direction were
//     ever accidentally reversed to a same-name comparison, this would incorrectly
//     report a 300mm section mismatch instead of Matched.
{
    var r = new ColumnElement { Id = "RCs1", Name = "Cs1", LevelName = "L1", StartPoint = new Point3D(1000, 1000, 0), EndPoint = new Point3D(1000, 1000, 3000), Width = 300, Depth = 600 };
    var e = new ColumnElement { Id = "ECs1", Name = "Cs1", LevelName = "L1", StartPoint = new Point3D(1000, 1000, 0), EndPoint = new Point3D(1000, 1000, 3000), Width = 600, Depth = 300 };
    var report = comparer.CompareColumns(new[] { r }, new[] { e }, tol);
    var res = report.Results.FirstOrDefault(x => x.RevitElementId == "RCs1");
    Check("Section mapping: Revit b(300)/h(600) vs correctly-swapped ETABS Width(600)/Depth(300) -> Matched",
        res?.Status == ValidationStatus.Matched, res?.Status.ToString() ?? "null");
    // And the inverse: if ETABS were modeled with the SAME (unswapped) axis values as
    // Revit's b/h, that is actually the wrong convention for this project and must
    // be caught as a SectionMismatch, proving the comparer really does apply the
    // swap rather than happening to ignore section entirely.
    var eWrong = new ColumnElement { Id = "ECs2", Name = "Cs2", LevelName = "L1", StartPoint = new Point3D(2000, 1000, 0), EndPoint = new Point3D(2000, 1000, 3000), Width = 300, Depth = 600 };
    var rWrong = new ColumnElement { Id = "RCs2", Name = "Cs2", LevelName = "L1", StartPoint = new Point3D(2000, 1000, 0), EndPoint = new Point3D(2000, 1000, 3000), Width = 300, Depth = 600 };
    var reportWrong = comparer.CompareColumns(new[] { rWrong }, new[] { eWrong }, tol);
    var resWrong = reportWrong.Results.FirstOrDefault(x => x.RevitElementId == "RCs2");
    Check("Section mapping: same-name (unswapped) axis values for a 300x600 section -> SectionMismatch (proves the swap is actually applied, not a no-op)",
        resWrong?.Status == ValidationStatus.SectionMismatch, resWrong?.Status.ToString() ?? "null");
}

// 23. Coordinate system integrity: identical Revit/ETABS coordinates (down to
//     fractional mm) must produce exactly zero deltas - proof that no hidden
//     translation, rounding, or unit-conversion offset is applied when both sides
//     already agree, for both columns and beams.
{
    var rc = new ColumnElement { Id = "RCc1", Name = "Cc1", LevelName = "L1", StartPoint = new Point3D(12345.678, -9876.543, 0), EndPoint = new Point3D(12345.678, -9876.543, 3000), Width = 400, Depth = 400 };
    var ec = new ColumnElement { Id = "ECc1", Name = "Cc1", LevelName = "L1", StartPoint = new Point3D(12345.678, -9876.543, 0), EndPoint = new Point3D(12345.678, -9876.543, 3000), Width = 400, Depth = 400 };
    var columnReport = comparer.CompareColumns(new[] { rc }, new[] { ec }, tol);
    var columnRes = columnReport.Results.FirstOrDefault(x => x.RevitElementId == "RCc1");
    Check("Coordinate integrity: identical column coordinates -> exactly zero position/elevation delta (no hidden shift)",
        columnRes != null && columnRes.PositionDeltaMm == 0.0 && columnRes.ElevationDeltaMm == 0.0,
        $"pos={columnRes?.PositionDeltaMm} elev={columnRes?.ElevationDeltaMm}");
}

// ===================================================================
// Performance: measure the Core matching engine at the actual reported model
// scale (911 Revit beams / 16,652 ETABS beams, ~18.3x ratio) to get real numbers
// instead of only analysis. This isolates the matching ALGORITHM's own cost from
// the ETABS COM read cost, which cannot be measured without a live ETABS install.
// ===================================================================
{
    const int floors = 11;
    const int bx = 10, by = 5; // 50 grid points per floor
    const double bay = 6000.0;
    const double floorHeight = 3500.0;

    var revitPerf = new List<BeamElement>();
    var etabsPerf = new List<BeamElement>();
    int rid = 0, eid = 0;

    for (var f = 0; f < floors; f++)
    {
        var z = f * floorHeight + 3000.0;
        var levelName = $"Level {f}";

        void AddPair(double x0, double y0, double x1, double y1)
        {
            revitPerf.Add(new BeamElement { Id = $"PR{rid}", Name = $"PR{rid++}", LevelName = levelName, StartPoint = new Point3D(x0, y0, z), EndPoint = new Point3D(x1, y1, z), Width = 300, Depth = 600 });
            // ETABS frame reference line at top-of-beam convention (z - depth/2), matching real-world modeling.
            etabsPerf.Add(new BeamElement { Id = $"PE{eid}", Name = $"PE{eid++}", LevelName = levelName, StartPoint = new Point3D(x0, y0, z - 300), EndPoint = new Point3D(x1, y1, z - 300), Width = 300, Depth = 600 });
        }

        for (var j = 0; j < by; j++)
            for (var i = 0; i < bx - 1; i++)
                AddPair(i * bay, j * bay, (i + 1) * bay, j * bay);

        for (var i = 0; i < bx; i++)
            for (var j = 0; j < by - 1; j++)
                AddPair(i * bay, j * bay, i * bay, (j + 1) * bay);
    }

    // Pad the ETABS side with unrelated "extra" frames (secondary framing/bracing
    // with no Revit counterpart - a realistic reason ETABS commonly has far more
    // frame objects than Revit has physical beams) up to the real reported ratio.
    var rand = new Random(12345);
    var targetEtabsTotal = (int)Math.Round(revitPerf.Count * (16652.0 / 911.0));
    var extraNeeded = Math.Max(0, targetEtabsTotal - etabsPerf.Count);
    for (var k = 0; k < extraNeeded; k++)
    {
        var f = k % floors;
        var z = f * floorHeight + 3000.0 - 300.0;
        var x = rand.NextDouble() * (bx - 1) * bay + 1500.0;
        var y = rand.NextDouble() * (by - 1) * bay + 1500.0;
        etabsPerf.Add(new BeamElement { Id = $"PX{k}", Name = $"PX{k}", LevelName = $"Level {f}", StartPoint = new Point3D(x, y, z), EndPoint = new Point3D(x + 400, y, z), Width = 200, Depth = 300 });
    }

    Console.WriteLine();
    Console.WriteLine($"PERF setup: {revitPerf.Count} Revit beams, {etabsPerf.Count} ETABS beams across {floors} floors (ratio {(double)etabsPerf.Count / revitPerf.Count:F1}x, target was 16652/911={16652.0 / 911.0:F1}x)");

    var swAll = Stopwatch.StartNew();
    var allFloorsReport = comparer.CompareBeams(revitPerf, etabsPerf, tol);
    swAll.Stop();
    var matchedAll = allFloorsReport.Results.Count(x => x.Status == ValidationStatus.Matched);
    Console.WriteLine($"PERF all-floors-at-once (worst case for a Z-blind spatial index): {swAll.ElapsedMilliseconds} ms, {allFloorsReport.Results.Count} results, {matchedAll} matched");
    Check("Performance: all-floors-at-once comparison of ~935 Revit vs ~16650 ETABS beams completes well under 30s (no O(N*M) blowup)",
        swAll.ElapsedMilliseconds < 30000, $"{swAll.ElapsedMilliseconds} ms");
    Check("Performance: matching correctness holds at scale - every Revit beam in the grid found its true ETABS match",
        matchedAll == revitPerf.Count, $"{matchedAll} of {revitPerf.Count} matched");

    var swPerFloor = Stopwatch.StartNew();
    var perFloorMatched = 0;
    var perFloorTotal = 0;
    for (var f = 0; f < floors; f++)
    {
        var levelName = $"Level {f}";
        var rSubset = revitPerf.Where(x => x.LevelName == levelName).ToList();
        var eSubset = etabsPerf.Where(x => x.LevelName == levelName).ToList();
        var floorReport = comparer.CompareBeams(rSubset, eSubset, tol);
        perFloorTotal += floorReport.Results.Count;
        perFloorMatched += floorReport.Results.Count(x => x.Status == ValidationStatus.Matched);
    }
    swPerFloor.Stop();
    Console.WriteLine($"PERF per-floor scoped (11 separate calls, as the UI's floor-scope filter already does): {swPerFloor.ElapsedMilliseconds} ms, {perFloorTotal} results, {perFloorMatched} matched");
    Check("Performance: per-floor scoping produces the same matched count as the all-at-once run",
        perFloorMatched == matchedAll, $"per-floor={perFloorMatched} vs all-at-once={matchedAll}");
    var speedupNote = swAll.ElapsedMilliseconds > 0
        ? $"{(double)swAll.ElapsedMilliseconds / Math.Max(1, swPerFloor.ElapsedMilliseconds):F1}x"
        : "n/a (both under timer resolution)";
    Console.WriteLine($"PERF cross-floor spatial-index bleed cost (Bug #5 from the QA report): all-at-once was {speedupNote} the cost of per-floor scoping");
}

// ---------------------------------------------------------------------------
// Floor-plan projection (PlanProjection).
//
// These exist because of a real, shipped bug: the plan canvas was built in raw
// model millimetres while every member glyph was sized as if the canvas were
// screen pixels (column radius 6, beam stroke 3, label font 10 = 6 mm, 3 mm and
// 10 mm IN THE BUILDING). Fitting a real ~120 m floor into a ~1200 px viewport
// gives a fit scale near 0.01, so columns rendered at 0.06 px and beams at
// 0.03 px: drawn correctly, invisible at every zoom. Three separate rounds of
// "fixes" to that view could not show anything while this held, so the
// arithmetic that decides visibility is now pinned down here.
// ---------------------------------------------------------------------------
{
    // Glyph sizes the plan view draws with, in canvas units (see MainWindow's
    // AddColumnVisual/AddBeamVisual/AddPlanLabel).
    const double columnGlyphRadius = 6.0;
    const double beamGlyphStroke = 3.0;
    // Representative plan viewport in device-independent pixels.
    const double viewportPx = 1200.0;

    // Reproduces FitPlan_Click: fit the canvas into the viewport, 6% margin.
    static double FitScale(PlanProjection projection, double viewport)
        => Math.Min(viewport / projection.CanvasWidth, viewport / projection.CanvasHeight) * 0.94;

    // A real basement floor: 126 m x 96 m, columns on a 6 m grid.
    const double bigFloorMaxX = 126000;
    const double bigFloorMaxY = 96000;
    var bigFloor = new List<Point3D>();
    for (var x = 0.0; x <= bigFloorMaxX; x += 6000)
    for (var y = 0.0; y <= bigFloorMaxY; y += 6000)
        bigFloor.Add(new Point3D(x, y, 0));

    var bigProjection = PlanProjection.Create(bigFloor);
    Check("Plan projection: a real 126x96 m floor produces a projection", bigProjection != null);

    if (bigProjection != null)
    {
        var fit = FitScale(bigProjection, viewportPx);
        var columnPx = columnGlyphRadius * 2.0 * fit;
        var beamPx = beamGlyphStroke * fit;
        Console.WriteLine($"PLAN 126x96 m floor: fit scale {fit:F3}, column glyph {columnPx:F2} px, beam stroke {beamPx:F2} px");

        // The regression itself: with the old raw-millimetre canvas this fit scale
        // was ~0.01 and these came out at 0.12 px / 0.03 px.
        Check("Plan projection: column glyph on a real-size floor renders at a visible size (>= 3 px across)",
            columnPx >= 3.0, $"{columnPx:F3} px");
        Check("Plan projection: beam stroke on a real-size floor renders at a visible width (>= 1 px)",
            beamPx >= 1.0, $"{beamPx:F3} px");

        // Corner mapping, including the Y flip (model Y up, canvas Y down).
        var topLeft = bigProjection.Map(new Point3D(0, bigFloorMaxY, 0));
        var bottomRight = bigProjection.Map(new Point3D(bigFloorMaxX, 0, 0));
        Check("Plan projection: model top-left maps to the canvas margin corner",
            Math.Abs(topLeft.X - PlanProjection.Margin) < 1e-6 && Math.Abs(topLeft.Y - PlanProjection.Margin) < 1e-6,
            $"({topLeft.X:F3}, {topLeft.Y:F3})");
        Check("Plan projection: model bottom-right maps to the far canvas corner (Y is flipped, not mirrored)",
            Math.Abs(bottomRight.X - (bigProjection.CanvasWidth - PlanProjection.Margin)) < 1e-6 &&
            Math.Abs(bottomRight.Y - (bigProjection.CanvasHeight - PlanProjection.Margin)) < 1e-6,
            $"({bottomRight.X:F3}, {bottomRight.Y:F3})");

        // Aspect ratio must survive normalization or the plan would be distorted.
        var worldAspect = bigProjection.WorldWidthMm / bigProjection.WorldHeightMm;
        var canvasAspect = (bigProjection.CanvasWidth - 2 * PlanProjection.Margin) /
                           (bigProjection.CanvasHeight - 2 * PlanProjection.Margin);
        Check("Plan projection: normalization preserves the plan's aspect ratio (no stretching)",
            Math.Abs(worldAspect - canvasAspect) < 1e-9, $"world {worldAspect:F6} vs canvas {canvasAspect:F6}");
    }

    // The small test model from the earlier session (4 columns over ~6 m) has to
    // stay visible too - the same bug made it a faint smudge rather than nothing.
    var smallProjection = PlanProjection.Create(new[]
    {
        new Point3D(0, 0, 0), new Point3D(6000, 0, 0),
        new Point3D(0, 6000, 0), new Point3D(6000, 6000, 0)
    });
    if (smallProjection != null)
    {
        var smallColumnPx = columnGlyphRadius * 2.0 * FitScale(smallProjection, viewportPx);
        Check("Plan projection: column glyph on a small 6x6 m test model is also visible",
            smallColumnPx >= 3.0, $"{smallColumnPx:F3} px");
    }

    // Scale invariance is the actual point: a 6 m model and a 400 m model must
    // both land on usable glyph sizes, because the canvas is normalized.
    var hugeProjection = PlanProjection.Create(new[]
    {
        new Point3D(0, 0, 0), new Point3D(400000, 0, 0), new Point3D(0, 400000, 0)
    });
    if (smallProjection != null && hugeProjection != null)
    {
        Check("Plan projection: fit scale is independent of building size (6 m vs 400 m models agree)",
            Math.Abs(FitScale(smallProjection, viewportPx) - FitScale(hugeProjection, viewportPx)) < 1e-9,
            $"{FitScale(smallProjection, viewportPx):F6} vs {FitScale(hugeProjection, viewportPx):F6}");
    }

    // One NaN coordinate used to poison Min/Max and leave the canvas un-renderable.
    var withGarbage = PlanProjection.Create(new[]
    {
        new Point3D(0, 0, 0), new Point3D(10000, 10000, 0),
        new Point3D(double.NaN, 5000, 0), new Point3D(5000, double.PositiveInfinity, 0)
    });
    Check("Plan projection: NaN/Infinity coordinates are excluded instead of poisoning the canvas size",
        withGarbage != null && !double.IsNaN(withGarbage.CanvasWidth) && !double.IsNaN(withGarbage.CanvasHeight) &&
        Math.Abs(withGarbage.WorldWidthMm - 10000) < 1e-6,
        withGarbage == null ? "null projection" : $"{withGarbage.CanvasWidth:F1} x {withGarbage.CanvasHeight:F1}");

    Check("Plan projection: a floor with no finite geometry yields no projection rather than a broken canvas",
        PlanProjection.Create(new[] { new Point3D(double.NaN, double.NaN, 0) }) == null);

    // Degenerate extent (a single column on a floor) must not divide by zero.
    var single = PlanProjection.Create(new[] { new Point3D(15000, 22000, 0) });
    Check("Plan projection: a single-member floor produces a finite canvas (no divide-by-zero)",
        single != null && single.CanvasWidth > 0 && single.CanvasHeight > 0 &&
        !double.IsInfinity(single.CanvasWidth) && !double.IsNaN(single.CanvasWidth));

    // Centroid-offset diagnostic: the "everything Missing, nothing Matched" signal.
    var offset = PlanProjection.CentroidOffset(
        new[] { new Point3D(0, 0, 0), new Point3D(10000, 0, 0) },
        new[] { new Point3D(45000, 3000, 0), new Point3D(55000, 3000, 0) });
    Check("Plan projection: centroid offset reports the Revit/ETABS plan shift (45 m X, 3 m Y)",
        offset != null && Math.Abs(offset.Value.DeltaXMm + 45000) < 1e-6 &&
        Math.Abs(offset.Value.DeltaYMm + 3000) < 1e-6,
        offset == null ? "null" : $"ΔX {offset.Value.DeltaXMm:F1}, ΔY {offset.Value.DeltaYMm:F1}");

    var aligned = PlanProjection.CentroidOffset(
        new[] { new Point3D(0, 0, 0), new Point3D(10000, 0, 0) },
        new[] { new Point3D(0, 0, 0), new Point3D(10000, 0, 0) });
    Check("Plan projection: co-located models report ~zero centroid offset (no false coordinate warning)",
        aligned != null && aligned.Value.OffsetMm < 1e-6, aligned == null ? "null" : $"{aligned.Value.OffsetMm:F3} mm");

    Check("Plan projection: centroid offset is undefined when one side has no members",
        PlanProjection.CentroidOffset(new[] { new Point3D(0, 0, 0) }, new Point3D[0]) == null);
}

// ---------------------------------------------------------------------------
// Revit level <-> ETABS story mapping (StoryMapper).
//
// Built from a real model where all 7 Revit levels reported "ETABS: no mapped
// story" even though the elevations agreed exactly. The old rule was "nearest
// elevation wins, no distance limit", which depends entirely on the ETABS story
// elevation list - and when that list comes back empty (the reader swallowed the
// error code), every level maps to nothing, the ETABS side is filtered to
// nothing, and a coordinated model reads as 100% Missing.
// ---------------------------------------------------------------------------
{
    // The actual names from that model, Revit side and ETABS side.
    var revitLevels = new List<(string RevitLevel, double RevitElevationMm)>
    {
        ("BASEMENT 2 LEVEL (SSL)", -9000),
        ("BASEMENT 1 LEVEL (SSL)", -5000),
        ("GROUND FLOOR (SSL)", -100),
        ("1ST FLOOR (PODIUM) (SSL)", 4850),
        ("2ND FLOOR (PODIUM) (SSL)", 8350),
        ("3RD FLOOR (PDOIUM DECK) (SSL)", 13650),
        ("4TH FLOOR (SSL)", 17175)
    };

    // ETABS story elevations as the Story Data dialog shows them: METRES.
    var etabsStoriesMetres = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
    {
        ["BASEMETN 2"] = -9,
        ["BASEMENT 1"] = -5,
        ["GROUND FLOOR"] = -0.1,
        ["1ST FLOOR (PODIUM)"] = 4.35,
        ["2ND FLOOR (PODIUM)"] = 8.35,
        ["3RD FLOOR (PODIUM)"] = 13.65,
        ["4TH FLOOR"] = 17.175,
        ["5TH FLOOR"] = 20.675,
        ["6TH FLOOR"] = 24.175
    };

    Check("Story mapping: '(SSL)' and 'LEVEL' decoration is stripped for comparison",
        StoryMapper.NormalizeName("BASEMENT 2 LEVEL (SSL)") == StoryMapper.NormalizeName("BASEMENT 2"),
        $"'{StoryMapper.NormalizeName("BASEMENT 2 LEVEL (SSL)")}' vs '{StoryMapper.NormalizeName("BASEMENT 2")}'");

    // The Revit name has a typo ("PDOIUM") and says DECK; dropping parenthesised
    // qualifiers makes the pair match anyway.
    Check("Story mapping: differing parenthesised qualifiers still match ('(PDOIUM DECK)' vs '(PODIUM)')",
        StoryMapper.NormalizeName("3RD FLOOR (PDOIUM DECK) (SSL)") == StoryMapper.NormalizeName("3RD FLOOR (PODIUM)"));

    Check("Story mapping: genuinely different floors do NOT normalize to the same name",
        StoryMapper.NormalizeName("4TH FLOOR (SSL)") != StoryMapper.NormalizeName("5TH FLOOR"));

    // ETABS in metres against Revit in millimetres.
    var scale = StoryMapper.DetectEtabsUnitScale(revitLevels, etabsStoriesMetres);
    Check("Story mapping: metre/millimetre unit mismatch is detected (scale 1000)",
        scale == 1000.0, scale?.ToString("F1") ?? "null");

    var mapped = StoryMapper.Map(revitLevels, etabsStoriesMetres);
    var matchedCount = mapped.Count(x => x.IsMatched);
    Console.WriteLine($"STORY MAP: {matchedCount}/{mapped.Count} levels matched; " +
        string.Join(", ", mapped.Select(x => $"{x.RevitLevel}->{(x.IsMatched ? x.EtabsStory : "(none)")}[{x.Kind}]")));

    // The regression: every one of these used to come back unmapped.
    Check("Story mapping: all 7 real Revit levels map to an ETABS story",
        matchedCount == 7, $"{matchedCount} of 7");

    Check("Story mapping: 4TH FLOOR maps to 4TH FLOOR (not 5TH)",
        mapped.First(x => x.RevitLevel == "4TH FLOOR (SSL)").EtabsStory == "4TH FLOOR");
    Check("Story mapping: GROUND FLOOR maps to GROUND FLOOR",
        mapped.First(x => x.RevitLevel == "GROUND FLOOR (SSL)").EtabsStory == "GROUND FLOOR");
    Check("Story mapping: 3RD FLOOR maps across the qualifier/typo difference",
        mapped.First(x => x.RevitLevel.StartsWith("3RD FLOOR")).EtabsStory == "3RD FLOOR (PODIUM)");

    // "BASEMETN 2" is misspelled in ETABS, so it cannot match by name - it has to
    // fall through to elevation, which only works once the unit scale is applied.
    var basement2 = mapped.First(x => x.RevitLevel.StartsWith("BASEMENT 2"));
    Check("Story mapping: a misspelled ETABS story ('BASEMETN 2') still matches by elevation",
        basement2.IsMatched && basement2.EtabsStory == "BASEMETN 2" && basement2.Kind == StoryMatchKind.Elevation,
        $"{basement2.EtabsStory} [{basement2.Kind}]");

    Check("Story mapping: no ETABS story is claimed by two different Revit levels",
        mapped.Where(x => x.IsMatched).Select(x => x.EtabsStory).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            == matchedCount);

    // 1ST FLOOR: Revit 4850 mm vs ETABS 4.35 m = 4350 mm. Matching by name is right
    // (it is that floor), and the 500 mm difference is a real finding to report.
    var first = mapped.First(x => x.RevitLevel.StartsWith("1ST FLOOR"));
    Check("Story mapping: a name-matched floor still reports its elevation difference (4850 vs 4350 = 500 mm)",
        first.Kind == StoryMatchKind.Name && Math.Abs(first.ElevationDeltaMm - 500.0) < 1e-6,
        $"{first.Kind}, delta {first.ElevationDeltaMm:F1} mm");

    // Same names, but ETABS already in millimetres - must not be rescaled.
    var etabsStoriesMm = etabsStoriesMetres.ToDictionary(x => x.Key, x => x.Value * 1000.0, StringComparer.OrdinalIgnoreCase);
    Check("Story mapping: matching millimetre elevations are left alone (scale 1)",
        StoryMapper.DetectEtabsUnitScale(revitLevels, etabsStoriesMm) == 1.0);
    Check("Story mapping: all 7 levels also map when ETABS is already in millimetres",
        StoryMapper.Map(revitLevels, etabsStoriesMm).Count(x => x.IsMatched) == 7);

    // Unbounded "nearest wins" would pair a lone basement with a roof story.
    var farOnly = StoryMapper.Map(
        new[] { ("SOME PLINTH", -9000.0) },
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["ROOF"] = 34675.0 });
    Check("Story mapping: an elevation match beyond tolerance is refused, not forced",
        !farOnly[0].IsMatched, farOnly[0].EtabsStory);

    // The fallback for when the ETABS Story API yields nothing: rebuild the story
    // table from the members, which already carry their story name.
    var etabsMembers = new List<ElementBase>
    {
        new BeamElement { Id = "b1", Name = "B1", LevelName = "GROUND FLOOR", StartPoint = new Point3D(0, 0, -100), EndPoint = new Point3D(5000, 0, -100), Width = 300, Depth = 500 },
        new BeamElement { Id = "b2", Name = "B2", LevelName = "GROUND FLOOR", StartPoint = new Point3D(0, 5000, -100), EndPoint = new Point3D(5000, 5000, -100), Width = 300, Depth = 500 },
        new BeamElement { Id = "b3", Name = "B3", LevelName = "4TH FLOOR", StartPoint = new Point3D(0, 0, 17175), EndPoint = new Point3D(5000, 0, 17175), Width = 300, Depth = 500 },
        // A column assigned to 4TH FLOOR spans up TO that story, so its top is the elevation.
        new ColumnElement { Id = "c1", Name = "C1", LevelName = "4TH FLOOR", StartPoint = new Point3D(0, 0, 13650), EndPoint = new Point3D(0, 0, 17175), Width = 400, Depth = 400 }
    };
    var derived = StoryMapper.DeriveStoryElevations(etabsMembers);
    Check("Story mapping: story elevations can be rebuilt from members when the Story API returns nothing",
        derived.Count == 2 && Math.Abs(derived["GROUND FLOOR"] + 100) < 1e-6 && Math.Abs(derived["4TH FLOOR"] - 17175) < 1e-6,
        string.Join(", ", derived.Select(x => $"{x.Key}={x.Value:F0}")));

    Check("Story mapping: levels map correctly against the rebuilt story table",
        StoryMapper.Map(
            new[] { ("GROUND FLOOR (SSL)", -100.0), ("4TH FLOOR (SSL)", 17175.0) },
            derived).Count(x => x.IsMatched) == 2);

    Check("Story mapping: an empty ETABS story table yields explicit unmatched levels, not a crash",
        StoryMapper.Map(revitLevels, new Dictionary<string, double>()).All(x => !x.IsMatched));
}

Console.WriteLine();
Console.WriteLine($"TOTAL: {passed} passed, {failures} failed");
return failures == 0 ? 0 : 1;
