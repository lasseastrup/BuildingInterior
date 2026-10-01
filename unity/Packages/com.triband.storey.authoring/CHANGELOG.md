# Changelog

## [Unreleased]

### Added
- Editing a site's layout (docs/EDITOR.md, slice 6.3; not yet run in an editor): `StoreyEdit` makes every edit one named undo step, saves to the `.storey` file (also on scene save, and asks on quit) and survives domain reloads; a first `StoreySite` inspector starts and ends an edit and sets floor counts. A reimported `.storey` file shows at once.
- `.storey` ScriptedImporter and **Tools > Storey > Import Prototype Layout...** (workstream 1).
- Package skeleton: editor assembly definition, smoke test (workstream 0).
