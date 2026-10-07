# Colours: Storey on Color Pipeline

How Storey's materials get their colours from Triband's Color Pipeline (`com.triband.colorpipeline`, Bitbucket `triband/colorpipeline`). Written against **2.1.11** (master, 2026-09-16), the version the current project is on. Nothing here is implemented yet; §7 is the order of work.

## 1. Decision

1. **The palette is the only source of colour values.** Every Storey material reads its albedo from Color Pipeline's atlas (`_GlobalColorPaletteTex`) at draw time. A palette edit reaches every building without regenerating anything, and buildings follow the same remaps as every other Color Pipeline object (and, from 3.0, the same palette grading).
2. **Styles reference palette entries, not RGB.** A `FacadeStyle` colour holds a palette id (a `SerializableGUID`'s two halves as 32 hex digits) instead of a CSS hex. Hex stays legal as a literal so prototype layouts still import; the editor maps literals to palette entries.
3. **Vertices name a colour instead of carrying one.** The generator emits a *swatch* per vertex in the 4 bytes the vertex colour uses today: which style (a colour row), which slot (wall, trim, door, rail, …) and a shade factor. The shade keeps derived colours tied to their base (the plinth is 72 % of the wall), so they follow palette edits and remaps.
4. **Colour rows are per-building data in the building table**, as LOD2's parameter rows already are (SPEC §6.4): one row per building tier holds the palette indices of that style's seventeen colours. A colour change on a building, or a palette change that moves indices, rewrites rows and never meshes; HLOD cells never rebuild for colour.
5. **Per-building remaps use 2.1.11's `ColorMappingManager.SetupColorRemap(ColorRemapDescriptor, out int offset)`.** The offset is stored in the building's rows and registered again on `OnMappingsInvalidated`. Never a `ColorRemap` component or a `MaterialPropertyBlock` on a Storey renderer.
6. **The Color Pipeline glue is one optional assembly** (`versionDefines` on `com.triband.colorpipeline` from 2.1.11 up to 3.0, its previews excluded), with a built-in hex palette when the package is absent. The headless tests, CI and the prototype parity harness then need neither a palette nor the private registry.

## 2. What exists today

### 2.1 Storey

- `FacadeStyle` holds seven CSS hex colours: wall, trim, interior, floor, roof, core and glass. Setback styles take interior, floor and core from the building (`Derived.StyleAt`).
- `Colors.Col` converts hex to linear RGB the way three.js does. `Palette` (the prototype's `colorsOf`) builds 17 named colours from a style, and the generator writes them per vertex into `MeshBuilder.C`.
- `MeshUpload` packs them as **UNorm8 linear RGB** in the vertex `Color` attribute (LOD0, LOD1). LOD2 parameter rows carry wall, trim, darkened glass and roof as float RGB.
- `Storey/Opaque` and `Storey/Glass` use the vertex colour as albedo. `Storey/Massing` computes the facade from the row's four colours (`StoreyFacadeColor`).

Every colour the generator emits is one of three kinds. This was checked against the prototype's census fixtures: all 50,905 LOD0 and LOD1 colour buckets of the 85 fixture buildings, and all 2,533 LOD2 parameter-row buckets, with no exceptions.

| Kind | Colours |
|---|---|
| A style colour at a fixed shade | wall ×1, ×0.72 (plinth) · trim ×1, ×0.8 (soffit, awning underside) · interior ×1, ×0.72 (door trim) · floor · roof · core ×1, ×1.08 (core inside), ×0.82 (stair steps), ×0.85 (slab sides) · glass ×1, ×0.42 (opaque glass, LOD2 windows) |
| A fixed colour (becomes a style colour, §3.1) | door `#3B3129`, metal `#A3ABAE`, rail `#3D4448`, ceiling `#F3F2EE`, lift interior `#8E9A9E`, lift button `#FFB36B`, detail metal `#C9CDCB`, grille `#6E7476`, detail dark `#5B5F5E`, dish `#DDE0DE` |
| A neighbour's interior | the outside face of a party wall (SPEC §4.3) |

So no vertex needs a free RGB value: (style, slot, shade) describes all of them. The play kit's lift car (`#8E979A`, `#DAD8D2`) is a separate prop and is coloured like any other Color Pipeline object.

### 2.2 Color Pipeline 2.1.11

| Piece | Contract |
|---|---|
| Palette | One `ColorPalette.palette` (JSON) in a `Resources` folder, with up to 512 `ColorDefinition`s: name, `Color` (sRGB, as entered) and a `SerializableGUID` id. A colour's **index is its list position**, so deleting an entry shifts every later index. |
| Atlas | `_GlobalColorPaletteTex`, 512 × 1024 RGBAHalf (4 MiB), bound globally by `ColorMappingManager`. Row 0 is the palette; every other row is a full copy with some entries swapped (a remap). In a linear project values are written as `Color.linear`: the standard sRGB curve, which is what `Colors.Col` implements. `uint _ColorAtlasWidth` (512) is a global. |
| Shader | `ColorPalette.hlsl`: `SampleColorPalette(offset + index)` is a `LOAD_TEXTURE2D` at (`i % width`, `i / width`). Models add a per-renderer `_ColorPaletteOffset`: a material copy at runtime, a `MaterialPropertyBlock` in the editor. |
| Remaps | A `ColorRemap` component (needs a `Renderer`), or since 2.1.11 `SetupColorRemap(ColorRemapDescriptor, out int offset)` for code that draws without one. Rows are deduplicated by `CalculateMappingHash`, the same hash a `ColorRemap` stores, so a descriptor and a component with the same mapping share a row. `OnMappingsInvalidated` fires when every row is handed out again: on a palette change, an atlas rebuild, a domain reload, or entering or leaving play mode. |
| Models | The Model Remapper writes each submesh's palette index into the mesh as a float (+0.1) in a configurable UV channel (TexCoord3 by default), merges the submeshes into one and assigns the default material. |

## 3. Design

### 3.1 Data: colour references

- A style colour is a **colour reference string**: either `#RRGGBB`, a literal as the prototype writes it, or a palette id: the `SerializableGUID`'s two halves (`m_Value0`, `m_Value1`, as the `.palette` file stores them) as 16 hex digits each, converted with `ToParts()` and `new SerializableGUID(ulong, ulong)`. Storey's own format rather than `ToString()`, so it never depends on how `Hash128` prints itself. It is the same field and the same JSON type, so `PrototypeJson`, `StyleAt` and the fixtures are unchanged and the engine-free model never needs Color Pipeline.
- **The ten fixed colours become style colours** (decided: per style, §8). `FacadeStyle` gains ten optional fields: `door`, `rail`, `metal` (lift frames), `ceiling`, `liftInterior`, `liftButton`, `detailMetal`, `grille`, `detailDark` and `dish`. Each holds a colour reference like the other seven.
  - **An absent field means the project default**, which comes from `StoreyColorSettings` in the bridge assembly (§3.7). That asset has `[ColorReference]` `SerializableGUID` fields, so Color Pipeline's drawer and picker come for free, and its defaults are the prototype's hex values. Existing styles and prototype files need no new keys, and the prototype keeps working without them; the writer omits a field that equals the default.
  - **Which tier wins** follows today's rule for interior, floor and core (`Derived.StyleAt`). The indoor ones (`ceiling`, `liftInterior`, `liftButton`, `rail`, `metal`) come from the building's style, so a setback changes the outside only. `door` and the four detail colours follow the tier's style, like the walls.
  - The inspector shows them under a *More colours* foldout, as the prototype keeps interior colours under one.
- **Presets** (`FacadeStyle` assets, Plan §4.1) reference palette ids like any other style.
- **Prototype compatibility, for now:** when Unity writes a `.storey` file it adds a top-level `palette` block with the name and current hex of every palette id the file uses (`"palette": { "<id>": { "name": "BrickRed", "hex": "#9A4B38" } }`). The prototype resolves ids through it, so the file still opens there, and headless tools can render ids without Unity. `PrototypeJson` learns the key; the block is a snapshot written at save and is never the source of truth. Edits made in the prototype on an id-coloured style write hex literals, which the next *Map colours to palette…* maps back.

### 3.2 Generator: swatches instead of RGB

The generator stops producing `Rgb`. `MeshBuilder.C` becomes a list of swatches, and `Palette` keeps its 17 names, but each one is now a swatch:

```csharp
public enum ColorSlot : byte { Wall, Trim, Interior, Floor, Roof, Core, Glass, Door, Metal, Rail, Ceiling, LiftInterior, LiftButton, DetailMetal, Grille, DetailDark, Dish }

/// <summary>The style a colour comes from: building (site index) and the floor its setback tier starts at; resolves to Derived.StyleAt.</summary>
public readonly struct StyleRef { public readonly int building, tier; … }

/// <summary>A colour by reference: a slot of a style at a shade.</summary>
public readonly struct Swatch { public readonly StyleRef style; public readonly ColorSlot slot; public readonly double tone; … }

// Palette: plinth = (s, Wall, 0.72), coreIn = (s, Core, 1.08), door = (s, Door, 1), …
```

- A swatch names its style as (building, tier start), so a party-wall face simply names its neighbour; the upload turns the distinct style references of a mesh into colour rows. `ColorResolver` turns swatches back into linear RGB through the site.
- The census tests resolve a swatch to RGB with today's arithmetic (`Colors.Shade(hex of the slot, tone)`). **The fixtures stay as they are** and keep judging the port.
- LOD2's `ParamRow` holds its four colours as swatches. `Texels` resolves them to RGB for the census; `GpuTexels` writes what the table uploads: texel 0 = (colour row, wall, trim and glass shades), texel 1 = (roof shade), then spec and run as before.

### 3.3 GPU data: colour rows

Two buffers join the building table and are allocated and freed like LOD2's parameter rows:

- `_StoreyColors` (`StructuredBuffer<uint4>`): **one row per building tier** of three `uint4`, 24 16-bit entries. Entries 0–16 are the palette indices of the style's seventeen colours in `ColorSlot` order, 17–22 are spare, and entry 23 is the **atlas row of the building's remap** (0 = none). At 48 bytes a row this is about 210 KiB for the 3,000-building test city, at 1.5 tiers per building.
- `_StoreyDetailColors` (`StructuredBuffer<uint>`): the palette index of each colour used by facade-detail models (slot − 24, §3.9), written once from the detail catalogue. Empty until detail models exist.

The CPU writes a row when a building is uploaded or its colours change. It rewrites every row when the palette's indices may have moved (`OnMappingsInvalidated`), resolving each palette id to its current index through `ColorPaletteDefinition.Instance`. Party-wall faces point at the neighbour's row, so the neighbour's interior colour follows it without a rebuild. Party walls already rebuild when a neighbour's structure changes.

Baked meshes need row numbers that survive the bake (LOD1, LOD2 and cells as importer sub-assets, Plan §4.1). LOD2's parameter rows have the same need today: both are written as global numbers at upload through a `rowMap`. Solve the two together in workstream 7, for example with a per-building row base in the state table.

### 3.4 Vertex encoding

The LOD0/LOD1 `Color` attribute stays UNorm8 × 4 at 4 bytes; only the meaning changes:

| Byte | Today | After |
|---|---|---|
| r, g | red, green (linear) | colour row, low and high byte (65,536 rows) |
| b | blue (linear) | slot |
| a | 255 | shade × 128 (1.0 is exact; every factor in use is within ±0.45 %) |

There is no new vertex stream and no size change, and nothing in TexCoord3 changes. The LOD2 vertex is unchanged, because its parameter row names the colour row. Precision improves: 8-bit *linear* colour loses the darks (the door's blue channel is stored 6 % off today), while the atlas is half-float.

### 3.5 Shaders

One new include, `StoreyPalette.hlsl`, is Storey's whole view of Color Pipeline. It declares the same globals as 2.1.11's `ColorPalette.hlsl` instead of including that file, so Storey's shaders still compile without the package and the move to 3.0 (§6) touches only this file.

```hlsl
TEXTURE2D(_GlobalColorPaletteTex);          // ColorMappingManager's atlas, or Storey's hex palette (§3.7)
uint _ColorAtlasWidth;
StructuredBuffer<uint4> _StoreyColors;       // per colour row, 3 x uint4: 24 x uint16 (17 style colours, spare, remap row)
StructuredBuffer<uint>  _StoreyDetailColors; // per facade-detail colour: palette index

// Color Pipeline 2.1.11's SampleColorPalette, same addressing
float4 StoreyAtlas(uint i) { return LOAD_TEXTURE2D(_GlobalColorPaletteTex, int2(i % _ColorAtlasWidth, i / _ColorAtlasWidth)); }

uint StoreyRowEntry(uint row, uint e)
{
    uint4 r = _StoreyColors[row * 3 + (e >> 3)];
    uint q = (e >> 1) & 3;
    uint w = q == 0 ? r.x : q == 1 ? r.y : q == 2 ? r.z : r.w;
    return (e & 1) ? w >> 16 : w & 0xFFFF;
}

float3 StoreyPaletteColor(uint row, uint slot, float tone)
{
    uint index = slot < 24 ? StoreyRowEntry(row, slot) : _StoreyDetailColors[slot - 24];
    return StoreyAtlas(StoreyRowEntry(row, 23) * _ColorAtlasWidth + index).rgb * tone;
}

float3 StoreyVertexColor(float4 c)   // the vertex Color attribute (§3.4)
{
    uint4 e = (uint4)round(c * 255.0);
    return StoreyPaletteColor(e.x | (e.y << 8), e.z, e.w / 128.0);
}
```

| Material | Change |
|---|---|
| `Storey/Opaque`, `Storey/Glass` | `StoreyVert` sets `OUT.color = float4(StoreyVertexColor(IN.color), 1)`. A face's vertices share one swatch, so resolving per vertex is exact, and the fragment program is untouched. The depth and shadow passes skip the lookup. |
| `Storey/Massing` | `StoreyFacadeColor` reads wall, trim, glass (times the row's shade) and roof through the parameter row's colour row. That is four atlas reads per fragment, alongside the parameter reads it already does. |
| Section cap, isolate ghost, Sink darkening, LOD tint | Unchanged: these are presentation colours, not palette colours. The cap could take a palette entry from `StoreyColorSettings` later. |

Nothing is per material: no `_ColorPaletteOffset` and no property blocks, so the SRP Batcher and the GPU Resident Drawer are unaffected (Plan §6.1). The cost is a row read and a texture load per vertex, plus a table read for facade-detail colours. Vertex texture loads are fine on Metal, Vulkan and DX12.

### 3.6 Remaps

```csharp
var d = new ColorRemapDescriptor(original, overwrite);           // SerializableGUID[] each
ColorMappingManager.SetupColorRemap(d, out int offset);
int atlasRow = offset / Shader.GetGlobalInt("_ColorAtlasWidth"); // goes into entry 23 of each of the building's rows
```

- Keep every descriptor that has been registered and register them all again in `OnMappingsInvalidated`, because offsets handed out before it are stale. `ColorRowBook` (engine-free) does this bookkeeping: rows per style, remaps per building, and a re-entrancy-safe rewrite.
- A building and a `ColorRemap` prop with the same mapping share one atlas row, since they have the same hash.
- Remaps are for gameplay and theming (a repainted house, a district palette), not for giving each building its colours. The rows do that; §4 explains why.

### 3.7 Integration assembly and fallback

```json
{
  "name": "Triband.Storey.ColorPipeline",
  "rootNamespace": "Triband.Storey.ColorPipeline",
  "references": ["Triband.Storey", "Triband.Storey.Unity", "Triband.ColorPipeline.Runtime", "Triband.Core.Runtime"],
  "defineConstraints": ["STOREY_COLORPIPELINE"],
  "versionDefines": [{ "name": "com.triband.colorpipeline", "expression": "[2.1.11,3.0.0-0)", "define": "STOREY_COLORPIPELINE" }],
  "noEngineReferences": false
}
```

**In a player build.** Nothing references the bridge (Storey finds its palette by name, so the bridge stays optional), so IL2CPP's managed code stripping would remove the whole assembly, `[Preserve]` or not. Then no building draws, and every site throws "Storey's bridge … not in this build". So:
- the bridge is marked `[assembly: AlwaysLinkAssembly]`, which keeps it in the build;
- it registers itself before the first scene loads (`RuntimeInitializeOnLoadMethod`, `StoreyPalettes.RegisterBridge`), so a player never relies on the lookup by name. The editor doesn't strip, so the lookup by name serves there.

- The `-0` in the upper bound matters. Semver sorts `3.0.0-preview2` below `3.0.0`, so `[2.1.11,3.0.0)` would compile this assembly against the 3.0 preview, which has no `ColorRemapDescriptor`.
- The assembly holds `ColorPipelinePalette` (palette ids to indices, remaps, invalidation) and `StoreyColorSettings` (loaded from `Resources/StoreyColorSettings`). Runtime assemblies may not name `UnityEditor` (a layout rule), so instead of registering itself at load the bridge is found by `StoreyPalettes` by type name when Color Pipeline is installed, which works in edit mode, play mode and players (`[Preserve]` keeps it from being stripped).
- **Fallback, `HexPalette`** (no Color Pipeline): it indexes the distinct hex literals in use, writes them as `Color.linear` into a 512 × 1 RGBAHalf texture, and binds that as `_GlobalColorPaletteTex` with `_ColorAtlasWidth` = 512. That is the same contract, so the shaders can't tell the difference. There are no remaps. The parity harness runs this way and still matches the prototype.
- **The fallback must never run next to Color Pipeline**, because it would replace the global that every other object in the project samples. `Triband.Storey.Unity` therefore gets a version define for *any* Color Pipeline version (`"expression": "0.0.1"`) and compiles the fallback out whenever the package is present. If the version is outside the bridge's range, Storey reports the unsupported version instead.
- With Color Pipeline installed, a hex literal that hasn't been matched yet renders as the nearest palette colour by the same rule as the editor (`PaletteMatch.Nearest`, below), with a warning. Color Pipeline's own `GetIDOfClosestColor` is not used: it measures in sRGB, so it can pick a different entry than the Model Remapper.
- **Package layout tests:** `ReferencesToOurOwnAssembliesResolve` gets an allowlist for the two external Triband assemblies. A new rule, machine-checked like the others, allows them only in an assembly whose `defineConstraints` come from a `versionDefines` entry on the owning package. The stub project gains declarations of the Color Pipeline members the bridge uses and compiles it with `STOREY_COLORPIPELINE`. `package.json` declares nothing (the dependency is optional), so `ThePackagesShareOneVersion` is unaffected.

### 3.8 Editor

- **Picking:** every style colour opens Color Pipeline's own picker (`PaletteColorPickerWindow.Open`, which is public) and shows the swatch and the palette name. The prototype's per-field swatch rows become curated palette subsets: `StoreyColorSettings` holds a short list of suggested palette ids for wall, trim, roof, interior and floor, shown above the full picker and seeded by mapping the prototype's `PALETTE` lists. Its free colour picker has no equivalent: an off-palette colour has to become a new palette entry, which is how Color Pipeline is meant to be used.
- **Importing prototype layouts (not built: buildings are made in Unity, and `unity/tools/map_to_palette.py` maps the odd prototype layout):** *Map colours to palette…* on a `.storey` asset lists each distinct hex literal with its best palette match and the similarity (`GetColorSimilarity`, the Model Remapper's measure). Exact matches are accepted automatically; the rest are accepted, changed, or added to the palette (`ColorPaletteDefinition.Colors.Add`, `Save`, `ColorMappingManager.Reset`). The file is then rewritten with palette ids. It uses the Model Remapper's vocabulary, so artists already know it.

- **Layouts stay inside the palette.** With Color Pipeline installed, every edit ends with `PaletteConform.Apply` over the palette (installed as `StoreyColorField.Conform` by `Triband.Storey.Editor.ColorPipeline`). It replaces each colour the palette lacks with the nearest entry, and opening a layout for editing does the same as one undo step, *Match colours to the palette*. So presets, new buildings, duplicated styles, prototype layouts, and entries deleted from the palette all end up as palette ids. Each change is logged, for example `Storey: #9A4B38 → BrickRed (96% similar), 3 uses`.
  - The colours it replaces are hex literals, and palette ids that the palette no longer has. A lost id is matched by its last colour in the layout's `palette` block. If the block doesn't have it, it is left alone and still renders as palette colour 0 with a warning.
  - The nearest entry is chosen exactly as the Model Remapper's auto-remap chooses it. The measure is `ColorFormulas`' CIE76 ΔE on colours floored to 8 bits, rounded half to even. The highest `GetColorSimilarity` wins, and the first of equal entries wins a tie.
  - This was checked against Color Pipeline's own `ColorFormulas` outside the repository. There was no difference over 200,000 random pairs, nor over 20,000 random colours matched against a 338-entry project palette. `PaletteConformTests` keeps the rule. `unity/tools/map_to_palette.py` uses the same rule.
  - A style may leave an optional colour (doors, rails, lift buttons, …) unset, so it takes the project's default from `StoreyColorSettings`. A default the settings don't set is the prototype's hex colour, which renders as its nearest palette entry, and the inspector shows it as that entry. *Store the defaults in Storey Color Settings* writes those entries into the settings (`StoreyColorSettings.FillUnsetDefaults`), so after that no default is a hex colour either.
  - Without Color Pipeline there is no palette to keep to, so colours stay CSS hex.

### 3.9 Facade details (imported models, SPEC §4.4)

Detail models go through the Model Remapper like any prop. The detail validator's rule that "the material is the building material" becomes: one submesh, Color Pipeline's default material, and colour indices in the configured UV channel. At import the `FacadeDetailDefinition` stores the palette **ids** of its vertices rather than the indices, which go stale when the palette loses an entry. When a detail is merged into a building mesh its colours become detail-table slots (24 and up), so a palette change rewrites the table and not the buildings.

## 4. Alternatives considered

| Option | Why not |
|---|---|
| **Resolve palette colours to RGB on the CPU**, keeping today's vertex colours and shaders | The cheapest option, but every palette edit regenerates every mesh and cell, there are no remaps, and from 3.0 buildings would miss palette grading, which is baked into the atlas. It treats the palette as a list of names rather than as the pipeline. |
| **Bake palette indices into the vertices**, as the Model Remapper does for FBX models | The simplest shader, but every colour edit rebuilds the building's meshes and its cell, and deleting a palette entry shifts indices so every baked `.storey` asset has to reimport. LOD2 needs rows anyway, because the facade shader mixes wall, glass and trim per fragment. That leaves two mechanisms whose colours can disagree at the LOD1 to LOD2 switch. |
| **One Color Pipeline remap row per style**: placeholder palette entries in the mesh, swapped per building through `SetupColorRemap` | It uses the 2.1.11 API as intended, but the atlas has 1,024 rows for the whole game and each new row re-uploads the whole 4 MiB texture. Tiers and party walls multiply the combinations, and a full atlas at runtime hands out an offset past the end of the texture. Remap rows are meant for occasional swaps. |

## 5. 2.1.11 behaviour the bridge must handle

- `GetIndexOfColor` returns **0 for an unknown id**. Check `TryGetColor` first, so a deleted palette entry is reported instead of silently becoming palette entry 0.
- `default(ColorRemapDescriptor)` has a null hash, and `SetupColorRemap` throws on it. "No remap" is atlas row 0, so don't register it.
- When the atlas is full at runtime it logs an error and still returns an offset past the texture. Treat any row ≥ 1,024 as 0 and warn.
- Each *new* mapping writes its row and calls `Apply()`, which uploads the whole 4 MiB atlas. Register at load and on gameplay events, never per frame.
- The atlas is created lazily, so `OnMappingsInvalidated` can fire *inside* your first `SetupColorRemap` call. The handler must be re-entrant.
- Static subscriptions die with a domain reload, and the order of Color Pipeline's `[InitializeOnLoadMethod]` relative to Storey's is undefined. Resolve rows and register remaps on Storey's own initialisation as well, rather than waiting for the first event.
- `Shaders/ExampleColorPipelineShader.shader.txt` still samples the pre-1.8 `StructuredBuffer`. The contract is `ColorPalette.hlsl`.
- TexCoord3 is Color Pipeline's default index channel, and it is also where Storey keeps the wall id (LOD0) and the parameter row (LOD2). That is no conflict: Color Pipeline only uses that channel on models it imports.
- The migration menu items (*Assets ▸ Triband ▸ Color Pipeline ▸ Migration*) only replace materials on renderers whose mesh has Model Remapper metadata, so they leave Storey's meshes alone.

## 6. Later: Color Pipeline 3.0

`preview-releases/3.0` (3.0.0-preview2, 2026-07-09) replaces `ColorPalette.hlsl` with `ColorPipelineSample.hlsl`. The lookup becomes two steps: an index texture (`_GlobalColorPaletteIndexTex`, one row per remap) points into a one-row palette texture. The atlas width becomes a compile-time define, palette profiles bake grading into the palette texture, and the atlas width, remap count and format become palette settings. Its last merge from master predates 2.1.11, so it doesn't have `ColorRemapDescriptor` yet. For Storey the move is the declarations and `StoreyAtlas` in `StoreyPalette.hlsl`, plus the bridge's version range. Rows, vertices and data stay as they are, and buildings pick up grading for free.

## 7. Order of work

1. **Runtime, headless (done):** `ColorSlot`, `StyleRef`, `Swatch`, the swatch `Palette`, `ColorResolver`, swatches in `ParamRow`, the ten optional style colours, and parsing colour references. Every existing census test passes with the fixtures untouched; `MeshUpload` still uploads RGB, through the resolver.
2. **Unity layer (done headless; editor check outstanding):** colour rows (and the empty detail table) in `BuildingTable`, the encoding in `MeshUpload`, `StoreyPalette.hlsl`, the `StoreyLit` and `StoreyFacade` changes, `HexPalette` and `StoreyPalettes`. `GpuColorTests` emulates the shader lookups for every vertex and LOD2 row of the fixture corpora against the census colours. Still to do in an editor: compile the shaders and confirm the parity harness matches the prototype with Color Pipeline not installed.
3. **Bridge (done headless; editor check outstanding):** the assembly, `ColorPipelinePalette`, `StoreyColorSettings`, `ColorRowBook`, `StoreyStreet.SetRemap`, the layout-test rule (`OptionalIntegrationsAreGatedByTheirPackage`) and the Color Pipeline stubs. It stub-compiles headlessly and `ColorRowBookTests` cover the bookkeeping against a palette that behaves like 2.1.11. Still to check in the editor: a palette value edit recolours buildings without regenerating them, deleting a palette entry leaves every building correct, and a building remap shares its atlas row with a `ColorRemap` prop that has the same mapping.
4. **Authoring:** done without the editor tools: the `palette` block (`StoreyDocument.palette`, read, written, `PaletteSnapshot.Refresh` with `ColorPipelinePalette.Entry` as the lookup), the prototype reading it and the ten per-style colours (`prototype/tools/check-palette.mjs`; the fixtures re-export byte-identical), and the engine-free mapping core (`ColorMapping.Literals`, `ExactMatches`, `Apply`). Buildings are made in Unity, so their colours are palette entries from the start: the *Map colours to palette…* window is not built (`unity/tools/map_to_palette.py` covers the odd prototype layout). The rest comes with workstream 6's inspector (docs/EDITOR.md, slice 6.4): the palette picker with the curated subsets on each style field, presets as `FacadeStyle` assets, and calling `Refresh` on save.
5. **Later:** facade-detail models (§3.9) and 3.0 (§6).

## 8. Decisions and open questions

1. **Every project that uses Storey has Color Pipeline** (decided). So `HexPalette` is no longer a feature for projects. It stays only as the path the headless tests, CI and the prototype parity harness take, which keeps them free of a palette asset and the private registry. The bridge stays an optional assembly for the same reason, and the rule that the fallback never runs next to Color Pipeline still holds.
2. **Unity-written `.storey` files still open in the prototype**, for now (decided): the `palette` snapshot block (§3.1).
3. **Fixed colours are per style** (decided, §3.1): ten optional `FacadeStyle` fields with project defaults in `StoreyColorSettings`, and a colour row of three `uint4`.
4. **The prototype's swatch rows become curated palette subsets per field** (decided, §3.8).
