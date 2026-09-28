# Building Creation Tool: Prototype Findings & Unity Plan

Status: draft, written with the web prototype in `prototype/index.html`
Scope: **editor-only tool.** Buildings are authored in the Unity Editor and baked into scenes; players never build. Play mode in the prototype stands in for Unity's Play mode, to test occlusion and circulation.
Goal: designers can build a good-looking building with 10 floors and rooftop access in a few minutes, and remove a floor just as quickly. During play, the player always sees the floor they are on, from any camera angle. The same buildings must scale to **a full city of thousands of buildings**, shipped as a Unity package (§6, §11).

---

## 1. What the prototype does

| Area | Prototype behaviour |
|---|---|
| **Modes** | *Edit*: set up one or more buildings. *Play* (stands in for Unity Play mode): third-person character, follow camera, virtual joystick + WASD. `P` / `Esc` switch. From the Interior tab, Play starts on the floor you're editing. |
| **Footprint** | Drag corner handles; click `+` on an edge to insert a corner; double-click a corner to delete it. Grid snap (25 cm), plus axis alignment with neighbouring corners. Edge lengths are shown live. Presets: Rect, L, U, T, Octagon. Move the building with the centre handle; Rotate 90°. |
| **Facade** | Five one-click style presets. Window type (none / punched / tall / ribbon / curtain wall), width, bay spacing. Ground-floor treatment (shopfront / match / solid). Floor bands, parapet, rooftop units, colour swatches with custom pickers. Facade tools: *Entrance* (click a ground-floor wall) and *Blank wall* (switch one wall's windows off). |
| **Floors** | Floor card on the right: count field (type `10` to get 10 floors) and `+` / `−`. **New floors copy the top floor's layout.** In the Interior tab the card expands into the floor list, with per-floor *copy layout to all floors above*, *duplicate* and *delete*. `[` `]` step floors, `+` `−` add or remove the top floor. |
| **Interior** | *Walk-in interior* or *Shell only* per building (see §3). Walls are edited like the footprint: drag joints, split with **+**, double-click to join or remove. Tools: Select, Wall (click-chain or drag, snaps to points and walls with visible markers, T-junctions split the host wall), Door (toggle a doorway on any wall, and on exterior walls at ground level), Erase, Stairs, Lift, Furnish (8 test props). Floors above the active one are hidden and drawn as outline "ghosts". |
| **Vertical circulation** | Stairs and lifts are **building-level cores** with a floor range (`bottom` → `top`, where `top = -1` means "follow the top floor") and a **roof access** flag. Adding floors extends them automatically. Deleting floors re-indexes them. Slabs are cut automatically where stairs pass through. Roof access generates a bulkhead with a door. |
| **Occlusion** | Floors above the player are hidden. The rest is handled per fragment in the shader: height clip, **cutaway** (walls between camera and player drop to a stub), **see-through cone** (dithered hole from camera to player), and a character silhouette. All of it runs live during play, with the modes and parameters exposed for tuning. |
| **Data** | The whole layout is one JSON document (**?** → Layout JSON). Undo/redo covers every edit. Autosaves to local storage. |
| **Rendering** | No z-fighting: generated geometry has zero visible coplanar faces at every LOD (automated check, §4.1). Optimised topology: walls are polygons with holes, and hidden faces are never generated (§4.2). |
| **City scale** | Three semantic LODs per building plus merged far-distance cells, picked per frame from on-screen feature size with a dithered cross-fade. Detail LODs build on demand, one storey per step, within a frame budget. The **stats card** (bar-chart button) shows the LOD split, draw calls and triangles, has a LOD-tint toggle, and can generate a 1,000- or 3,000-building test city (§6). |

## 2. Workflow principles that proved out

1. **Floors are cheap and the layout is inherited.** The typical case is "same floor ×N". Adding a floor clones the top floor, so 10 floors takes one number entry. Designers only touch the floors that differ (lobby, penthouse).
2. **Cores span floors instead of living on one floor.** A stair or lift is placed once and owns its floor range. It extends when floors are added, so rooftop access never needs rework. This is the single biggest QoL win over placing stairs per floor.
3. **Nothing is destructive without undo.** Delete floor / building / footprint preset are all one Ctrl+Z away. That's why the prototype has no confirmation dialogs.
4. **The edit view and play view use the same occlusion.** In the Interior tab the active floor is sliced and cut away exactly as in play, so what the designer sees is what the player gets.
5. **Everything snaps.** Grid, corners, walls, and axis lock while drawing walls. Free placement is the exception (hold Alt when dragging corners).
6. **Test instantly.** Pressing Play from the Interior tab drops the character on the floor being edited, next to the nearest core.

## 3. Data model (Unity: ScriptableObject or serialised class, generated at edit time)

```text
BuildingData
  id, name
  position (x,z), all child coords are building-local
  footprint: Vector2[]            // any simple polygon, winding-agnostic
  groundHeight: float             // 3.0–6.0
  floorHeight: float              // 2.7–4.5 (all upper floors)
  floors: FloorData[]             // index 0 = ground, roof level = floors.Count
  cores: CoreData[]
  entrances: {edge:int, t:float}[] // t = 0..1 along footprint edge
  blankEdges: int[]
  interior: bool                  // false = shell only (see below)
  style: FacadeStyle

FloorData
  walls: {a:Vector2, b:Vector2, doors:{t:float}[]}[]
  props: {type, x, z, rot(quarter turns)}[]

CoreData
  id, type: Stairs | Lift
  x, z, rot (quarter turns)
  bottom: int, top: int (-1 = follow top floor), roofAccess: bool

FacadeStyle
  preset, wall/trim/interior/floor/roof/core/glass colours,
  windows: None|Punched|Tall|Ribbon|Curtain, winWidth, baySpacing,
  ground: Shopfront|Match|Solid, bands, parapet, rooftopUnits
```

**Shell-only buildings** (`interior = false`) keep their floor count, facade and roof, but generate no intermediate slabs, rooms, props or cores. Windows become opaque (tinted glass), entrances become closed doors, and the collider is a solid shell. Floor data is kept, so switching back restores the interior. Use them for background blocks: the 9-floor shell in the demo has about a third of the triangles of the 10-floor walk-in block, and nothing inside needs occlusion.

Runtime only (never stored): each building gets a small integer **index** into the GPU building table (§6.4). Merged meshes carry it per vertex; single-building renderers carry it via the renderer user value.

Derived values, never stored: `floorBase(k)`, `floorH(k)`, `roofY`, `shaftTop`, `shaftReachesRoof`, slab holes, window placement, and collision.

Edge-indexed data (entrances, blank edges) is re-mapped when corners are inserted or removed (see `insertVertex` / `removeVertex` in the prototype).

## 4. Generation (Unity)

- **One mesh per LOD per building** (LOD0 has an opaque and a glass submesh). There are no per-floor objects: floors above the active one are removed by the clip plane in the shader (§5), which also removes their shadows. This keeps a 10-floor building at 1–2 draws instead of 22.
- Exterior walls sit **outside** the footprint line (inner face on the line). Slabs fill the footprint exactly, so walls and slabs never z-fight and the facade is continuous.
- Interior walls, core walls and props stop at the **underside of the slab above** (`floorH - slab`). They never reach the next floor's surface.
- Walls are generated from **openings lists** (`u0,u1,y0,y1`) as **wall panels** (§4.2): each face is one polygon with the openings as holes (doors as notches in the outline), plus the four reveal faces per opening. The same routine handles windows, doors, interior doorways and bulkhead doors.
- LOD0 vertices carry **occlusion data** (only the building the player is in is ever cut away, and it is always at LOD0):
  - `uv2.xy` = wall line point (world xz), `uv2.zw` = wall normal (xz)
  - `uv3.x` = kind (0 = floor/stairs, 1 = wall, 2 = prop)
  - Every LOD also carries a **building tag** (`building index + 65536 × LOD`) so shared materials and merged meshes can look up per-building state.

  In Unity, bake these into mesh UV channels. For props, use the prop's pivot rather than a per-renderer property, because MaterialPropertyBlocks break batching (§6.4).
- Collision: the prototype uses 2D segments per floor. In Unity, generate **one MeshCollider (or box colliders) per floor**, with stairs as ramp colliders. Keep a lightweight segment list for navigation / AI if needed.
- Rebuild on every edit, but only for the dirty building, and only the LODs currently shown (hidden LODs are dropped and rebuilt on demand). The generators are written as **resumable jobs that yield after every storey**. The prototype time-slices them within a 6 ms per-frame budget. The Unity version runs the same steps in Burst jobs writing into `Mesh.MeshData` (§6.5).

### 4.1 No z-fighting: generation rules

Z-fighting comes from two faces that share a plane, face the same way, and overlap. The generator avoids ever producing that:

1. **Corners are mitered on the bisector plane.** Every strip that runs along a footprint edge (wall, floor band, plinth, parapet, parapet cap) ends on the corner's bisector. Neighbours meet face-to-face and never overlap: `u_end(w) = L + σ·tan(θ/2)·w` and `u_start(w) = −σ·tan(θ/2)·w`, where `θ` is the corner's turn angle, `σ = +1` for convex and `−1` for reflex corners, and `w` is the distance out from the footprint line. The first version extended each wall by its thickness at every corner, and that alone accounted for most of the ~14,000 overlapping face pairs.
2. **Nothing touches the next floor's surface.** Interior, core and lift walls end at the slab underside, so their tops can't coincide with the floor above.
3. **Parts touch face-to-face, never side-by-side.** Door frames sit 1 cm proud of the opening, frame heads start where jambs end, trims on flush windows (curtain / ribbon / shopfront) don't overhang into neighbouring openings, windows keep 14 cm clear of a neighbouring wall at reflex corners, and props are modelled so their boxes don't share outer faces.
4. **Scene layers are ≥ 1 cm apart**, and the camera near plane is 0.3 m (about 2 mm depth precision at 100 m). Editor highlights are padded so they never share a plane with geometry, and the editor floor grid sits 3 cm above the slab and doesn't write depth.

**Automated check (make this a Unity edit-mode test).** For every generated mesh, bucket triangles by plane (normal + offset). Clip each same-plane pair and flag any overlap area > 2 cm², unless a point just in front of the overlap lies inside another solid (buried faces can't be seen). The prototype passes with **0 visible overlaps** across the demo plus 30 generated variants (every footprint preset × every window style, entrances on every edge, parapet on/off, walk-in and shell).

### 4.2 Optimised mesh topology

| Rule | What it removes |
|---|---|
| **Wall panels, not stacked boxes.** Each face of a wall (outer, inner) is one polygon with the openings cut out, triangulated once (earcut) with shared vertices. Openings get exactly four reveal quads. | The touching faces between pier, sill and head pieces; T-junctions along piece edges (cracks and sparkles); duplicated vertices. |
| **Never emit a face that another part covers.** Wall tops and bottoms (covered by the slab, the next storey or the parapet); mitered wall ends at corners; the back faces of trims, bands, plinths, canopies and door jambs (against the wall); faces of furniture parts that rest on or press against another part; stair treads against the centre wall; core wall ends against the back wall. | Faces the camera can never see. Cut views still look solid because back faces render as the section cap. |
| **Slabs are one triangulated polygon per side** with shared vertices, holes for stairs, and side faces only around the holes. The ground slab under a shell building and the ceiling under a roof-only LOD are not generated. | Per-triangle vertex copies; faces sitting on the ground. |
| **Glass is a single pane** (two-sided for walk-in buildings, one-sided and opaque elsewhere). | The 6-face glass boxes. |
| **Compact vertex format:** float3 position, SNorm8 normal, UNorm8 colour, float building tag; occlusion data only on LOD0. 16-bit indices whenever a mesh has ≤ 65,535 vertices. | Roughly half the vertex memory. 32-bit indices aren't supported on some older mobile GPUs (for example Mali-400), so city cells should stay under the 16-bit limit. |
| **Unity:** after writing each mesh, call `Mesh.Optimize()` (index then vertex reorder for the GPU caches; runtime API for procedural meshes), and set `indexFormat` explicitly. | Vertex cache misses. |

Measured in the prototype (same buildings, same generator settings):

| | Before | After |
|---|---|---|
| Linden Court (10 floors, walk-in), LOD0 triangles (opaque mesh; the glass pane mesh adds ~950) | 44,828 | **31,940** (−29%) |
| Linden Court, LOD0 vertices | 89,860 | **60,830** (−32%) |
| Linden Court, internal (fully covered) faces | 27% | **2%** |
| Westgate Tower (9-floor shell), LOD0 triangles | 14,370 | **7,170** (−50%) |
| Draw calls per building | up to 22 (per floor × opaque/glass) | 1–2, and LOD2 is shared per cell |
| Materials | 3 per building | 5 in total, shared |

## 5. Occlusion system (the key runtime feature)

Each layer runs every frame from a handful of global shader properties (`Shader.SetGlobalVector`). There is no per-building material state: the building being cut is identified by a global `_OccActiveBuilding` index compared against each vertex's building index (§6.4).

| Layer | Rule | Why |
|---|---|---|
| **Floors above** | In the active building only, discard fragments above `floorBase + floorH − slab` of the active floor (just under the slab above). | Removes every storey above, the roof, and stair flights and tall props poking up. Because the same discard runs in the shadow pass, the active floor is lit. |
| **Cutaway** | For kind = wall on the active floor, above `stubHeight`: discard if the wall's line separates camera and player: `sign(dot(cam - p, n)) != sign(dot(player - p, n))`. For props, discard above the stub if the prop is on the camera side of the player. | "Sims-style" walls-down: works for any wall orientation and any camera yaw with no raycasts. |
| **Section cap** | Back faces render as a flat dark colour. | Cut walls read as solid sections, not hollow shells. |
| **See-through cone** | Discard (4×4 Bayer dither) fragments inside a cone from camera to the player's chest, above the player's feet. Applies to all buildings and world props. | Handles other buildings, scenery, and edge cases the cutaway misses (for example the player hugging a wall). |
| **Silhouette** | Player drawn a second time with `ZTest Greater`. | The player is never lost. |
| **Shadow pass** | The same discard logic runs in the ShadowCaster pass. | Cut-away walls must not cast shadows into the room. |

The player's floor comes from height, with a threshold 0.9 m below each floor line. This gives natural switching halfway up the second flight of stairs.

**Unity implementation:** a URP Shader Graph (or HLSL include) sub-graph `BuildingOcclusion`, used by building, prop and world materials. Globals: `_OccCamPos`, `_OccFocus`, `_OccFeetY`, `_OccConeRadius`, `_OccStubHeight`, `_OccCamDirXZ`, `_OccActiveBuilding`, `_OccClipY`, `_OccCutOn`, `_OccCutBase`, `_OccCutTop`. Use alpha clipping (not transparency), so it stays in the opaque queue with no sorting issues. Add `_OccFade` if we want the cutaway to animate over ~0.15 s instead of snapping.

## 6. City scale: LODs and thousands of buildings

### 6.1 LOD levels (semantic, generated from the same data)

| LOD | Contents | Triangles (prototype) | Used when | Built |
|---|---|---|---|---|
| **LOD0** full | Everything: interior walls, furniture, cores, frames, see-through glass, occlusion data. | Linden Court 31,940 · Harbor Office 15,080 · 38-floor tower 78,916 | Within ~60 m (feature scale ≥ 16 px/m), plus the building the player is in or the designer is editing, always | On demand, a storey per step |
| **LOD1** shell | Outer faces only; recessed opaque windows and doors; floor bands, plinth, canopies, parapet, roof units, stair/lift bulkheads. No interior, frames or transparency. | 4,150 · 3,062 · 26,818 | Up to ~240 m (≥ 4 px/m) | On demand, a storey per step |
| **LOD2** massing | One quad per footprint edge for the full height, parapet (inner face and cap), and the roof polygon. Windows and bands come from the facade shader, from the same parameters as the geometry, so they don't move at the switch. Sub-pixel windows fade to their average colour. | 40 · 54 · 26 (about 7 per footprint edge) | Beyond LOD1 range, out to the far plane | Always resident; merged per cell |
| **Culled** | — | 0 | Past the far plane / fog | — |

Why semantic LODs rather than decimation: automatic simplification of boxy architecture collapses window openings and trims first, and gives no way to drop interiors or swap to a shader-drawn facade. The same data drives all three generators, so the LODs always agree.

### 6.2 LOD selection

- **By feature scale, not object size.** Each frame, compute how many pixels one metre covers at the building's *nearest point*: `pxPerMetre = screenHeightPx / (2·tan(fov/2)·distance) × lodBias`. Frames and furniture only matter up close, whatever the size of the building. A size-based metric (Unity's default) asks for full detail on a 130 m tower 200 m away. The prototype hit exactly this: 331 LOD requests queued at street level before the switch.
- **Thresholds:** LOD0 ≥ 16 px/m, LOD1 ≥ 4 px/m, LOD2 ≥ 0.3 px/m, with 12% hysteresis on every boundary. The bias slider scales all three.
- **Forced LOD0** for the building the player is in and the building being edited.
- **Cross-fade:** 0.35 s dithered fade between the old and new LOD, drawn as complementary patterns so they never z-fight.
- **While a detail LOD is still building,** the best available LOD is shown.
- **Cost:** selection for 3,005 buildings takes about 1 ms of CPU per frame in the prototype.
- **Unity mapping:** use a `LODGroup` per building with `fadeMode = CrossFade`. Compute each building's transition heights from its size so the switches happen at fixed feature scales: `relativeHeight_i = size × PX_i / referenceScreenHeight`. Under the GPU Resident Drawer, LODGroup and dithered cross-fade work but *animated* cross-fade isn't supported, so use `fadeTransitionWidth` instead of `animateCrossFading`. URP's *LOD Cross Fade* setting must be on.

### 6.3 Far distance: merged cells (HLOD)

- **One mesh per 128 m cell.** All LOD2 massings in a cell are merged into a single mesh with shared vertex attributes. That is one draw call per cell, frustum-culled as a unit.
- **Buildings shown at LOD0/LOD1 are switched off inside the merged mesh by the vertex shader**, which reads the building table. So cells never rebuild on LOD changes, only when a member building is edited (0.08 ms per cell in the prototype).
- **Numbers:** the 3,000-building test city uses 316 cells. The overview draws about 110 calls and 80k triangles.
- **Unity:** no maintained HLOD system exists. Unity-Technologies/HLODSystem hasn't changed since early 2023 and targets 2021.3, and Entities Graphics dropped its HLOD in 1.0. So build our own cells. Bake cell meshes at edit time (they depend only on data), keep them under 65,535 vertices, and make them `MeshRenderer`s so the GPU Resident Drawer and GPU occlusion culling handle them.

### 6.4 Per-building data without per-object materials

- **Five shared materials for the whole city:** opaque, glass, massing (facade shader), and their shadow-caster variants.
- **Building table:** a `GraphicsBuffer` (StructuredBuffer) bound with `Shader.SetGlobalBuffer`. Per building it holds:
  - displayed LOD, previous LOD and fade (updated per frame, only when something changes);
  - storey heights, window specs and colours (updated on edit).

  The prototype uses two float textures with the same layout.
- **Finding a building's row:** merged meshes carry the building index per vertex. A single-building renderer can use the vertex attribute too, or, on Unity 6.3+, `MeshRenderer.SetShaderUserValue(uint)` / `unity_RendererUserValue`, which is compatible with the SRP Batcher and the GPU Resident Drawer.
- **Do not use MaterialPropertyBlocks.** A renderer with a MaterialPropertyBlock is SRP-Batcher-incompatible and is excluded from the GPU Resident Drawer.
- **Reading the buffer from Shader Graph:** use a Custom Function node with a file-mode HLSL include. Declare the buffer globally, guard the preview with `SHADERGRAPH_PREVIEW`, and set it with `SetGlobalBuffer`. Vertex-stage buffer reads need storage-buffer support (`SystemInfo.maxComputeBufferInputsVertex` > 0), so OpenGL ES targets would need a texture fallback like the prototype's.

### 6.5 Generation, memory and streaming

- **LOD2 and cells:** tiny; generated for every building at load or edit (3,000 buildings plus their cells in about 0.5–1 s in JS). In Unity, bake them as assets.
- **LOD1 and LOD0:** built on demand as resumable jobs, largest on screen first. A 38-floor tower's LOD0 costs 100–150 ms in one go in the prototype, but about 3 ms per storey step. Unity: schedule Burst jobs writing into `Mesh.AllocateWritableMeshData`, apply on the main thread with `Mesh.ApplyAndDisposeWritableMeshData` (it validates little, so set bounds and submeshes yourself), then `Mesh.Optimize()`.
- **Caches:** least-recently-used, keeping at most 16 LOD0 and 260 LOD1 meshes that aren't on screen. The street-level test used 87 MB of geometry including caches, and the overview 12 MB.
- **Baked or runtime?** Recommendation: bake LOD2, cells and LOD1 at edit time (static, streamable with the scene or Addressables per district), and generate LOD0 at runtime near the camera and player, since interiors are the heavy part and only a handful are ever needed at once. Hero buildings can bake LOD0 too.
- **Colliders** come from the data, independent of the render LOD. A shell building's collider is its exterior ring; a walk-in building gets per-floor colliders, built when the player comes near.
- **Undo** stores only what an action touched (the affected buildings' JSON). Bulk operations snapshot everything. In the 3,000-building city, an edit, undo or redo takes about 3 ms, and restoring is exact.

### 6.6 Measured city test (prototype)

Headless Chromium with a software GPU: CPU timings are meaningful, frame rates are not.

| Scenario | LOD split (0 / 1 / 2 / culled) | Draw calls | Triangles |
|---|---|---|---|
| Demo street, 5 buildings | 3 / 2 / 0 / 0 | 8 | ~48k |
| 3,005 buildings, overview from ~800 m | 0 / 0 / 2,530 / 475 | ~110 | ~80k |
| 3,005 buildings, street level (after generation) | 8 / 58 / 2,939 / 0 | 76 | 471k |
| 1,005 buildings, looking down a street canyon | 14 / 78–90 / ~910 / 0 | 58–60 | 526–575k |

The coplanar check passes with 0 visible overlaps at LOD0, LOD1 and LOD2 across 35 test buildings.

### 6.7 Unity rendering setup for a city

- **Pipeline:** URP with the Forward+ (or Deferred+) rendering path, the SRP Batcher on, and the **GPU Resident Drawer** set to *Instanced Drawing*. This needs *BatchRendererGroup Variants = Keep All*. It doesn't run on OpenGL ES, and only MeshRenderers qualify.
- **GPU occlusion culling** is worth turning on for dense street-level views, where most buildings hide behind the first row. It needs the GPU Resident Drawer and Render Graph, and can cost more than it saves in open views.
- **No static batching:** it isn't compatible with the GPU Resident Drawer, and cells already merge the far geometry.
- **Mesh LOD (Unity 6.2+)** is not a substitute for the semantic building LODs: it can't change materials or renderers between LODs, and Unity recommends against combining it with LODGroup. It could still help imported props inside LOD0.
- **Budgets to validate on target hardware** (open question 9): draw calls, triangles and geometry memory, taking the street-level numbers above as the baseline.

## 7. Vertical circulation spec

- **Stairs core:** 2.6 × 5.2 m footprint. Switchback: near landing (1 m) → left flight up to a mid landing at half height → right flight up to the next near landing. The slab above has a hole over the flights, and the near landing stays part of the slab. Railings are generated automatically across the dead-end lanes on the bottom and top floors.
- **Lift core:** 2.4 × 2.4 m, door on the front face at every served floor. Entering the car opens a floor panel (G, 1…N, R). Rides move at 4.5 m/s.
- **Roof access:** a bulkhead (walls + roof + door) on the roof over a core that reaches the top floor and has `roofAccess`. Stairs default to roof access on; lifts default to off.
- **Floor edits:** insert/delete re-index `bottom` / `top`. `top = -1` follows the top floor. A core that only spanned a deleted floor is removed.

## 8. Implementation plan (Unity)

| Phase | Scope | Exit criteria |
|---|---|---|
| **1. Data + generation** | `BuildingData` model, footprint → slabs/walls/windows generator with the §4.1 rules, floor grouping, JSON import of prototype layouts, coplanar-face test | Prototype JSON loads in Unity and looks the same; coplanar test passes |
| **2. Occlusion** | `BuildingOcclusion` shader include, floor toggling, cutaway, cone, section caps, shadow pass, silhouette. Tuning panel in play. | Walk every floor of a 10-floor building from any camera yaw without losing the character |
| **3. Play-mode test rig** | Third-person controller, follow camera (orbit / zoom / pitch clamp), virtual joystick, stairs ramps, lift interaction. Only for testing authored buildings; game code may replace it. | Outside → lobby → stairs → roof → lift down, on device |
| **4. Editor: shape & facade** | Scene-view handles (footprint corners, insert, delete, move), facade inspector with presets, entrance and blank-wall tools, undo via `Undo.RecordObject`, shell-only toggle | Designer makes a styled 3-floor building in < 2 min |
| **5. Editor: floors & interior** | Floor overlay (count, add/remove/duplicate/delete, copy layout up), wall/door/erase tools with snapping, core placement with range inspector, prop placement | 10 floors + rooftop access in < 5 min, remove floor 4 in one click |
| **6. City scale** | LOD1 and LOD2 generators, facade shader, building table (GraphicsBuffer), LOD manager on LODGroups with feature-scale transition heights, HLOD cells, Burst generation jobs, LRU caches, city generator for tests | 3,000-building test city at the §6.6 numbers or better; coplanar and LOD-consistency tests pass |
| **7. Polish** | Per-floor overrides (height, facade), prefab props, baked lighting strategy, streaming districts (Addressables) | Perf budget met on target device |

## 9. Editor UI (Unity mapping)

The prototype's UI was cut down so only the current task is on screen:

| Prototype | Unity Editor |
|---|---|
| Building switcher (one line; opens to rename / switch / new / duplicate / delete) | Selection in the Hierarchy plus a `Create ▸ Building ▸ Rect/L/U/T/Octa` menu. The inspector header shows name and floor count. |
| Tabs: Shape · Facade · Interior | Custom inspector for `Building` with three tabs. The active tab also sets the active **EditorTool** (footprint handles, facade picking, interior tools), so there's never more than one set of handles in the Scene view. |
| Shape: presets, two height sliders, rotate, snap | Inspector fields. Corner, insert and move handles are drawn with `Handles` in the Scene view. Edge lengths show only while dragging. |
| Walls as a graph: in the Interior tab every wall is a line with a point at each end. Walls that meet share a joint that moves as one. A **+** at a wall's middle splits it; double-clicking a joint joins two straight walls or removes the walls ending there. | `Handles.FreeMoveHandle` per joint and `Handles.DrawAAPolyLine` per wall, on the active floor's plane. The data stays a list of wall segments; joints are derived by matching endpoints (1 cm tolerance). |
| One snapping rule for drawing and dragging: points first (ring marker), then grid with axis guides (dashed), then onto a wall or footprint edge (diamond marker). A point landing on the middle of a wall splits that wall into a T-junction, so the walls stay connected. Alt disables snapping. | Same rule in a shared `WallSnap` utility, previewed in the Scene view with `Handles.DrawWireDisc` and a dotted guide line. |
| Facade: style presets, window type, three colour rows, *Entrance / Blank wall* picking; everything else under **More options** | Presets are `FacadeStyle` ScriptableObject assets (so they're shared and versioned). A foldout holds the less-used fields. |
| Interior: walk-in / shell, one-row tool strip, selection inspector; interior colours under a foldout | A **Scene view overlay toolbar** (Overlays API) for tools. Selected cores and props use the standard inspector. |
| Floor card: count ± always; floor list only in Interior | A Scene view overlay panel. It stays collapsed to the count field except while the Interior tool is active. |
| Hints only when nothing is selected; tool tips inline under the tool strip | Scene view notification (`SceneView.ShowNotification`) for one-off tips. Shortcuts are registered with the `ShortcutManager`, so they're rebindable. |

## 10. Open questions for the team

1. ~~Editor-only or in-game?~~ **Decided: editor-only.** Players don't build. The play-mode rig is a test harness.
2. **Per-floor heights and facades:** is a single ground height plus a single upper height enough, or do we need a penthouse / setback floor with a smaller footprint? Setbacks would mean footprints per floor range.
3. **Curved walls / non-orthogonal interiors:** footprints can be any polygon, but cores and props rotate in 90° steps. Is that acceptable?
4. **Doors as objects:** the prototype has open doorways only. Do we need doors that open or close, or lock?
5. **Occlusion default:** cutaway + cone together reads best in the prototype. Should the cutaway height or fade be a player setting?
6. **Floors below the player:** they are visible now (you see them through the cone from outside). Should they be dimmed for readability?
7. **Art pipeline:** will facades stay procedural (like here) or be assembled from modular prefab kits (wall / window / corner pieces)? The data model supports both. Only the generator changes.
8. **Multiple buildings sharing walls (terraces):** needed? It affects collision and occlusion between adjacent buildings.
9. **Target platforms and budgets:** which devices, and what draw-call, triangle and memory budgets? Mobile rules out the GPU Resident Drawer on OpenGL ES and makes the 16-bit index limit and texture fallback for the building table mandatory.
10. **World size and streaming:** how many buildings in one scene, and do districts stream in and out? That decides whether cells are baked per district and loaded with Addressables.
11. **LOD1 for tall glass towers:** the 38-floor curtain-wall tower is 26.8k triangles at LOD1 (a recess per window). Options: merge flush openings per storey at LOD1, or move to the facade shader earlier with a parallax ("interior mapping") effect.

## 11. Unity package layout

Standard UPM layout. Folders ending in `~` aren't imported, and samples must be listed in `package.json`.

```text
com.<studio>.storey/
  package.json            name "com.<studio>.storey", version, displayName, unity "6000.0" (6000.3 for renderer user values), dependencies:
                          com.unity.render-pipelines.universal, com.unity.burst, com.unity.collections, com.unity.mathematics
  README.md  CHANGELOG.md  LICENSE.md
  Runtime/                <Studio>.Storey.asmdef       data model, generators (Burst), LOD manager, building table, occlusion globals, colliders
    Shaders/              BuildingOcclusion.hlsl, Facade.hlsl, Shader Graphs (opaque, glass, massing)
  Editor/                 <Studio>.Storey.Editor.asmdef (Editor only)  inspector tabs, EditorTools, Scene view overlays, bake-to-asset, city generator
  Tests/Editor/           coplanar-face test, topology census (internal faces, T-junctions), LOD consistency (window positions LOD1 = LOD2 shader)
  Tests/Runtime/          play-mode physics (stairs, lifts, roof), LOD selection/perf test on a generated 3,000-building city
  Samples~/DemoStreet/    the prototype's demo buildings, imported from its JSON
  Documentation~/
```

The prototype's layout JSON is the interchange format for phase 1: the Unity importer should reproduce the same buildings from it, and the tests above should give the same results.
