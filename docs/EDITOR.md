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
     - On a straight flight, rails run along both sides of the opening on each storey above, and across its front on the top storey. A building wall stands in for the rail on a side against it. On a flat roof they come up through a railed opening, with no bulkhead, and the parapet runs on past them.
     - The arrow shows which way the flights go up, and the dotted line marks the walkway. **Reverse** turns them round (the walkway changes side too). Only the sides can share a building wall, because both ends are landings.
   - Drag a core, a wall point and a "+": the walls and the building follow the pointer, and the drag is one undo step. Double-click a point to join or remove walls.
   - Click stairs or a lift without dragging: it is selected (anywhere on it, not only its handle). Drag one past an outside wall or into another core: it stops there but keeps sliding along it with the pointer.
   - No key does anything Storey-specific.
   - Storey height, and the Storey Floors overlay: count, copy up, duplicate, delete.
   - **Switching floors animates.** Going up, the storeys grow up to the new ceiling; going down, they sink. The new floor's walls in front of the camera then slide down to the stub, and the old floor's slide back up (0.22 s, as in play). Selecting another building, or opening the Interior tab, shows its floor at once.
   - **Camera follows** (next to the floor list, on by default): the Scene view's camera eases up or down with the floor.
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
- Doors, details and blank walls on the two edges keep their places; those in the part cut away go. A cut too big for its edges, or one the outline rules refuse (cores, setbacks), says why.
- **Corner entrance** (the base outline, Chamfer): a street door in the middle of the chamfer. It needs about 2.7 m.
- The cut is ordinary corners. Drag them, or undo, as any other.

Engine-free: `Outlines.Corner`, `Outlines.AllCorners`, `Outlines.CornerPoints` (`CornerTests`).

**Checks:**
1. Click a corner of a building, choose Round with a 3 m radius: the arc shows green. *Cut this corner*: the building is rounded there, with walls, bands and windows following.
2. Chamfer 3 m with *Corner entrance*: a street door opens on the chamfer.
3. *Every corner* on a setback.

### 6.2 Mansard roofs and dormers

**Mansard** is a roof type in the Facade tab's Roof fields: a steep 70° slope rises from the eaves to the break (*Steep part*, 2.4 m by default), then a shallow hip at *Top pitch* (20°) covers the rest. Its eaves are short (at most 0.2 m), so the steep slope starts at the wall top.

**Dormers** works on every pitched roof: hip, gable, shed and mansard. *Dormers every* sets the spacing along each eave (3.5 m by default). Each dormer has:
- a front wall with a window just behind the wall below;
- two cheeks back to the roof;
- its own 45° gabled roof running back into the slope.

Only dormers that fit on their eave's slope are built, clear of hips, valleys and ridges, and on a mansard under the break. A small roof gets none. LOD2's massing leaves them out.

Layouts with a mansard or dormers are Storey's own: the prototype doesn't read them (`"roofType": "mansard"`, `"mansard"`, `"dormers"`).

Engine-free: `Roofs.Make`, `RoofType.Mansard`, `FacadeStyle.mansard` and `FacadeStyle.dormers` (`RoofFeatureTests`).

**Checks:**
1. Set a building's roof to Mansard: steep sides, a shallow top. Change *Steep part*.
2. Turn on Dormers on a hip, a gable and the mansard, then change *Dormers every*.

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

It runs once an edit has settled (never during a drag), using the meshes the site already shows. The demo street takes about 30 ms, the 78-building variants corpus about 250 ms.

Engine-free: `Validate.Problems` (`ProblemTests`). On the demo street it finds two real problems:
- Row House: a wall 10 cm in front of the stairs shuts them in above the ground floor.
- Linden Court: the room behind the lift is reached only past a 40 cm gap.

**Checks:**
1. Open the demo street: Problems lists Row House floors 1 to 3 and Linden Court's rooms. Click one: the Scene view goes there.
2. Remove a building's stairs: its upper floors are listed. Undo: the list clears.
3. Draw a wall across a room with no door: "Part of … can't be reached".

### 6.6 Building templates

**Save as template…** in the building bar keeps the selected building as an asset (`StoreyBuildingTemplate`): its outlines and setbacks, floors, rooms, stairs and lifts, courtyards, doors, details and style. Its place in the layout and its bridges aren't kept. Save over an existing template to update it.

**New ▸ From template** places one in any layout:
- It goes at a free spot near the Scene view's centre.
- It gets fresh ids, and its name with a number if the layout already has one by that name.
- Edit it as any other building: it isn't linked to the template.

A template's inspector says what it holds (storeys, size, height, cores). *Assets ▸ Create ▸ Storey ▸ Building Template* makes an empty one to save into. The asset is Storey's JSON for one building, as the prototype copies one to the clipboard.

Engine-free: `Buildings.AsTemplate`, `Buildings.FromTemplate` (`TemplateTests`).

**Checks:**
1. Select a building, then *Save as template…*: the asset appears and is pinged. Its inspector summarises it.
2. In another scene's site: New ▸ From template ▸ it. The building appears near the view, named, and editable.
3. Place it twice: "Name 2", "Name 3".

### 6.7 Facade details

**Details** in the Facade tab, from the artist's brick building. Each option is per style, so a setback can differ. A style with none of them builds exactly as before.

- **Window heads:** *Flat* (the thin head strip) or *Arch*, an arched hood over each window and door. The hood is 12 cm deep, stands 6 cm out from the wall, and rises a sixth of its span.
- **Glazing bars:** *Panes across* × *Panes up* (2 × 3 like the artist's) divides every window into panes.
- **Frames:** a frame all round each window and door, in the new *Frames* colour. Sills and heads take that colour too.
- **Band:** its height (*Band height*), how far it stands out (*Band stands out*, at least 1 cm), and *Band in a wall shade* instead of the trim colour. Windows and their arched heads stay under a band you've set.
- **Street doors** (base): *Canopy*, as before, or *Glazed*: double doors with a frame, a transom bar and a fanlight above it, and no canopy. On a filled or shell storey the doors are closed: two glazed leaves with a centre stile, bottom rails and pull handles. On a walk-in storey the doorway stays open, because the player walks through it; the frame, transom and fanlight still show.
- **Plinth height** (base), and the new optional *Plinth* colour (unset, it's a shade of the wall, as before). Ground-floor windows start above a plinth you've set.
- **Foundation** (base): a band along the foot of the ground floor, under the doors too, in the new *Foundation* colour. *Below ground* sets how deep it goes (for terrain that isn't flat) and *Above ground* how much of it shows.
- **Brick patches:** small groups of bricks laid on the walls, about one group per 2.5 m² at 1. They keep clear of openings, heads, the band and the plinth, and come back in the same places on every rebuild.

LODs:
- LOD0 has all of these.
- LOD1, the lean shell, has the band, plinth and foundation, and its glazed doors are dark glass with no canopy.
- LOD2's shader follows the band's height and colour.

Layout keys: `head`, `panes`, `frames`, `bandH`, `bandDepth`, `bandWall`, `doorType`, `plinthH`, `foundation`, `foundationH`, `bricks`, plus the colours `frame`, `foundationColor` and `plinth` (`FacadeDetailTests`).

**Checks:**
1. Arch heads, 2 × 3 panes, Frames, Band 0.65 m in a wall shade, Glazed doors, Plinth 0.8 m with a grey Plinth colour, Foundation, Brick patches 0.6: the building looks like the artist's.
2. Set Foundation *Below ground* to 1 m and lower the ground (or lift the site): the foundation shows down to it.
