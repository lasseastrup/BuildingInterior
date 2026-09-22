# Building Creation Tool: Prototype Findings & Unity Plan

Status: draft, written with the web prototype in `prototype/index.html`
Goal: designers can build a good-looking building with 10 floors and rooftop access in a few minutes, and remove a floor just as quickly. During play, the player always sees the floor they are on, from any camera angle.

---

## 1. What the prototype does

| Area | Prototype behaviour |
|---|---|
| **Modes** | *Edit*: set up one or more buildings. *Play*: third-person character, follow camera, virtual joystick + WASD. `P` / `Esc` switch. **Walk it** starts play on the floor you're editing. |
| **Footprint** | Drag corner handles; click `+` on an edge to insert a corner; double-click a corner to delete it. Grid snap (25 cm), plus axis alignment with neighbouring corners. Edge lengths are shown live. Presets: Rect, L, U, T, Octagon. Move the building with the centre handle; Rotate 90°. |
| **Facade** | Five one-click style presets. Window type (none / punched / tall / ribbon / curtain wall), width, bay spacing. Ground-floor treatment (shopfront / match / solid). Floor bands, parapet, rooftop units, colour swatches with custom pickers. Facade tools: *Entrance* (click a ground-floor wall) and *Blank wall* (switch one wall's windows off). |
| **Floors** | Floor stack on the right: count field (type `10` to get 10 floors), `+` / `−`, per-floor *duplicate above* and *delete*. **New floors copy the top floor's layout** (can be turned off). *Copy layout to floors above*. `[` `]` step floors, `+` `−` add or remove the top floor. |
| **Interior** | Tools: Select, Wall (click-chain or drag, snaps to corners and walls), Door (toggle a doorway on any wall, and on exterior walls at ground level), Erase, Stairs, Lift, Furnish (8 test props). Floors above the active one are hidden and drawn as outline "ghosts". |
| **Vertical circulation** | Stairs and lifts are **building-level cores** with a floor range (`bottom` → `top`, where `top = -1` means "follow the top floor") and a **roof access** flag. Adding floors extends them automatically. Deleting floors re-indexes them. Slabs are cut automatically where stairs pass through. Roof access generates a bulkhead with a door. |
| **Occlusion** | Floors above the player are hidden. The rest is handled per fragment in the shader: height clip, **cutaway** (walls between camera and player drop to a stub), **see-through cone** (dithered hole from camera to player), and a character silhouette. All of it runs live during play, with the modes and parameters exposed for tuning. |
| **Data** | The whole layout is one JSON document (JSON button). Undo/redo covers every edit. Autosaves to local storage. |

## 2. Workflow principles that proved out

1. **Floors are cheap and the layout is inherited.** The typical case is "same floor ×N". Adding a floor clones the top floor, so 10 floors takes one number entry. Designers only touch the floors that differ (lobby, penthouse).
2. **Cores span floors instead of living on one floor.** A stair or lift is placed once and owns its floor range. It extends when floors are added, so rooftop access never needs rework. This is the single biggest QoL win over placing stairs per floor.
3. **Nothing is destructive without undo.** Delete floor / building / footprint preset are all one Ctrl+Z away. That's why the prototype has no confirmation dialogs.
4. **The edit view and play view use the same occlusion.** In the Interior tab the active floor is sliced and cut away exactly as in play, so what the designer sees is what the player gets.
5. **Everything snaps.** Grid, corners, walls, and axis lock while drawing walls. Free placement is the exception (hold Alt when dragging corners).
6. **Test instantly.** *Walk it* drops the character on the floor being edited, next to the nearest core.

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

Derived values, never stored: `floorBase(k)`, `floorH(k)`, `roofY`, `shaftTop`, `shaftReachesRoof`, slab holes, window placement, and collision.

Edge-indexed data (entrances, blank edges) is re-mapped when corners are inserted or removed (see `insertVertex` / `removeVertex` in the prototype).

## 4. Generation (Unity)

- **One mesh per floor level** (opaque + glass submeshes), grouped under `Floor_k` GameObjects so a whole floor can be shown or hidden with one toggle. Roof level is `Floor_N`.
- Exterior walls sit **outside** the footprint line (inner face on the line). Slabs fill the footprint exactly, so walls and slabs never z-fight and the facade is continuous.
- Walls are generated from **openings lists** (`u0,u1,y0,y1`): piers between openings, a sill piece below each, a head piece above. The same routine handles windows, doors, interior doorways and bulkhead doors.
- Every vertex carries **occlusion data**:
  - `uv2.xy` = wall line point (world xz), `uv2.zw` = wall normal (xz)
  - `uv3.x` = kind (0 = floor/stairs, 1 = wall, 2 = prop)

  In Unity, bake this into mesh UV channels. For props, use the prop's pivot as a per-renderer property instead.
- Collision: the prototype uses 2D segments per floor. In Unity, generate **one MeshCollider (or box colliders) per floor**, with stairs as ramp colliders. Keep a lightweight segment list for navigation / AI if needed.
- Rebuild is fast enough to run on every edit: the 10-floor demo block regenerates in about 50 ms in the browser prototype, measured headless, and it's un-optimised JS. In Unity, rebuild only the dirty building (ideally only the dirty floor). During drags, throttle to once per frame.

## 5. Occlusion system (the key runtime feature)

Each layer runs every frame from a handful of global shader properties (`Shader.SetGlobalVector`) and one per-building MaterialPropertyBlock:

| Layer | Rule | Why |
|---|---|---|
| **Floor visibility** | Deactivate `Floor_k` for every `k > playerFloor` in the building the player is in. | Cheapest possible cull; also removes their shadows so the active floor is lit. |
| **Ceiling clip** | Discard fragments above `floorBase + floorH` of the active floor. | Removes stair flights, lift cars and tall props poking into the floor above. |
| **Cutaway** | For kind = wall on the active floor, above `stubHeight`: discard if the wall's line separates camera and player: `sign(dot(cam - p, n)) != sign(dot(player - p, n))`. For props, discard above the stub if the prop is on the camera side of the player. | "Sims-style" walls-down: works for any wall orientation and any camera yaw with no raycasts. |
| **Section cap** | Back faces render as a flat dark colour. | Cut walls read as solid sections, not hollow shells. |
| **See-through cone** | Discard (4×4 Bayer dither) fragments inside a cone from camera to the player's chest, above the player's feet. Applies to all buildings and world props. | Handles other buildings, trees, and edge cases the cutaway misses (for example the player hugging a wall). |
| **Silhouette** | Player drawn a second time with `ZTest Greater`. | The player is never lost. |
| **Shadow pass** | The same discard logic runs in the ShadowCaster pass. | Cut-away walls must not cast shadows into the room. |

The player's floor comes from height, with a threshold 0.9 m below each floor line. This gives natural switching halfway up the second flight of stairs.

**Unity implementation:** a URP Shader Graph (or HLSL include) sub-graph `BuildingOcclusion`, used by building, prop and world materials. Globals: `_OccCamPos`, `_OccFocus`, `_OccFeetY`, `_OccConeRadius`, `_OccStubHeight`, `_OccCamDirXZ`. Per-building: `_OccClipY`, `_OccCutOn`, `_OccCutBase`, `_OccCutTop`. Use alpha clipping (not transparency), so it stays in the opaque queue with no sorting issues. Add `_OccFade` if we want the cutaway to animate over ~0.15 s instead of snapping.

## 6. Vertical circulation spec

- **Stairs core:** 2.6 × 5.2 m footprint. Switchback: near landing (1 m) → left flight up to a mid landing at half height → right flight up to the next near landing. The slab above has a hole over the flights, and the near landing stays part of the slab. Railings are generated automatically across the dead-end lanes on the bottom and top floors.
- **Lift core:** 2.4 × 2.4 m, door on the front face at every served floor. Entering the car opens a floor panel (G, 1…N, R). Rides move at 4.5 m/s.
- **Roof access:** a bulkhead (walls + roof + door) on the roof over a core that reaches the top floor and has `roofAccess`. Stairs default to roof access on; lifts default to off.
- **Floor edits:** insert/delete re-index `bottom` / `top`. `top = -1` follows the top floor. A core that only spanned a deleted floor is removed.

## 7. Implementation plan (Unity)

| Phase | Scope | Exit criteria |
|---|---|---|
| **1. Data + generation** | `BuildingData` model, footprint → slabs/walls/windows generator, floor grouping, JSON import of prototype layouts | Prototype JSON loads in Unity and looks the same |
| **2. Occlusion** | `BuildingOcclusion` shader include, floor toggling, cutaway, cone, section caps, shadow pass, silhouette. Tuning panel in play. | Walk every floor of a 10-floor building from any camera yaw without losing the character |
| **3. Play** | Third-person controller, follow camera (orbit / zoom / pitch clamp), virtual joystick, stairs ramps, lift interaction | Street → lobby → stairs → roof → lift down, on device |
| **4. Editor: shape & facade** | Scene-view handles (footprint corners, insert, delete, move), facade inspector with presets, entrance and blank-wall tools, undo via `Undo.RecordObject` | Designer makes a styled 3-floor building in < 2 min |
| **5. Editor: floors & interior** | Floor stack window (count, add/remove/duplicate/delete, copy layout up), wall/door/erase tools with snapping, core placement with range inspector, prop placement | 10 floors + rooftop access in < 5 min, remove floor 4 in one click |
| **6. Polish** | Per-floor overrides (height, facade), prefab props, baked lighting strategy, perf pass (static batching per floor, LODs for distant buildings) | Perf budget met on target device |

## 8. Open questions for the team

1. **Is this an editor-only tool, or does the player also build in-game?** The prototype mirrors both. Answering this decides whether we build Unity Editor tooling (EditorWindow + scene handles) or a runtime builder UI.
2. **Per-floor heights and facades:** is a single ground height plus a single upper height enough, or do we need a penthouse / setback floor with a smaller footprint? Setbacks would mean footprints per floor range.
3. **Curved walls / non-orthogonal interiors:** footprints can be any polygon, but cores and props rotate in 90° steps. Is that acceptable?
4. **Doors as objects:** the prototype has open doorways only. Do we need doors that open or close, or lock?
5. **Occlusion default:** cutaway + cone together reads best in the prototype. Should the cutaway height or fade be a player setting?
6. **Floors below the player:** they are visible now (you see them through the cone from outside). Should they be dimmed for readability?
7. **Art pipeline:** will facades stay procedural (like here) or be assembled from modular prefab kits (wall / window / corner pieces)? The data model supports both. Only the generator changes.
8. **Multiple buildings sharing walls (terraces):** needed? It affects collision and occlusion between adjacent buildings.
