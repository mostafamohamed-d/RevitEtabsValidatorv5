# Changelog

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
