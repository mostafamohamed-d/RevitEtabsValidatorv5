# Revit 2025 ↔ ETABS Structural Model Validator

A C# / .NET 8 Revit 2025 add-in that compares reinforced-concrete frame members between the active Revit model and an open/started ETABS model.

## Implemented
- Revit 2025 ExternalApplication + ExternalCommand
- Modeless WPF UI driven by Revit ExternalEvent
- ETABS COM connection without requiring an ETABS API DLL reference
- Columns and beams
- Coordinate-based level correspondence via elevation (does not require Revit level names and ETABS story names to match as text)
- One-to-one deterministic element matching with distance/geometry scoring
- Plan position, elevation, length, width, depth and orientation checks
- Configurable tolerances
- Summary counts and detailed results grid
- Floor-by-floor plan visualization in the WPF window
- Select corresponding Revit element from the result row
- CSV export
- JSON export
- Re-run and refresh
- Diagnostic/logging file
- Installer script that writes the Revit 2025 .addin manifest

## Important engineering boundary
This is a coordination/validation tool. It does not perform structural design code checks. A PASS means the two extracted models agree within the configured coordination tolerances; it is not a statement of structural adequacy.

## Build
Revit 2025 uses .NET 8, so build with Visual Studio 2022 17.8+ / .NET 8 SDK. The project auto-references:
`C:\Program Files\Autodesk\Revit 2025\RevitAPI.dll`
`C:\Program Files\Autodesk\Revit 2025\RevitAPIUI.dll`

If Revit is installed elsewhere:
`dotnet build -p:RevitInstallPath="D:\Autodesk\Revit 2025"`

ETABS is accessed through COM at runtime. The tool first attempts to attach to a running ETABS instance and, when requested, can start ETABS through its COM ProgID.

## Install
1. Build Release.
2. Run `Installer\Install-RevitEtabsValidator.ps1` as the current Windows user.
3. Restart Revit 2025.
4. Use Add-Ins → Revit ↔ ETABS Validator.

## Default tolerances
- Position: 25 mm
- Elevation: 25 mm
- Section dimensions: 5 mm
- Angle: 1 degree
- Length: 25 mm

Adjust these based on your project BIM/modeling convention.

## ETABS units
The connector requests kN-mm-C before extracting geometry. CSI's API is display-unit based, so explicit unit selection is important.
