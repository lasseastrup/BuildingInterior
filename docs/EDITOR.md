# Workstream 6: editor tools

Buildings are made in Unity, so the editor is where Storey is used (SPEC §9 maps the prototype's UI; UNITY-PACKAGE-PLAN §4 sets the idioms). Exit test: a designer makes a styled 3-floor building in under 2 minutes, and 10 floors with roof access in under 5.

## 1. Where the logic lives

The prototype's editing is plain functions over the layout: add or delete a floor, insert or drag a corner, push an edge, add a setback, place a core, draw a wall. They carry the rules that make the tool pleasant: new floors copy the top floor, cores follow the floor count, doors and details keep their place when an outline gains a corner, a setback must stand on the floor below.

These rules are ported to the engine-free `Runtime/Edit/` and judged against the prototype the way the generator is. `prototype/tools/export-ops.mjs` runs sequences of operations in the prototype and writes the resulting buildings to `unity/Fixtures/ops.json`, and the C# operations must produce the same JSON. The Unity tools are then thin: a handle or a button calls an operation inside one undo step and redraws.

## 2. Authoring model in the editor

- **One document per site.** A `.storey` file holds a street or district, as in the prototype, because party walls and edge snapping need the neighbours. A `StoreySite` component in the scene references the asset and draws it in edit mode. Buildings are selected inside it with the Storey tools (as ProBuilder selects faces), not as separate GameObjects. The tools belong to the **Storey tool context** (`StoreyToolContext`, an `EditorToolContext`). You enter it with a tab (Shape, Facade, Interior) in the site's inspector, or by choosing *Storey* in the Scene view's tool context menu with a site selected. While it is active, the Tools overlay shows Shape, Facade and Interior in place of Move, Rotate and Scale, so the site can't be moved by accident. Clicking empty space doesn't leave the context; choose the GameObject context in the same menu to leave it.
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

1. Select the site and click a tab: the Scene view switches to the Storey tool context (the Tools overlay shows Shape, Facade and Interior, and Move, Rotate and Scale are gone). Click a building: it is selected, and the inspector shows its name and floor count. **Isolate** hides the others.
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
   - The floors above the active one are hidden.
   - **Walls** (Interior tab) sets this storey's walls, after The Sims' wall modes:
     - *Down* (the default): every wall drops to a 1 m stub, and nothing moves as the camera goes round.
     - *Cutaway*: only the walls between the camera and the focus drop, and they follow the camera.
     - *Up*: every wall at full height.
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

- **Sills** and **Heads** (under *Windows and doors*): turn off the sill under each window, or the head over it (flat or arched; on doors, the arched hood). Both are on by default. Off, the window is a plain opening with its glass. Layout keys `sills` and `heads`, written only when off.
- **Head shape:** *Flat* (the thin head strip) or *Arch*, the artist's cap over each window and door. It's a solid hood standing 14 cm out from the wall and reaching 10 cm past the opening each side. Its top is arched (7 cm thick at the ends, rising about a twelfth of its span in the middle), and its underside dips 4 cm at the ends, so it hangs over the window.
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

### 6.8 Windows

Windows show a room behind the glass, after the project's *Window* shader graph. Add **Storey Windows** (*Add Component › Storey › Storey Windows*) to any object in the scene and give it the room atlas. Without the component, or with no atlas, the glass is the plain dark glass of before.

- **Rooms:** each window shows one cell of the atlas (*Atlas grid* columns × rows) as a box behind the pane, in perspective (*Depth*). Each window picks its own room and is mirrored at random.
- **By day:** the room is lit by the main light's colour times *Tint*.
- **At night:** as *Night blend* rises from 0 to 1, more windows light up, up to *Light chance* of them. Each lit window takes a lamp colour between *Light* and *Light 2*. A day and night system can set `StoreyWindows.NightBlend` instead of the field.
- **Glare:** an optional texture over the glass, at *Glare strength* (0.03 in the shader graph).

Which room a window shows, and when it lights, comes from a number worked out from the window's centre on the wall. LOD0 and LOD1 bake it into the pane's vertices, and LOD2's shader works it out the same way, so a window keeps its room and its light across an LOD switch.

Limits:
- It applies to the panes that aren't see-through: shell and filled storeys in LOD0, every window in LOD1, and LOD2's painted windows. A walk-in storey keeps its see-through glass, because the real room is behind it.
- There is no fog term yet.
- `StoreyWindowParallax` in StoreyWindow.hlsl takes the same inputs as the shader graph's `WindowInteriorParallax_float`, so that node's code can replace it.
- In LOD2, a window whose painted rectangle is a little off the real one can, rarely, pick a different room than in LOD1.

**Checks:**
1. Add Storey Windows with the room atlas and its grid. Shell and filled storeys show rooms, each window its own, and they move in perspective as the camera moves.
2. Drag *Night blend* from 0 to 1: windows light up one by one in the lamp colours.
3. Zoom out through LOD1 and LOD2: each window keeps its room and its light.
4. Remove the component: the glass goes back to plain dark glass.

### 6.9 Artist-made windows and doors

An artist can make a window or a street door as a mesh, and every building whose style picks it gets one in each opening, in place of the generator's own.

1. Model it with the pivot at the bottom middle of the hole in the wall: +Y up, +Z out of the wall, in metres. Z 0 is the wall's outer face, and negative Z goes into the reveal; the wall is 0.25 m thick. Each submesh is one part. Name its material after what it is: *Glass*, *Frame*, *Sill* or *Trim*, *Door* or *Leaf*, *Handle* or *Metal*, *Wall*.
2. *Assets › Create › Storey › Window or Door*, then set *Mesh*. The parts take their colours from the material names, and *Size* (the hole) comes from the mesh's bounds. Tick *Door* for a street door. *Faces back* turns round a mesh made facing −Z.
3. Check the parts list. Each part takes one of the style's colours, so the building's palette colours it; the model's materials aren't used.
   - **Glass** shows the window shader's rooms on shell and filled storeys (§6.8), and is see-through on walk-in ones.
   - **Leaf** marks the door itself. It's left out on a walk-in ground floor, where the player walks through the doorway.
4. In the Facade tab, pick it under **Window** (next to *Windows*) or **Street door** (with the other details). *Generated* goes back to the generator's own.

The mesh is stretched to each opening, so the style's *Windows* type, *Window width* and *Bay spacing* still decide where openings are and how big. Turn off *Stretch* to keep the mesh's own size, centred on the opening's bottom; then set *Window width* to match. Heads, sills, frames, glazing bars, canopies and glazed doors aren't drawn where an artist's mesh is: the mesh is the whole window or door.

LODs: LOD0 places the mesh. LOD1 places *LOD1 mesh* if there is one, and otherwise keeps its plain pane. LOD2 keeps its painted windows.

The site lists the windows and doors its layout uses (*Openings*, kept by the editor when you save), so builds include them. The mesh is copied into the asset when set, and baked again when its model is reimported. If Unity says the mesh isn't readable, tick *Read/Write* on the model.

Engine-free: `OpeningKind`, `OpeningKinds` (`Register`, `Of`, `Place`), `FacadeStyle.windowKind`/`doorKind` (layout keys `windowKind`, `doorKind`) (`OpeningKindTests`). Unity: `StoreyOpening`, `StoreySite.openings`.

**Checks:**
1. Make a window from a model with Glass and Frame materials. Its parts list says Glass and Frame, and its size matches the model.
2. Pick it under Window on a shell building: every window is the model, stretched to the openings, with rooms behind the glass.
3. On a walk-in building the glass is see-through.
4. Make a door with a Leaf part and pick it under Street door. On a shell building the door shows; on a walk-in building the leaf is gone and you can walk in.
5. Change the model in your DCC tool and reimport it: the buildings update.
6. Enter Play mode, and make a build: the windows and doors are there.

### 6.10 The site inspector's layout

The site's inspector reads top to bottom in the order you work: which layout, which building, what's wrong, which mode, then that mode's settings. The rare settings come last.

1. **Layout**: the `.storey` file. Selecting the site starts editing it (§4).
2. **Building** card: pick a building, *New ▾* (a shape or a template), *Duplicate*, *Delete*, its name, *Isolate* and *Save as template…*. A box is kept for controls that act together; everything in this card acts on the building picked at its top.
3. **Problems**: one quiet line when there are none, or a warning foldout with the count.
4. **Shape / Facade / Interior**: three large buttons, each with an icon and a line saying what it's for. The selected one is tinted, with an accent bar along its bottom. On a narrow inspector the line moves under the bar.
5. The mode's settings in named groups, each a header with a hairline and space above it, not a box:
   - **Shape:** *Outline* (setbacks, presets), *Corners*, *Courtyards and atria* (folded until the building has one), *Storey heights*.
   - **Facade:** *Style for* (with setbacks), *Style* (preset), *Windows and doors* (type, artist's window, width, bay spacing, heads, glazing bars, frames, street doors), *Walls* (ground floor, bands, plinth, foundation, brick patches), *Colours* (*More colours* folded), *Roof*, *Details by rule* (folded), *Place on walls* (two rows of tools), *Bridges*.
   - **Interior:** *Inside* (walk-in or shell), *Floor*, *Tools*, the selection, *This storey's height*, *Interior colours* (folded).
6. **Site settings** (folded): materials, the LOD shown, colliders, and the artist's windows and doors the layout uses.

Foldouts remember whether they're open for the rest of the editor session.

The rules follow Unity's editor guidelines: groups set apart by spacing and headers, and containers only where the controls interact as a group. They also follow the Gestalt grouping principles (proximity and common region) and progressive disclosure for the less-used settings.
- [Unity Foundations: content organisation](https://foundations.unity.com/patterns/content-organization), [Unity Foundations: foldout](https://foundations.unity.com/components/foldout)
- [NN/g: common region](https://www.nngroup.com/articles/common-region/), [Laws of UX: common region](https://lawsofux.com/law-of-common-region/)
- [UX Planet: how to design a properties panel](https://uxplanet.org/how-to-design-properties-panel-4d562cc47da3), [Retool: simplifying the inspector](https://retool.com/blog/simplifying-retools-inspector)

Code: `StoreyInspectorUI` (`Section`, `Fold`, `ModeBar`, the three icons, drawn so they follow the editor skin).

**Checks:**
1. Select a site. The layout, the building card, the problems line and the three mode buttons are all visible without scrolling at a normal inspector height.
2. Click each mode: the button tints and the Scene view's tool changes.
3. Narrow the inspector: the mode buttons keep their icons and names, and the description moves below them.
4. Switch the editor to the light skin: the icons and accents are still readable.
