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

In the editor: select a Storey Site, then **Tools ▸ Storey ▸ Test City ▸ Make 300 / 1,000 / 3,000 Buildings**. It asks before adding them. **Remove Test City** takes them out. It's in a menu rather than the site's inspector so nobody adds thousands of buildings by accident. The city is an edit like any other, so it is saved with the layout and can be undone.

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
- **Changes cross-fade** (dithered) over 0.35 s. The shader hides a building's other LODs through its row in the building table (shown, fading from, fade), so nothing is enabled or disabled per building.
- **Detail is built on demand.**
  - Only the massings are built when a layout loads.
  - A building that wants LOD0 or LOD1 asks for it, and the ones covering the most pixels go first.
  - It shows the best LOD it already has meanwhile.
  - The meshes are generated on worker threads, one fewer than the cores, and one building at a time each. The main thread only uploads them, for up to 6 ms a frame (at least one when any is ready).
  - A result for a layout or building that has changed since is thrown away, as is one no longer wanted.
  - With **Build on worker threads** off, and always on WebGL, building happens on the main thread and stops after 6 ms a frame (at least one is always built).
  - The generator's memos and caches are thread-safe. `ParallelBuildTests` checks that building on 8 threads gives exactly the meshes that building one after another does.
- **Residency.** At most 16 LOD0 and 260 LOD1 meshes are kept. Past that, the least recently shown ones are dropped. A building's collider comes with its first LOD0 and stays after that LOD0 is dropped.
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
- **Size.** A cell's mesh is split to stay under 65,535 vertices (16-bit indices), never through a building.
- **The test city** of 3,000 falls into 307 cells (the prototype has 316). Each fits in one mesh, 263,000 vertices in all.

**Table sizes.** Each massing needs a parameter row per run of storeys in the building table: the test city needs about 9,000. The parameter and colour-row tables double when they fill. Building indices are still capped at 8,192 a layout.

## 4. What it costs

Headless numbers (`CityBenchTests`, `LodManagerTests`). They are CPU only, without the GPU upload, measured on the CI container:

| | Time |
|---|---|
| Loading 3,000 buildings: every massing | 0.21 ms a building, 0.63 s in all |
| Merging the 307 cells | 43 ms |
| Picking every building's LOD | 0.4–0.9 ms a frame |
| One LOD1 | about 6–8 ms |
| One LOD0 | about 23–34 ms on average, 100–150 ms at worst (tall walk-in buildings) |
| LOD0 and LOD1 on 3 worker threads | about 9 ms a building, off the main thread |
| Committing one edit (finding what changed, the layout's text) | about 140 ms (was 350) |
| One step of a drag | about 0.1 s (was 1.4 s: the whole layout was read again each step) |

The time for one LOD0 is several frames' budget. That's why the generating is on worker threads, so the main thread pays only for the upload. How long the upload takes in Unity is not measured yet. If it shows up as a hitch, the next step is to build the vertex buffers on the workers too, with `Mesh.AllocateWritableMeshData`.

## 5. Trying it in Unity

1. Select a Storey Site. In **Site settings**, check that LOD mode is *Automatic* and turn on **Show LOD stats**.
2. **Tools ▸ Storey ▸ Test City ▸ Make 3,000 Buildings**.
3. Orbit and zoom in the Scene view.
   - The stats in the corner show how many buildings are at each LOD, how many meshes are resident, how many cells are drawn, and the LOD time.
   - Turn on **LOD tint** to see the LODs in colour.
4. Press Play with the play kit. The stats show in the Game view, and the player's building and the buildings in the way stay at LOD0.

## 6. Not yet

- Measuring the upload in Unity, and moving the vertex buffers onto the workers if it hitches (§4).
- Districts: streaming parts of the city in and out (Addressables). The whole layout is still loaded at once.
- Cells baked at import. They are merged when the layout loads, which takes tens of milliseconds for the test city.
- Memory accounting against the budgets in the package plan §6.6.
- None of the above is measured in Unity yet; the numbers in §4 are headless.
