# Building Creation Tool: Prototype Findings & Unity Plan

Status: draft, written with the web prototype in `prototype/index.html`
Scope: **editor-only tool.** Buildings are authored in the Unity Editor and baked into scenes; players never build. Play mode in the prototype stands in for Unity's Play mode, to test occlusion and circulation.
Goal: designers can build a good-looking building with 10 floors and rooftop access in a few minutes, and remove a floor just as quickly. During play, the player always sees the floor they are on, from any camera angle. The same buildings must scale to **a full city of thousands of buildings**, shipped as a Unity package (§6, §11).

---

## 1. What the prototype does

| Area | Prototype behaviour |
|---|---|
| **Modes** | *Edit*: set up one or more buildings. *Play* (stands in for Unity Play mode): third-person character, follow camera, virtual joystick + WASD. `P` / `Esc` switch. From the Interior tab, Play starts on the floor you're editing. |
| **Footprint** | Drag corner handles; click `+` on an edge to insert a corner; double-click a corner to delete it. No grid: corners move freely (stored to the centimetre) and align to neighbouring corners on each axis (Alt for fully free). Edge lengths are shown live. Presets: Rect, L, U, T, Octagon. Move the building with the centre handle; an edge that comes within 0.5 m of a neighbour's edge snaps onto it (making a shared wall), and base corners snap to neighbours' corners and edges. An orange bar outside each edge pushes that whole wall in or out, keeping the neighbouring walls' angles. |
| **Setbacks** | Any floor can start a new outline that every floor above inherits (until the next setback). The Shape tab lists the outlines (*Base G–5*, *Setback 6–9*); **Add setback** splits the selected one halfway up, **Starts at** moves it, **Inset 1.5 m** pulls every edge in, and the handles then edit that outline at its own height with the outline below drawn dashed. A setback can step in or out: at least a quarter of it must rest on the floor below. The roof it leaves uncovered becomes a walkable **terrace** with a parapet (a door onto it is placed with the Entrance or Door tool), or, with *Uncovered roof below › Roof*, a **pitched roof** (with its own pitch) that runs up against the walls above; where it sticks out (an **overhang**), it gets a soffit underneath and a fascia under its wall. Each setback can have its **own facade style** (Facade tab › *Style for*); one without its own looks like the floors below. Demo: Linden Court's L loses its wing from floor 6; Overlook cantilevers a glass section out from floor 3 and steps back with a terrace at floor 6. |
| **Facade details** | AC units, vents, dishes, fire escapes and awnings on the outside of walls, placed by clicking a wall (Facade tab › palette) or by the style's rules (densities per style, deterministic). They ride the building's own LOD meshes, drop with the cutaway and squash with Sink. Adding a kind in Unity is a prefab plus a definition asset (§4.4). Demo: Linden Court's wing has a fire escape; Corner Shop has awnings and a dish. |
| **Roofs** | Facade tab › *Roof*: Flat (parapet or curb, walkable, roof access), **Hip**, **Gable** or **Shed**, with a pitch (5–60°) and an eave overhang (0–1.2 m). Works on any outline, including L, U, T, octagons and setback tops. Pitched roofs have no roof access. Demo: Row House (gable), Dockside Store (shed), Westgate Tower (hip). Some low blocks in the generated test city get gable or hip roofs. |
| **Storey heights** | Ground and upper defaults in the Shape tab; any floor can override its height (Interior tab › *Storey height*, shown in the floor list). Overlook has a 5 m ground floor. |
| **Shared walls** | Buildings built wall to wall share one party wall: the taller one builds it (blank, its outside face in the neighbour's interior colour) wherever the neighbour reaches, and the neighbour leaves its wall, bands and parapet out along it. Above the neighbour's roof the wall is a normal facade again. Demo: Row House between the Corner Shop and the Dockside Store. | Upper-floor **doors onto a neighbour's roof or terrace** at the same level join connected buildings (§4.3).
| **Facade** | Five one-click style presets. Window type (none / punched / tall / ribbon / curtain wall), width, bay spacing. Ground-floor treatment (shopfront / match / solid). Floor bands, parapet, colour swatches with custom pickers. Facade tools: *Entrance* (click a ground-floor wall, or a setback wall facing a terrace) and *Blank wall* (switch one wall's windows off, per outline). |
| **Floors** | Floor card on the right: count field (type `10` to get 10 floors) and `+` / `−`. **New floors copy the top floor's layout.** In the Interior tab the card expands into the floor list, with per-floor *copy layout to all floors above*, *duplicate* and *delete*. `[` `]` step floors, `+` `−` add or remove the top floor. |
| **Interior** | *Walk-in interior* or *Shell only* per building (see §3). Walls are edited like the footprint: drag joints, split with **+**, double-click to join or remove. Tools: Select, Wall (click-chain or drag, snaps to points and walls with visible markers, T-junctions split the host wall), Door (toggle a doorway on any wall, on exterior walls at ground level, and on walls facing a terrace), Erase, Stairs, Lift. Stairs and lifts turn to any angle: a new one lines up with the nearest wall, R turns it 90°, and the inspector has an angle field and *Align to wall*. No furniture or roof equipment is placed by the tool. Floors above the active one are hidden and drawn as outline "ghosts". |
| **Vertical circulation** | Stairs and lifts are **building-level cores** with a floor range (`bottom` → `top`, where `top = -1` means "follow the top floor") and a **roof access** flag. Adding floors extends them automatically. Deleting floors re-indexes them. Slabs are cut automatically where stairs pass through. Roof access generates a bulkhead with a door. |
| **Occlusion** | Floors above the player are hidden. The rest is handled per fragment in the shader: height clip, **cutaway** (walls between camera and player drop to a stub), **buildings in the way** (switchable sink, slice, cutout or fade, driven by a camera-to-player ray test), and a character silhouette. All of it runs live during play, with the modes and parameters exposed for tuning. |
| **Data** | The whole layout is one JSON document (**?** → Layout JSON). Undo/redo covers every edit. Autosaves to local storage. |
| **Rendering** | No z-fighting: generated geometry has zero visible coplanar faces at every LOD (automated check, §4.5). Optimised topology: walls are polygons with holes, and hidden faces are never generated (§4.5). |
| **City scale** | Three semantic LODs per building plus merged far-distance cells, picked per frame from on-screen feature size with a dithered cross-fade. Detail LODs build on demand, one storey per step, within a frame budget. The **stats card** (bar-chart button) shows the LOD split, draw calls and triangles, has a LOD-tint toggle, and can generate a 1,000- or 3,000-building test city (§6). |

## 2. Workflow principles that proved out

1. **Floors are cheap and the layout is inherited.** The typical case is "same floor ×N". Adding a floor clones the top floor, so 10 floors takes one number entry. Designers only touch the floors that differ (lobby, penthouse).
2. **Cores span floors instead of living on one floor.** A stair or lift is placed once and owns its floor range. It extends when floors are added, so rooftop access never needs rework. This is the single biggest QoL win over placing stairs per floor.
3. **Nothing is destructive without undo.** Delete floor / building / footprint preset are all one Ctrl+Z away. That's why the prototype has no confirmation dialogs.
4. **The edit view and play view use the same occlusion.** In the Interior tab the active floor is sliced and cut away exactly as in play, so what the designer sees is what the player gets.
5. **Snap to what is already there.** Corners, walls and axis alignment snap while drawing and dragging; there is no grid. Hold Alt for fully free placement.
6. **Test instantly.** Pressing Play from the Interior tab drops the character on the floor being edited, next to the nearest core.

## 3. Data model (Unity: ScriptableObject or serialised class, generated at edit time)

```text
BuildingData
  id, name
  position (x,z), all child coords are building-local
  footprint: Vector2[]            // any simple polygon, winding-agnostic (the base outline)
  groundHeight: float             // 3.0–6.0
  floorHeight: float              // 2.7–4.5 (all upper floors)
  floors: FloorData[]             // index 0 = ground, roof level = floors.Count
  cores: CoreData[]
  entrances: {edge:int, t:float, floor:int}[] // t = 0..1 along the edge; floor 0 = ground,
                                  // floor > 0 = terrace door on that setback's outline
  blankEdges: int[]               // of the base outline
  interior: bool                  // false = shell only (see below)
  style: FacadeStyle

FloorData
  walls: {a:Vector2, b:Vector2, doors:{t:float}[]}[]
  height?: float                  // overrides groundHeight / floorHeight for this storey
  shape?: Vector2[]               // setback: this floor and the ones above use this outline
  blankEdges?: int[]              // blank walls of that outline
  style?: FacadeStyle             // setback's own exterior style (interior colours stay the building's)

CoreData
  id, type: Stairs | Lift
  x, z, rot (degrees, any angle)
  bottom: int, top: int (-1 = follow top floor), roofAccess: bool

FacadeStyle
  preset, wall/trim/interior/floor/roof/core/glass colours,
  windows: None|Punched|Tall|Ribbon|Curtain, winWidth, baySpacing,
  ground: Shopfront|Match|Solid, bands, parapet,
  roofType: Flat|Hip|Gable|Shed, pitch (degrees), eave (m)   // the top tier's style decides the roof
```

**Shell-only buildings** (`interior = false`) keep their floor count, facade and roof, but generate no intermediate slabs, rooms or cores. Windows become opaque (tinted glass), entrances become closed doors, and the collider is a solid shell. Floor data is kept, so switching back restores the interior. Use them for background blocks: the 9-floor shell in the demo has about a third of the triangles of the 10-floor walk-in block, and nothing inside needs occlusion.

Runtime only (never stored): each building gets a small integer **index** into the GPU building table (§6.4). Merged meshes carry it per vertex; single-building renderers carry it via the renderer user value.

**Setbacks.** A floor with a `shape` starts a *tier*: it and every floor above use that outline until the next floor with a `shape`. The outline of floor `k` is `outline(k)` = the nearest `shape` at or below `k`, or the base footprint. Rules the editor enforces on every edit (the handle simply stops where a move would break one):
- A setback is a simple polygon, and at least a quarter of it (and 4 m²) rests on the tier below; the same holds for the tier above it. It may step in (terrace) and out (overhang) at once.
- Where a setback steps in, it leaves at least 0.8 m of terrace, or lines up exactly with the edge below. Anything thinner would put the setback's 0.3 m wall on top of the parapet. (Stepping out has no minimum.)
- A setback without its own `style` looks like the tier below it; the base uses the building's style.
- Every core fits inside the outline of every level it serves (stairs to the roof must fit the top tier).
- Floor operations carry setbacks along: duplicating or adding floors copies rooms but never the outline (the copy inherits it); deleting the first floor of a setback moves the outline and its terrace doors up one floor; a setback that becomes the ground floor becomes the new base footprint.

**Storey heights.** `floorH(k)` = the floor's `height`, or the ground / upper default; `floorBase(k)` is the running sum (cache it per building in Unity).

**Shared walls** are derived, never stored: see §4.3.

Derived values, never stored: `floorBase(k)`, `floorH(k)`, `roofY`, `outline(k)`, `styleAt(k)`, terraces, overhangs, party walls, `shaftTop`, `shaftReachesRoof`, slab holes, window placement, and collision.

Edge-indexed data (entrances, blank edges) is re-mapped when corners are inserted or removed (see `insertVertex` / `removeVertex` in the prototype), per outline. After a drag, corners that coincide or sit on a straight line are merged (`simplifyTier`), with doors keeping their position along the merged edge.

## 4. Generation (Unity)

- **One mesh per LOD per building** (LOD0 has an opaque and a glass submesh). There are no per-floor objects: floors above the active one are removed by the clip plane in the shader (§5); they still cast shadows (the clip is colour-pass only). This keeps a 10-floor building at 1–2 draws instead of 22.
- Exterior walls sit **outside** the footprint line (inner face on the line). Slabs fill the footprint exactly, so walls and slabs never z-fight and the facade is continuous.
- Interior walls and core walls stop at the **underside of the slab above** (`floorH - slab`). They never reach the next floor's surface.
- Walls are generated from **openings lists** (`u0,u1,y0,y1`) as **wall panels** (§4.6): each face is one polygon with the openings as holes (doors as notches in the outline), plus the four reveal faces per opening. The same routine handles windows, doors, interior doorways and bulkhead doors.
- LOD0 vertices carry **occlusion data** (only the building the player is in is ever cut away, and it is always at LOD0):
  - `uv2.xy` = wall line point (world xz), `uv2.zw` = wall normal (xz)
  - `uv3.x` = kind (0 = floor/stairs, 1 = wall, 2 = free-standing object, reserved for game-placed objects)
  - Every LOD also carries a **building tag** (`building index + 65536 × LOD`) so shared materials and merged meshes can look up per-building state.

  In Unity, bake these into mesh UV channels. For free-standing objects placed later by the game, use the object's pivot rather than a per-renderer property, because MaterialPropertyBlocks break batching (§6.4).
- Collision: the prototype uses 2D segments per floor. In Unity, generate **one MeshCollider (or box colliders) per floor**, with stairs as ramp colliders. Keep a lightweight segment list for navigation / AI if needed.
- Rebuild on every edit, but only for the dirty building, and only the LODs currently shown (hidden LODs are dropped and rebuilt on demand). The generators are written as **resumable jobs that yield after every storey**. The prototype time-slices them within a 6 ms per-frame budget. The Unity version runs the same steps in Burst jobs writing into `Mesh.MeshData` (§6.5).

### 4.1 Setbacks and terraces

Each storey is generated from `outline(k)`. At a floor `k` that starts a setback (lower = `outline(k−1)`, upper = `outline(k)`):

- **Slab:** the floor surface covers the upper outline (with stair holes); the **terrace deck** is `lower − upper`, a polygon difference that can have holes or several parts (the prototype uses the `polygon-clipping` library; in Unity use **Clipper2**); the underside covers the lower outline, and the **soffit** `upper − lower` covers the overhang. Deck, floor and soffit share edges exactly, so they never overlap.
- **Edge classification:** each edge of one outline is split where the other outline's edges cross or touch it, and each piece is *on* the other outline, *inside* it or *outside* it (midpoint test). Lower-edge pieces outside the upper outline carry the terrace parapet; upper-edge pieces outside the lower outline carry the overhang **fascia** (a strip under the upper wall, from the slab underside to the floor, closing the slab edge). Pieces inside the other outline need nothing: under an overhang the lower wall simply meets the slab.
- **Parapet:** A run (parapet or fascia) ends on the neighbouring run's miter at an open corner, or against the **outer face of the other outline's wall** where the two outlines meet, so they touch face-to-face. The same runs feed LOD0/1 boxes, LOD2 quads and the collision segments.
- **Roofed setback** (`floor.terraceRoof = {pitch}`, per setback; the editor's *Uncovered roof below: Terrace | Roof*): instead of a deck and parapet, the lower storey gets a **hip roof** over its whole outline, sitting on its wall tops at the setback height (0.3 m eave), built by the same straight-skeleton roof generator as §4.2, then **cut** in plan: away from the upper storeys out to their outer wall face, and out past the eave along any stretch where an upper wall runs flush with a lower one, the strips running on past a corner where two flush walls meet (so no eave strip or corner scrap clings to that facade). Sloped and flat pieces are cut by polygon difference and lifted back onto their plane; vertical pieces (fascias, walls) keep the stretches of their base line outside the cut. The same pieces go into LOD0, LOD1 and LOD2. Doors onto it close (there is nothing to step out on), and the option only shows when the setback leaves roof uncovered.
- **Terrace doors** are ordinary exterior doors on the setback's first floor, placed only where there's deck in front. Their threshold face is skipped because the deck is already there.
- **Interior walls** are trimmed to the storey's outline at generation time, so a copied layout that crosses a setback edge just stops at the facade. The stored walls are untouched.
- **Style per setback:** storey `k` is built with `styleAt(k)` (the tier's own style, else the tier below's, else the building's), with the building's interior colours. Decks, parapets and the roof take the style of the storey below them.
- **LOD2** is one extrusion per tier, split into **runs** of storeys with the same height and window type (the ground floor is its own run). Each run has a row (**slot**) in the parameter table: colours, window spec, storey height, number of banded storeys, run height and parapet mode. Every vertex carries only its slot index, and the top run's quad also covers the parapet, which the shader colours above the run height. Decks, parapets, soffits and fascias are extra quads. Cost measured on the 3,000-building test city: 61k overview triangles (50k before per-floor heights), 13 MB of LOD2 memory (10 MB before), and the same load time.
- **Play:** level `k` is walkable over `outline(k−1)` (floor and terrace) and over `outline(k)` (an overhanging floor); the parapet is in floor `k`'s collision.

### 4.2 Roofs

The roof is built from the top tier's outline and style (`styleAt(N)`), at every LOD from the same planar pieces.

- **Hip:** a **straight skeleton** of the outline pushed out to the eave line: every edge moves inward at the same speed and sweeps one roof face, so every face has the same pitch and hips and valleys fall where they belong on any simple polygon. Height = `wallTop − eave·tan(pitch) + tan(pitch) × (distance the edge travelled)`, so each face crosses the wall's outer face exactly at the wall top. The prototype runs a brute-force event simulation (edge events, split events, and closing wavefronts that have collapsed onto a ridge). It's tested on the presets plus 1,920 random rectilinear, chamfered and star-shaped outlines: every result tiles the footprint exactly, and the worst case took 9 ms. In Unity use the same algorithm, or a library (e.g. a CGAL-based straight skeleton) behind the same interface.
- **Gable:** the hip skeleton, then each triangular end face whose tip closes a ridge (exactly three faces meet there) becomes a **gable wall**: the tip moves out along the ridge line to the wall face, so the neighbouring faces stay planar. A square's pyramid has no ridge, so it stays hipped.
- **Shed:** one plane rising from the longest edge. Edges roughly parallel to it get eaves; the others are rakes.
- **Clipping:** faces are clipped (polygon intersection) to the real roof outline, which is the eave line on eave edges, the wall face on rake and gable edges, and the owner's wall face on a party edge this building doesn't own. A party edge it owns gets no eave.
- **Under the roof:** rake and gable edges get a wall following the roof profile up from the wall top, over the length of the building's wall. Eave edges get a fascia (15 cm) at their own roof-edge height and a horizontal soffit back to the wall face; where that is above the wall top (a shed's tall side) a wall rises from the wall top to the soffit. End caps close each eave box between its soffit and the roof slope at outer corners. The top storey's flat slab stays as its ceiling, enclosing the attic.
- **LOD2** draws the same pieces as extra triangles (roof, trim and wall colours from the tier's slot). The top run's quad stops at the wall top instead of carrying a parapet.
- **Play:** pitched roofs have no roof access (stairs can't continue to the roof), and nothing up there is walkable.

### 4.3 Shared walls

- **Detection:** for each outline edge, look up neighbours in the city grid and find their outline edges that are anti-parallel and collinear (within 1 cm) and overlap by at least 10 cm. That overlap is a **party range**.
- **Ownership:** each side's height along the range is how far its tiers run along it, counting up from the base. The taller side owns the range; on a tie, the lower id owns it. Both sides compute the same answer independently.
- **Owner:** its storeys along the range are built **blank** (no openings, bands or plinth) while they're below the neighbour's height. The outside face gets the neighbour's interior colour, because it is the neighbour's room wall. Above the neighbour's roof the facade is normal again, with windows looking out over that roof.
- **Neighbour:** leaves its wall, bands, plinth, roof parapet and terrace parapet out along the range. Its slabs run on to the shared line and meet the owner's wall. Walls are built outside their own outline, so each building's wall sits inside the other's footprint and nothing doubles up.
- **Occlusion:** the owner's party-wall vertices carry the neighbour's building index too (`kind + 8 × (neighbour + 1)` in the kind channel), so the cutaway also applies when the player is in the neighbour. The ceiling clip does not apply, so the wall still reads as the owner's building above.
- **Collision:** the owner's wall segments serve both buildings. Collision already merges the segments of every building nearby.
- **Updates:** editing or moving a building re-generates every building whose bounds touch its old or new position. LOD2 needs nothing special, because each side's massing hides the other's.
- **Editor:** moving a building snaps an edge onto a neighbour's anti-parallel edge within 0.5 m; base corners snap to neighbours' corners and edges.

**Doors between connected buildings.** A door on any upper floor may open onto a neighbour's **flat roof or terrace** that lies in front of it at the same level (within 35 cm up or down): `landingAt(b, k, edge, t)` probes the point 0.5 m outside the wall, at `floorBase(k)`, against the neighbours' top outlines (flat roofs only) and terrace decks. Such a door renders like any other exterior door on that storey (the wall there is the taller building's own wall, above the shared part, so it takes openings), gets a threshold face when the landing is lower (a flush landing is the neighbour's own slab), and closes automatically if the neighbour moves, grows or gets a pitched roof. In play the walkable surfaces run out under the wall thickness (`surfaceAt` accepts points within `T_EXT` of an outline), so the doorway between the two footprints has floor all the way across and the player simply walks from one building onto the other, where the occlusion follows them (§5). Doors are anchored to the tier of their floor, so outline edits remap them like terrace doors, and a setback move that changes a door's outline drops it. Editor: the Entrance tool accepts a click on any floor with a landing in front (message otherwise); the Interior Door tool likewise on an exterior wall. Demo: Row House floor 3 opens onto the Corner Shop's roof.

### 4.4 Facade details (AC units, fire escapes, awnings…)

Small things on the outside of a wall that make a block read as lived in. The prototype has five: AC unit, vent, dish (wall items), fire escape (a tier item: it climbs from its floor as far as its wall goes on, through any setback whose wall above is flush with it, and stops where the wall steps back), and awning (over a ground-floor window or door). The design goal is that an artist adds a new kind by dropping in a prefab, and every placed item works with the LODs, the cutaway and Sink without per-item code.

**Data.** A placement is `{kind, floor k, edge i of outline(k), t (fraction along the edge), y (metres above the floor; wall items only)}`, stored on the building (`b.details`), exactly like a door. Because it is anchored to an outline edge, the same edge-remap that keeps doors in place when a corner is added, removed or merged keeps details in place too (`remapEdgeItems`), a setback that moves or goes takes its details with it, and floor inserts and deletes shift `k`. A placement that no longer resolves (its edge is gone, its window was removed) is simply skipped at generation time rather than deleted, so the data is robust to edits.

**By hand or by rule.** Besides hand placement, each style has **rules** with a density per kind (`style.details = {ac: 30, vents: 12}`, percentages): AC units under a share of the punched windows (never on glass curtain, never on the ground floor), vents in the wall gaps between windows. Rule placements are seeded by `(building id, kind, floor, edge, index)`, so editing something else never reshuffles them, and they keep clear of hand-placed items on the same wall. Nothing goes on a party wall (§4.3). Hand placement is validated on click: wall items are clamped into the storey and refused across a window or door; awnings snap to the nearest opening; a fire escape is refused across a door (its stair would cross the canopy) or where its drop ladder would come down over one; a click on an existing hand-placed item removes it.

**Generation.** Details are generated **per storey**, into the building's own LOD meshes, right after that storey's facade (`buildDetails`). Each item takes the cutaway data of its host wall (`wallCtx`, kind = wall), so it drops with the wall in the walls-down cutaway, is clipped with the floors above, and, since every vertex lies inside its storey's height range, squashes correctly with Sink. A fire escape is one item that spans storeys: each storey builds its own platform, railing and the stair up to the next platform (alternating direction), the storey below the lowest platform builds the drop ladder, and the platform also appears in LOD2. Levels of detail:

| LOD | What a detail contributes |
|---|---|
| LOD0 | Full item: AC body, grille and brackets; vent with louvres; dish with arm; fire escape with posts, rails, steps, stringers and ladder rungs; awning with side flaps. |
| LOD1 | Simplified: body boxes only, fire escape as platform + outer rail + a sloped slab, awning without flaps. |
| LOD2 | Nothing, except a fire escape's platform slabs: at massing distance the rest is below feature scale. |

Items are built from boxes and planar polygons that never share a face with the wall or with the facade's own trims (the platform sits at floor + 0.24 m, above a curtain wall's bottom frame and the floor band; neighbouring awnings meet in the middle) (a box against the wall skips its inner face; anything on a face of another item is offset or has that face skipped), so the z-fighting rules of §4.5 hold; the overlap checker covers 85 test buildings with rule-placed and hand-placed details.

**Unity mapping.** This is where the artist workflow lives:

- **Catalogue asset:** `FacadeDetailDefinition` (ScriptableObject) per kind: display name and icon; **anchor** (`Wall`, `Tier`, `Opening`; later `RoofEdge`, `Ground`); footprint on the wall (w, h, depth) used for clamping, overlap tests and the ghost box; whether it spans storeys and builds a ladder below; per-LOD meshes (`lod0`, `lod1`, optional `lod2`; missing ones fall back to the next coarser or to nothing); collision (none by default). Pivot convention: at the wall contact point, +Z outward, +Y up, so placement is one transform from `(edge frame, t, y)`. A validator on import checks pivot, that the material is the building material (vertex-colour palette or the shared atlas), and triangle budgets per LOD.
- **Adding a kind** = drop a prefab into the catalogue folder and fill in the definition; it appears in the Facade tab palette and, if it has densities, in the rules asset. No code.
- **Rules asset** per project/style (`FacadeDetailRules`): density per kind, seed salt, exclusions (window kinds, ground floor), same deterministic hash as the prototype so a rebuild reproduces the same city.
- **Baking:** details are baked into the building's per-LOD meshes at edit time by the same generator that bakes the facade, copying the host wall's occlusion attributes per vertex (§5), so a building stays at 1–2 draws and every runtime system sees ordinary building geometry. For a very dense catalogue (hundreds of props per building) the alternative is a `BatchRendererGroup` instancer keyed by building index, with the wall data as per-instance floats; the shader is the same `BuildingOcclusion` include.
- **Editor:** palette in the Facade tab (click to place, click again to remove, drag along the wall to slide); densities on the style; hover ghost shows the clamped position and turns red when a placement is refused. Placements are part of the building asset and go through the same undo stack.

### 4.5 No z-fighting: generation rules

Z-fighting comes from two faces that share a plane, face the same way, and overlap. The generator avoids ever producing that:

1. **Corners are mitered on the bisector plane.** Every strip that runs along a footprint edge (wall, floor band, plinth, parapet, parapet cap) ends on the corner's bisector. Neighbours meet face-to-face and never overlap: `u_end(w) = L + σ·tan(θ/2)·w` and `u_start(w) = −σ·tan(θ/2)·w`, where `θ` is the corner's turn angle, `σ = +1` for convex and `−1` for reflex corners, and `w` is the distance out from the footprint line. The first version extended each wall by its thickness at every corner, and that alone accounted for most of the ~14,000 overlapping face pairs.
2. **Nothing touches the next floor's surface.** Interior, core and lift walls end at the slab underside, so their tops can't coincide with the floor above.
3. **Parts touch face-to-face, never side-by-side.** Door frames sit 1 cm proud of the opening, frame heads start where jambs end, trims on flush windows (curtain / ribbon / shopfront) don't overhang into neighbouring openings, windows keep 14 cm clear of a neighbouring wall at reflex corners.
4. **Terraces don't stack faces.** The deck is the exact polygon difference of the two outlines, parapets stop at the setback wall's outer face, and terrace doors have no threshold face.
5. **Scene layers are ≥ 1 cm apart**, and the camera near plane is 0.3 m (about 2 mm depth precision at 100 m). Editor highlights are padded so they never share a plane with geometry, and the editor floor grid sits 3 cm above the slab and doesn't write depth.

**Automated check (make this a Unity edit-mode test).** For every generated mesh, bucket triangles by plane (normal + offset). Clip each same-plane pair and flag any overlap area > 2 cm², unless a point just in front of the overlap lies inside another solid (buried faces can't be seen). The prototype passes with **0 visible overlaps** at LOD0, LOD1 and LOD2 across the demo plus 42 generated variants (every footprint preset × every window style, entrances on every edge, parapet on/off, walk-in and shell, and one- and two-step setbacks with terrace doors on every preset).

### 4.6 Optimised mesh topology

| Rule | What it removes |
|---|---|
| **Wall panels, not stacked boxes.** Each face of a wall (outer, inner) is one polygon with the openings cut out, triangulated once (earcut) with shared vertices. Openings get exactly four reveal quads. | The touching faces between pier, sill and head pieces; T-junctions along piece edges (cracks and sparkles); duplicated vertices. |
| **Never emit a face that another part covers.** Wall tops and bottoms (covered by the slab, the next storey or the parapet); mitered wall ends at corners; the back faces of trims, bands, plinths, canopies and door jambs (against the wall); stair treads against the centre wall; core wall ends against the back wall. | Faces the camera can never see. Cut views still look solid because back faces render as the section cap. |
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
| **Floors above** | In the active building only, discard fragments above `floorBase + floorH − slab` of the active floor (just under the slab above). | Removes every storey above, the roof, and stair flights poking up. The floors above still cast their shadow, so the active floor keeps the light it would have with the building intact (sun only through the windows). |
| **Cutaway** | For kind = wall on the active floor (including a party wall whose neighbour is the active building, §4.3), above `stubHeight`: discard if the wall separates camera and player: `sign(dot(cam - p, n)) != sign(dot(player - p, n))`, **and** the camera-to-player sightline crosses the wall's own extent (±0.5 m for the character's body), not just its infinite line, so a room beside the player keeps its front wall. Each wall vertex (facades, interior walls, stair and lift cores, parapets, fascias) carries its start point and its normal scaled by `1 + length`; a unit normal would mean no known extent (the line test alone). `cam` is the camera's offset from its target applied to the player's exact position, so the camera's follow lag never flips an edge-on wall. **Walls slide rather than snap:** every wall gets a global id (a block of ids is allocated when a LOD0 mesh is installed and freed when it is disposed; per vertex `aWid`, 0 = none). Each frame the CPU runs this test for the active building's walls and for neighbours' party walls shared with it, and eases a per-wall value 0..1 over 0.22 s into a float table indexed by id. The shader lowers the wall's top from the ceiling to the stub by that value (smoothstep), in the colour pass (cut walls keep casting shadows). Walls without an id fall back to the instant shader test. For free-standing objects (kind 2), discard above the stub if the object is on the camera side of the player. | "Sims-style" walls-down: works for any wall orientation and any camera yaw with no raycasts. |
| **Section cap** | Back faces render as a flat dark colour. | Cut walls read as solid sections, not hollow shells. |
| **Buildings in the way** | Other buildings between camera and player, found by a ray test (below). One of four designer-selectable modes; see the table below. | Handles walking behind a house, and a neighbour blocking the view into the building you're in. |
| **Silhouette** | Player drawn a second time with `ZTest Greater`. | The player is never lost. |
| **Shadow pass** | None of the occlusion discards (floors above, cutaway, buildings in the way) and none of Sink's displacement run in the ShadowCaster pass: it always casts from the whole building. Only the LOD cross-fade dither and isolate run there. | Occlusion is a view aid, not a change to the world: going inside or dropping a wall must not change the lighting. |

The player's floor comes from height, with a threshold 0.9 m below each floor line. This gives natural switching halfway up the second flight of stairs.

### 5.1 Buildings in the way

**Detection (CPU, per frame).** Cast three segments from the camera to the player's feet, chest and head. Test them against the prism volume of each nearby building's tiers (a ray that runs along or within 5 cm of an outline edge counts as a hit, so a ray straight down the seam between two joined buildings hits both) (each tier clipped to its own height range, plus the roof rise or parapet), using the spatial grid to find candidates. The player's own building is excluded, since the cutaway handles it, and so is a building whose door the player is standing at (within 1.2 m of it, on its floor), so it never flashes dark on the way in or out. The rays stop 0.5 m short of the player, so the character's own body touching a wall doesn't count. Each hit building keeps an animated amount `occ` that rises over 0.2 s and falls over 0.2 s after a 0.35 s hold, so a building doesn't flicker when a ray grazes a corner. Each active occluder takes one of 16 **occluder slots**: a small float table (Unity: a `GraphicsBuffer`) whose row holds a header (segment count, dark amount, clip height, solid base height) and, for Sink, one entry per storey (floor line, vertical scale, collapsed flag, slab-above-gone flag). The **w channel of the building's row in the per-building state table** (§6.4) points at the slot (0 = not in the way), so there are no per-object materials and no extra draw calls.

| Mode | Shader rule | Look | Trade-off |
|---|---|---|---|
| **Sink** (default) | Every storey from the player's height up collapses, one after another from the roof down (0.12 s each, staggered by at most 0.1 s in all; about 0.25 s in total). The vertex shader scales each storey linearly between its own floor lines, so every storey's geometry squashes with no gaps or cracks; a collapsed storey is discarded, and so is the slab above a storey once the storey on it has gone. In its place a small **footprint mesh** appears: the outline of that storey as a flat near-black fill 2 cm above its floor, ringed by a 0.3 m rim of the outer wall in its own colour, with no doors, canopies or interior. It is built once per building version and storey, and skips party-wall ranges. A party wall stays standing while its neighbour stands (it is the neighbour's wall too) and collapses when the neighbour is sinking as well (the shader checks the neighbour's state row), so joined buildings merge into one black outline with one rim. Storeys below the player stay, tinted near-black. Occluders are forced to LOD0. The shadow pass is untouched. Reverses when clear. | You always see that you're behind something: a clean black outline of the building, framed by a low wall. The collapse makes the change readable instead of a pop. | Storey-crossing geometry would distort mid-collapse, so it relies on the per-storey mesh layout. One extra small mesh per active occluder. |
| **Slice** | Discard fragments of that building above a cut height `y`: the ceiling of the storey at the player's height (at least player + 2 m). The row stores the cut height, which eases down from the roof. Colour pass only, so the shadow stays. Occluders are forced to LOD0 so their interiors show. | The neighbour drops to the player's storey like a dollhouse; you see into its rooms. | Strongest read and consistent with our own-building slice, but it changes the skyline while it's active. |
| **Cutout** | Below the base height (floor of the player's storey + 1 m) the building stays solid and near-black. Above it, discard with a Bayer dither inside a soft screen-space circle around the player's chest (radius in metres converted to pixels from distance and FOV), only for fragments nearer than the player (`depth < playerDepth`). Colour pass only. | A soft round window through whatever is in the way (as in *Divinity*, *Baldur's Gate 3* and similar). | Local and cheap; the building keeps its shape. Needs the hole size tuned per camera distance. |
| **Fade** | Keep the same solid dark base. Above it, dither the building to ~20% coverage when it is in front of the player (`depth < playerDepth`). Colour pass only, so shadows stay. | The building ghosts out (as in *Diablo*-style isometric games). | Simplest; can read as noisy on large buildings and at low resolution. |
| **Off** | Nothing. | For comparison, or for projects whose camera never goes behind buildings. | |

All modes keep the **silhouette** (on by default), so the player is never lost even during a fade-in. **Camera assist** (optional): when the player has been hidden for more than 0.5 s, the camera pitch eases up by ~0.3 rad until the view clears, then eases back.

**Unity implementation:** a URP Shader Graph (or HLSL include) sub-graph `BuildingOcclusion`, used by building, object and world materials. Globals: `_OccCamPos`, `_OccFocus`, `_OccStubHeight`, `_OccCamDirXZ`, `_OccActiveBuilding`, `_OccClipY`, `_OccCutOn`, `_OccCutBase`, `_OccCutTop`, `_OccMode`, `_OccPlayerDepth`, `_OccPlayerScreen`, `_OccHoleRadiusPx`, plus the occluder slot buffer `_OccSlots`. The per-building slot index is the state table's w channel; Sink's displacement goes in the vertex stage of the building shader and its ShadowCaster pass. Detection runs in a small `OccluderSystem` (Burst job over grid candidates, or `Physics.RaycastNonAlloc` against the building colliders). Use alpha clipping (not transparency), so it stays in the opaque queue with no sorting issues. The wall slide values are a `GraphicsBuffer` `_OccWallSlide` indexed by the wall id baked into a mesh UV channel; a small `CutawaySystem` updates it each frame.

## 6. City scale: LODs and thousands of buildings

### 6.1 LOD levels (semantic, generated from the same data)

| LOD | Contents | Triangles (prototype) | Used when | Built |
|---|---|---|---|---|
| **LOD0** full | Everything: interior walls, cores, frames, see-through glass, occlusion data. | Linden Court 31,940 · Harbor Office 15,080 · 38-floor tower 78,916 | Within ~60 m (feature scale ≥ 16 px/m), plus the building the player is in or the designer is editing, always | On demand, a storey per step |
| **LOD1** shell | Outer faces only; recessed opaque windows and doors; floor bands, plinth, canopies, parapet, terrace decks and parapets, stair/lift bulkheads. No interior, frames or transparency. | 4,150 · 3,062 · 26,818 | Up to ~240 m (≥ 4 px/m) | On demand, a storey per step |
| **LOD2** massing | One quad per outline edge for the height of each setback tier, parapet (inner face and cap), terrace decks and parapets, and the roof polygon. Windows and bands come from the facade shader, from the same parameters as the geometry, so they don't move at the switch. Sub-pixel windows fade to their average colour. | 40 · 54 · 26 (about 7 per footprint edge) | Beyond LOD1 range, out to the far plane | Always resident; merged per cell |
| **Culled** | — | 0 | Past the far plane / fog | — |

Why semantic LODs rather than decimation: automatic simplification of boxy architecture collapses window openings and trims first, and gives no way to drop interiors or swap to a shader-drawn facade. The same data drives all three generators, so the LODs always agree.

### 6.2 LOD selection

- **By feature scale, not object size.** Each frame, compute how many pixels one metre covers at the building's *nearest point*: `pxPerMetre = screenHeightPx / (2·tan(fov/2)·distance) × lodBias`. Frames and interiors only matter up close, whatever the size of the building. A size-based metric (Unity's default) asks for full detail on a 130 m tower 200 m away. The prototype hit exactly this: 331 LOD requests queued at street level before the switch.
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
  - displayed LOD, previous LOD, fade, and the occluder slot (§5.1) (updated per frame, only when something changes);
  - one row per LOD2 run (colours, window spec, storey height / bands / run height / parapet), referenced by a slot index on the LOD2 vertices and rewritten on edit. A building uses one row per run plus one per tier, typically 2–4.

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
- **Mesh LOD (Unity 6.2+)** is not a substitute for the semantic building LODs: it can't change materials or renderers between LODs, and Unity recommends against combining it with LODGroup. It could still help imported objects inside LOD0.
- **Budgets to validate on target hardware** (open question 9): draw calls, triangles and geometry memory, taking the street-level numbers above as the baseline.

## 7. Vertical circulation spec

- **Stairs core:** 2.6 × 5.2 m footprint. Switchback: near landing (1 m) → left flight up to a mid landing at half height → right flight up to the next near landing. The slab above has a hole over the flights, and the near landing stays part of the slab. Railings are generated automatically across the dead-end lanes on the bottom and top floors.
- **Lift core:** 2.4 × 2.4 m, door on the front face at every served floor. Entering the car opens a floor panel (G, 1…N, R). Rides move at 4.5 m/s.
- **Roof access:** a bulkhead (walls + roof + door) on the roof over a core that reaches the top floor and has `roofAccess`. Stairs default to roof access on; lifts default to off.
- **Floor edits:** insert/delete re-index `bottom` / `top`. `top = -1` follows the top floor. A core that only spanned a deleted floor is removed.

## 8. Implementation plan (Unity)

| Phase | Scope | Exit criteria |
|---|---|---|
| **1. Data + generation** | `BuildingData` model, footprint → slabs/walls/windows generator with the §4.5 rules, floor grouping, JSON import of prototype layouts, coplanar-face test | Prototype JSON loads in Unity and looks the same; coplanar test passes |
| **2. Occlusion** | `BuildingOcclusion` shader include, floor toggling, cutaway, buildings-in-the-way modes (sink, slice, cutout, fade) and ray test, section caps, shadow pass, silhouette. Tuning panel in play. | Walk every floor of a 10-floor building from any camera yaw without losing the character |
| **3. Play-mode test rig** | Third-person controller, follow camera (orbit / zoom / pitch clamp), virtual joystick, stairs ramps, lift interaction. Only for testing authored buildings; game code may replace it. | Outside → lobby → stairs → roof → lift down, on device |
| **4. Editor: shape & facade** | Scene-view handles (footprint corners, insert, delete, move, edge push), setbacks (tier list, containment rules, terrace generation with Clipper2), facade inspector with presets, entrance and blank-wall tools, undo via `Undo.RecordObject`, shell-only toggle | Designer makes a styled 3-floor building in < 2 min |
| **5. Editor: floors & interior** | Floor overlay (count, add/remove/duplicate/delete, copy layout up), wall/door/erase tools with snapping, core placement with range inspector | 10 floors + rooftop access in < 5 min, remove floor 4 in one click |
| **6. City scale** | LOD1 and LOD2 generators, facade shader, building table (GraphicsBuffer), LOD manager on LODGroups with feature-scale transition heights, HLOD cells, Burst generation jobs, LRU caches, city generator for tests | 3,000-building test city at the §6.6 numbers or better; coplanar and LOD-consistency tests pass |
| **7. Polish** | Per-floor overrides (height, facade per tier), baked lighting strategy, streaming districts (Addressables) | Perf budget met on target device |

## 9. Editor UI (Unity mapping)

The prototype's UI was cut down so only the current task is on screen:

| Prototype | Unity Editor |
|---|---|
| Building switcher (one line; opens to rename / switch / new / duplicate / delete) | Selection in the Hierarchy plus a `Create ▸ Building ▸ Rect/L/U/T/Octa` menu. The inspector header shows name and floor count. |
| Tabs: Shape · Facade · Interior | Custom inspector for `Building` with three tabs. The active tab also sets the active **EditorTool** (footprint handles, facade picking, interior tools), so there's never more than one set of handles in the Scene view. |
| Shape: outline list (base + setbacks) with add / remove / starts-at / inset, presets for the base, two height sliders | Inspector fields and a reorderable list of tiers. Corner, insert, edge-push and move handles are drawn with `Handles` at the selected tier's height, with the tier below drawn dotted (`Handles.DrawDottedLines`). A rejected move shows why next to the cursor (`Handles.Label`), for example "Stairs or a lift are in the way". Edge lengths show only while dragging. |
| Walls as a graph: in the Interior tab every wall is a line with a point at each end. Walls that meet share a joint that moves as one. A **+** at a wall's middle splits it; double-clicking a joint joins two straight walls or removes the walls ending there. | `Handles.FreeMoveHandle` per joint and `Handles.DrawAAPolyLine` per wall, on the active floor's plane. The data stays a list of wall segments; joints are derived by matching endpoints (1 cm tolerance). |
| One snapping rule for drawing and dragging: points first (ring marker), then axis alignment with the previous point and nearby points (dashed guides), then onto a wall or footprint edge (diamond marker). There is no grid. A point landing on the middle of a wall splits that wall into a T-junction, so the walls stay connected. Alt disables snapping. | Same rule in a shared `WallSnap` utility, previewed in the Scene view with `Handles.DrawWireDisc` and a dotted guide line. |
| Facade: style presets, window type, three colour rows, *Entrance / Blank wall* picking; everything else under **More options** | Presets are `FacadeStyle` ScriptableObject assets (so they're shared and versioned). A foldout holds the less-used fields. |
| Facade: *Style for* (base or a setback) when the building has setbacks; a setback can take its own style or match the floors below | A tier dropdown at the top of the Facade tab; the style fields bind to the selected tier's `FacadeStyle` (or show *Give it its own style*). |
| Facade: a **details palette** (AC unit, Vent, Dish, Fire escape, Awning): click a wall to place, click the item to remove; *Details by rule* densities per style | Palette buttons come from the `FacadeDetailDefinition` catalogue (icon + name), so a new prefab shows up on its own; placement uses the facade EditorTool's wall pick with the definition's footprint for the ghost and the clamps; densities live on the `FacadeStyle` asset. |
| Facade › *Roof*: Flat / Hip / Gable / Shed, pitch and eaves (only on the style that drives the top floor) | An enum popup plus two sliders on `FacadeStyle`; the roof preview updates live in the Scene view. |
| Storey height per floor (Interior tab, active floor), shown in the floor list | A float field with a reset button in the floor overlay row. |
| Stairs and lifts at any angle: placed aligned to the nearest wall, R = +90°, angle field and *Align to wall* in the inspector | `Handles.Disc` rotation handle snapping to the nearest outline edge angle (and 15° steps with Ctrl). |
| Moving a building snaps an edge onto a neighbour's facing edge (0.5 m); base corners snap to neighbours | Same snap in the move handle, using the city grid for neighbour lookup. A shared-wall highlight shows which building owns it. |
| Interior: walk-in / shell, one-row tool strip, selection inspector; interior colours under a foldout | A **Scene view overlay toolbar** (Overlays API) for tools. Selected cores use the standard inspector. |
| Floor card: count ± always; floor list only in Interior | A Scene view overlay panel. It stays collapsed to the count field except while the Interior tool is active. |
| **Isolate** (button next to the building name, or `I`): every other building dissolves out completely (no ghost, no shadows); the camera frames the selected building. Done in the shared shader from the building index, so nothing is rebuilt. | `SceneVisibilityManager.Isolate` gives the same result for scene objects, but merged HLOD cells contain many buildings, so drive the same shader global (`_IsolateBuilding`, `_IsolateAmount`) from the editor tool. |
| Overlay handles are projected with the *current* frame's camera: the prototype updates the camera matrices right after moving the camera. Before that fix they trailed by a frame, 200–280 px while orbiting. | Draw handles inside the Scene view's `OnSceneGUI` / `Handles` pass, which already uses the current camera. |
| Hints only when nothing is selected; tool tips inline under the tool strip | Scene view notification (`SceneView.ShowNotification`) for one-off tips. Shortcuts are registered with the `ShortcutManager`, so they're rebindable. |

## 10. Decisions (formerly open questions)

All questions from the prototype review have been answered, and every decision that changes the data model or generator is in the prototype.

| # | Question | Decision | What it means |
|---|---|---|---|
| 1 | Editor-only or in-game? | **Editor-only.** | Players don't build. The play-mode rig is a test harness. |
| 2 | Setback floors with a smaller footprint? | **Yes**, as outlines per floor range (§3, §4.1). | Done in the prototype. |
| 3 | Overhangs (an upper floor bigger than the one below)? | **Allow overhangs.** Done in the prototype (§4.1). | Containment became "a quarter rests on the floor below". The overhang gets a soffit and a fascia at every LOD, the floor is walkable, and doors still need deck in front of them. Columns under large cantilevers can come later. |
| 4 | Facade style per setback? | **Yes, per setback.** Done in the prototype. | `FloorData.style` on the floor that starts a tier. A tier without one looks like the tier below. LOD2 gets one colour slot per tier (§6.4). |
| 5 | Storey heights? | **Per floor.** Done in the prototype. | `FloorData.height` overrides the defaults, and `floorBase(k)` is a running sum. LOD2 quads are split into runs of equal storey height, each carrying its height and window spec (§4.1). |
| 6 | Core rotation? | **Free rotation.** Done in the prototype. | `rot` is in degrees. New cores line up with the nearest wall, and the inspector has an angle field and *Align to wall*. Fit uses the oriented rectangle (corners, edge midpoints, and no outline corner inside it); core-to-core spacing uses a separating-axis test. |
| 7 | Doors as objects? | **Open doorways only.** | The game may place door objects in the openings later. The generator exposes each opening's frame (position, width, height, facing). |
| 8 | Player occlusion settings? | **Fixed by design.** | Designers tune cutaway height, the buildings-in-the-way mode and hole size per project in a settings asset. No player-facing option. |
| 9 | Dim floors below the player? | **Leave as is.** | Floors below draw normally. |
| 10 | Art pipeline? | **Procedural first.** | Keep the generator and add materials and textures (trim sheets, tiling wall materials). Modular kits can come later from the same data, because only the generator changes. |
| 11 | Buildings sharing walls? | **Yes.** Done in the prototype (§4.3). | Party walls are derived from outlines that run along each other, never stored. The taller building owns the wall and builds it blank; the other leaves its wall out. Moving a building snaps it onto a neighbour's edge. The demo's Row House shares walls with two neighbours. |
| 12 | Platforms? | **PC / Mac, high-end mobile, low-end mobile.** | Low-end mobile sets the floor: OpenGL ES fallback, so the GPU Resident Drawer is optional (on for desktop and Vulkan/Metal only). 16-bit indices everywhere (cells split under 65,535 vertices). A texture fallback for the building table. Per-platform `LODCFG` (px/m thresholds, cell size, cache sizes). Budgets are still needed per tier; proposed starting points are ≤ 150 draws and ≤ 300k triangles on low-end mobile, ≤ 250 draws and ≤ 800k triangles on high-end mobile, and ≤ 1,000 draws and ≤ 3M triangles on desktop. |
| 13 | World size and streaming? | **Streaming districts.** | Districts are separate scenes or Addressables groups. HLOD cells are baked per district at edit time. Building data is loaded per district, and LOD0/1 are generated at runtime or baked per platform. A district border must not split a cell. |
| 14 | LOD1 for tall glass towers? | **Merge openings.** | At LOD1, flush glass openings (curtain wall, ribbon, shopfront) on one storey of one wall become a single recess. That cuts the 38-floor glass tower from about 26.8k to about 3k triangles at LOD1. |

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
