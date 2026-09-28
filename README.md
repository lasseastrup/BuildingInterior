# BuildingInterior

A prototype of the building creation workflow, made to write the spec before we build it in Unity as an editor-only package.

- `prototype/index.html`: self-contained three.js prototype (edit mode + play mode). Open it in a browser; it loads three.js and polygon-clipping from jsDelivr.
  - Buildings: footprint, facade styles, floors, interiors, stairs and lifts, dynamic floor occlusion in play mode.
  - Setbacks: any floor can start a smaller outline that the floors above inherit. The roof it uncovers becomes a walkable terrace with a parapet and a door.
  - City scale: three generated LODs per building, merged far-distance cells, on-demand generation. The bar-chart button opens LOD stats and a 1,000/3,000-building stress test.
- `docs/SPEC.md`: findings and the Unity plan. Covers the data model, z-fighting and topology rules, occlusion, LODs and city-scale rendering, the implementation plan, the editor UI mapping, the UPM package layout and the decisions from the prototype review.
