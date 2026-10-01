# Changelog

## [Unreleased]

### Added
- The Storey editor tools (docs/EDITOR.md, slices 6.4–6.7; stub-compiled, not yet run in an editor): Shape, Facade and Interior Scene view tools for a Storey Site, the Storey Floors overlay, the site inspector with the building bar and the three tabs, `FacadeStylePreset` assets, and the colour field hook with Color Pipeline's picker when it is installed (`Triband.Storey.Editor.ColorPipeline`).
- Editing a site's layout (docs/EDITOR.md, slice 6.3; not yet run in an editor): `StoreyEdit` makes every edit one named undo step, saves to the `.storey` file (also on scene save, and asks on quit) and survives domain reloads; a first `StoreySite` inspector starts and ends an edit and sets floor counts. A reimported `.storey` file shows at once.
- `.storey` ScriptedImporter and **Tools > Storey > Import Prototype Layout...** (workstream 1).
- Package skeleton: editor assembly definition, smoke test (workstream 0).
