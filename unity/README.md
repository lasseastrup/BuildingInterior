# Storey Unity test project

Unity 6.3 LTS project that embeds the three Storey packages (`Packages/com.triband.storey*`) so they can be edited in place, tested, and demoed. See `../docs/UNITY-PACKAGE-PLAN.md` for the plan and `../docs/SPEC.md` for what the packages implement.

## First open

1. Open `unity/` with Unity 6.3 LTS (6000.3.x). If the Hub offers a different 6.3 patch, accept it; `ProjectSettings/ProjectVersion.txt` then updates itself and should be committed.
2. Package resolution: the manifest pins the Unity 6 versions of Burst, Collections, Mathematics and URP. If the editor reports that a version does not exist for 6000.3, take the version the Package Manager suggests and commit `Packages/packages-lock.json`.
3. URP is a dependency but the project has no pipeline asset yet: `Assets > Create > Rendering > URP Asset (with Universal Renderer)`, assign it under `Project Settings > Graphics`, and set the renderer to **Forward+**, `BatchRendererGroup Variants = Keep All`, `GPU Resident Drawer = Instanced Drawing` (Plan §6.1). Commit the generated `ProjectSettings/*.asset` files and the new assets.
4. `Window > General > Test Runner` should list `Triband.Storey.Tests`, `Triband.Storey.Editor.Tests` and `Triband.Storey.PlayKit.Tests` with one passing smoke test each.

## Layout

```text
unity/
  Assets/                     test scenes, fixtures, the demo street (nothing a consumer needs)
  Packages/manifest.json      pins + "testables" so the package tests show in this project's Test Runner
  Packages/com.triband.storey             runtime package (bake API, LOD, occlusion, shaders)
  Packages/com.triband.storey.authoring   editor package (importer, tools, inspectors, bake)
  Packages/com.triband.storey.playkit     sample-grade character, camera and lift UI
```

Consumers install a package by git URL with a path suffix, e.g.
`https://github.com/lasseastrup/BuildingInterior.git?path=/unity/Packages/com.triband.storey#v0.1.0`.

## CI

`.github/workflows/unity-tests.yml` runs Edit Mode and Play Mode tests with GameCI on every push and pull request. It needs three repository secrets: `UNITY_LICENSE`, `UNITY_EMAIL`, `UNITY_PASSWORD` (see https://game.ci/docs/github/activation). Until they exist the workflow fails at activation, which is expected.
