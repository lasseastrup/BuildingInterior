# City scale (workstream 7)

How Storey draws a few thousand buildings: a test city to measure with, the automatic LOD, and the merged far cells. It's a port of the prototype's LOD system (SPEC §6.2, §6.3). Its numbers are kept, so a run in Unity can be compared with one in the prototype.

## 1. The test city

`TestCity.Generate(doc, count)` (Runtime/Edit) fills the layout with generated blocks around the hand-made buildings and keeps 12 m clear round each of them.

- Buildings are taller towards the middle.
- Most are shells. About 6% of those with 12 storeys or fewer are walk-in buildings with a stair core.
- Low blocks often get pitched roofs.
- It is the prototype's `cityBuildings`, number for number: the same seeded generator (20260928) and the same calls in the same order. `TestCityTests` checks every one of the 3,000 buildings against `Fixtures/city.json`.
  - To make that file: `cd prototype/tools && npm run city`.
- Generated buildings are marked `gen`. Making a city again replaces them. `TestCity.Clear` removes them.

In the editor: select a Storey Site, then **Tools ▸ Storey ▸ Test City ▸ Make 300 / 1,000 / 3,000 Buildings**. It asks before adding them. **Make … Walk-in Buildings** makes the same city with every building walk-in: switchback stairs from the ground to the top where they fit (about 95% of buildings or more), and a lift beside them above five storeys. Use it to test interiors, LOD0 and the occlusion at scale. **Remove Test City** takes them out. It's in a menu rather than the site's inspector so nobody adds thousands of buildings by accident. The city is an edit like any other, so it is saved with the layout and can be undone.

## 2. The automatic LOD

A Storey Site's **LOD mode** is *Automatic* by default; *Fixed* shows one LOD everywhere. `StoreyStreet`, the parity harness, stays fixed.

`LodManager` (Runtime/Lod) is engine-free. Each frame it picks every building's LOD from the camera:

| LOD | What | Shown when one metre covers at least |
|---|---|---|
| 0 | Full detail: interiors, frames, furniture | 16 px |
| 1 | The shell, with windows | 4 px |
| 2 | The massing, drawn in its cell | 0.3 px |
| 3 | Not drawn (also beyond 1,500 m) | |

- **Feature scale, not screen size.** The pixel count is taken at the building's nearest point. Frames and furniture only matter up close, however big the building is. At 1080p with a 60° field of view, LOD0 is within about 58 m and LOD1 within about 230 m.
- **Hysteresis of 12%** stops buildings flickering at a boundary.
- **Squares passed over.** The manager keeps the buildings in 128 m squares. A square whose buildings all show their massing, or nothing, and can't change this frame is passed over whole: each building's nearest point is between the square's nearest and farthest, and neither crosses a threshold. On the test city most of the city is such squares, so a frame looks at a few hundred buildings, not 3,000; `LodSquaresTests` checks the picks are exactly those of looking at every building.
- **Changes cross-fade** (dithered) over 0.35 s. The shader hides a building's other LODs through its row in the building table (shown, fading from, fade), so nothing is enabled or disabled per building.
- **Windows don't change at the switch.** LOD1's windows are opaque panes showing a painted room; LOD0's, in a building with an interior, are see-through glass onto the real rooms. LOD0's glass starts as LOD1's pane (the same painted room, opaque) at the distance LOD0 takes over (`LodSettings.Lod0Distance`) and clears to glass by **Windows Clear At** (0.6) of that distance, blended by each pixel's distance (`_StoreyGlassFill`). So the cross-fade swaps the geometry while the windows look the same, and the rooms behind them fade in as the camera comes closer.
- **Loading.** Only the massings are built when a layout loads, cell by cell, each cell merged as soon as its buildings are built. The worker threads build the massings a few cells ahead of the one the main thread is writing and merging (`LayoutLoad`).
  - In Play mode a site's first load (a district streamed in, a scene starting) goes over several frames, nearest cells first, within the build budget (one a frame for every site together: districts loading at once share it), so it never holds a frame: the buildings appear as their cells come (**Storey Site** `LoadOverFrames`, on by default; `Loading` while it goes on, and the stats overlay counts the cells to go). Turn it off to have every building at once, behind a loading screen say.
  - The layout's text, and its neighbours' (for the party walls along the edge), is parsed on the build threads first (`StoreyDocumentAsset.DocumentOffMainThread`, `LayoutParse`). The site waits for it, `Loading` meanwhile and with no `PlanArea` yet. Parsing the 3,000-building test city's text takes 0.1–0.2 s and makes 60 MB of garbage. That held the frame the site started in, a hitch whenever a district streamed in. Headless, the frames now go on while it parses (11 at 60 fps), and the first cells show up to 0.1 s later. Edit mode, and Play mode with `LoadOverFrames` off, parse at once.
  - The build threads make the massings four cells a thread ahead of the cell taken (`LayoutLoad.AheadPerThread`), so each frame takes as many cells as its budget allows. Headless, with the cells packed straight into the upload's layout and the massings reused (§7), the 3,000-building test city loads in 20–27 frames: every cell shows after 0.71–0.73 s with two build threads at 60 fps (0.63–0.68 s with three) and 0.96–1.06 s at 30 fps on the minimum tier, and the first view's detail is done after 1.36–1.47 s and 1.33–1.36 s. With one cell a thread ahead, a frame took the few that were ready and the threads sat idle the rest: about 130 frames, with every cell and the first view's detail done after 2.4–2.5 s and 4.5–4.6 s. A load's frames now spend up to the build budget on it (**Build Budget Ms**, 6 ms), where they spent 1–3 ms.
  - After an edit, and in the editor, a load is done at once, so nothing vanishes for a frame.
- **Detail is built on demand.**
  - A building that wants LOD0 or LOD1 asks for it, and the ones covering the most pixels go first: forced buildings, then those in the camera's view (its frustum, with a margin of a fifth of the building's radius and 6 m), then the rest. A building out of view gets no LOD0 built: it shows its shell until it comes into view (`LodView`; docs/PERFORMANCE.md §9.2). The prototype orders its queue the same way.
  - It shows the best LOD it already has meanwhile.
  - The meshes are generated on Storey's own build threads (`BuildThreads`), one building at a time each. The main thread only uploads them, for up to 6 ms a frame (at least one when any is ready).
  - The build threads are long-lived background threads named "Storey build 1, 2, …", below normal priority where the platform allows it, and registered with Unity's Profiler: their timeline shows `Storey.Generate LOD0`, `Storey.Generate LOD1`, `Storey.Pack` and `Storey.Collision mesh`. How many is the quality asset's **Build Threads**; automatic is two on a phone or tablet and one fewer than the cores, at most four, on a desktop. They aren't Unity's job workers: a build takes several milliseconds, longer than Unity's guidance for a job, and the job workers run the engine's own culling and physics. Waiting for a build that hasn't started (a forced building, a layout loaded at once) runs it on the waiting thread.
  - A result for a layout or building that has changed since is thrown away, as is one no longer wanted.
  - A building's collision mesh is made on the worker with its first LOD0, when colliders are on.
  - A building that must have LOD0 at once (see Forced buildings below) while a worker is already building it waits for that build rather than starting another.
  - With **Build on worker threads** off, and always on WebGL, building happens on the main thread and stops after 6 ms a frame (at least one is always built).
  - **The steady variant.** Every Storey shader can discard a pixel: the LOD cross-fade's dither, the cutaway, the active floor's ceiling, the buildings in the way, the isolate ghost. Apple's GPUs then treat a building like alpha-tested foliage and can't hide its pixels behind others before shading them. So the opaque and massing shaders have a variant without any of it (`STOREY_STEADY`: no discard, no Sink), and each building's renderers draw with a copy of the material that turns it on while nothing can hide their pixels: the building isn't cross-fading, no other building is isolated, it isn't in the way, and it isn't the player's building or one carrying a party wall with it (`Occlusion.Steady`; a LOD0 mesh lists those neighbours as it is packed, `PackedMesh.Mates`). A cell is steady while all its buildings are. The renderers are switched just before the cameras render, once every site's frame has written the building table and the view (`RenderPipelineManager.beginContextRendering`); the copies take their materials' properties again twice a second in Play mode, at once in edit mode. With **Occlusion On Far Materials** off (the minimum tier), massings in the way stay steady too: far buildings aren't cut or faded, and far geometry never discards. The shaders queue at AlphaTest (for the discarding variant) and the steady copies at Geometry, so every steady draw comes before every discarding one, the order Apple's GPUs need for hidden-surface removal to cover the steady majority (docs/PERFORMANCE.md §7.6).
  - The generator's memos and caches are thread-safe. `ParallelBuildTests` checks that building on 8 threads gives exactly the meshes that building one after another does.
- **Residency.** A building's LOD0 and LOD1 meshes are kept after it stops showing them, up to 16 LOD0s and 260 LOD1s held in all; past that, the least recently shown of those not shown are dropped. The caps don't limit the ones shown: every building near enough for LOD0 shows it (the LOD0 distance comes from **LOD0 Pixels Per Metre** and the screen's height), so a dense view holds more (headless, 64 small buildings packed round the camera all show LOD0 with a cap of 8). A mesh kept but not shown is switched off, not drawn. A building's collider comes with its first LOD0 and stays after that LOD0 is dropped, so it isn't cooked again as the building comes and goes. Past 64 (**Colliders Kept** in the quality asset; `LodSettings.MaxColliders`, `LodManager.OldestDetail`), those whose LOD0 went longest ago lose theirs, made again with their next LOD0; one showing its LOD0 always keeps it. They piled up for the session before: headless, a 30-minute walk through the walk-in test city left 125 colliders (1.4 MB of meshes, besides PhysX's cooked copies, which aren't measured), and keeps 64 now (0.6 MB), with the same LOD0 builds.
- **Forced buildings** always have LOD0. It is built at once when needed, never shown as a massing:
  - in the editor, the selected building and the one being edited (`SiteView.focusId`, `activeId`);
  - in Play mode, the building the player is in and the buildings in the camera's way (the occlusion system's);
  - whatever a project adds to `StoreySite.keepDetail`.
- **Edits.** An edited building gets back at once the detail it showed. A full rebuild (adding or removing a building) does the same for every building in view, so nothing fades through its massing.
- **The camera.**
  - In Play mode: `StoreySite.lodCamera`, or the main camera when that is empty.
  - In the editor: the Scene view's. The authoring package asks for an editor update when that camera moves, and while a fade or a build is pending.
  - An orthographic camera is treated as a perspective one 200 m behind it, so detail follows its zoom.
- **The numbers** are on `StoreyQualitySettings` (*Level of detail*). Give a site one through **Quality**; without one it uses the defaults above. Changes apply while running.

## 3. Cells

Every building's LOD2 massing goes into a 128 m square of the ground (`Cells`, by the centre of its bounds) and is merged into one mesh per cell. That makes the far city one draw call a cell.

- **Tags.** Each vertex keeps its building's tag, so the shader hides one building inside a cell while it shows a finer LOD.
- **When a cell is rebuilt.** Only when one of its buildings changes, never for an LOD change.
- **Drawing.** A cell's renderer is enabled while any of its buildings shows LOD2.
- **Shadow proxies.** A building at LOD1 casts its shadow through its massing in the cell (43 triangles against LOD1's 980, with the same outline and heights); LOD1's own renderers cast none. The massing's shadow caster pass treats LOD1 and LOD2 alike (whole between them, dithered against LOD0 and culled), and a cell whose buildings show LOD1 but none LOD2 draws into the shadow maps only (`ShadowCastingMode.ShadowsOnly`). LOD0 keeps its own shadow: the interiors' light depends on it (docs/PERFORMANCE.md §7.8).
- **Size.** A cell's mesh is split to stay under 65,535 vertices (16-bit indices), never through a building.
- **The test city** of 3,000 falls into 307 cells (the prototype has 316). Each fits in one mesh, 263,000 vertices in all.

**Table sizes.** Each massing needs a parameter row per run of storeys in the building table: the test city needs about 9,000. The parameter and colour-row tables double when they fill. Building indices are still capped at 8,192 a layout.

## 4. What it costs

Headless numbers (`CityBenchTests`, `LodManagerTests`). They are CPU only, without the GPU upload, measured on the CI container:

| | Time |
|---|---|
| Loading 3,000 buildings: every massing | 0.1–0.2 ms a building, built on the workers ahead of the main thread, which works about 50 ms of the load (writing and merging) instead of about 300 ms (4 cores). In Play mode a site's first load is spread over frames within the build budget: 20–27 frames for this city (§2) |
| Merging the 307 cells | 43 ms |
| Picking every building's LOD | about 0.05 ms a frame walking the street (Release build; 0.16 ms when every building was looked at every frame) |
| One LOD1 | about 6–8 ms |
| One LOD0 | about 23–34 ms on average, 100–150 ms at worst (tall walk-in buildings) |
| LOD0 and LOD1 on 3 worker threads | about 9 ms a building, off the main thread |
| Committing one edit (finding what changed, the layout's text) | about 140 ms (was 350) |
| One step of a drag | about 0.1 s (was 1.4 s: the whole layout was read again each step) |

The time for one LOD0 is several frames' budget. That's why the generating is on worker threads, so the main thread pays only for the upload. The workers also pack the meshes into the upload's vertex format (`MeshPack`). The main thread fills in each vertex's colour row and wall id, which only it knows, and hands the arrays to Unity: 0.13 ms a building on the 3,000-building test city, where packing on the main thread took 2.5 ms (worst 42 ms). How long Unity's own copy and upload take is not measured yet. If it shows up as a hitch, the next step is `Mesh.AllocateWritableMeshData` on the workers.

**The two test cities, 1,000 buildings each** (headless, one thread):

| | Plain | Walk-in |
|---|---|---|
| Walk-in buildings | 42 (4%) | 1,000, all with stairs, 746 with a lift |
| LOD0 triangles a building (average / 95th percentile / most) | 18,300 / 48,500 / 142,000 | 24,500 / 62,600 / 178,000 |
| LOD0 build a building (average) | 22–24 ms | 29–38 ms |
| LOD0 GPU memory, 16 resident at the 95th percentile | 48 MB | 60 MB |
| LOD1 and LOD2 | the same (LOD1 about 980 triangles, flat walls: 4,230 when every window was cut into them; LOD2 43, 4.2 MB for the city) | the same |
| Collision mesh triangles a building | 89 | 566 |
| First problem check of the whole city | 0.7 s | 89 s (in the background; edits after it recheck only what they touch) |

## 5. Districts

A city too big for one layout is split into districts: one Storey Site each, a street or a few blocks (50–300 buildings), each in a scene of its own. Cut along streets where you can, so few buildings share a wall across an edge.

**One building table for every site.** The table's buffers are the shaders' globals, so there can only be one: every site's renderer takes the shared one (`BuildingTable.Acquire`) and a block of building indices for its layout (`IndexBlocks`, at most 8,176 buildings across the loaded sites; the last 16 rows are the occluders' footprints). A party wall's vertices name their neighbour by layout index, moved into the block at upload (`MateIndex.Shift`). The view globals (the active building, the cutaway, isolate, the occlusion) are written only by the site that has a view, the editor's or the occlusion system's.

**Party walls across an edge.** A site lists its neighbouring districts: their layouts, and how far their sites are from it. In the site's inspector, **Find neighbouring districts** links every other open site whose buildings come within 2 m, both ways. Their buildings near the edge join this site's generator as context (`Site.WithContext`): copies, never drawn, there so the party walls along the edge are worked out as within one layout. Where two buildings across the edge are as tall, the owner is picked by layout name and id, so both districts agree and the wall is built once (`Site.OwnerKey`, `DistrictsTests`).

**The player.** **Storey Occlusion** with *Follow Player* (on by default) attaches each frame to the district the player is in (the one they are in while within 4 m of it, otherwise the nearest), and the play kit's character keeps its place in the world as it moves to the next site.

**Streaming.** Add **Storey Districts** to the scene that stays loaded (the player, the camera, the lighting):
1. Open the district scenes alongside it and press **Add open district scenes**: each becomes a district, with its area read from its Storey Site. **Read areas** reads them again after editing.
2. Put the district scenes in the build's scene list (the inspector warns about those missing).
3. In Play mode, a district within **Load Radius** (300 m) of the player is loaded additively, nearest first and **Max Loading** at a time. One loaded is unloaded once it is past **Unload Radius** (400 m), and the one the player is in never is (`Districts.Plan`).

## 6. Trying it in Unity

1. Select a Storey Site. In **Site settings**, check that LOD mode is *Automatic* and turn on **Show LOD stats**.
2. **Tools ▸ Storey ▸ Test City ▸ Make 3,000 Buildings**.
3. Orbit and zoom in the Scene view.
   - The stats in the corner show how many buildings are at each LOD, how many meshes are resident, how many cells are drawn, and the LOD time.
   - Turn on **LOD tint** to see the LODs in colour.
4. Press Play with the play kit. The stats show in the Game view, and the player's building and the buildings in the way stay at LOD0.

## 7. Memory

What a Storey Site holds, measured headlessly (.NET 8, not Unity) by replaying the renderer's Play-mode load frame by frame with its engine-free code, then the allocation test's five-minute walk. Each figure is the C# live set after a full collection, the GPU's meshes and building table in the formats uploaded, and the colliders' meshes. PhysX's cooked copies of the colliders aren't counted, nor Unity's own objects (a GameObject per building and per mesh). MB = 2²⁰ bytes.

| | First view loaded | After the walk | Peak |
|---|---|---|---|
| 3,000-building test city, default tier (an iPhone 14's screen, three build threads) | 76.6 MB | 98.8 MB | 116.1 MB |
| The same, minimum tier (an iPhone 7's screen, two build threads) | 46.6 MB | 76.2 MB | 83.6 MB |
| 1,000-building walk-in city, default tier | 60.1 MB | 81.9 MB | 102.2 MB |

After the walk at the default tier, the 3,000-building city holds:
- **GPU, 58.0 MB:** the 307 cells 9.8 MB, 16 LOD0s 26.2 MB, 215 shells 20.0 MB, the building table 2.0 MB.
- **C#, 40.7 MB:** the mesh chunks kept for reuse 15.4 MB and the spare mesh builders 3.3 MB (Reuse below); the layout's text 3.9 MB and the parsed layout 8.9 MB (its asset keeps both); the site's index 3.8 MB; the renderer's buildings, cells, LOD entries and colour rows 1.8 MB; the building table's CPU copy 1.8 MB; the walk model and occlusion 1.3 MB; the LOD0 shapes 0.4 MB.
- **Colliders:** 37, 0.1 MB of meshes (64 at most, §2).

Before the memory work the same walk held 333.6 MB, 276.5 MB of it C# copies the game never read again. A load's C# peaks a little above where it settles: 41.2 MB in the default tier's, 36.5 MB in the walk-in city's (a cell being packed while detail builds wait for their upload).

Both budgets in the package plan (§6.6: 120 MB on iPhone 7, 200 MB on iPhone 14 and newer) hold on the walk with room. The worst views are worked out from the GPU sizes of the buildings and the C# measured above, for the numbers of LOD0s given: nothing caps how many buildings show LOD0 (§2, Residency), so a view with more large buildings near the camera holds more. At the default tier, the densest view (the 95th-percentile building at every LOD0 and the largest shells) takes 85.7 MB on the GPU, about 126 MB in all; the 16 largest towers all at LOD0 take 144.0 MB on the GPU, about 185 MB in all. Both are inside 200 MB.

**The minimum tier.** On 2 GB phones (iPhone 7), give the sites the package's **Storey Minimum Tier** quality asset (in its `Quality Tiers` folder, beside **Storey Primary Tier**, the defaults): half the residency (**LOD0 Residency** 8, **LOD1 Residency** 130), 12 MB of chunks (**Mesh Chunks Kept MB**), no occlusion on far materials, and the opaque and massing materials lit without specular highlights or reflections (**Specular And Reflections** off; docs/PERFORMANCE.md §7.3). The walk then makes 163 MB of garbage in five minutes: the smaller LOD1 residency rebuilds shells two and a half times as often. The residency caps drop only meshes not shown, and don't limit those shown. What keeps the iPhone 7's detail down is its smaller screen (750 pixels high), which brings fewer buildings near enough for LOD0: 8 on the walk, where an iPhone 14's screen has 16. With the 37.7 MB of C# measured there, the 3,000-building city takes about 66 MB in all typically (28.4 MB on the GPU) and about 93 MB in the densest view (54.8 MB). With the 8 largest towers all at LOD0 (90.1 MB on the GPU) it comes to about 128 MB, past the 120 MB budget by about 8 MB. To have fewer buildings at LOD0 there, raise **LOD0 Pixels Per Metre**: LOD0 then comes nearer. The C# side's largest parts there are the chunks kept for reuse (11.7 MB, the setting above) and the layout held twice, as text and parsed (12.8 MB). The walk-in city of 1,000, the heaviest test, takes 97.8 MB on the GPU at worst.

**Nothing is kept from the generator but a building's shape.**
- **LOD0 and LOD1.** The meshes are uploaded from the generator's result, with no CPU copy kept (`UploadMeshData(true)`), and the result is let go. While a building has its LOD0, the renderer keeps its shape (`Lod0Shape`, `SiteRenderer.ShapeOf`): the collision segments and the wall list, for the walk model, the colliders and the cutaway. A shape is 10–25 KB, about 1% of its LOD0's GPU meshes, and 40–70 KB for the largest towers. The generator's whole result, kept before, was 4.7 to 5 times the GPU meshes: 3.5 to 7.7 MB a building, 32 to 35 MB for the largest towers.
- **A cell is packed straight into its upload layout** (`Cells.Pack`), in chunks that go back once it is uploaded (see Reuse below). Merging the 3,000-building test city's cells into lists made 78 MB of garbage on the main thread, and their upload arrays 10 MB more; packing them makes none once the chunks are kept, and a load's main thread works a third as long on the iPhone 7 setting (83–120 ms, was 286–340, headless).
- **Massings.** A layout loads cell by cell: each cell is merged as soon as its buildings' massings are built, and the massings are given back (`Lod2Mesh.Recycle`): the next ones built take them, their lists keeping their room (`Lod2Mesh.Make`, 32 kept), and a massing's quads are made without arrays. A massing allocates 24 KB on the 3,000-building test city (was 62) and 16 KB on the walk-in one (was 55), the same massings byte for byte; a load of the 3,000-building city allocates 135–151 MB on the build threads and the main thread (was 318–328, the layout's parse not counted). One cell's massings are held at a time, 0.4 MB at most on the test city; all 3,000 were kept before (50 MB). When a cell is merged again (a building in it was edited or removed), its other buildings' massings are made again from the layout: about 1 ms for the test city's biggest cell (24 buildings).
- **The walk model** (`PlayWorld`, docs/PLAY.md §1) keeps the shapes of the buildings the player has been near, at most `PlayWorld.Keep` (32), the least recently used going first. It takes the renderer's shape while the building has its LOD0 (built on a worker as the camera nears, long before the player can touch it) and otherwise builds the shape itself without meshes (`Lod0.BuildShape`: exactly the full build's segments and walls, about 50 times faster). The walk above holds 14 shapes, 1.4 MB. It kept 14 whole LOD0 results before, 170 MB, and built them on the main thread: five frames over 16.7 ms, the worst 68 ms. The walk's worst frame is now 1.0 ms with the walk model building its own shapes, 0.6 ms with the renderer's.
- **A finished build** waits for its upload packed (§4): 0.8 MB a building on the 3,000-building test city, 1.7 MB in the walk-in one, where the generator's welded result was 3.0 and 6.5 MB. Building a LOD0 allocates 2.6 MB for a walk-in building (29 MB before the reuse below), nearly all of it the chunks of its lists, which the renderer's next build takes again: the builder doesn't copy each wall's data into every vertex, welds in place, and grows its lists without copying them, so a building built again (its LOD0 or LOD1 dropped and wanted back) needs nothing from its last build.
- **The far city's cells** store each massing's facade numbers (metres along a wall and up a run of storeys, the window span, the bay's width, the kind) as 16-bit floats (`Cells.HalfVertex`): 36 bytes a vertex instead of 48, 9.8 MB instead of 12.8 on the 3,000-building city. The shader reads them as before. They are off by 2.5 cm at most on the test cities, a tenth of a pixel where a massing shows; a GPU that can't read 16-bit floats in a vertex gets the 48-byte one.
- **Large LOD0s** come in parts: a mesh past 65,535 vertices is split into meshes with 16-bit indices instead of one with 32-bit ones (`MeshPack.Pack`; a triangle is never split, and almost no vertex is copied). The 16 largest LOD0s of the 3,000-building city take 90.5 MB on the GPU instead of 98.6, in 51 meshes instead of 32.
- **The building table** uploads only the chunks that changed each frame (`DirtyChunks`): a few fading buildings' state rows, the wall slides that moved, the rows of the buildings in the way. On the five-minute walk that is 1.0 KB a frame on average, where the whole wall, state and occluder buffers went up every frame (344 KB). Its wall tables start at 8,192 walls and double when full, up to 65,536 (they were 65,536 from the start: 1.1 MB on the GPU and 1.1 MB in C# saved).
- **Reuse.** A mesh builder's lists and a packed mesh are made of fixed-size chunks (`Chunks`, `ChunkList`: 2,048 elements, 48 KB of positions). A builder holds a vertex's normal and colour as indices into its own tables of them (`IndexedList`): 46 bytes a vertex in all, where three 24-byte values made it 86. A list grows a chunk at a time and never copies, and gives its chunks back when its builder is recycled (`MeshBuilder.Recycle`) or its mesh uploaded (`PackedMesh.Release`). Every chunk of a type fits every list of it, so the next build takes them whatever the sizes of the two buildings. The wall panels and their triangulation (Earcut) use their builder's own lists and nodes again for every wall, and each wall's openings and panel options are the build's own, made again wall after wall; other triangulations, a mesh's packing and the walk model's queries borrow their lists for the call (`Lender`, a lock each way). Nothing is kept per thread: Unity's manual warns that the .NET `[ThreadStatic]` attribute crashes a Unity script, and `PackageLayoutTests` fails if one comes back. The five-minute walk of the 3,000-building city makes 91 MB of garbage instead of 2,818, a sixth of it the chunks' first fill: 32 MB in its busiest second (the first builds, was 376), 0.3 MB in a typical busy one. That matters most in a player, where Unity's collector scans the whole heap each time. The chunks kept between builds are capped by the quality asset (**Mesh Chunks Kept MB**, 16), each type of chunk to its share of what builds take: a builder's lists go back before its packed mesh, and would otherwise fill the cap. After the walk Storey holds 16.6 MB for reuse in all: the chunks (15.9 MB), the spare builders and the lent lists. With more kept the walk makes 88 MB (it needs 23 MB on one build thread, most of it for the largest towers); with less, a large build's chunks past the cap are garbage: 122 MB with 12, 226 MB with 8. A packed build waiting for its upload holds its meshes rounded up to a chunk (0.69 MB a building on the 3,000-building city, its GPU meshes 0.66 MB).
- **Welding.** LOD0 and its glass are packed unwelded (`MeshPack.Pack`'s `weld`): the generator shares their vertices already, and welding found 0.27% more on the test cities while taking a fifth to a quarter of a worker's build. A LOD0 build takes 6.6 ms of a worker instead of 8.2 on the 3,000-building city, 12.2 instead of 16.6 on the walk-in one. LOD1 is still welded (3.7% fewer vertices); `WeldTests` fails if the generator starts making duplicates.
- **The problem list** builds shapes too: a full check of the 3,000-building city takes 0.7 s instead of 2.7 s, of the walk-in city of 1,000 6 s instead of 16 s (Release build). An edit's check finds overlaps through the site's grid instead of comparing every pair of buildings: about 70 ms for one building and its neighbours on the 3,000-building city, was about 320; finding what the edit changed (each building's text compared) adds 120–150 ms.

| One building's | Plain city of 1,000 | Walk-in city of 1,000 |
|---|---|---|
| LOD0, made by the generator: average (worst) | 4.0 ms (20–30 ms), 1.8 MB allocated, was 13.6 ms and 13.4 MB | 4.2 ms (33–44 ms), 2.6 MB allocated, was 15.8 ms and 17.8 MB |
| Its shape alone (`Lod0.BuildShape`) | 0.5 ms (3 ms), 0.11 MB allocated | 0.4 ms (3–10 ms), 0.18 MB allocated |
| Kept after upload: the shape | 13 KB | 24 KB |

`MemoryBenchTests` keeps these numbers honest on the 3,000-building city, with bounds a few times what is measured: a LOD0 building keeps only its shape (8.7 KB a building, 38.5 KB for the largest), a finished build waits at the size of its meshes, a building built again allocates less than the first time, a load holds a few cells' massings at a time (the cell taken and four a build thread ahead: at most 7 MB of the 52.5 MB every building's take), and the walk model stays small on a five-minute walk. `Lod0ShapeTests` checks that a shape built alone is the full build's to the last bit (the demo street, the variants, a walk-in city, bridges, courtyards and atria), that the walk model walks the same keeping two shapes as keeping all, and that a massing made again is the one built first.

## 8. Not yet

- Measuring the upload in Unity, and moving the vertex buffers onto the workers if it hitches (the MeshData API, `Mesh.AllocateWritableMeshData`; §4).
- Loading districts through Addressables. They load as scenes from the build's scene list today (§5).
- Occluders across a district edge: the occlusion works on the district the player is in, so a building of the next district in the camera's way doesn't sink.
- Bridges between districts: a bridge reaches only buildings of its own layout.
- A neighbour district edited in the same session: its party walls along the edge update when this site next rebuilds everything (the neighbour's file is saved and reimported).
- Cells baked at import. They are merged when the layout loads: over frames in Play mode, at once after an edit or in the editor.
- Memory measured in Unity and on a device (the Memory Profiler, Xcode's gauge). §7 is headless.
- Unity's own copy and upload of a mesh (§4).
- Warming up the shaders' pipeline states before play (a `GraphicsStateCollection` traced on each device tier). The steady variant doubles the opaque and massing variants, and a variant a frame needs for the first time is compiled then, which can hitch.
- Burst for packing (`MeshPack` works on plain arrays already).
- The steady variant checked in the editor in every occlusion mode, and the GPU time it saves measured on a device (Xcode's GPU counters).
- None of the above is measured in Unity yet; the numbers in §4 are headless.
