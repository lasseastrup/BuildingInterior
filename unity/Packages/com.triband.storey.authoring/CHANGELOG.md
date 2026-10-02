# Changelog

## [Unreleased]

### Added
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
- Stairs and lifts are selected by a click, without dragging, and anywhere on them. Dragged past a wall or into another core, they keep sliding along it instead of stopping dead.
- Dragging a wall point or a "+" now moves the walls while dragging. Before, it showed only a dotted preview, and the move could fail to apply on release.
