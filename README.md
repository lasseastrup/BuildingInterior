# BuildingInterior

A prototype of the building creation workflow, made to write the spec before we build it in Unity as an editor-only package.

- `prototype/index.html`: self-contained three.js prototype (edit mode + play mode). Open it in a browser; it loads three.js and polygon-clipping from jsDelivr.
  - Buildings: footprint, facade styles, facade details (AC units, fire escapes, awnings…), floors, interiors, stairs and lifts, dynamic floor occlusion in play mode.
  - Setbacks: any floor can start a new outline that the floors above inherit, stepping in (a walkable terrace with a parapet and a door) or out (an overhang), with its own facade style.
  - Roofs: flat, hip, gable or shed (straight-skeleton roofs on any outline), with pitch and eaves.
  - Per-floor storey heights, stairs and lifts at any angle, and shared (party) walls between buildings built wall to wall.
  - City scale: three generated LODs per building, merged far-distance cells, on-demand generation. The bar-chart button opens LOD stats and a 1,000/3,000-building stress test.
- `docs/SPEC.md`: findings and the Unity plan. Covers the data model, z-fighting and topology rules, occlusion, LODs and city-scale rendering, the implementation plan, the editor UI mapping, the UPM package layout and the decisions from the prototype review.

## Unity package

The Unity 6.3 port lives in `unity/` (three packages under `unity/Packages/com.triband.storey*`), tested with `dotnet test unity/Headless/Triband.Storey.Tests` and no editor. See `unity/README.md` and `docs/UNITY-PACKAGE-PLAN.md`.
