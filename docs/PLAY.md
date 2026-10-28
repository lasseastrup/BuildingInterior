# Workstream 5: occlusion and play

Buildings are walked in Play mode to check occlusion and circulation (SPEC §5, §7). The occlusion is the runtime feature games ship; the play kit is the reference character and camera that drives it (UNITY-PACKAGE-PLAN §2). Exit test: walk the demo street on an iPhone 7, with the first measured numbers for SPEC §6.6.

## 1. Where the logic lives

The prototype's play mode is plain functions over the layout and the LOD0 data: where the player is, what they stand on, which walls stop them, which walls drop for the cutaway, which buildings are in the way and how far each has sunk. These are ported to the engine-free runtime and judged against the prototype, as the generator and the edit operations are.

`prototype/tools/export-play.mjs` (`npm run play`) writes `unity/Fixtures/play.json`. It loads the demo street and the variant set headlessly, and records the following:

- **Collision segments:** every building's segments per storey.
- **Walk queries:** `surfaceAt`, `collide`, `playerLoc` and `segHitsBuilding` at seeded points over and around every building and storey.
- **Walks:** play mode stepped at a fixed 1/30 s with held keys. The walks go from outside every walk-in building's street door in through it and round under each buildings-in-the-way mode, and up every switchback stair, two storeys. Each frame records the player, the camera, the view uniforms, every occluder (slot, amounts, row) and every sliding wall.

The prototype exposes these through `__sb.play`, and its frame loop can be held so the harness steps play itself. The Unity side is then thin: a component reads the player and the camera, runs the engine-free step, and writes the results into the building table and the shader globals.

The walk model's collision is the prototype's model, not physics: walkable surfaces by height, and 2D wall segments with a radius per storey. A project with its own character can ask the same queries, or use the mesh colliders (§5).

**The walls the walk model knows.** A building's segments and its wall list (for the cutaway) are its LOD0 shape (`Lod0Shape`). The walk model asks for a building's shape the first time the player comes within 3 m of it, or enters the building next to it, and keeps it while the player is near. It keeps at most `PlayWorld.Keep` (32) shapes, letting go of the one used longest ago, so its memory doesn't grow however far the player walks. In Unity the Storey Occlusion hands it the renderer's shapes (`PlayWorld.Source`), made with the LOD0 the renderer builds on a worker as the camera nears. When the renderer has none (the building isn't at LOD0, or there's no renderer: the problem list, tests), the walk model builds the shape itself without any meshes (`Lod0.BuildShape`): about 0.3 ms for a typical building, 3 ms at worst, against 14–16 ms for a whole LOD0 (docs/CITY.md §7).

## 2. Order of work

| # | Slice | Contents | Done when |
|---|---|---|---|
| 5.1 | Walk model (**done**: `Play/PlayWorld`, `Walker`, `FollowCamera`; every `play.json` query and the 16 walks match) | `Runtime/Play`: the building and floor the player is in, the surface under a point (floors, terraces, roofs, both kinds of stairs, slab holes), collision against LOD0's segments, the player step (walk, run, gravity, step-up limit), the lift ride, the follow camera | `play.json`'s segments, surface, collide and loc cases match; the walks' player and camera match frame by frame |
| 5.2 | Occlusion core (**done**: `Occlusion/OcclusionCore`, `SinkFootprint`; the walks' view, occluders, rows and walls match frame by frame) | `Runtime/Occlusion`: the view (active building, ceiling clip, cutaway range, the camera offset applied to the player), the sliding cutaway per wall id, buildings in the way (the three rays, the door rule, the fade and hold, slots, Slice heights, Sink's plan and rows), camera assist, Sink's footprint mesh | The walks' view, occluders and walls match frame by frame |
| 5.3 | `OcclusionSystem` (**written; editor check outstanding**, §4: `StoreyOcclusion`, `StoreyOcclusionSettings`) | The Unity component: takes a camera and a focus, runs 5.2 each frame, writes `_StoreyState.w`, `_StoreyOcc`, `_StoreyWall` and the globals, shows the footprint meshes; `StoreyOcclusionSettings` (mode, stub, base height, hole size, silhouette, assist) | The demo street in Play mode occludes as in the prototype |
| 5.4 | Play kit (**written; editor check outstanding**, §4: `StoreyPlayKit`, `StoreyCharacter`, `StoreyPlayCamera`, `Storey/Silhouette`) | `com.triband.storey.playkit`: third-person controller on 5.1, follow camera with orbit, keyboard, gamepad and on-screen joystick, lift panel, silhouette | Walk every floor of a 10-floor building from any camera yaw without losing the character (SPEC §8) |
| 5.5 | Device run | iPhone 7 and iPhone 14 builds of the demo street; draws, triangles, memory and frame time recorded; the minimum tier's quality settings | The numbers replace the estimates in UNITY-PACKAGE-PLAN §6.6 |

5.1 and 5.2 are headless; 5.3 and 5.4 need an editor session to check, 5.5 a device.

## 3. Storey's own

Straight flights (`CoreType.Flight`) are not in the prototype. Their walk surface and slab holes follow their geometry (docs/EDITOR.md §5) and have their own tests.

## 4. Checking slices 5.3 and 5.4 in the editor

**What is written.**
- **The occlusion system:** `StoreyOcclusion`, on the Storey Site, with `StoreyOcclusionSettings` (*Assets ▸ Create ▸ Storey ▸ Occlusion Settings*). It runs the engine-free core each frame and writes the results into the building table and the shader globals:
  - the floor clip, and the cutaway's camera and range;
  - each wall's slide;
  - each building in the way's occluder slot and row.

  It shows Sink's and Dissolve's footprints, each through its occluder slot's own row in the table. The site's renderer hands it the view in Play mode and leaves its own view globals alone.
- **Dissolve:** a sixth mode for the buildings in the way. It ends where Sink ends: everything from the floor of the player's storey up is gone, the rest is dark, and the footprint (a dark fill ringed by a 30 cm rim of the outer wall) stands in. It gets there by fading instead of collapsing: over the fade (0.2 s) the building dithers away and darkens while its footprint dithers in on the complementary pattern, so the two never draw the same pixel and the rim doesn't z-fight the wall it replaces. The shadow and depth passes dither the same way, so the building's shadow goes with it, as Sink's does. The storey that stays is held while the building is partly gone, as Sink holds it while sunk.
- **Entering and leaving:**
  - **Wall Margin** (0.3 m): a wall starts to drop only once the player is that far past its line, and stays down until they cross back. A wall seen edge-on, pointing at the camera, no longer drops and rises again as the player passes it. The prototype's is 0.
  - **Hold After Exit** (on): a building the player steps out of stays cut away (the floors above clipped, the walls between camera and player down) while it still stands between the camera and the player, by the same three rays as the buildings in the way. A camera trailing behind is no longer buried in it. It's let go as soon as the camera is clear of it, or the player walks into another building.
  - Only the buildings next to the one the player is in are checked for shared walls, so walking into the first building no longer builds every building's LOD0 for the walk model.
- **The play kit:** add **Storey Play Kit** to any object (*Add Component ▸ Storey ▸ Play Kit*). It adds the occlusion system to the site if there is none, and makes the character and the camera. The character walks the walk model, with no physics.
- **The HUD:** where the character is, the lift panel and the touch joystick. It is drawn by its own component (`StoreyHud`), which the character adds and switches with **Show Hud** (on by default). Turn it off for measurement runs: then nothing is drawn or asked for it. The character works out the place and the lift once a frame, and a lift's stops once per lift, where the HUD asked on every GUI event (docs/PERFORMANCE.md §11.4).

**Controls.**
- **Keyboard:** WASD or the arrows to walk, Shift to run, Q and E to turn the camera.
- **Mouse:** right- or middle-drag to orbit, the wheel to zoom.
- **Gamepad:** the left stick walks, the right stick orbits.
- **Touch:** the joystick bottom left walks; a drag elsewhere orbits, a pinch zooms.

The play kit depends on the Input System package. With the project's *Active Input Handling* set to the old Input Manager only, it falls back to that.

**Changes made along the way.**
- **Party-wall neighbour index:** a building's row in the building table is now its index in the layout. A party wall's vertices carry the neighbour's layout index, and the shader looks that up in the table. The two only agreed by luck before.
- **Moved sites:** the cutaway is given the camera and the focus in the site's own x and z, the space the wall data in the vertices is in. A site moved away from the origin now cuts correctly, in the editor too.
- **Cutout and Fade:** they project the player in the shader, with the camera they draw with: eye depth, and pixels from the screen's centre. That holds on every platform, with or without reversed depth or a flipped render target. `StoreyGlobals.SetOcclusion` now takes the player's chest in world space and the hole radius in metres.

**Checks:**
1. **Setup:** add **Storey Play Kit** to the demo street's site and press Play. The character stands where the object is, and the camera trails it.
2. **Walking:** walk in through a street door.
   - The floors above are clipped.
   - The walls between the camera and the character slide down to a 1 m stub and come back up as you pass.
   - Turning the camera works from any direction.
3. **Stairs:**
   - Climb switchback stairs: the storey switches halfway up the second flight.
   - Climb straight flights, then walk back along the walkway.
4. **Lifts:** stand in a lift car. The panel lists its floors, and a floor button rides there.
5. **Buildings in the way:** walk behind a building with each mode set in the settings asset.
   - **Sink:** it collapses from the roof down to a black outline with a low rim.
   - **Slice:** it is cut down to your storey.
   - **Cutout:** a soft hole opens around you.
   - **Fade:** it ghosts out above a dark base.
   - Nothing changes in the shadows.
6. **Silhouette:** the character shows in orange through walls.
7. **Moved site:** move the site off the origin and repeat 2.

**Not yet:**
- The lift car is not drawn.
- With the site's LOD mode *Fixed*, occluders are not forced to LOD0 when Displayed LOD is higher. With *Automatic*, the default, they are (docs/CITY.md §2).
- The silhouette shader must be referenced by a material (the character's *Silhouette* slot) to be in a player build.

## 5. Mesh colliders

For a project's own physics character, raycasts or props, every building gets one `MeshCollider` on its root GameObject, on the site's layer. Every object the site makes (each building, its LODs and collider, the merged cells, Sink's footprints) is on the Storey Site's layer, so set the layer on the site for cameras' culling masks, lights and physics; changing it moves the objects already made. **Generate colliders** on the Storey Site switches it (on by default). Storey's play kit does not use them: it walks the walk model.

**A building without colliders** (Storey's own, for the old BuildingGenerator's buildings with `_generateColliders` off). **Colliders** on the building card (docs/EDITOR.md §6.10; `BuildingData.colliders`, layout key `colliders`, written only when off) takes that one building's out, as one edit like any other: the site makes it none, whichever way it would have come (with its LOD0, on a build thread or the main thread; a kept LOD0 shown again; **Colliders At Load**; **Colliders in edit mode**), so physics and raycasts pass through it. Nothing waits for it: `CollidersReadyAround` and the Storey Props take it as ready, there being none to come, and `SiteRenderer.HasCollider` says false. The walk model still walks it. With the switch on everywhere, every building gets its collider as before.

**The mesh.** `Play/CollisionMesh` builds it from the walk model's own data, so the collider and the walk model agree:
- **Walls:** a thin box round every collision segment (LOD0's segments, at their radius), storey by storey, merged into one box where a segment repeats on the storeys above.
- **Floors and terraces:** the outlines at each slab, grown by the walk model's margin, with the stair holes cut. Interior slabs that only separate two storeys of a shell building are left out.
- **Stairs:** a ramp per flight, at the walk model's heights, for both kinds of stairs.
- **Pitched roofs.**

Each triangle is tagged Floor, Stair, Wall or Roof (`CollisionMesh.Kind`). The demo street's buildings have 64 to 871 triangles each: 1 to 15% of their LOD0.

**Tested** headlessly against the walk model (`CollisionMeshTests`). These tests run on both corpora:
- A ray dropped at each of `play.json`'s surface points lands where `PlayWorld.SurfaceAt` does. There are more than 3,000 points.
- Every collision segment stops a horizontal ray.
- The floors and stairs face up.

**Performance.**
- **Run time:** a collider is built only when a building is. Its collision mesh is made on the build thread with the building's first LOD0. The buildings built since the last batch are cooked together, in parallel with `Physics.BakeMesh` in an `IJobParallelFor` on Unity's job workers, while the frames go on: the colliders are assigned in a later frame, once the batch has finished, so cooking never holds a frame. No mesh is destroyed while its batch cooks (a building released meanwhile waits for it), and in edit mode, which updates only when asked, the batch is finished at once. Each mesh is given to its collider with the same cooking options, so assigning it cooks nothing. The options are `UseFastMidphase` only: the mesh is already welded and has no degenerate triangles, so cleaning and welding are skipped. The mesh is positions and indices only, with 16-bit indices.
- **Edit time:** no colliders by default, so dragging a wall cooks nothing. **Colliders in edit mode** turns them on for editor raycasts. Each edit then rebuilds the colliders of only the buildings it touched.

**When a building's collider is there.** It comes with the building's LOD0 (in view and near enough, docs/CITY.md §2), assigned a frame or two after the LOD0 is up, and a site loading over frames builds no LOD0 before it has read its layout. Physics doesn't wait, so a rigid body placed on a building falls through until the collider is there.
- **Colliders At Load** on the Storey Site makes every building's collider as the site loads in Play mode, before physics first steps, and keeps them all (**Colliders Kept** doesn't apply): nothing placed on a building falls through it at the start, a prop or not. Off by default.
  - **How.** The layout loads at once, not over frames. When the load ends, the collision meshes of the buildings without a collider are made on the build threads at once, the main thread helping (`CollisionMesh.BuildAll`), from a shape made for the purpose where a building has no LOD0 (`Lod0.BuildShape`, no meshes). They are then cooked together on Unity's job workers and waited for (`SiteRenderer.AllColliders`, `MakeColliders`). A building rebuilt by an edit gets its collider again in the same frame. LOD0s built later make no collision mesh of their own.
  - **What it costs.** Headless, before PhysX's cooking, which needs the editor to measure:

    | Layout | Buildings | One thread | Four threads | Collision meshes |
    |---|---|---|---|---|
    | Demo street | 7 | 59 ms | 19 ms | 7 KB a building |
    | Test city | 300 | 382 ms | 155 ms | 0.5 MB |
    | Test city, walk-in | 300 | 426 ms | 187 ms | 3.7 MB |
    | Test city | 3,000 | 2.2 s | 0.7 s | 4.6 MB |
    | Test city, walk-in | 3,000 | 3.2 s | 1.0 s | 22.6 MB |

    PhysX keeps a cooked copy of each besides, and every building gets its own object for its collider. An iPhone 7 core is about a third of a desktop core, with two build threads. So it suits a level's layout; a streamed city leaves it off and lets its colliders come with LOD0, and a district streamed in with it on holds the frame it loads in. `CollisionMeshTests.ManyAtOnceAreTheSameAsOneByOne` checks the meshes made at once against those made one by one.
- **Storey Props** with a Rigidbody are held still until then (docs/PROPS.md §2.6).
- **Anything else** (a physics character, a rigid body that isn't a prop) waits on `StoreySite.CollidersReadyAround(position, radius)`: true once every building within the radius has its collider (or has its colliders off), and when the site makes none. A spawn or teleport asks for detail there first (`PrefetchAround`) and waits for this behind its fade, as it waits for `DetailReadyAround`. `SiteRenderer.HasCollider(id)` asks about one building.
- **A building showing its LOD0 has its collider.** One whose collider went past the cap while its LOD0 was kept has it made again from that LOD0 when it's shown. A LOD0 prefetched round the player and never shown is the first collider to go past the cap, and it was shown later without one.

**Limits.**
- Walls on the roof storey are 1.2 m high, a parapet's height, whatever the parapet.
- Lift shafts are solid floor: the walk model does not let the player fall down them either.
- Gable ends above the top storey are not walls in the collision mesh. The pitched roof itself is.
- Without Colliders At Load, a building out of view or far away has no collider until its LOD0 is built. Storey Props there stay held; any other rigid body falls through it.

## 6. Hiding a building at run time

A game with its own occlusion (a converted scene's dollhouse, `OccludedBuilding`) switches off the building the player is behind: its GameObject and its rooftop props go, while a collider of its own (the raycast target) and shadow-only renderers stay, so its shadow does. Under Storey a building is part of its site, with no GameObject of its own, so the site hides it instead (Storey's own).

**`StoreySite.SetHidden(buildingId, hidden)`** hides one building of the layout, or shows it again; `IsHidden(buildingId)` says whether it is.
- **Hidden**, it draws nothing in the colour pass or the depth prepass, at any LOD: LOD0 with its glass and rooms, LOD1, and its massing inside its merged cell, whose other buildings draw as before. Its shadow is cast as before (LOD0's own, and at LOD1 and LOD2 its massing's, docs/CITY.md §3). Its collider (§5) and the walk model are untouched.
- **Shown again**, it draws exactly what it would have. Its LOD carries on while it's hidden: the LOD manager picks, builds, cross-fades and drops its meshes as for any building, so its shadow follows the camera and it comes back at the LOD the buildings round it show.
- **Kept by id** on the site: a building not built yet (a layout loading over frames, a district streamed in) is hidden as it's built, and an edit, the site switched off and on or given other materials keep it hidden.
- **Nothing per frame, no garbage.** Hiding or showing writes one number into the building table, and the shaders do the rest.
- **Its party walls** at LOD0 stay while the neighbour they're built for shows (they're its walls too, as Sink keeps them), so the neighbour stays closed; they go when both are hidden.
- **Its Storey Props** go with it from the colour pass and keep casting their shadows; one drawn whole (*Hide Whole*) is switched off, shadow and all (docs/PROPS.md).
- **Storey's own occlusion** (§4, every mode) never takes a hidden building for one in the way, and one hidden while in the way comes back up with no footprint.

**How it's done.** A building's row in the building table says which LOD it shows, which fades out and how far (docs/CITY.md §2), and every Storey shader draws a mesh's vertices, in a building's own renderers or in a cell, only where its building's row says that LOD shows. Hiding adds 4 to the LOD it shows (`Lod.StateRow.Hidden`, `BuildingTable.SetHidden`): the colour and depth passes then draw none of its vertices (`StoreyVertex`, StoreyOcclusion.hlsl), and the shadow caster takes the LOD less 4. The rest of the row (the LOD fading out, the fade, the occluder slot) is left alone and every write of its LOD keeps the 4, so the cross-fades, the steady variant and the occlusion carry on under it, and showing it takes the 4 off again. Switching the building's renderers to shadows only would leave its massing drawn in its cell, one mesh for many buildings, which only the shader can tell apart; a table of its own would be one more read for every vertex, hidden or not. The row is read already: a building that isn't hidden costs a compare a vertex, and only a hidden one's vertices read its party walls' neighbours.

**The handle.** **Storey Building Handle** (`StoreyBuildingHandle`, *Add Component ▸ Storey ▸ Storey Building Handle*) stands for one building: its **Site** and **Building Id**. Switching its GameObject off (the object, a parent, or the component) hides the building, and switching it on shows it, so a game system that switches a building's object off works unchanged: put the handle on the object it switches (the converted building's own). Whatever goes first is safe: a site not loaded yet hides the building as it's built, and a site destroyed first is left alone. Play mode only; in the editor, selected, it draws its building's box and a line to it, and moves nothing (a converted building's object may carry the game's own children).

**Limits.**
- A handle whose object is saved switched off hides nothing until it's switched on and off: Unity calls neither `OnEnable` nor `OnDisable` for it. One handle a building: the last one switched wins.
- At LOD1 a hidden building's party walls go with it (LOD1's vertices don't name their neighbour), so a neighbour at LOD1 shows a gap there until the hidden one is at LOD0 again or shown. A building in the player's way is near the camera, at LOD0. Party walls across a district edge go with it too, as their neighbour isn't named (docs/CITY.md §5).
- A hidden building's LOD0 still goes through the colour pass's vertex shader, its vertices dropped there, as a LOD cross-fading out does. If a profile shows it, its LOD0's renderers could be switched to shadows only besides (not done).

**Checks** (in the editor):
1. In Play mode, hide a building near the camera (`site.SetHidden(id, true)`, or a handle switched off): it's gone, its shadow stays on the street and its neighbours, a raycast still hits its collider, and a neighbour it shares a wall with stays closed.
2. Walk away while it's hidden, so it goes to LOD1 and to its cell, and back: nothing of it shows, its shadow follows its LOD, and the other buildings of its cell are drawn. Show it again: it matches the buildings round it.
3. Hide a building before the site loads (a handle switched off as the scene starts, then on): it's never drawn until shown.
4. With Storey Occlusion in each mode, walk behind a hidden building: nothing sinks, slices or fades for it.
