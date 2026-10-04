# Changelog

## [Unreleased]

### Added
- **Tools ▸ Storey ▸ Log Edit Timings**: one Console line per edit with where its time went, and Profiler samples named "Storey …" for each stage (docs/EDITOR.md §2.1). The project's Storey Opening and Facade Style assets are searched for once and kept until the project changes, instead of on every inspector redraw.
- Drags (corners, pushed walls, wall points, splits) put back only the dragged building at each step instead of reading the whole layout again: on the 3,000-building city about 0.1 s a step instead of 1.4 s. `StoreyEdit.DragStartBuilding`.
- Problems: a **Check** button and an **Auto** toggle. The check runs on a worker thread, on a copy of the layout. After the first check, only the buildings changed since the last one (with their neighbours and bridges) are checked again. On the 3,000-building test city an edit's check takes about 0.3 s; before, a 22 s check froze the editor after every edit (docs/EDITOR.md §6.5).
- **Test city** in the site inspector: 300, 1,000 or 3,000 generated buildings, or Remove (docs/CITY.md §1).
- The automatic LOD picks for the Scene view's camera, and the editor updates when that camera moves. The LOD stats are shown in the Scene view for a selected site (`StoreyLodEditor`).

### Changed
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
- No more console log for every colour matched to the palette while editing (the matching itself is unchanged).
- Making a storey shorter in the Interior tab still flashed its ceiling for a frame. The rebuilt storey was drawn with the floor clip from the frame before. The site now asks the tool for its view when it builds (`StoreySite.ViewSource`), so the clip and the meshes come from the same layout.
- In the Interior tab, going down a floor or making a storey shorter no longer shows the ceiling over the storey for a moment. The clip drops to the new ceiling at once; going up still grows.
- Delete in the Scene view removed the whole Storey Site. With a Storey tool active it now removes the selected wall, stairs, lift, corner or courtyard instead.
- Undoing a deleted Storey Site brought back the layout as last saved, not as edited. The edit is now saved before the site goes, and found again when the site comes back.
- Stairs and lifts are selected by a click, without dragging, and anywhere on them. Dragged past a wall or into another core, they keep sliding along it instead of stopping dead.
- Dragging a wall point or a "+" now moves the walls while dragging. Before, it showed only a dotted preview, and the move could fail to apply on release.
