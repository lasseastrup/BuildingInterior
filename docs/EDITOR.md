# Workstream 6: editor tools

Buildings are made in Unity, so the editor is where Storey is used (SPEC §9 maps the prototype's UI; UNITY-PACKAGE-PLAN §4 sets the idioms). Exit test: a designer makes a styled 3-floor building in under 2 minutes, and 10 floors with roof access in under 5.

## 1. Where the logic lives

The prototype's editing is plain functions over the layout: add or delete a floor, insert or drag a corner, push an edge, add a setback, place a core, draw a wall. They carry the rules that make the tool pleasant: new floors copy the top floor, cores follow the floor count, doors and details keep their place when an outline gains a corner, a setback must stand on the floor below.

These rules are ported to the engine-free `Runtime/Edit/` and judged against the prototype the way the generator is. `prototype/tools/export-ops.mjs` runs sequences of operations in the prototype and writes the resulting buildings to `unity/Fixtures/ops.json`, and the C# operations must produce the same JSON. The Unity tools are then thin: a handle or a button calls an operation inside one undo step and redraws.

## 2. Authoring model in the editor

- **One document per site.** A `.storey` file holds a street or district, as in the prototype, because party walls and edge snapping need the neighbours. A `StoreySite` component in the scene references the asset and draws it in edit mode. Buildings are selected inside it with the Storey tools (as ProBuilder selects faces), not as separate GameObjects. The tools belong to the **Storey tool context** (`StoreyToolContext`, an `EditorToolContext`). Clicking a building in the Scene view selects the mesh Unity picks, as for any object, and changes nothing else. To edit it, choose *Storey* in the Scene view's Active Context menu: the site is selected in the mesh's place, that building is picked in it, and the site's last tab's tool is shown. The context is always listed there, because the mesh has no `StoreySite` for a context to target; chosen with the site selected it edits the building picked before, and with nothing of Storey's selected it waits for a click on a building. A tab (Shape, Facade, Interior) in the site's inspector enters it too. While it is active, the Tools overlay shows Shape, Facade and Interior in place of Move, Rotate and Scale, so the site can't be moved by accident, and the **Storey Floors** overlay shows. Clicking a building, another site's too, edits it. Clicking empty space doesn't leave the context; choosing the GameObject context in the same menu, or selecting another object in the scene, does.
- **The selection outline.** Unity outlines a selected object's children too, and the site's children draw every building, so with the site selected every building had it. In the Storey context only the building being edited is outlined, and none while none is picked; in the GameObject context the whole site is, as any object is. Unity draws the outline with each material's selection pass (`SceneSelectionPass`), and Storey's shaders keep one building's triangles in it (StoreyLit.hlsl, `STOREY_SELECTION`): `StoreySelectionOutline` sets `_StoreyOutline` (`StoreyShaderIds.Outline`) to that building's table index plus one, or 0 for every building. A renderer's selected state (`EditorUtility.SetSelectedRenderState`) can't do it: the GPU Resident Drawer outlines everything under the selection whatever it says, and a merged cell holds many buildings. The pass is compiled when first drawn (`editor_sync_compilation`; in the background, the buildings the GPU Resident Drawer draws had no outline at all) and a player never draws it. A project's own Storey shader needs the same pass; the Color Palette Lit sample's have it (`SelectionOutlineTests`).
- **No keyboard shortcuts.** Every action (tool modes, isolate, floors, turning a core, removing, finishing a wall) is a button in the inspector, in the Storey Floors overlay or in the Scene view. `PackageLayoutTests` checks that the editor reads no keys. Unity's own Frame Selected (F) frames the building being edited while the Storey context is active (its active storey in the Interior tab), not the whole site: the site's inspector gives Unity the bounds. Lock View to Selected (Shift+F) still follows the site. Per-building proxies in the Hierarchy can be added later if level design needs them.
- **Edit session and undo.** Editing a site opens a `StoreyEditSession`, a hidden ScriptableObject holding the document's JSON. Every edit is `Undo.RecordObject(session, name)`, an operation from `Runtime/Edit`, and the JSON written back, so each edit is one named undo step and undo restores the text exactly. Saving (Ctrl+S, or leaving the tool) writes the `.storey` file and reimports it.
- **Live preview.** The edited building and the neighbours whose shared walls changed regenerate after each edit, debounced, LOD0 only while editing (UNITY-PACKAGE-PLAN §4.3). Generation never runs in `OnValidate`.

### 2.1 Where an edit's time goes

Every stage of an edit is a Profiler sample whose name starts with "Storey". Search the Profiler's hierarchy for "Storey".

The stages:
- **The edit:** the undo record, the operation, matching colours to the palette, and finding what changed.
- **The site:** a rebuild of the edited buildings, or a full rebuild of every building, and its parts:
  - indexing the layout;
  - rewriting the colour rows;
  - each building's massing;
  - LOD0 and LOD1 built and uploaded;
  - the cells merged.
- **The editor:** the automatic LOD, the colliders, the inspector's draw and the Storey tool in the Scene view.

**Tools ▸ Storey ▸ Log Edit Timings** (off by default, and off again after an editor restart) prints one Console line per edit once it has settled. The line gives the stages, slowest first, and counts such as buildings rebuilt, full rebuilds and cells merged. It also gives the slowest editor frame since the edit. If that frame is far above Storey's stages, the time went somewhere else: another inspector or window, the Scene view, or the garbage collector.

## 3. Order of work

| # | Slice | Contents | Done when |
|---|---|---|---|
| 6.1 | Floors, outlines, setbacks (**done**: `Edit/Floors`, `Outlines`, `Setbacks`, `Tiers`; 3,927 prototype cases match) | `Runtime/Edit`: floor count, add top floor (copying its layout), duplicate, delete with core/door/detail re-indexing, copy layout up; insert, remove and merge corners with door and blank-wall remapping, simplify, push edge, inset, outline validation with its reasons; add, remove and move setbacks; footprint presets | `ops.json` matches |
| 6.2 | Cores and walls (**done**: `Edit/Shafts`, `Walls`; 15,281 prototype cases match) | Placing, snapping, rotating and fitting stairs and lifts, floor ranges; the interior wall graph (split, join, T-junctions) and the shared snap rule; doors | `ops.json` matches |
| 6.3 | Session and preview (**written; editor check outstanding**, §4) | `StoreySite`, `StoreyEditSession`, undo, save, debounced regeneration in edit mode, isolate | Edits in the Scene view undo and save; the demo street draws in edit mode |
| 6.4 | Inspector (**written; editor check outstanding**, §5) | UI Toolkit inspector with the Shape, Facade and Interior tabs; building list (new from preset, duplicate, delete, rename); style fields with Color Pipeline's palette picker and `StoreyColorSettings`' suggestions; `FacadeStyle` preset assets | A building restyled entirely from the inspector |
| 6.5 | Shape tool (**written; editor check outstanding**) | Corner, insert, edge-push and move handles at the selected tier's height, the tier below dotted, snapping (neighbours, axis, Alt for free), edge lengths while dragging, the reason a move is refused | Outlines and setbacks edited as in the prototype |
| 6.6 | Floors and interior (**written; editor check outstanding**) | Floor overlay (count, list, per-floor height, copy up, duplicate, delete); Interior tool (walls, doors, stairs, lifts) on the active floor, with the floors above clipped as in play | 10 floors with roof access in under 5 minutes |
| 6.7 | Facade tool (**written; editor check outstanding**) | Entrance and blank-wall picking, the details palette from the Facade Detail assets (§6.12), details by rule | The exit test |

Each slice is usable on its own and ends with its tests green. 6.1 and 6.2 are headless; from 6.3 on, each slice needs an editor session to check.

## 4. Checking slice 6.3 in the editor

Headless, the session's bookkeeping is tested (`EditSessionTests`: which buildings an edit or an undo rebuilds; `ColorRowBookTests`: rows kept in place) and the Unity code compiles against the stubs. What only the editor can show:

1. Add **Storey Site** to an empty GameObject; assign a `.storey` layout (the demo street, or `unity/Parity/demo-project.storey`) and the three materials (`Storey/…`, or the Color Palette Lit variants). The street draws **without pressing Play**, and nothing of it is saved with the scene (the generated objects are greyed out in the Hierarchy).
2. Select the site (editing starts at once), pick a building, type a floor count. The building (and the neighbours it shares walls with) changes at once; the rest of the street does not flicker.
3. **Ctrl+Z / Ctrl+Y** step through the edits, each named in *Edit ▸ Undo History*.
4. The layout saves itself to the `.storey` file: when you deselect the site, save the scene (Ctrl+S), enter Play mode or quit. There are no Save, Revert or Stop editing buttons; undo goes back as far as you need. Storey writes the file in its own JSON layout, so the first save reformats a file that came from the prototype; the content is the same.
5. Change the `.storey` file outside the editor (a version control update) while the site has nothing unsaved: the site shows the new file at once.
6. **Delete** (or Backspace) in the Scene view, with a Storey tool active, removes what the tool has selected and never the site's GameObject:
   - Interior: a wall, stairs or a lift.
   - Shape: a corner or a courtyard.

   With nothing selected it says so. Delete the site from the Hierarchy.
7. Delete the site's GameObject anyway (from the Hierarchy), then undo: the site comes back with the layout as it was. The edit is written to the file before the site goes, and shown on it again when it returns.
6. Enter and leave Play mode, and edit a script to force a domain reload, while editing: the edit and its undo history survive.
   - With domain reload off (*Project Settings ▸ Editor ▸ When entering Play Mode*), make a building walk-in and enter Play mode: Play mode shows it walk-in. The layout is saved and reimported as Play starts, and the reimport writes the new text into the same asset object, so the asset reads it again rather than keeping its parse of the layout from before (`StoreyDocumentAsset`). Play mode showed the layout from before the edit until a domain reload.

## 5. Slices 6.4–6.7: what is written, and checking it in the editor

**Engine-free, checked against the prototype:**
- `Edit/Facades`: facade picking, blank walls, entrances and terrace doors, details by hand with their ghosts and refusal reasons, terrace or roof.
- `Edit/Styles`: the prototype's five presets, the style each tier edits, own style and match below, which style the roof is built with, and the preset key a style loses when a field of it is edited by hand.
- `Edit/Buildings`: new from a footprint preset or a template in a free spot, duplicate, delete, names; `Edit/Setbacks` replaces a footprint with a preset or a template's.
- `Edit/Picking`: the building under the pointer, and the Interior tab's storey clip.

`unity/Fixtures/ops-facade.json` holds 14,138 prototype cases. `EditFacadeTests` and `PickingTests` check them.

**In the authoring package (stub-compiled only):**
- `StoreyShapeTool`, `StoreyFacadeTool` and `StoreyInteriorTool`, Scene view tools for a selected Storey Site.
- The **Storey Floors** overlay, shown while a building is edited.
- The site inspector with the building bar and the three tabs.
- `FacadeStylePreset` assets: every preset in the Facade tab, the package's five (`Facade Styles/`, the prototype's presets) and the project's.
- `StoreyDetails`: every Facade Detail asset in the Facade tab's palette, the runtime package's five (`Facade Details/`, the generator's own) and the project's (§6.12).
- The colour field hook, `StoreyColorField.Picker`. With Color Pipeline installed, `Triband.Storey.Editor.ColorPipeline` replaces it with Color Pipeline's picker and the suggestions from `StoreyColorSettings`. It also installs `StoreyColorField.Conform`, so every edit ends with palette colours only (docs/COLOURS.md §3.8).

**Where this differs from the plan, for now:**
- The inspector is IMGUI, not UI Toolkit. It is rebuilt from the layout on every draw, which keeps it simple while the fields settle; moving it to UI Toolkit later changes no data.
- Dragging a wall point or a "+" moves the walls live. Each frame redoes the move from where the drag began, so the result is the prototype's single move on release: a point passing over another doesn't merge with it on the way.
- While dragging, the edited building rebuilds all its LODs once per frame, not LOD0 only.
- Facade details are Facade Detail assets (`StoreyDetail`, §6.12), not prefabs with a `FacadeDetailDefinition` each (SPEC §4.4). An artist's is a mesh in parts coloured from the style, as an artist's window is, and its rule's density is on the style, not in a rules asset. Palette icons, LOD2 meshes and colliders for details aren't done.

**Checks in the editor**, after §4:

1. Select the site and click a tab: the Scene view switches to the Storey tool context (the Tools overlay shows Shape, Facade and Interior, and Move, Rotate and Scale are gone). Click a building: it is selected, and the inspector shows its name and floor count. **Isolate** hides the others.
   - In the GameObject context, click a building in the Scene view: its mesh is selected, and the context doesn't change. Choose *Storey* in the Active Context menu: the site is selected, that building is the one edited, with the site's last tab's tool. Near buildings and far ones (drawn merged into cells) alike.
   - The **Storey Floors** overlay shows only while a building is edited: not in the GameObject context, and not in the Storey context with no site selected.
   - Choose *Storey* with nothing selected: the Scene view says to click a building, and a click on one edits it. A click on anything else selects it and goes back to the GameObject context.
   - Select another object (a light, a prop) while editing: the GameObject context is back, with its Move tool.
   - With two sites, click the other site's building while editing one: that site and building are edited, with the same tool.
   - Press **F** in the Scene view: it frames the selected building, and in the Interior tab its active storey. In the GameObject context F frames the selection, as Unity does.
2. **Shape:**
   - Drag a corner. It snaps to the neighbours' corners and edges, Alt moves it freely, and edge lengths show while you drag.
   - Drag a "+" to add a corner, and double-click a corner to remove it.
   - Drag an orange bar to push a wall.
   - Drag the move gizmo's arrows or squares (Unity's own, on the building's ground floor): the building moves along that axis or in that plane and snaps against a neighbour along it, which then shares the wall. Its up arrow raises and lowers it (§6.11).
   - Add setback, Inset 1.5 m, Starts at, and what it leaves uncovered below (a terrace, or a roof: the floors below's *Terrace roof* card, Facade tab).
   - Something refused says why next to the pointer, for example "Stairs or a lift are in the way".
   - Each drag is one undo step.
3. **Facade:**
   - The **Preset** list (every Facade Style asset: the package's five, then the project's; *Custom* once a field of the style is edited by hand, its roof aside; presets keep the roof), windows, the three colours (Color Pipeline's picker if installed), the roof fields on the top style only.
   - With Color Pipeline: a preset or a new building shows palette names, never "not a palette colour", and the Console lists each colour that was matched. Opening the demo street for editing matches its colours as one undo step. Under *More options*, a default the project hasn't set shows as its nearest palette colour ("Brass (nearest to #8A8F93)"). **Store the defaults in Storey Color Settings** stores those colours, creating `Assets/Resources/StoreyColorSettings.asset` if there is none.
   - Give a setback its own style.
   - Doors ▸ *Place doors*, Details ▸ Blank wall, then each detail (the package's five, then the project's Facade Details): ghosts in orange (add) or green (remove), a door's naming the door a click puts in or takes out, and refusals as a notification.
   - Save as preset…: the new preset shows as the style's, under Project in the list; apply it to another building. Apply one of the package's presets, change a field (the list says *Custom*), and save that as a preset too.
4. **Interior:**
   - The floors above the active one are hidden.
   - **Walls** (Interior tab) sets this storey's walls, after The Sims' wall modes:
     - *Down*: every wall drops to a 1 m stub, and nothing moves as the camera goes round.
     - *Cutaway*: only the walls between the camera and the focus drop, and they follow the camera.
     - *Up* (the default): every wall at full height.
   - Draw walls by clicking a chain, or by dragging one. Rings mark points, diamonds mark walls, and dotted guides show alignment.
   - Doors, Erase, Stairs and Lift (**Turn 90°** in the inspector turns the next one), with green or red ghosts.
   - **Finish wall** in the Scene view ends a chain.
   - Stairs come in two kinds, chosen under the Stairs tool or on selected stairs. Both serve the same floors: from where they're placed up to the top floor and the roof, or whatever **From** and **To** say.
     - **Switchback:** two flights and a landing per storey, in a walled stairwell.
     - **Straight flights:** one flight per storey with no walls, stacked one above the other, and a walkway beside it back to the start of the next flight. They're 2.6 × 6.4 m: a 1.3 m flight lane and a 1.3 m walkway.
     - On a straight flight, rails run along both sides of the opening on each storey above, and across its front on the top storey. A building wall stands in for the rail on a side against it. On a flat roof they come up through a railed opening, with no bulkhead, and the parapet runs on past them.
     - The arrow shows which way the flights go up, and the dotted line marks the walkway. **Reverse** turns them round (the walkway changes side too). Only the sides can share a building wall, because both ends are landings.
   - Drag a core, a wall point and a "+": the walls and the building follow the pointer, and the drag is one undo step. Double-click a point to join or remove walls.
   - Click stairs or a lift without dragging: it is selected (anywhere on it, not only its handle). Drag one past an outside wall or into another core: it stops there but keeps sliding along it with the pointer.
   - No key does anything Storey-specific.
   - Storey height, and the Storey Floors overlay: count, copy up, duplicate, delete.
   - **Switching floors animates.** Going up, the storeys grow up to the new ceiling. Going down, or making a storey shorter, the storeys above go at once, so the ceiling never covers the storey for a moment. The new floor's walls (in Cutaway, those in front of the camera) then slide down to the stub, and the old floor's slide back up (0.22 s, as in play). Selecting another building, or opening the Interior tab, shows its floor at once.
   - **Camera follows** (next to the floor list, off by default): the Scene view's camera eases up or down with the floor.
6. **Feedback while editing:**
   - The building under the pointer is outlined faintly.
   - What you select (a building, a core, a wall) glows for a moment.
   - A corner or wall point that snaps pulses a ring where it landed.
   - A building snapped against a neighbour flashes the wall they now share.
   - A new or duplicated building grows up from the ground, and added floors rise into place.
   - Placed stairs and lifts pulse and glow.
   - **Isolate** fades the other buildings out and back.
   - **Rooms / Filled** on a floor:
     - A filled storey has nothing inside: opaque windows and closed doors, no rooms and no stairs. The Storey Floors overlay marks it ▪.
     - A lift passes through it without stopping, so its panel lists only open floors.
     - Stairs stop at a filled storey, with rails across the dead end.
     - Example: an 11-storey block with floors 1 to 9 filled, and a lift from the ground straight to floor 10.
5. **The exit test (Plan §8):** a styled 3-floor building in under 2 minutes. Then 10 floors with roof access in under 5: type 10 in the overlay, place stairs on the ground floor, check *reaches the roof* on the roof.


## 6. Building features beyond the prototype

These are Storey's own: the prototype does not have them, so they have their own tests rather than fixtures.

### 6.1 Corners

**Corners** in the Shape tab chamfers or rounds the selected corner of the outline being edited (click a corner to select it), or **Every corner** at once.

- **Chamfer:** a straight cut, *Size* metres back along each edge.
- **Round:** an arc of that *Radius*, in steps of at most 15°.
- The cut shows green on the selected corner before you make it.
- **A cut corner stays one corner.** The Scene view shows it as one point, at the sharp corner (dotted), and you drag it like any other.
  - Select it and the Corners controls show its cut. Changing *Chamfer*/*Round*, the size or *Corner entrance* changes the cut at once, so you can try styles quickly.
  - **Make sharp** takes the cut away. **Clear all** makes every corner of the outline sharp again.
- The cut follows its edges: drag a neighbouring corner or push a wall, and the arc or chamfer is made again to meet them. If its edges get too short for it, the corner is sharp until they're long enough again (in the same drag).
- Doors, details and blank walls on the two edges keep their places, and those on the cut itself stay on it. A cut too big for its edges, or one the outline rules refuse (cores, setbacks), says why.
- **Corner entrance** (the base outline, Chamfer): a street door in the middle of the chamfer. It needs about 2.7 m.
- A setback made from an outline with cuts keeps them.
- Corners cut before this change are plain corners. Clear them as before: double-click each extra point.

In the file, the outline still holds the cut's points, so everything that builds from it is unchanged. The building's (or setback's) `corners` list records each cut: its sharp corner, shape, size, door and points. A cut whose points are no longer in the outline is forgotten.

Engine-free: `CornerCuts` (`Sharp`, `Around`, `Cut`, `Clear`, `CutAll`, `ClearAll`), `Outlines.CornerPoints` (`CornerTests`, `CornerCutsTests`).

**A sharp corner left sharp is clipped.** Everything that stands out from a wall (the wall's own thickness, the plinth, bands, cornice, curb, parapet, cap, a terrace's parapet, an overhang's fascia) ends at a corner on its bisector, where the next wall's meets it. A corner sharper than about 67° (a turn whose half has a tangent over `Geo.MitreLimit`, 1.5) would send those ends far out past it, metres for a deep cap, so they stop at that limit, as the prototype's do. They used to leave the corner open between the two walls, down to the inside and through the roof's edge. Now each one closes its half of the corner, from its end across to the bisector, on every face it has (`Cut.Clipped`, `Cut.Tip`; `MeshBuilder.ClipFace` and `ClipFlat`, `MBox` with its cuts, `Facade.WallPanel`, `StripFace`, LOD2's walls and tops). The corner ends in a small flat face across its tip, like a stroke's clipped mitre, standing out from the corner by at most 1.8 times the strip's depth. The outline and every corner up to 67° build as before (`AcuteCornerTests`). For a blunter tip, chamfer the corner (above).

**Checks:**
1. Click a building's corner, choose Round with a 3 m radius: the arc shows green. *Cut this corner*: the building is rounded there, with walls, bands and windows following. The corner is still one point.
2. With it selected, switch to Chamfer and drag the size: the cut changes as you drag. *Make sharp*: the corner is square again.
3. Drag the rounded corner, then its neighbour: the arc moves with them.
4. Chamfer 3 m with *Corner entrance*: a street door opens on the chamfer.
5. *Every corner* on a setback, then *Clear all*.

### 6.2 Mansard roofs and dormers

**Mansard** is a roof type in the Facade tab's Roof fields: a steep 70° slope rises from the eaves to the break (*Steep part*, 2.4 m by default), then a shallow hip at *Top pitch* (20°) covers the rest. Its eaves are short (at most 0.2 m), so the steep slope starts at the wall top.

**Dormers** works on every pitched roof: hip, gable, shed and mansard. *Dormers every* sets the spacing along each eave (3.5 m by default). Each dormer has:
- a front wall with a window just behind the wall below;
- two cheeks back to the roof;
- its own 45° gabled roof running back into the slope.

Only dormers that fit on their eave's slope are built, clear of hips, valleys and ridges, and on a mansard under the break. A small roof gets none. LOD2's massing leaves them out.

Layouts with a mansard or dormers are Storey's own: the prototype doesn't read them (`"roofType": "mansard"`, `"mansard"`, `"dormers"`).

Engine-free: `Roofs.Make`, `RoofType.Mansard`, `FacadeStyle.mansard` and `FacadeStyle.dormers` (`RoofFeatureTests`).
- Dormer windows follow the style as the facade's windows do: sills, heads (flat or arched), frames, glazing bars, the artist's window, and rooms behind the glass (the window shader). LOD1 shows a plain pane with rooms.

**Surface** (the Roof card's *Surface*, under any pitched roof): the slopes' tiles, drawn by the shader rather than modelled, so they add no triangles, draw calls or textures. *Pattern*:
- **Tiles:** clay tiles, rounded, in columns up the slope, each row's foot over the one below.
- **Slate:** flat slates, each row half a slate along, a fine joint between two.
- **Shingles:** rounded feet, each row half a shingle along.
- **Seams:** a metal roof, a standing seam up the slope at each panel's edge.

*Tile width* (along the eave; for seams the *Panel width*) and *Tile height* (how much of each shows up the slope) default to the pattern's own: 0.25 × 0.35 m, 0.3 × 0.22 m, 0.25 × 0.18 m, 0.5 m. *Relief* is how strongly the light picks out each tile, and *Variation* how much each tile's shade differs from the next. The colour is the roof's.

The pattern runs along each slope's own eave and up its own slope, so a hip roof's faces each have their rows. Dormer roofs and the steep sides of a mansard have it too. The flat top of a roof, the trim and the soffits don't. A roofed terrace has the surface of the style of the floors it covers, set on their *Terrace roof* card.

**Terrace roof** (the Facade tab, with the floors under a setback selected in *Style for*): what covers those floors where the setback above leaves them uncovered. *Terrace*: flat, with a parapet, to walk out onto through a door. Or a roof: *Hip* (sloping up from every eave to the walls above, as the prototype's) or *Gable*, with its *Pitch* and *Surface*, the roof of the outline below cut away where the setback stands (docs/SPEC.md §4.1; `TerraceRoofData.type`, layout key `type` in `terraceRoof`, written only when not a hip; `TerraceRoofTypeTests`). Not a shed, whose one slope over the whole outline below would tower over the setback, nor a mansard, whose steep slope would hide it: a layout's other types are read as a hip. The card is there whatever the floors' style, and whichever style drives the roof on top: their own roof is set with them. When it is these floors' style that drives the roof on top (the setback shares it), that card is titled *Roof on top*. Its colour and surface are the floors' style's; where they share the floors below's, the card says so. The Shape tab shows what covers the floors below a setback, with *Roof…* to go to the card.

The tiles fade to the roof's plain colour where one is under a few pixels on screen, so they don't shimmer in the distance. Every pattern averages that colour, which is also what LOD2's massing paints, so the switch to the massing changes nothing (`RoofSurfaceTests`).

How it is drawn: the style's surface rides in three spare entries of its colour row (`ColorRows.RoofEntry`). A vertex of the roof's colour slot on a face that isn't level reads them, one buffer load, and hands them to its fragments where a window pane's coordinates go (a pane never lies on a roof). The fragment works out the pattern from the face's normal and its place, about 40 instructions on roof pixels only (StoreyRoof.hlsl).

Layout key `roofSurface` on the style: an object with `pattern` (`tiles`, `slate`, `shingles` or `seams`), `w`, `h`, `relief` and `variation`, each written only when set. The baked layout format is version 9.

**Checks:**
1. Set a building's roof to Mansard: steep sides, a shallow top. Change *Steep part*.
2. Turn on Dormers on a hip, a gable and the mansard, then change *Dormers every*.
3. Give a hip roof each Surface pattern, close up and far off: the tiles follow each slope, and far off the roof is its plain colour again.

### 6.3 Courtyards and atria

**Courtyards and atria** in the Shape tab adds an opening through the building. It runs from a storey's floor up through the roof, and has two kinds:

- **Courtyard:** open to the sky.
  - Facades face into it, with windows and bands by the style.
  - A door opens onto it on its bottom storey, in the middle of its longest edge.
  - It's paved on its bottom storey (*Paved on*): the ground, or a podium deck on a floor above.
  - A flat roof has a parapet round it. A pitched roof (hip, gable, shed or mansard) is cut back to the courtyard's walls, and walls rise from them to the roof, as a light well.
- **Atrium:** inside the building.
  - The floors above its bottom storey (*Floor on*) are cut open round it, with rails at each slab edge.
  - A flat roof has a skylight over it: a curb and a glass roof. Under a pitched roof it stops at the top storey's ceiling.

**Adding and editing.**
- *Add courtyard* or *Add atrium* puts a rectangle in the roomiest part of the building, as big as fits, up to 8 × 8 m.
- The selected one is green in the Scene view, at its bottom floor. Drag its corners (they line up with their neighbours; Alt moves them freely), click a + to add a corner, double-click a corner to remove it, and drag the centre to move it.
- It must stay 1.5 m inside the outline of every floor it passes, clear of the stairs and lifts on the storeys it opens, and of other courtyards. A change that breaks this is refused, and says why.
- The rules work both ways. Outlines, setbacks and cores are refused where they'd run into a courtyard, and a footprint preset drops courtyards that no longer fit.
- Interior walls stop at the opening on the storeys it's open.

**In play:** the walk model and the colliders are open over it. A courtyard is reached through its door; an atrium's rails stop you.

Layouts with these are Storey's own: the prototype doesn't read them (`"voids"`).

Engine-free: `Courtyards`, `Voids`, `VoidData` (`CourtyardTests`).

**Checks:**
1. *Add courtyard* on a 4-storey building. It shows green, and the building gets an open courtyard with windows facing in and a door on the ground floor. Drag a corner, and move it.
2. Set *Paved on* to Floor 1: the ground floor is a room under it.
3. Switch it to an atrium: rails on the floors above, and a skylight on the roof.
4. Set the roof to Hip: the courtyard is a light well through it.
5. Try to place stairs in it, or push an outside wall into it: both are refused.

### 6.4 Bridges between buildings

The **Bridge** tool in the Facade tab adds a bridge:
- Click an upper floor's wall. The bridge goes straight out from it to the first building whose wall faces back within 15°, and arrives on that building's floor nearest this one's.
- A ghost shows it before you click. A click that can't make one says why: no wall faces this one, too long (at most 40 m), too steep (at most 1 in 5), another building in the way, or a bridge already there.
- A door opens at each end. If the two floors are at different heights, the bridge ramps between them.
- The floor band under the bridge's floor stops at its deck; a band deeper than the deck goes on beneath it.
- Click a bridge's door, on either building, to remove it.

**Bridges** in the Facade tab lists every bridge from or to the building. For each you can set **Enclosed** (glass sides with mullions, and a roof) or **Open** (a deck with rails), and its width (1.5 to 4 m).
- The building the bridge leaves from builds it, in every LOD: LOD2 shows it as a plain box.
- Moving either building rebuilds both. The bridge follows as long as the walls still face each other.
- A bridge that no longer meets the other building shows a warning there, with *Remove*.
- Deleting a building removes the bridges to it. A duplicated building has no bridges.

**In play:** walk from one building's floor across to the other's. The sides hold you, and the colliders have its deck and roof.

Layouts with bridges are Storey's own (`"bridges"`).

Engine-free: `Bridges`, `BridgeEdits`, `BridgeData` (`BridgeTests`).

**Checks:**
1. Two buildings facing each other across a street. Pick Bridge and hover floor 2's wall: the ghost spans the street. Click: a glass bridge, with a door at each end.
2. Set the second building's upper storeys taller: the bridge ramps.
3. Switch it to Open, and change the width.
4. Move the second building along the street: the bridge follows. Move it round the corner: the bridge warns that it no longer meets.
5. Click its door to remove it.

### 6.5 Problems

**Problems** sits under the building bar, for the whole layout. It lists what's broken or can't be used, one line each. Click a line to select that building, open the storey (Interior tab) and frame the Scene view on the spot. The Scene view marks every problem: red for serious ones, amber for the rest. The selected building's markers say what is wrong.

What it finds:
- **Floors nobody can reach.** Each storey of a walk-in building is walked on a 25 cm grid, as the player would, with the walk model's own walls. It starts from the street doors and bridges, and goes up and down the stairs and lifts. Possible findings:
  - a whole floor with no way there;
  - a floor the stairs or lift arrive at but a wall closes in;
  - part of a floor nobody can reach ("a room with no door?").
- **No way in:** a walk-in building with no street door or bridge.
- **Doors** on upper floors with nothing to open onto (they aren't built), and doors into filled storeys.
- **Stairs and lifts** that no longer fit every floor they serve, that overlap, or a lift with one stop.
- **Courtyards and atria** that no longer fit.
- **Bridges** that no longer meet, or that lead into or out of a storey with nothing inside.
- **Buildings that overlap.**
- **Artist windows** whose dressing (shutters, a surround) meets the next window's: the bays it needs (§6.9).

It runs on a worker thread, on its own copy of the layout, so the editor never waits for it.
- **The first check** covers the whole layout.
- **After that**, only what changed since the last check is checked again, and the result is merged into the list (`Problems.Scope`, `Changed`, `Merge`). The changes are found by comparing each building's text. What gets rechecked:
  - the buildings added, edited or removed;
  - every building within 2 m of where they are or were (party walls, doors onto a neighbour's roof);
  - every building joined to those by bridges, chained through further bridges.

  `ScopedProblemTests` checks that this gives exactly the list a whole-layout check would.
- **Check** runs it now. The list says when it's older than the layout ("edited since").
- **Auto** (on by default, remembered per user) runs it again after each edit has settled, never during a drag.

Timings: the demo street takes about 30 ms, the 78-building variants corpus about 250 ms. On the 3,000-building test city, the first check takes about 7 s; a check after an edit takes about 0.3 s, most of it spent finding what changed. Overlaps only clip footprints whose bounding boxes overlap; before that, the full check took 22 s.

Engine-free: `Validate.Problems` (`ProblemTests`). On the demo street it finds two real problems:
- Row House: a wall 10 cm in front of the stairs shuts them in above the ground floor.
- Linden Court: the room behind the lift is reached only past a 40 cm gap.

**Checks:**
1. Open the demo street: Problems lists Row House floors 1 to 3 and Linden Court's rooms. Click one: the Scene view goes there.
2. Remove a building's stairs: its upper floors are listed. Undo: the list clears.
4. Make a test city of 3,000. The editor stays responsive while "Checking … s" counts up, and the list appears after about 7 s. Then edit one building: the list updates in well under a second.
3. Draw a wall across a room with no door: "Part of … can't be reached".

### 6.6 Building templates

**Save as template…** in the building bar keeps the selected building as an asset (`StoreyBuildingTemplate`): its outlines and setbacks, floors, rooms, stairs and lifts, courtyards, doors, details and style. Its place in the layout and its bridges aren't kept. Save over an existing template to update it.

Every building **New** makes comes from a template. The package ships the prototype's five footprints as 3-storey Brick walk-up buildings (`Building Templates/`: Rect, L, U, T, Octa), listed first; the project's follow under **Project**. New places one in any layout:
- It goes at a free spot near the Scene view's centre.
- It gets fresh ids, and its name with a number if the layout already has one by that name (the package's own are unnamed: the layout's next name).
- One of the package's own takes the package's Brick walk-up preset as it is now.
- Edit it as any other building: it isn't linked to the template.

The Shape tab's **Start from** replaces the selected building's footprint with a template's outline and its cut corners: the package's as buttons, the project's under *Project ▾*.

A template's inspector says what it holds (storeys, size, height, cores). *Assets ▸ Create ▸ Storey ▸ Building Template* makes an empty one to save into. The asset is Storey's JSON for one building, as the prototype copies one to the clipboard.

Engine-free: `Buildings.AsTemplate`, `Buildings.FromTemplate`, `Setbacks.ApplyFootprint` (`TemplateTests`, `BuildingTemplateAssetTests`).

**Checks:**
1. Select a building, then *Save as template…*: the asset appears and is pinged. Its inspector summarises it.
2. In another scene's site: New ▸ Project ▸ it. The building appears near the view, named, and editable.
3. Place it twice: "Name 2", "Name 3".
4. New ▸ L: a 3-storey brick L, as New made one before. Start from ▸ Project ▸ the saved template: the selected building takes its outline.

### 6.7 Facade details

**Details** in the Facade tab, from the artist's brick building. Each option is per style, so a setback can differ. A style with none of them builds exactly as before.

- **Sills** and **Heads** (*Windows › Built-in window*, shown while a style picks the built-in window): turn off the sill under each window, or the head over it (flat or arched; on doors, the arched hood). Both are on by default. Off, the window is a plain opening with its glass. Layout keys `sills` and `heads`, written only when off.
- **Head shape:** *Flat* (the thin head strip) or *Arch*, the artist's cap over each window and door. It's a solid hood standing 14 cm out from the wall and reaching 10 cm past the opening each side. Its top is arched (7 cm thick at the ends, rising about a twelfth of its span in the middle), and its underside dips 4 cm at the ends, so it hangs over the window.
- **Glazing bars:** *Panes across* × *Panes up* (2 × 3 like the artist's) divides every window into panes.
- **Frames:** a frame all round each window and door, in the new *Frames* colour. Sills and heads take that colour too.
- **Floor bands** (*Walls*): its *Height*, how far it *Stands out* (at least 1 cm), and *Shade of the wall* instead of the trim colour (shown while the band has no colour of its own). Windows and their arched heads, and doors with their posts, stay under a band you've set.
- **Set into the wall** (*Windows › Depth in the wall* and *Doors › Depth in the wall*, each with its *Depth*; the old BuildingGenerator's window and door insets): how deep windows and doors sit in the wall, from its face (`recess`, `doorRecess`, metres). Off, as before: the generator's pane and a shell storey's door panel 16.5 cm in (`OpeningKinds.ShellReveal`), an artist's window or door at the wall's face. On, the generator's pane goes that deep (1 to 25 cm, so its frame stays in the 30 cm wall), with its frame, glazing bars and centre bar; a shell storey's reveal goes as deep; the plain door's panel goes that deep (1 to 29 cm); and an artist's window or door is pushed back that far, its wall face at the recess's back (a shell storey's reveal then reaches as far behind it as `ShellReveal`). A recessed street door's threshold is in the *Foundation* colour. Sills and heads stay on the wall's face. LOD1's cut openings (doors, and windows an artist's LOD1 reaches into) follow it, and an artist's LOD1 in a hole is pushed back; LOD1's plain windows on the flat wall keep the shader's fixed recess (StoreyWindow.hlsl), which a few centimetres either way don't change at that distance. LOD2 paints windows flat. A street door set's entry can have a recess of its own, over `doorRecess` (`OpeningEntry.recess`, docs/OPENINGS.md §7.1): the *depth* field on each *Street door* row, empty for the style's; its threshold is in the *Foundation* colour too. Courtyard walls follow the style too.
- **Opening margin** (*Doors*, under *Set into the wall*, while a street door is one of yours; `doorRecessOffset`, Storey's own; the old BuildingGenerator's door inset offset): how much bigger each side a recessed artist's door's hole is than the door (metres; below 0 smaller, the door's frame then behind the wall), its top as far over it, its foot on the floor. Held to the door's bay with fixed bays and under the storey's band; only on a shell storey and with a recess. The door keeps its own size in it.
- **Solid back** (under either card's *Set into the wall*, one setting for both; `recessBack`, Storey's own; the old BuildingGenerator caps its insets): a recessed artist's window or door on a shell storey has a back in the wall's colour across its hole at the recess's depth, where the window or door is pushed back to: what of it lies behind that is hidden, and round a hole bigger than it the wall shows. LOD1 has it too (a door with no LOD1 mesh shows its plain panel at its own size in front of it).
- **Door** (*Doors*, on the base): *Built-in*, a flat panel in the door *Colour* at the back of the wall's recess, with nothing round it (no surround, canopy or hood). On a walk-in storey the doorway stays open, because the player walks through it. Or a door an artist made (§6.9). Layouts saved when street doors were *Canopy* or *Glazed* read as before, with plain doors.
- **Place doors** (*Doors › On the walls*; it was *Entrance*, among the details): click a wall in the Scene view to put a door in it, and a door to take it out; above the street, where a terrace or a neighbour's roof is in front of the wall. Where the style has several doors, *Door to place* says which a click puts in: one of the list (the first unless another is picked), or *Any of them, by weight*. A door put in by name keeps it (`EntranceData.door`: a Storey Opening door's id, or `plain` for the built-in one; layout key `door` on the entrance, written only when set), whatever the style picks for its wall and whatever its list becomes, as long as the project has that door; one placed as *Any of them* (or with a style of one door) is the style's pick, as every door was before. The Scene view says which door a click puts in, or takes out. To move a door, take it out and put it in where it should be. The site keeps the doors placed by name in its list, so builds include them.
- **Cornice:** a strip round the top of each tier's walls (the old BuildingGenerator's top strip): its *Height*, how far it *Stands out* (10 cm by default, at least 1 cm) and its *Colour* (unset, the trim). It runs under the roof, and on a tier with a setback above it along the terrace's edge (under the terrace's parapet), not where the setback's wall goes on up, where the band stays. On that storey it takes the band's place. Its top is the wall's top (the roof's level, `Derived.RoofY`, or the setback's floor), or under a pitched roof (the building's or a terrace's) the eave's soffit, which hangs `Roofs.SoffitDrop` below it (the eave's drop at its slope plus the fascia's 15 cm); its foot is *Height* lower (`Facade.CorniceUnder`: how far under the storey's top the foot is). The tier's top storey's windows, doors, vents and an artist's details on bare wall stay under it (windows by the margin they keep under a band).
- **Floor bands:** *Ground floor only* (base; the old generator's ground-floor strip), and *Colour*, its own colour (unset, the trim, or a wall shade with *Shade of the wall*).
- **Plinth** (base; off is a height of 0): its *Height*, how far it *Stands out* (4 cm by default, as it always was; the old generator's bottom strip depth), and its optional *Colour* (unset, it's a shade of the wall, as before). Where a door cuts it (8 cm either side of the door), a plinth standing out more than the prototype's 4 cm is closed, or it would show into itself; the prototype's sliver is left as it draws it, as are the ends against a party wall. Ground-floor windows start above a plinth you've set.
- **Ground floor walls** (base, under *Colours*): the ground storey's own wall colour. Unset, it's the walls'. The plinth (without a colour of its own), a band in a wall shade and brick patches on the ground storey are shades of it. Courtyard walls keep the walls' colour.
- **Foundation** (base): a band along the foot of the ground floor, under the doors too, in its *Colour* (with no foundation, under *More colours*, for a set-in door's threshold). *Below ground* sets how deep it goes (for terrain that isn't flat), *Above ground* how much of it shows, and *Stands out* how far it reaches out from the wall (0.01 to 1 m; 8 cm by default).
- **Roof edge** (*Roof* card, a flat roof; the old BuildingGenerator's roof insert): a cap on the roof's edge, of one of three *Kinds*, each set by numbers that are all positive, which the layout's signed ones follow. In the layout, `capH` above 0 makes it stand up from the roof (2 cm at least, so a strip alone lies on nothing), below 0 hang from the roof's edge; `capDepth` measures its outer side in from the wall's face, below 0 an overhang. *Top strip* (*Thickness* for *Set in*) is the band round its top in its *Colour* (unset, the trim), the rest of its top in the roof's colour; *Underside* colours its overhang's underside (unset, the cap's colour shaded as a soffit is). The kinds (`RoofCap`):
  - **Slab** (`raised`: *Height*, and *Overhang* or flush): a slab on the walls' top from the wall's inner face (or further in, for a wider strip) out past them by the overhang. On a roof nothing comes up through (no stairs, lift or bulkhead, no courtyard or atrium: `RoofCap.Whole`), the whole roof rises with it to its top, a lid, as the old generator's roof insert (`RoofCap.Lid`; the roof is walked on there too); on one with, the roof keeps its height inside it, for the doors onto it. It takes the curb's place; with *Parapet* on, the parapet stands on it. It stops where the parapet does: along party walls, at corners cut for a neighbour, and at bulkheads flush with the wall.
  - **Hanging** (`fascia`: *Hangs down* and *Overhang*, `capH` below 0): a strip hanging that far from the roof's edge, out past the wall, under the curb or parapet, which stay. A cornice goes under it.
  - **Set in** (`inset`: *Height* and *In from the edge*, `capDepth` above 0): on a roof nothing comes up through, a solid block that far in from the wall's face, as the old generator's inset roof insert, its top the roof's colour with the strip's width round it in the cap's; otherwise a ring standing on the roof that far in (at least to the curb's inner face), the strip's width thick (or the wall's), cut along party walls and flush bulkheads and round every bulkhead, stair opening, courtyard and atrium on the roof, so the roof stays walkable and its bulkheads stand clear. Either is the outline offset with Clipper (mitred), so a concave outline, round corners and an inset bigger than an edge come out right, where the old generator's crossed itself.
  - A cap of any kind but a fascia takes the 30 cm curb's place round the roof's edge, as the old generator has none. A slab covers the walls' top itself; round a cap set in, the walls' top is closed flush with the roof, in its colour round a block (seen from above, a roof is closed out to the walls' outer face whatever its edge: `RoofCapTests.SeenFromAboveTheRoofIsClosedOutToTheWalls`).
  - LOD1 has it as LOD0 does. LOD2 lifts the massing's top by a raised cap and paints the cap's colour in its height (the overhang's few decimetres don't show at that distance), draws an inset ring's sides and top in the trim's colour and the roof's (or the trim's with a strip), a block's top the roof's, and leaves a fascia out.
- **Brick patches:** at *Amount*, small groups of bricks laid on the walls, about one group per 2.5 m² at 1. They keep clear of openings, heads, the band and the plinth, and come back in the same places on every rebuild.

LODs:
- LOD0 has all of these.
- LOD1, the lean shell, has the band, cornice, plinth and foundation, the ground storey's colour, and the plain door's panel.
- LOD2's shader follows the band's height and colour (its own colour too), paints the ground storey in its own colour, and paints the cornice on the tier's top storey, which is a run of its own when it has one (its windows are shorter). It doesn't show the strips' depth, nor the plinth (it never did). The extra colours are looked up only on the rows that have them, from parameter texels the shader already reads, so a massing without them costs nothing more.

Layout keys: `head`, `panes`, `frames`, `bandH`, `bandDepth`, `bandWall`, `bandGround`, `corniceH`, `corniceDepth`, `plinthH`, `plinthDepth`, `foundation`, `foundationH`, `foundationOut`, `bricks`, `capH`, `capDepth`, `capStrip`, `recess`, `doorRecess`, `doorRecessOffset`, `recessBack`, plus the colours `frame`, `foundationColor`, `plinth`, `groundWall`, `band`, `cornice`, `cap` and `capRim` (`FacadeDetailTests`, `FacadeStripTests`, `RoofCapTests`, `RecessTests`). The strips', cornice's, ground wall's and roof cap's colours each have a slot of their own (`ColorSlot.GroundWall`, `Band`, `Cornice`, `Cap`, `CapRim`; docs/COLOURS.md §3.3); unset, a style draws with the slot they follow, so its vertices are as before.

**Checks:**
1. Arch heads, 2 × 3 panes, Frames, Band 0.65 m in a wall shade, Plinth 0.8 m with a grey Plinth colour, Foundation, Brick patches 0.6: the building looks like the artist's.
2. Set Foundation *Below ground* to 1 m and lower the ground (or lift the site): the foundation shows down to it.
3. On an L-shaped building set *Bay width* to 1.5 m and *Bays* to *Fixed width*: each wall has as many windows as whole 1.5 m bays fit on its outside, centred on it, at LOD0, LOD1 and LOD2 alike; AC units sit under windows and vents between them; one Undo puts the bays back to *Fill the wall*.

**Blank, quiet and bare walls** (`EdgeState`, Storey's own, for the old BuildingGenerator's per-edge states). *Details › Place by hand › Blank wall* has three choices for what a click makes a wall:
- *No windows* (**blank**, as before): no windows; doors placed by hand stay, as do the bands, plinth and details. The old generator's *suppressed* edge.
- *No details* (**quiet**): no automatic details (AC units, vents, an artist's Facade Details with a rule) either; hand-placed ones stay. The old generator's sides left out of its *Detail Sides* mask.
- *Bare*: no floor bands or plinth either, a plain wall; its foundation and the parapet stay. The bands and plinths of the walls each side end square at its corners: closed where they stand out past a convex corner, against the wall at a concave one. The old generator's *blank* edge.

Click a wall that is already so to get its windows back. *Whole side* sets every wall of the outline facing *Front* (+Z), *Back*, *Left* or *Right* (by the larger part of its outward normal, as the old generator's sides) at once, leaving walls with more taken away as they are (`Facades.SetSide`). The old generator's per-building side mask is not a setting of its own: a converter sets the sides it leaves out to quiet walls, so each wall says what it has, can be changed one by one, and keeps its state through outline edits (corners added, removed, merged or cut, setbacks added or moved); a mask would silently change which walls it means when a wall is turned past 45°.

Layout keys `quiet` and `bare` (edge lists, beside `blank`, on the building and on a setback's floor; written only when there are any). A quiet or bare wall is in `blank` too, so the prototype shows it without windows. At LOD2 a blank or quiet wall paints no windows, as before, and a bare one no band either (a parameter row of its own, made only when a run has a bare wall).

**Bays** (*Windows › Size and spacing*, before *Bay width*; `bayFit`, Storey's own, for the old BuildingGenerator's tiles). *Fill the wall* (the default, as before): as many bays as *Bay width* rounds to, stretched to fill the wall between its corners, a window centred in each. *Fixed width*: as many whole bays of *Bay width* as fit on the wall's outside face, corner to corner (each end on the corner's bisector, as the walls are mitred), each exactly that wide and the leftover split equally between the ends, as the old generator lays out `floor(edge / tile width)` tiles; a window is centred in each. An artist's window of its own size keeps it however narrow the bay (*Fill the wall* fits it to the bay less 50 cm); the built-in windows take *Window width* up to the bay less 50 cm, as before, and Ribbon and Curtain span the bay. A window nearer a corner than the 14 cm windows keep from the neighbouring wall (its dressing too) is moved in that far, and left out if it would then meet the window next to it. Doors, lifts and blank walls keep windows away as before. The bays are worked out from the storey's outline wherever openings are laid out (`Facade.Ops`: LOD0, LOD1, details by rule, Problems, the editor's tools; courtyard walls on their face into the court), so details keep clear of the same windows LOD0 builds, and LOD2 paints the same bays (an artist's window of its own size at its own width). Layout key `bayFit` (`"fixed"`), written only when fixed.

| BuildingBuild | Storey |
|---|---|
| `_suppressedEdges` | `blank` |
| `_blankEdges` | `bare` (and `blank`) |
| `_detailSides` without a side | its walls `quiet` (and `blank`), unless already `bare` (`Facades.SetSide`) |

### 6.8 Windows

Windows show a room behind the glass, after the project's *Window* shader graph. Add **Storey Windows** (*Add Component › Storey › Storey Windows*) to any object in the scene and give it the room atlas. Without the component, or with no atlas, the glass is the plain dark glass of before.

- **Rooms:** each window shows one cell of the atlas (*Atlas grid* columns × rows) as a box behind the pane, *Depth* times the window's width deep (1 a cube), each cell laid out in thirds (ceiling, floor and side walls round the back wall), as the shader graph's `WindowInteriorParallax` node has it since October 2026. Each window picks its own room and is mirrored at random.
- **By day:** the room is lit by the main light's colour times *Tint*.
- **At night:** as *Night blend* rises from 0 to 1, more windows light up, up to *Light chance* of them. Each lit window takes a lamp colour between *Light* and *Light 2*. A day and night system can set `StoreyWindows.NightBlend` instead of the field.
- **Glare:** an optional texture over the glass, at *Glare strength* (0.03 in the shader graph).

Which room a window shows, and when it lights, comes from a number worked out from the window's centre on the wall. LOD0 and LOD1 bake it into the pane's vertices, and LOD2's shader works it out the same way, so a window keeps its room and its light across an LOD switch.

Limits:
- It applies to the panes that aren't see-through: shell and filled storeys in LOD0, every window in LOD1, and LOD2's painted windows. A walk-in storey keeps its see-through glass, because the real room is behind it.
- The rooms are fogged as a project's lighting fogs (`STOREY_FOG`); Storey's own PBR lighting doesn't fog.
- `StoreyWindowParallax` in StoreyWindow.hlsl is the shader graph's `WindowInteriorParallax_float`, transcribed; a project shader can put its own node in instead (`STOREY_WINDOW_PARALLAX`), and its own night blend (`STOREY_WINDOW_NIGHT_BLEND`), as the Color Palette Lit sample does with the project's node and `_GlobalNightBlend`.
- In LOD2, a window whose painted rectangle is a little off the real one can, rarely, pick a different room than in LOD1.

**Checks:**
1. Add Storey Windows with the room atlas and its grid. Shell and filled storeys show rooms, each window its own, and they move in perspective as the camera moves.
2. Drag *Night blend* from 0 to 1: windows light up one by one in the lamp colours.
3. Zoom out through LOD1 and LOD2: each window keeps its room and its light.
4. Remove the component: the glass goes back to plain dark glass.

### 6.9 Artist-made windows and doors

An artist can make a window or a street door as a prefab of several objects, or as one mesh. Every building whose style picks it gets one in each opening, in place of the generator's own. The design is docs/OPENINGS.md.

**From a prefab**, as Flamingo's old BuildingGenerator takes them:
1. Build it as a prefab: any objects with a MeshFilter and a MeshRenderer, each where you want it, nested prefabs and variants too. +Y is up and +Z out of the wall, in metres.
2. Add **Storey Cutout** to the prefab's root (*Add Component › Storey › Storey Cutout*). It is the hole in the wall the window fills:
   - *Size* and *Center* are in the root's X and Y, so the pivot can be anywhere. *Wall face* is where the wall's outer face is along Z; the wall is 0.3 m thick (`Dim.T_EXT`), behind it.
   - Adding it fits the hole to the meshes. *Fit to* fits it to one object's meshes instead (the frame, so shutters and a sill stay outside the hole).
   - While the root is selected, the Scene view draws the hole, the reveal to the wall's back, and the wall's face round it.
   - Without one, the meshes' bounds are the hole.
3. For a door, list the objects that are the door itself under *Leaf*. They are left out, with everything under them, on a walk-in ground floor, where the player walks through the doorway.
4. For **Slice** (below): set *Borders*, the slice lines in from the hole's left, right, bottom and top, as thick as the frame's members, so they keep their thickness. List objects that must never stretch (a keystone, a lamp, a house number) under *Keep shape*. The Scene view draws the slice lines dotted.
5. Select the prefabs and choose *Assets › Create › Storey › Window or Door from Prefab*. Each gets a Storey Opening asset beside it, fitted by *Slice*. It is a door if its name says so or it has leaf objects.
6. Check its **Parts**: one for each material in the prefab, each taking one of the style's colours, guessed from the material's name (*Glass*, *Frame*, *Sill* or *Trim*, *Door* or *Leaf*, *Handle* or *Metal*, *Wall*). The materials themselves aren't used. On a door, *Leaf* makes a part the leaf wherever it is.
7. A part can keep a colour of its own instead: pick *Its own colour* and a palette colour (Color Pipeline's picker). It keeps that colour whatever the style, and follows palette edits and a building's remap like the style's colours. Glass always takes the style's, because it is the window shader's.

**Prefabs coloured by Color Pipeline's Model Remapper** (one material, each vertex's palette index in UV channel 3): their parts are one for each palette colour, named after the palette entry, and each keeps that colour. A Color Pipeline *Color Remap* on one of the prefab's renderers is kept too: that renderer's parts keep the colour it remaps them to (`StoreyColorField.RemapOf`), as the old generator's window prefabs recolour their glass. So they look as they did in the old solution. A part named for glass takes the style's Glass instead. Any part can be given a style colour, so that the building's style colours it.

What is read, and what isn't:
- Active objects with an enabled MeshRenderer, each where it is in the prefab. The root's position and rotation don't count and its scale does, as when Unity instantiates a prefab. A mirrored object (a negative scale) is turned the right way out.
- Inactive objects, objects tagged EditorOnly, other renderers and other components (lights, colliders, scripts) are left out. The inspector lists them.
- A LOD Group's LOD 0 is the window and its LOD 1 is LOD1's. *LOD1 prefab* takes LOD1's place.
- The bake leaves out the faces nobody sees: those inside the wall round the hole, and those on the wall's faces turned into it. It joins up what the model split (UV seams).

**From a mesh**, as before:
1. Model it with the pivot at the bottom middle of the hole in the wall: +Y up, +Z out of the wall, in metres. Z 0 is the wall's outer face, and negative Z goes into the reveal; the wall is 0.3 m thick. Each submesh is one part. Name its material after what it is, as above.
2. *Assets › Create › Storey › Window or Door*, then set *Mesh*. The parts take their colours from the material names, and *Size* (the hole) comes from the mesh's bounds. Tick *Door* for a street door.

Either way, *Faces back* turns round one made facing −Z, and the parts take the building's palette colours:
- **Glass** shows the window shader's rooms on shell and filled storeys (§6.8), and is see-through on walk-in ones.
- **Leaf** marks the door itself, left out on a walk-in ground floor.
- **Its own colour** keeps a palette colour whatever the style. A project can have 232 such colours across its windows, doors and facade details (`KindColors`). Past that, a part takes its style colour.

In the Facade tab, pick it under **Window**, first in the *Windows* card, or **Door**, first in the *Doors* card. *Built-in* goes back to Storey's own. *Window* is what fills each opening; *Openings* is where the openings are and how big (*Standard*, *Tall*, *Ribbon*, *Glass wall* or *None*, described in its tooltip; in the layout `punched`, `tall`, `ribbon`, `curtain` and `none`).

**Several windows** (docs/OPENINGS.md §7.1): *+ Another window* adds a row under *Window*. Each row is a window (*Built-in*, *None* for no window there, or an artist's), its weight, its share in percent, and × to remove it. Each opening takes one, picked by weight, the same on every build and different from building to building. *Variety* keeps them alike *Per column*, *Per storey* or *Per wall* instead of *Per window*, or gives every building the *Same pattern everywhere* (docs/OPENINGS.md §7.1); it shows while a list has more than one entry. With one row left, the style has one window again. On the base, *Ground floor › Own windows* gives the ground floor rows of its own (starting as the floors above's); off, it has theirs. *Door* has rows the same way (*+ Another door*): a door placed as *Any of them* is one picked for its wall by weight, every such door on one wall the same one, and each row has a *depth* of its own (metres; empty: the style's); *Door to place* puts one of them in by name. *Height* (*Centred* or *Sill height*) shows when any of the windows keeps its own size; the *Built-in window* group (*Sills*, *Heads*, *Glazing bars*, *Frames*) shows only while one of them is the built-in window.

The inspector says how many objects, parts and triangles it has and where its hole came from, and warns of what will cost too much or look wrong:
- more triangles than the most dressed built-in window (140, with arch heads, frames and glazing bars; a block of six storeys has about 156 windows), or a LOD1 of more than 12 (most buildings on screen are at LOD1, where the plain pane is 2);
- a window's LOD1 that reaches into the wall, so LOD1 cuts the wall round every window (LODs, below);
- faces on the wall's outer face round the hole, facing out: they flicker against the wall;
- anything behind the wall's back: it shows inside the rooms;
- a share of the hole left open (on a shell or filled storey it shows into the building), and glass deeper than 16.5 cm (a shell storey's reveal stops there);
- a window without Glass (no rooms behind it), and a door without a leaf.

*Check on a building* builds a 20 × 12 m block of six storeys with it, gives its triangles against the built-in window's or door's, and lists faces that overlap, at LOD0 and LOD1. *Open Prefab* opens the prefab; saving it bakes the asset again, as does reimporting anything it uses (a mesh, a material, a nested prefab, a variant's base). If Unity says a mesh isn't readable, tick *Read/Write* on its model.

**Fit** says how it goes into each opening:
- **Stretch:** stretched to the opening, all of it. The style's *Openings*, *Window width* and *Bay width* decide where openings are and how big. For a window that is only a window.
- **Slice** (made from a prefab, it starts so): fitted as 9-slice scaling fits a sprite. What lies between the slice lines stretches; what lies outside them keeps its size and moves with the hole's sides. So shutters keep their width and follow the hole's sides, a sill or a flower box gets longer but not taller, a pediment keeps its height, a frame's members keep their thickness (with *Borders* as thick as they are), and *Keep shape* objects move unscaled. Triangles crossing a slice line are cut there when baked, so a curve bends only at the lines. One prefab then fits every window type and width a style has.
- **Keep size:** the hole takes the window's own size, as in the old solution. The style still decides where windows go: *Bay width* and *Bays* across (with *Fixed width* bays, §6.7, the window keeps its width however narrow the bay), and *Height* how high in each storey they sit. *Openings* then offers only *As big as the window* or *None*, and *Window width* doesn't apply (the Facade tab leaves it out). *Sill height* starts at the sill of the style's window type (Standard 90 cm, Tall 40 cm, Ribbon 95 cm, Glass wall 10 cm). *Height: Centred* (`winCentre`, Storey's own, for the old BuildingGenerator, which centres its window prefabs in each floor) puts each window's middle halfway up its storey instead, the ground storey's in its own height, and *Sill height* goes; a plinth, a band, a cornice and the window's own dressing keep it clear as they do a sill. Over a plinth, the ground floor's windows stay 15 cm above it, and a sill so high that 40 cm or less of the window fits under the storey's top leaves that storey without windows, at every LOD. Where an opening has to give (a bay too narrow, a band), the window is sliced into it. LOD2's painted windows take the same size. A street door takes its own height; on a shell storey it keeps its own width too (a shop's double door as wide as it is), and on a walk-in storey the standard doorway's width (1.6 m, which the entrance tools and the player walking through assume), sliced into it.

Heads, sills, frames and glazing bars aren't drawn where an artist's window is: it is the whole window or door.

What it has round its hole (shutters, a sill, a flower box, a surround, a pediment) is kept clear, as far as it reaches past the hole:
- Windows keep it 14 cm from a neighbouring wall, as they keep their holes, and 10 cm from a door's (their holes stay 30 cm apart).
- It stays 5 cm above the floor and above a plinth (ground-floor windows start 15 cm over a plinth, or as far as their sill needs), and 5 cm under a band and the storey's top.
- AC units go under it, vents between windows' dressings, awnings over it (and past shutters), and hand-placed details and brick patches keep clear of it.
- Where two windows' dressing would meet, **Problems** says so, and which bays the window needs (*Bay width*).

LODs:
- LOD0 places the window.
- LOD1 places its LOD1 (*LOD1 prefab*, the LOD Group's LOD 1, or *LOD1 mesh*) if it has one. Otherwise it keeps its plain pane, flat on the wall, with the recess the shader draws.
  - A LOD1 that stays in front of the wall's face (shutters, a sill, a pediment) lies on the flat wall too, at the cost of its own triangles. What of it is over the hole must stand at least 3 cm out, as far as the plain pane, or it would flicker against the flat wall behind it.
  - Any other LOD1 gets a hole cut for it in LOD1's wall, about 12 triangles a window more. The inspector says which it is.
  - Where a LOD1 leaves its hole open (shutters alone, a frame without glass), the plain pane fills it. One that covers it (its own glass, a door's leaf) has none.
- LOD2 keeps its painted windows.

The site lists the windows and doors its layout uses (*Openings*, kept by the editor when you save), so builds include them. The geometry is baked into the asset, and the prefab and mesh references are the editor's: they don't ship.

A style names its window and door by the asset's id, kept through renames and moves. The editor keeps every id unique:
- A new window or door takes its asset's GUID as its id.
- A copy (Ctrl+D, or the file copied in) gets an id of its own as soon as it's imported, so it's a window of its own. Before, a copy kept its original's id, and the two were one window: a style showed whichever loaded last.
- The original keeps its id, so the styles that picked it still show it. The original is the one whose GUID the id is; for one made before ids were GUIDs, the one that wasn't just imported.
- Two that already share an id (copies made before) are split when the editor loads: the first by path keeps it, and the Console says which got its own. A style that picked that one may show the other: pick it again.

Facade details follow the same rule (§6.12).

Engine-free: `OpeningKind`, `OpeningKinds` (`Register`, `Of`, `Place`, `Check`), `KindBake` (a prefab's objects into the opening's space, cut at slice lines), `KindPlace` (stretched, sliced, rigid), `KindColors` (colours of their own), `Validate.KindTrial` (Check on a building), `OpeningKind.lod1OnWall`/`lod1Covers` (LOD1 on the flat wall, the plain pane in an open hole), `FacadeStyle.windowKind`/`doorKind`/`sillH`/`winCentre` (layout keys `windowKind`, `doorKind`, `sillH`, `winCentre`, the last written only when on) (`OpeningKindTests`, `KindBakeTests`, `KindColorsTests`, `OpeningFitTests`, `SliceTests`); `AssetIds`, the id rule (`AssetIdsTests`). Unity: `StoreyOpening`, `StoreyCutout`, `StoreySite.openings`, `BuildingTable.SyncKindColors`. Editor: `StoreyOpenings`, `StoreyOpeningBake`, `StoreyCutoutEditor`, `StoreyPartColours`, `StoreyAssetIds`; `StoreyColorField.PaletteEntryAt`, installed by the Color Pipeline integration.

**Checks:**
1. Make a window from a model with Glass and Frame materials. Its parts list says Glass and Frame, and its size matches the model.
2. Pick it under Window on a shell building: every window is the model, stretched to the openings, with rooms behind the glass.
3. On a walk-in building the glass is see-through.
4. Make a door with a Leaf part and pick it under Door. On a shell building the door shows; on a walk-in building the leaf is gone and you can walk in.
5. Change the model in your DCC tool and reimport it: the buildings update.
6. Duplicate the window and give the copy another mesh. Pick the copy under Window: buildings show the copy, and those that picked the original still show the original.
7. Enter Play mode, and make a build: the windows and doors are there.
8. Give three window prefabs a Storey Cutout and choose *Window or Door from Prefab*. Each asset's hole matches its cutout's gizmo, its parts are its materials, and *Check on a building* finds no faces that flicker.
9. Add a Storey Cutout to a prefab: it fits the meshes, and the Scene view draws the hole. Move a nested sill prefab and save: the buildings update.
10. Give a door prefab a leaf object: on a walk-in building the leaf is gone, the frame stays.
11. Make a window from a prefab the Model Remapper coloured: its parts are its palette colours, and buildings show them whatever their style. Give one part a style colour: it follows the building's style. Change that palette entry in Color Pipeline: the windows change colour without a rebuild.
12. Set *Fit* to *Keep size* on a window 0.9 m wide: every window's hole is 0.9 m, at LOD0, LOD1 and LOD2 alike, and *Window width* isn't shown. *Openings* offers *As big as the window* and *None*; pick *Height: Sill height* and move *Sill height* and every window moves up and down, at all three LODs.
13. Give a window shutters 45 cm wide: the outermost windows' shutters stay clear of the corners; set *Bay width* so they would meet, and Problems lists it.
14. Make a shuttered window *Slice* with 6 cm *Borders*, and a keystone under *Keep shape*. Pick it on the brick, stucco and warehouse presets: the shutters keep their width, the frame its thickness and the keystone its shape, whatever the windows' size; *Check on a building* finds no faces that flicker.
15. Give that window a LOD1 of its shutters and sill only, in front of the wall's face. The inspector says LOD1 lies on the flat wall; zoom out to LOD1 and the shutters and sill show beside the plain pane. *Check on a building* gives LOD1 the built-in window's count plus the LOD1's triangles for each window. Move the LOD1's sill 2 cm into the wall: the inspector says LOD1 now cuts the wall.

### 6.10 The site inspector's layout

The site's inspector reads top to bottom in the order you work: which layout, which building, what's wrong, which mode, then that mode's settings. The rare settings come last.

1. **Layout**: the `.storey` file. Selecting the site starts editing it (§4).
2. **Building** box: pick a building, *New ▾* (a template: the package's five shapes, or the project's), *Duplicate*, *Delete*, its name, *Isolate* and *Save as template…*, and *Colliders* (on: the building gets its collider as Site settings say; off: none, so physics and raycasts pass through it; docs/PLAY.md §5). Everything in this box acts on the building picked at its top.
3. **Problems**: one quiet line when there are none, or a warning foldout with the count.
4. **Shape / Facade / Interior**: three large buttons, each with an icon and a line saying what it's for. The selected one is tinted, with an accent bar along its bottom. Where any line doesn't fit its button, none show in the buttons; the picked mode's line is under the bar anyway.
5. The mode's settings in **cards**, one for each part of the building. A card is a box with its icon and name in a header, a stripe of its colour down its left side, and on the right of the header a short line of what is set (*Own size · bays of 2.74 m*, *Flat · slab edge*), which shows folded too. A click on the header folds it. Inside a card:
   - *sub-headers* (small, muted, with a hairline) group its settings: *Size and spacing*, *Ground floor*, *Depth in the wall*, *Built-in window*;
   - a part that can be switched on or off is a **feature**, its name in bold by a check box (*Cornice*, *Floor bands*, *Plinth*, *Foundation*, *Brick patches*, *Set into the wall*, *Parapet*, *Roof edge*, *Dormers*), and its settings are indented under it only while it's on;
   - a choice of two or three is buttons side by side (*Bays*, *Height*, roof edge *Kind*, corner *Cut*);
   - only the settings that apply show: *Window width* for Standard and Tall openings, the *Built-in window* group while the built-in window is picked, *Variety* while a list has a choice to make, *Opening margin* for doors of your own, *Shade of the wall* for a band with no colour of its own. A quiet line says why something isn't there (*Your window brings its own frame, sill and glazing bars.*).

   The cards:
   - **Shape:** *Placement* (how high it stands, which the move gizmo sets; Snap to ground), *Outline* (setbacks, templates to start from), *Corners*, *Courtyards and atria* (folded until the building has one), *Storey heights*.
   - **Facade:** *Style for* (with setbacks) above the cards, then *Style* (preset, Save as preset…), *Windows* (the window, or several with their weights and *Variety*; *Size and spacing*: openings, height for a window of its own size, window width, bays, bay width; *Ground floor*: its layout and own windows; *Depth in the wall*; *Built-in window*: sills, heads, glazing bars, frames), *Doors* (base: the door, or several; its colour; *On the walls*: *Place doors* and, with several, *Door to place*; *Depth in the wall*, opening margin), *Walls* (cornice, floor bands, plinth, foundation, brick patches, each with its colour), *Colours* (*More colours* folded), *Roof* (type; pitched: pitch, eaves, dormers; flat: parapet, roof edge), *Details* (*Placed for you*: AC units, vents and every Facade Detail with a rule, each as a chance; *Place by hand*: Blank wall with its No windows / No details / Bare choice and *Whole side*, every Facade Detail, Bridge, as many to a row as fit), *Bridges* (when it has any).
   - **Interior:** *Inside* (walk-in or shell; the colours inside), *Floor* (the storey, how its walls show, rooms or filled, its height; on the roof, which stairs and lifts reach it), *Tools* (and what is selected with them).
6. **Site settings** (folded): materials, the LOD shown, colliders, and the artist's windows, doors and facade details the layout uses.

Cards and foldouts remember whether they're open for the rest of the editor session (`SessionState`). A Facade Detail's button shows its name in words, without what all the project's details' names start with (`Visual_BuildingDetail_EdgeBricks` is *Edge Bricks*), and short of room without its asides in brackets, then cut with an ellipsis; its tooltip has its asset's name. The buttons in a row share its width, so the inspector never scrolls sideways.

The colours: Style purple, Windows blue, Doors amber, Walls terracotta, Colours pink, Roof teal, Details green; the Shape and Interior cards take the same set. Each has a light-skin shade.

The rules follow Unity's editor guidelines: groups set apart by space, headers and a common region, and progressive disclosure for the less-used settings. They also follow the Gestalt grouping principles (proximity and common region).
- [Unity Foundations: content organisation](https://foundations.unity.com/patterns/content-organization), [Unity Foundations: foldout](https://foundations.unity.com/components/foldout)
- [NN/g: common region](https://www.nngroup.com/articles/common-region/), [Laws of UX: common region](https://lawsofux.com/law-of-common-region/)
- [UX Planet: how to design a properties panel](https://uxplanet.org/how-to-design-properties-panel-4d562cc47da3), [Retool: simplifying the inspector](https://retool.com/blog/simplifying-retools-inspector)

Code: `StoreyInspectorUI` (`Card`, `SubHeader`, `Feature`, `Segmented`, `Note`, `Fold`, `ModeBar`, `ShortNames`, `Fit`, the `Tint` colours and the icons, drawn so they follow the editor skin).

**Checks:**
1. Select a site. The layout, the building box, the problems line and the three mode buttons are all visible without scrolling at a normal inspector height.
2. Click each mode: the button tints and the Scene view's tool changes.
3. Narrow the inspector: the mode buttons keep their icons and names, the description is below them, and nothing in the Facade tab scrolls sideways: every slider's value shows.
4. Fold the Windows card: its header still says what the windows are. Select another building: it stays folded.
5. Turn *Cornice* off: its settings go; on again, they're back as they were.
6. Switch the editor to the light skin: the icons, stripes and accents are still readable.
7. With *Colliders in edit mode* on in Site settings, turn a building's *Colliders* off in its box: its MeshCollider goes and the others stay; one Undo brings it back. In Play mode with *Colliders At Load*, it has none either.

### 6.11 Buildings at different heights

Each building has an **elevation**: its ground floor's height above the Storey Site, in metres. The whole building stands on it, interior included. Every floor height in the code (`Derived.FloorBase`) is a site height, so the walk model, the occlusion, the cutaway, bridges and the editor's tools all follow it. A layout saves `elevation` only for a raised building, so one on flat ground reads as before.

**Moving it up and down.** The Shape tool puts Unity's own move gizmo on the building, at its ground floor (`StoreyShapeTool.MoveGizmo`): deep inside its outline (`Geo.Deepest`, the point furthest from its walls, so the gizmo keeps clear of the corner and wall handles; an outline's box centre can lie on a wall), or with Unity's *Pivot* setting at the building's own origin (`pos`); along the site's axes, or with *Global* the world's. Its arrows and squares move the building across, snapping onto a neighbour's wall within 0.5 m along the axes being dragged (an arrow keeps to its line); its up arrow sets the elevation, standing it level with the site's ground or with the ground floor of a building within a metre of it as it comes within 15 cm of one (`Ground.Level`), so floors line up and the wall between them is shared all the way up. Alt drags freely, to the centimetre. Unity's own snapping (Ctrl or Cmd for increments, grid snapping) works as on any object. While it moves up or down, a label by the gizmo says how high it stands, and what it's level with. Each drag is one undo step. Shape ▸ *Placement* says how high it stands; it has no field to type it in.

**Snap to ground** casts down onto the scene's colliders (a Terrain has one) at the building's corners (30 cm in), its edges' middles and its centre, skipping the site's own colliders (`Ground.Samples`). The building stands on the highest ground found, so it never sinks into the slope. Where the ground falls away by more than 5 cm, its foundation (Facade ▸ *Walls*) reaches 10 cm past the lowest point, so it never floats (`Ground.Snap`). One undo step.

**Neighbours at different heights** share a wall only over the heights both reach: from the higher elevation to the lower top (`Party.Range.Lo` to `Hp`). The taller one still owns that band and builds it blank. Each building's storeys outside the band keep their own outside wall, windows and all: a ground floor below a raised neighbour, or the storeys of a raised building above a lower one. Two buildings that share less than half a metre of height share nothing. A storey counts as in the band if its floor is.

A template comes in at elevation 0 (it lands somewhere else); a duplicate keeps its source's.

**Not handled:** a building that steps down a slope within itself (split levels, a ground floor that's one storey on one side and two on the other). Raise it to the high side and let the foundation fill the low side.

**Checks:**
1. Put a Terrain with a slope under a site. Select a building, Shape ▸ *Snap to ground*: it stands on the high side, and its foundation reaches the ground on the low side.
2. Drag the move gizmo's up arrow: the building moves up, inside and out, and a label says how high. Bring it down near the ground: it stops level with it. Undo puts it back in one step. Walk into it with the play kit, and up its stairs: the floor clip and the cutaway follow.
3. Raise one of two buildings back to back by a storey: the lower one's ground floor along the shared wall gets windows; the raised one's top storey above its neighbour does too.
4. Undo each step.

### 6.12 Artist-made facade details

The details in Facade ▸ *Details* ▸ *Place by hand* are **Facade Detail** assets. Storey's own five ship in the runtime package's `Facade Details/` folder (AC unit, Vent, Dish, Fire escape, Awning), and the generator draws them as before. An artist can add more: a mesh, placed by hand or by a style's rules, and built into the building's own meshes like the five. So it drops with the cutaway and squashes with Sink.

1. Model it in metres, +Y up, +Z out of the wall:
   - Z 0 is the wall's outer face. Nothing should go behind it, or it shows through the wall inside, and no face should lie on it facing out.
   - Each submesh is one part. Name its material after what it is: *Metal*, *Grille* or *Louvre*, *Dark* or *Black*, *Dish*, *Rail*, *Canvas* or *Fabric*, *Glass*, *Wall*, *Frame*, *Door*.
2. *Assets › Create › Storey › Facade Detail*, then set *Mesh*. The parts take their colours from the material names, and *Size* comes from the mesh's bounds. *Faces back* turns round a mesh made facing −Z.
3. Pick where it **Goes**:
   - **On a wall**: where it's clicked, at the height clicked, kept clear of windows and doors. Its pivot is the bottom middle of its back.
   - **Up the wall**: on every storey from the one clicked up to where the wall steps back, as the fire escape climbs. The pivot is as on a wall, at each storey's floor. A door on any storey it climbs refuses it. *Stretch* fits it to each storey's height. Leave out its top and bottom faces then (a pipe needs no caps): they'd lie on the floor band's top and flicker, and the inspector warns of them.
   - **Over an opening**: over a ground-floor window or door, as the awning. Its pivot is the middle of its back, and goes 25 cm above the opening's top, clear of a window's head; an awning can hang below it. *Stretch* fits it across the opening and 25 cm past each side, and two neighbours meet in the middle.
4. **Placed by rule** (optional):
   - *Under upper-floor windows*: as AC units, its top 7 cm under the window. Never on the ground floor or a curtain wall.
   - *Between windows*: as vents, in a stretch of wall with 25 cm to spare each side, its top 45 cm under the ceiling.
   - *Over ground-floor windows*: one put over a door is placed by hand.

   - *Scattered on the walls* (Storey's own): as brick patches, an even spread over every wall, so many to the square metre, each keeping a gap from the openings and the heads over them, the band, the plinth, the wall's ends, party walls and everything else on the wall. Its pivot can be anywhere: its size places it.
   - *Along the top of the wall* (Storey's own): in a row along the top of each tier's wall, so many to a bay (the style's *Bay*), the bays centred on the edge as the old generator laid its tiles. Its pivot goes at the top of the storey's wall, so the mesh hangs below it. Not where the tier above carries the wall on.
   - *Along the foot of the wall* (Storey's own): the same along the foot of the ground floor, its pivot on the ground, never across a doorway.
   - *Up the corners* (Storey's own): stacked up each corner of the building, so far apart, over the whole run of storeys the corner goes up (its tier's, those *Storeys* puts it on), the leftover split between the run's foot and its top, as the old generator stacks its vertical edge details up the whole building; each one middled in its step, and belonging to the storey its middle is on (it may reach into the next). Stacked a storey at a time, as before, each storey's leftover was a gap at every floor. It faces out along the corner's bisector, from where the two walls' outer faces meet: x along the bisector's normal, +Z out. Not where a party wall meets the corner.

   Then give it a chance per style in Facade ▸ *Details* ▸ *Placed for you*; once it has one, its settings show under its slider. Each detail has its own seed, so a rebuild puts every one back where it was. It keeps clear of what is already on the wall: what was put by hand, the AC units and vents, then the other details by rule in id order. Details in a row or up the corners share their places: at each place one draw picks at most one of them, each by its chance, so their chances act as the weights of a list (two at 50% fill every place between them; one at 30% leaves the rest bare).

   A style's settings for each detail, beside its chance (`KindRule`, Storey's own; under the detail's slider):
   - *Per m²* (`density`, scattered; 0.5): how many to the square metre of wall at 100%. Its chance scales it, so two patches at 50% each share a density as the old generator's weighted list did.
   - *Gap* (`spacing`, scattered; 0.1 m): the clear gap kept round each. Up the corners, *Every* (`spacing`; 1 m): how far apart they are up the corner.
   - *Per bay* (`per`, in a row; 1): how many to each bay, 1 to 16.
   - *Storeys* (`floors`, scattered or up the corners): *All*, *Upper* (not the ground floor) or *Ground* alone.
   - *Corners* (`corners`, up the corners): *Outward* (standing out), *Inward* (set in) or *All*.
   - *Colour* (`color`, any rule): the colour the detail's main colour (the one most of its triangles have) takes in this style, as the old generator's per-theme detail colours did. So one detail serves many styles. Unset, it keeps its own.

   A style writes a detail's chance alone as before (`"details": {"<id>": 50}`), and an object in its place when it has settings: `"<id>": {"p": 50, "density": 1.2, "spacing": 0.12, "floors": "upper", "color": "#9A4B38"}`.
5. In the Facade tab, pick it under *Details › Place by hand* and click a wall. Click a placed one to remove it. Placing one refuses where it doesn't fit, and says why.

Each part takes one of the style's colours, so the building's palette colours it, or keeps a palette colour of its own whatever the style (*Its own colour*, as a window's part can: §6.9). The model's materials only name the parts (docs/COLOURS.md §3.9).

LODs:
- LOD0 places the mesh.
- LOD1 places *LOD1 mesh* if there is one, otherwise the mesh itself. Give a heavy mesh a LOD1 mesh: many buildings are seen in LOD1 at once, and the inspector warns past 300 triangles.
- *Leave out of LOD1* (`DetailKind.noLod1`, Storey's own) leaves it out of LOD1 altogether: for small details, brick patches above all, that LOD1's distance can't show. The generator's own brick patches are LOD0's alone too. Measured (`DetailRuleTests.BrickPatchesCostLodZeroLittleAndLodOneNothing`): a two-brick patch as the old generator's Bricks01 (20 triangles, 40 vertices) on a 20 × 12 m block of six storeys (6,210 LOD0 triangles) gives 67 patches at 0.15/m², 202 at 0.5/m² and 487 at 2/m² (a 5 cm gap): +1,340, +4,040 and +9,740 LOD0 triangles (+2,680, +8,080, +19,480 vertices). In LOD1, whose shell is about 1,000 triangles, the same would more than double or treble it; left out, it costs LOD1 nothing. LOD0's meshes are split at 65,535 vertices, so even the densest stays in one or two parts.
- LOD2 has none.

The site lists the details its layout uses (*Facade Details*, under Site settings), so builds include them. The editor keeps the list: when you place one, give one a chance, and when the layout saves.

The mesh is copied into the asset when it's set, and baked again when its model is reimported. If Unity says the mesh isn't readable, tick *Read/Write* on the model.

A new Facade Detail takes its asset's GUID as its id, and a copy, of one of the five too, gets one of its own as soon as it's imported, so it's a detail of its own (the rule in §6.9). A layout keeps a detail whose asset is gone: it isn't built, and the style's chance for it shows as *Missing detail* until set to 0.

**From a prefab** (Storey's own): select prefabs and use *Assets › Create › Storey › Facade Detail from Prefab*, or give a Facade Detail a *Prefab* (it wins over *Mesh*). The prefab is flattened into the detail as a window's is (docs/OPENINGS.md §3, `KindBake`): every active object under it with a mesh, where it is in the prefab (its root's position and rotation don't count, its scale does). Its root's origin is the pivot and its Z 0 the wall's outer face, +Z out of the wall. Against a whole wall: what lies in the wall, or on its face turned into it, is left out, and a face lying on the wall's face turned out is counted and warned of. Each material is a part, and a model the Model Remapper coloured (palette indices in UV channel 3) gives a part for each palette colour, which keeps that colour as its own. So the old generator's detail prefabs (Color Palette material, indices baked into the mesh) come over with their colours, and a style's *Colour* for the detail recolours its main one. *LOD1 prefab*, or else the prefab's LOD Group's LOD 1, is LOD1's. Size comes from what the bake kept. One made from the menu that is small and shallow (no deeper than 10 cm, at most 0.5 m² across) starts *Left out of LOD1*. Saving the prefab, or reimporting anything it uses, bakes it again. A *Color Remap* on a renderer is kept, as a window's is. Other components (colliders) aren't carried over, and the inspector says so. *Check on a building* tries it on a 20 × 12 m block of six storeys, by its rule at 100% or put by hand, and counts triangles and flickering faces (`KindTrial.Run(DetailKind)`).

Not done: palette icons, LOD2 meshes, colliders (the five have none either), and triangle budgets beyond the LOD1 warning.

Engine-free:
- `DetailKind`, `DetailKinds` (`Register`, `Get`, `Place`, `BuiltIn`), `Details.Rule`, `Details.OverLift`.
- `DetailData.kind`: the detail's id, `ac`, `vent`, `dish`, `escape` and `awning` for the five, as before.
- `DetailRules.kinds`: layout key, the detail's id beside `ac` and `vents` in a style's `details`.
- `DetailRules.rules` (`KindRule`: `density`, `spacing`, `per`, `floors`, `corners`, `color`), the rules `Scatter`, `AlongTop`, `AlongFoot` and `Corners`, `DetailKind.noLod1` and `DetailKind.main`, `Details.Item.corner`. Baked layout format version 4.
- `KindBake.Hole.None`: a detail's bake against a whole wall. `KindTrial.Run(DetailKind)`.
- Tests: `DetailKindTests`, `DetailRuleTests`, `FacadeDetailAssetTests`.

Unity: `StoreyDetail` (`prefab`, `lod1Prefab`, `leaveOutOfLod1`, `Part.key`), `StoreySite.facadeDetails`. Editor: `StoreyDetails`, `StoreyDetailBake`.

**Checks:**
1. Make a detail from a model with Metal and Grille materials. Its parts say Detail metal and Grille, and its size matches the model.
2. Place it on a wall by hand. The ghost has its size; it's refused across a window; clicking it removes it.
3. Give it *Under upper-floor windows*, then 50% under *Placed for you*. It shows under about half the upper floors' windows. Set AC units to 100%: it gives way to them.
4. Make one *Over an opening*, with Stretch and *Over ground-floor windows*. It spans each ground-floor window, and two close windows' meet in the middle.
5. Make one *Up the wall* with Stretch from a capless pipe segment: it runs up every storey of the wall.
6. Zoom out to LOD1: the LOD1 mesh shows. Change the model and reimport: the buildings update.
7. Duplicate the asset: the copy is a detail of its own in the palette.
8. Enter Play mode, and make a build: the details are there.
9. Select the old generator's `Visual_BuildingDetail_Bricks01` and `Bricks02` prefabs and use *Facade Detail from Prefab*. Each keeps its palette colour (*Its own colour*), is left out of LOD1, and *Check on a building* says no faces overlap.
10. Give both *Scattered on the walls*, then 50% each under *Placed for you* with *Per m²* 1: patches spread evenly over the walls, clear of windows, the band and the plinth; *Storeys: Upper* takes them off the ground floor. Turn on *Recolour*: both patches take its *Main colour*, on this style alone. Zoom out to LOD1: they're gone.
11. Make `TopBricks` a detail *Along the top of the wall* at 100% with *Per bay* 2: two to each bay along the roof line, none where a setback's wall goes on up. `EdgeBricks` *Up the corners*, *Inward*, on an L-shaped building: a stack up the corner set in, facing out along its bisector.
12. Undo each change: the style writes the chance alone again once its settings are back to their defaults (`"<id>": 50`).
