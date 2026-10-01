# Color Palette Lit lighting

Storey's three materials lit like `Flamingo/Color Palette/ColorPallete-Lit`, so buildings and props of the same palette colour shade alike: `Flamingo/Storey/Opaque`, `Flamingo/Storey/Glass` and `Flamingo/Storey/Massing`.

Only the lighting differs from Storey's own shaders. LOD switching and cross-fade, Sink, the cutaway, the active-floor clip, the occlusion modes, isolate, the section cap and the palette colours all come from the package (`StoreyLit.hlsl`, `StoreyFragment.hlsl`).

`StoreyColorPaletteLit.hlsl` is the graph's lighting, transcribed from its generated code node for node. It calls the same custom-function files:

- `Assets/Art/Shaders/CustomNodes/GlobalVariables.hlsl` for the shadow colour, the shadow fade distance and interval, and the player position
- `MainLightNode.hlsl` for `MainLight_float`
- `AdditionalLightsNode.hlsl` for `SG_MobileVertexLights_float`

If those files move, change the three include paths. If the graph's lighting changes, change this file to match.

| The graph | Here |
|---|---|
| `PipelineColor`: the palette colour of `uv3.x + _ColorPaletteOffset`, sampled in the vertex stage | `s.albedo`: the palette colour of the vertex's colour row and slot, times its shade |
| `_ShadowStrenght` | `_StoreyShadowStrength` |
| Cull Back | Cull Off: walls are seen from inside, and back faces show the section cap |
| UnlitShadowStep, UnlitShadowSmoothness, CelShading, NoiseColor, NoiseTile | Not used by the graph's output either |

## Use

1. Import this sample (Package Manager ▸ Storey ▸ Samples).
2. Make three materials with these shaders and assign them to the Storey Street harness, or to `StoreyStreet` in your own scene, in place of the `Storey/…` materials.

To light Storey another way, copy this sample:

- Define `STOREY_CUSTOM_LIGHTING` in every pass.
- Put the material's own properties in `STOREY_MATERIAL_PROPERTIES`.
- Include `StoreyLit.hlsl`, define `float4 StoreyLighting(StoreySurface s)`, then include `StoreyFragment.hlsl`.
