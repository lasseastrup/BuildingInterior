# Props: occluding the objects inside buildings

**Status: written; checking in Unity is outstanding** (§5).

The game puts objects on each floor of a house: furniture, clutter, rigidbodies that can be knocked down the stairs or out of a window. They disappear with the building around them:

- **The player's own building:** the floors above the player are clipped.
- **Buildings in the way:** Sink, Slice, Cutout, Fade or Dissolve.
- **The building's LOD0:** they fade with it, and are switched off once it settles at LOD1.

Props in the street (outside every building) are never occluded, as before.

## 1. The idea: a prop is drawn as part of the building it's in

The building shaders decide everything per vertex and per pixel from one building index. The index tells them:
- the building's state row (shown LOD, cross-fade, occluder slot);
- its occluder row (Sink's floor lines, Slice's height, Cutout/Fade/Dissolve's fade);
- the view globals (the active building and its ceiling clip).

A prop that knows its building's index goes through the same `StoreySink` and `StoreyOcclude`, with no occlusion logic of its own:

| What happens to the building | What its props do |
|---|---|
| Floors above the player clipped | A prop on those floors is clipped (per pixel, by height) |
| Sink | Props squash with their storey, and collapse once it has gone |
| Slice | Clipped at the slice height |
| Cutout, Fade | The same hole or ghost around the player, the same dark base |
| Dissolve | The same dither, the same darkening |
| LOD cross-fade | A prop counts as LOD0: it fades in and out with the building's LOD0 |
| Isolate (editor) | Ghosts with its building |
| Hidden at run time (`StoreySite.SetHidden`, docs/PLAY.md §6) | Gone from the colour and depth passes with it, its shadow kept |

Two consequences:
- **Moving props need little handling.** A dropped object is re-filed under whichever building it's now in. Within its building it's tested by height every frame, so a fall to another floor needs nothing at all.
- **The behaviour can't drift from the buildings'**, because it's the same code.

## 2. The parts

### 2.1 Telling the shader which building: Renderer Shader User Value

Unity 6.3's `MeshRenderer` / `SkinnedMeshRenderer.SetShaderUserValue(uint)` gives each renderer a 32-bit value the shader reads as `unity_RendererUserValue`. All props share their materials.
- **What it costs:** no material instances and no MaterialPropertyBlocks. A property block would take each prop out of the SRP Batcher and the GPU Resident Drawer (SPEC §6.4).
- **What's stored:** the building's table index + 1; 0 means "in the street" (`PropLocator.Encode`).
- **What it isn't:** the value isn't saved with the scene. Storey Prop writes it at run time, and in the editor.

**Still to verify in Unity:**
- that the value arrives in every pass, colour, shadow and depth, with the GPU Resident Drawer on and off (UNITY-PACKAGE-PLAN §6.1 marks GRD as undocumented);
- the declaration's type. `StoreyProp.hlsl` reads it as `(uint)unity_RendererUserValue`; if your URP version declares it differently, define `STOREY_PROP_USER_VALUE` before the include.

**Fallback if it doesn't hold:** a per-site grid of building indices, read at the prop's pivot. Not built.

### 2.2 Your Shader Graphs: two Custom Function nodes

`Packages/com.triband.storey/Unity/Shaders/StoreyProp.hlsl` holds two functions for Custom Function nodes in **File** mode.

1. **Vertex.** Add a Custom Function node in the vertex stage.
   - **Name:** `StoreyPropVertex`
   - **Input:** `PositionOS` (Vector 3), from a Position node set to **Object** space.
   - **Output:** `Out` (Vector 3), into the master stack's **Vertex ▸ Position**.
   - If the graph already offsets vertices, chain it: your offset first, then this node.
2. **Fragment.** Add a Custom Function node in the fragment stage.
   - **Name:** `StoreyPropFragment`
   - **Inputs:** `PositionWS` (Vector 3), from a Position node set to **World** space; `BaseColor` (Vector 3), your colour.
   - **Outputs:** `Color` (Vector 3), into **Fragment ▸ Base Color**; `Alpha` (Float), into **Fragment ▸ Alpha**.
   - In the Graph Settings turn **Alpha Clipping** on, threshold 0.5. Alpha is always 1, but wiring it makes Shader Graph run the function in the depth and shadow passes too.
3. **Optional:** wrap the two nodes in a sub-graph, so every prop graph takes one node. Shader Graph assets can't be written outside the editor, so this is a two-minute job in Unity.
4. **Optional: the steady variant** (docs/PERFORMANCE.md §11.1). Every prop graph clips, so on Apple GPUs every prop draw is a discarding one, which the GPU can't hide behind other geometry before shading. A graph can have the buildings' steady variant:
   - **The keyword.** Add a Boolean Keyword with the reference `STOREY_STEADY`, set to Multi Compile and Local, default off. With it on, `StoreyOcclude` returns before any clip.
   - **Alpha Clipping.** In the Graph Settings, turn on **Allow Material Override**, so a material can turn Alpha Clipping off. Alternatively, turn Alpha Clipping off in the graph altogether; the cost is that the LOD cross-fade and Dissolve then no longer reach the prop's shadow and depth.
   - **What Storey Props does with it.** In Play mode it gives each renderer a copy of its material, with the keyword on and Alpha Clipping off, in the opaque queue. A prop uses the copy while nothing can hide its building's pixels (the building's own steady rule), and always in the street. The copies follow their materials' properties and keywords twice a second.
   - **Graphs it skips.** A graph without the keyword, a transparent one, or one whose Alpha Clipping is baked in (on, without the override) keeps its materials.
   - **Shipping the variant.** Unity builds an Alpha Clipping-off variant only when a material in the build uses it. Keep one material per graph with Alpha Clipping off in the build (in a Resources folder, say), or turn on **Strict shader variant matching** in development builds to see a missing one. Otherwise the copies draw the clipping variant: correct, but nothing gained.

**How clipping behaves in each pass:**
- **Colour pass:** all the clipping happens here, as for the buildings, so the light indoors doesn't change with the cutaway.
- **Depth and shadow passes:** only the LOD cross-fade and Dissolve reach them.
- **Darkening:** a building in the way darkens the prop's base colour, before lighting (the buildings darken after). Emission isn't darkened.

### 2.3 Which building a prop is in: the locator

`PropLocator.BuildingAt(site, x, y, z)` returns the site's own building whose outline holds the point, from a metre below its ground floor to 4 m above its roof base.
- **Garbage-free:** it uses the site's grid cell (`Site.InCell`) and the outline test (`PlayWorld.Inside`).
- **Neighbouring districts:** their buildings, there only for shared walls, are never a prop's home.
- **Several sites:** they're searched in turn.

### 2.4 Components

**Storey Prop** (*Add Component ▸ Storey ▸ Storey Prop*) goes on any object or prefab root. It covers every MeshRenderer and SkinnedMeshRenderer under it, except those under another Storey Prop.

| Setting | What it does |
|---|---|
| **Motion: Auto** | Moving if there's a Rigidbody on it or above it, still otherwise. |
| **Motion: Still** | Found its building when enabled, and when moved in the editor. A script that teleports it calls `Refresh()`. |
| **Motion: Moving** | Found again whenever it has moved 5 cm; a Rigidbody's only while awake. |
| **Locate By** | Its renderers' bounds centre (the default), or its pivot. |
| **Hide Whole** | §2.5. |
| **Wait For Collider** | In Play mode, its Rigidbody is held still until there is a floor under it (§2.6). On by default. |

The inspector says which building and storey it's in.

**Storey Props** is the manager. It's hidden, made on demand, and runs after the sites each frame (execution order 1001, so it reads this frame's rows).
- **No per-prop cost:** no garbage and no Update per prop. In Play mode the frame visits the moving props alone; in the editor, every prop, for those moved by hand. A prop leaves the lists by swapping with the last.
- **Writing the value:** only when a prop's building changes.
- **Rebuilds:** when a site is built again (an edit, a district streamed in), that site's props re-resolve their building's index by id. Props that had no building look for one in that site alone; the other sites' props are left as they are.
- **Distance culling:** each building's props are a group, switched off with `forceRenderingOff` once the building has settled at LOD1 or LOD2 (Play mode only). That's one check per group per frame, not per prop. Their own `enabled` is never touched. A group goes when its last prop does.
- **Steady copies:** a group's props move to their materials' steady copies, and back, as the building's steady answer changes (§2.2, step 4).
- **Rigidbodies:** held still while their building has no collider, and while their site is loading (§2.6).

**Tools ▸ Storey ▸ Props ▸ Show Prop Buildings** tints every prop by its building, and magenta in the street. A prop that doesn't change colour has a shader without the nodes. This is also the quickest check that the user value reaches the shader with your settings.

### 2.5 Hide Whole: for shaders that can't take the nodes

A Storey Prop with **Hide Whole** is switched off while its point is in a storey the occlusion hides (`OcclusionCore.HidesPoint`):
- above the ceiling clip of the player's building;
- a Sink storey gone or squashed past half;
- above Slice's plane;
- above Cutout's, Fade's or Dissolve's base once half faded;
- anywhere in a building hidden at run time (docs/PLAY.md §6), shadow and all.

It's all or nothing: no dither, no squash.

### 2.6 Rigidbodies: held until there is a floor under them

A building's collider comes with its LOD0 (docs/PLAY.md §5): built on a build thread as the camera nears, then cooked off the main thread and assigned a frame or two later. A site loading over frames hasn't even read its layout in its first frames. Physics starts at once, so a rigidbody placed on a floor or a roof fell through before the floor was there.

So in Play mode a Storey Prop's Rigidbody is **held**: kinematic while there may be nothing under it, let go as soon as there is.

- **When it's held:**
  - its building has no collider: not up yet (the site loading, the building's LOD0 not built, the collider cooking), or let go past **Colliders Kept**;
  - it registered (or was refreshed) in the street, at rest, while a site making colliders was still loading: it may yet be in one of that site's buildings. Once no site is loading it's let go, or held by its building's rule. A prop already in the street when a district starts loading isn't held.
- **Decided before the physics step.** A prop that registers (a scene starting, a district streamed in, an instantiated prefab) is looked at in the props manager's `FixedUpdate`, which runs before Unity's physics steps, or its `LateUpdate`, whichever comes first. Not as it registers: a game that instantiates a prop and sets its velocity right after keeps that velocity.
- **What a hold keeps.** The body's velocities, given back when it's let go, so a body held in flight goes on. Continuous collision detection (Continuous, Continuous Dynamic) is Continuous Speculative while held, the most a kinematic body takes, and given back after.
- **Out of view or far.** A building gets its LOD0, and with it its collider, only in view and near enough (docs/CITY.md §2), so its props stay held until then. They're hidden meanwhile anyway: a building's props are drawn only with its LOD0. A body that moves into a building with no collider (thrown onto a roof out of view, say) is held where it is, and that building's LOD0 is asked for at once (`StoreySite.PrefetchAround`), so it goes on a moment later.
- **Never held:** a body that's kinematic already (the game's), a prop with **Wait For Collider** off, the props of a site with **Generate colliders** off or of a building with its **Colliders** off (docs/PLAY.md §5: none is coming), and anything outside Play mode.
- **Several props on one body** (a Rigidbody on their parent) share its hold: it's held while any of them is.
- **A held prop isn't looked for again,** since nothing moves it. A script that teleports one calls `Refresh()`, as for a still prop.
- **A script that makes a held body kinematic itself** does so again once it's let go: letting go makes it dynamic.
- **The inspector** says when a prop is held (`StoreyProp.Held`).

A rigidbody or a physics character that isn't a Storey Prop waits on `StoreySite.CollidersReadyAround` instead (docs/PLAY.md §5).

On a site with **Colliders At Load**, every building has its collider before physics first steps and keeps it, and a rebuilt building gets its own back in the same frame (docs/PLAY.md §5). No prop is held there; the hold is for sites whose colliders come with LOD0.

## 3. What it costs

- **CPU:**
  - Still props cost nothing per frame.
  - An awake rigidbody costs a vector compare per frame, plus a grid lookup and an outline test (no garbage, `PropTests`) each time it has gone 5 cm.
  - Each building with props costs one LOD check, one steady check and one collider check per frame.
  - A held rigidbody costs nothing per frame. Holding one and letting it go happen on changes only: the prop registering, its building's collider coming or going, the site's load ending.
  - A steady copy swap re-registers the renderer with the GPU Resident Drawer, as a building's does. Storey Diagnostics counts them as "prop swaps".
- **GPU:**
  - Per vertex, the same reads as a building vertex.
  - Per pixel, the same tests as a building's.
  - No extra draws, the SRP Batcher and GPU Resident Drawer stay on, and props of buildings at LOD1 aren't drawn.

## 4. Not built (yet)

- **The Storey/Prop Lit shader.** Your props use your own Shader Graphs, so it waits until it's wanted.
- **Track Layers** (props by layer without a component), and `StoreyProps.Register` for objects without one.
- **The fallback grid** (§2.1), unless the user value fails its check.
- **Props in the play kit's demo street.**

## 5. Checks in Unity

1. **The user value:**
   - Add the two nodes to a prop graph, put Storey Prop on some furniture inside a house, and turn on **Show Prop Buildings**: each house's props take its colour, and props in the street turn magenta.
   - Repeat with the GPU Resident Drawer on.
   - Repeat in a build.
2. **Floors:** walk into a house with props on every floor. The floors above go, and their props go with them. Climb the stairs: the next floor's props appear as its ceiling clip lifts.
3. **Buildings in the way:** walk behind the house in each mode (Sink, Slice, Cutout, Fade, Dissolve). Its props squash, clip, open, ghost or dissolve with it.
4. **Moving props:** throw a crate down the stairs; it shows or hides with whichever floor it's on. Throw it out of a window: it stays visible in the street.
5. **Distance:** walk away. The house's props fade with its LOD0, and the Frame Debugger shows no prop draws once the house is at LOD1.
6. **Edits:** edit the house in the editor. Its props keep following it (its index may move).
7. **Hide Whole:** set it on a prop with a third-party shader. It pops in and out with its storey.
8. **The profiler:** no GC Alloc from Storey Props while walking (the steady copies' sync allocates their keyword lists twice a second, when there are copies).
9. **The steady variant:** with the keyword and the override in a prop graph, the Frame Debugger shows the house's props in the opaque queue (2000) without `_ALPHATEST_ON` until the house is in the way, cut away or fading, and in the AlphaTest queue then.
10. **Rigidbodies at the start (§2.6):** put crates (Storey Prop and Rigidbody) on a roof and on an upper floor, press Play. They stay where they are, and the inspector says they're held until the building's collider is up; then they settle where they were. Repeat:
    - with Load Over Frames on and off;
    - with the crates on a building behind the camera: they're let go once the camera turns to it;
    - in a district streamed in;
    - with a crate set to Continuous Dynamic: no warning in the Console.
