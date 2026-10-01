# Workstream 6: editor tools

Buildings are made in Unity, so the editor is where Storey is used (SPEC §9 maps the prototype's UI; UNITY-PACKAGE-PLAN §4 sets the idioms). Exit test: a designer makes a styled 3-floor building in under 2 minutes, and 10 floors with roof access in under 5.

## 1. Where the logic lives

The prototype's editing is plain functions over the layout: add or delete a floor, insert or drag a corner, push an edge, add a setback, place a core, draw a wall. They carry the rules that make the tool pleasant: new floors copy the top floor, cores follow the floor count, doors and details keep their place when an outline gains a corner, a setback must stand on the floor below.

These rules are ported to the engine-free `Runtime/Edit/` and judged against the prototype the way the generator is. `prototype/tools/export-ops.mjs` runs sequences of operations in the prototype and writes the resulting buildings to `unity/Fixtures/ops.json`, and the C# operations must produce the same JSON. The Unity tools are then thin: a handle or a button calls an operation inside one undo step and redraws.

## 2. Authoring model in the editor

- **One document per site.** A `.storey` file holds a street or district, as in the prototype, because party walls and edge snapping need the neighbours. A `StoreySite` component in the scene references the asset and draws it in edit mode. Buildings are selected inside it with the Storey tools (as ProBuilder selects faces), not as separate GameObjects. Per-building proxies in the Hierarchy can be added later if level design needs them.
- **Edit session and undo.** Editing a site opens a `StoreyEditSession`, a hidden ScriptableObject holding the document's JSON. Every edit is `Undo.RecordObject(session, name)`, an operation from `Runtime/Edit`, and the JSON written back, so each edit is one named undo step and undo restores the text exactly. Saving (Ctrl+S, or leaving the tool) writes the `.storey` file and reimports it.
- **Live preview.** The edited building and the neighbours whose shared walls changed regenerate after each edit, debounced, LOD0 only while editing (UNITY-PACKAGE-PLAN §4.3). Generation never runs in `OnValidate`.

## 3. Order of work

| # | Slice | Contents | Done when |
|---|---|---|---|
| 6.1 | Floors, outlines, setbacks (**done**: `Edit/Floors`, `Outlines`, `Setbacks`, `Tiers`; 3,927 prototype cases match) | `Runtime/Edit`: floor count, add top floor (copying its layout), duplicate, delete with core/door/detail re-indexing, copy layout up; insert, remove and merge corners with door and blank-wall remapping, simplify, push edge, inset, outline validation with its reasons; add, remove and move setbacks; footprint presets | `ops.json` matches |
| 6.2 | Cores and walls (**done**: `Edit/Shafts`, `Walls`; 15,281 prototype cases match) | Placing, snapping, rotating and fitting stairs and lifts, floor ranges; the interior wall graph (split, join, T-junctions) and the shared snap rule; doors | `ops.json` matches |
| 6.3 | Session and preview (**written; editor check outstanding**, §4) | `StoreySite`, `StoreyEditSession`, undo, save, debounced regeneration in edit mode, isolate | Edits in the Scene view undo and save; the demo street draws in edit mode |
| 6.4 | Inspector | UI Toolkit inspector with the Shape, Facade and Interior tabs; building list (new from preset, duplicate, delete, rename); style fields with Color Pipeline's palette picker and `StoreyColorSettings`' suggestions; `FacadeStyle` preset assets | A building restyled entirely from the inspector |
| 6.5 | Shape tool | Corner, insert, edge-push and move handles at the selected tier's height, the tier below dotted, snapping (neighbours, axis, Alt for free), edge lengths while dragging, the reason a move is refused | Outlines and setbacks edited as in the prototype |
| 6.6 | Floors and interior | Floor overlay (count, list, per-floor height, copy up, duplicate, delete); Interior tool (walls, doors, stairs, lifts) on the active floor, with the floors above clipped as in play | 10 floors with roof access in under 5 minutes |
| 6.7 | Facade tool | Entrance and blank-wall picking, the details palette from `FacadeDetailDefinition`s, details by rule | The exit test |

Each slice is usable on its own and ends with its tests green. 6.1 and 6.2 are headless; from 6.3 on, each slice needs an editor session to check.

## 4. Checking slice 6.3 in the editor

Headless, the session's bookkeeping is tested (`EditSessionTests`: which buildings an edit or an undo rebuilds; `ColorRowBookTests`: rows kept in place) and the Unity code compiles against the stubs. What only the editor can show:

1. Add **Storey Site** to an empty GameObject; assign a `.storey` layout (the demo street, or `unity/Parity/demo-project.storey`) and the three materials (`Storey/…`, or the Color Palette Lit variants). The street draws **without pressing Play**, and nothing of it is saved with the scene (the generated objects are greyed out in the Hierarchy).
2. **Edit layout** in the site's inspector, pick a building, type a floor count. The building (and the neighbours it shares walls with) changes at once; the rest of the street does not flicker.
3. **Ctrl+Z / Ctrl+Y** step through the edits, each named in *Edit ▸ Undo History*.
4. **Save** (or Ctrl+S, which saves the scene and the layout) writes the `.storey` file. Storey writes it in its own JSON layout, so the first save reformats a file that came from the prototype; the content is the same.
5. Undo past a save: the inspector says *Unsaved* again. **Revert** goes back to the file; **Stop editing** asks before dropping changes.
6. Enter and leave Play mode, and edit a script to force a domain reload, while editing: the edit and its undo history survive.

