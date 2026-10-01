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
| `com.triband.storey` (**runtime**) | Data model, generator (bake API), shaders, `BuildingTable`, `LodManager`, `OcclusionSystem`, streaming hooks, colliders. Depends on URP, Burst, Collections, Mathematics. | This is what shipped games carry. It must stay small, allocation-free at runtime, and free of editor code. Games that only consume baked districts need nothing else. |
| `com.triband.storey.authoring` (**editor**) | Inspectors, EditorTools and Overlays, bake pipeline (ScriptedImporter for `.storey` files, district/HLOD bake), prototype JSON importer, city generator, validation (coplanar test) as menu commands. Editor-only assembly; depends on the runtime package. | Level artists install it; build machines don't need it beyond the bake step. Independent cadence: tooling changes weekly, the runtime rarely. |
| `com.triband.storey.playkit` (**sample-grade runtime**) | Third-person controller, follow camera, virtual joystick, lift UI: the prototype's play mode. Also shipped as a *Sample* of the runtime package so it can be imported into a project and edited. | Projects have their own character and camera; the occlusion system exposes an API for them and the kit is only the reference implementation. |

Facts that shape this: a package's `dependencies` cannot be git URLs, only the project manifest can hold git deps, so a multi-package split needs the three packages installed together (embedded or git URLs in the project manifest during phases 1–3) and a **scoped registry** (Verdaccio/Cloudsmith, npm protocol) once they are versioned separately. Optional integrations (Splines, Entities) are compile-time `versionDefines` in the asmdef, not more packages.

Repository: this one. The Unity 6.3 test project lives in `unity/` next to `prototype/` and `docs/`, with the three packages embedded under `unity/Packages/com.triband.storey*`. Embedded packages are editable in place, the test project is where tests and the demo scene live, and the JSON golden files shared with the prototype stay in one repository. Consumers add a package by git URL with a path suffix (`?path=/unity/Packages/com.triband.storey#v0.1.0`), so the layout costs nothing on the install side. The Unity `.gitignore` (Library, Temp, Logs, UserSettings) is added at `unity/`.

## 3. Package layout and conventions

Standard UPM layout per package (`~` folders are not imported), with the split the Spline-road-mesh packages proved out: an **engine-free `Runtime/`** and an engine-facing `Unity/`.

```text
com.triband.storey/
  package.json          name, version (SemVer, start 0.1.0; MAJOR 0 until the data format is stable), unity "6000.3",
                        dependencies (exact versions), samples []
  README.md  CHANGELOG.md  LICENSE.md  Third Party Notices.md
  Runtime/Triband.Storey.asmdef       noEngineReferences: true, references []: data model, generator, LOD logic,
                                      occlusion maths. Builds as a plain .NET library (unity/Headless) and is
                                      tested there against the prototype's fixtures.
  Runtime/Data/                       BuildingData & co., PrototypeJson (read/write), Derived, DocumentSummary
  Runtime/Text/Json.cs                the JSON reader/writer (owned, ~300 lines; Unity has no engine-free one)
  Runtime/ThirdParty/<lib>/           vendored, engine-free
  Unity/Triband.Storey.Unity.asmdef   MonoBehaviours, ScriptableObjects, mesh upload, Burst jobs, buffers, shaders;
                                      depends on Runtime + Burst/Collections/Mathematics/URP
  Unity/Shaders/                      BuildingOcclusion.hlsl, Facade.hlsl, Shader Graphs
  Samples~/DemoStreet/                the prototype's demo buildings imported from JSON, plus the play kit
  Documentation~/                     manual (workflows), reference (settings), API
com.triband.storey.authoring/
  Editor/Triband.Storey.Editor.asmdef includePlatforms ["Editor"]
com.triband.storey.playkit/
  Runtime/Triband.Storey.PlayKit.asmdef
unity/Headless/                       .NET projects over the same sources: Headless (Runtime/**), Stubs (engine-facing
                                      code against hand-written UnityEngine/UnityEditor declarations), Tests (xunit)
unity/Fixtures/                       reference answers emitted by prototype/tools/export-fixtures.mjs
unity/tools/meta.py                   writes the committed .meta files (GUIDs derived from paths)
```

Every `.cs` opens with `#nullable enable`; every asset has a committed `.meta`; every source folder is compiled by the headless or the stub project. These and the rest of the layout rules (no duplicate references, editor assemblies constrained to the editor, referenced Unity packages declared, cross-package references declared, one shared version) are enforced by `PackageLayoutTests` in `unity/Headless/Triband.Storey.Tests`, so a package cannot pass its behaviour tests and still refuse to import.

Burst-compiled hot paths (polygon clipping, skeleton, mesh assembly) are a consequence of this split: the algorithm lives in `Runtime/` on plain arrays and is tested there; the `Unity/` layer wraps it in jobs over native containers. Where that duplication is not worth it the algorithm stays in `Runtime/` and runs on the main thread at bake time, which is where it runs anyway.

Rules to hold from day one:

- **Versioning:** SemVer as Unity defines it (MAJOR = breaking public API or data format, MINOR = additions, PATCH = fixes). The data format version lives inside the asset too (`BuildingData.version`) with migration code, so old scenes load after a MINOR bump.
- **Data format:** the prototype's JSON schema (SPEC §3) is the interchange format, field names included. A `.storey` file *is* that JSON; the importer stores the text on a `StoreyDocumentAsset` with a summary for the inspector and parses on demand, because Unity's serializer cannot hold the model's optional sub-objects and the model must stay engine-free. `PrototypeJson.Read` reports every key it has no field for, and a fixture test fails on any, so the model cannot silently lag the prototype. Absent and zero differ for `eave`, `pitch`, floor `h` and detail `y` (the prototype defaults absent ones), so those are nullable.
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
| Polygon boolean/offset (terraces, roof cuts, setbacks) | `polygon-clipping` (JS) | **Clipper2 1.5.4** C# sources vendored under `Runtime/ThirdParty/Clipper2` (done) | Boost 1.0 | Managed API (`PathsD`, `PolyTreeD` for polygons with holes), so not Burst-compatible; main-thread per building. Fine for bake-only. Only `Geo.TerracePolys` calls it, so a NativeList port can replace it later. |
| Straight skeleton (hip/gable/shed roofs) | own brute-force event simulation (proven on 1,920 random outlines) | **Port our own** to C# first (it is ~150 lines and tested); evaluate `StraightSkeletonNet` only if ours is too slow | ours | Licence of the NuGet ports is unverified; CGAL wrappers are GPL-encumbered, ruled out. |
| Triangulation | `THREE.ShapeUtils` (earcut) | **Earcut 3.0.1 ported to C#** under `Runtime/ThirdParty/Earcut` (done; the prototype's triangulator is earcut, so behaviour matches on degenerate input too) | ISC | `Triangulate.Shape` reproduces three.js's wrapper (duplicate end points dropped); `Triangulate.Planar` projects a 3D polygon onto its dominant plane. |
| Math, containers, jobs | — | `com.unity.mathematics`, `com.unity.collections`, `com.unity.burst` | Unity Companion Licence | Declared dependencies; listed in Third Party Notices. |

## 6. Rendering, runtime performance and memory

Targets are **iOS and desktop (Windows, macOS)** only; no Android. That fixes the graphics APIs to Metal, DirectX 12 and Vulkan, which all support compute buffers, the GPU Resident Drawer and 32-bit indices, so the plan has a single render path.

What the research settled (Unity 6.3 LTS is the current LTS; 6.4–6.6 add WebGPU with the GPU Resident Drawer and Content Directories; the Built-in pipeline is deprecated from 6.5, so URP is the right bet):

### 6.1 Draw submission

- **SRP Batcher + GPU Resident Drawer (GRD)** on every target (Metal, DX12, Vulkan). GRD needs Forward+ or Deferred+, *BatchRendererGroup Variants = Keep All*, plain `MeshRenderer`s with no `MaterialPropertyBlock`, no light probes on the renderer, and **Static Batching off** (it disables instancing and duplicates mesh data). Our design already fits: one merged mesh per building per LOD, five shared materials, all per-building data in a buffer.
- **Two device tiers, one content set.** The experience is designed for the **primary tier: iPhone 14 and newer** (A15+, 6 GB RAM, 60 Hz; Pro models 120 Hz), which is where the vast majority of players are. The **minimum tier: iPhone 7** (A10 Fusion, 2 GB RAM, 1334×750, iOS 15 is its last OS) must run the game, but never sets the design: nothing about the buildings, the LODs or the occlusion modes is shaped by it. The difference between tiers is a URP Quality Level plus the package's own `StoreyQualitySettings` asset (LOD radii, LOD0 residency cap, occlusion keyword on LOD2, GPU occlusion culling, frame-rate target), selected at startup from the device model. Metal is on both, so GRD and the compute-buffer path are the same; the minimum tier only trades distance detail and a couple of shader features for memory and fill rate. To verify in the editor before workstream 0: Unity 6.3's minimum iOS version must still admit iOS 15 (Unity 6 has been iOS 13+; the 6.x release notes for 6.4–6.7 may raise it, which would drop the iPhone 7 from later Unity versions).
- GRD stays a project setting rather than a hard dependency (a consuming project may have it off), so everything must also render correctly with the SRP Batcher alone; that is a correctness test, not a tuned path.
- **GPU occlusion culling** tests each renderer as a bounding sphere, so it works poorly for large merged meshes and tall buildings. Keep HLOD cells compact (≤ 65,535 vertices, roughly square in plan) and treat GPU culling as a per-project toggle measured at street level, not a default.
- **Per-building data**: the per-vertex building index stays the primary mechanism (portable everywhere, works inside merged cells). `MeshRenderer.SetShaderUserValue` (6.3+, read as `unity_RendererUserValue`) is an option for single-building renderers, but it isn't serialised and its behaviour under GRD instanced draws is not documented, so it is an optimisation to try, not a dependency.
- The **building table** is one `StructuredBuffer` bound with `Shader.SetGlobalBuffer`, read from a Custom Function node in Shader Graph (file-mode include). Vertex-stage buffer reads are available on all three APIs, so the prototype's float-texture fallback is not ported; a startup assert on `SystemInfo.maxComputeBufferInputsVertex > 0` documents the assumption.

### 6.2 LODs

- Our LODs are **semantic** (interior / shell / massing) with different vertex sets, and the transition is by feature scale, not screen size, so the prototype's `LodManager` (state table, cross-fade dither, storey-by-storey generation queue, LRU cache) is ported as is. Unity 6.2's **Mesh LOD** (index-range LODs in one mesh, selected by screen size) doesn't fit that; it may later be used *inside* LOD0/LOD1 for facade detail meshes, and it needs GRD for cross-fade.
- **HLOD**: no GameObject HLOD ships with Unity 6 (the HLODSystem repo targets 2021.3; Entities Graphics has one for entities). Cells are baked by the package as in SPEC §6.3.

### 6.3 Occlusion shader on mobile

- Alpha clipping (our `discard`) costs early-Z/hidden-surface removal on tile-based mobile GPUs and hurts batching on URP. Mitigation: the occlusion code is behind a **shader keyword**, compiled into the near materials (LOD0/LOD1, always the few buildings around the player) and left out of the LOD2 and HLOD-cell materials on mobile quality levels. Buildings in the way are forced to LOD0 for Sink/Slice anyway; Cutout/Fade at LOD2 become a quality option.
- **Colour-pass only**: keep Alpha Clipping on and force alpha = 1 under `SHADERPASS_SHADOWCASTER` in the include, which is the documented pattern and what the prototype's `OCC_DEPTH` define does.
- The three rays for buildings in the way run on the CPU against outline prisms (a Burst job over the grid's candidates); no physics raycasts needed.

### 6.4 Streaming

- **Districts = Addressables additive scenes**, one small group per district (Unity's guidance is small groups), loaded by distance; each district carries its buildings' assets, HLOD cells and a lightweight index of neighbour outlines for party walls across boundaries. Content Directories (6.6) are Addressables-compatible and can replace bundles later without code changes. Entities sub-scene streaming is not used (the package is GameObject-based).

### 6.5 Memory

- Meshes are built with `Mesh.AllocateWritableMeshData` + `SetVertexBufferParams` in the prototype's compact format (float3 position, SNorm8×4 normal, UNorm8×4 colour, float tag; attributes must be 4-byte multiples), **16-bit indices** (the default, and the only guaranteed format on old GPUs), `MeshUpdateFlags` to skip validation, and `UploadMeshData(true)` after bake so no CPU copy is kept. Collision is the prototype's 2D wall segments, not mesh colliders, so meshes never need to be readable.
- Budgets from the prototype's city test, to hold or beat: 3,000 buildings at ~15 MB for LOD2/cells resident, ~90 MB total at street level with LOD0/LOD1 around the player (LRU-bounded); the building table is 8,192 × 16 B. The minimum tier caps the LOD0 residency (fewer full-detail buildings) and shrinks the LOD1 radius rather than changing the content; the primary tier runs the desktop configuration.
- **Primary tier (iPhone 14+, 6 GB).** iOS lets an app use several GB here, so memory is not the constraint; the package takes the desktop budget of **≤ 200 MB** (full LOD0/LOD1 residency around the player, larger LRU, whole neighbouring districts resident) and spends it on detail distance, not on holding more content.
- **Minimum tier (iPhone 7, 2 GB).** The device grants an app roughly 1.2–1.4 GB before iOS starts terminating it; a game's own budget after Unity, textures, audio and UI is realistically 200–300 MB for the world. The package's share is **≤ 120 MB** for a city of the prototype's size: cells and LOD2 resident (~15 MB), LOD0/LOD1 LRU capped so that ~60 MB of near-detail meshes is the most it ever holds, the building table and occlusion buffers (< 1 MB), and headroom for a district being streamed in. Textures are not part of this (the package is untextured by design; a project's facade materials are its own budget).
- Unity gives no fixed MB budget; the plan is profiler-driven: the performance tests record peak mesh memory per LOD on device and fail on regression, with one threshold set per tier.

### 6.6 Performance targets

| Target | Overview (3,000 buildings) | Street level, desktop and iPhone 14+ (primary) | Street level, iPhone 7 (minimum) |
|---|---|---|---|
| Draw calls | ~110 (cells) | ≤ 90 | ≤ 60 |
| Triangles | ≤ 80k | ≤ 500k | ≤ 200k (LOD0 residency cap, LOD1 radius shrunk) |
| LOD CPU | < 2 ms/frame | < 2 ms/frame | < 3 ms/frame (A10 is ~⅓ of a desktop core) |
| Package GPU time | – | ≤ 3 ms of a 16.7 ms frame (≤ 2 ms if a project targets 120 Hz on Pro models) | ≤ 8 ms of a 33 ms frame; occlusion keyword on near materials only |
| Package memory | – | ≤ 200 MB | ≤ 120 MB (§6.5) |
| Frame rate | 60 fps | **60 fps**, all occlusion modes, full LOD radii, GPU occlusion culling allowed | 30 fps; whatever falls out of the settings above, never a reason to change content |

The primary column is the prototype's measured numbers (SPEC §6.6) taken as ceilings and applies unchanged to iPhone 14 and newer (an A15 at 2532×1170 has more fill rate per pixel than the prototype's desktop test needs). The iPhone 7 column is a starting estimate to be replaced by measurements in workstream 5 (the first device run establishes the real numbers, then the performance tests enforce them per tier). Because the iPhone 7 has little fill rate, the alpha-clip discard in the near materials and the Fade/Cutout modes at LOD2 are the two things most likely to blow its GPU budget; both are quality settings the minimum tier turns off, and the primary tier keeps.

**Rule for the minimum tier:** if the iPhone 7 cannot reach 30 fps with the quality settings alone, the answer is a further setting (smaller radii, fewer resident LOD0 buildings, dynamic resolution), never a change to the content or to the primary tier's defaults.

## 7. Testing and CI

The loop has no editor in it. `dotnet test unity/Headless/Triband.Storey.Tests` runs everything below in seconds, in any container with the .NET 8 SDK (`.claude/session-start.sh` installs it), and the GitHub workflow runs the same command with no secrets. An editor is opened for the things a headless harness cannot ask: how it looks, what a frame costs on a device, whether the GPU Resident Drawer behaves.

- **Differential tests** (headless, xunit, the bulk): the prototype emits fixtures into `unity/Fixtures/` (the demo street and the 78-building variant set as `state.buildings[]` JSON, derived values, and a **face census**: for each building the LOD0 triangles bucketed by plane and colour with the area they cover). The port is judged against them: identity and topology exactly, derived values to a tolerance, and geometry by census, which is independent of how a polygon is triangulated or which boolean library cut it, so a bucket missing, extra or off in area is a face the generator put somewhere else. Exact vertex-buffer comparison was ruled out because the prototype's polygon-clipping and Unity's Clipper2 order vertices differently. This is the prototype's checks ported: zero visible coplanar faces at every LOD, topology census (no internal faces, no T-junctions), LOD consistency (window positions LOD1 = LOD2 shader), edge remaps for doors and details, placement rules (party walls, doors under fire escapes, flush cores), importer round-trip.
- **Stub compile** (headless): the engine-facing assemblies compile against hand-written declarations of exactly the engine members they use. Catches typos, wrong overloads, missing usings and undeclared package references; proves nothing about behaviour.
- **Layout rules** (headless): §3's rules, plus source lints as they become needed (no engine object built in a MonoBehaviour field initialiser, no `??`/`?.` on a `UnityEngine.Object`), each written when the mistake it names has happened once.
- **Benchmarks** (headless): bake time per building and LOD-manager CPU time per frame over the 3,000-building city, as xunit tests that print numbers and fail on regression, like the traffic package's tick benchmark.
- **In the editor, occasionally**: Play Mode tests for stairs, lifts, roof and terrace doors, and occlusion detection on the demo street; and the device runs that set §6.6's iPhone numbers. These are run by hand and recorded in the plan, not on every push.

## 8. Workstreams and order

Each workstream ends with something usable; the prototype's demo street is the acceptance fixture throughout.

| # | Workstream | Deliverable | Exit test |
|---|---|---|---|
| 0 | Repo & packages | `unity/` with three embedded packages, engine-free/engine-facing split, headless .NET projects (behaviour, stub compile, layout rules), `.meta` generator, session hook installing the SDK, secret-free workflow. **Done**: 50 tests green. | `dotnet test` green |
| 1 | Data + importer | Engine-free data model (`Runtime/Data`), JSON reader/writer, prototype layout read/write with unknown-key reporting, derived values (floor bases, tiers, outlines, styles, shaft tops), `.storey` ScriptedImporter and import menu, fixture exporter in `prototype/tools`. **Done**: demo and 78-variant corpora round-trip structurally identical; derived values match the prototype; 411 tests. | Demo JSON imports; fields round-trip |
| 2 | Generator core | Slabs, wall panels, openings, cores, party walls, setbacks/terraces/overhangs, flat roofs: `Runtime/Generate` (MeshBuilder, Geo, Cores, Site/Party, Facade, Lod0), Clipper2 1.5.4 and an Earcut port vendored, `Validate/CoplanarCheck`. Judged against the prototype's **face census** (`census.json`: area per plane and colour, triangulation-independent). | Coplanar test 0 on the fixture set; triangle counts within 10% of the prototype |
| 3 | Roofs, details | Straight skeleton, hip/gable/shed with eaves and gable walls, roofed setbacks cut around the storeys above, facade details by hand and by rule (`Generate/Skeleton`, `Roofs`, `Details`). **Done**: the census now covers the full LOD0 mesh of every building in both corpora. | Same tests extended |
| 4 | Shaders + table | `Unity/Rendering`: `MeshUpload` (compact vertex streams, 16-bit indices, CPU copy released), `BuildingTable` (state, occluder, wall-slide and parameter-row structured buffers with the prototype's free lists), `StoreyGlobals` uniforms, `StoreyStreet` parity harness; `Unity/Shaders`: `StoreyOcclusion.hlsl`, `StoreyFacade.hlsl`, `StoreyLit.hlsl` and the Opaque, Glass and Massing URP shaders with colour-only occlusion and full shadow casting. **Written against stubs; unverified in an editor**: the first editor session compiles the shaders and takes the parity screenshots. | Visual parity screenshots |
| 5 | Occlusion + play kit | `OcclusionSystem` (cutaway, floors above, Sink/Slice/Cutout/Fade), play kit sample | Walk the demo street on an iPhone 7; first measured numbers for §6.6 |
| 6 | Editor tools | Shape/Facade/Interior tools, overlays, inspectors, snapping, ghosts; slices and the editor authoring model in docs/EDITOR.md (**6.1 and 6.2 done**: the floor, outline, setback, core, wall, door and erase operations, engine-free, matching the prototype case by case; **6.3–6.7 written**: the facade, style and building operations engine-free and matching the prototype; the Shape, Facade and Interior tools, the floors overlay and the inspector stub-compiled, editor check outstanding) | Designer makes a styled 3-floor building in < 2 min; 10 floors with roof access in < 5 min |
| 7 | City scale | LOD1/LOD2 bake (**done headless**: `Generate/Lod1`, `Generate/Lod2` with the parameter rows the facade shader reads, census-tested at every LOD), HLOD cells, LOD manager, streaming districts | 3,000-building city at SPEC §6.6 numbers on the target devices |
| 8 | Hardening | Docs, samples, performance thresholds in CI, registry publishing | 0.1.0 published to the scoped registry |

Estimated effort: workstreams 2–3 and 6 dominate; the rest is glue. Porting order deliberately puts the generator before any editor UI, because the tests that make the generator trustworthy exist already and the editor is where the design will change most in Unity.

## 9. Risks and open points

- **Clipper2 on the main thread** is fine for bake-only; if runtime generation is ever wanted, a NativeList-based boolean is the first thing to write.
- **Straight skeleton robustness** is proven in the prototype on random outlines but not on outlines with collinear or near-collinear edges from snapped user input; keep the fixture set growing.
- **Party walls across districts:** a building at a district boundary depends on a neighbour in another district; the bake needs the neighbour's outline available (store outlines in a lightweight district index).
- **Mobile alpha clipping cost** (occlusion discards) is the runtime risk to measure first; the keyword split in §6.3 is the mitigation, and the first device test should compare with/without it.
- **iPhone 7 OS ceiling (iOS 15).** Each Unity 6.x release can raise the minimum iOS version; if a later LTS drops iOS 15 the minimum tier moves to the iPhone 8 / A11 (same 2 GB memory class) or the iPhone 7 is dropped, a business call rather than a technical one. Check the release notes at each Unity upgrade.
- **Minimum-tier creep.** The one way the iPhone 7 can hurt the primary tier is if a workaround for it lands in the defaults or the content; the two quality assets and the per-tier performance thresholds in CI are the guard, and a review rule: a change to the primary tier's settings needs a primary-tier reason.
- **Renderer user values under GRD** are undocumented; the per-vertex index is the safe path, so this is only upside.
- **Party walls across districts** need a neighbour index; without it a boundary building rebuilds with an open side.
