# Workstream 5: occlusion and play

Buildings are walked in Play mode to check occlusion and circulation (SPEC §5, §7). The occlusion is the runtime feature games ship; the play kit is the reference character and camera that drives it (UNITY-PACKAGE-PLAN §2). Exit test: walk the demo street on an iPhone 7, with the first measured numbers for SPEC §6.6.

## 1. Where the logic lives

The prototype's play mode is plain functions over the layout and the LOD0 data: where the player is, what they stand on, which walls stop them, which walls drop for the cutaway, which buildings are in the way and how far each has sunk. These are ported to the engine-free runtime and judged against the prototype, as the generator and the edit operations are.

`prototype/tools/export-play.mjs` (`npm run play`) writes `unity/Fixtures/play.json`. It loads the demo street and the variant set headlessly, and records the following:

- **Collision segments:** every building's segments per storey.
- **Walk queries:** `surfaceAt`, `collide`, `playerLoc` and `segHitsBuilding` at seeded points over and around every building and storey.
- **Walks:** play mode stepped at a fixed 1/30 s with held keys. The walks go from outside every walk-in building's street door in through it and round under each buildings-in-the-way mode, and up every switchback stair, two storeys. Each frame records the player, the camera, the view uniforms, every occluder (slot, amounts, row) and every sliding wall.

The prototype exposes these through `__sb.play`, and its frame loop can be held so the harness steps play itself. The Unity side is then thin: a component reads the player and the camera, runs the engine-free step, and writes the results into the building table and the shader globals.

Collision is the prototype's model, not physics: walkable surfaces by height, and 2D wall segments with a radius per storey (UNITY-PACKAGE-PLAN §4.4: meshes never need to be readable, no mesh colliders). A project with its own physics character can ask the same queries.

## 2. Order of work

| # | Slice | Contents | Done when |
|---|---|---|---|
| 5.1 | Walk model (**done**: `Play/PlayWorld`, `Walker`, `FollowCamera`; every `play.json` query and the 16 walks match) | `Runtime/Play`: the building and floor the player is in, the surface under a point (floors, terraces, roofs, both kinds of stairs, slab holes), collision against LOD0's segments, the player step (walk, run, gravity, step-up limit), the lift ride, the follow camera | `play.json`'s segments, surface, collide and loc cases match; the walks' player and camera match frame by frame |
| 5.2 | Occlusion core (**done**: `Occlusion/OcclusionCore`, `SinkFootprint`; the walks' view, occluders, rows and walls match frame by frame) | `Runtime/Occlusion`: the view (active building, ceiling clip, cutaway range, the camera offset applied to the player), the sliding cutaway per wall id, buildings in the way (the three rays, the door rule, the fade and hold, slots, Slice heights, Sink's plan and rows), camera assist, Sink's footprint mesh | The walks' view, occluders and walls match frame by frame |
| 5.3 | `OcclusionSystem` (**written; editor check outstanding**, §4: `StoreyOcclusion`, `StoreyOcclusionSettings`) | The Unity component: takes a camera and a focus, runs 5.2 each frame, writes `_StoreyState.w`, `_StoreyOcc`, `_StoreyWall` and the globals, shows the footprint meshes; `StoreyOcclusionSettings` (mode, stub, base height, hole size, silhouette, assist) | The demo street in Play mode occludes as in the prototype |
| 5.4 | Play kit (**written; editor check outstanding**, §4: `StoreyPlayKit`, `StoreyCharacter`, `StoreyPlayCamera`, `Storey/Silhouette`) | `com.triband.storey.playkit`: third-person controller on 5.1, follow camera with orbit, keyboard, gamepad and on-screen joystick, lift panel, silhouette; **Play from this floor** in the Interior tab | Walk every floor of a 10-floor building from any camera yaw without losing the character (SPEC §8) |
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

  It shows Sink's footprints. The site's renderer hands it the view in Play mode and leaves its own view globals alone.
- **The play kit:** add **Storey Play Kit** to any object (*Add Component ▸ Storey ▸ Play Kit*). It adds the occlusion system to the site if there is none, and makes the character and the camera. The character walks the walk model, with no physics.
- **Play from this floor:** a button in the Interior tab. It starts Play mode on the active storey, in front of its stairs or lift. A scene without a play kit gets one.

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
7. **Play from this floor:** in the Interior tab on floor 3. Play starts there.
8. **Moved site:** move the site off the origin and repeat 2.

**Not yet:**
- The lift car is not drawn.
- Occluders are not forced to LOD0 when Displayed LOD is higher (the LOD manager, workstream 7, does that).
- The silhouette shader must be referenced by a material (the character's *Silhouette* slot) to be in a player build.
