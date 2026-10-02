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
| 5.2 | Occlusion core (**done except Sink's footprint mesh**: `Occlusion/OcclusionCore`; the walks' view, occluders, rows and walls match frame by frame) | `Runtime/Occlusion`: the view (active building, ceiling clip, cutaway range, the camera offset applied to the player), the sliding cutaway per wall id, buildings in the way (the three rays, the door rule, the fade and hold, slots, Slice heights, Sink's plan and rows), camera assist, Sink's footprint mesh | The walks' view, occluders and walls match frame by frame |
| 5.3 | `OcclusionSystem` | The Unity component: takes a camera and a focus, runs 5.2 each frame, writes `_StoreyState.w`, `_StoreyOcc`, `_StoreyWall` and the globals, shows the footprint meshes; `StoreyOcclusionSettings` (mode, stub, base height, hole size, silhouette, assist) | The demo street in Play mode occludes as in the prototype |
| 5.4 | Play kit | `com.triband.storey.playkit`: third-person controller on 5.1, follow camera with orbit, keyboard, gamepad and on-screen joystick, lift panel, silhouette; **Play from this floor** in the Interior tab | Walk every floor of a 10-floor building from any camera yaw without losing the character (SPEC §8) |
| 5.5 | Device run | iPhone 7 and iPhone 14 builds of the demo street; draws, triangles, memory and frame time recorded; the minimum tier's quality settings | The numbers replace the estimates in UNITY-PACKAGE-PLAN §6.6 |

5.1 and 5.2 are headless; 5.3 and 5.4 need an editor session to check, 5.5 a device.

## 3. Storey's own

Straight flights (`CoreType.Flight`) are not in the prototype. Their walk surface and slab holes follow their geometry (docs/EDITOR.md §5) and have their own tests.
