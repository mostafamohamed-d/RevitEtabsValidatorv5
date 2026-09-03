# Technical Notes

## Coordinate convention
All comparison geometry is normalized to millimetres in a common X/Y/Z coordinate system. Revit geometry is converted from Revit internal feet using `UnitTypeId.Millimeters`. ETABS display units are set to kN-mm-C before frame geometry is read.

## Matching
Matching is one-to-one. Candidates are first constrained by level when possible; each candidate receives a geometry score using plan position, elevation, section and orientation. The best candidate is selected, and near-tied candidates are flagged as ambiguous instead of silently accepted.

## Section handling
The current implementation is targeted at rectangular RC columns and beams. Revit tries common type parameter names (`b`, `h`, `Width`, `Depth`) and falls back to instance bounding box extents. ETABS reads rectangular frame properties through `PropFrame.GetRectangle`.

For non-rectangular frames, the tool will still extract identity and geometry but section dimensions may be zero; those cases should be treated as unsupported rather than as proof that the member is wrong.

## ETABS COM
The connector intentionally uses COM late binding, which avoids a compile-time dependency on a specific ETABS API assembly. At runtime it uses the `CSI.ETABS.API.ETABSObject` ProgID and can attach to an active COM object through `oleaut32!GetActiveObject`; otherwise it can instantiate and start ETABS.

CSI's API documentation demonstrates the `cOAPI -> SapModel` relationship and COM creation pattern. Keep the ETABS version consistent on machines used for production automation.

## Revit execution model
All Revit API reads and selection changes are performed through an `ExternalEvent`, so the modeless WPF UI does not directly call Revit API members from arbitrary UI events.
