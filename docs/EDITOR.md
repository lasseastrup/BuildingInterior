# Workstream 6: editor tools

Buildings are made in Unity, so the editor is where Storey is used (SPEC §9 maps the prototype's UI; UNITY-PACKAGE-PLAN §4 sets the idioms). Exit test: a designer makes a styled 3-floor building in under 2 minutes, and 10 floors with roof access in under 5.

## 1. Where the logic lives

The prototype's editing is plain functions over the layout: add or delete a floor, insert or drag a corner, push an edge, add a setback, place a core, draw a wall. They carry the rules that make the tool pleasant: new floors copy the top floor, cores follow the floor count, doors and details keep their place when an outline gains a corner, a setback must stand on the floor below.

These rules are ported to the engine-free `Runtime/Edit/` and judged against the prototype the way the generator is. `prototype/tools/export-ops.mjs` runs sequences of operations in the prototype and writes the resulting buildings to `unity/Fixtures/ops.json`, and the C# operations must produce the same JSON. The Unity tools are then thin: a handle or a button calls an operation inside one undo step and redraws.

## 2. Authoring model in the editor

- **One document per site.** A `.storey` file holds a street or district, as in the prototype, because party walls and edge snapping need the neighbours. A `StoreySite` component in the scene references the asset and draws it in edit mode. Buildings are selected inside it with the Storey tools (as ProBuilder selects faces), not as separate GameObjects. The tools belong to the **Storey tool context** (`StoreyToolContext`, an `EditorToolContext`). You enter it with *Edit layout*, or by choosing *Storey* in the Scene view's tool context menu with a site selected. While it is active, the Tools overlay shows Shape, Facade and Interior in place of Move, Rotate and Scale, so the site can't be moved by accident. Clicking empty space doesn't leave the context. *Stop editing* returns to the GameObject context.
- **No keyboard shortcuts.** Every action (tool modes, isolate, floors, turning a core, removing, finishing a wall) is a button in the inspector, in the Storey Floors overlay or in the Scene view. `PackageLayoutTests` checks that the editor reads no keys. Per-building proxies in the Hierarchy can be added later if level design needs them.
- **Edit session and undo.** Editing a site opens a `StoreyEditSession`, a hidden ScriptableObject holding the document's JSON. Every edit is `Undo.RecordObject(session, name)`, an operation from `Runtime/Edit`, and the JSON written back, so each edit is one named undo step and undo restores the text exactly. Saving (Ctrl+S, or leaving the tool) writes the `.storey` file and reimports it.
- **Live preview.** The edited building and the neighbours whose shared walls changed regenerate after each edit, debounced, LOD0 only while editing (UNITY-PACKAGE-PLAN §4.3). Generation never runs in `OnValidate`.

## 3. Order of work

| # | Slice | Contents | Done when |
|---|---|---|---|
| 6.1 | Floors, outlines, setbacks (**done**: `Edit/Floors`, `Outlines`, `Setbacks`, `Tiers`; 3,927 prototype cases match) | `Runtime/Edit`: floor count, add top floor (copying its layout), duplicate, delete with core/door/detail re-indexing, copy layout up; insert, remove and merge corners with door and blank-wall remapping, simplify, push edge, inset, outline validation with its reasons; add, remove and move setbacks; footprint presets | `ops.json` matches |
| 6.2 | Cores and walls (**done**: `Edit/Shafts`, `Walls`; 15,281 prototype cases match) | Placing, snapping, rotating and fitting stairs and lifts, floor ranges; the interior wall graph (split, join, T-junctions) and the shared snap rule; doors | `ops.json` matches |
| 6.3 | Session and preview (**written; editor check outstanding**, §4) | `StoreySite`, `StoreyEditSession`, undo, save, debounced regeneration in edit mode, isolate | Edits in the Scene view undo and save; the demo street draws in edit mode |
| 6.4 | Inspector (**written; editor check outstanding**, §5) | UI Toolkit inspector with the Shape, Facade and Interior tabs; building list (new from preset, duplicate, delete, rename); style fields with Color Pipeline's palette picker and `StoreyColorSettings`' suggestions; `FacadeStyle` preset assets | A building restyled entirely from the inspector |
| 6.5 | Shape tool (**written; editor check outstanding**) | Corner, insert, edge-push and move handles at the selected tier's height, the tier below dotted, snapping (neighbours, axis, Alt for free), edge lengths while dragging, the reason a move is refused | Outlines and setbacks edited as in the prototype |
| 6.6 | Floors and interior (**written; editor check outstanding**) | Floor overlay (count, list, per-floor height, copy up, duplicate, delete); Interior tool (walls, doors, stairs, lifts) on the active floor, with the floors above clipped as in play | 10 floors with roof access in under 5 minutes |
| 6.7 | Facade tool (**written; editor check outstanding**) | Entrance and blank-wall picking, the details palette from `FacadeDetailDefinition`s, details by rule | The exit test |

Each slice is usable on its own and ends with its tests green. 6.1 and 6.2 are headless; from 6.3 on, each slice needs an editor session to check.

## 4. Checking slice 6.3 in the editor

Headless, the session's bookkeeping is tested (`EditSessionTests`: which buildings an edit or an undo rebuilds; `ColorRowBookTests`: rows kept in place) and the Unity code compiles against the stubs. What only the editor can show:

1. Add **Storey Site** to an empty GameObject; assign a `.storey` layout (the demo street, or `unity/Parity/demo-project.storey`) and the three materials (`Storey/…`, or the Color Palette Lit variants). The street draws **without pressing Play**, and nothing of it is saved with the scene (the generated objects are greyed out in the Hierarchy).
2. **Edit layout** in the site's inspector, pick a building, type a floor count. The building (and the neighbours it shares walls with) changes at once; the rest of the street does not flicker.
3. **Ctrl+Z / Ctrl+Y** step through the edits, each named in *Edit ▸ Undo History*.
4. **Save** (or Ctrl+S, which saves the scene and the layout) writes the `.storey` file. Storey writes it in its own JSON layout, so the first save reformats a file that came from the prototype; the content is the same.
5. Undo past a save: the inspector says *Unsaved* again. **Revert** goes back to the file; **Stop editing** asks before dropping changes.
6. Enter and leave Play mode, and edit a script to force a domain reload, while editing: the edit and its undo history survive.

## 5. Slices 6.4–6.7: what is written, and checking it in the editor

**Engine-free, checked against the prototype:**
- `Edit/Facades`: facade picking, blank walls, entrances and terrace doors, details by hand with their ghosts and refusal reasons, terrace or roof.
- `Edit/Styles`: the five presets, the style each tier edits, own style and match below, and which style the roof is built with.
- `Edit/Buildings`: new from a footprint preset in a free spot, duplicate, delete, names.
- `Edit/Picking`: the building under the pointer, and the Interior tab's storey clip.

`unity/Fixtures/ops-facade.json` holds 14,138 prototype cases. `EditFacadeTests` and `PickingTests` check them.

**In the authoring package (stub-compiled only):**
- `StoreyShapeTool`, `StoreyFacadeTool` and `StoreyInteriorTool`, Scene view tools for a selected Storey Site.
- The **Storey Floors** overlay.
- The site inspector with the building bar and the three tabs.
- `FacadeStylePreset` assets.
- The colour field hook, `StoreyColorField.Picker`. With Color Pipeline installed, `Triband.Storey.Editor.ColorPipeline` replaces it with Color Pipeline's picker and the suggestions from `StoreyColorSettings`. It also installs `StoreyColorField.Conform`, so every edit ends with palette colours only (docs/COLOURS.md §3.8).

**Where this differs from the plan, for now:**
- The inspector is IMGUI, not UI Toolkit. It is rebuilt from the layout on every draw, which keeps it simple while the fields settle; moving it to UI Toolkit later changes no data.
- Dragging a wall point or a "+" moves the walls live. Each frame redoes the move from where the drag began, so the result is the prototype's single move on release: a point passing over another doesn't merge with it on the way.
- While dragging, the edited building rebuilds all its LODs once per frame, not LOD0 only.
- Facade details are the five built-in kinds. `FacadeDetailDefinition` assets (SPEC §4.4) need the generator to place imported models (docs/COLOURS.md §3.9) and come with that work.
- Play from the Interior tab ("drop the character on this floor") waits for the play kit (workstream 5).

**Checks in the editor**, after §4:

1. **Edit layout**: the Scene view switches to the Storey tool context (the Tools overlay shows Shape, Facade and Interior, and Move, Rotate and Scale are gone). Click a building: it is selected, and the inspector shows its name and floor count. **Isolate** hides the others.
2. **Shape:**
   - Drag a corner. It snaps to the neighbours' corners and edges, Alt moves it freely, and edge lengths show while you drag.
   - Drag a "+" to add a corner, and double-click a corner to remove it.
   - Drag an orange bar to push a wall.
   - Drag the centre handle: the building moves and snaps against a neighbour, which then shares the wall.
   - Add setback, Inset 1.5 m, Starts at, Terrace or Roof.
   - Something refused says why next to the pointer, for example "Stairs or a lift are in the way".
   - Each drag is one undo step.
3. **Facade:**
   - The **Preset** list (the five built-in presets, then the project's Facade Style assets; *Custom* when the style matches none; presets keep the roof), windows, the three colours (Color Pipeline's picker if installed), the roof fields on the top style only.
   - With Color Pipeline: a preset or a new building shows palette names, never "not a palette colour", and the Console lists each colour that was matched. Opening the demo street for editing matches its colours as one undo step. Under *More options*, a default the project hasn't set shows as its nearest palette colour ("Brass (nearest to #8A8F93)"). **Store the defaults in Storey Color Settings** stores those colours, creating `Assets/Resources/StoreyColorSettings.asset` if there is none.
   - Give a setback its own style.
   - Entrance and Blank wall, then each detail: ghosts in orange (add) or green (remove), and refusals as a notification.
   - Save as preset…, then apply the asset to another building.
4. **Interior:**
   - The floors above the active one are hidden, and walls between the camera and the focus drop to a stub.
   - Draw walls by clicking a chain, or by dragging one. Rings mark points, diamonds mark walls, and dotted guides show alignment.
   - Doors, Erase, Stairs and Lift (**Turn 90°** in the inspector turns the next one), with green or red ghosts.
   - **Finish wall** in the Scene view ends a chain.
   - Stairs come in two kinds, chosen under the Stairs tool or on selected stairs. Both serve the same floors: from where they're placed up to the top floor and the roof, or whatever **From** and **To** say.
     - **Switchback:** two flights and a landing per storey, in a walled stairwell.
     - **Straight flights:** one flight per storey with no walls, stacked one above the other, and a walkway beside it back to the start of the next flight. They're 2.6 × 6.4 m: a 1.3 m flight lane and a 1.3 m walkway.
     - On a straight flight, rails run along both sides of the opening on each storey above, and across its front on the top storey. A building wall stands in for the rail on a side against it. On a flat roof, a bulkhead with its door at the back closes the stair in.
     - The arrow shows which way the flights go up, and the dotted line marks the walkway. **Reverse** turns them round (the walkway changes side too). Only the sides can share a building wall, because both ends are landings.
   - Drag a core, a wall point and a "+": the walls and the building follow the pointer, and the drag is one undo step. Double-click a point to join or remove walls.
   - No key does anything Storey-specific.
   - Storey height, and the Storey Floors overlay: count, copy up, duplicate, delete.
5. **The exit test (Plan §8):** a styled 3-floor building in under 2 minutes. Then 10 floors with roof access in under 5: type 10 in the overlay, place stairs on the ground floor, check *reaches the roof* on the roof.

