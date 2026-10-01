# Parity with Color Pipeline

Checking Storey in a project that uses Color Pipeline 2.1.11 against the prototype: the same geometry and the same colour on every surface (docs/COLOURS.md). Lighting is URP's, not three.js's, so frames are compared side by side, never pixel for pixel.

| File | What |
|---|---|
| `demo-project.storey` | **Use this one.** The demo street mapped to the project's own `ColorPalette.palette` by `unity/tools/map_to_palette.py`: every style colour (the ten optional ones included) is the id of its nearest palette entry by the Model Remapper's measure (all 46 within ΔE 10.5, 96 % similar or better), plus the palette block. Nothing in the project's palette changes. The prototype renders it with the palette's colours, which is what Unity shows. |
| `colorpipeline-entries.json`, `demo-palette.storey` | The other route: the demo's exact colours as 46 `Storey_<hex>` entries to add to a palette, and the demo mapped to them. Only for checking against the original demo colours. |

Palette ids in a `.storey` are Storey's format: a `SerializableGUID`'s `m_Value0` and `m_Value1`, as the `.palette` file stores them, as 16 hex digits each. To map another layout or after the palette changed:

```bash
python3 unity/tools/map_to_palette.py path/to/ColorPalette.palette unity/Fixtures/demo.json unity/Parity/demo-project.storey
```

In the editor, *Assets ▸ Storey ▸ Map Colours to Palette…* on a `.storey` asset does the same, using Color Pipeline's own similarity, and also lets you pick another entry, add a colour to the palette, or keep it. Nearest matches can differ from the tool's when two entries are within rounding of each other.

## Without Color Pipeline first

Open this repository's `unity/` project in Unity 6.3 and follow "The first editor session" in `unity/README.md`. That checks the shaders and the harness on Storey's built-in hex palette, so a shader problem and a Color Pipeline problem don't show up at the same time.

## In a project with Color Pipeline

Needs Unity 6.3 (6000.3), URP 17.3, Linear colour space and a working Color Pipeline 2.1.11 setup. The project's palette is not touched.

1. **Packages.** Add to `Packages/manifest.json` (the repository is private, so Unity's git needs GitHub access):
   ```json
   "com.triband.storey": "https://github.com/lasseastrup/BuildingInterior.git?path=/unity/Packages/com.triband.storey#claude/amazing-thompson-dgsbo8",
   "com.triband.storey.authoring": "https://github.com/lasseastrup/BuildingInterior.git?path=/unity/Packages/com.triband.storey.authoring#claude/amazing-thompson-dgsbo8"
   ```
   Then check that the assembly `Triband.Storey.ColorPipeline` exists (for example in the Project window under the package's `ColorPipeline` folder, its asmdef is not greyed out). If it is missing, the version define did not match and Storey reports it when the harness starts.
2. **Layout.** Copy `demo-project.storey` anywhere under `Assets/`. Its inspector should list 7 buildings and no unknown keys.
3. **Materials.** Create three materials with the shaders `Storey/Opaque`, `Storey/Glass` and `Storey/Massing`. Fix and report any shader compile errors. To light the buildings like the project's props, import the package's *Color Palette Lit lighting* sample and use `Flamingo/Storey/Opaque`, `Flamingo/Storey/Glass` and `Flamingo/Storey/Massing` instead. Put a building next to a prop of the same palette colour: lit faces, shadowed faces, the shadow edge and the player's blob should match.
4. **Scene.** Put an empty GameObject in a scene, add **Storey Street (parity harness)**, assign the layout and the three materials, and press Play.
   - The Console should show **no Storey warnings**. "not in the Color Pipeline palette" means the palette changed since the mapping: run the tool again.
   - Switch **Displayed LOD** between 0, 1 and 2. At LOD2 the windows must stay in place.
5. **Compare.** Open `prototype/index.html` in a browser, *? → Layout JSON…*, paste `demo-project.storey`, *Load pasted JSON*: the prototype then shows the street in the palette's colours. Match the camera and take screenshots of both. Shapes, positions and every surface's colour must match. Light, shadows, sky and glass translucency will differ.

## The Color Pipeline checks (docs/COLOURS.md §7, step 3)

- **Remap without a rebuild.** In Play mode select the harness and set *Remap Building* `0` (Linden Court), *Remap From* `e341af71a12497dbde0e79242759f7c5` (8D392C, its brick wall), *Remap To* `215663abc1f254b82648ee38e07405eb` (556160, the Dockside Store's grey-green). Right-click the component ▸ *Apply remap*. The walls turn grey-green at once and the plinth follows, darker; nothing else changes. Clear both fields and *Apply remap* again to undo. A `ColorRemap` prop with the same mapping uses the same atlas row (*Triband ▸ Color Pipeline ▸ Show texture*).
- **The palette is the source.** Only if you want to: stop, change `8D392C` in the Palette Editor, save, press Play: Linden Court's walls and plinth show the new colour. Undo the change afterwards. (The harness builds its meshes at Start, so this shows where colours come from; that recolouring needs no new geometry is tested headlessly.)
- **Deleting an entry** (optional, on a branch: it edits the palette). Delete an entry the street uses from the middle of the palette and press Play. Only the surfaces using it change (a warning names the id; they show palette entry 0). Everything else stays right even though every later index moved.

## What to send back

Shader compile errors, any Storey warnings or exceptions in the Console, and the side-by-side screenshots (keep them here, under `unity/Parity/`).
