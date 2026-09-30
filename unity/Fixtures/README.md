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
| `derived.json` | Per building of both corpora: floor bases, roof height, tier starts, outline per floor, resolved wall and interior colour per floor, shaft tops, and triangles per LOD (a target for workstream 2). | `DerivedTests` |
