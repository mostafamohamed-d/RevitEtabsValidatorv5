# Revit ↔ ETABS Structural Model Validator

A shared C# codebase for a Revit add-in that compares structural framing between Revit and ETABS.

## Supported host combinations

- **Revit 2024 + ETABS 21** → `.NET Framework 4.8` / `net48`
- **Revit 2025 + ETABS 22** → `.NET 8` / `net8.0-windows`

The same coordination engine is shared between both targets. The project references the Revit API and ETABSv1 API from the installation selected by the target framework.

## Implemented

- Revit ExternalApplication + ExternalCommand
- Modeless WPF UI driven by Revit ExternalEvent
- Typed ETABSv1 connection and model reader
- Columns and beams
- Geometry-first one-to-one correspondence
- Beams treated as line segments using endpoint geometry with start/end reversal handling
- Columns treated as plan points with base/top elevation
- Plan position, elevation, length, width, depth and orientation checks
- Configurable tolerances
- ETABS frame-name exclusion rule for names beginning with `0`
- Summary counts and detailed results grid
- Floor-by-floor plan visualization
- Select corresponding Revit element from result rows
- CSV and JSON export
- Diagnostics and installer support

## Engineering boundary

This is a coordination/validation tool. It does not perform structural design-code checks. A matched result means the extracted Revit and ETABS geometry agree within the configured coordination tolerances; it is not a statement of structural adequacy.

## Build in Visual Studio

Open `RevitEtabsValidator.sln` in Visual Studio 2022.

### Revit 2024 + ETABS 21

Build the `net48` target. Default references:

`C:\Program Files\Autodesk\Revit 2024\RevitAPI.dll`

`C:\Program Files\Autodesk\Revit 2024\RevitAPIUI.dll`

`C:\Program Files\Computers and Structures\ETABS 21\ETABSv1.dll`

Output:

`bin\Release\net48\RevitEtabsValidator.dll`

`bin\Release\net48\ETABSv1.dll`

### Revit 2025 + ETABS 22

Build the `net8.0-windows` target. Default references:

`C:\Program Files\Autodesk\Revit 2025\RevitAPI.dll`

`C:\Program Files\Autodesk\Revit 2025\RevitAPIUI.dll`

`C:\Program Files\Computers and Structures\ETABS 22\ETABSv1.dll`

Output:

`bin\Release\net8.0-windows\RevitEtabsValidator.dll`

`bin\Release\net8.0-windows\ETABSv1.dll`

Paths can be overridden with MSBuild properties such as `RevitInstallPath` and `EtabsInstallPath`.

## Installer

The installer is target-aware:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\Installer\Install-RevitEtabsValidator.ps1 -Target Revit2024-ETABS21
```

or:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\Installer\Install-RevitEtabsValidator.ps1 -Target Revit2025-ETABS22
```

The script builds only the selected target, verifies the application entry point, and copies both `RevitEtabsValidator.dll` and the matching `ETABSv1.dll` beside the manifest in the correct Revit Addins directory.

## Manual Add-in Manager use

For Revit 2024, load the `net48` build and keep its matching `ETABSv1.dll` in the same directory.

For Revit 2025, load the `net8.0-windows` build and keep its matching `ETABSv1.dll` in the same directory.

Do not use the Revit 2025 DLL in Revit 2024 or the Revit 2024 DLL in Revit 2025.

## Default tolerances

- Position: 25 mm
- Elevation: 25 mm
- Section dimensions: 5 mm
- Angle: 1 degree
- Length: 25 mm

The comparison logic also supports separate beam/column Z offsets where the project coordinate convention requires them.
