# Runtime performance analysis

A review of the Storey packages' runtime cost for AAA production on the plan's targets: iPhone 7 (minimum tier), iPhone 14 and newer (primary tier), and desktop (Plan §6). It covers CPU (main thread, build threads), GPU, memory footprint and garbage for every runtime area, from code reading and headless measurements, against Unity 6.3, URP 17 and Apple GPU guidance.

Every Unity-engine and device-level statement is marked with its source in square brackets ([R…] rendering, [CPU…] runtime; appendix B). Measurements are headless (appendix A). Nothing here has run on a device.

## 0. Summary

**The architecture is right, and in places better than most shipped city systems.**
- Semantic LODs chosen by feature scale.
- Far geometry merged into cells and hidden per building through a GPU-resident table.
- No per-object materials or property blocks.
- Detail generated off the main thread and packed into compact formats.
- Frames that allocate nothing.
- A headless suite that measures garbage and memory.
- A "steady" shader variant so tile-based GPUs keep hidden-surface removal.

docs/CITY.md shows the memory work already done: a 5-minute walk went from 333.6 MB to 98.8 MB and from 2.8 GB of garbage to 91 MB.

**What stands between it and AAA production is mostly what the headless harness can't see:**
1. **No device or engine measurement yet.** No Unity Play Mode performance tests exist; every frame-time and memory figure is .NET 8 on x64. We checked where that proxy holds (§12.2: Unity 6's collections match .NET 8's) and where it doesn't (`Enum` parsing, iOS thread priority, Boehm, the engine's own work).
2. **Hitches, not averages.** Steady frames are cheap (LOD pick 0.05 ms; occlusion core 0.02–0.14 ms headless). The risk is in one-off frames:
   - a forced LOD0 built synchronously on the main thread (generate, pack and collision mesh: 4–9 ms on average and up to 75–135 ms for the largest towers, headless; about three times that on an A10; at spawn, teleport and district entry, §3.1);
   - the `Show` frame of every load (18–34 ms headless, §6.2);
   - first meetings in the walk model and occlusion (worst frames of 10–27 ms headless, §10.2);
   - uploads under a 6 ms budget that allows one more (§3.2);
   - collections triggered by load-time garbage (§12).
3. **Runtime JSON instead of the planned bake.** Every load parses text (60 MB of garbage per 3,000 buildings, 34 MB peak), builds every massing (71 MB), keeps text and parse forever, and parses neighbouring districts whole (§6). The plan decided on a bake-only runtime; finishing that decision is the largest single gain in CPU, garbage and memory.
4. **GPU work the steady variant doesn't yet remove** (§7):
   - every opaque triangle is two-sided (about half of them face away; measured);
   - discarding draws are interleaved with opaque ones on Apple GPUs;
   - the vertex stage writes its full set of outputs in every pass, shadows included;
   - the far city's fragment shader re-reads per-quad constants per pixel;
   - shadow coordinates are wrong with cascades;
   - interiors are drawn and cast shadows behind windows that look opaque.
5. **Threads on iOS** (§9): one build thread on an iPhone 7, not two; no lowered priority (IL2CPP ignores it on iOS); and one FIFO queue in which a forced build can wait behind a district's parse.
6. **Generation is pooled but not yet lean** (§8):
   - packing costs 40–75 % as much as generating, and a fused pass halves it with byte-identical output;
   - half of LOD0 is per-vertex bookkeeping, and a fifth is general triangulation of rectangular walls;
   - roof and party-wall work is redone for every LOD;
   - artist-made windows and details, once a project adds them, allocate 7–11 MB per building build.

### 0.1 Priorities

Effort: S under a day, M a few days, L a week or more. Impact as estimated in each section.

| # | What | Area | Gain | Effort | § |
|---|---|---|---|---|---|
| 1 | Device baseline: a Play Mode performance scene, release-player counters, p99 thresholds per tier | all | Turns every estimate here into a number; gates the rest | M | 14 |
| 2 | Never block the main thread on a forced LOD0; prefetch along the camera–player line | CPU main | Removes stalls of up to 75–135 ms headless (several hundred on an iPhone 7) at spawn, teleport, district entry | M | 3.1 |
| 3 | Bake the layout, cells, parameter rows and edge context at import; no runtime JSON | CPU, GC, memory | Load main thread to a few ms; 60 + 71 MB garbage and about 15 MB resident per 3,000 buildings removed | L | 6.4 |
| 4 | Until then: lazy parser paths, `Show` off the main thread, release documents on unload, fix the party-wall memo | CPU, GC, memory | About 25 MB less garbage per 3,000-building parse; no Show frame; −6.9 MB resident on the minimum tier | S–M | 6.1–6.6 |
| 5 | Discarding materials in the AlphaTest queue, steady copies in Geometry | GPU (Apple) | Hidden-surface removal stays effective around cut and fading buildings | S | 7.6 |
| 6 | Lean depth/shadow vertex stage; per-variant outputs; shadow coordinate and SH as URP does | GPU | Less parameter-buffer traffic in every shadow cascade; correct cascades | M | 7.2, 7.3 |
| 7 | Close the shells' openings, then cull back faces on steady draws | GPU | About 50 % fewer triangles past the vertex stage, colour and shadow passes | M | 7.1 |
| 8 | iOS build threads: QoS utility, measure 1 against 2, priority classes, storey-step builds | CPU | Detail and loads without starving the main and render threads | M | 9 |
| 9 | Pre-build walk shapes, roof heights and footprints off the main thread; keep the walk model per site | CPU main | Removes the 10–27 ms first-meeting frames (headless) | S–M | 10.2 |
| 10 | Massing colours resolved per row on the CPU; no integer divide in palette lookups | GPU | Far-city pixel shader: 12 dependent loads and 4 integer divides become 3 loads | S–M | 7.4 |
| 11 | Artist window, door and detail kinds compacted once per kind, before artist content lands | GC, CPU worker | Avoids 7–11 MB of garbage per building build | S–M | 8.1 |

The roadmap (§15) orders everything else.

Done since: #5 (§7.6), #6's per-variant vertex outputs (§7.2) and §7.3's shadow coordinate, the minimum tier's lighting without specular or reflections (§7.3), shadow proxies (§7.8), LOD0's rooms as their own renderers (§7.7), #8's priority for detail in view and its priority classes (§9.2), #2 (§3.1), #7: the shells closed, the ray test in the suite and back faces culled on steady draws by default (§7.1), #9 but the walk model's grid (§10.2, §10.3), #4's `Show` off the main thread (§6.2), the uploads: budgeted per mesh part and per tier, with the vertices finished on the build threads (§3.2, §4.1), #8's iOS threads: utility quality of service and one count for every site, with the recorder's switches to measure them (§9.1), #11 (§8.1), #1 but the device runs: Storey Performance Run with per-tier p99 thresholds, and Profiler markers in the occlusion, props and play kit (§14, §10.5), #10 another way: the massing's colours in two colour-row loads and no integer divide in any palette lookup (§7.4), #6's rest: SH where URP's mode says and `half` interpolants (§7.2, §7.3), and §7.9's warm-up and stripping, with §7.10's per-tier URP settings written up (`Documentation~/quality-tiers.md`). The shaders are compile-checked outside Unity now (`unity/tools/shaders/check.py`).

## 1. Scope and method

- **In scope**: everything that runs in a player. That is the runtime package (`Runtime/` engine-free, `Unity/`, shaders), the Color Pipeline bridge, and the play kit (sample-grade, but it is what PLAY.md §5.5's iPhone test measures). The authoring package is editor-only and is not covered, except where it decides what ships (artist meshes, §11.2).
- **Method**:
  - Code reading of every runtime file.
  - Five parallel investigations: generation, loading and threads, occlusion and play, Unity rendering research, Unity runtime research. Their claims were spot-checked against the code and the cited sources before use. One claim, that `List<T>.AddRange` allocates in Unity, was refuted against Unity's own Mono sources and dropped.
  - Headless harnesses: heap census, back-face ray test, load replays, walk and occlusion replays, profiles with `dotnet-trace` and `dotnet-gcdump`, and a Boehm GC proxy (appendix A).
- **Limits**: no Unity editor, player, device or GPU. Headless timings come from a shared 4-core x64 machine and are noisy; byte counts, object counts and the ray test are exact. Where a cost depends on Unity's engine internals, the source is cited and the item is marked for device measurement.
- **Platform notes**:
  - The A10 Fusion runs one of its two CPU clusters at a time and appears as a dual-core [CPU-4]. CITY.md's "about a third of a desktop core" is used for estimates.
  - Its GPU is Apple GPU family 3: discard is weaker than on A11+, FP16 runs at twice the FP32 rate, and fragment inputs are capped at 60 components [R6].

### 1.1 Where the existing docs and this review differ

| Doc's statement | What we found | § |
|---|---|---|
| Plan §1, §6.5: a bake-only runtime | Layouts are parsed and the far city built at every load | 6.4 |
| CITY.md §2: a streamed load "never holds a frame" | The parse doesn't; `Show`'s synchronous work does (18–34 ms headless) | 6.2 |
| CITY.md §2: build threads "below normal priority where the platform allows it" | iOS doesn't allow it: IL2CPP ignores thread priority there | 9.1 |
| CITY.md §7: the minimum tier with two build threads | The automatic count gives an iPhone 7 one | 9.1 |
| CITY.md §7: the walk's worst frame is 1.0 ms | 10.5–27.4 ms in the five-minute-walk bench on the test machine | 10.2 |
| PROPS.md §3: still props cost nothing per frame | Every prop is visited every frame | 11.1 |
| CITY.md §7: `Lod0.BuildShape` is "about 50 times faster" than a full build | 5 to 9 times (CITY.md's own table: 0.5 ms against 4.0 ms) | 8 |
| `Lender`, `PackageLayoutTests`: `[ThreadStatic]` crashes a Unity script | Only for Unity objects held in thread statics; engine-free state is safe, and the build threads could own their state without either | 9.3 |

## 2. What is already right (keep it)

| Area | What | Evidence |
|---|---|---|
| Frame | The LOD pick skips settled 128 m squares: about 0.05 ms a frame for the city | CITY.md §4, `LodManagerTests` |
| Frame | Zero garbage per frame for the walk model, the occlusion core and the LOD pick; Unity 6's collections behave the same (§12.2) | `FrameAllocTests`, appendix A.1 |
| Rendering | One merged mesh per building per LOD; far buildings merged into cells; per-building state in global structured buffers; no material instances or property blocks, so SRP Batcher and GRD compatible | SPEC §6.4 |
| Rendering | Dirty-chunk table uploads (1 KB a frame instead of 344 KB); wall tables that grow | CITY.md §7 |
| GPU | The steady variant (no discard) for everything nothing can hide; far occlusion off on the minimum tier | CITY.md §2 |
| Memory | Compact vertex formats; 16-bit indices with meshes split at 65,535 vertices; half-float massing attributes; no CPU copies after upload | Plan §6.5, CITY.md §7 |
| CPU | Generation and packing off the main thread; builders and packed meshes of pooled chunks, capped per tier; only a 10–25 KB shape kept per LOD0 | CITY.md §7, `MemoryBenchTests` |
| Physics | Collision meshes a few hundred triangles; cooked in jobs with matching options; capped | `SiteRenderer.cs:1007-1076` |
| Loading | Parse off the main thread; loads over frames within one shared budget; cells taken nearest first; massings reused | CITY.md §2, §7 |
| Process | Quality tiers as assets with a written rule ("settings, never content"); a headless suite that guards memory and garbage | Plan §6.6 |

## 3. The frame on the main thread

Each Storey Site does this every frame, from `StoreySite.LateUpdate` (execution order 1000):

1. `PickFor`: the quality asset applied to the live settings (no allocation), the forced set refilled (`keepDetail`, the editor's buildings, the occlusion system's active building and occluders).
2. `SiteRenderer.Frame`:
   - a finished collider batch assigned;
   - pending colliders queued;
   - `UpdateLods`: forced LOD0s built, a load over frames advanced, finished worker builds uploaded within the budget, `LodManager.Update`, drops and new jobs, state rows written, dirty cells merged, every cell's visibility checked;
   - glass and tint globals;
   - the occlusion system's `Apply` (Play mode) or the editor's cutaway;
   - `palette.Bind`, `table.Upload`.
3. Before the cameras render (`RenderPipelineManager.beginContextRendering`): `UpdateSteady` for every live site.

Steady state is cheap. Picking the LODs takes about 0.05 ms headless (CITY.md §4), the occlusion core and walk model allocate nothing (`FrameAllocTests`, appendix A.1), and the remaining loops cost a few microseconds per thousand buildings. The frame-time risk is in the spikes: §3.1, §3.2 and §4.

### 3.1 Forced LOD0s are built synchronously on the main thread (high: hitches)

`SiteRenderer.UpdateLods` (`SiteRenderer.cs:813-824`) gives every forced building that lacks LOD0 its LOD0 before the frame goes on:

```csharp
if (!TakeJobNow(bt, 0)) BuildDetail(bt, b, 0);
```

`TakeJobNow` calls `j.task.Wait()` on a worker's build of it, which may have just started. Otherwise `BuildDetail` generates it right there. Neither is budgeted. In Play mode the forced buildings are the player's building and every building in the camera's way (`StoreySite.PickFor`, `StoreySite.cs:301-306`).

A LOD0's generation, packing and collision mesh take 3.5–4.5 ms on average for a plain building and 8.8 ms for a walk-in one. The largest walk-in towers take 75–135 ms (headless, appendix A.7; CITY.md §4's older table gives 23–34 ms on average and 100–150 ms). The plan reckons an A10 core at about a third of a desktop core, so expect about three times that on an iPhone 7. For a tall building that is a multi-frame stall whenever an occluder or the player's building lacks LOD0.

In continuous play that is rare: a simulated 467-second run had none of 69 occluders arrive without LOD0 (appendix A.6). It is certain after a spawn, a teleport, a district load, a LOD0 dropped by the residency cap, or a build queue that has fallen behind. Recommendation:
- **Occluders: never block.** Keep forced buildings at the head of the worker queue (`LodManager` already puts them first) and show what exists meanwhile. Cutout and Fade work on any LOD, so use them for an occluder until its LOD0 arrives, then switch to Sink or Slice.
- **The player's building at a spawn or teleport:** build it behind the transition (loading screen or fade) before the view is revealed.
- **Prefetch:** ask for LOD0 for buildings near the camera-to-player segment, and within a few metres of the player, before they are forced.

Effort: M.

Status: done (CHANGELOG). In Play mode a forced building shows the best LOD it has until its LOD0 is up. Its build goes to the head of the build threads' queue (a lane of its own, §9.2), a build of it already waiting is moved up, and the finished LOD0 is uploaded whatever the budget. The occluder modes need no LOD0: Cutout, Fade, Sink and Slice all draw from the building table's rows on whatever LOD is shown (Sink's squash is approximate on a massing's quads of several storeys, for the frames until LOD0 is up), and the cutaway skips walls of a building without one. Prefetch: the buildings within 5 to 8 m of the camera's line to the player, and of a point a spawn or teleport asks for (`StoreySite.PrefetchAround`), get their LOD0 built after the forced ones and those in view, and keep it past the residency cap while asked for; `StoreySite.DetailReadyAround` is what a transition waits for. The editor, and a site with no build threads, still build a forced LOD0 at once.

### 3.2 The upload budget

Detail is uploaded on the main thread "for up to 6 ms a frame, at least one when any is ready" (`buildBudgetMs`, shared by all sites, `TakeJobs`, `SiteRenderer.cs:730-748`). The check runs after each building, and one building's `FinishDetail` uploads all its parts at once: LOD0's opaque parts, its glass parts, and its wall rows in the table. Unity's own share of the upload (the copy into the mesh, the GPU buffer creation) isn't measured yet (CITY.md §8).

- Six milliseconds is 36 % of a 60 fps frame. AAA streaming budgets on the main thread are typically 1 to 2 ms at 60 fps; make the budget per tier, and count it per mesh part. Carry the remaining parts to the next frame and show the building when all have arrived.
- `TakeLoad` (a load over frames) and `MergeCells` share the same clock; `Cells.Pack` runs on the main thread in `BuildCell` (`SiteRenderer.cs:901`). Moving the packing to the build threads (the row map is known once the rows are written) leaves only the upload.

Status: done (CHANGELOG). The budget is counted per mesh part. A worker's build is staged as it comes in (`SiteRenderer.Stage`): its walls are written into the table and its parts' colour rows found on the main thread, its vertices are finished on a build thread (§4.1), and then its parts are uploaded one at a time within the budget, the rest in later frames (`TakeStaged`). The LOD is marked built, and shown, when the last part is up. A forced building's parts go up at once, then those of the buildings in view, then the prefetched; the builds being made and those waiting for their upload together are at most twice the build threads (no new one starts meanwhile), and a build whose building is let go or no longer wanted before its first part gives its walls and chunks back. **Build Budget Ms** is per tier: 2 ms on the primary tier, 4 ms on the minimum tier, whose frames are 30 fps (was 6 ms on both). The load replay of the 3,000-building test city (appendix A.5's harness, two build threads at 60 fps) then takes 39 frames instead of 20, with every cell shown after 0.70–0.72 s instead of 0.39–0.51 s; no load frame goes past 3.3 ms, where the 90th percentile was 6.2–6.5 ms. `Cells.Pack` runs on the build threads in Play mode; a cell keeps its meshes until its new ones are uploaded, within the budget. What's left is the device: Unity's own share of an upload, and whether 2 ms holds the frame on the iPhone (the recorder's `build: LOD0 upload` timings).

### 3.3 Per-frame loops that could be event-driven (low to medium)

- `UpdateSteady` (`SiteRenderer.cs:115-149`) runs for every live site before every render context. It visits every building's three renderer lists and calls `Steady.Of` for every member of every cell, about 3,000 calls on the test city, whether or not anything changed. The inputs change only when a building's state row changes (LodManager's `Changed`), its occluder slot changes, or the active or isolated building changes. Keep a dirty set from those and re-evaluate only those buildings and their cells. Guard it with the frame count as well, so a frame with several render calls evaluates once.
- The cell visibility pass (`SiteRenderer.cs:859-866`) walks every cell's members every frame looking for one at LOD2. The same `Changed` list gives the cells whose answer can change (each building knows its cell, `bt.cell`).

Both are O(buildings) in the tens of microseconds headless, more on an A10, and they grow with the loaded districts. In a player `beginContextRendering` fires once per `RenderPipeline.Render` call, which is once a frame plus each manual `Camera.Render` or render request; in the editor, once per visible Scene and Game view [R3].

### 3.4 Scene objects

| Object | Count on the 3,000-building test city | Made | Note |
|---|---|---|---|
| Building root `GameObject` | 3,007, all at load | `Build` (`SiteRenderer.cs:569`) | Only the cells draw massings. A root is needed only for a detail LOD or a collider |
| LOD part objects (`MeshFilter` + `MeshRenderer`) | 1 to 4 per resident detail LOD | `Add`, per build | Destroyed on drop: `new GameObject` + 2 × `AddComponent` + `SetParent` per mesh |
| Cell objects | 307 | `BuildCell` | Rebuilt only on edits |
| `Mesh` | one per part | `MeshUpload` | Destroyed on drop |

Recommendations:
- **Lazy building roots.** Create a building's root on its first detail LOD or collider. That is about 2,700 fewer GameObjects for the test city at street level. Each costs native memory (GameObject plus Transform) and a managed wrapper, and creating them is part of each load's main-thread time (3,314 GameObjects created on load and destroyed in one frame at unload, cells included). The same goes for the renderer's per-building record: `Built` is about 912 bytes a building as declared (2.6 MB for the city), most of it per-LOD lists that only buildings with detail use. Keep those in a detail object created with the first detail LOD.
- **Pool the LOD part objects.** Keep disabled `MeshFilter`/`MeshRenderer` pairs and swap `sharedMesh`, instead of creating and destroying a GameObject and two components per build. That removes the native object churn, the managed-wrapper garbage and the transform-hierarchy insertions. The GPU Resident Drawer still re-registers a renderer whose mesh changes [R1]; that cost stays.
- **One transform hierarchy for the whole site.** Every building root and cell is a child of the site; set `transform.hierarchyCapacity` before a load, or parent per cell, so the hierarchy isn't reallocated as it grows [CPU-6].
- **Names.** Every upload builds strings (`name + " LOD1"`, `Part(...)`, `$"Cell {x},{z}"`) and passes them to native `GameObject` and `Mesh` names. Skip them outside the editor and development builds.
- **Steady material swaps.** A building starting or ending a cross-fade, entering or leaving the camera's way, or becoming the active building swaps `sharedMaterial` on its renderers. Under the GPU Resident Drawer a material swap re-registers the renderer in full, on the main thread at PostLateUpdate: its instances are freed and recreated, its data uploaded again and the batches rebuilt [R1]. Every LOD transition costs two swaps per renderer (fade start and end). Count them during a fast walk and profile `GPUResidentDrawer` there; at a few per second the cost is small.
- `StoreySite` has an `OnGUI` (the stats overlay). IMGUI calls it for every GUI event every frame even when the overlay is off [CPU-9]. Move the overlay to a development-only component.

### 3.5 Colliders on the main thread

The design is right: collision meshes made on the workers, cooked in an `IJobParallelFor` with `Physics.BakeMesh`, and assigned with the same cooking options so nothing is cooked twice (`SiteRenderer.cs:1007-1076`). Remaining costs:
- `ToMesh` (`SiteRenderer.cs:1085-1116`) allocates a `Vector3[]` and a `ushort[]` per collider, plus an `int[]` and a persistent `NativeArray` per batch. Write through `Mesh.MeshData` or reused `NativeArray`s instead.
- Cooking is `UseFastMidphase` only: no `CookForFasterSimulation`, mesh cleaning or welding. That is the fastest cook. Whether characters see internal-edge bumps on the welded meshes without the faster-simulation data is a feel question for the play kit [CPU-5].
- `AddComponent<MeshCollider>` and `sharedMesh` assignment insert a static shape into PhysX's scene-query structure, deferred and batched [CPU-5]. At the walk's pace (37 colliders kept after five minutes) that is negligible.

## 4. Mesh upload and geometry memory

### 4.1 The upload path (medium)

A detail build reaches the GPU like this (`MeshUpload.Upload`, `MeshUpload.cs:66-91`):
1. Finish on the main thread: `PackedMesh.Finish` rewrites every vertex's colour row (the worker can't know it) and LOD0's wall ids. CITY.md measured 0.13 ms a building.
2. `SetVertexBufferParams`, then one `SetVertexBufferData` per 2,048-vertex chunk per stream, then the index chunks, all `MeshUpdateFlags`-quiet.
3. `UploadMeshData(true)`: the GPU copy is made and the CPU copy released.

The managed chunks are copied into the mesh's native data on the main thread, and that is copied again to the GPU. `Mesh.AllocateWritableMeshData` / `ApplyAndDisposeWritableMeshData` [R4] lets the build threads write the vertex and index data straight into native mesh data, leaving the main thread only the apply. CITY.md §8 already names this as the next step if the upload hitches. It also lets the colour rows be resolved before packing (the style-to-row mapping can be handed to the job), so step 1 leaves the main thread too.

Status: step 1 leaves the main thread (CHANGELOG). The colour rows (a lookup per style) and the wall base (an allocation in the building table) are still found on the main thread, which owns the table. `PackedMesh.Finish` then writes them into every vertex on a build thread, in the build's own lane, and `MeshUpload.Upload` finishes only a mesh not yet finished. `MeshPackTests.AMeshFinishedOnABuildThreadIsTheSameBytes` checks the bytes against a finish on the calling thread. Steps 2 and 3 stay on the main thread, a part at a time within the budget (§3.2), until `AllocateWritableMeshData` (§15, phase 4).

### 4.2 Vertex formats

| Mesh | Layout | Bytes |
|---|---|---|
| LOD1, LOD2 alone, footprints | float3 position, SNorm8×4 normal, UNorm8×4 colour reference, float tag | 24 |
| LOD0 | the above + stream 1: float kind, float wall id | 32 |
| Cells (half) | float3 position, SNorm8×4 normal, float tag, half×4 + half×2 facade numbers, float row | 36 (48 without half support) |

This is already lean (Plan §6.5). If the minimum tier's memory binds, two further steps are available. Both need care.
- Positions as UNorm16×4, quantised to each mesh's bounds, with the dequantise folded into the renderer's transform (scale and offset). The GPU Resident Drawer handles per-instance matrices, and the fourth component can hold the tag. 24 → 16 bytes for LOD1, 32 → 24 for LOD0. At 50 m bounds the step is under a millimetre. The risk is hairline cracks where two buildings' meshes meet on different grids: test it on party walls.
- LOD0's kind and wall id as UInt16×2 instead of two floats (−4 bytes). The kind field packs the party-wall neighbour's index (`kind + 8 × (neighbour + 1)`), which needs 17 bits for 8,192 buildings, so it needs one more bit budgeted elsewhere.

### 4.3 Meshes and objects are not reused

Every detail build creates `Mesh` objects and every drop destroys them. Mesh creation is cheap next to the copy, but pooling the part objects (§3.4) and keeping their meshes (`SetVertexBufferParams` reallocates as needed) removes managed-wrapper churn and keeps the GPU Resident Drawer's mesh registrations stable.

## 5. The building table and shader globals

`BuildingTable` (`BuildingTable.cs`) is well built:
- one shared table for all sites;
- structured buffers bound globally;
- no per-object materials or property blocks;
- dirty-chunk uploads, measured at 1.0 KB a frame on the walk (was 344 KB);
- wall and parameter tables that grow on demand.

The remaining costs:
- **Per-frame calls.** `Upload` makes one `SetData` call per dirty run in each of five buffers, plus seven `SetGlobalBuffer`s, per site per frame; with several sites the shared table is bound once per site. Bind once per frame (a static frame stamp), and only re-bind a buffer when it is replaced. A managed array binds to `SetData(System.Array, …)`, which checks blittability and looks up the element size on every call; Unity 6.3 has no generic array overload, but the `NativeArray<T>` one skips both. Keeping the CPU mirrors in `NativeArray`s also takes 1.8 MB out of the managed heap the collector scans [R5].
- **Frequently written rows.** The state, occluder and wall-slide rows change every frame in small runs. `GraphicsBuffer.UsageFlags.LockBufferForWrite` "always results in fewer memory copies than SetData" [R5], but the buffer becomes read-only to the GPU, D3D11 falls back to a slow path with uploads in flight, and one DX12 report shows slower GPU reads from upload-heap memory. The GPU Resident Drawer itself uses small `SetData` uploads scattered by a compute shader. Keep `SetData` until a device profile says otherwise; the dirty-chunk scheme already keeps the volume at about 1 KB a frame.
- **Garbage at load.** `WriteRow` calls `ParamRow.GpuTexels`, which allocates a 24-element `double[]` per row (`Lod2.cs:35`): about 9,000 rows on the test city, about 2 MB of garbage on the main thread per load. Write straight into `Params`.
- **Memory.** About 2 MB on the GPU and 1.8 MB of CPU mirrors for the test city (CITY.md §7). The parameter table doubles (9,000 rows → 16,384 capacity, 1.5 MB each side); grow by 1.5× or size it from the layout's row count at load.

## 6. Loading, parsing and districts

The plan's decision was a bake-only runtime (Plan §1, §6.5): meshes and colliders as assets, the runtime doing LOD, streaming and occlusion. The code does more at load. A layout is JSON text on a `StoreyDocumentAsset`; in Play mode a site parses it, and its neighbours' layouts, on the build threads. It then builds every massing, merges and uploads the cells, and writes every building's parameter rows. The measurements below are headless (x64, .NET 8; appendix A.2 and A.5) on the 3,000-building test city. A district of the intended 50 to 300 buildings costs about a tenth of each, except where neighbours multiply it.

| Stage | Where it runs | Cost (3,000 buildings) |
|---|---|---|
| Parse (`PrototypeJson.Read`) | build thread | 94–139 ms, **60.3 MB allocated**, about 34 MB live at its peak (text 3.9 + DOM 21.4 + document 8.9) |
| `Show`: the site index, colour rows, load plan | **main thread, one frame** | 17.7–18.1 ms (`Site` 10 ms, `LayoutLoad` 7.2 ms); the Show frame measured 18–34 ms in every replay |
| Massings (`Lod2.Build`) | build threads | 422 ms on one thread, 71 MB allocated (18.7 MB of it the party-wall queries) |
| Cells: pack, upload, GameObjects | main thread, over frames | 83–208 ms over 15–65 frames, 16 MB allocated on the main thread |

### 6.1 The parser makes 30 bytes of garbage per character (high)

`Json.Parse` builds a `Dictionary<string, object?>` per JSON object, a `Substring` per key, string and number, and boxes every number and bool (`Json.cs:85-86, 100-134, 175, 230-231`): 25.6 MB of DOM. `PrototypeJson` then makes another 34.7 MB converting it:
- Path strings for error messages, built eagerly per element: `$"buildings[{i}]"` twice per building, three per point (`PrototypeJson.cs:48-49, 522-533`). That is 14.7 MB of strings and 2.3 MB of boxed indices.
- `params string[]` key lists per object (`Check`, `PrototypeJson.cs:544-547`): 3.9 MB.
- Default objects the reader then replaces: half of the 2.9 MB of `FacadeStyle`.
- On device only, `Enum.TryParse` allocates per enum field (§12.2).

Parsing on a build thread keeps the work off the main thread, but not its garbage. Boehm stops every thread to collect (§12.1); in 4 of 12 load replays a .NET collection landed in a main-thread frame (82–106 ms frames). The parse peak also sets the managed heap's size for the rest of the session.

Recommendation, cheapest first:
1. **Lazy error paths and no throwaway objects (S).** Pass the parent and index down, and format a path only when reporting. Use static known-key sets. Parse enums through a static dictionary. Remove 23–25 MB of the conversion's garbage and about half its time.
2. **A pull reader (M)** that fills the model straight from the text, with no DOM, numbers parsed from spans and keys matched without substrings. That removes the DOM's 25.6 MB of garbage and 21 MB of peak.
3. **No parse at runtime (L)**, §6.4.

Status: 1 is done (CHANGELOG). A path is a small struct passed down (`PrototypeJson.Where`), made into text only for an unknown key or an error, the same text as before. Each kind of object's keys are one static array. An enum is read through a static dictionary of its names (`Enum.TryParse` stays only for a number). The model's own lists and a building's default style are filled where they are, not replaced. Reading the 3,000-building test city makes 34.5 MB, where it made 57.8: 25.6 MB of it is the JSON tree (option 2's) and 8.8 MB the model itself, where the conversion made 32.1 MB. It takes 133–163 ms, where it took 210–237 (headless). `PrototypeJsonTests` pins the paths' text and a garbage bound (2 MB besides the tree for 300 buildings).

### 6.2 `Show` holds a frame (medium)

CITY.md §2 says a streamed load "never holds a frame". The parse is off the main thread, but `Show` then does three things synchronously on the main thread:
- builds the site index (`new Site`, or `Site.WithContext` with neighbours);
- builds the colour-row book;
- plans the load, with `LayoutLoad`'s LINQ grouping by cell.

That is 18 ms for 3,000 buildings headless; estimated 40–70 ms on an A10 (4–7 ms for a 300-building district). `Derived.TierStart` walks down the floors on every call (`Derived.cs:44-49`), so per-floor loops are quadratic in storeys: 38 % of the `Site` constructor's samples. `Site.BoundsOf` is computed three times per building (`Site`, `LayoutLoad`, `StoreySite.PlanArea`).

Recommendation (S–M):
- Build the `Site` and the cell plan on the build thread straight after the parse; both are engine-free.
- Cache each building's bounds and tier starts once.
- With neighbours, see §6.3.

Status: done (CHANGELOG). In Play mode, loading over frames, the site index and the load plan are made on a build thread after the parse (`SiteRenderer.Prepare`, `LayoutLoad.Plan`): 6.5–7 ms and 2.3–3 ms for 3,000 buildings headless, off the main thread. `Site.BoundsOf` reads each distinct outline once (2.3 ms for 3,000 buildings, from 7.5), and the site keeps the bounds (`Site.Bounds`). The colour-row book stays on the main thread (it writes the building table). Tier starts are left as they are: with the bounds no longer quadratic, the per-storey walks that remain are within single storeys' work.

### 6.3 Neighbouring districts are parsed whole for a few edge buildings (high in a streamed city)

For party walls along district edges, a site parses every neighbour's layout (`StoreySite.cs:227-232, 246-247`) and copies the buildings within 2 m of its own. Each copy is a JSON round trip on the main thread: `PrototypeJson.ReadBuilding(PrototypeJson.Write(nb))` (`Site.cs:103`), costing 0.3–0.8 ms and 29–83 KB per building. `WithContext` for a 751-building district with 2,256 around it took 13–17.6 ms of the Show frame, against 2.8–4.7 ms without neighbours.

With four 300-building neighbours, a first visit is five parses: about 41 MB of garbage and 80 ms of build-thread time (x64) instead of 8 MB and 16 ms, and all five documents stay cached (§6.5).

Recommendation (M): bake a per-district edge index at import, as Plan §9 already calls for. It holds the neighbour buildings within reach, moved by their offsets, with their owner keys. A site then never parses a neighbour's layout, and the copy becomes a struct copy.

Status: done another way (CHANGELOG). A layout's offsets from its neighbours belong to the scene, not to the layout, so the importer can't know which buildings are along an edge. Instead each layout is baked with a directory of its buildings' bounds (§6.4), and a site reads a baked neighbour's buildings within reach alone, each a new object, so nothing is copied (`Site.WithContext`'s baked overload, `SiteRenderer.BakedNeighbours`). The neighbour isn't parsed, and the site doesn't hold its parse. For the 751-building district with 2,256 around it (headless): 1.8–7.9 ms and 0.6 MB allocated for the context, where parsing the three neighbours and taking the context took 60 ms or more and allocated 26.1 MB, and kept 6.4 MB of their documents. `DistrictsTests.ABakedNeighbourGivesTheSameEdge` checks the same site and the same massings either way.

### 6.4 The far city is generated at every load (high; the plan's own decision)

Every load builds all 3,000 massings (422 ms of build-thread time, 71 MB of garbage), merges and uploads 307 cells, and writes about 9,000 parameter rows. CITY.md §8 lists "cells baked at import" as not done. Baking at import:
- the cells as mesh sub-assets (9.8 MB for the city), uploaded through Unity's asynchronous upload pipeline;
- each building's parameter rows and bounds as a blob copied into the table at the site's block offset;
- the party-wall context resolved;
- the layout itself as a binary blob read with `TextAsset.GetData<byte>()` into native memory (about 0.8–0.9 MB for the test city's model, against 3.9 MB of text plus 8.9 MB parsed).

That turns a load into a few milliseconds of main-thread copies with almost no garbage (estimated; appendix A.5). JSON stays the authoring and interchange format, so the importer and the headless tests keep their single source of truth.

A baked, persistent far-cell layer for the whole city (about 3.3 KB of GPU per building) would also keep unloaded districts visible past the load radius. Today nothing is drawn there.

Status: the layout is baked (CHANGELOG); the cells, rows and bounds are not. The importer bakes the parsed layout into a compact binary on the asset (`BakedLayout`, `StoreyDocumentAsset.Baked`): every field of the model, each distinct string and style once, and a directory of the buildings with their bounds. `Document` and the build threads' read (`LayoutParse`) read it in place of the text. In a player the text goes as the asset loads (`Json` is empty there). Headless:

| | Text | Baked |
|---|---|---|
| 3,007 buildings: size | 1.94 M characters (3.87 MB as a string) | 0.75 MB |
| read | 56–65 ms, 34.5 MB allocated | 3.9–4.5 ms, 7.8 MB allocated (the model) |
| the model it gives | 8.84 MB | 7.74 MB |
| 1,007 walk-in buildings: read | 32–34 ms, 16.2 MB | 3.2–3.4 ms, 3.5 MB |
| 307 buildings: read | 6.1–6.6 ms, 4.6 MB | 0.5–0.6 ms, 1.2 MB |

The format follows the model field for field, versioned with the importer's version (a new format imports every layout again). `BakedLayoutTests` reads back every fixture and both test cities field for field, fills every field of the model at random and reads it back, and pins the model's shape: a field added fails it until the format and its version are updated. The cells are not baked: a cell's massings depend on its neighbouring districts, which belong to the scene, and its vertices carry the building table's rows and indices, which are only known as the site loads. The load already stays within the frame's budget (§3.2); what baking them would save is the build threads' 0.4 s and the massings' garbage on them, for 9.8 MB of meshes on disk and the importer generating every massing. Worth it once a device run shows the load's garbage collections in its frames.

### 6.5 Nothing is released when a district unloads (high for long sessions)

`StoreyDocumentAsset` caches its parsed document for the asset's lifetime (`StoreyDocumentAsset.cs:39-50, 61-70`). `StoreyDistricts` unloads scenes with `UnloadSceneAsync` (`StoreyDistricts.cs:68-72`). Unity doesn't unload a scene's assets with it [CPU-7], and nothing in the package calls `Resources.UnloadUnusedAssets`. So every district visited keeps its text and parsed document, and every neighbour it parsed keeps its own: 1.27 MB per 300-building district, 12.7 MB after ten (measured).

`Resources.UnloadUnusedAssets` is the wrong fix mid-game: it is CPU-heavy and meant for loading screens [CPU-7]. Instead:
- Reference-count a document's users (sites and neighbour references).
- On the last release, drop `cached` and the parse; keep an LRU of one or two for quick re-entry.
- In players, discard `json` after parsing, and use a version number as the change token instead of the text's reference (`StoreySite.cs:240, 260`).

Streaming districts through Addressables (Plan §6.4) then releases the rest with the bundle. Effort: S for the reference count, L for Addressables.

Status: done (CHANGELOG). Districts can come through Addressables (**Source** on Storey Districts, `AddressableDistrictScenes` in an optional assembly that compiles once the package is in the project): a district unloaded releases its scene's bundles by reference count, its layout's text, bytes and parse with them. That needs a device run with the package to measure. The reference count: In Play mode a site uses its layout and its neighbours' while it is enabled (`StoreyDocumentAsset.Use`, `Release`); a layout no site uses keeps its parse while it is among the last two let go (`StoreyDocumentAsset.KeepUnused`), and loses it after. A layout asked for again is parsed again. The engine-free count is `Lod.Users`. Replaying a walk through districts of 300 buildings in a row, each site holding its own layout and its two neighbours' (headless): after 10 districts the layouts hold 7.2 MB, where they held 12.7; after 40, 18.8 MB, where they held 50.7. What stays is each visited district's text, 0.39 MB of a district's 1.27, and four parses. The text isn't discarded in players: a district's scene loaded again finds the same asset object, its text gone, and a player can't read it back from disk while the object stays loaded. It goes with the asset when the district comes from an Addressables bundle, or with the baked layout of §6.4. The change token stays the text's reference for the same reason.

### 6.6 The party-wall memo keeps 3 MB of nothing (medium, effort S)

`Party.Ranges` memoises per building and edge in a `ConcurrentDictionary` per building (`Site.cs:21, 207-258`). After a test-city load the memo holds 3,007 inner dictionaries and 14,928 edge entries containing **4 ranges in total**. That is 2.99 MB of the site index's 3.88 MB, about 1 KB per building. Each miss also allocates a dictionary, a list, a hash set and outline lists: 18.7 MB of the load's 71 MB of massing garbage. Each build clears its own building's entry when it starts (`Lod0.cs:70`, `Lod1.cs:34`, `Lod2.cs:108`) and refills it as it goes, so the memo stays full between builds.

Recommendation:
- Add a broad-phase early-out: no other building near the edge gives a shared empty list, with nothing stored. That removes nearly all of the 3 MB and most of the 18.7 MB of garbage.
- Then keep the memo for the site's lifetime instead of clearing a building's entry at each build (§8.5). The ranges change only with the layout, and a new `Site` is made for every load and edit anyway.
- Key it by (building index, tier, edge) in one map, rather than a `ConcurrentDictionary` per building, created with a low concurrency level.

Status: done (CHANGELOG). One map per site, keyed by building index, tier and edge, with a low concurrency level, kept for the site's life (no build clears it: a site is made anew for every load and edit). An edge no neighbour shares gets a shared empty list. Nothing is kept for it when no building is near it at all; otherwise only its entry in the map. The scan allocates nothing until a neighbour's edge runs along this one: the near buildings come into a lent list, and a neighbour's tiers are walked without `Tiers`' lists. On the 3,000-building test city, building every massing allocates 58.5 MB, where it allocated 75.0, and takes 105–160 ms, where it took 168–209. The site after it holds 1.8 MB, where it held 4.0 MB (headless).

### 6.7 The parsed model is about three times its information (medium)

The 8.9 MB document is about 178,000 objects:
- 95,530 of its 105,652 `List<T>`s are empty;
- 19,600 of its 19,630 floors are `{"walls":[]}`, each a `FloorData` with four lists;
- 3,008 `FacadeStyle`s at 456 bytes, of which 137 are distinct;
- 21,049 colour strings with 36 distinct values.

Every Boehm collection walks all of it (§12.1). Null or shared empty lists, floors as a count plus sparse overrides, and interning colours and styles while reading bring it to an estimated 2.5–3 MB with about 150,000 fewer objects. The baked blob of §6.4 makes the runtime copy unnecessary. Effort M, because the editor's operations mutate these lists.

Status: the strings are interned (CHANGELOG): the baked layout's reader makes each distinct string once, so the 21,049 colour strings are 36, and the model is 7.74 MB where it was 8.84. The rest is not done. The model is public and changeable: the editor's operations change its lists and styles in place, and so can a project's code, so lists or styles shared between buildings would change together, and floors as sparse overrides change the model's API for every reader. That wants a read-only runtime model beside the editable one, larger than this item.

### 6.8 Main-thread garbage per load (medium, effort S)

16 MB per 3,000-building load on the main thread, about 10 MB of it avoidable:

| Garbage | Size | Cause |
|---|---|---|
| Palette strings | 4.1 MB | `HexPaletteIndex.IndexOf` normalises the colour reference before looking it up |
| Parameter-table growth | 3.1 MB | `Params` doubles four times (1,024 → 16,384 rows for 9,022), with a new `GraphicsBuffer` each time |
| `GpuTexels` | 2.0 MB | a `double[24]` per row (`Lod2.cs:35`) |

With Color Pipeline, `ColorPipelinePalette.IndexOf` parses the palette id before checking its memo (`ColorPipelinePalette.cs:74`): 112 bytes and about 0.2 µs per call, about 7 MB per load and 14 ms per full colour-row rewrite, though every lookup hits the memo.

Fixes:
- Check the memo first.
- Write texels straight into the table.
- Size `Params` and `Colors` from the layout at load.
- Upload colour rows by dirty range instead of whole.

Status: done (CHANGELOG). The built-in palette remembers each reference as the layout writes it, and Color Pipeline's remembers each palette id with the id it parsed, which checks the entry is still there: an id is parsed only on a miss, and in place (`ColorRef.Parts`, no substrings). A parameter row's texels go into the table from the stack (`ParamRow.GpuTexels`' span overload). A layout sizes both tables once as it loads (`BuildingTable.Reserve`: three parameter rows and one colour row a building, the test cities' ratio, and a margin; a layout with more grows them as before). Colour rows go up by the chunks that changed, three of the buffer's elements a row (`BuildingTable.ColorWords`). Headless, for the 3,000-building test city:

| | Before | After |
|---|---|---|
| Colour rows, built-in palette | 16.8 ms, 5.6 MB | 7.1 ms, 1.0 MB |
| Colour rows, palette ids (Color Pipeline's lookup, emulated) | 63.2 ms, 10.2 MB | 13.9 ms, 2.8 MB |
| Texels | 1.9 MB | none |
| Parameter table | four doublings to 16,384 rows: 2.9 MB, four GPU buffers | made once at 16,384 rows: 1.6 MB, one buffer |
| Colour rows uploaded | the whole table at every change | the changed chunks |

What the colour rows still make is a row's palette indices and the book's maps (about 340 bytes a row). The emulated Color Pipeline lookup leaves out walking the palette on a miss, which happens once a palette id.

### 6.9 Smaller items

- **Block allocation.** `IndexBlocks` is first-fit with one block per site (`IndexBlocks.cs:21-41`). Load two 3,000-building sites and unload the first, and a 3,500-building site is refused with 5,176 indices free. Streaming random 50–300-building districts near the 8,176 cap refused 192 of 10,097 loads. Use best-fit or several blocks per site. Low impact at district sizes.
- **Growing registries.** Without Color Pipeline, `HexPaletteIndex` keeps every literal it has seen and throws at 512, which would break the next district's build (`ColorRows.cs:126-137`). `BuildingTable`'s arrays never shrink.
- **Per-frame scene lookups.** `StoreyDistricts.Update` calls `SceneManager.GetSceneByPath` per district per frame. Use the `AsyncOperation`s it already holds, or the scene-loaded callbacks. Status: done (CHANGELOG): the loader knows a scene loaded from Unity's scene callbacks and its own operations (`BuildSceneDistricts`).
- **Background loading priority.** `Application.backgroundLoadingPriority` is left at its default (BelowNormal: 4 ms of integration a frame) [CPU-7]. Document it beside `buildBudgetMs`: they share the frame.
- **Activation.** A district's scene activation runs every object's `Awake`/`OnEnable` in one frame [CPU-7]. The layout asset's 3.9 MB string field loads with it. Measure the integration step on device (Profiler marker `Application.Integrate Assets in Background`) and keep district scenes small and the layout binary (§6.4).

## 7. Shaders and the GPU

What runs: three URP shaders (Opaque for LOD0 and LOD1, Massing for LOD2 and the cells, Glass for LOD0's see-through panes) built from `StoreyLit.hlsl`, `StoreyFragment.hlsl`, `StoreyOcclusion.hlsl`, `StoreyPalette.hlsl`, `StoreyWindow.hlsl` and `StoreyFacade.hlsl`. Each opaque and massing renderer draws in the colour pass, the shadow caster pass (once per cascade it touches) and the DepthOnly pass when a depth prepass runs. Nothing of the URP setup is committed yet (`unity/ProjectSettings` holds only the editor version), so the costs below are the shaders' own; §7.10 lists the project settings that decide the rest.

The street-level load the shaders see (CITY.md §4, SPEC §6.6): about 16 LOD0 buildings (18,300 triangles on average, 48,500 at the 95th percentile; 24,500 and 62,600 in the walk-in city), 60 to 215 LOD1 shells (about 980 triangles) and the cells (43 triangles a massing, 263,000 vertices across the 3,000-building city), about 500,000 triangles in the colour pass before shadows.

### 7.1 Every opaque triangle is drawn two-sided (high)

`Cull Off` on Opaque and Massing (`StoreyOpaque.shader:12`, `StoreyMassing.shader:12`) is there for the section cap: a cut exposes a wall's inside, which draws dark (`STOREY_CAP`). Only buildings that are cut, faded or in the way can show it, and those draw with the non-steady variant. The steady majority (CITY.md §2) pays for it anyway.

Measured (headless ray test, appendix A.3): seen from outside, 49 to 54 % of every LOD's triangles face away from the eye. With back faces culled they would be dropped after the vertex stage, before binning and rasterisation, in the colour pass and in every shadow cascade. On a tile-based GPU that is parameter-buffer bandwidth and tiler work; on desktop it is setup and depth-test work.

It is not a one-line change. The same test finds back faces that are visible from outside today: rays through door openings into the hollow LOD1 and LOD2 shells, and through see-through LOD0 windows onto slab and wall faces that have no counterpart. 0.26 % of first hits on LOD0 (39 of the 78 variant buildings), 0.08 % on LOD1 (21) and 0.07 % on LOD2 (8); 0.9 to 4 % on the demo street, whose walk-in buildings have more openings. These draw as the dark cap now and would turn into see-through holes with culling.

Recommendation:
1. Close the shells: a backing face behind every opening in LOD1 and LOD2 (a door's reveal ends in a dark quad, as LOD1's windows end in a pane), and the missing interior faces the test finds in LOD0.
2. Add the ray test to the headless suite (zero back-face first hits from outside), next to the coplanar check.
3. Give Opaque and Massing a cull property (`Cull [_StoreyCull]`), set to Back on the steady copies (`SiteRenderer.SteadyCopy`) and left Off on the originals. Effort: M (generator) + S (shader). Expected: about half the triangles past the vertex stage for the steady majority, in colour and shadow passes.

Status: done, and on by default on both tiers (`StoreyQualitySettings.cullBackFacesWhenSteady`, CHANGELOG). Step 1: wherever the prototype leaves a surface one-sided or a gap open from outside, Unity draws a back or a closing face, a centimetre or so off (a cut or fading building's materials draw both sides, and two faces back to back would flicker): the roof parts' backs at every LOD, the walls' tops under pitched and terrace roofs, a lid under LOD2's pitched roofs, slab edges above curtain-wall openings, strips' backs across openings, core walls' ends behind windows, and a few more (CHANGELOG). On the 1,000-building walk-in test city they add 1.3 % to LOD0's shell, 0.3 % to LOD1 and 9 % to LOD2 (4,265 triangles); the ones only clear glass shows go with LOD0's rooms (§7.7), drawn only near. Two of the openings were real gaps in the prototype as well: a corner cut a storey too high beside a party wall (fixed in both) and the walls' tops under a pitched roof where its edge rises above them (a cut corner's short edge), which the prototype's two-sided faces showed as the room's inside. Step 2: `BackFaceTests` casts 4,000 rays from eyes round each building of the demo street, the variants and a 40-building walk-in city, at each LOD as it is drawn, LOD0 near (rooms in, glass clear) and far (rooms out, panes painted over), and finds no back face first; it allows slivers only, a ray within 6° of the face or a front face within 5 cm (a backing face's edge seen aslant) or up to 30 cm behind it. What's left: the gain on the iPhone, from the recorder's GPU time at the same view with the switch on and off.

### 7.2 Vertex outputs are the full set in every pass (high)

`Varyings` (`StoreyLit.hlsl:56-73`) carries 34 floats on Opaque and 42 on Massing, plus the instance id, and every pass writes all of them:

| Pass and variant | What its fragment stage reads | What the vertex stage writes |
|---|---|---|
| Shadow caster, depth, steady | nothing but the position | the position plus 8 more float3/float4 outputs (the shadow coordinate among them), and a `_StoreyWallData` read per LOD0 wall vertex |
| Shadow caster, depth, non-steady | position, fade, building index, kind, occluder slot, origY | the same full set |
| Colour, steady | position, normal, colour, world position, window uv, shadow coordinate (and the facade numbers for massing) | the same full set, including the cutaway's `wall`, `misc` and `occ` that the steady fragment never reads |

On Apple's GPUs the transformed vertices and their outputs are stored in the tiled vertex buffer between the two stages, and a full buffer forces a partial render [R6]. Apple advises `half` interpolants. The A10 (Apple GPU family 3) also caps fragment inputs at 60 components, against 124 from the A11 on; Massing already uses 43. Nothing guarantees that unread outputs are removed when the pipeline is linked. The shadow pass is the clearest waste: about 140 bytes written per shadow-caster vertex, per cascade, where 16 would do.

Recommendation: per-pass `Varyings` (a lean depth/shadow vertex function that outputs only the position, plus the five scalars the non-steady dither and Dissolve need), `#if !defined(STOREY_STEADY)` around the cutaway outputs, and `half` for colour, normal and window uv. Skip the `_StoreyWallData` read when the variant can't use it. Effort: M.

Done: `Varyings` (`StoreyLit.hlsl`) now carries, per pass and variant, only what its fragment stage reads, and the colour pass's normal, vertex colour, window-pane values and per-vertex ambient in `half` (CHANGELOG; `ShaderVaryingsTests`). Positions, the facade's metres and building indices stay `float`: indices reach 8,192, past the 2,048 integers a half holds exactly. Steady depth and shadow passes output the position alone (4 floats, from 34 on Opaque and 42 on Massing); non-steady ones the position and five scalars (9). The steady colour pass drops the cutaway's and occluders' values (24 floats on both shaders, from 34 and 42), the massing never carries the vertex colour or pane, and the `_StoreyWallData` read happens only in the non-steady colour pass. The prototype's materials follow the same split.

### 7.3 Shadow coordinates and spherical harmonics

- `OUT.shadowCoord = TransformWorldToShadowCoord(worldPos)` runs in the vertex stage of every pass (`StoreyLit.hlsl:123`), the depth and shadow passes included, and is interpolated. URP interpolates it only for a single cascade or screen-space shadows (`REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR`). With cascades it computes it per pixel, because a per-vertex cascade index is wrong across a large triangle such as a massing wall, a slab or a roof [R7]. Follow URP's pattern, and never compute it in the depth passes. Correctness first, a little vertex work second.
- `SampleSH(s.normalWS)` per pixel (`StoreyLit.hlsl:168`). URP's own shaders evaluate SH per vertex on mobile by default (the asset's SH Evaluation Mode, Auto; `EVALUATE_SH_VERTEX`/`EVALUATE_SH_MIXED`) [R7], and Storey's shader bypasses that setting. Every Storey face is flat, so the per-vertex value is exact anyway: evaluate it in the vertex stage and pass a `half3`. That saves tens of ALU instructions on every lit pixel.
- Smoothness 0.14 and metallic 0 on Opaque, 0.1 on Massing, still run URP's full PBR: GGX specular per light and a reflection-probe sample per pixel. Defining `_ENVIRONMENTREFLECTIONS_OFF` (probe sampling replaced by a constant colour) and `_SPECULARHIGHLIGHTS_OFF` [R7] for Opaque and Massing on the minimum tier (glass keeps both) removes a cube-map fetch and the specular term. That is a quality-tier keyword, not a content change.

Done: the shadow coordinate and the minimum tier's lighting. The colour passes take the shadow coordinate as URP's Lit does: interpolated (`GetShadowCoord`) only where URP defines `REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR` (one cascade, or screen-space shadows, where it is the screen position before the divide), the pixel's own with cascades, and the glass defines `_SURFACE_TYPE_TRANSPARENT` so that it reads the shadow map rather than the screen-space shadows of what lies behind it. **Specular And Reflections** (`StoreyQualitySettings.specularAndReflections`, off on the minimum tier) sets the global keyword `STOREY_SIMPLE_LIGHTING`, which the opaque and massing colour passes declare and `StoreyLit.hlsl` turns into URP's two keywords before including its lighting. The prototype's touch screens draw with Lambert lighting likewise. SH per vertex is done too (CHANGELOG). The colour passes of Opaque, Massing and Glass declare URP's own `EVALUATE_SH_MIXED` and `EVALUATE_SH_VERTEX`, so the pipeline asset's SH Evaluation Mode decides, as it does for URP's Lit: per vertex on mobile under Auto. The vertex stage evaluates the probe with `SampleSHVertex` into a `half3`, and the default lighting takes it through `SampleSHPixel`. Every face is flat and the back faces show the section cap, so the per-vertex value is exact. A project's own lighting (the Color Palette Lit sample's uses no probe) finds it in `StoreySurface.vertexSH`. Whether the interpolant beats the tens of instructions a pixel it saves is for a GPU capture on a device; the pipeline asset's mode switches it.

### 7.4 The massing fragment shader re-derives per-quad constants per pixel (high for the overview, medium at street level)

Everything `StoreyFacadeColor` (`StoreyFacade.hlsl:129-159`) reads is constant across a facade quad, and it is re-read per pixel:
- five `_StoreyParams` loads (texels 0, 1, 2, 4, 5);
- four palette colours (wall, trim, glass, roof), each two `_StoreyColors` loads, an atlas `Load` and an integer divide and modulo by a uniform (`StoreyPalette.hlsl:25`). Apple calls integer division by a non-constant "very, very slow… hundreds of clocks" [R6];
- with the window effect on, `StoreyFacadeWindow` adds `TransformWorldToObject`, `normalize(cross(...))`, extra derivatives, the window hash, and in `StoreyWindowPane` the parallax maths and two texture samples.

In the overview, massings cover most of the screen; at street level they are the skyline behind everything. Recommendation, cheapest first:
1. Resolve each parameter row's four colours (with their shades) on the CPU when the row is written (`BuildingTable.WriteRow`), and rewrite the rows when the palette or a remap changes, as the colour rows already are. The fragment stage then reads three float4s of colour instead of 8 buffer loads, 4 atlas loads and 4 integer divides.
2. Replace the integer divide and modulo by the atlas width with a shift and mask (2.1.11's atlas is 512 wide; pass log2 or precompute the row's atlas y). This also helps every LOD0 and LOD1 vertex.
3. Or fetch the per-quad values in the vertex stage as `nointerpolation half` outputs. That trades varyings for loads, so it is worse on tile-based GPUs than option 1.

Effort: S to M.

Status: done another way (CHANGELOG). Option 1 needs the palette's colours on the CPU, and with Color Pipeline they aren't there: its atlas, remap rows included, is its own (`ColorMappingManager`), bound by it. So the massing finds its four colours in fewer reads instead (`StoreyStyleColors`). Wall, trim, roof and glass are entries 0, 1, 4 and 6 of the colour row, all in its first `uint4`, and the remap row is in its third. That is two buffer loads where four lookups made eight, plus four `_StoreyDetailColors` loads that a style's slot never used (HLSL reads both sides of the slot's `?:`). The atlas's size is found once, not four times. Option 2 is in for every lookup, the LOD0 and LOD1 vertices' too: a power-of-two atlas width (Color Pipeline's 512) splits the flat index with a shift and a mask, behind a uniform branch, and any other width still divides. A massing pixel now makes 11 loads (5 parameter texels, 2 colour-row loads, 4 atlas texels) and no integer divide, where it made 21 loads, 4 size queries, and 4 divides and 4 modulos by a uniform. `GpuColorTests.TheMassingFindsItsFourColoursWhereTheRowPacksThem` pins the words and halves the shader reads. A compute pass that resolves each parameter row's colours on the GPU when rows or the atlas change would save the last four atlas loads; that, and the effect of this change, need a GPU capture on a device.

### 7.5 Occlusion paths (non-steady only)

These run only for the few buildings being cut, faded or in the way, but those are close to the camera and large on screen:
- **Sink's storey search in the fragment stage** (`StoreyOcclusion.hlsl:177-188`) walks the occluder's storeys bottom up: up to n iterations and two buffer loads per pixel. The vertex stage already walks them (`StoreySink`). Pass the storey (or its row) from the vertex stage, or use a binary search. Triangles never cross a storey line by construction (SPEC §5.1).
- **Cutout and Fade** compute `mul(UNITY_MATRIX_VP, _StoreyPlayer)` per pixel (uniform for the whole draw) and `mul(UNITY_MATRIX_VP, worldPos)`, which duplicates `SV_Position` (`StoreyOcclusion.hlsl:201-209`). Set the player's clip position per camera (`beginCameraRendering`) and use `SV_Position.xy` and `.w`.
- **Precision**: all of it is `float`. URP's own lighting is `half` on mobile. On the A10, FP16 runs at twice the FP32 rate (about 690 against 350 GOPS); from the A15 the rates are equal, and `half` saves registers, occupancy and bandwidth instead [R6]. Colours, normals, window uvs and dither values are safe in `half`; positions and the cutaway's plane tests are not. Unity ignores the `h` literal suffix: write `half(x)`.

### 7.6 Discarding draws are interleaved with opaque ones (high on Apple GPUs, effort S)

Apple's rule for its GPUs: draw opaque first, then anything with `discard` or depth output, then translucent, and don't interleave opaque and discarding meshes. Any discard turns that draw's fragments into feedback fragments that hidden-surface removal has to wait for [R6]. URP doesn't sort opaques front to back on GPUs with hidden-surface removal; it keeps the render queue and otherwise orders for state changes [R6]. Storey's Opaque and Massing shaders are both `"Queue" = "Geometry"`, and the steady copies inherit it, so the few discarding buildings (cross-fading, cut, in the way) are mixed in among the steady ones.

Fix: put the discard-capable materials in the AlphaTest queue (2450) and set the steady copies' `renderQueue` back to Geometry (2000) in `SiteRenderer.SteadyCopy`. All steady draws then go first. One line each. On desktop GPUs it costs nothing, and opaque before alpha-tested is the conventional order there too.

Done: both shaders (and the Color Palette Lit sample's) queue at AlphaTest, the steady copies at Geometry, checked by `SteadyTests.SteadyDrawsComeBeforeDiscardingOnes`.

### 7.7 Glass and the rooms behind it (medium)

LOD0's see-through glass (`StoreyGlass.shader`) is transparent with no depth write, lit with full PBR, and in the band between `windowsClearAt` × LOD0 distance and the LOD0 distance it fades to LOD1's painted pane (`StoreyFragment.hlsl:229-244`). Two costs follow:
1. In that band the pane is opaque to the eye (alpha lerps to 1) but still blended, so the opaque room behind it is shaded first: the GPU's hidden-surface removal can't reject pixels behind a blended surface.
2. The rooms themselves: walk-in LOD0s average 24,500 triangles against 18,300 for plain ones (CITY.md §4), so interiors are roughly a quarter of a walk-in LOD0. They are drawn and cast shadows wherever the building shows LOD0, including the band where no window shows them.

Recommendation: split LOD0's interior into its own renderer. Draw it (and the glass as glass) only while the glass is clear or the building is the player's or cut; in the painted band draw the opaque pane instead. Interiors cast shadows only for the player's building, which needs them for its indoor light (SPEC §5). Effort: M (the generator knows which faces are interior).

Status: the rooms are their own renderers (CHANGELOG): the inner faces of a storey's outside walls, its partitions and its cores, 18.6% of a walk-in LOD0's triangles (21.9% on the demo street). They draw while the building's nearest point is within the distance its windows are painted over from, or while it can show its inside, and they cast shadows only for the player's building. Floors and ceilings stay with the shell: without their shadows the sun would light every storey through the windows of those above. What an opening shows at any distance stays with the shell too: a storey with a doorway or an artist's glass (which the fill doesn't paint), the top storey under a bulkhead's door, and every storey under a skylight or a straight flight's roof opening. A ray test from eyes 50 to 150 m away, with the painted panes opaque and every other glass clear, meets a room first in 1 of 20,000 rays on the demo street, 7 of 205,000 on the variants and none on the walk-in test city. Every one of those rays enters through a gap in the shell itself (§7.1 step 1). Measured on the two views the iPhone 11 Pro recorded: every LOD0 there is nearer than the painted-over distance (100 m), so the colour pass is unchanged and the gain is the shadow pass, 17.4–17.6% fewer LOD0 triangles per cascade. The colour pass gains only on LOD0s in their hysteresis band (100 to 127 m); painting the windows over sooner would widen it (at half the LOD0 distance, 4–10% fewer LOD0 triangles in those views), a look to judge on the device. The opaque pane in the painted band (cost 1) is still open.

### 7.8 Shadow casters (medium to high, depends on cascades)

Every LOD0, LOD1 and cell renderer casts shadows (`ShadowCastingMode.On`: `SiteRenderer.Add`, `BuildCell`), and the shadow pass runs Storey's full vertex stage (§7.2). Shadow cost scales with triangles × cascades touched, and at street level LOD1 shells and cells fill the far cascades. Recommendations:
- **Shadow proxies for distance:** let a building at LOD1 cast through its massing in the cell (a shadow-only pass of the massing that ignores the LOD hide for buildings at LOD1) and turn LOD1's own casting off. LOD1 is about 980 triangles, its massing 43, with the same outline and heights. *Done* (docs/CITY.md §3): the massing's shadow caster pass treats LOD1 and LOD2 alike, LOD1 renderers cast none, and a cell with LOD1 members but no LOD2 one draws into the shadow maps only. The prototype does the same (its cell stays drawn, every triangle clipped in its colour pass, three.js having no shadows-only mode); its street views show the same shadows but for roof bulkheads and canopies.
- Interiors as above (§7.7). *Done*: LOD0's rooms cast only for the player's building.
- Per tier: cascades and shadow distance (§7.10).

### 7.9 Variants and pipeline-state warm-up

- Opaque's colour pass multi-compiles the main-light shadow modes, additional lights, four soft-shadow levels, the cluster loop, SSAO, DOTS instancing and `STOREY_STEADY`: 960 combinations before URP strips them, ×2 for every keyword a project adds. The Color Palette Lit sample's opaque pass has 30,720 combinations before stripping. `STOREY_STEADY` must stay a real variant: `discard` present in a shader is what costs hidden-surface removal [R6/R8], so a dynamic branch is no substitute.
- The steady split doubles the PSOs a frame can need for the first time. CITY.md §8 lists warm-up as not done; a variant first met mid-game compiles then. `ShaderVariantCollection.WarmUp` is not recommended on Metal, Vulkan or DX12 because it can build the wrong PSOs. Use `GraphicsStateCollection` (experimental since 6000.0): trace it in a development build on each device tier and graphics API over the test-city walk, ship it, and call `WarmUp`/`WarmUpProgressively` behind the loading screen [R8]. Strip unused URP keywords per tier with `IPreprocessShaders` and URP's global stripping settings.
- Props (`StoreyProp.hlsl`) require Alpha Clipping on in Shader Graph, so every prop draw has a `discard` even when nothing hides it. The buildings solved this with the steady variant; props have no equivalent. Offer a steady keyword on the prop graph, or use Hide Whole on the minimum tier.

Status: warm-up and stripping are in (CHANGELOG). **Storey Shader Warm-Up** (`StoreyShaderWarmUp`) traces in a development build: `GraphicsStateCollection.BeginTrace` at start, saved when the app goes to the background or quits, and sent to the editor when one is connected. At start it picks the collection for the device's platform and graphics API, and its quality level where several match, and makes it a few states a frame (`WarmUpProgressively`), or all at once behind a loading screen. The API is `UnityEngine.Experimental.Rendering` up to 6000.4 and `UnityEngine.Rendering` from 6000.5; the component takes either. The authoring package's build step (`StoreyShaderStripping`) leaves out `STOREY_SIMPLE_LIGHTING`'s variants when no quality asset the build can use turns specular off: one a scene in the build depends on, or any in `Assets`. It logs each Storey shader's variants after the build. Storey's other keywords are all needed, and URP strips its own by its assets' features. The per-tier URP settings are written up in the package's `Documentation~/quality-tiers.md`, with the warm-up's steps. What's left: the traces themselves, on each tier's device, and the prop graph's steady keyword (§11.1).

### 7.10 Missing passes and project settings the package depends on

- No `DepthNormals` pass, so the buildings are missing from the normals texture: URP SSAO in depth-normals mode and decals break. No `MotionVectors` pass either [R7]. Static buildings are covered by the camera's depth-based motion vectors, but Sink's moving vertices are not, so they ghost under TAA and STP. That matters for performance because STP (compute; it runs on Metal; Render Scale only, not Dynamic Resolution [R12]) or a lower render scale is the minimum tier's main fill-rate lever (Plan §6.6's rule: settings, never content).
- Depth priming must stay off (SPEC §6.7), and is off on iOS anyway [R10]. A depth prepass, if a project's features force one, draws every building whole and doubles the vertex work of a vertex-heavy scene. On mobile let URP copy depth instead (Depth Texture Mode *After Transparents*, Unity's recommendation for bandwidth [R11]).
- **GPU Resident Drawer on the minimum tier is a measurement, not a given.**
  - GRD needs Forward+ or Deferred+ on every renderer in the URP asset [R1].
  - Unity's docs recommend plain Forward for mobile and low-end devices [R10].
  - From 6000.4 the docs say GRD "slightly increases GPU workload", more so on low-end mobile [R1].
  - With a unique mesh per building, GRD can't instance anything: every building is at least one draw per pass either way. Its gain is CPU-side (resident instance data, Burst culling).
  - The plan already requires everything to render correctly with the SRP Batcher alone (Plan §6.1). Measure an iPhone 7 both ways (GRD with Forward+, SRP Batcher with Forward) and pick per tier.
- **GPU occlusion culling** tests each renderer's bounding sphere against a depth pyramid. It is conservative for long or merged objects, so cells and tall buildings are rarely culled. It forces the depth texture, turns draws into indirect ones the CPU still issues, and doesn't cull shadow casters [R2]. Keep it a per-tier measurement, as the plan says.
- **Per-tier URP settings to document beside the quality assets** (written up in `Documentation~/quality-tiers.md`, as starting points for the device runs to confirm):
  - main-light cascades (1 to 2 on the minimum tier) and shadow distance (about the LOD0 to LOD1 range);
  - soft-shadow quality;
  - SSAO off on the minimum tier;
  - render scale or STP;
  - MSAA;
  - HDR format;
  - additional-light limits.

Done since: auto resolution. **Storey Auto Resolution** (`StoreyAutoResolution`) is the prototype's, which held its city views on an iPhone at 60 fps by drawing them at 60–80 % of the screen's resolution, where full resolution drew 53: Unity's dynamic resolution (`ScalableBufferManager` and the camera's Allow Dynamic Resolution, from which URP 17.3 draws into an intermediate texture by itself), stepped between full and half resolution by `Lod.AutoResolution` with the prototype's rules. With Frame Timing Stats on, a frame is late when the GPU took longer than the frame rate's interval, and a step up waits until the GPU's time says it fits; without, it goes by frames over a fifth of an interval late, whatever made them late. It needs a platform with dynamic resolution (Metal, Vulkan, DirectX 12, consoles); elsewhere render scale or STP stay the lever.

### 7.11 Small triangles

At the far end of LOD0's range (58 m at 1080p, 16 px/m), a 5 cm window-frame bar is under a pixel wide. Thin triangles shade whole 2×2 quads for one or two covered pixels, so the fragment cost per visible pixel rises. LOD0 px/m is already a tier setting. Measure quad efficiency (Xcode's GPU counters, RenderDoc's quad overdraw) before changing it.

## 8. Detail generation on the build threads

A detail build is `Lod0.Build` (or `Lod1.Build`), then `MeshPack.Pack` and, for LOD0, `CollisionMesh.Build`, on a build thread; the main thread uploads it, a part at a time, once a build thread has finished its vertices (§3.2, §4.1). The work is off the main thread, so its cost decides how quickly detail arrives, how many buildings the threads can keep up with, battery and heat on a phone, and the garbage every thread's collection pays for (§12). Measured on the test cities with the renderer's own sequence (.NET 8, steady state with the pools warm; profiles from an unbiased sampler; appendix A.7):

| Per building | Plain city | Walk-in city |
|---|---|---|
| LOD0 generate, mean (p95 / max) | 2.0–2.45 ms (5–7 / 12–32) | 5.3 ms (17.8 / 49) |
| LOD0 pack (opaque + glass, unwelded) | 1.2–1.7 ms | 2.7 ms |
| Collision mesh | 0.34 ms | 0.81 ms |
| LOD1 generate, weld and pack | 1.3 ms | 1.6 ms |
| LOD2 (massing) | 0.16 ms | 0.11 ms |
| Garbage: LOD0 generate, steady (cold) | 98 KB (1,176 KB) | 192–204 KB (2,396 KB) |
| Garbage: collision mesh / LOD1 / LOD2 | 69 / 68 / 24–29 KB | 332–339 / 96 / 16–21 KB |

The pools work as CITY.md §7 says: steady LOD0 garbage is a twelfth of a cold build's, and packing makes almost none. What is left is CPU per vertex, work done more than once, and one latent garbage source that real content will switch on.

### 8.1 Artist-made windows, doors and details allocate per placement (high once content uses them)

`OpeningKinds.Place` (`OpeningKinds.cs:69-109`) and `DetailKinds.Place` (`DetailKinds.cs:110-130`) allocate two `P3[]`s per placement. Per part, `Compact` (`OpeningKinds.cs:112-123`) adds a `Dictionary<int,int>`, two `List<P3>`s and their `ToArray` copies; walk-in glass also allocates reversed index and inverted normal arrays. `MeshBuilder.Mesh` then reads them through `IReadOnlyList` interfaces.

With a 100-vertex, two-part window kind registered for every building (measured):

| Build | Generator's windows | Artist's window |
|---|---|---|
| Plain LOD0 | 2.6 ms, 93 KB | 7.5 ms, **7.2 MB** |
| Plain LOD1 | 0.8 ms, 65 KB | 6.6 ms, **7.2 MB** |
| Walk-in LOD0 | 4.3 ms, 192 KB | 13.0 ms, **11.3 MB** |

The shipped detail assets carry no meshes, so none of this is live yet. It becomes the largest garbage source in the package the day a project adds artist windows (EDITOR.md sections 6.9 and 6.12).

Fix (S–M): compact each part once per kind, at `Register`, including the reversed and inverted copies, and transform straight into the builder through an array or span overload of `MeshBuilder.Mesh`.

Status: done (CHANGELOG). Each part is made ready once (`KindPart`): when its kind is registered, or when first placed if its arrays changed since. It holds its points and normals, its triangles into them, and those wound the other way for walk-in glass. A placement transforms it straight into the builder (`KindPart.Put`, `KindPlace`), with nothing allocated. On the test cities with a 100-vertex, two-part window kind on a quarter of the buildings (`OpeningKindGarbageTests.AnArtistsWindowMakesNoGarbageWherePlaced`), a build allocates what one with the generator's windows does: plain LOD0 168 KB (was 21.0 MB; the generator's 165 KB), plain LOD1 104 KB (was 20.9 MB), walk-in LOD0 502 KB (was 21.9 MB; the generator's 317 KB), walk-in LOD1 160 KB (was 21.0 MB). The meshes are byte for byte the same, windows, doors and details (402 meshes, 16.5 million vertices compared). A triangle naming a point the mesh hasn't is now left out whole, where it pointed at another part's vertex or past the mesh.

### 8.2 Packing costs 40–75 % of generating (medium, effort S for most of it)

`MeshPack.Pack` (`MeshPack.cs:163-254`) makes three passes besides the conversion: an index range check, a neighbour scan through the non-inlined `Packer.Kind`, and a bounds pass in the `PackedMesh` constructor with `Math.Min/Max`. The conversion itself is 42 % of the loop (30 ns a vertex).

A fused single pass, prototyped in a scratch copy, does all of it at 18.5 ns a vertex instead of 35.4. Indices are checked as they are copied, neighbours collected with a last-value cache, bounds taken with plain compares. All 303 test meshes came out byte-identical.

Two more fixes:
- **The split path** for LOD0s past 65,535 vertices (119 of the 3,007 plain buildings, 151 of the 1,007 walk-in) gathers every vertex through indexers at about 230 ns a vertex: as long as generating them. Close parts at primitive boundaries while emitting, so each part is a contiguous range.
- **The `ChunkList<T>` indexer isn't inlined** (`Chunks.cs:121-128`). Its range check throws with an interpolated message, which C# 9 compiles to `string.Format` with boxing inside the getter. A `[NoInlining]` throw helper with an `[AggressiveInlining]` getter measured −9 % on LOD1, −12 % on the pack loop and −28 to −35 % on the split path.

Status: the fused pass and the indexer are done (CHANGELOG); the split path still gathers. A mesh packed whole is one pass over its vertices (each style named, the vertex written, its party-wall neighbour found, the bounds taken) and one over its indices (each checked as it is copied), where it was four passes and the bounds' fifth. The indexer is inlined, its throw apart. LOD0's rooms, packed apart since §7.7, go through the shared path, which now reads the builder chunk by chunk. That path had made a walk-in LOD0's pack cost more than its generation. Headless, back to back, a building on average: the plain city's LOD0 pack 0.80–0.86 ms → 0.55–0.60, its LOD1 weld and pack 0.21–0.25 → 0.17; the walk-in city's LOD0 pack 5.3–5.5 ms → 2.6–2.8, its LOD1 0.36 → 0.25. Every mesh of both test cities and the fixtures is the same to the byte (36,828 compared: LOD0, its glass and rooms packed, the shape, the collision mesh, LOD1 packed, LOD2).

### 8.3 Per-vertex bookkeeping is half of LOD0 (medium)

`V`, `Quad`, `OBox` and `ChunkList<int>.Add` together are about 54 % of a plain LOD0's samples. Every vertex appends to five lists (`P`, `N`, `C`, `K`, `WI`). Every normal and colour goes through `IndexedList.EntryOf`, an interface `Equals` against the last entry and, on a miss (12 %), a `Dictionary<P3,int>` lookup hashing three longs. Every quad re-derives its winding with a cross product. A plain LOD0 averages 19,599 `V` calls, 4,608 quads and 39,198 `EntryOf` calls; a walk-in one twice that.

Fixes:
- **Incremental (S–M), an estimated −15 to −25 %:**
  - quad and box fast paths that reserve and write four vertices and six indices at once;
  - normal and colour resolved once per face;
  - `K`/`WI` kept as runs per wall context rather than per vertex;
  - a struct comparer as a generic parameter instead of an interface (only 11 normals and 10–16 swatches per building).
- **Structural (L):** emit straight into the upload layout (float positions, SNorm8 normals, colour bytes), keeping a double-precision debug path for the census and coplanar tests. That absorbs most of §8.2, halves the builder's 46 bytes a vertex, and is the route to Burst (§8.8).

Status: a face's normal and colour are resolved once for its vertices (`MeshBuilder.Quad`, `Poly`, `PolyTris`), where each vertex looked them up: LOD0 generation's best of nine runs on the plain test city 1.02–1.05 ms → 0.91–0.93 a building, on the walk-in city 2.80 → 2.45–2.61 (headless), the same meshes to the byte. The quad and box fast paths, struct comparers and runs of `K`/`WI` are not done, nor the structural change.

### 8.4 Wall faces go through general Earcut (medium)

Every wall face is a rectangle with rectangular holes in its own (u, y) plane, triangulated by Earcut (`Facade.cs:444-459`). That is 18 % of a plain LOD0 and 26 % of a walk-in one, mostly hole bridging.

A rectilinear triangulator would do it in O(openings): split the face at every opening's u0 and u1 and fan each column, sharing break heights so no T-junctions appear. Keep Earcut for slabs and roofs. The census is triangulation-independent, and the topology tests guard the T-junctions. Estimated −15 to −20 % of LOD0; effort M.

Status: not done. It changes every wall's triangles, so the meshes can't be compared to the byte as the other changes here were, and a T-junction left would show as a crack on a device before any test here caught it. Worth doing with a device to look at.

### 8.5 Work done again for every LOD (medium)

- **Roof parts.** A pitched building's straight skeleton and roof clipping run for LOD0, LOD1, LOD2, the walk shape and the collision mesh: five times. `BuildShape` builds them only to draw nothing (`Lod0.cs:525`). Memoise per site and building. Status: done (CHANGELOG): the top roof and each roofed setback's are made once a site (`Site.roofMemo`), and every LOD reads them. On the plain test city each of LOD0, LOD1, the collision mesh and the walk shape allocates 13 KB less, LOD2 a third of what it did (6.8 KB, was 20.1), and LOD2 takes 0.021–0.023 ms where it took 0.035–0.043 (headless); the same meshes.
  - Within `Roofs.Parts`, gable vertices are matched by formatted strings (`Roofs.cs:167-178`: `q[0].ToString("F5") + "," + …` in nested loops). Use rounded integer keys.
  - `Skeleton` allocates a `double[2]` per point query. Its garbage grows with corners: 12 KB at 4 corners, 523 KB at 68 (0.5 ms). The test city averages 5, so this is latent for authored rounded outlines.
- **The party-wall memo is reset per build** (§6.6). Within a build it works (184–260 queries, 5 computed), but every LOD of every building recomputes it in a fresh `ConcurrentDictionary`. Keep it for the `Site`'s lifetime: a new `Site` is made per load and per edit anyway, and the per-build reset came from the prototype, which mutates buildings in place.
- **LOD2 is allocation-bound**: 38 % of its samples are allocation and collection on .NET (Boehm allocates more slowly), about 70 MB per 3,000-building load. Roof parts are 45 % of it and party queries 31 % (`Site.Near` builds a `HashSet`, `Party.Tiers` two lists). The fixes above, plus a cached palette per tier and `ParamRow`'s arrays as fields, cut most of it. Massing throughput is what bounds load time (§6).
- **LOD1 computes each storey's openings twice.** Status, in part (CHANGELOG): the openings no longer make a list for each lift's levels, one for the lifts flush with the edge where none is, or one for a building's bridge doors where it has none; a walk-in LOD1 allocates 66 KB, was 96, and a walk-in LOD0 211 KB, was 246. They are still worked out twice. `Facade.Ops` runs in the facade loop and again in `Details.At`, 26.6 % of a walk-in LOD1. Inside it, `Cores.Levels(...).Contains(k)` allocates a list per lift per edge per storey. LOD1 also welds (33 % of its build-weld-pack time, for 3–3.6 % fewer vertices) and keeps pane UVs in a dictionary (8 %). Compute openings once per storey and edge, carry pane UVs per vertex, and weigh dropping LOD1's weld as LOD0's was (about +0.7 MB GPU across the resident shells).

### 8.6 Wall ids: three times as many as walls, a third unused (medium, effort S–M)

A plain LOD0 hands out 117 wall ids for 37 distinct walls. 42 of them (36 %) are used by no vertex, and glass never shares an opaque wall's id (`Lod0.cs:67, 355-366`; `MeshBuilder.cs:198-204` clones `W` for each). Each id costs two `double[4]` allocations (15–20 % of LOD0's steady garbage), a row in the building table (`FinishDetail` writes three times the rows), shape memory, and a pass of the per-frame cutaway loops (§10.1). Intern walls by (W, kind, storey base) in `Ctx`, give glass its wall's id, create an id only when a vertex uses it, and store W as four floats.

Status: not done. The ids are what the cutaway slides by, on the CPU and in the shaders; changing how they are handed out changes every LOD0's wall data, which the occlusion tests cover only in part. The cutaway's own work per frame is cut by §10.1 instead.

### 8.7 The long tail and memory per build

- **Steady LOD0 garbage (98–204 KB)**: `double[]` (10 %), wall-list growth (9 %), range lists and arrays (17 %), `Opening`s (10 %), `WinSpec` (a class per `Ops` call), segment-list copies from `TrimExcess`, and `Bridges.Doors` allocating before its early return (`Bridges.cs:143`). Scratch lists through `Lod0.Shared`, `WinSpec` as a struct, and pre-sized wall and segment lists cut it by an estimated 2–3×.
- **Latent: `Derived.StyleAt`** clones a `FacadeStyle`, rule dictionary included, on every call for a setback tier with its own style (`Derived.cs:83-90`). That is 132–193 calls per LOD0. The test city has no such tiers; authored buildings will. Memoise per building and tier.
- **Collision meshes** run a Clipper union, inflate and difference per walkable level: `GrownUnion` 1.4 times a plain building and 9.9 a walk-in one (0.34 / 0.81 ms, 69 / 335 KB). Middle storeys share outline and holes; memoise by those. Status: done (CHANGELOG): a level's area is made once for the levels with the same outlines below and at it and the same holes open, and a wall run's box makes no arrays. A walk-in building's collision mesh takes 0.25 ms and allocates 153 KB, where it took 0.44 ms and 366 KB; a plain one's allocates 40 KB, was 54 (headless, the same meshes).
- **Peak memory per build.** The builder holds 46 bytes a vertex (24 of them double positions); `Lod2Mesh` 100 bytes a vertex (normals and facade numbers as doubles). The largest tower holds 14.6 MB of builders and 11 MB packed at once. With two to four threads on towers, transient peaks of 25–100 MB are possible, and chunks past the keep cap become garbage. Use float positions in the builder (packing converts to float anyway), and build one large tower at a time on the minimum tier.

Status of the rest of §8 (headless, a building on average, back to back with the code before; every mesh of both test cities the same to the byte throughout, 36,828 compared): the collision memo above, and the allocations of the openings (§8.5). `Derived.StyleAt` isn't memoised: a memo by object would hand the editor's in-place edits a stale copy. `WinSpec` stays a class (a public type, about 5 KB a building). Float positions in the builder, the quad and box fast paths, the split path's contiguous parts and integer gable keys are not done. Where the detail pipeline stands now:

| | Plain city, before → now | Walk-in city, before → now |
|---|---|---|
| LOD0 generate | 1.02–1.05 → 0.91–0.93 ms (best of nine) | 2.80 → 2.45–2.61 ms |
| LOD0 pack | 0.80–0.86 → 0.52–0.60 ms | 5.3–5.5 → 2.6–2.9 ms |
| Collision mesh | 0.11 → 0.08 ms, 67 → 40 KB | 0.41–0.46 → 0.25 ms, 372 → 153 KB |
| LOD1 generate, weld and pack | 0.41–0.48 → 0.33–0.37 ms, 62 → 44 KB | 0.73–0.75 → 0.57–0.60 ms, 101 → 66 KB |
| LOD2 | 0.033–0.043 → 0.021–0.023 ms, 20 → 7 KB | 0.031 → 0.025–0.029 ms, 11 → 6 KB |
| LOD0 garbage | 90 → 72 KB | 252 → 211 KB |

### 8.8 Burst, IL2CPP and what a port needs

Builds run on Storey's own threads, so Burst-compiled function pointers can be called from them directly, with no job scheduling: engine-free `static unsafe` kernels in `Runtime/` over pinned chunks, compiled with `[BurstCompile]` in the Unity assembly. Unity's own mesh samples measured managed-to-Burst speedups of 4–15× single-threaded (`MeshApiExamples` [CPU-11]).

| Candidate | First |
|---|---|
| Fused `MeshPack` (§8.2) | pane UV as a per-vertex array, style numbering done beforehand; "works on plain arrays already" (Plan §4.3) isn't quite true yet (a dictionary and the `Packer` class) |
| `Cells.PackPiece` | `Lod2Mesh` as arrays or chunk lists; `math.f32tof16` for the half conversions |
| `Weld` | nothing beyond flat chunk data |
| Quad and box expansion | a command buffer from the managed generator (L; the biggest win) |

Clipper2, the skeleton, roofs and the facade logic are class-heavy control code; keep them managed and cut their allocations [CPU-11].

For IL2CPP:
- Turn off null and bounds checks on the proven hot loops with `[Il2CppSetOption]`: packing, `Cells.PackPiece`, `Finish`, `Weld`, `ChunkList`. The attribute is matched by name, so `Runtime/` can declare its own copy and stay engine-free [CPU-10].
- Prefer struct comparers and arrays to interface dispatch per vertex.
- Mind write barriers under incremental collection: Earcut's and Clipper2's node links are reference stores.

Status: the packer's kernels are in (CHANGELOG); cell packing and the weld aren't. The mesh packer's loops are pointer kernels in `Runtime/` (`PackKernels`: a whole mesh's vertices, its cutaway stream, its indices, and `PackedMesh.Finish`), compiled by Burst through direct calls in the Unity assembly (`StoreyBurst`) and called from the build threads. With Burst on they pack a whole mesh; otherwise the packer's own single pass does, which runs a tenth faster managed than the kernels do (headless). The kernels' bounds are .NET's `Math.Min` and `Math.Max` written out, so Burst's build gives the same, and `PackKernelsTests` packs every fixture's LOD0, glass and LOD1 and walk-in LOD0s both ways, to the byte, finished. IL2CPP's array and null checks are off on the chunk lists' indexer and `Add` (`Il2CppSetOption`, a copy of IL2CPP's attribute), whose range the code checks already. Cell packing and the weld read `List<T>`s, which .NET Standard 2.1 can't pin; they need `Lod2Mesh` and the weld's tables as arrays or chunk lists first. What Burst gains on a phone is the device run's to measure: the recorder's detail times, with Burst's compilation on and off (Jobs ▸ Burst in the editor; `STOREY_NO_BURST` leaves it out of a player).

## 9. Threads

`BuildThreads` (`Lod/BuildThreads.cs`) is a `TaskScheduler` over the package's own long-lived threads, with one FIFO queue (`LinkedList<Task>`). It carries everything off the main thread: layout parses, massings for a load, LOD0 and LOD1 builds, packing and collision meshes. Unity's job workers are deliberately not used; a build takes milliseconds and Unity's guidance is that jobs don't yield and long ones block the workers [CPU-4]. That reasoning is sound. What the design doesn't yet account for is the iPhone.

### 9.1 How many threads, at what priority (high on the minimum tier)

- **Thread count.** `Automatic(cores, handheld) = Max(1, Min(handheld ? 2 : 4, cores - 1))` (`BuildThreads.cs:31`).
  - The A10 Fusion runs one of its two clusters at a time and reports two cores to software [CPU-4]. So an iPhone 7 gets **one** build thread, not the two CITY.md §7's minimum-tier figures assume.
  - With one thread the test city's load takes 60–65 frames instead of 23–29, and every cell shows after 1.1–1.3 s at 60 fps (2.2 s at 30) instead of 0.5–0.75 s (appendix A.5).
  - An iPhone 14 reports six cores and gets two.
- **Priority.** The threads ask for below-normal priority "where the platform allows it", and iOS doesn't. IL2CPP's POSIX thread layer leaves `SetPriority` empty, and threads are created with no QoS class, so they run at the default QoS beside Unity's main and render threads [CPU-4].
- **Who shares the cores.** On an iPhone 7 the main thread, the render thread, one job worker, and the build thread(s) all share two cores.

Recommendation:
- Set the build threads' QoS to utility from native code (`pthread_set_qos_class_self_np(QOS_CLASS_UTILITY, 0)` in `BuildThreads.Started`, through a tiny iOS plugin) [CPU-4].
- Measure one against two threads on an iPhone 7 (frame time and time to detail) before choosing.
- Make the count one global setting; today every site resizes the shared pool from its own quality asset every frame (`SiteRenderer.cs:715-724`), so two sites with different assets would fight.

Status: done but the measurement (CHANGELOG). Each build thread sets its own quality-of-service class to utility before its first build on iOS (`Plugins/iOS/StoreyThreads.c`, `StoreyThreads.Utility`, through `BuildThreads.Configure`). Every site shares one count, the most any site's settings ask for (`BuildThreads.SharedCount`), or `StoreyThreads.Override`. For the measurement, Storey Diagnostics has **Build Threads** (every site's count for a run) and **Utility Threads** (off: each thread keeps the class it started with). Its record names the count and each thread's class before and after, and every ten seconds a "detail" note gives how long detail took from a build's start to its last mesh up (median, 90th percentile, most, per LOD) and how many buildings waited for a thread. Its frame line shows the builds waiting for their upload (`upl`). What's left is the device: a run with 1 and one with 2 threads on an iPhone 7, and on the primary tier whether utility's efficiency cores bring detail in too slowly.

### 9.2 One FIFO queue (medium)

Everything waits in order:
- A detail build queued behind a district's parses and massings started about 0.5 s later (measured, one or two threads).
- A forced building's LOD0 still in the queue is run inline on the main thread by `Task.Wait` (§3.1).
- A long LOD0 (75–135 ms for the largest walk-in towers on x64, appendix A.7) holds its thread with no way to yield or cancel.

Recommendation:
- Priority classes: forced and visible detail first, then the cells of a load, then parses and prefetch.
- Builds in steps, a storey per step, as the prototype does (SPEC §6.5). A step's boundary is where a build checks for cancellation (layout changed, no longer wanted) or yields to a higher priority.

Done, the first half for detail: `LodManager.Update` takes the camera's view (`LodView`, from `SiteRenderer.LodEye`) and asks for forced buildings' detail first, then for buildings in view, then for the rest, and builds no LOD0 for a building out of view, which shows its shell meanwhile. The renderer starts jobs in that order, so the threads' queue gets it too. A frame with the view makes no garbage (`FrameAllocTests`). The priority classes are in too (`BuildLane`): forced detail, then detail, then a layout's cells, then parses and prefetch, the first queued of the most urgent lane taken first (`BuildThreadsTests`). Builds in storey steps, with cancellation, are in too (CHANGELOG). A LOD0 on a build thread is asked after each storey (`Lod0.Build`'s stop overload, the same meshes when it goes on). There the builds waiting in a more urgent lane run first, on that thread (`BuildThreads.RunMoreUrgent`), and a build no longer wanted stops, giving back what it made. It is no longer wanted when a finished one would be thrown away: another layout, its LOD there already, or a coarser LOD wanted, for a quarter of a second, so a building on a LOD's edge doesn't lose its build each time the camera wavers. With the only build thread on the walk-in test city's tallest tower (39 storeys, 113 ms headless), a building wanted at once had its LOD0 after 2.9 ms, where it waited 111 ms for the tower (`BuildStepsTests`). LOD1, at most about 20 ms, isn't stepped.

### 9.3 Locks (low)

IL2CPP's `lock` is several times slower than Mono's (UUM-77985: 6,872 ms against 1,782 ms for the same lock loop; Won't Fix) [CPU-4]. Storey takes few: about 150 per LOD0 build, measured (66 chunk rents and returns, 13 `Lender` borrows, 4 spare-builder locks), with no contention from one to four threads. Even at several times the cost that is tens of microseconds per build. No action needed for speed.

The build threads belong to the package, so each could own its working state outright: a per-thread context created in `BuildThreads.Started` and passed into the generator. That would be simpler than lending, and needs neither locks nor thread statics. The ban on `[ThreadStatic]` (`PackageLayoutTests`) also rests on a narrower rule than it states. The manual's warning traces back to Unity's asset-unloading scan, which ignores thread statics: a `UnityEngine.Object` held only there can be unloaded under you [CPU-3]. Engine-free state is unaffected, and Unity's own code uses thread statics.

## 10. Occlusion and the walk model

In Play mode `StoreyOcclusion` drives the view: each frame the engine-free `OcclusionCore` finds the buildings in the way (three rays against outline prisms over the grid's candidates), animates them, and eases the cutaway's wall slides. `StoreyOcclusion.Write` then puts the result into the building table and shows Sink's footprints. The walk model (`PlayWorld`, `Walker`) gives the play kit its collision and floors.

Measured per frame (headless, x64; mean / 95th / 99th percentile):

| Scenario | Occlusion core | of which the cutaway | Walker step |
|---|---|---|---|
| 3,000-building city, orbiting outside | 0.023 / 0.039 / 0.074 ms | ~0 | 0.003 / 0.005 / 0.009 ms |
| Inside 40 typical walk-in buildings | 0.064–0.067 / 0.15–0.17 / 0.25–0.27 ms | 0.05 / 0.14–0.15 / 0.23–0.25 ms | 0.008 / 0.02 / 0.03 ms |
| Inside the 10 tallest walk-in buildings | 0.12–0.14 / 0.19–0.24 / 0.33–0.37 ms | 0.11–0.125 / 0.18–0.22 / 0.31–0.34 ms | 0.015 / 0.025 / 0.05 ms |

Both allocate nothing per frame (`FrameAllocTests`; §12.2 explains why that carries over to Unity 6). The steady cost is small. What grows is the cutaway inside large buildings, and the spikes when something new enters the view.

### 10.1 The cutaway works on every storey of the building (medium; grows with interiors)

`OcclusionCore.UpdateCutaway` (`OcclusionCore.cs:242-270`) walks every wall of the player's building, and every party wall of its neighbours, on every storey. It keys each in a `HashSet<(string, int)>` and `Dictionary<(string, int), double>`, and rebuilds the key list to slide walls back up. The shader reads the slide only inside the active storey's cut range (`StoreyOcclusion.hlsl:141-147`), and the editor's own cutaway already filters to that storey (`SiteRenderer.cs:533`).

In the 34-storey, 1,105-wall test building, 117 slide entries are live on average and 3.4 of them (2.9 %) are on the active storey. 85 % of the cutaway's time goes to hashing and dictionary work on string-keyed tuples; the wall test itself is 11 %. On the Unity side, `StoreyOcclusion.Write` looks up `WallBaseOf(string)` per wall entry per frame (`StoreyOcclusion.cs:146-151`).

Recommendation (M):
- Filter to the active storey ±1, which keeps stairs seamless. `Lod0Shape.WallBase` is non-decreasing in every walk-in test building, so a binary search gives each storey's range.
- Keep per-building arrays (a slide per wall, an int frame stamp instead of the `seen` set) instead of tuple-keyed maps, and hand `StoreyOcclusion` global wall ids.
- Measured on the same building: 0.008–0.015 ms instead of 0.13–0.18 ms. That matters for real interiors: at 60–90 walls a storey over 30–40 storeys, today's cutaway projects to 0.6–2.4 ms a frame on an A10.

### 10.2 Spikes when a building is first met

The memory bench's five-minute walk (`TheWalkModelStaysSmallOnAFiveMinuteWalk`) records its worst frame. Over four runs on the test machine it was 10.5, 11.7, 17.5 and 27.4 ms (its bound is 25 ms), where CITY.md §7 gives 1.0 ms. The slow frames are first meetings: a walk shape built on the main thread (p95 0.9–2.6 ms), a pitched roof clipped to find its height, a footprint built, plus collections. On an A10 those are multi-frame hitches. Everything in this table is engine-free, so it can be built on the build threads ahead of need, as the renderer already does for LOD0:

| Event | Cost (x64) | Fix |
|---|---|---|
| First ray test of a pitched-roof building: `OcclusionCore.ShapeOf` builds the whole roof (Clipper2) for one height (`OcclusionCore.cs:129`) | mean 0.1–0.2 ms, max 5.5 ms, 53 KB; 28 % of buildings have pitched roofs | compute the rise from the roof's parameters, or memoise it with the building's LOD build |
| First Sink footprint per building, storey and slot: `SinkFootprint.Build` + pack, main thread | 0.08–0.13 ms mean, max 2.2 ms, **83 KB of garbage for about 60 vertices** (a fresh `MeshBuilder`, never recycled; LINQ) | `MeshBuilder.Make`/`Recycle`, no LINQ, build it on a worker with the building's LOD0 |
| The occluded site changes (each `Show`, each edit, each district crossing, crossing back too): a new `PlayWorld` and `OcclusionCore` (`StoreyOcclusion.cs:103-114`) | 3.9 ms and 1.7 MB for 3,007 buildings; throws away walk shapes, roof caches and footprints | keep one per site; build it on a worker at load; reuse the site's grid (the walk model duplicates it: 0.82 MB) |

Status: done but the grid (CHANGELOG). The massing's build keeps each roof's rise (`Roofs.Rise`), so the first ray test reads it. The walk model makes the shapes of the buildings within 24 m of the player, and of the neighbours of the one they are in, ahead on build threads (`PlayWorld.Prepare`): on the walk-in city's walk with the threads given a frame's time, every shape met was ready (`FirstMeetingTests`). Sink's footprints come from a shared builder (2.4 KB of garbage each) and are kept per site, 32 at most. The walk model and occlusion core are kept per site and made on a build thread; other districts' are made ahead. The walk model keeps its own grid: its cells hold the buildings in the prototype's order, which the walks' parity with the prototype depends on.

### 10.3 Footprints are hidden and shown again every frame (medium; Unity-side)

`StoreyOcclusion.Write` calls `HideFootprints()` every frame (`StoreyOcclusion.cs:157, 214-220`): `SetActive(false)` and a push onto a stack per footprint. Then each sunk occluder pops one, assigns `sharedMesh`, sets its layer and calls `SetActive(true)` (`:176, 197-200`). With two or more footprints the stack swaps which object gets which mesh, so `sharedMesh` really changes every frame.

Under the GPU Resident Drawer a renderer whose material or mesh changes is re-registered in full, processed on the main thread at PostLateUpdate [R1]. In the orbit test 67 % of frames had an occluder. Keep one persistent object per occluder slot (16 at most), and set mesh, layer and enabled state only when they change.

Status: done (CHANGELOG): an object per slot, its mesh, layer and state set only on a change.

### 10.4 Table writes per occluder (low)

Each frame `Write` clears every occluding building's slot and sets it again (`StoreyOcclusion.cs:155, 163`), which dirties its state chunk. It also rewrites the occluder's 64 texels and marks them dirty unconditionally (`:164-171`). That is about 2 KB of upload per occluder per frame, up to 32 KB with 16. Diff the occluding set and mark rows only when they change.

### 10.5 Smaller items

- `SegmentHits` has no cheap rejection: 89 % of candidate tests miss. A 2D box test against the outline's bounds rejected 88 % with no false rejections over 24,938 tests. The three rays share one plan segment, so test the plan once.
- Caches that only grow per site:
  - footprint meshes keyed by building, storey and slot: 129 meshes for 126 buildings on the orbit test, the slot in the key duplicating them;
  - the core's tier and record caches: 648 KB after 400 occluders.
  Cap them (LRU), or clear on district change.
- `PlayWorld.FloorAtY` and `SurfaceAt` are quadratic in storeys through `Derived.FloorBase`: `Locate` takes 3.9 µs in a 38-storey building.
- `Bridges.Span` builds a string key on every call (`Bridges.cs:118`), from `SurfaceAt` per bridge per walker sub-step: 40 bytes each. The fixture cities have no bridges.
- No Profiler markers in `StoreyOcclusion`, `StoreyProps` or the play kit. Add `Storey.Occlusion`, `Storey.Occlusion.Cutaway`, `Storey.Props`, `Storey.Walk` and `Storey.HUD` before the first device run (PLAY.md §5.5). Done (CHANGELOG), named as Storey's other stages are (`StoreyTimings`, which Storey Diagnostics' stall notes read too): "Storey occlusion: view and buildings in the way" and "Storey occlusion: cutaway" inside "Storey occlusion: frame" (`OcclusionCore.FrameView`, `FrameCutaway`), "Storey props: frame", "Storey walk: character", "Storey walk: camera" and "Storey hud: the place and the lift panel".
- Forced LOD0 (§3.1): a simulated 467-second run (two build threads, LOD builds at 4× and 8× the measured time, 750 and 1,170 px screens, running at 7 m/s with the camera swinging) had 0 of 69 buildings entering the way without their LOD0. The blocking build is a spawn, teleport and district-entry problem, not a continuous-play one.

### 10.6 Colliders and physics

Covered in §3.5: built on the workers, cooked with `Physics.BakeMesh` in a job, assigned with matching cooking options, capped at 64. Two notes for the project's physics settings:
- Sweep-and-prune, Unity's default broadphase, degrades when many colliders are added and removed. Unity 6.3 recommends Automatic Box Pruning for large worlds with many static colliders [CPU-5].
- Leave `Physics.autoSyncTransforms` off [CPU-5].

## 11. Props, facade details, windows and the play kit

### 11.1 Props

- **Every prop is alpha-tested** (PROPS.md §2.2 requires Alpha Clipping on every prop graph). On Apple GPUs every prop draw is then a discarding draw, the cost the buildings' steady variant exists to avoid (§7.6). `StoreyProps` already groups props per building, so it can swap a group to a non-clipping material when `Steady.Of` holds for its building, on transitions only (M).
- **Still props are not free per frame**: the loop visits every prop for a null check and an `Application.isPlaying` call (`StoreyProps.cs:185-202`). Estimated 0.1–0.3 ms per 10,000 props on an A10. Keep moving props in their own list and hoist `isPlaying`.
- **Unregistering is quadratic**: `List.Remove` per prop (`StoreyProps.cs:84`). 10,000 props took 19–148 ms to unregister and 20,000 took 65–406 ms (x64), which lands on the frame a district unloads. Swap-remove with an index stored on the entry.
- **A rebuild re-files every prop on every site**: each `SiteRenderer.AnyVersion` bump (every `Show`, end of a load, edit) re-resolves them all in one frame. Re-resolve only the props of the site that changed. Prune empty groups, and use `GetComponents(List<Renderer>)` on register.

### 11.2 Facade details and artist windows

`StoreyDetail` and `StoreyOpening` keep serialised `Mesh` references (`StoreyDetail.cs:45-47`, `StoreyOpening.cs:43-45`) that only the editor reads. In a build the meshes ship and load with every site that lists the kind, beside the baked float copy and the kind's double-precision copy (48 bytes a vertex for positions and normals). A 2,000-vertex, 3,000-triangle detail is about 84 KB baked, 132 KB as a kind, plus the Mesh asset. Make the references editor-only (or move them to an editor-side asset) and keep one runtime copy. Medium, depending on how large the detail libraries get.

### 11.3 Windows, palette and globals

`StoreyWindows.Update` sets eight globals a frame and `HexPalette.Bind` two (a few microseconds). That is fine as it is.

### 11.4 The play kit (sample-grade, but it is what PLAY.md §5.5's iPhone 7 test measures)

- **The HUD is IMGUI** (`StoreyCharacter.OnGUI`, `StoreyCharacter.cs:98-135`). It runs `Locate` and `LiftAt` on every GUI event, draws a `GUI.Button` per lift stop (up to 40), and draws the joystick every frame on mobile. In a lift car `Cores.Stops` allocates a list, closure and delegate per call: at least 3 KB a frame. Compute once per frame in `Update`, cache the stops per shaft, and move the HUD to UI Toolkit or UGUI updated on change. At least add a switch to hide it for measurement runs.
- **The silhouette clone keeps a collider.** `Destroy(body.GetComponent<Collider>())` is deferred to the end of the frame, and the body is `Instantiate`d into the silhouette right after (`StoreyCharacter.cs:57-63`). The clone keeps a `CapsuleCollider` that moves every frame. Use `DestroyImmediate` before cloning.
- **Transform writes.** `StoreyCharacter.Place` and `StoreyPlayCamera` set position and rotation separately; use `SetPositionAndRotation`.
- **Not performance.** `StoreyPlayKit.cs:44` uses `GetComponent<StoreyPlayCamera>() ?? AddComponent…`, which Unity's fake-null objects defeat in the editor. `StoreySite` and `StoreyProps` both run at execution order 1000, so props can read last frame's occlusion; use 1001.

## 12. Garbage and the managed heap

### 12.1 What a collection costs in Unity

Unity's collector (Boehm–Demers–Weiser, under Mono and IL2CPP) is non-generational and non-compacting. Each collection marks everything reachable, so its cost grows with the number of live objects and references, not just with garbage [CPU-1]. Incremental mode (on in SPEC §6.7's checklist) spreads the marking over frames but adds write barriers and can fall behind under heavy allocation [CPU-1]. Three consequences for Storey:

- **Garbage still matters at load.** The steady frame makes none (appendix A.1), but parsing a layout makes about 30 bytes of garbage per character of JSON (60 MB for the 3,000-building city, appendix A.2), and building detail makes 0.2 to 2.6 MB per building (CITY.md §7). A streamed district or a burst of LOD0 builds is several collections. Each pauses every managed thread, the build threads included.
- **Live object count is a per-collection tax.** One loaded 3,000-building layout is 188,316 live objects in 14.2 MB (appendix A.2). They are mostly the parsed model's per-floor lists: 22,637 each of `List<int>`, `List<CornerData>` and `List<Vec2>`, 19,630 `FloorData` with a `List<WallData>` each, 33,968 strings, and a `FacadeStyle` per building at 456 bytes. Every collection walks them whether or not anything changed. Flattening the runtime representation (§6.7) removes most of them.
- **Peaks set the heap's size.** The managed heap grows to fit the peak of live plus not-yet-collected garbage and returns memory to the OS slowly, if at all [CPU-1]. A district's parse peak is therefore paid for the rest of the session in the app's footprint, which is what iOS counts against its limit [CPU-8].

### 12.2 How far the headless numbers carry into Unity

The headless benches run on .NET 8 with RyuJIT; a device runs IL2CPP-compiled C++, Unity's class libraries and Boehm. We checked the class libraries against the source Unity 6.3 actually compiles (`Unity-Technologies/mono`, branch `unity-6000.3-mbe`, `mcs/class/corlib/corlib.dll.sources`) [CPU-2]:

- **Collections behave as in .NET 8.** `List<T>`, `Dictionary`, `ConcurrentDictionary`, `ArraySortHelper`, `StringBuilder` and number formatting are compiled from corefx/CoreRT. `List<T>.AddRange` and `InsertRange` copy directly, and `Sort(Comparison<T>)` allocates nothing. The zero-garbage results of `FrameAllocTests` for the engine-free code are a fair proxy.
  - Older Unity guidance, and the reference-source `list.cs` that still sits unbuilt in Unity's Mono fork, say otherwise. They don't apply to Unity 6.
- **`Enum` is still the .NET Framework reference source.** `Enum.TryParse` splits the string and boxes the result, so each call allocates on device and not on .NET 8. The layout parser calls it about 6,900 times per 3,000-building layout (`PrototypeJson.cs:509`): load-time garbage the headless numbers don't show.
- **`List<T>` objects are 40 bytes** here rather than 32 (the corefx version Unity pins keeps a `_syncRoot` field). The parsed layout's 105,652 lists are about 0.85 MB larger on device than measured.
- **`lock` is much slower under IL2CPP** (UUM-77985: 6,872 ms against 1,782 ms on Mono for the same loop; Won't Fix) [CPU-4]. Storey takes about 150 locks per LOD0 build, so this is tens of microseconds per build, not a hot spot (§9.3).
- **Thread priority is ignored on iOS.** IL2CPP's POSIX thread layer leaves `SetPriority` empty, so the "below normal" build threads run at the default QoS class beside Unity's main and render threads (§9) [CPU-4].
- **Unity's own API allocations aren't covered at all**: names, `GetComponents`, IMGUI, scene queries. Neither is anything the engine does (mesh upload, renderer registration, PhysX). Only a device profile sees those (§14).

### 12.3 What a full collection would cost on the minimum tier

Not measured on a device yet. The best proxy we have:
- **The live set.** CITY.md §7 measured 40.7 MB of C# at the default tier: layout text and parse 12.8 MB, chunks 15.4 MB, site index 3.8 MB, the rest small.
- **What the collector scans.** IL2CPP allocates arrays and objects that hold no references as atomic blocks it never scans [CPU-1]. That covers the chunks (arrays of plain structs) and the layout text. The marking work is the object graph: the parsed layout, the site index (mostly the party-wall memo, §6.6), LOD entries and the renderer's dictionaries.
- **A Boehm proxy.** The same allocation pattern run against libgc 8.2.6 on the x64 test machine: a full collection took 20–22 ms with a 30 MB game heap, and 25–28.5 ms once a 3,000-building layout's text and document were resident. One layout parse triggered one or two collections and grew the heap from 42 to 66 MB, where it stayed after everything was freed (appendix A.5).

An A10 is slower than that machine, so a non-incremental full collection is a multi-frame stall on the minimum tier. Incremental collection (3 ms slices by default, plus idle time at the end of a frame when a frame rate is set) hides it only while allocation stays low [CPU-1].

Measure in an IL2CPP player on an iPhone 7: `GC.Collect()` time after the test city has loaded, and the total of the incremental slices per collection (Profiler markers `GC.Collect` and `GC.MarkDependencies`). Then decide between incremental collection and `GarbageCollector.GCMode = Manual` with collections at loading screens and district boundaries [CPU-1].

## 13. Memory footprint

What a Storey site holds, from CITY.md §7 (headless replays) and this review's measurements:

| | Default tier, after the 5-minute walk | Minimum tier | Notes |
|---|---|---|---|
| GPU: cells | 9.8 MB | 9.8 MB | Bakeable (§6.4) |
| GPU: LOD0 | 26.2 MB (16) | about half (8) | Densest view 85.7 MB (default tier); 16 largest towers 144 MB (CITY.md §7) |
| GPU: LOD1 | 20.0 MB (215) | about half | |
| GPU: building table | 2.0 MB | 2.0 MB | |
| C#: mesh chunks and spare builders kept | 15.4 + 3.3 MB | 11.7 MB + builders | Deliberate: the cap trades memory for garbage |
| C#: layout text + parsed | 3.9 + 8.9 MB | same | Never released (§6.5); 0.8–0.9 MB as a baked blob (§6.4) |
| C#: site index | 3.8 MB | same | 3.0 MB of it the empty party-wall memo (§6.6) |
| C#: renderer, table mirrors, walk model, shapes | 5.3 MB | similar | Lazy per-building objects (§3.4) shrink the renderer's share |
| Not measured | | | Unity objects (about 3,300 GameObjects, Mesh objects), PhysX cooked meshes, GRD instance data (about 340 bytes per registered renderer on the GPU [R1]), shader variants, the managed heap's free space |

What this review adds:
- **The managed heap is bigger than its live set.** A layout parse peaks at about 34 MB live and grew a Boehm heap by 24 to 56 MB that stayed (appendix A.5). The live-set numbers above understate the app's footprint by the heap's free space.
- **Visited districts accumulate** (§6.5): about 1.3 MB per 300-building district, plus its neighbours' documents.
- **Effort-S savings for the minimum tier**: the text (3.9 MB, §6.5) and the memo (3.0 MB, §6.6) take 6.9 MB off its 37.7 MB of C#. That nearly closes CITY.md §7's gap of about 8 MB in the worst view (8 largest towers: 128 MB against the 120 MB budget). A baked layout (§6.4) and a flattened or absent parsed model (§6.7) save most of the rest.
- **Device ceilings for context**: about 1.4 GB before jetsam on a 2 GB iPhone 7 and 2.8–3 GB on a 6 GB iPhone 14 class device (community figures [CPU-8]). Storey's budgets (120 and 200 MB) are a small share of either; the risk is the rest of a game, not Storey alone.

## 14. Measuring it in Unity

Every number in the docs, and nearly every one here, is headless. The plan's exit tests (Plan §6.6, PLAY.md §5.5) need device numbers, and the decisions above (thread count, GRD on the minimum tier, shadow settings, upload budgets) need them too. The packages have no Play Mode tests yet, though the manifest already has `com.unity.test-framework.performance`.

**A performance scene.** The 3,000-building city and the 1,000-building walk-in city, driven along fixed paths:
- a street walk;
- an orbit;
- an overview;
- walking into and up the tallest building;
- a teleport;
- a district crossing.

**Counters through `ProfilerRecorder`** [CPU-12]:
- In release players: CPU Main Thread, CPU Render Thread and GPU Frame Time (FrameTimingManager, four frames late), Draw Calls, SetPass Calls, Batches, Vertices, Total Used and Reserved Memory, GC Used and Reserved Memory, System Used Memory.
- In development players only: GC Allocated In Frame, Gfx Used Memory, and Storey's markers.

**What to report and fail on.** p50, p95, p99 and the worst frame. Mean frame time hides exactly the spikes this review is about. Set thresholds per tier.

**Device runs.**
- Xcode: Time Profiler, Metal System Trace, GPU counters (for §7.1, §7.6 and §7.11).
- A Memory Profiler snapshot after the walk.
- `os_proc_available_memory()` headroom.
- The first runs of a build with the scheme's Metal API Validation and Shader Validation on (Edit Scheme ▸ Run ▸ Diagnostics). A shader's read outside a buffer reads as 0 on D3D and Vulkan, so the editor on Windows never shows it, and a Mac's may not. Metal leaves it undefined: an iPhone can take a GPU fault, and iOS then stops running the app's GPU work (artifacts, then a frozen picture). Shader Validation names the shader and the line; without it, the console says "GPU Address Fault" or "Ignored (for causing prior/excessive GPU errors)". The 1,000-building test city loaded like that on an iPhone 11 Pro while Storey's shaders still read outside the building table where a condition seemed to guard the read (since kept in range: CHANGELOG, `TableReadTests`).
- Without a Mac, **Storey Diagnostics** (*Add Component ▸ Storey*, CHANGELOG). It records the last few seconds of frames and what happened to a file. A stall's note names the stage the main thread is in and when the GPU last finished a frame. The next run shows the record with a Copy button. On iOS it also notes Metal's own error for each GPU command buffer that fails, with each of its passes' state, where Xcode's console would show it (`Unity/Plugins/iOS/StoreyMetalErrors.mm`). Error 3 is a GPU fault: a read outside a buffer, after which iOS ignores the app's GPU work. Error 10 is a pass with more geometry than its memoryless attachments allow, a camera's depth that no later pass reads and Render Graph keeps on the tile. The record lists every camera that renders and the renderers' settings, which decide that. iOS's own log is read from Windows with a tool such as iMazing's Console or libimobiledevice's `idevicesyslog`.

Status: the performance run is in (CHANGELOG): **Storey Performance Run** (the play kit's `StoreyPerformanceRun`) takes the camera, and the occlusion's player, along fixed paths once the sites have loaded. It does an overview and an orbit with the occlusion's view left out, a walk along the layout's longest street with the play camera's trailing pose, a climb of the tallest building a storey every two seconds, a teleport to the street's far end, and a crossing into the nearest other site (`Play.PerformancePaths` finds them, headless-tested). Per path it reports frame time p50, p95, p99 and worst. With Frame Timing Stats on it adds the main thread, render thread and GPU. It reports draw calls, SetPass calls, batches and vertices through `ProfilerRecorder`, and memory at the path's end. In a development player it adds garbage per frame and the 99th percentile of Storey's stages. A path passes when its p99 is under the quality asset's **P99 Frame Ms**: 20 ms on the primary tier (at most a frame in a hundred late at 60 fps), 40 ms on the minimum tier's 30 fps. The report shows on screen with a Copy button, is saved to `StoreyPerformance.txt` in the app's persistent data folder, and is noted in Storey Diagnostics' record. It is a component, not a Unity Test Framework Play Mode test: it runs in an iOS player built on Windows, where running Play Mode tests on a device needs a Mac. What's left is the runs themselves: an iPhone 7, an iPhone 14, and a desktop.

**Headless additions.** These guard the fixes:
- the back-face ray test (appendix A.3);
- a parse garbage and time budget;
- a Show-frame budget;
- the cutaway's work per storey;
- the footprint's allocation.

Status: in the suite, all but the cutaway's (§10.1, with its fix). `BackFaceTests` casts the rays. `CityBenchTests.ReadingTheCitysLayoutFitsItsBudget` reads the 3,000-building test city under 38 MB of garbage (34.5 MB) and 600 ms (133–200 ms headless). `CityBenchTests.ShowingTheCityHoldsItsFrameBriefly` keeps the Show frame's own work, with the index and the plan made ahead, under 4 ms (0.23 ms; 6.6 ms with them made in the frame). `FirstMeetingTests.AFootprintMakesLittleGarbage` keeps a footprint under 24 KB (2.4 KB).

## 15. Roadmap

Ordered so that each step is measured by the one before it. Effort: S under a day, M a few days, L a week or more.

### Phase 1: measure, and remove the hitches

| Item | § | Effort |
|---|---|---|
| Play Mode performance scene and counters; first iPhone 7, iPhone 14 and desktop runs; p99 thresholds per tier | 14 | M |
| Profiler markers in `StoreyOcclusion`, `StoreyProps`, the play kit | 10.5 | S |
| Forced LOD0 never blocks the main thread; prefetch along the camera–player line | 3.1 | M |
| Uploads budgeted per mesh part; tier budgets; `Cells.Pack` on the build threads | 3.2 | S–M |
| `Show`'s site index and load plan on the build thread; bounds and tier starts cached | 6.2 | S–M |
| Walk shapes, roof heights and footprints built ahead on the build threads; the walk model and occlusion core kept per site | 10.2 | S–M |
| Footprint objects persistent per slot, changed only on change | 10.3 | S |
| iOS build threads at QoS utility; 1 against 2 threads measured on an iPhone 7; one global thread count | 9.1 | S–M |
| Finish without a main-thread pass (rows resolved on the worker, wall base from the table); a LOD0's parts uploaded across frames | 8, 4.1 | M |
| Artist window, door and detail kinds compacted once per kind, before artist content lands | 8.1 | S–M |

### Phase 2: the GPU on the minimum tier

| Item | § | Effort |
|---|---|---|
| Discarding materials in the AlphaTest queue, steady copies in Geometry | 7.6 | S |
| Shadow coordinate per pixel with cascades; SH per vertex; reflections and specular off for Opaque and Massing on the minimum tier | 7.3 | S |
| Lean depth and shadow vertex stage; per-variant outputs; `half` interpolants | 7.2 | M |
| Massing colours resolved per row on the CPU; shift and mask instead of integer division in palette lookups | 7.4 | S–M |
| Close the shells' openings; back-face ray test in the suite; `Cull Back` on steady copies | 7.1 | M |
| Interior as its own renderer (glass clear or active building only; shadows for the player's building only); LOD1 shadows through the massing | 7.7, 7.8 | M |
| `GraphicsStateCollection` traced and warmed per tier and API; keyword stripping | 7.9 | M |
| GRD with Forward+ against the SRP Batcher with Forward, measured on the iPhone 7; per-tier URP settings documented | 7.10 | S (once measured) |

### Phase 3: bake the far city and the layout

| Item | § | Effort |
|---|---|---|
| Interim: lazy parser paths, static key sets, enum dictionary, no throwaway defaults | 6.1 | S |
| Interim: documents reference-counted and released on unload; `json` dropped in players | 6.5 | S |
| Party-wall memo: nothing stored for edges without a neighbour; kept for the site's lifetime in one map | 6.6, 8.5 | S |
| Main-thread load garbage: palette memo first, texels straight into the table, tables sized at load | 6.8 | S |
| Import bakes the binary layout, cells, parameter rows and per-district edge context; runtime parses no JSON | 6.3, 6.4 | L |
| Parsed model flattened (or replaced by the baked blob): shared empty lists, sparse floors, interned colours and styles | 6.7 | M |
| Districts through Addressables, released by reference count | 6.5 | L |

### Phase 4: throughput and scale

| Item | § | Effort |
|---|---|---|
| Build queue with priority classes; builds in storey steps with cancellation | 9.2 | M |
| Fused `MeshPack` pass; inlined `ChunkList` indexer; split path without per-vertex gathers | 8.2 | S–M |
| Builder fast paths for quads and boxes; per-face normal and colour; struct comparers | 8.3 | M |
| Rectilinear wall-face triangulator | 8.4 | M |
| Roof parts kept per site; integer gable keys; LOD1's openings once per storey | 8.5 | S–M |
| Wall ids interned, created only when used | 8.6 | S–M |
| Steady-garbage long tail; `StyleAt` memoised; collision unions memoised; float builder positions | 8.7 | S–M |
| Burst kernels for packing, cell packing and welding; then quad and box expansion | 8.8 | M–L |
| Cutaway filtered to the active storey ±1, array-based | 10.1 | M |
| Lazy building roots; pooled LOD part objects; `hierarchyCapacity` | 3.4 | M |
| `Mesh.AllocateWritableMeshData` written on the build threads | 4.1 | M |
| Event-driven steady and cell-visibility updates | 3.3 | S |
| Props: steady material swap, moving-only loop, swap-remove, per-site re-resolve | 11.1 | M |
| Artist meshes editor-only at runtime | 11.2 | M |
| Vertex compression (quantised positions, 16-bit ids), only if the minimum tier's memory binds | 4.2 | M |
| Play kit HUD on UI Toolkit or UGUI, cached lift queries; silhouette collider | 11.4 | S–M |

## Appendix A. Measurements made for this review

All headless (.NET 8.0, Release, on a 4-core 2.8 GHz Xeon container). Several measurements ran alongside each other, so times are noisy; byte counts, object counts and the ray test are not. Unity's runtime (IL2CPP, Boehm GC, Mono class libraries) and the GPU were not available.

### A.1 Existing benches

`dotnet test unity/Headless/Triband.Storey.Tests -c Release --filter "FullyQualifiedName~FrameAllocTests|FullyQualifiedName~MemoryBenchTests"`:

| Test | Result |
|---|---|
| `AFrameMakesNoGarbage` | walking 0 B, occlusion 0 B, LOD 2 B a frame; 68 of 1,200 frames with buildings in the way |
| `WalkingInAndUpTheStairsMakesNoGarbage` | 0 B a frame over 2,430 frames, 1,151 inside a building |
| `AFinishedBuildWaitsAtTheSizeOfItsMeshes` | 0.69 MB packed (1.61 MB as the builder held it); GPU meshes 0.66 MB |
| `ABuildingAtLod0KeepsOnlyItsShape` | 16 largest: 38.5 KB a shape (0.6 % of 6.02 MB GPU); spread of 251: 8.7 KB (1.2 % of 0.74 MB) |
| `BuildingOneAfterAnotherReusesTheMemory` | 2.50 MB allocated cold, 0.23 MB warm; 12.5 MB of chunks kept |
| `ALoadHoldsAFewCellsMassingsAtATime` | biggest cell's 24 massings 411 KB; all massings 52.4 MB; made again in 2.5 ms |
| `TheWalkModelStaysSmallOnAFiveMinuteWalk` | 14 shapes, 1.35 MB; **worst frame 27.4 ms** (fails its 25 ms bound under load; see §8) |

### A.2 Heap census of a loaded layout

Harness: the 3,000-building test city written to text, then parsed and indexed as a site does, then `dotnet-gcdump`.

| | |
|---|---|
| Buildings | 3,007 |
| Text | 2,031,538 characters, 3.9 MB as a .NET string |
| Parse (`PrototypeJson.Read`) | 366–433 ms (contended), **60.3 MB allocated**, 8.9 MB retained |
| Site index (`new Site`) | 24–31 ms, 1.2 MB allocated, 0.8 MB retained at construction |
| Live heap: text + parsed layout + site | **188,316 objects**, 14.2 MB |
| Largest object populations | 33,968 `string`; 22,637 each of `List<int>`, `List<CornerData>`, `List<Vec2>`; 19,630 `FloorData` + 19,630 `List<WallData>`; 4,624 `List<BuildingData>` (site grid); 3,013 `FacadeStyle` (456 B each); 3,011 `EntranceData` |

### A.3 Back-face exposure

Harness: for every building of `demo.json` (7) and `variants.json` (78), the opaque mesh of each LOD (`Lod0.Build(...).Op`, `Lod1.Build`, `Lod2.Build`). Random eyes 2 to 60 m outside the building's bounds, half at street level, half up to 40 m above the roof, aimed at random points in its bounds; first hit by ray cast (uniform grid, Möller–Trumbore). Glass is left out, so rays see through windows as a camera does. Front faces wind as `cross(b − a, c − a)`; no triangle's winding disagrees with its stored normal.

| | LOD0 | LOD1 | LOD2 |
|---|---|---|---|
| Triangles facing away from outside eyes | 49.8–50.2 % | 49.2–50.6 % | 53.5–53.7 % |
| First hits on a back face, variants | 0.26–0.29 % (39–47 of 78 buildings) | 0.08 % (21–27) | 0.07 % (8) |
| First hits on a back face, demo street | 2.3–2.7 % (5–6 of 7) | 4.0–4.1 % (4–5) | 0.9 % (3) |

Since the shells were closed (§7.1), `BackFaceTests` finds none at any LOD on the demo street, the variants or a 40-building walk-in city: 4,000 rays a building, with LOD0 seen near (rooms in, glass clear) and far (rooms out, panes painted over), slivers aside. A sweep of 12,000 a building with another seed found one ray, 1.3° off a wall's face, slipping through the seam of two tiers' coplanar walls.

### A.4 Wall segments per floor (the walk model's collision query)

`Lod0.BuildShape` for 200 sampled buildings of the 1,000-building cities: plain 5 segments a floor on average (95th percentile 8, most 19), walk-in 14 (17, 20). A segment is 40 bytes.

### A.5 Loading, parsing and threads

Harness: the engine-free half of `Show`, `TakeLoad`, `Build`, `BuildCell` and `MassingOf` replayed frame by frame with a 6 ms budget and the parse on the build threads; `DOTNET_TieredCompilation=0`.

| Build threads @ fps | Load frames | Every cell shown | Show frame | Load frame p50 / p90 | Main thread in all |
|---|---|---|---|---|---|
| 3 @ 60 | 14–15 | 0.33–0.35 s | 18–19 ms | 6.0 / 6.5 ms | 83–92 ms |
| 2 @ 60 | 23–29 | 0.50–0.75 s | 22–34 ms | 2.3–3.5 / 6.1–7.8 ms | 99–208 ms |
| 2 @ 30 | 23–27 | 0.87–0.96 s | 18–21 ms | 3.4 / 6.1–7.3 ms | 101–133 ms |
| 1 @ 60 | 62–65 | 1.12–1.27 s | 19–24 ms | 1.2 / 1.8–2.3 ms | 90–216 ms |
| 1 @ 30 | 60–61 | 2.16–2.23 s | 20–28 ms | 1.2 / 2.2–2.4 ms | 205–251 ms |

The same harness after §6.2 (the site index and plan on the build threads) and §3.2 (the budget per tier), two runs each:

| Budget, build threads @ fps | Load frames | Every cell shown | Load frame p50 / p90 / max |
|---|---|---|---|
| 6 ms, 2 @ 60 (before) | 20 | 0.39–0.51 s | 2.3–3.9 / 6.2–6.5 / 6.3–10.9 ms |
| 2 ms, 2 @ 60 (primary tier) | 39 | 0.70–0.72 s | 2.1 / 2.3–2.6 / 2.9–3.3 ms |
| 3 ms, 2 @ 60 | 27–29 | 0.56–0.62 s | 2.3–3.1 / 3.1–3.3 / 3.4–3.7 ms |
| 6 ms, 3 @ 60 (before) | 14–16 | 0.33–0.39 s | 6.0–6.1 / 6.2–6.3 / 7.7 ms; one run had a 109 ms frame |
| 2 ms, 3 @ 60 | 30–34 | 0.55–0.62 s | 2.1 / 2.2–2.3 / 2.5–2.9 ms |
| 6 ms, 2 @ 30 (before) | 20–22 | 0.80–0.89 s | 2.3–2.6 / 6.0–6.1 / 6.1–6.3 ms |
| 4 ms, 2 @ 30 (minimum tier) | 24–25 | 0.86–1.05 s | 3.1–3.3 / 4.4–5.8 / 4.9–7.4 ms |
| 2 ms, 2 @ 30 | 32–35 | 1.13–1.29 s | 2.0–2.1 / 2.3 / 4.3 ms; one run had a 96 ms frame |

The two single slow frames are as long as the collections inside load frames measured below (82–106 ms); the harness didn't record their cause.

| Measurement | Result |
|---|---|
| `Json.Parse` (DOM only), 3,000 buildings | 25.6 MB allocated, 35 ms min, DOM 21.4 MB live |
| `PrototypeJson.Read` | 60.3 MB allocated (8.2 MB for 300 buildings), 94 ms min / 115–139 ms median (16–17 ms for 300), document 8.85 MB |
| Parse allocations by type | strings 21.6 MB (about 14.7 MB of it path strings), dictionary entries 7.9 MB, `string[]` 3.9 MB, boxed `Int32` 2.3 MB, `FacadeStyle` 2.9 MB, key collections 1.2 MB |
| The JSON | 47,017 objects, 34,743 arrays, 149,027 keys (51 distinct names), 40,175 string values, 56,671 numbers, 12,166 bools |
| Load, all threads, excluding the parse | 104–125 MB allocated; main thread 16.1–16.4 MB |
| A collection inside a load frame | 4 of 12 runs, 82–106 ms frames |
| Site index after a load | 3.88 MB, of which the party-wall memo 2.99 MB (3,007 inner dictionaries, 14,928 entries, 4 ranges) |
| `Site.WithContext`, 751 buildings with 2,256 around | 13.0–17.6 ms (2.8–4.7 ms without context) |
| Context copy (JSON round trip) | 292 µs and 29 KB per generated building; 835 µs and 83 KB per hand-made one |
| Ten 300-building districts visited | 12.69 MB held (1.27 MB each) |
| `Built` per building | 912 bytes |
| Job overhead | 128 B per job; wake-up 37–41 µs median, 86–120 µs p95 |
| Boehm proxy (libgc 8.2.6, x64) | full collection 20–22 ms with a 30 MB heap, 25–28.5 ms with a 3,000-building layout resident; one parse grew the heap 42 → 66 MB (30 MB game heap) and 106 → 162 MB (80 MB), and it stayed |

### A.6 Occlusion and the walk model

Harness: `OcclusionCore`'s phases timed separately, walks and orbits replayed through the real walk model; .NET 8, Release, tiered compilation off. Table in §10; additionally:

| Measurement | Result |
|---|---|
| Cutaway, 1,105-wall 34-storey building | 0.13–0.18 ms mean; 116.8 slide entries on average (346 max), 3.4 on the active storey; filtered to the active storey ±1: 0.008–0.015 ms |
| Cutaway profile | `HashSet.Clear` 30 %, dictionary lookups 18 %, set adds 17.5 %, `(string, int)` hashing 17 %, the wall test 10.7 % |
| Pitched roof's first ray test | 0.10–0.20 ms mean, max 0.3–5.5 ms, 53–55 KB |
| Sink footprint | `Build` 0.06–0.10 ms (max 1.1–2.2 ms), 66 KB; `Pack` 0.02–0.03 ms, 17 KB |
| New walk model + occlusion core | 3.9 ms and 1.69 MB for 3,007 buildings; 1.0 ms and 0.59 MB for 1,007 |
| Prop removal (`List.Remove` pattern) | 10,000 props 19–148 ms; 20,000 props 65–406 ms |
| Forced-LOD0 simulation | 0 of 69 entries into the way without LOD0 (two threads, builds at 4× and 8×, 750 and 1,170 px, 467 s at 30 fps) |

### A.7 Detail generation

Harness: the repository's `Runtime/` compiled into a console app (netstandard2.1, C# 9, Release), run on .NET 8, building every 60th plain-city building and every 20th walk-in one, 51 each, through the renderer's own sequence. CPU profiles come from a ptrace sampler at 600 Hz that interrupts at any instruction and symbolises through the runtime's perf map, all Tier-1 code. The EventPipe sampled-thread-time profile only stops threads at safe points and inflated `Lender` and `Chunks.Rent` several-fold. Allocation is attributed by GC allocation ticks per isolated phase, within 1–3 % of the byte totals.

| Profile | Top entries (% of samples) |
|---|---|
| LOD0 plain, exclusive | `ChunkList<int>.Add` 10.3, `Quad` 9.9, `OBox` 9.8, `V` 8.4, `Earcut.IsEar` 4.6, `Earcut.EliminateHoles` 4.2, `Dictionary<P3,int>.FindValue` 3.9, `PolyTris` 3.8, `HashCode.Combine` 3.2, `Facade.Dress` 3.1 |
| LOD0 plain, inclusive | `FacadeStorey` 83.2, `Facade.Dress` 39.0, `OBox` 35.2, `WallPanel` 34.2, `WallPanel.Face` 23.7, `Quad` 21.8, `Earcut.Tessellate` 18.2, `Details.Build` 9.8 |
| LOD0 walk-in, inclusive | `FacadeStorey` 79.5, `WallPanel` 40.8, `OBox` 32.8, `Dress` 32.0, `Face` 31.9, `Earcut.Tessellate` 25.6, `Quad` 22.3, `Shafts` 10.0 |
| LOD1 plain, inclusive | `Details.Build` 24.9, `Details.At` 17.0, `Roof` 15.7, `Facade.Ops` 12.1, `Party.WallPieces` 11.4, `Roofs.Make` 11.1, `Party.Ranges` 9.5, `Facade.PaneUV` 8.1 |
| LOD1 build + weld + pack | `Pack` 48.6, of which `Weld` 33.3 of the total; inside `Pack`: `ChunkList<int>.get_Item` 32.0, `Weld` 29.4, `HashCode.Combine` 12.8 |

| Measurement | Result |
|---|---|
| Per-LOD0 call counts, plain / walk-in | `V` 19,599 / 39,425; `Quad` 4,608 / 8,988; `OBox` 697 / 1,408; `EntryOf` 39,198 / 78,851 (12.7 / 12.1 % dictionary misses); Earcut runs 40 / 113 |
| Fused `Pack` prototype | 35.4 → 18.5 ns a vertex; 303 of 303 meshes byte-identical |
| `ChunkList` indexer with a throw helper | LOD1 −9 %, pack loop −12 %, split path −28 to −35 % |
| `PackedMesh.Finish` (main thread) | 5.6–6.6 ns a vertex: mean 0.12 ms plain, 0.27–0.30 ms walk-in; 1.0–1.3 ms for the largest plain buildings, 1.5–5.4 ms for the largest walk-in ones |
| `Cells.Pack` (main thread), whole city | 12–20 ms warm, 30–63 ms cold; 45 ns a vertex full-float, 71–76 ns half |
| Wall ids per LOD0, plain / walk-in | 116.6 for 37 distinct, 42.4 unused / 234.7 for 119 distinct, 23.4 unused |
| Thread scaling, LOD0 + LOD1 packed, wall time per building | 3.45 ms (1 thread), 1.93 (2), 1.19–1.37 (3), 1.05–1.49 (4) |
| Lock traffic per LOD0 | 66 chunk rents and returns, 13 `Lender` borrows, 4 spare-builder locks; no contention |
| Skeleton by corner count | 4: 0.026 ms, 12 KB; 12: 0.05 ms, 48 KB; 36: 0.21 ms, 201 KB; 68: 0.51 ms, 523 KB |
| `Lod0.BuildShape` | 0.33–0.41 ms mean, 1.5–4.4 ms max, 96 KB (plain); 0.57 ms, 5.6 ms, 171 KB (walk-in) |

## Appendix B. Sources

Unity's documentation site was not reachable from the review's sandbox. Statements about it come from search extracts of the cited pages, and engine behaviour was checked in Unity's own source where possible.

Unity source checked:
- `Unity-Technologies/Graphics`, branch `6000.3/staging`: URP, the GPU Resident Drawer.
- `Unity-Technologies/UnityCsReference`, branch `6000.3`.
- `Unity-Technologies/mono`, branch `unity-6000.3-mbe`: `mcs/class/corlib/corlib.dll.sources`, `referencesource/mscorlib/system/enum.cs`.
- `mono/corefx` `List.cs` at the pinned commit.
- Unity's `bdwgc` fork.
- libil2cpp 6000.3, through a third-party mirror; the same files ship in the editor under `il2cpp/libil2cpp`.

**[R] Rendering**

| Ref | Topic | Sources |
|---|---|---|
| R1 | GPU Resident Drawer | docs.unity3d.com/6000.3/Documentation/Manual/urp/gpu-resident-drawer.html; …/urp/make-object-compatible-gpu-rendering.html; Graphics `GPUResidentDrawer.cs`, `InstanceCuller.cs`, `RenderersParameters.cs`; docs.unity3d.com/6000.4/Documentation/Manual/urp/gpu-resident-drawer-performance.html |
| R2 | GPU occlusion culling | docs.unity3d.com/6000.2/Documentation/Manual/urp/gpu-culling.html; Graphics `OcclusionCullingCommon.hlsl`, `UniversalRendererRenderGraph.cs` |
| R3 | `beginContextRendering` | Graphics `UniversalRenderPipeline.cs`; docs.unity3d.com/ScriptReference/Rendering.RenderPipeline.Render.html |
| R4 | Mesh API | docs.unity3d.com/6000.3/Documentation/ScriptReference/Mesh.MeshData.html; …/Mesh.UploadMeshData.html; …/Rendering.VertexAttributeDescriptor.html; developer.apple.com/metal/Metal-Feature-Set-Tables.pdf; github.com/Unity-Technologies/MeshApiExamples |
| R5 | `GraphicsBuffer` | docs.unity3d.com/6000.0/Documentation/ScriptReference/GraphicsBuffer.LockBufferForWrite.html; UnityCsReference `GraphicsBuffer.bindings.cs` |
| R6 | Apple GPUs | developer.apple.com/videos/play/wwdc2020/10632/, /10602/, /10603/; developer.apple.com/videos/play/wwdc2016/606/; developer.apple.com/documentation/metal/tailor-your-apps-for-apple-gpus-and-tile-based-deferred-rendering; docs.unity3d.com/ScriptReference/SystemInfo-hasHiddenSurfaceRemovalOnGPU.html |
| R7 | URP shader authoring | docs.unity3d.com/Manual/SRPBatcher-Materials.html; docs.unity3d.com/Manual/dots-instancing-shaders.html; docs.unity3d.com/Manual/urp/urp-shaders/urp-shaderlab-pass-tags.html; Graphics `Shadows.hlsl`, `GlobalIllumination.hlsl`, `ForwardPlusKeyword.deprecated.hlsl` |
| R8 | Variants and warm-up | docs.unity3d.com/Manual/shader-prewarm.html; docs.unity3d.com/Manual/shader-conditionals-choose-a-type.html; docs.unity3d.com/Manual/shader-variant-stripping.html |
| R10 | Rendering paths | docs.unity3d.com/Manual/urp/rendering-paths-comparison.html; docs.unity3d.com/6/Documentation/Manual/urp/urp-universal-renderer.html |
| R11 | Transparents | Graphics `DrawObjectsPass.cs`; docs.unity3d.com/Manual/urp/configure-for-better-performance.html |
| R12 | STP | docs.unity3d.com/6000.2/Documentation/Manual/urp/stp/stp-upscaler.html; Graphics `Runtime/STP/STP.cs` |

**[CPU] Runtime**

| Ref | Topic | Sources |
|---|---|---|
| CPU-1 | Garbage collection | docs.unity3d.com/6000.3/Documentation/Manual/performance-incremental-garbage-collection.html; docs.unity3d.com/6000.4/Documentation/Manual/performance-managed-memory-introduction.html; docs.unity3d.com/ScriptReference/Scripting.GarbageCollector-incrementalTimeSliceNanoseconds.html; docs.unity3d.com/6000.3/Documentation/ScriptReference/Scripting.GarbageCollector.GCMode.html; libil2cpp `vm/Array.cpp`; bdwgc `darwin_stop_world.c` |
| CPU-2 | Class libraries | docs.unity3d.com/6000.3/Documentation/Manual/dotnet-profile-support.html; the mono and corefx sources above |
| CPU-3 | `[ThreadStatic]` | docs.unity3d.com/Manual/unity-attributes.html; issuetracker.unity3d.com/issues/e-document-threadstatic-attribute-must-not-be-used-i-will-cause-crashes; libil2cpp `vm/Liveness.cpp` |
| CPU-4 | Threads on iOS | libil2cpp `os/Posix/ThreadImpl.cpp`; developer.apple.com/forums/thread/709257; apple.com/newsroom/2017/09 (A11's performance controller); developer.apple.com/videos/play/tech-talks/110147/; issuetracker UUM-77985; unity.com/blog/engine-platform/improving-job-system-performance-2022-2-part-2 |
| CPU-5 | Physics | docs.unity3d.com/ScriptReference/Physics.BakeMesh.html; docs.unity3d.com/6000.3/Documentation/Manual/physics-optimization-cpu-mesh-cooking-options.html; docs.unity3d.com/6000.3/Documentation/Manual/physics-optimization-cpu-broad-phase.html; NVIDIA PhysX 4.1 guide (Geometry, Scene Queries) |
| CPU-6 | Transforms | docs.unity3d.com/ScriptReference/Transform-hierarchyCapacity.html; unity.com/blog/engine-platform/best-practices-from-the-spotlight-team-optimizing-the-hierarchy |
| CPU-7 | Scene streaming | docs.unity3d.com/ScriptReference/Application-backgroundLoadingPriority.html; docs.unity3d.com/2022.1/Documentation/ScriptReference/SceneManagement.SceneManager.UnloadSceneAsync.html; docs.unity3d.com/Packages/com.unity.addressables@2.7/manual/memory-assetbundles.html |
| CPU-8 | iOS memory | developer.apple.com/documentation/xcode/identifying-high-memory-use-with-jetsam-event-reports; developer.apple.com/documentation/os/os_proc_available_memory |
| CPU-9 | `OnGUI` and update overhead | docs.unity3d.com/ScriptReference/MonoBehaviour.OnGUI.html; unity.com/blog/engine-platform/10000-update-calls |
| CPU-10 | IL2CPP | docs.unity3d.com/6000.0/Documentation/Manual/il2cpp-runtime-checks.html; docs.unity3d.com/ScriptReference/Il2CppCompilerConfiguration.html; dougallj.github.io/applecpu/firestorm-simd.html |
| CPU-11 | Burst and mesh jobs | github.com/Unity-Technologies/MeshApiExamples; docs.unity3d.com/Packages/com.unity.burst@1.8/manual/csharp-type-support.html |
| CPU-12 | Measuring | docs.unity3d.com/6000.3/Documentation/Manual/profiler-counters-reference.html; docs.unity3d.com/6000.3/Documentation/Manual/frame-timing-manager-counter-reference.html; docs.unity3d.com/Packages/com.unity.test-framework.performance@3.6/manual/index.html |
