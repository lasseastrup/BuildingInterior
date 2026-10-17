# Changelog

## [Unreleased]

### Changed
- The character asks its site to build full detail round where it spawns (`StoreySite.PrefetchAround`): the building it stands in, and those round it, get their LOD0 ahead of the rest of the city. A teleport through `Spawn` does the same; a game behind a fade waits for `StoreySite.DetailReadyAround`.
- The character keeps its place in the world when the occlusion moves to another district.

### Added
- The play kit (docs/PLAY.md, slice 5.4; stub-compiled, not yet run in an editor). **Storey Play Kit** sets up a walkable layout in one component. `StoreyCharacter` walks the engine-free walk model and shows the lift panel and where it is, with a silhouette through walls (`Storey/Silhouette`). `StoreyPlayCamera` orbits and follows, with camera assist. `PlayInput` takes keyboard, mouse, gamepad and touch (an on-screen joystick) through the Input System, or the old Input Manager. Depends on `com.unity.inputsystem`.
- Package skeleton: runtime assembly definition, smoke test (workstream 0).
