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

The inspector says which building and storey it's in.

**Storey Props** is the manager. It's hidden, made on demand, and runs after the sites each frame.
- **No per-prop cost:** one list, one loop, no garbage, and no Update per prop.
- **Writing the value:** only when a prop's building changes.
- **Rebuilds:** when any site is built again (an edit, a district streamed in), props re-resolve their building's index by id, and props that had no building look again.
- **Distance culling:** each building's props are a group, switched off with `forceRenderingOff` once the building has settled at LOD1 or LOD2 (Play mode only). That's one check per group per frame, not per prop. Their own `enabled` is never touched.

**Tools ▸ Storey ▸ Props ▸ Show Prop Buildings** tints every prop by its building, and magenta in the street. A prop that doesn't change colour has a shader without the nodes. This is also the quickest check that the user value reaches the shader with your settings.

### 2.5 Hide Whole: for shaders that can't take the nodes

A Storey Prop with **Hide Whole** is switched off while its point is in a storey the occlusion hides (`OcclusionCore.HidesPoint`):
- above the ceiling clip of the player's building;
- a Sink storey gone or squashed past half;
- above Slice's plane;
- above Cutout's, Fade's or Dissolve's base once half faded.

It's all or nothing: no dither, no squash.

## 3. What it costs

- **CPU:**
  - Still props cost nothing per frame.
  - An awake rigidbody costs a vector compare per frame, plus a grid lookup and an outline test (no garbage, `PropTests`) each time it has gone 5 cm.
  - Each building with props costs one LOD check per frame.
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
8. **The profiler:** no GC Alloc from Storey Props while walking.
