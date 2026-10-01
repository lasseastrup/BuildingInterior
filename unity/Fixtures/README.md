# Fixtures

Reference answers emitted by the prototype and read by `unity/Headless/Triband.Storey.Tests`.
Regenerate them from the prototype, never by hand:

```bash
cd prototype/tools && npm install && npm run fixtures
```

| File | What | Read by |
|---|---|---|
| `demo.json` | The demo street as the prototype saves it (layout v2, 7 buildings). | `PrototypeJsonTests` |
| `variants.json` | 78 buildings: every footprint preset × window style, one- and two-step setbacks, overhangs with their own style and a taller floor, roofed terraces, hip/gable/shed roofs, flush cores, hand-placed details. The same set the coplanar checker runs over. | `PrototypeJsonTests` |
| `census0.json`, `census1.json` | Per building of both corpora: the LOD0 opaque mesh's (LOD1 mesh's) triangles bucketed by plane (normal, offset) and colour with the area they cover. Triangulation-independent; the generator is judged on producing the same surface in the same place. | `GeneratorTests` |
| `census2.json` | The LOD2 massing mesh bucketed by plane, quad kind, bay width, window span and the parameter row the facade shader reads. | `GeneratorTests` |
| `ops.json` | The floor, outline and setback operations run in the prototype on the demo street and a third of the variant set (`prototype/tools/export-ops.mjs`, `npm run ops`): per case the operation, its arguments, what it returned and the building afterwards. | `EditOpsTests` |
| `ops-interior.json` | Stairs, lifts, the interior wall graph, doors and erase, from the same script: cases of one or more steps as the tools make them (the tool handlers replayed line for line), with the last step's return value and the building after. | `EditInteriorTests` |
| `ops-facade.json` | The Facade tab's edits from the same script: details by hand for every kind, entrances, blank walls, style presets and setback styles, terrace or roof; plus where new buildings go (`freeSpots`) and what a footprint and style preset make (`made`). | `EditFacadeTests` |
| `derived.json` | Per building of both corpora: floor bases, roof height, tier starts, outline per floor, resolved wall and interior colour per floor, shaft tops, and triangles per LOD (a target for workstream 2). | `DerivedTests` |
