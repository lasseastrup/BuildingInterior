# Unity package plan

How the Storey Builder prototype becomes a Unity package, what stays in the prototype, and what has to be true about performance, memory and usability at each step. The prototype (`prototype/index.html`) stays alive as the design lab: features are tried there first and ported; its saved JSON is an import format for Unity, so the same demo buildings verify both.

Decisions taken for this plan: **Unity 6.3 LTS minimum**, **internal distribution** (git URL for early phases, a scoped registry once there is more than one package), **bake-only runtime** (meshes and colliders are assets; the runtime does LOD, streaming and occlusion), **JSON bridge** from the prototype.

## 1. What has to be ported

The prototype is a 3,400-line single file with these subsystems (line counts are a rough size of each port):

| Prototype subsystem | Lines | Unity home | Notes |
|---|---|---|---|
| Data model, undo, persistence | ~130 | Runtime data (`BuildingData`, `FloorData`, `Style`, `Core`, `Detail`), Editor undo | Same fields as SPEC §3; undo comes free from `Undo.RecordObject` / SerializedObject. |
| Building generation (slabs, wall panels, openings, setbacks, terraces, roofs, party walls, cores, details) | ~820 | Runtime assembly, Burst jobs where hot | The largest and most valuable piece. Pure data-in / mesh-out today; keep it that way. |
| Straight skeleton, polygon clipping, triangulation | (inside the above) | Runtime, third-party or own | See §5 libraries. |
| Materials, per-building tables, occlusion shader | ~270 | Shaders (HLSL include + Shader Graphs), `BuildingTable` | Two float textures in the prototype become one `GraphicsBuffer`. |
| LOD system, cells (HLOD), LOD2 massing, memory accounting | ~190 | Runtime `LodManager`, editor bake of cells | |
| Occlusion (view state, buildings in the way, cutaway, Sink footprints) | ~180 | Runtime `OcclusionSystem` + shader | Camera/player agnostic: takes a focus point and a camera. |
| Play mode (controller, camera, physics, lifts) | ~140 | **Sample**, not the package core | A test harness; game code replaces it. |
| Edit tools, outline editing, pointer input, handles overlay | ~700 | Editor assembly: `EditorTool`s, Overlays, Handles | Biggest usability surface; re-designed for Unity idioms rather than ported 1:1. |
| Panels, chrome, modals, city stats | ~500 | Editor: UI Toolkit inspectors and windows | |
| City generator, perf card | ~40 | Editor test utility + performance tests | |

Everything in `docs/SPEC.md` stays the design reference; this document is about packaging, process and the engineering constraints.

## 2. Package split

Recommendation: **three packages, one repository**, released together but versioned independently.

| Package | Contents | Why separate |
|---|---|---|
| `com.<studio>.storey` (**runtime**) | Data model, generator (bake API), shaders, `BuildingTable`, `LodManager`, `OcclusionSystem`, streaming hooks, colliders. Depends on URP, Burst, Collections, Mathematics. | This is what shipped games carry. It must stay small, allocation-free at runtime, and free of editor code. Games that only consume baked districts need nothing else. |
| `com.<studio>.storey.authoring` (**editor**) | Inspectors, EditorTools and Overlays, bake pipeline (ScriptedImporter for `.storey` files, district/HLOD bake), prototype JSON importer, city generator, validation (coplanar test) as menu commands. Editor-only assembly; depends on the runtime package. | Level artists install it; build machines don't need it beyond the bake step. Independent cadence: tooling changes weekly, the runtime rarely. |
| `com.<studio>.storey.playkit` (**sample-grade runtime**) | Third-person controller, follow camera, virtual joystick, lift UI: the prototype's play mode. Also shipped as a *Sample* of the runtime package so it can be imported into a project and edited. | Projects have their own character and camera; the occlusion system exposes an API for them and the kit is only the reference implementation. |

Facts that shape this: a package's `dependencies` cannot be git URLs, only the project manifest can hold git deps, so a multi-package split needs the three packages installed together (embedded or git URLs in the project manifest during phases 1–3) and a **scoped registry** (Verdaccio/Cloudsmith, npm protocol) once they are versioned separately. Optional integrations (Splines, Entities) are compile-time `versionDefines` in the asmdef, not more packages.

Repository: `Packages/com.<studio>.storey*` embedded in a Unity 6.3 test project (`UnityProject/`), with the prototype alongside (`prototype/`). Embedded packages are editable in place, and the test project is where tests and the demo scene live.

## 3. Package layout and conventions

Standard UPM layout per package (`~` folders are not imported):

```text
com.<studio>.storey/
  package.json          name, version (SemVer, start 0.1.0; MAJOR 0 until the data format is stable), unity "6000.3",
                        dependencies (exact versions), samples []
  README.md  CHANGELOG.md  LICENSE.md  Third Party Notices.md
  Runtime/<Studio>.Storey.asmdef              (+ AssemblyInfo.cs: InternalsVisibleTo Editor and Tests assemblies)
  Runtime/Shaders/                            BuildingOcclusion.hlsl, Facade.hlsl, Shader Graphs
  Tests/Runtime/<Studio>.Storey.Tests.asmdef  (defineConstraints UNITY_INCLUDE_TESTS)
  Samples~/DemoStreet/                        the prototype's demo buildings imported from JSON, plus the play kit
  Documentation~/                             manual (workflows), reference (settings), API
com.<studio>.storey.authoring/
  Editor/<Studio>.Storey.Editor.asmdef        includePlatforms ["Editor"]
  Tests/Editor/<Studio>.Storey.Editor.Tests.asmdef
```

Rules to hold from day one:

- **Versioning:** SemVer as Unity defines it (MAJOR = breaking public API or data format, MINOR = additions, PATCH = fixes). The data format version lives inside the asset too (`BuildingData.version`) with migration code, so old scenes load after a MINOR bump.
- **Data format:** the prototype's JSON schema (SPEC §3) is the interchange format. The importer reads `state.buildings[]` and writes `.storey` assets; the same JSON is committed as test fixtures so the Unity generator can be compared against the prototype's numbers (triangle counts, coplanar test).
- **Third-party code** is vendored under `Runtime/ThirdParty/<lib>/` with its licence in `Third Party Notices.md` (see §5).
- **Tests are testable from a consuming project:** the test project's `manifest.json` lists the packages under `"testables"`.

## 4. Editor: workflow, performance and usability

### 4.1 Authoring model

- **Authored data = a `.storey` asset** (ScriptableObject serialised via a `ScriptedImporter` on a `.storey` JSON file). Deterministic, cache-server friendly, diff-able in git, and the same format the prototype writes. The importer (`OnImportAsset`) runs the generator and adds the LOD meshes and colliders as sub-assets (`ctx.AddObjectToAsset`), so a building regenerates when its file changes and never at scene load.
- **Scene placement = a `Building` MonoBehaviour** referencing the asset plus position and a building index. A district (scene or Addressables group) holds many. Party walls depend on neighbours, so a district-level bake step recomputes shared walls and HLOD cells (`AssetDatabase.RegisterCustomDependency` on the neighbour set).
- **Style presets** are `FacadeStyle` ScriptableObjects; facade details are `FacadeDetailDefinition` assets (SPEC §4.4) so artists add kinds without code.

### 4.2 Editor tooling (usability)

Unity 6.3 idioms, not a 1:1 port of the DOM panels:

- **One `EditorToolContext`** ("Storey") with tools for Shape, Facade and Interior, mirroring the prototype's three tabs. Tools derive from `EditorTool`, draw with `Handles` inside `EditorGUI.BeginChangeCheck` + `Undo.RecordObjects`, and expose settings through a **Tool Settings Overlay** (UI Toolkit `Overlay`). Transient overlays (floor list, tier list) are added with `SceneView.AddOverlayToActiveView` only while the tool is active. This is the ProBuilder 6 pattern and what users expect in Unity 6.
- **Inspector in UI Toolkit** (`CreateInspectorGUI`): the building asset's inspector holds what the prototype's panel holds (style, windows, colours, roof, details by rule, occlusion preview). IMGUI only where UI Toolkit lacks a control.
- **Snapping:** respect the global grid snap (`EditorSnapSettings`) for outline corners and cores, plus the prototype's semantic snaps (wall alignment, flush cores, neighbour outlines).
- **Ghosts and previews** before commit, as today (door ghost, detail ghost, core ghost with the fit colour). Every commit is one undo step with a readable name.
- **Floor navigation** as an overlay list (the prototype's right-hand floors panel), with the Interior tool clipping the view above the active floor exactly as in play (SPEC principle 4).
- **Play-mode check inside the editor:** the play kit runs in Play mode with the occlusion settings asset, so "walk it" is one click.

### 4.3 Editor performance

- **Never generate in `OnValidate`** (it runs on load threads and often); generation happens in the importer or in an explicit debounced bake driven from `EditorApplication.update`, reported through the `Progress` API when it takes longer than a frame.
- **Incremental:** regenerate only the edited building (and neighbours whose party walls changed), storey by storey as the prototype's resumable jobs do, so dragging a handle stays interactive. LOD1/LOD2 regenerate after the drag ends.
- **Burst in the editor:** the wall-panel, slab and roof generators are jobified over `NativeList`s; the first run may be un-Bursted (async compilation) so bake timings are measured on warm runs. Managed helpers (Clipper2) run on the main thread per building, which is fine for authoring but is why they're isolated behind an interface.
- **Domain reload:** editor state is static-free or reset with `[InitializeOnEnterPlayMode]`, so `EnterPlayModeOptions.DisableDomainReload` works for the team.
- **Budget:** a 10-floor building regenerates in < 50 ms warm (the prototype does ~20 ms in JS); a district of 300 buildings bakes in < 30 s; the test city of 3,000 in < 2 min including HLOD cells.

## 5. Third-party libraries

| Need | Prototype | Unity choice | Licence | Notes |
|---|---|---|---|---|
| Polygon boolean/offset (terraces, roof cuts, setbacks) | `polygon-clipping` (JS) | **Clipper2** C# (NuGet `Clipper2`, netstandard2.0) | Boost 1.0 | Managed API (`Paths64`), so not Burst-compatible; main-thread per building. Fine for bake-only. Wrap behind `IPolygonOps` so a NativeList port can replace it later. |
| Straight skeleton (hip/gable/shed roofs) | own brute-force event simulation (proven on 1,920 random outlines) | **Port our own** to C# first (it is ~150 lines and tested); evaluate `StraightSkeletonNet` only if ours is too slow | ours | Licence of the NuGet ports is unverified; CGAL wrappers are GPL-encumbered, ruled out. |
| Triangulation | `THREE.ShapeUtils` (earcut) | **Cutear** (earcut port, MIT, zero-alloc) or `LibTessDotNet` (SGI-B) for robustness with holes | MIT / SGI-B | Cutear for planar roof pieces; keep the interface swappable. |
| Math, containers, jobs | — | `com.unity.mathematics`, `com.unity.collections`, `com.unity.burst` | Unity Companion Licence | Declared dependencies; listed in Third Party Notices. |

## 6. Rendering, runtime performance and memory

_Filled in from the runtime research (see the second report)._

## 7. Testing and CI

- **Edit Mode tests** (authoring package): the prototype's checks ported: zero visible coplanar faces at every LOD over the 85-building fixture set (`zfight3`), topology census (no internal faces, no T-junctions), LOD consistency (window positions LOD1 = LOD2 shader), edge remaps for doors and details, placement rules (party walls, doors under fire escapes, flush cores), importer round-trip on the demo JSON.
- **Play Mode tests** (runtime package): stairs, lifts, roof access, terrace doors, doors onto a neighbour's roof, occlusion detection (buildings in the way), LOD selection on the 3,000-building city.
- **Performance tests** with `com.unity.test-framework.performance`: bake time per building, LOD CPU time per frame, draw calls and triangles at overview and street level, memory per LOD (the prototype's `city` numbers become thresholds).
- **CI:** Unity Test Framework in batch mode (`-runTests -testPlatform EditMode|PlayMode`) via GameCI on GitHub Actions or Unity Build Automation; test results as NUnit XML; a nightly device build for the mobile budget.

## 8. Workstreams and order

Each workstream ends with something usable; the prototype's demo street is the acceptance fixture throughout.

| # | Workstream | Deliverable | Exit test |
|---|---|---|---|
| 0 | Repo & packages | Unity 6.3 project, three embedded packages, asmdefs, CI running an empty test | Green CI |
| 1 | Data + importer | `.storey` ScriptedImporter, data model with versioning, prototype JSON import | Demo JSON imports; fields round-trip |
| 2 | Generator core | Slabs, wall panels, openings, cores, party walls, setbacks/terraces/overhangs, flat roofs | Coplanar test 0 on the fixture set; triangle counts within 10% of the prototype |
| 3 | Roofs, details | Straight skeleton, hip/gable/shed, roofed setbacks, facade details | Same tests extended |
| 4 | Shaders + table | Five materials, `BuildingTable` buffer, LOD2 facade shader, occlusion include | Visual parity screenshots |
| 5 | Occlusion + play kit | `OcclusionSystem` (cutaway, floors above, Sink/Slice/Cutout/Fade), play kit sample | Walk the demo street on device |
| 6 | Editor tools | Shape/Facade/Interior tools, overlays, inspectors, snapping, ghosts | Designer makes a styled 3-floor building in < 2 min; 10 floors with roof access in < 5 min |
| 7 | City scale | LOD1/LOD2 bake, HLOD cells, LOD manager, streaming districts | 3,000-building city at SPEC §6.6 numbers on the target devices |
| 8 | Hardening | Docs, samples, performance thresholds in CI, registry publishing | 0.1.0 published to the scoped registry |

Estimated effort: workstreams 2–3 and 6 dominate; the rest is glue. Porting order deliberately puts the generator before any editor UI, because the tests that make the generator trustworthy exist already and the editor is where the design will change most in Unity.

## 9. Risks and open points

- **Clipper2 on the main thread** is fine for bake-only; if runtime generation is ever wanted, a NativeList-based boolean is the first thing to write.
- **Straight skeleton robustness** is proven in the prototype on random outlines but not on outlines with collinear or near-collinear edges from snapped user input; keep the fixture set growing.
- **Party walls across districts:** a building at a district boundary depends on a neighbour in another district; the bake needs the neighbour's outline available (store outlines in a lightweight district index).
- **Mobile alpha clipping cost** (occlusion discards) is the runtime risk to measure first; see §6.
