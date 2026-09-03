# Changelog

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
