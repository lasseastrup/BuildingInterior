# Changelog

## [Unreleased]

### Added
- **Storey Performance Run** (`StoreyPerformanceRun`, *Add Component ▸ Storey ▸ Play Kit*): a performance run for a device (docs/PERFORMANCE.md §14). Once every site has loaded, it takes the camera, and the occlusion's player, along fixed paths. These are an overview and an orbit, a walk along the layout's longest street with the play camera's trailing pose, a climb of the tallest building a storey every two seconds, a teleport to the street's far end, and a crossing into the nearest other site. For each it reports the frame time's median, 95th and 99th percentiles and worst; the main thread's, render thread's and GPU's with Frame Timing Stats on; draw calls, SetPass calls, batches and vertices; memory; and, in a development player, the garbage a frame and Storey's own stages. A path passes when its 99th percentile is under the quality asset's **P99 Frame Ms**. The report shows on screen with a Copy button, is saved beside Storey Diagnostics' record (`StoreyPerformance.txt`), and is noted in it. The character and the play camera are paused meanwhile and put back after.
- Profiler markers: "Storey walk: character", "Storey walk: camera" and "Storey hud: the place and the lift panel" (docs/PERFORMANCE.md §10.5).

### Changed
- The character asks its site to build full detail round where it spawns (`StoreySite.PrefetchAround`): the building it stands in, and those round it, get their LOD0 ahead of the rest of the city. A teleport through `Spawn` does the same; a game behind a fade waits for `StoreySite.DetailReadyAround`.
- The character keeps its place in the world when the occlusion moves to another district.

### Added
- The play kit (docs/PLAY.md, slice 5.4; stub-compiled, not yet run in an editor). **Storey Play Kit** sets up a walkable layout in one component. `StoreyCharacter` walks the engine-free walk model and shows the lift panel and where it is, with a silhouette through walls (`Storey/Silhouette`). `StoreyPlayCamera` orbits and follows, with camera assist. `PlayInput` takes keyboard, mouse, gamepad and touch (an on-screen joystick) through the Input System, or the old Input Manager. Depends on `com.unity.inputsystem`.
- Package skeleton: runtime assembly definition, smoke test (workstream 0).
