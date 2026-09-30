# The Unity packages

Ports of the prototype into Unity packages, and the test harnesses that keep the
ports honest without an editor. The design is `../docs/SPEC.md`; the port plan
is `../docs/UNITY-PACKAGE-PLAN.md`.

| Package | What it is | Assemblies |
| --- | --- | --- |
| `com.triband.storey` | Buildings: data model, geometry generation, LODs, occlusion, streaming hooks. Bake-only runtime. | `Triband.Storey` (engine-free), `Triband.Storey.Unity` |
| `com.triband.storey.authoring` | Editor tooling: importer, inspectors, tools and overlays, district bake. Editor-only. | `Triband.Storey.Editor` |
| `com.triband.storey.playkit` | The prototype's play mode: character, follow camera, joystick, lift UI. Sample-grade. | `Triband.Storey.PlayKit` |

## The split everything depends on

`Runtime/` in the runtime package is **engine-free** (`noEngineReferences: true`,
no references at all). That is what lets the same sources build as a plain .NET
library and be tested with `dotnet test`, which is where the generation half, the
half with the risk in it, is judged against the prototype's fixtures. Anything
that touches `UnityEngine` lives in `Unity/` (or the editor and play kit
packages) and is compiled headlessly against hand-written stubs of exactly the
engine members it uses: a compile check, not a behaviour check, and the only one
that half gets without a licence.

```bash
# Everything, no editor, no licence. Behaviour against fixtures, the engine-facing
# code against stubs, and the package layout (asmdefs, manifests, .meta files).
dotnet test unity/Headless/Triband.Storey.Tests

# The .meta files, without which Unity ignores a package installed from a git URL.
# Run without --check to write the missing ones (GUIDs derive from the path).
python3 unity/tools/meta.py --check

# Reference answers, from the implementation that works (needs npm; ~2 min headless).
cd prototype/tools && npm install && npm run fixtures
```

## Getting a layout into Unity

Copy the text from the prototype's **Layout data** panel into a file with the `.storey`
extension anywhere under `Assets/`, or use **Tools > Storey > Import Prototype Layout...**.
The importer makes a `StoreyDocumentAsset`: the text plus a summary (buildings, floors,
walk-in count, cores, details, unknown keys). Nothing is generated at import; that is a
separate step from workstream 2 on. A key the model does not know is an import warning
here and a failing fixture test in the headless suite.

A fresh Claude Code session installs the .NET SDK through `.claude/session-start.sh`.
`.github/workflows/unity-tests.yml` runs the same two commands on pushes touching
`unity/`; it needs no secrets and can be deleted without losing anything.

## Layout

```text
unity/
  Packages/manifest.json            pins + "testables" for the (optional) editor project
  Packages/com.triband.storey*/     the packages, with committed .meta files
  Headless/
    Triband.Storey.Headless/        Runtime/** as a netstandard2.1 library
    Triband.Storey.Stubs/           Unity/**, Editor/**, play kit against Stubs/UnityEngine.cs etc.
    Triband.Storey.Tests/           xunit: fixtures, layout rules, smoke tests
  Fixtures/                         reference answers emitted by the prototype (workstream 1)
  tools/meta.py                     .meta generator
  Assets/, ProjectSettings/         the Unity project, for when a scene is wanted
```

## The first editor session: shader compile and visual parity

Workstream 4 was written against stubs. The first time the project opens in Unity 6.3:

1. Fix whatever the three shaders under `Packages/com.triband.storey/Unity/Shaders` fail to compile (they include URP's `Core.hlsl` and `Lighting.hlsl` and use `UniversalFragmentPBR`; the include paths and function names are the ones documented for URP 17, but nothing here has been through the compiler).
2. Make three materials from them (Storey/Opaque, Storey/Glass, Storey/Massing).
3. Import `unity/Fixtures/demo.json` as a `.storey` file (Tools > Storey > Import Prototype Layout...), add a **Storey Street** component to an empty GameObject, assign the layout and the materials, press Play. `Displayed LOD` switches all buildings between LOD0, LOD1 and LOD2; `Lod Tint` colours them by LOD.
4. Compare with the prototype at the same camera positions and keep the screenshots under `unity/Parity/`.

## Opening it in Unity (optional)

The folder is also a Unity 6.3 LTS project with the packages embedded, for the
things a headless harness cannot ask: how it looks, what a frame costs on a
device, whether GRD instancing behaves. Open `unity/` with 6000.3.x; if the Hub
offers a different 6.3 patch, accept it and commit `ProjectSettings/ProjectVersion.txt`
and `Packages/packages-lock.json`. Create a URP asset, assign it under
Project Settings > Graphics with Forward+, `BatchRendererGroup Variants = Keep All`
and the GPU Resident Drawer on (Plan §6.1), and commit the generated settings. If the
editor rewrites any committed `.meta` with a different GUID, `meta.py --check`
reports it; keep the editor's version.

Consumers install a package by git URL with a path suffix, e.g.
`https://github.com/lasseastrup/BuildingInterior.git?path=/unity/Packages/com.triband.storey#v0.1.0`.
A package's dependencies cannot be git URLs, so a project installing from git adds
`com.triband.storey` and `com.triband.storey.authoring` both (Plan §2).
