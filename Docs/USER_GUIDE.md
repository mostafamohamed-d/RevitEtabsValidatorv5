# User Guide

## 1. Install prerequisites
- Revit 2025
- ETABS installed and its COM API registered
- Visual Studio 2022 17.8+ or .NET 8 SDK for building

## 2. Build
Open the solution and build Release. Revit 2025 uses .NET 8, so this project targets `net8.0-windows`.

## 3. Install
Run:
`powershell -ExecutionPolicy Bypass -File .\Installer\Install-RevitEtabsValidator.ps1 -BuildFirst`

The installer writes the manifest to the current user's Revit 2025 Addins directory.

## 4. Run
Open a Revit project. Start ETABS or let the tool start it. Open the validator from the Structural QA ribbon.

Click **Read Revit**, **Connect ETABS**, then **Run Validation**.

## 5. Interpret statuses
- Matched: geometry and section values within tolerance.
- PositionMismatch: plan position or beam length outside tolerance.
- ElevationMismatch: vertical location outside tolerance.
- SectionMismatch: width/depth outside tolerance.
- RotationMismatch: physical plan orientation outside tolerance.
- MissingInEtabs: Revit element was not found in ETABS.
- MissingInRevit: ETABS element was not found in Revit.
- AmbiguousMatch: two ETABS candidates are too close under the matching score.

## 6. Floor plan
Choose a level under Floor Plan. Revit elements are drawn as solid lines/dots and ETABS elements as dashed lines/orange points. Red markers identify non-matched/failed checks.

## 7. Selecting a Revit member
Select a result row and click **Select in Revit**. The tool uses Revit's selection API to select the corresponding physical element.

## 8. Tolerances
Defaults are intentionally tight for coordination:
- Position 25 mm
- Elevation 25 mm
- Section 5 mm
- Length 25 mm
- Rotation 1°

Use project BIM/modeling conventions when changing them. Tolerances are coordination settings, not structural design-code limits.
