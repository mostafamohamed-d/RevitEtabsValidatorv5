# User Guide

## 1. Install prerequisites
- Revit 2024/2025
- ETABS installed and its COM API registered - any installed ETABS version
  works (see "ETABS version support" below), not just 21/22
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

**Connect ETABS** first checks how many ETABS instances are currently running:
- None found: falls back to attaching to whatever ETABS reports as its active
  instance (enable "Start ETABS if not running" to launch one instead).
- Exactly one: connects to it directly.
- More than one: a picker lists each running instance so you choose which one
  to attach to, instead of the tool guessing.

## ETABS version support
The connector doesn't hardcode a specific ETABS version. At startup it scans
every `ETABS <version>` folder under `Program Files\Computers and Structures`
(and the x86 equivalent), and uses the newest one it finds that actually
contains `ETABSv1.dll`. CSI keeps the handful of OAPI members this tool calls
(`FrameObj`, `PointObj`, `PropFrame`, `Story`, `SetPresentUnits`,
`ApplicationStart`) stable release to release, so this works against ETABS 21,
22, 23, 24, or a future release without a separate build per version. The
`RevitVersion`/`EtabsVersion` MSBuild properties only control which default
folder the *compiler* references when building from source (see README) -
they don't limit which version the compiled add-in can connect to at runtime.

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
Choose a level under Floor Plan. Revit elements are drawn as solid lines/dots
and ETABS elements as dashed lines/orange points. A non-matched member is
colored by *which* check failed, matching the legend in the plan view's
bottom-left corner: gold = position, crimson = elevation, purple = section,
teal = rotation, pink = ambiguous match, black = missing counterpart (only
drawn on the side that has an element). Click any member for its full
coordination detail, including the reason.

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

## 9. Beam/Column Z-Offset
Separate from the tolerances above, **Beam Z-Offset** and **Column Z-Offset**
are a signed millimeter correction for a *systematic* elevation datum
difference between the two models (for example, ETABS beams referenced at a
different level than Revit's beam host level). Symptom: the same ΔElev value
repeats across most or all beams (or columns) on a level, rather than varying
member to member. That pattern means one consistent modeling-convention
offset, not N separate real errors - set the corresponding Z-Offset field to
cancel it out, then re-run, instead of loosening the Elevation tolerance
(which would just stop flagging *real* elevation errors too). Selecting any
ElevationMismatch result and reading its "Why?" panel shows whether an offset
is currently applied for that element type.
