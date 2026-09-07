# Changelog

## 1.0.10
- Removed the "Export JSON" button and its `ExportJson_Click` handler at the
  user's request. Note for the record: that method itself had no defect -
  the "MainWindow doesn't contain ExportJson_Click" error reported earlier
  was a cascading symptom of the missing-`using` compile failures fixed in
  1.0.9 (once one file in the assembly fails to compile, everything else,
  including this button's own handler, is reported as broken too). CSV
  export (`Export CSV` / `ExportCsv_Click`) is unaffected and remains the
  only export path; `_all`'s JSON serialization code is gone with it.

## 1.0.9
- Fixed (CRITICAL, net48 build): 17 files across `Core/`, `ETABS/`, and `Revit/`
  (most notably `MainWindow.xaml.cs`, `ModelComparer.cs`, `EtabsConnection.cs`,
  `EtabsInstallationScanner.cs`) used `List<>`, `Dictionary<>`, `Math.*`,
  `Path`/`Directory`/`File`, and LINQ (`.Where`/`.Select`/...) without an
  explicit `using` for their namespace, relying entirely on the SDK's
  `ImplicitUsings` code-gen. That was never actually exercised end-to-end for
  the `net48` leg of this project before now (only `net8.0-windows`, via this
  session's own dependency-free test project on Linux, and CI has no Windows
  build at all) - on at least one real net48/VS toolchain the generated
  implicit-usings file for that target didn't get produced, so the same
  `CS0103 'Path'/'Directory' does not exist` failure (and the cascading
  "MainWindow doesn't contain ExportJson_Click" error from the resulting
  broken assembly) that a user hit building for Revit 2024/ETABS 21 was
  latent in every one of those 17 files, not just the one that happened to
  surface first. Every affected file now has its own explicit `using`
  statements and no longer depends on `ImplicitUsings` to compile.

## 1.0.8
- Fixed (HIGH): the floor plan could render blank/near-invisible on the very
  first validation run. `FitPlan_Click` computed its fit scale from
  `PlanViewHost.ActualWidth/ActualHeight`, but on the first draw right after
  `RunValidation_Click` those can still read 0 (WPF hasn't run a layout pass
  over the newly-populated panel yet). Fitting geometry that can be
  thousands of mm wide against a 0-sized viewport produces a near-zero
  scale, and nothing else would trigger a re-fit unless the user happened
  to resize the window afterward - the plan would just look permanently
  empty. `FitPlan_Click` now detects a not-yet-laid-out host and retries
  itself once real layout is available, instead of committing to that fit.
- Added: clicking a row in the results grid now visually highlights the
  corresponding member(s) on the plan (a gold glow on both the Revit and
  ETABS shape for that result), not just the side detail panel - previously
  only a plan click drove the detail panel, not the reverse.
- Added: every mismatched member (any status other than Matched) now also
  gets a translucent red halo behind its type-colored line/circle, so any
  problem reads as "red" at a glance while the existing per-type legend
  color (position/elevation/section/rotation/ambiguous) still distinguishes
  what kind of mismatch it is.
- Added: a floor/story mapping label at the top of the plan view (e.g.
  "Level 2 → ETABS "Story2" · Columns 4 Revit / 4 ETABS · Beams 4 Revit / 4
  ETABS") so it's visible at a glance which ETABS story a Revit level
  mapped to and how many members are in view.
- Changed: the plan view and results grid are now separated by a draggable
  `GridSplitter` (previously the results grid had a fixed 190px height), so
  the plan can be resized larger; the default window size and the plan's
  minimum height were also increased.

## 1.0.7
- Fixed: `Installer/Install-RevitEtabsValidator.ps1` ran
  `dotnet clean`/`dotnet build ... -f net48` without also passing
  `-p:BuildLegacy=true`. Since the csproj only exposes `net48` as a valid
  `TargetFrameworks` entry when that MSBuild property is set (it defaults
  to `net8.0-windows` only, to keep default/CI builds simple), the
  `Revit2024-ETABS21` install path would fail immediately with an
  "invalid/undefined target framework" error. Both commands now pass
  `-p:BuildLegacy=true` unconditionally; `-f $TargetFramework` still
  restricts the actual build to the one framework being installed, so this
  has no effect on the `Revit2025-ETABS22` path.
- Documented: building `net48` directly from Visual Studio (rather than
  via the installer script) requires a local `RevitEtabsValidator.csproj.user`
  override, since VS's default/IntelliSense build otherwise only sees the
  `net8.0-windows` target and fails with MSB3644 on machines without the
  .NET 8 SDK. See `BUILD_NOTES.txt`.

## 1.0.6
- Fixed (CRITICAL): `MainWindow.xaml.cs`'s `RunComparisonForSelectedScope`
  silently validated the *entire* ETABS model (all columns/beams, every
  story) whenever the selected Revit floor(s) had no mapped ETABS story at
  all. Root cause: `filterEtabs` required `_selectedEtabsStories.Count > 0`
  before filtering at all, so an empty mapping (a level ETABS doesn't
  model) skipped filtering entirely instead of correctly narrowing to
  nothing. A single-floor validation could turn into a full-model one,
  flooding the results with thousands of unrelated `MissingInRevit` rows
  for every other floor's ETABS elements. Fixed by filtering whenever
  `_selectedEtabsStories.Count < _etabsStoryElevationsMm.Count` (the
  `Where(...).Contains(...)` filter already correctly yields an empty set
  when nothing is mapped, once it isn't skipped) - one line. Also added a
  visible status warning when this happens, instead of a silent flood of
  results, per the "handled safely and visibly" requirement.
- Fixed (HIGH): every ETABS COM call (`Connect ETABS`, and the ETABS read
  inside `Run Validation`) ran synchronously on the same thread hosting the
  window, which for a modeless Revit add-in window is Revit's own UI
  thread. ETABS is a separate out-of-process application, so each of the
  tens of thousands of individual COM calls needed to read a 16,000+ beam
  model (`GetPoints`/`GetLabelFromName`/`GetSection`/`GetLocalAxes` per
  frame) is an inter-process round trip; doing all of them one at a time,
  synchronously, on the UI thread is the most likely cause of Revit
  appearing to hang during a large-model read. `ConnectEtabs_Click` and the
  new `ReadEtabsAsync` now do the actual COM/data-fetching work inside
  `Task.Run`, while every WPF-control-touching line stays on the UI thread
  (either before the first `await` or automatically after it, via WPF's
  dispatcher-based `SynchronizationContext`). The toolbar is disabled and a
  wait cursor shown for the duration (`SetBusy`), so a long read gives
  visible feedback and a second click can't start an overlapping operation
  on the same connection. `RunComparisonForSelectedScope` (the actual
  Revit/ETABS matching pass) is deliberately left untouched and still runs
  synchronously on the UI thread - it is pure C#, not COM, and was measured
  in the prior release's added performance test at ~0.4s even at a
  16,000+-beam scale, so it was never the bottleneck this addresses.
  **Not verified against a live ETABS install** (no Windows/ETABS access in
  this environment) - out-of-process COM proxies are generally safe to call
  from a background thread since the real marshaling happens over RPC to
  the other process regardless of the calling thread, but if this
  introduces a COM threading exception on a real machine that the
  synchronous version didn't have, report it and the fix is a straightforward
  revert of just this one change (the two fixes in this release are
  independent).

## 1.0.5
- Added: ETABS version support is no longer hardcoded to 21/22. New
  `ETABS/EtabsInstallationScanner` scans every `ETABS <version>` folder under
  Program Files (and the x86 equivalent) at runtime and loads the newest one
  that actually contains `ETABSv1.dll`, instead of only looking for the exact
  folder name matching the build's target framework. CSI keeps the handful
  of OAPI members this project calls stable across releases, so one compiled
  add-in now connects to ETABS 21, 22, 23, 24, or a future release without a
  separate build per version. Both `ETABS/EtabsAssemblyResolver.cs` and
  `Revit/Services/EtabsAssemblyResolver.cs` (the two independent runtime
  resolvers) now use this. Covered by 6 new regression tests in
  `Tests/RevitEtabsValidator.Core.Tests` (version picking, missing-DLL
  fallback, no-install case, and folder-name parsing).
- Added: **Connect ETABS** now enumerates every running ETABS instance via
  the Windows COM Running Object Table before connecting. Zero found falls
  back to the previous single-instance behavior; exactly one connects
  directly; more than one shows a new `EtabsInstancePickerWindow` so you
  choose which instance to attach to, instead of the tool silently grabbing
  whichever one plain `GetActiveObject` would have returned.
- Added: **Beam Z-Offset** and **Column Z-Offset** tolerance fields in the UI
  (previously `ValidationTolerance.BeamZOffsetMm`/`ColumnZOffsetMm` existed
  in `Core/` but had no UI control, so they were always 0 and unreachable by
  users). A systematic ΔElev that repeats across most/all members of one
  type is almost always a Revit/ETABS modeling-datum difference, not N real
  errors - these fields cancel it out. The "Why?" panel for an
  ElevationMismatch now also states the tolerance and offset that were
  actually applied, and Position/Section/Rotation mismatches likewise now
  show their applied tolerance.
- Added: the floor plan now colors a mismatched member by *which* check
  failed (gold = position, crimson = elevation, purple = section, teal =
  rotation, pink = ambiguous, black = missing counterpart) instead of a
  single generic red, with a matching legend in the plan view. Hovering a
  member's tooltip also now names its status, not just its name/ID.

## 1.0.4
- Redesigned `Revit/UI/MainWindow.xaml`: consistent card-based layout, a
  proper button/text-box style system (rounded corners, hover/press
  feedback, a primary accent style for the main actions vs. a secondary
  outline style for the rest), styled `DataGrid` headers, and a colored
  severity stripe on each results-grid row (via a new
  `SeverityToBrushConverter`) so problem rows are visible at a glance
  without scrolling to the Status column. The eight summary tiles now carry
  a colored accent bar matching their meaning (green for Matched, amber for
  Warnings, red for Errors/Missing).
- Fixed: the ETABS connection indicator dot in the header was hardcoded to
  green in XAML and never actually updated - it looked "connected" even when
  `ConnectionStateText` said "Not connected". It's now named `ConnectionDot`
  and set alongside the status text through a new `SetEtabsConnectionState`
  helper in `MainWindow.xaml.cs`.
- Removed: the "All" button in the Floor Plans panel and its
  `AllFloors_Click` handler. That button had already been hidden at runtime
  since a prior release (`MainWindow.CompatibilityFixes.cs`, now deleted)
  because it was redundant with "Scope…" → "Select All"; instead of styling
  a control nobody could click, it and its hide-button workaround are gone.
- Not verified visually in this session: this environment has no Windows
  Desktop SDK workload or Revit/ETABS install, so `RevitEtabsValidator.csproj`
  cannot be built or run here (see BUILD_NOTES.txt). The XAML was checked for
  well-formedness and every `x:Name`/event handler was cross-referenced
  against the code-behind, but a first run on a real Windows/Revit host is
  recommended before relying on it.

## 1.0.3
- Fixed: `Core/Comparison/ModelComparer.cs`'s identity gate (deciding whether a
  Revit/ETABS pair is even a candidate for the same physical element) used the
  *exact same* distance/angle as the pass/fail tolerance check. Any column or
  beam that drifted even slightly past `PositionToleranceMm`/`AngleToleranceDegrees`
  was therefore excluded from candidacy entirely and reported as two orphaned
  `MissingInRevit`/`MissingInEtabs` rows instead of one linked `PositionMismatch`
  or `RotationMismatch` result with an actionable delta - making those two
  documented statuses (see `Docs/USER_GUIDE.md`) unreachable in practice. Added
  `ValidationTolerance.IdentityGateMultiplier` (default 4x) so the identity
  search window is meaningfully wider than the strict pass/fail tolerance,
  while the pass/fail checks themselves are unchanged. Verified with new
  regression tests in `Tests/RevitEtabsValidator.Core.Tests` covering both the
  newly-reachable mismatch statuses and that genuinely unrelated, far-apart
  elements are still correctly reported as missing rather than falsely paired.
- Added: `Tests/RevitEtabsValidator.Core.Tests`, a dependency-free console
  test project covering `ModelComparer`. `Core/` has no Revit/ETABS/WPF
  dependency, so unlike the main add-in project this builds and runs on any
  machine with the plain .NET SDK - including this Linux session, which has
  no Windows Desktop workload or Revit/ETABS reference assemblies. Run with
  `dotnet run --project Tests/RevitEtabsValidator.Core.Tests`.
- Fixed (found by automated PR review): the identity-gate widening above
  introduced two follow-on defects, both confirmed by reverting the fix and
  watching the new regression tests fail against the un-fixed code:
  - The per-Revit-item processing order was "fewest candidates first, then by
    level/name" (unchanged from before this release). Once the identity
    window was widened, an out-of-tolerance Revit element could become the
    *only* candidate for an ETABS element that was also an exact match for a
    different Revit element; if the drifted element happened to sort first,
    it claimed the ETABS id and the true exact match was falsely reported as
    missing. Processing order is now the globally best candidate score first
    (falling back to candidate count, then level/name), so an exact match
    always claims its ETABS counterpart before a weaker candidate can.
  - `IdentityGateMultiplier` has no widening effect when a user sets
    `PositionToleranceMm`/`AngleToleranceDegrees` to `0` (a valid, if strict,
    setting the UI accepts): a multiplier times zero is still zero, so even a
    1 mm/1 degree drift would fall outside the identity window and silently
    reproduce the original "two orphaned Missing entries" bug. Added an
    absolute floor (5 mm / 2 degrees) under the identity window so a strict
    zero pass/fail tolerance still gets a usable identity window.

## 1.0.2
- Fixed: `Installer\Install-RevitEtabsValidator.ps1`'s own artifact-verification
  steps (both the post-build and post-deploy checks) loaded the built DLL via
  `Assembly.LoadFrom` + `GetType()` from plain PowerShell, not from inside
  Revit. Since `App` implements `IExternalApplication` (defined in
  `RevitAPIUI.dll`, intentionally not copied into the build output because
  Revit supplies it at runtime), resolving that type outside Revit's process
  failed silently and the script reported "Compiled DLL does not contain
  RevitEtabsValidator.App" even on a correct build. Added a `-RevitInstallPath`
  parameter (defaults from `-RevitVersion` the same way the .csproj does) and
  now copy `RevitAPI.dll`/`RevitAPIUI.dll` next to each verification copy
  before loading it, so type resolution succeeds the same way it would inside
  Revit.
- Fixed: silenced `ETABS/EtabsConnection.cs`'s CS8618/CS8603 nullable warnings
  properly (null-forgiving on the COM object, which is genuinely null before
  a successful connect - `IsConnected` is what callers should check) instead
  of leaving them unaddressed.

## 1.0.1
- Fixed: `ETABS/EtabsModelReader.cs` called the ETABS COM object through `dynamic`
  with `ref` parameters (`GetNameList`, `GetPoints`, `GetLabelFromName`, `GetSection`,
  `GetLocalAxes`, `GetCoordCartesian`, `GetRectangle`, `Story.GetNameList`,
  `Story.GetElevation`) - this does not compile in C# (CS1975: a dynamic call
  site cannot have a ref/out argument). Rewrote to use `Type.InvokeMember`
  reflection-based late binding instead, which supports ByRef COM parameters
  and preserves the original no-compile-time-ETABSv1-reference design.
- Fixed: also corrected `FrameObj.GetSection`'s `SAuto` out-parameter type from
  `bool` to `string`, matching CSI's documented signature.
- Fixed: `Core/Comparison/ModelComparer.cs` pre-filtered every match candidate
  by exact string equality between Revit's Level name and ETABS's Story name.
  Since these names are not the same text between the two models in general,
  this silently produced zero candidates for nearly everything, reporting the
  whole model as missing/mismatched. Removed the name-equality filter -
  elevation is already part of the geometry match score and is a correct,
  coordinate-based level discriminator that doesn't depend on naming
  conventions matching.
- Fixed: `Properties/AssemblyInfo.cs` was not included by the project's
  Compile glob (only `Core/`, `ETABS/`, `Revit/`) and would throw a duplicate-
  attribute build error (CS0579) if ever re-included, since
  `GenerateAssemblyInfo` already emits the same attributes. Moved its values
  into `.csproj` MSBuild properties and removed the file.
- Fixed: `Core/Validation/ValidationResult.cs` had non-nullable `string`
  properties with no default under `<Nullable>enable</Nullable>` (CS8618
  warnings). `RevitElementId`/`EtabsElementId`/`RevitName`/`EtabsName` are now
  `string?` to match how the rest of the codebase already treats them
  (existing null checks in `MainWindow.xaml.cs`); the always-populated fields
  (`ElementType`, `StoryOrLevel`, `Message`) default to `""`.

## 1.0.0
- Rebuilt as a Revit 2025 / .NET 8 add-in.
- Added WPF modeless UI and Revit ExternalEvent workflow.
- Added ETABS COM connection.
- Added unified column/beam model and validation engine.
- Added tolerances, ambiguity detection, floor filtering, plan visualization, Revit selection, CSV/JSON export.
- Added installation manifest template and PowerShell installer.
