# Artist-made windows and doors from prefabs

How an artist's window or door can be a prefab of several objects, as Flamingo's old BuildingGenerator takes them, while Storey keeps building every opening into the building's own meshes. **Status: all five steps are built (docs/EDITOR.md §6.9); step 5 came second, as §9's answers asked.** §9 has the answers to the questions asked before step 1.

## 1. Where we are

### 1.1 The old solution

Flamingo's BuildingGenerator takes a window or door as a prefab:
- **One or more objects**, each with a MeshFilter and a MeshRenderer, anywhere in the hierarchy, each with its own transform and materials. Nested prefabs and variants come with that: one sill prefab shared by ten windows, a window with shutters made as a variant of one without.
- **A `BuildingCutoutBox` on the root**: the rectangle the generator carves into the wall, as `Size` and `Center` in the root's local XY, independent of the meshes. Adding the component fills both from the bounds of the first MeshRenderer it finds, and the artist adjusts them against a cyan rectangle in the Scene view. The generator reads it in the editor (`TryGetPrefabLocalBounds`).

### 1.2 Storey today

A **Storey Opening** asset (`StoreyOpening`, docs/EDITOR.md §6.9):
- one mesh, and an optional LOD1 mesh, with the pivot at the bottom middle of the hole and +Z out of the wall;
- each submesh is a part: one of the style's colour slots, and whether it is the door's leaf (left out on a walk-in storey). The model's materials only name the parts;
- `size` is the hole the mesh fills, taken from the mesh's bounds;
- *Stretch* scales the whole mesh to each opening. With it off, the mesh keeps its size, but the hole is still the style's, so the artist sets *Window width* to match.

The mesh is copied into the asset (`Bake`), and the generator builds a copy into every opening, in the building's own meshes (`OpeningKinds.Place`). A building stays at one or two draws, and the cutaway, Sink, the floor clip and the LOD fades work on its windows with no code of their own. Colours come from the palette through the building's colour rows (docs/COLOURS.md). The mesh references are the editor's and don't ship (docs/PERFORMANCE.md §11.2).

### 1.3 What the old solution allows and Storey doesn't

| | Old | Storey today |
|---|---|---|
| Several objects with their own transforms; nested prefabs, variants | Yes | One mesh: the artist merges it in the DCC tool |
| Dressing outside the hole: shutters, sills, surrounds, pediments, flower boxes | Yes: the cutout is the hole, whatever the meshes reach | The mesh's bounds are the hole, and *Stretch* stretches the dressing with the window |
| The pivot anywhere | Yes: `Center` | The bottom middle of the hole, or the inspector warns |
| The window decides the size of its hole | Yes | The style does; *Stretch* off puts the mesh's size in the style's hole |
| Materials | As authored | Each part one of the style's colours |
| Other components (lights, colliders, scripts) | Not known (§9) | None |

## 2. Decisions

1. **Openings stay built into the building's meshes.** A prefab is where the geometry comes from. It is flattened into the Storey Opening asset at edit time and never instantiated. A 20 × 12 m block of six storeys has 156 windows, and a street has thousands. Instancing them would undo what the design rests on: one or two draws a building, occlusion and Sink decided per vertex from the wall a window is on, the LOD cross-fades, and colours by colour row (SPEC §4.4, §4.6, §6.4). The few objects that must stay objects take the props' path (docs/PROPS.md, §7).
2. **The Storey Opening asset stays the catalogue entry**: the id a style names, and what a site lists and a build ships. It gains a *Prefab* field beside *Mesh*, editor-only as *Mesh* is, so neither the prefab nor its meshes and materials ship. Its baked copy keeps today's format, so in step 1 the generator, the build threads and the runtime side don't change.
3. **The hole is authored on the prefab**, as before, with a component of Storey's own (**Storey Cutout**, §3.2). Storey's packages know nothing of Flamingo's `BuildingCutoutBox`: the old prefabs are given a Storey Cutout once, by a script in the game's project (§3.2).
4. **Colours stay palette colours.** Each material is a part that takes one of the style's colours, and from step 5 can keep a palette colour of its own (§6).
5. **The geometry work is engine-free** (`Triband.Storey`) and tested headlessly, as the rest of the generator is. The Unity side only gathers meshes, matrices and materials.

## 3. Prefabs as they are (step 1)

### 3.1 What is read

- Every active object under the root with a MeshFilter and an enabled MeshRenderer. Submesh *s* takes material *s*. A submesh with no material isn't drawn by Unity, so it's left out here too.
- Each object's transform relative to the root, as Unity instantiates a prefab: the root's position and rotation don't count, its scale does. Positions go through the matrix and normals through its inverse transpose. A mirrored object (negative scale) has its triangles wound the other way.
- *Faces back* still turns the whole thing round.
- Left out, and listed in the inspector: inactive objects, disabled renderers, objects tagged EditorOnly, skinned meshes, and every component that isn't a mesh (lights, colliders, audio, particles, scripts).
- **LOD1:** *LOD1 prefab* if it is set. Otherwise, if the prefab has a LOD Group, its LOD 0 renderers are the window and its LOD 1 renderers are LOD1's (renderers in neither are in both). Without either, none, which keeps LOD1's plain pane (an asset made from a mesh takes *LOD1 mesh*, as before). §5 is about what a LOD1 costs.

### 3.2 Storey Cutout

A component for the prefab's root (*Add Component › Storey › Storey Cutout*):

| Field | What it is |
|---|---|
| Size | The hole's width and height, in metres. |
| Center | The middle of the hole, in the root's local XY. The pivot can be anywhere. |
| Wall face | Where the wall's outer face is along the prefab's Z (0 by default), so a prefab modelled for another depth needs no edit. The wall is 0.30 m thick (`Dim.T_EXT`). |
| Leaf | The objects that are the door itself, with everything under them: left out on a walk-in storey. Today the leaf is a material, so a metal handle on the leaf and a metal hinge on the frame can't be told apart. |
| Slice borders, Keep shape | Step 3 (§4.3). |

- **Gizmo** in Prefab Mode: the hole, the wall's face and its back (0.30 m behind), and from step 3 the slice lines.
- **Fit to the meshes:** the bounds of every mesh in the root's space, from the meshes' own vertices. The old component's `Reset` takes the first renderer it finds and its world-space `Renderer.bounds`, so a prefab of several objects, or one not at the origin, gets the wrong box.
- **Fit to the selection:** the bounds of the selected objects (the frame, say), which is what a window with shutters wants.
- **Without a cutout**, the bounds of every mesh are the hole. That's right for a prefab that is only the window.
- **Flamingo's `BuildingCutoutBox`** isn't read: Storey's packages know nothing of the old generator. The old prefabs keep theirs while that generator uses them, and are given a Storey Cutout once by the script below, which belongs in the game's project, where both components are.
- **What `Center` means** needs checking (§9). The old component's comment says (0, 0) puts the hole's bottom on the pivot, but its code (the bounds' centre, and the gizmo) treats `Center` as the middle. The script takes the code's meaning, unless the old generator reads it the other way.

The script, as `Assets/Editor/StoreyCutoutsFromBuildingCutoutBoxes.cs` (an assembly definition there would reference Flamingo's BuildingGenerator assembly and `Triband.Storey.Unity`). Select the old prefabs and choose *Assets › Storey Cutouts from BuildingCutoutBoxes*. A prefab that already has a Storey Cutout is left as it is, and its BuildingCutoutBox stays for the old generator.

```csharp
using System.Linq;
using Triband.Flamingo.BuildingGenerator;
using Triband.Storey.Unity;
using UnityEditor;
using UnityEngine;

static class StoreyCutoutsFromBuildingCutoutBoxes
{
    [MenuItem("Assets/Storey Cutouts from BuildingCutoutBoxes")]
    static void Convert()
    {
        foreach (var go in Selection.objects.OfType<GameObject>())
        {
            string path = AssetDatabase.GetAssetPath(go);
            if (string.IsNullOrEmpty(path)) continue;
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var old = root.GetComponent<BuildingCutoutBox>();
                if (old == null || root.GetComponent<StoreyCutout>() != null) continue;
                var cut = root.AddComponent<StoreyCutout>();
                cut.size = old.Size;
                cut.center = old.Center;   // the hole's middle, as the old gizmo draws it
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
```

### 3.3 Parts: one per material

- The parts list becomes one row per material in the prefab, not one per submesh. A row is keyed by the material asset (its GUID and local id), shows its name, and has its colour guessed from the name as today (`StoreyOpening.Guess`). Rows survive objects being added, removed or reordered, which today's submesh order wouldn't. An asset made from a mesh keeps its rows by submesh, as now.
- A part can take triangles from many objects. Each part is welded (exact duplicates only), and faces nobody can see are left out: those inside the wall round the hole, and those on the wall's faces (its outer face, its back, the hole's sides) turned into it. A frame modelled flush with the reveal loses its faces against it. Faces lying on the wall's outer face round the hole and facing out are kept and counted: they flicker against the wall (SPEC §4.5), and the inspector says so.

### 3.4 Baking, and baking again

- `Bake` writes today's baked arrays, so nothing downstream changes in this step.
- The asset bakes again when its prefab, or anything the prefab depends on, is imported or saved: a nested sill prefab, a variant's base, a mesh, a material (`AssetDatabase.GetDependencies`, kept per asset). `StoreyOpenings.Rebake` already gets every import's paths; today it compares them with the mesh's alone.

### 3.5 Making one, and its inspector

- **Create › Storey › Window or Door from Prefab**, on one or many selected prefabs, makes an asset beside each. Its door is guessed from the name and the leaf, and its parts and cutout are read. Once the old prefabs have their Storey Cutouts (§3.2), a whole library of them converts in one go.
- **The inspector** says what it found (objects, parts, triangles at LOD0 and LOD1) and what will cost too much or look wrong:
  - **Triangles on a building:** its triangles times the windows of the 20 × 12 m block above (156), beside what the generator's own windows cost there: 22 triangles each, or 140 with arch heads, frames and 2 × 3 glazing bars (measured headlessly). A starting budget is about 140 at LOD0, so a building is no heavier than with the generator's dressed windows. Tune it with the device runs (docs/PLAY.md, slice 5.5).
  - **Behind the wall:** anything deeper than the wall's back shows inside the rooms.
  - **Gaps in the hole:** on a shell or filled storey the wall has no inside face, and the reveal stops 16.5 cm in, where the generator's pane is. A window that doesn't cover its hole, or glass set deeper than that, shows into the building. This is measured by drawing the parts' front faces over the hole.
  - **Faces on the wall's face** that face out: they flicker against the wall.
  - **A window without Glass** shows no rooms (docs/EDITOR.md §6.8). **A door without a Leaf** keeps its leaf on a walk-in storey, and the player walks through it.
  - What was left out (§3.1).
- **Check on a building** builds a test block with it at LOD0 and LOD1 and runs `CoplanarCheck`, as `OpeningKindTests` does, and lists the overlaps.
- **Open Prefab** goes to the prefab, where the cutout is edited.

## 4. Fitting it to the openings

Today *Stretch* scales everything to the style's opening, and with it off the mesh keeps its size in the style's hole. Dressing outside the hole needs three more things.

### 4.1 The window decides its hole (step 2)

With *Stretch* off ("Keep size"), the hole takes the window's size. `Facade.WindowSpec` takes its width and height from the style's window kind, and the style still decides where windows go (its bays) and their sill height (*Sill height*, `FacadeStyle.sillH`, added after the plan; without it, the window type's sill). Everything that uses openings follows, since they're all laid out from `WindowSpec`: LOD0, LOD1, LOD2's painted windows (`Lod2.SpecOf`), courtyard walls, the facade details and the editor's tools. A window wider than its bay allows (the bay less 0.5 m, as `Ops` keeps it) is narrowed and fitted into the smaller hole, and the Facade tab says so. That is the old solution's behaviour, without setting *Window width* to match.

Doors keep the standard doorway (1.6 m, `Dim.DOOR_EXT`), which the entrance tools, courtyards and the walls' door spots assume, and a door is fitted into it. Doors of their own size come later (§7).

### 4.2 Dressing outside the hole (step 2)

When it's registered, the kind measures how far it reaches past its hole: left, right, below and above. `Opening` carries that reach, set from the style's kind where openings are laid out (`Facade.Ops`, and courtyard walls in `Courtyards`), and everything that keeps clear of windows uses it instead of the hole:
- the 14 cm windows keep from a neighbouring wall (`Facade.Span`), so shutters don't go through a corner;
- the wall between neighbours: where two windows' dressing would meet, a Problem (docs/EDITOR.md §6.5) names the style and the bay spacing it needs;
- a band, which `WindowSpec` keeps windows under, and a plinth, which ground-floor windows start above;
- facade details: AC units under windows, vents between them, awnings over openings, and hand placement's refusal (`Details`, `Edit.Facades`);
- brick patches (`Facade.Bricks`).

### 4.3 Slice: one prefab for every size of opening (step 3)

A third fit, as 9-slice scaling works for sprites. Slice lines run along the cutout's edges. What lies inside the hole's span is stretched, and what lies outside moves with the nearest edge, unscaled. Shutters keep their width and follow the hole's sides, a sill or a flower box gets longer but not taller, and a pediment keeps its height and widens.

```
      ┆           ┆            Across: inside |x| ≤ a, x × A / a; outside, x ± (A − a),
  ┌───┼───────────┼───┐        where a = w/2 − b and A = W/2 − b.
  │   ┆  pediment ┆   │        Up: under the bottom border, as modelled; between the borders,
──┼───┼───────────┼───┼──      stretched; over the top border, moved up by H − h.
  │ S ┆           ┆ S │
  │ h ┆   glass   ┆ h │        w × h: the cutout. W × H: the opening it is fitted to.
  │ u ┆ (stretch) ┆ u │        b: a border, the slice line's distance inside the hole.
  │ t ┆           ┆ t │
──┼───┼───────────┼───┼──
  └───┴───sill────┴───┘
```

- **Borders** keep a frame's members as thick as they were modelled: with 6 cm borders a frame stays 6 cm whatever the window's width.
- **Keep shape**: the objects listed on the Storey Cutout (a keystone, a lamp, a house number) move to where their bounds' centre maps, and never stretch.
- Triangles crossing a slice line are split there when baked, so curves (an arch) bend only at the lines.
- Normals are scaled where their point is stretched, as `KindPlace.Normal` does now, and are unchanged elsewhere.
- It's engine-free, in `KindPlace`: a few comparisons a vertex, and still nothing allocated a placement (docs/PERFORMANCE.md §8.1).
- Slice becomes the default for an asset made from a prefab. Stretch stays for a window that is only a window.

## 5. LOD1 (step 4)

With a LOD1 mesh, LOD1 cuts the wall at every window (`Lod1.Build`), about 12 triangles a window more than its flat pane. On the block above, LOD1 goes from 1,116 triangles to 3,036 even with a two-triangle LOD1 mesh, and most buildings on screen are at LOD1.

So a LOD1 that stays in front of the wall's face (shutters, a sill, a pediment) goes on the flat wall, beside the flat pane, with no cut. The shader draws the pane's recess as it does for the generator's windows. A LOD1 that reaches into the reveal cuts the wall, as now. The bake knows which a kind is, so there's no setting.

As built (`OpeningKind.lod1OnWall`, `lod1Covers`, found when a kind is registered):
- What of the LOD1 is over the hole must stand at least as far out as the flat pane (3 cm). Nearer, it would flicker against the flat wall behind it, so the wall is cut for it.
- Where a LOD1 leaves the hole open, the plain pane fills it, cut or not. One that covers the hole (its own glass, a door's leaf) has none. Before, a LOD1 with no glass showed a hole into the building.
- *Check on a building* looks for faces that flicker at LOD1 too.

On the block above, a two-triangle sill in front of the wall makes LOD1 1,448 triangles, against 1,140 for the generator's own windows. A two-triangle pane 5 cm in still makes it 3,038.

## 6. Colours of their own (step 5)

- **A palette colour per part**, as well as a style's colour: a part (a brass handle, red geraniums) keeps a palette entry whatever the style. Its vertices name slots 32 and up, which the shaders already read from `_StoreyDetailColors` (`BuildingTable.DetailColors`: 224 entries, "none set until detail models exist"). The Unity side fills it with the palette's indices and writes it again when they move. The building's remap still applies, through its colour row's remap entry. The asset stores palette ids, never indices, because an index moves when an entry is deleted (docs/COLOURS.md §2.2).
- **Models through Color Pipeline's Model Remapper**: one default material, and each vertex's palette index in TexCoord3 (docs/COLOURS.md §2.2). Their parts are one per palette colour, read from TexCoord3 when baked and stored as ids. Each keeps its colour or takes a style's.
- Engine-free: `Swatch` names a colour of its own by its slot byte, and `ColorResolver` (the census tests) resolves it. `MeshPack` already writes the slot as a byte.
- The limit is 232 such colours across the project's windows, doors and facade details. Facade Detail assets can then use them too (docs/COLOURS.md §3.9).

## 7. What stays out

- **Textures.** The package is untextured by design (UNITY-PACKAGE-PLAN §6.5): its vertices carry no UVs, and its materials draw palette colours. A textured window would need a UV stream in LOD0's vertex and textures in `Storey/Opaque`, for every building. That's a decision of its own (§9).
- **Live parts**: a light by a door, a door that opens, a collider. These aren't baked. If they're wanted, a prefab's other objects could be instanced beside the building as Storey Props (docs/PROPS.md), which already fade, clip and Sink with their building.
- **Holes that aren't rectangles** (arched or round). `WallPanel` already cuts holes as polygons, but the reveals, the merging of neighbouring openings, LOD1's flat pane and LOD2's painted windows are all rectangles. Meanwhile, an arched surround in front of a rectangular hole looks the same from the street.
- **Doors of their own size**, after §4.1: the entrance tools, courtyards and walls would take the width from the door kind instead of `Dim.DOOR_EXT`.
- ~~**Several windows a style**~~: built, §7.1.

### 7.1 Several windows and doors a style (built)

The old generator's themes have a weighted list of one to three window prefabs (8 of 26 with a "no window" entry), a separate list for the ground floor (24 of 26), and a weighted list of doors. Storey's styles have the same, as sets (`FacadeStyle.windowSet`, `groundSet`, `doorSet`; `OpeningSets` picks, `Edit.WindowSets` edits).

- **An entry** (`OpeningEntry`) is an artist's window or door (`kind`, a Storey Opening's id), the generator's own (no `kind`), or **none** (`none`: the wall is left whole there). Its `weight` is how often it is picked against the others'; 0 leaves it out, unless every weight is 0, when each is picked as often (the old generator's rule). A door entry is never none: an entrance always has a door, so a converted "no door" entry is the plain door.
- **One window a style stays as it was.** A style without a set has `windowKind` (and `doorKind`) in every opening, and builds exactly as before. A set of one window is the same as `windowKind`; the Facade tab keeps it there, so a set is only written once a style has two entries. Layouts, presets and baked layouts that name one kind read and write unchanged. Migrating every `windowKind` into a set would rewrite every layout for nothing, and the census fixtures are layouts of one window a style.
- **The ground floor's set** is its own when it has one, otherwise the floors above's. It fills the openings Storey's *Ground floor* lays out: a storefront's full bays, the windows of *Match*, none for *Solid*. The old generator's empty ground list (a flat ground floor) is `ground: solid`, or `groundSet: [{"none": true}]` to keep the doors' storefront margins.
- **The pick** is a hash of the building's id, the storey, the wall's edge and the window's place along the wall (`OpeningSets.WindowSeed`). So a rebuild, every LOD, and moving the building keep each window, and two buildings with the same footprint differ. The old generator seeds by edge, floor and slot only, so every building of one footprint and theme looks the same. Doors are picked by wall (`DoorSeed`): the doors along one wall are alike, as the old generator's are.
- **Picked by** (`windowPick`, Storey's own): *Window* picks each on its own, as the old generator does. *Column* keeps a column the same window all the way up its tier (the ground floor, with its own set, a column of its own), *Storey* a storey of a wall, *Wall* a whole wall. A facade whose windows change along a column reads as a patchwork; real ones change by column or by floor.
- **Where the windows go doesn't depend on the pick.** The bays are laid out over the span less the most dressing any entry has (`OpeningSets.Reach`), so shutters keep clear of a corner whichever is picked, and picking one never moves another. Each opening is then the size its own window makes it: a Keep size window its own size and sill, the generator's the style's (`Facade.Windows`).
- **Cost:** a hash and a walk of the set per window, nothing allocated (`OpeningSetTests.APickMakesNoGarbage`); a wall's window specs are made once per window the set picks there, in specs lent for the wall. The pick lives on the opening (`Opening.kind`), so LOD0's dressing, LOD1, details, Problems and the editor all see the same window without picking again. Everything is still one merged mesh.

**LODs.** LOD0 and LOD1 take each opening's own window: LOD1 cuts the wall only for the windows whose LOD1 reaches into it, and lays the rest on the flat wall. A none slot is whole wall at both. LOD2 paints every bay with one window, the set's heaviest that isn't none (`OpeningSets.Representative`; a dormer takes it too), and paints none only when every entry is none. So at LOD2 a slot left empty is painted as a window, and a smaller window as the heaviest's size. That's an approximation: at LOD2 a bay is a few pixels, and the share of glass changes by at most the none entries' weight. Matching LOD2 exactly would need the massing shader to know each run's picks, a parameter per window or the pick hashed in the shader from the building's id, edge and bay; the parameter row has a free texel for it (texel 3), if the pop at the LOD1 to LOD2 switch shows.

**The old generator's settings, for a converter:**

| BuildingTheme | Storey |
|---|---|
| `_windowPrefabs` (one entry) | `windowKind` |
| `_windowPrefabs` (several) | `windowSet`: each entry's prefab as `kind` (its Storey Opening's id), a null prefab as `{"none": true}`, `Weight` as `weight` |
| `_overrideGroundFloorWindows` off | no `groundSet` |
| on, entries | `groundSet`, as above |
| on, an empty list | `ground: "solid"` |
| `_doorPrefabs` | `doorKind` for one, `doorSet` for several; a null prefab is the plain door (`{}`) |
| `WallFillerPrefab` (no window list) | `windowKind` |
| per-entry inset depth and offset | not yet (a per-entry `recess` is planned) |

## 8. Order of work

| Step | What | Size | Done when |
|---|---|---|---|
| 1 | Prefabs as they are: §3 | M | Old prefabs that are only the window convert in one go and look as they did, on shell and walk-in buildings. Editing a nested prefab updates the buildings. The player build of the stubs compiles without the prefab field. |
| 2 | The hole from the window, and the dressing kept clear: §4.1, §4.2 | M | Old prefabs with shutters and sills look as they did. LOD0, LOD1 and LOD2 put a Keep-size window's hole in the same place on every fixture. Details and brick patches keep clear of the dressing, and Problems names a style whose bays are too tight. |
| 3 | Slice: §4.3 | M | One shuttered prefab fits Punched, Tall and storefront openings with its shutters and sill unstretched. `CoplanarCheck` finds no overlaps, and placements make no garbage (`OpeningKindGarbageTests`). |
| 4 | LOD1 on the flat wall: §5 | S | A shuttered window costs LOD1 its LOD1 mesh and the flat pane, with no cut. |
| 5 | Colours of their own: §6 | M | A Model Remapper prefab shows its own colours, a palette edit recolours it without a rebuild, and the census resolves its colours. |

Steps 1 and 2 together give artists what the old solution gives them. Step 5 moves before step 2 if the old prefabs are coloured by the Model Remapper (§9).

**Where the code goes:**

| Where | What |
|---|---|
| `Runtime/Generate/KindBake.cs` (new, engine-free) | Flattening (matrices, mirroring, parts by key), the cutout's re-basing, welding, hidden faces, the reach, splitting at slice lines. |
| `Runtime/Generate/OpeningKinds.cs`, `KindMesh.cs` | The fit (Stretch, Keep size, Slice), borders, the reach, Keep-shape anchors; `KindPlace` slicing. |
| `Runtime/Generate/Types.cs`, `Facade.cs`, `Courtyards.cs`, `Lod1.cs`, `Details.cs`, `Edit/Facades.cs`, `Validate/Problems.cs` | `Opening`'s reach, Keep-size specs, LOD1 on the flat wall, the clearances, the Problem. |
| `Unity/Rendering/StoreyOpening.cs` | *Prefab* (editor-only, beside *Mesh*), parts by material, the fit. |
| `Unity/Rendering/StoreyCutout.cs` (new) | The component: data only, so `PackageLayoutTests`' rules hold. |
| `com.triband.storey.authoring/Editor/StoreyOpenings.cs` | Gathering a prefab, baking again by dependencies, the inspector, *from Prefab*, the cutout's gizmo and buttons (`[DrawGizmo]`). |
| `Headless/Triband.Storey.Stubs` | The engine members used: `MeshFilter.sharedMesh`, `LODGroup.GetLODs`, `AssetDatabase.GetDependencies`, and so on. |
| Tests | `KindBakeTests` (new), `OpeningKindTests`, the clearances in `DetailKindTests` and `FacadeDetailTests` (brick patches), `ProblemTests`. |
| Docs | docs/EDITOR.md §6.9, SPEC §4.4's mapping, the packages' CHANGELOGs. |

## 9. Questions to answer before step 1

Answered when the plan was approved:

1. **Textures:** the old prefabs use none, so §7's first point stays out.
2. **Colours:** the old prefabs are coloured by the Model Remapper. So step 5 comes second, before the hole and the dressing: without it they would take the style's colours alone.
3. **The old cutout:** not known. The conversion script (§3.2) takes `Center` as the hole's middle, as the component's own gizmo and its fit to the bounds have it, and the style decides the sill height. If an old prefab's hole sits half its height too low or too high, its `Center` was meant as the bottom: fix it in its Storey Cutout.
4. **What else is on them:** only meshes, and one prefab per style. So nothing is instanced, and a style keeps one window and one door. (Later, for the old themes' weighted lists: several a style, §7.1.)
