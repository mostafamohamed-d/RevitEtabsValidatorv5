# Known Limitations / Next Release Candidates

- Section extraction is designed for rectangular frame sections. Circular/T/L/non-prismatic sections need dedicated property readers.
- Revit family parameter conventions vary. The reader uses common dimension parameter names with a bounding-box fallback; office-specific parameter mapping should be added before using the result as a formal BIM QA gate.
- Coordinates are assumed to already use a common project/global origin between Revit and ETABS. A future release should add an explicit shared-coordinate transform or user-entered XYZ transform (translation + rotation + scale validation).
- The current floor-plan display is an in-tool diagnostic view. It is not a replacement for a Revit drafting/coordination view.
- SAFE support is intentionally not mixed into the first production path. The core comparison model can be extended with a SAFE adapter, but SAFE uses a different modeling emphasis (slabs/foundations/supports) and should be implemented as a separate validation profile.
- A real host build must be performed on a Windows workstation with Revit 2025 installed because this Linux build environment does not have the .NET SDK or Autodesk reference assemblies.
