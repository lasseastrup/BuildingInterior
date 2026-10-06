# Props: occluding the objects inside buildings

**Status: plan, not started.** The game puts objects on each floor of a house: furniture, clutter, rigidbodies that can be knocked down the stairs or out of a window. They have to disappear with the building around them:

- **The player's own building:** the floors above the player are clipped.
- **Buildings in the way:** Sink, Slice, Cutout, Fade or Dissolve.
- **Through every LOD change.**

This plan says how, and in what order.

## 1. The idea: a prop is drawn as part of the building it's in

The building shaders already decide everything per vertex and per pixel from one building index. The index tells them:
- the building's state row (shown LOD, cross-fade, occluder slot);
- its occluder row (Sink's floor lines, Slice's height, Cutout/Fade/Dissolve's fade);
- the view globals (the active building and its ceiling clip).

A prop that knows its building's index can go through the same two functions, `StoreySink` (vertex) and `StoreyOcclude` (fragment), with no new occlusion logic:

| What happens to the building | What its props do, through the same code |
|---|---|
| Floors above the player clipped | A prop on those floors is clipped (per pixel, by height) |
| Sink | Props squash with their storey, and vanish when it has |
| Slice | Clipped at the slice height |
| Cutout, Fade | The same hole or ghost around the player, the same dark base |
| Dissolve | The same dither, the same darkening |
| LOD cross-fade | A prop counts as LOD0: it fades in and out with the building's LOD0 |
| Isolate (editor) | Ghosts with its building |

A prop outside every building has no index and is never occluded. That's the same rule as now: only buildings are occluded.

Two consequences:
- **Moving props need no special handling.** A dropped object is re-filed under whichever building it's now in. Within its building it's tested by height every frame, so a fall to another floor needs nothing at all.
- **The behaviour can't drift from the buildings' own,** because it's the same code.

## 2. The parts

### 2.1 Telling the shader which building: Renderer Shader User Value

Unity 6.3's `MeshRenderer` / `SkinnedMeshRenderer.SetShaderUserValue(uint)` gives each renderer a 32-bit value the shader reads as `unity_RendererUserValue`. All props share one material.
- **What it costs:** no material instances or MaterialPropertyBlocks. A property block would take each prop out of the SRP Batcher and the GPU Resident Drawer (SPEC §6.4).
- **What's stored:** the building's table index + 1, 13 bits. 0 means "outside".
- **What it isn't:** it isn't saved with the scene. That's fine, because it's worked out at run time anyway (§2.3).

**To verify first (phase 0):**
- that the value is read correctly in every pass: colour, shadow and depth;
- that it reads correctly with the GPU Resident Drawer on (UNITY-PACKAGE-PLAN §6.1 marks this as undocumented);
- that it reaches a Shader Graph Custom Function.

**Fallback if it doesn't hold:** a per-site grid of building indices, a 0.5 m raster of the outlines, read in the shader at the object's pivot (the model matrix's translation).
- **Pros:** no per-renderer data at all, and moving props cost no CPU.
- **Cons:** memory (about 0.7 MB for a 300 × 300 m district), and props within half a cell of a shared wall can be filed under the neighbour.
- Only built if phase 0 fails.

### 2.2 Shaders: three ways to opt in, for designers

1. **Storey/Prop Lit**: a ready-made URP shader (base map, normal map, metallic, smoothness, emission) with the occlusion built in. Swap it onto a prop's material and it works.
2. **Storey Prop Occlusion** Shader Graph sub-graph, for the project's own graphs. It takes three connections:
   - **Vertex position:** wire it through the sub-graph.
   - **Alpha Clip Threshold:** wire it to the master stack.
   - **Base Color:** wire it through the darkening.

   Underneath it's a Custom Function in file mode on `StoreyProp.hlsl`, the way the building table is read already (SPEC §6.4).
3. **Any other shader:** the CPU fallback (§2.5).

The include is the buildings' own:
- `StoreyProp.hlsl` wraps `StoreySink`, `StoreyVertex` (as LOD0) and `StoreyOcclude` (kind 0: no cutaway, which is for walls).
- **Same pass rules as the buildings:** clipping is colour-pass only, so light indoors doesn't change with the cutaway. Sink's squash and Dissolve's dither reach the shadow pass.

### 2.3 Which building a prop is in: the locator

- **How it decides:** an engine-free `PropLocator` uses each site's grid and outline test (what `PlayWorld.Locate` does) to give a point's building and storey.
- **Which point:** the prop's bounds centre, or its pivot (a setting).
- **Several districts:** they're searched by area, as the occlusion's Follow Player does.

The result is held by building **id** and turned into a table index when it's written. A site rebuilt by an edit, or a district streamed in, moves its buildings' indices, and its props are written again.

### 2.4 Components

- **Storey Prop** goes on any object or prefab root and covers every renderer under it. A chair, a whole room's furniture or a spawned crate all get the same component.
  - **Static:** located once when enabled (and when moved in the editor).
  - **Dynamic** (automatic when there's a Rigidbody): located again when it has moved more than 5 cm, and only while the rigidbody is awake.
  - **Its inspector shows:**
    - which building and storey it's in (live in Play mode);
    - a warning for any renderer whose material won't follow the occlusion, with a button to switch the material to Storey/Prop Lit or to use the CPU fallback.
- **Storey Props** is the one manager, created on demand.
  - It keeps every prop in flat arrays and updates them in one loop: no `Update` per prop, and no garbage per frame (`FrameAllocTests` gets a case).
  - It also has **Track layers**: every renderer on chosen layers under the sites' areas is a prop without a component. That covers set dressing placed in bulk, and spawned objects on those layers are picked up by a rescan or by `StoreyProps.Register(go)`.
- **Distance culling:** a building's props are switched off (`renderer.enabled`) once the building has settled at LOD1 or LOD2. Seen from a street away, a house's hundred props then cost no draw calls. It runs on the LOD manager's change list, not every frame. Off in the editor.

### 2.5 The CPU fallback, for shaders that can't be changed

A per-renderer setting on Storey Prop, **Hide whole**, switches the renderer off (or to shadows only) instead of clipping it in the shader. It hides when either:
- the prop's storey is above the player's in the player's building;
- its building is in the way and its storey has gone (sunk, sliced, dissolved).

The decision is an engine-free function of the occlusion core's state, so it's testable. It's all or nothing (no dither, no squash), which is why it's the fallback and not the default.

## 3. What it costs

- **CPU:**
  - Static props cost nothing per frame.
  - A moving prop costs one grid lookup and an outline test when it has moved, then one `SetShaderUserValue` if its building changed. Target: 1,000 moving props under 0.2 ms.
  - LOD culling only runs when a building's LOD changes.
- **GPU:**
  - Per vertex, the same reads as a building vertex: its state row, plus its occluder row when it's in the way.
  - Per pixel, the same tests as a building's.
  - No extra draws, and the SRP Batcher and GPU Resident Drawer stay on.
- **Memory:** a few bytes per prop for the arrays.

## 4. Order of work

| Phase | What | Checked by |
|---|---|---|
| 0 | Verify Renderer Shader User Value: a scene of cubes with a test shader reading it, GRD on and off, in Game view and shadows. Choose user value or the fallback grid | You, in Unity (I write the scene and the shader) |
| 1 | `PropLocator`, the prop registry (by building id, written as table indices), the user-value encoding, the CPU fallback's rule | Headless tests: the demo street and the test city; props moved between storeys and out of a window; indices kept across a rebuild |
| 2 | `StoreyProp.hlsl`, Storey/Prop Lit, the Shader Graph sub-graph | Compiles against the stubs; in Unity: a prop on each floor goes with its building in every mode |
| 3 | Storey Prop and Storey Props components: static and dynamic, Track layers, distance culling, inspector warnings | Headless (no garbage, culling decisions); in Unity: knock a crate down the stairs and out of the door |
| 4 | The play kit's demo street gets some props; docs, changelog | The checks in §5 |

## 5. Checks in Unity

1. Walk into a house with props on every floor: the floors above go, and their props go with them. Climb the stairs: the next floor's props appear as its ceiling clip lifts.
2. Walk behind the house in each mode (Sink, Slice, Cutout, Fade, Dissolve): its props squash, clip, open, ghost or dissolve with it. Their shadows behave like the building's.
3. Throw a crate down the stairs: it shows or hides with whichever floor it's on. Throw it out of the window: it stays visible in the street.
4. Walk away: the house's props fade with its LOD0, and the Frame Debugger shows no prop draws once the house is at LOD1.
5. Edit the house in the editor: its props keep following it.
6. A prop with a third-party shader: the inspector warns. With **Hide whole** it pops in and out with its storey.
7. The profiler: no GC Alloc from Storey Props while walking; under 0.2 ms with 1,000 rigidbodies moving.

## 6. Decisions for you

1. **What shaders do your props use?** URP Lit, your own Shader Graphs, or third-party? This sets which of §2.2's paths comes first.
2. **Should props be switched off once their building is at LOD1?** I recommend yes (§2.4). A prop on a balcony would then go at distance, along with the building's detail.
3. **Props in the street** (outside every building) stay unoccluded, as they are now. Is that right for the game?
