# Parity with Color Pipeline

Checking Storey in a project that uses Color Pipeline 2.1.11 against the prototype: the same geometry and the same colour on every surface (docs/COLOURS.md). Lighting is URP's, not three.js's, so frames are compared side by side, never pixel for pixel.

| File | What |
|---|---|
| `colorpipeline-entries.json` | The demo street's 46 colours as Color Pipeline palette entries (`Storey_<hex>`), to add to the project's `ColorPalette.palette`. |
| `demo-palette.storey` | The demo street with every style colour mapped to those entries, including the ten per-style colours, plus the palette block. It renders exactly as `unity/Fixtures/demo.json` in the prototype and in the headless tests (`ParityFilesTests`, `prototype/tools/check-palette.mjs`). |

The ids are made of one repeated byte (`1111…`, `2424…`), so their string form is the same whichever byte order `Hash128` prints in. They are test ids; real entries come from the Palette Editor.

## Without Color Pipeline first

Open this repository's `unity/` project in Unity 6.3 and follow "The first editor session" in `unity/README.md`. That checks the shaders and the harness on Storey's built-in hex palette, so a shader problem and a Color Pipeline problem don't show up at the same time.

## In a project with Color Pipeline

Needs Unity 6.3 (6000.3), URP 17.3, Linear colour space and a working Color Pipeline 2.1.11 setup. Do it on a branch: the palette is edited.

1. **Packages.** Add to `Packages/manifest.json` (the repository is private, so Unity's git needs GitHub access):
   ```json
   "com.triband.storey": "https://github.com/lasseastrup/BuildingInterior.git?path=/unity/Packages/com.triband.storey#claude/amazing-thompson-dgsbo8",
   "com.triband.storey.authoring": "https://github.com/lasseastrup/BuildingInterior.git?path=/unity/Packages/com.triband.storey.authoring#claude/amazing-thompson-dgsbo8"
   ```
   Then check that the assembly `Triband.Storey.ColorPipeline` exists (for example in the Project window under the package's `ColorPipeline` folder, its asmdef is not greyed out). If it is missing, the version define did not match and Storey reports it when the harness starts.
2. **Palette.** Back up `Resources/ColorPalette.palette`, open it in a text editor and append the 46 objects of `colorpipeline-entries.json` to its `m_Colors` array. Save; Unity reimports. Open the Palette Editor and press Save once so the generated C# class picks them up. The palette must stay at 512 colours or fewer.
3. **Layout.** Copy `demo-palette.storey` anywhere under `Assets/`. Its inspector should list 7 buildings and no unknown keys.
4. **Materials.** Create three materials with the shaders `Storey/Opaque`, `Storey/Glass` and `Storey/Massing`. Fix and report any shader compile errors.
5. **Scene.** Put an empty GameObject in a scene, add **Storey Street (parity harness)**, assign the layout and the three materials, and press Play.
   - The Console should show **no Storey warnings**. "not mapped to the palette yet" or "not in the Color Pipeline palette" means an entry from step 2 did not land.
   - Switch **Displayed LOD** between 0, 1 and 2. At LOD2 the windows must stay in place.
6. **Compare.** Open `prototype/index.html` in a browser (it shows the same demo street by default; to be sure, *? → Layout JSON…*, paste `demo-palette.storey`, *Load pasted JSON*). Match the camera and take screenshots of both. Shapes, positions and every surface's colour must match. Light, shadows, sky and glass translucency will differ.

## The Color Pipeline checks (docs/COLOURS.md §7, step 3)

- **Remap without a rebuild.** In Play mode select the harness and set *Remap Building* `0` (Linden Court), *Remap From* `11111111111111111111111111111111` (Storey_9A4B38, its brick wall), *Remap To* `05050505050505050505050505050505` (Storey_56645F, the warehouse green). Right-click the component ▸ *Apply remap*. The walls turn green at once and the plinth follows, darker; nothing else changes. Clear both fields and *Apply remap* again to undo. A `ColorRemap` prop with the same mapping uses the same atlas row (*Triband ▸ Color Pipeline ▸ Show texture*).
- **The palette is the source.** Stop, change `Storey_9A4B38` in the Palette Editor, save, press Play: Linden Court's walls and plinth show the new colour. (The harness builds its meshes at Start, so this shows where colours come from; that recolouring needs no new geometry is tested headlessly.)
- **Deleting an entry.** Delete one `Storey_` entry from the middle of the palette and press Play. Only the surfaces using it change (a warning names the id; they show palette entry 0). Everything else stays right even though every later index moved. Restore the palette afterwards.

## What to send back

Shader compile errors, any Storey warnings or exceptions in the Console, and the side-by-side screenshots (keep them here, under `unity/Parity/`).
