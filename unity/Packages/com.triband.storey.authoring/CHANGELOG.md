# Changelog

## [Unreleased]

### Added
- A Storey Opening's inspector says how LOD1 places its LOD1: on the flat wall, or in a hole cut in the wall, and with the plain pane where it leaves the hole open. A window whose LOD1 reaches into the wall gets a note that LOD1 cuts the wall round every window.
- A Storey Opening's **Fit**: *Stretch*, *Slice* or *Keep size* (it was the *Stretch* toggle). One made from a prefab starts as *Slice*. The Storey Cutout has *Borders* (the slice lines, drawn dotted in the Scene view) and *Keep shape*.
- The Facade tab greys out *Window width* when the style's artist window keeps its own size, and says so.
- Windows and doors from prefabs the Model Remapper coloured (docs/EDITOR.md §6.9): the bake reads each vertex's palette index from UV channel 3 (as the index + 0.1), and makes a part of each palette colour, named after the palette entry, which keeps that colour; a part named for glass takes the style's Glass. The palette's ids are found through `StoreyColorField.PaletteEntryAt`, which the Color Pipeline integration installs, so deleting a palette entry doesn't shift the colours.
- *Its own colour* for any part of a window, door or facade detail (`StoreyPartColours`): a palette colour picked with the project's colour picker, kept whatever the style. A facade detail's parts are listed with their materials' names (`StoreyDetail.Part.label`, from when the mesh is set).
- **Assets ▸ Create ▸ Storey ▸ Window or Door from Prefab** (docs/EDITOR.md §6.9): a Storey Opening for each prefab selected, beside it, a door if its name says so or its Storey Cutout names leaf objects. Its parts are the prefab's materials, their colours guessed from their names.
  - The bake (`StoreyOpeningBake`, installed as the asset's baker) gathers the prefab's active objects with an enabled MeshRenderer, its LOD Group's LOD 0 (and LOD 1 for LOD1, unless *LOD1 prefab* is set), and reads its hole. It lists what it leaves out: inactive objects, submeshes without a material, and components other than meshes.
  - A Storey Opening is baked again when its prefab, or anything the prefab uses, is imported: a mesh, a material, a nested prefab, a variant's base (`AssetDatabase.GetDependencies`, kept per prefab until it is imported again).
  - Its inspector shows the prefab's parts with their colours (and *Leaf* on a door), the hole and where it came from, and warns of faces on the wall's face round the hole, anything behind the wall, a hole left partly open, glass deeper than a shell storey's reveal, a window without glass, a door without a leaf, and more triangles than the generator's most dressed window (140) or a LOD1 of more than 12. *Check on a building* tries it on a block of six storeys. *Open Prefab* opens the prefab.
  - The Storey Cutout's inspector fits the hole to every mesh under it (as it does when it is added) or to one object's, and the Scene view draws the hole, the reveal to the wall's back and the wall's face round it.
- The Storey Prop's inspector says when its Rigidbody is held still in Play mode, waiting for its building's collider (docs/PROPS.md §2.6).
- **Tools ▸ Storey ▸ Test City…**, the Storey Test City window (docs/CITY.md §1). It makes a test city in the selected site's layout from settings kept per user: how many buildings (up to 8,000), the seed (or a new one at random), a height multiplier and the most storeys, and which buildings are walk-in. That is a share of them, exact, spread through the city or the ones nearest the middle, with a lift above the storeys set; or the prototype's own mix. It shows the site's test city now (buildings, walk-in, storeys, lifts), makes it again or removes it, and with no site selected lists the scene's to pick.
- A build step for Storey's shaders (`StoreyShaderStripping`, docs/PERFORMANCE.md §7.9). It leaves out the minimum tier's lighting variants (`STOREY_SIMPLE_LIGHTING`) when no quality asset the build can use turns Specular And Reflections off: one a scene in the build depends on, or any in the project's `Assets` folder. That halves the opaque and massing colour passes' variants in such a build. The scripting define `STOREY_KEEP_ALL_SHADER_VARIANTS` keeps them. After a build, the Console has a line for each of Storey's shaders: its variants compiled, and how many were left out.
- Facade details as assets (docs/EDITOR.md §6.12). The Facade tab's **Place on walls** lists every Facade Detail asset: the package's five (AC unit, Vent, Dish, Fire escape, Awning), then the project's. They were five buttons written into the code. **Details by rule** has a slider for each of the project's details with a rule.
  - A Facade Detail's inspector says where it goes and what places it, in words. Setting a mesh names its parts from the model's materials and takes its size from the mesh.
  - It warns of a pivot that looks off, a mesh behind the wall's face, a heavy mesh without a LOD1 mesh, and flat ends on a mesh fitted up the wall.
  - A new Facade Detail takes its asset's GUID as its id, and a copy gets one of its own, so it is a detail of its own (`StoreyAssetIds`). A reimported model bakes its details again. Saving the layout updates the site's list of the details it uses (`StoreyDetails`).
- The prototype's five footprints (Rect, L, U, T, Octa) ship as Building Template assets in the package's `Building Templates/` folder, the 3-storey brick buildings New made of them, the same kind of asset **Save as template…** makes. **New** lists every template, the package's first, then the project's under **Project**; it listed five shapes from code, then the project's templates apart. The Shape tab's **Start from** takes any template's outline with its cut corners (the project's under *Project ▾*), where it had the five shapes only.
- The prototype's five facade presets (Brick walk-up, Glass office, Pastel stucco, Concrete slab, Warehouse) ship as Facade Style assets in the package's `Facade Styles/` folder, the same kind of asset **Save as preset…** makes. The Facade tab's **Preset** list is every Facade Style asset, the package's first, then the project's, and applies each the same way; they were five presets written into the code beside the project's assets. A new building starts as the package's Brick walk-up. To make a variant, apply one, change it and **Save as preset…**.
- The Storey Prop's inspector says which building and storey it's in; *Tools ▸ Storey ▸ Props ▸ Show Prop Buildings* tints every prop by its building (docs/PROPS.md).
- Shape ▸ *Placement*: a building's **Elevation**, and **Snap to ground**, which casts down onto the scene's colliders under it (a Terrain's too), stands it on the highest ground found and deepens its foundation to the lowest (docs/EDITOR.md §6.11).
- **Find neighbouring districts** in the site's Site settings, and the **Storey Districts** inspector (**Add open district scenes**, **Read areas**, a warning for scenes missing from the build) (docs/CITY.md §5).
- **Tools ▸ Storey ▸ Test City ▸ Make 300 / 1,000 / 3,000 Walk-in Buildings**: a test city where every building has an interior.
- **Tools ▸ Storey ▸ Log Edit Timings**: one Console line per edit with where its time went, and Profiler samples named "Storey …" for each stage (docs/EDITOR.md §2.1). The project's Storey Opening and Facade Style assets are searched for once and kept until the project changes, instead of on every inspector redraw.
- Drags (corners, pushed walls, wall points, splits) put back only the dragged building at each step instead of reading the whole layout again: on the 3,000-building city about 0.1 s a step instead of 1.4 s. `StoreyEdit.DragStartBuilding`.
- Problems: a **Check** button and an **Auto** toggle. The check runs on a worker thread, on a copy of the layout. After the first check, only the buildings changed since the last one (with their neighbours and bridges) are checked again. On the 3,000-building test city an edit's check takes about 0.3 s; before, a 22 s check froze the editor after every edit (docs/EDITOR.md §6.5).
- **Test city** in the site inspector: 300, 1,000 or 3,000 generated buildings, or Remove (docs/CITY.md §1).
- The automatic LOD picks for the Scene view's camera, and the editor updates when that camera moves. The LOD stats are shown in the Scene view for a selected site (`StoreyLodEditor`).

### Changed
- `StoreyShaderStripping` runs after URP's own stripping (`callbackOrder` 1000; it was 0, as URP's, so its counts could include what URP then left out). Its line after a player build gives each Storey shader's fragment and vertex programs compiled, and the keywords in the fragment programs with how many each is in, the Color Palette Lit sample's shaders too.
- The Test City menu (Make 300, 1,000 or 3,000 Buildings, the walk-in ones and Remove Test City) is the Storey Test City window. Making a city no longer asks first: Undo takes it back.
- A style carries the key of the preset it was applied from (`FacadeStyle.preset`: a package preset's own, `brick` and the rest as the prototype names them, or a project preset's asset GUID), and the **Preset** list shows that preset as the style's, a project's too. A field of the style edited by hand takes the key off (*Custom*), as in the prototype, except the roof, which a preset never sets; before, no edit did, so an edited style still showed its preset, and so did a preset saved from it. **Save as preset…** names the style after the new asset and shows it as the style's.
- The test city moved from the site's inspector to **Tools ▸ Storey ▸ Test City** (Make 300, 1,000 or 3,000 Buildings, which asks first, and Remove Test City), acting on the selected site.
- The Interior tab's tools (Select, Wall, Door, Erase, Stairs, Lift) are buttons with drawn icons, the name under each and what it does in the tooltip.
- **Log Edit Timings** is kept for the editor session only: it is off again after a restart.
- The Interior tab's wall tool refuses a wall wholly outside the storey, such as one over a setback's terrace, and shows it red while drawing. Nothing of such a wall was built. A wall partly outside is still cut to the storey, as before.
- Defaults in the Interior tab: **Walls** starts at *Up*, and **Camera follows** is off.
- **Walls** in the Interior tab: *Down* (the new default: every wall of the storey low, steady as the camera orbits), *Cutaway* (the old camera-following drop) or *Up*. `SiteView.walls`, `CutWalls`.
- The site inspector, laid out for reading at a glance (docs/EDITOR.md §6.10):
  - large Shape, Facade and Interior buttons with icons;
  - a building card;
  - a one-line problems summary;
  - named groups in each mode, with the less-used ones folded;
  - the site's own settings folded at the bottom.

  The Facade tab's *More options* is gone: its fields moved into the groups they belong to.
- A cut corner is one point in the Shape tool. Selecting it shows its cut, which changes as you set it; **Make sharp** and **Clear all** take cuts away (docs/EDITOR.md §6.1).
- Selecting a Storey Site starts editing it. The layout saves itself when the site is deselected, with the scene, before Play and on quit. The *Edit layout*, *Save*, *Revert* and *Stop editing* buttons are gone; undo replaces Revert.
- An open layout with nothing unsaved follows its file when the file changes outside the editor.

### Added
- Foundation **Stands out** slider in the Facade tab's Walls group.
- **Sills** and **Heads** toggles under *Windows and doors*. *Window heads* is now *Head shape* under them.
- **Window** and **Street door** pickers in the Facade tab, for artist-made `StoreyOpening` assets. The asset's inspector names the parts from the model's materials and bakes the mesh again on reimport (docs/EDITOR.md §6.9).
- **Details** in the Facade tab: window heads, glazing bars, frames, the band, street doors, plinth height, the foundation and brick patches, plus the Frames, Foundation and Plinth colours (docs/EDITOR.md §6.7).
- Building templates (`StoreyBuildingTemplate`): **Save as template…** in the building bar, **New ▸ From template** to place one (docs/EDITOR.md §6.6).
- **Problems** under the building bar, and markers in the Scene view (`StoreyProblems`; docs/EDITOR.md §6.5).
- The **Bridge** tool and the **Bridges** list in the Facade tab (docs/EDITOR.md §6.4).
- **Courtyards and atria** in the Shape tab, with their corners, edges and position edited in the Scene view (docs/EDITOR.md §6.3).
- Roof fields for mansards (*Top pitch*, *Steep part*) and dormers (*Dormers*, *Dormers every*) in the Facade tab (docs/EDITOR.md §6.2).
- **Corners** in the Shape tab: chamfer or round the selected corner, or every corner, with a corner entrance on a chamfer (docs/EDITOR.md §6.1).
- Editor feedback (`StoreyJuice`):
  - a faint outline on the building under the pointer, and a short glow on what is selected;
  - ring pulses where a corner or wall point snaps, and a flash along a wall a moved building now shares;
  - new buildings grow up from the ground and added floors rise into place, and placed stairs and lifts pulse;
  - Isolate eases in and out;
  - *Camera follows* moves the Scene view up and down with the active floor.
- Switching floors in the Interior tab animates: the floor clip eases to the new ceiling, and the active storey's walls slide down to the stub and back up rather than snapping.
- **Rooms / Filled** per floor in the Interior tab, marked ▪ in the Storey Floors overlay.
- Stairs come as **Switchback** or **Straight flights**, chosen under the Stairs tool or on selected stairs. Straight flights show an arrow up the flight lane and the walkway beside it, and can be reversed.
- The Storey tool context (`StoreyToolContext`): the Shape, Facade and Interior tools belong to it, *Edit layout* enters it, and the built-in transform tools are off inside it.
- Layouts stay inside the palette: with Color Pipeline, every edit, and opening a layout for editing, replaces colours the palette lacks with the nearest entry (`StoreyColorField.Conform`), and logs each change.
- The Storey editor tools (docs/EDITOR.md, slices 6.4–6.7; stub-compiled, not yet run in an editor): Shape, Facade and Interior Scene view tools for a Storey Site, the Storey Floors overlay, the site inspector with the building bar and the three tabs, `FacadeStylePreset` assets, and the colour field hook with Color Pipeline's picker when it is installed (`Triband.Storey.Editor.ColorPipeline`).
- Editing a site's layout (docs/EDITOR.md, slice 6.3; not yet run in an editor): `StoreyEdit` makes every edit one named undo step, saves to the `.storey` file (also on scene save, and asks on quit) and survives domain reloads; a first `StoreySite` inspector starts and ends an edit and sets floor counts. A reimported `.storey` file shows at once.
- `.storey` ScriptedImporter and **Tools > Storey > Import Prototype Layout...** (workstream 1).
- Package skeleton: editor assembly definition, smoke test (workstream 0).

### Changed
- The facade presets are one **Preset** list (built-in, then the project's Facade Style assets) instead of a row of buttons.
- With Color Pipeline, a default colour the project hasn't set shows as the palette colour it renders as, not "(matched to the palette on the next edit)", which was wrong: defaults are never matched by an edit. **Store the defaults in Storey Color Settings** stores them.
- No keyboard shortcuts: the tool modes, Isolate, floor stepping, turning and removing are buttons (**Turn 90°** for placing stairs and lifts, **Finish wall** for a wall chain).

### Fixed
- A copied Window or Door asset (Ctrl+D, or its file copied in) kept its original's id, so the two were one window: every style that picked either showed whichever loaded last. A copy now gets an id of its own once it's imported, and the original keeps its id, so the styles that picked it still show it (docs/EDITOR.md §6.9). A new window or door takes its asset's GUID as its id; the ids of those made before are kept. Two that already share an id are split when the editor loads, and the Console says which got its own (`StoreyAssetIds`).
- No more console log for every colour matched to the palette while editing (the matching itself is unchanged).
- Making a storey shorter in the Interior tab still flashed its ceiling for a frame. The rebuilt storey was drawn with the floor clip from the frame before. The site now asks the tool for its view when it builds (`StoreySite.ViewSource`), so the clip and the meshes come from the same layout.
- In the Interior tab, going down a floor or making a storey shorter no longer shows the ceiling over the storey for a moment. The clip drops to the new ceiling at once; going up still grows.
- Delete in the Scene view removed the whole Storey Site. With a Storey tool active it now removes the selected wall, stairs, lift, corner or courtyard instead.
- Undoing a deleted Storey Site brought back the layout as last saved, not as edited. The edit is now saved before the site goes, and found again when the site comes back.
- Stairs and lifts are selected by a click, without dragging, and anywhere on them. Dragged past a wall or into another core, they keep sliding along it instead of stopping dead.
- Dragging a wall point or a "+" now moves the walls while dragging. Before, it showed only a dotted preview, and the move could fail to apply on release.
