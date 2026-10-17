# Quality tiers and the URP settings beside them

A tier is two things a project picks at startup from the device model: a Storey quality asset (the package's `Quality Tiers` folder has the two the plan defines) and a URP Quality Level, whose pipeline asset and renderer hold everything below. Storey's quality asset sets Storey's own numbers: the LOD distances, the residency caps, the build budget and threads, the lighting keyword, and Storey Performance Run's pass mark.

The URP settings below are starting points, not measurements. Every number in the repository is headless so far. Confirm each tier with **Storey Performance Run** (the play kit), which reports frame-time percentiles per path against the quality asset's **P99 Frame Ms**, and with Xcode's GPU tools where there is a Mac (docs/PERFORMANCE.md §14).

| Setting | Primary tier (iPhone 14 and newer, 60 fps) | Minimum tier (iPhone 7, 30 fps) | Why |
|---|---|---|---|
| Storey quality asset | Storey Primary Tier | Storey Minimum Tier | The LOD residency, chunks, build budget (2 and 4 ms), lighting and pass mark per tier (docs/CITY.md §7). |
| `Application.targetFrameRate` | 60 | 30 | Mobile runs below the screen's rate unless it is set (docs/SPEC.md §6.7). |
| Rendering path | Forward+ with the GPU Resident Drawer; Forward without | Forward, unless the GPU Resident Drawer measures better | The GPU Resident Drawer needs Forward+ or Deferred+. Unity recommends Forward on low-end mobile, and with a mesh per building the drawer can't instance anything: its gain is on the CPU. Measure both on the device (docs/PERFORMANCE.md §7.10). |
| GPU occlusion culling | Off unless it measures better | Off | Conservative for merged cells and tall buildings, it forces the depth texture and doesn't cull shadow casters. |
| Depth priming | Disabled | Disabled | A depth prepass draws every building whole, doubling the vertex work. Off on iOS anyway (docs/SPEC.md §6.7). |
| Depth texture | Only when a feature needs it, copied After Transparents | Off | The copy is cheaper in bandwidth than a prepass on tile-based GPUs. |
| Opaque texture | Off | Off | Storey's glass doesn't read it. |
| Main light shadow cascades | 2 | 1 | Each cascade draws the shadow casters again. A building at LOD1 casts through its massing (about 43 triangles), and LOD0's rooms cast only for the player's building. |
| Shadow distance | About where LOD1 hands over to the massing: 150 to 250 m | 60 to 100 m | Past it a building shows its massing, whose shadow shows little at that size. |
| Main light shadow resolution | 2048 | 1024 | |
| Soft shadows | Low, or off | Off | The soft-shadow keywords multiply the colour pass's variants too. |
| Additional lights | Per pixel, 4 an object, no shadows | Per vertex, or off | |
| SSAO | Off | Off | Storey has no DepthNormals pass, so SSAO in depth-normals mode misses the buildings (docs/PERFORMANCE.md §7.10). |
| HDR | On only for post-processing that needs it, 32-bit format | Off | Bandwidth. |
| MSAA | Off, or 2× without an upscaler | Off | |
| Resolution | Storey Auto Resolution (dynamic resolution, 60 to 100 %), or Render Scale with STP | Storey Auto Resolution, or a fixed Render Scale of about 0.75 | Fill rate is the minimum tier's main lever. STP runs on Metal through Render Scale only, not dynamic resolution. |
| SH Evaluation Mode | Auto (per vertex on mobile) | Auto | Storey's default lighting follows it (docs/PERFORMANCE.md §7.3). |
| URP Global Settings, Strip Unused Variants | On | On | URP leaves out its keywords' variants for features no pipeline asset in the build uses. Storey's build step leaves out the minimum tier's lighting variants when no quality asset in the build uses them (`StoreyShaderStripping`). |
| Player Settings, Frame Timing Stats | On | On | The CPU and GPU times in Storey Diagnostics, Storey Performance Run and Storey Auto Resolution. |
| Shader warm-up | Storey Shader Warm-Up with a traced collection for Metal | The same, traced on the minimum tier's settings | A pipeline state met mid-game is compiled in that frame (docs/PERFORMANCE.md §7.9). |
| `Application.backgroundLoadingPriority` | BelowNormal (Unity's default: about 4 ms of integration a frame) | Low (about 2 ms), or BelowNormal behind a loading screen | A district's scene integrates its assets on the main thread in the same frames as Storey's **Build Budget Ms** (2 and 4 ms): the two share the frame. Measure Unity's `Application.Integrate Assets in Background` marker on the device (docs/PERFORMANCE.md §6.9). |
| Physics broadphase | Automatic Box Pruning | Automatic Box Pruning | Colliders come and go with buildings' LOD0 (up to the quality asset's **Colliders Kept**); sweep-and-prune, the default, degrades with many static colliders added and removed (docs/PERFORMANCE.md §10.6). |
| `Physics.autoSyncTransforms` | Off | Off | Unity's default; on, every transform change syncs physics at once. |

## Shader warm-up

Add **Storey Shader Warm-Up** (*Add Component ▸ Storey*) to the first scene.

1. Make a development build with its **Trace** on.
2. On each tier's device (or its quality level, on one device), walk the city with the play kit, or let Storey Performance Run do it: overview, streets, into buildings, cut and faded buildings. When the app goes to the background or quits, the states it used are saved to `Storey <platform> <API>.graphicsstate` in its persistent data folder, and sent to the editor if one is connected.
3. Add each file to the project (it imports as a Graphics State Collection) and to the component's **Collections**.

A release build then makes the matching collection's states a few a frame from the start, or all in the first frame (**Per Frame** 0) behind a loading screen. Trace again when the shaders, URP's settings or the quality levels change. Unity keeps one collection per platform and graphics API.
