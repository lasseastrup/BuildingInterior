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

**The walls the walk model knows.** A building's segments and its wall list (for the cutaway) are its LOD0 shape (`Lod0Shape`). The walk model asks for a building's shape the first time the player comes within 3 m of it, or enters the building next to it, and keeps it while the player is near. It keeps at most `PlayWorld.Keep` (32) shapes, letting go of the one used longest ago, so its memory doesn't grow however far the player walks. In Unity the Storey Occlusion hands it the renderer's shapes (`PlayWorld.Source`), made with the LOD0 the renderer builds on a worker as the camera nears. When the renderer has none (the building isn't at LOD0, or there's no renderer: tests), the walk model builds the shape itself without any meshes (`Lod0.BuildShape`): about 0.3 ms for a typical building, 3 ms at worst, against 14–16 ms for a whole LOD0 (docs/CITY.md §7).

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

Straight flights (`CoreType.Flight`) are not in the prototype. Their walk surface and slab holes follow their geometry (docs/EDITOR.md §5) and have their own tests. So do their rails and solid flights: a railed side, and a solid flight's sides and back on its floor, are walls to the walk model (`Seg.Flush`: the collider has them as they are), so nobody steps off a railed flight or walks in under a solid one (`StairRailTests`). An opening left without its rails (*Opening rails* off, `CoreData.openingRails`) keeps its edges as walls all the same, to the walk model and the collider, as high as its rails would be, so nobody walks into it.

**Out on a terrace the building is whole, and what stands over the terrace is a building in the way.** A player on a setback's terrace is in the building's plan, so `PlayWorld.Locate` puts them in it, on the storey the setback starts at, as the prototype does (the HUD's floor, the lifts and the walk model are unchanged). But they are outdoors, past the setback's walls (`OcclusionCore.OnTerrace`), and the prototype's indoor view clipped the setback above that storey's ceiling, roof and all. Now the building is shown whole, every storey and its roof, with no walls cut away.
- **Behind the setback.** When what stands above the terrace is between the camera and the player, it is a building in the way like any other, under the same mode: a tower in the middle of its terrace sinks to its footprint on the deck, is sliced, opens a hole, fades or dissolves. Nothing is clipped or cut away. The view was as indoors there, its storey's ceiling clipped and the walls between camera and player cut away, which opened a shell-only tower onto its empty inside. In the way is tested with the same three rays as the buildings in the way, from the terrace's storey up (`SegmentHits`' *from*): the storeys under the deck can't hide the player.
- **Only the setback.** The building's occluder row starts at the terrace's storey (`Occluder.k`), and the shaders draw it as in the way only over the terrace's floor (`OcclusionView.terraceY`, `_StoreyActive.zw`, `StoreyOverTerrace` in StoreyOcclusion.hlsl). The storeys under the terrace, which the player stands on, aren't darkened, sunk or faded, and the deck stays. So do the terrace's own walls: its parapet, and the rails round a stair coming up onto it. They are the walls made on the terrace's storey whose middle is off the setback's outline (`OcclusionCore.TerraceWalls`); the occlusion marks them -1 in the wall table, and the vertex stage takes them out of the way (`StoreyTerraceWall`).
- **Its door.** A walk-in setback is never in the way while the player is at its door onto the terrace, as no building is at its door (`AtDoorOf`): there the view is as indoors while the setback is between the camera and the player, as it is inside. Past the door, with the camera trailing over the setback, it is in the way and starts gone, as it ends, rather than standing whole for a moment and then sinking. Back at the door it is as indoors again at once. A shell's door is shut, so its setback is in the way at the door too.
- **With the buildings in the way left alone** (mode *Off*), the view behind the setback is as indoors, as before.
- **Props** standing on the deck go with the setback (a prop has no wall to tell it apart): they sink, darken, fade or dissolve with it, drawn whole too (`HidesPoint`).

The prototype's walks never stand on a terrace, so their frames are unchanged (`EnterExitTests.OnATerraceTheBuildingIsWholeAndTheSetbackIsInTheWay`, `TerraceOcclusionTests`).

**A pitched roof's eave goes down with the wall under it.** On the top storey the ceiling clip takes off everything above the storey's ceiling, the roof with it, but a pitched roof's eave hangs below the clip: its fascia and soffit, a hip's or a gable's by about 10 cm, a mansard's steep foot by half a metre and more, a big eave further still. The prototype's roof is all slab to the cutaway (kind 0), so over the wall it took down the eave was left in the air, a line along the roof's foot nearest the camera. Each part of a roof now knows the edge of the outline it hangs over (`RoofPart.Edge`: an eave's fascia and soffit, the end closing the soffit at a rake, the slope rising from it), and is made with the cutaway id of the top storey's wall on that edge (`MeshBuilder.CtxOf`, `Roofs.Draw`'s `walls`), so it slides down and back up as that wall does, in Play mode and in the editor's Interior tab alike. A roofed terrace's eave goes with the storey's walls under the terrace. The roof adds no wall to the cutaway's list, so the walks' slides are as the prototype's (`CutawayWallTests.APitchedRoofsEaveGoesWithTheWallUnderIt`, `ATerracesRoofsEaveGoesWithTheWallUnderIt`).

**A cut wall reads as solid.** A wall the cutaway takes down, or the ceiling clip cuts, is seen into from above, and the backs of its faces draw as the section cap (`STOREY_CAP`). But a wall was open at its foot, so an eye looking straight down along it went on down through the walls of every storey under it and saw the ground, the inside of the wall. Two things close it:

- **Its foot.** Every LOD0 wall of a walk-in storey has its foot closed a centimetre up off the floor it stands on, facing down, so from above it is the cap too: outside walls, room walls and courtyard walls (`Facade.WallPanel`), core walls and door casings (`MeshBuilder.Foot`). Only Unity draws it (`MeshBuilder.Backing`), so the prototype's census is unchanged.
- **Faces inside another wall.** Where one wall runs into another, each one's faces go on inside the other: a room wall's sides and end into the room wall it meets, that wall's face across the joint, a room wall's end into a solid stretch of an outside wall (not where it stands in a window), the outside wall's inner face across it, and a door casing's end and top in its wall's thickness. Hidden while the wall stands, they showed inside its cut as light panels across the cap. They are made apart as kind 3 (`MeshBuilder.Buried`, from `Facade.PanelOpt.buried` and `Lod0.Shared.RoomWall.Inside`), and the shader leaves them out of the building the player is in (`StoreyOcclusion.hlsl`, next to the ceiling clip). Every other building draws them, inside walls that stand. They are the same faces in more pieces, so the census's areas are the prototype's. Where a door's casing meets a crossing wall, both are left whole (the casing can lie on the other's face, and split, the coplanar check found them apart).

A ray test holds it (`CutawayWallTests.ACutWallIsSolidLookingDownIt`): from inside every full-height stretch of the walk-in storeys' walls of both corpora, cut at the stub and at the ceiling clip, a ray straight down meets the cap every time, and of those slanting along and across the wall the test asks 98.5 % (99 % do on the demo street, all but one on the variants; the rest leave by a door or a window beside, or meet a core's wall or a casing head's end, left whole to keep the triangle count near the prototype's).

**The walls facing the camera can go down too.** The cutaway takes down the walls between the camera and the player, and only those: the walls beside that one stand, though the camera looks at their outsides and they hide the room. In a building with a notch, or one seen at an angle, the player runs through it with walls sliding up and down beside them as the sightline sweeps across their ends. With **Walls Facing Camera** on (`OcclusionSettings.wallsFacingCamera`, off by default), every wall of the player's storey whose outside faces the camera goes down as well: the storey opens on the camera's side like a doll's house, and its far walls stand.
- **Which walls.** A wall faces the camera when the camera's direction from the player, in plan, is on its outside (`OcclusionCore.WallFaces`). That is its normal's side: LOD0 makes outside walls, courtyard walls, parapets, details and cores face out of what they close. For a neighbour's party wall it is the neighbour's side.
- **Nothing moves as the player walks.** The test takes the camera's direction, not its place, so a wall goes down or up only as the camera turns past it. A wall starts down once the camera is 2° round onto its outside (`OcclusionCore.FacingMargin`), and goes back up once the camera turns back past its plane, so a wall seen edge-on doesn't drop and rise as the camera wavers.
- **Partitions.** A room's walls and a stair's middle wall have an inside on either side and face both ways (`MeshBuilder.Partitions`, `Lod0Shape.Partitions`, marked as LOD0 makes them), so for them the test says nothing. They go down as before, when they are between the camera and the player.

The walls between the camera and the player still go down as without the option, a wall facing away too (round a notch, or with the player just stepped out and the building held). With it off, the walks' slides are the prototype's (`FacingWallsTests`).

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
- **Walls Facing Camera** (off): the cutaway takes down every wall facing the camera too, not only those between the camera and the player, and they move only as the camera turns (§3).
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
   - With **Walls Facing Camera** on: every wall facing the camera is down from the first step, and the far walls stand. Walk about: none of them moves. Turn the camera: each wall goes down or up as the camera passes its plane.
   - Turning the camera works from any direction.
   - Walk out onto a terrace (Linden Court's, through its door on storey 6): the whole building shows, setback and roof. Turn the camera to look through the setback at the character: the setback goes as a building in the way does, under each mode, while the six storeys under the terrace and the terrace's parapet stand as they are. Walk out of the door with the camera trailing over the setback: it's gone as you step past the door, with no storey of it standing whole first.
   - A shell-only building with a tower in the middle of its terrace: walk round behind the tower. It goes as a building in the way does; its walls never slide down onto its empty inside.
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
- **Walls:** a thin box round every collision segment (LOD0's segments, at their radius), storey by storey, merged into one box where a segment repeats on the storeys above. Each is as tall as its storey, but a terrace's parapet (1.1 m, `Lod0.Shared.TerraceParapet`) and the rails round a stair opening or an atrium (1.02 m) are as high as they are (`Seg.h`): they were a storey high, the parapet merged with the wall under it into one box reaching a storey over the deck, walls nobody could see that stopped what the visible parapet and rails let over. The walk model still takes every segment as a wall all the way up, as the player can't climb a parapet anyway.
  - **A flat roof's edge** is as high as what stands on its walls (`Lod0.Shared.RoofEdgeH`). With the top tier's style's *Parapet* on, it is a parapet's 1.2 m (`Lod0.Shared.RoofParapet`, 10 cm over its coping) over what the parapet stands on: the walls, or a raised cap, which lifts it. Without it, the edge is the 30 cm curb (`Lod0.Shared.RoofCurb`; a hanging cap leaves it), a raised cap's top however high (with the roof lifted to it, nothing stands over the roof), or, round a cap set in, where the walls' top is flush with the roof, no wall at all (`Seg.Flush`). A courtyard's edge on the roof is its parapet or curb the same way. On the roof a wall is as high as it says, where on a storey it stops at the storey's top; one that says nothing (a bulkhead, an atrium's skylight curb) is a parapet's height (`CollisionMesh.RoofWallHeight`). The edge was 1.2 m whatever stood on it: a parapet nobody could see over the visible curb, and a parapet on a raised cap lower than it is. The walk model still keeps the player on the roof.
- **Floors and terraces:** the outlines at each slab, grown by the walk model's margin, with the stair holes cut. Interior slabs that only separate two storeys of a shell building are left out.
- **Stairs:** a ramp per flight, at the walk model's heights, for both kinds of stairs. A straight flight's railed side is a wall from its floor to its handrail (90 cm over the nosings, or as its look sets it), sloping with it; a solid one on its floor has its sides as high as the ramp and its back as high as the floor above (`CollisionMesh.Fence`). The walk model has those as walls all the way up the storey.
- **Pitched roofs.**
- **A roof cap set in** (`RoofCap.inset`), where LOD0 stands it (`Lod0.Shared.InsetCapRegion`) and as high as it is. Its sides are walls. A ring's top is a wall's; a block's, over a whole roof, is the roof's (`CollisionKind.Roof`). The walk model has no cap: it walks the roof under it, which stays a floor.
- **A roof cap standing out past the walls**, a slab on them or a fascia hanging from the roof's edge: out as far as its *Overhang* sets it. Its pieces are the boxes LOD0 draws it with, cut where it is cut (along party walls, at corners cut for a neighbour, at bulkheads) and mitred at the corners (`Lod0Shape.Blocks`, `Block`). It had only the wall's thickness, so the Overhang didn't change the collider. The walk model's walls stay the wall's.
- **A shell-only building** keeps its stairs and lifts for when it is walk-in again, but builds none of them, and the collider has none of them: no ramps and no holes in its roof. Along a core flush with its wall, where a walk-in building's bulkhead takes the parapet's place, its roof's edge is the edge's height, as its parapet is drawn there. The walk model keeps them all, as the prototype's does; nobody gets into a shell to use them.

Each triangle is tagged Floor, Stair, Wall or Roof (`CollisionMesh.Kind`). The demo street's buildings have 64 to 871 triangles each: 1 to 15% of their LOD0.

**Tested** headlessly against the walk model (`CollisionMeshTests`). These tests run on both corpora:
- A ray dropped at each of `play.json`'s surface points lands where `PlayWorld.SurfaceAt` does. There are more than 3,000 points.
- Every collision segment stops a horizontal ray, at a height it stands to (one that nothing stands on, `Seg.Flush`, is no wall).
- The floors and stairs face up.

**Performance.**
- **Run time:** a collider is built only when a building is. Its collision mesh is made on the build thread with the building's first LOD0. The buildings built since the last batch are cooked together, in parallel with `Physics.BakeMesh` in an `IJobParallelFor` on Unity's job workers, while the frames go on: the colliders are assigned in a later frame, once the batch has finished, so cooking never holds a frame. No mesh is destroyed while its batch cooks (a building released meanwhile waits for it), and in edit mode, which updates only when asked, the batch is finished at once. Each mesh is given to its collider with the same cooking options, so assigning it cooks nothing. The options are `UseFastMidphase` only: the mesh is already welded and has no degenerate triangles, so cleaning and welding are skipped. The mesh is positions and indices only, with 16-bit indices.
- **Edit time:** no colliders by default, so dragging a wall cooks nothing. **Colliders in edit mode** turns them on for editor raycasts. Each edit then rebuilds the colliders of only the buildings it touched. A NavMesh bake doesn't need it (§5.1).

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
- A bulkhead and an atrium's skylight curb are walls a parapet's height, 1.2 m, on the roof.
- A cornice, bands, a plinth and the facade's details are not in the collision mesh: a wall is its thickness.
- The walk model keeps a roof at its own height under a cap over it: a raised cap's lid and a block set in are the collider's alone.
- Lift shafts are solid floor: the walk model does not let the player fall down them either.
- Gable ends above the top storey are not walls in the collision mesh. The pitched roof itself is.
- Without Colliders At Load, a building out of view or far away has no collider until its LOD0 is built. Storey Props there stay held; any other rigid body falls through it.

### 5.1 Baking a NavMesh

A NavMesh Surface (Unity's AI Navigation package) with **Use Geometry** set to **Physics Colliders** bakes the buildings from their colliders: their floors, stairs, walls and roofs (§5). A bake takes the colliders in the scene as it starts. In edit mode a site has none unless **Colliders in edit mode** is on, and then only for its buildings at LOD0, near the Scene view's camera. So every bake needed that switch turned on by hand on every site first, and off again after.

**Now there is nothing to switch.** While a NavMesh Surface can be baked, every enabled site has every building's collider. That is from when a surface is selected, or shown in an Inspector (a locked one too), until neither is true and its bake has finished.
- They are made at once as that starts, whatever LOD each building shows and wherever the camera is, as with Colliders At Load (§5). An edit meanwhile makes its buildings' colliders again.
- After, each site's own switches decide again: with Colliders in edit mode off, the colliders go.
- No setting changes, so there is no undo step, and the colliders are never saved with the scene.
- Edit mode only, as the Bake button is.
- The authoring package does it where the AI Navigation package (1.1 or 2.x) is installed: `StoreyNavMeshBake`, in an assembly of its own that only builds then.

**What it costs.** Selecting a surface makes every collider of every open site at once. That is tens of milliseconds for a level's layout, and about a second for 3,000 buildings (the table in §5, plus PhysX's cooking). While a surface stays selected or shown, each edit makes its buildings' colliders again, as Colliders in edit mode does. A surface on the Storey Site's own object is selected whenever the site is, so give it an object of its own.

**What the bake sees.**
- The site's layer must be in the surface's **Include Layers**.
- A site with **Generate colliders** off, and a building with its **Colliders** off (§5), give the NavMesh nothing, as they give physics nothing.
- A NavMesh Modifier on the site's object, with **Apply To Children**, applies to its buildings: their colliders are on objects under the site.
- **Render Meshes** would take the meshes the site draws, which aren't shapes to walk on: glass, rooms, and every building's massing in the merged cells, which the shaders hide where a building shows its detail. Use Physics Colliders.

**A script's own bake** holds the colliders itself with `StoreySite.EveryCollider()`, and disposes of it when the bake is done:

```csharp
using (StoreySite.EveryCollider())
    surface.BuildNavMesh();
```

The colliders are made before `EveryCollider` returns. For an asynchronous bake (`NavMeshSurface.UpdateNavMesh`, `NavMeshAssetManager.StartBakingSurfaces`), keep the handle until the operation is done. In Play mode, a site still loading over frames makes the rest as its load ends, so wait for `StoreySite.Loading` to clear first. The same handle serves any other tool that reads the colliders in edit mode.

**Checks in Unity:**
1. Give a NavMesh Surface (Physics Colliders) an object of its own, in a scene with a Storey Site whose *Colliders in edit mode* is off. Select the surface: every building gets a MeshCollider, also those far from the Scene view's camera. Bake: the NavMesh covers the floors and stairs and goes round the walls, on every building.
2. Select something else: the colliders go once the bake has finished. The Undo history has no step for them, and the site's settings haven't changed.
3. Open a second Inspector and lock it on the surface. Select the site, and in the other Inspector move a wall: the building's collider follows. Bake in the locked one: so does the NavMesh.
4. With the surface selected, enter Play mode: the site makes its colliders as its own switches say.

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

## 7. Other sites' buildings in the way

The occlusion drives one site, the one the player is in (§4; with districts, the one *Follow Player* picks, docs/CITY.md §5). But other sites can be loaded at the same time. The next district over is one, when the player walks along its edge. A prop or landmark placed as a site of its own is another: a bridge's pillars built in Storey, each a prefab holding a Storey Site with a one-building layout. Their buildings stand between the camera and the player as often as the district's do. They were never in the way, because the occlusion core only knew the buildings of the walk model it was made from. Now they go as the district's do, under the same mode.

**Which sites.** Each frame, after the site the player is in, Storey Occlusion looks at every other enabled site whose ground plan (`StoreySite.PlanArea`, its buildings' bounds) comes within 1 m of the camera's line to the player, in plan (`Districts.Area.Distance` for a segment). It also keeps looking at a site with a building still going or coming back up, so a building that has just left the line comes back up as one of the player's site does. These are `StoreyOcclusion.Guests`. A site out of them is left alone and costs nothing.

**Each in its own space.** A site round the player's runs its own core: its walk model, made on a build thread the first time it is needed (`BuildLane.Forced`), with the player and the camera moved into its space. The core's frame for it is `OcclusionCore.FrameOccluders`: its buildings in the way alone, by the same three rays, door rule, fade and hold. There is no view. None of its buildings holds the player, so none is clipped or cut away, and any can be in the way, even one the player stands in. Its view stays neutral, and walls left down from when the player was in the site slide back up. Building ids only mean something within a layout, and most layouts have a `b1`, but each core knows only its own site's buildings, so no lookup by id reaches into another site. A district's site does hold copies of the next district's buildings along the edge, for the party walls (docs/CITY.md §5). It never draws them, so its core leaves them alone, and the next district's own core takes the building instead: one slot, where the copy held one it could never draw.

**One set of slots.** Every site draws through one building table (docs/CITY.md §5), and its sixteen occluder rows are the shaders' globals. So every core takes its occluders' slots from the table's one set (`BuildingTable.OccluderSlots`, an `OccluderSlots`), and each core writes only its own slots' rows. A building of a site round the player's waits for a free slot when all sixteen are taken, as one of the player's site does. A core that is let go gives its slots back (`OcclusionCore.Clear`): when the player leaves a site, a site stops being looked at, or a site's layout changes or is destroyed. A second Storey Occlusion for the other sites wouldn't do: it would write the same rows and the view globals over the first's.

**Written through the site's own renderer.** A site round the player's has its occluders written into the table through its own renderer: its table rows, its height (the rows' heights are world heights, and a pillar's site stands where it is placed), its footprints (Sink's and Dissolve's) under its own transform and on its layer. Its renderer swaps the building to the discarding material as the slot lands in its row, as for any building in the way. Its buildings in the way are kept at LOD0 (`StoreySite.PickFor`, as in docs/CITY.md §2). Its LOD0 along the camera's line is prefetched, and its props drawn whole hide by its own core (`StoreySite.OccludedBy`, `StoreyOcclusion.CoreOf`). The view globals are still written once, by the site the player is in.

**Districts and the rest.** **District** on the Storey Site (on by default) says the site is a district of the city, one the player walks about in. *Follow Player* follows districts alone. A prop's site is a few metres across, and with the player more than 4 m out of every district's box (on a bridge, say), the nearest box was a pillar's: the occlusion moved into its one-building site, and no district's building was in the way until the player came back. Turn **District** off on a site placed as a prop. Its buildings still go when they are in the way, but the occlusion never follows the player into it, and *Find neighbouring districts* neither links it nor links it to others: a prefab moves, and a neighbour is linked by where it stands.

**For a project's own systems.** `StoreyOcclusion.Guests` lists this frame's sites round the player's, and `CoreOf(site)` gives each one's core: its `Active` occluders, their rows in `Rows` at their slots, and `HidesPoint`, all in that site's space. `Core` is still the player's site's alone.

**What it costs.** Nothing for a site away from the line. For one near it, a frame of its core: the candidates along the line from its grid, three segment tests each, rejected by their boxes first (docs/PERFORMANCE.md §10.5). That is a pillar's one building, or the edge of the next district. No garbage a frame (`OtherSitesOcclusionTests.AnotherSitesFrameMakesNoGarbage`).

**Limits.**
- A walk-in building of a site the player isn't in isn't walked: the player inside it sees it in the way, not cut away. A building the player enters belongs in a district.
- A site round the player's must be unrotated and unscaled, as every site must (its inspector warns): its space is its position's alone.
- A district the player leaves has its buildings in the way cleared at once, as before, and looked at again as a site round the player's from the next frame. One still in the way comes back in over the fade (0.2 s).

`OtherSitesOcclusionTests` holds the core's half. Two sites' buildings with the same id take slots of their own and hide their own. A site waits for a slot when all are taken. A neighbouring district's building is its own district's to take. A site the player isn't in has no view. And the line's distance to a ground plan. The Unity half is pinned in its source there, as for §6.

**Checks** (in the editor):
1. Place a site of its own (a prefab with a one-building layout, **District** off) next to a district, and walk so it stands between the camera and the player: it goes under each mode, its footprint where it stands. A district building in the way at the same time goes too.
2. Walk along a district's edge with a building of the next district between the camera and the player: it goes.
3. With the player on a bridge more than 4 m out of every district, near a pillar's site: the district's buildings still go when they are in the way (the occlusion stayed on the district).
4. Turn **District** back on for the pillar's site, and select a district: *Find neighbouring districts* links it. Turn it off: neither links it.
