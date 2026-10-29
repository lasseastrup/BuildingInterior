# Color Palette Lit lighting

Storey's three materials lit like `Flamingo/Color Palette/ColorPallete-Lit`, so buildings and props of the same palette colour shade alike: `Flamingo/Storey/Opaque`, `Flamingo/Storey/Glass` and `Flamingo/Storey/Massing`.

Only the lighting differs from Storey's own shaders. LOD switching and cross-fade, Sink, the cutaway, the active-floor clip, the occlusion modes, isolate, the section cap and the palette colours all come from the package (`StoreyLit.hlsl`, `StoreyFragment.hlsl`).

`StoreyColorPaletteLit.hlsl` is the graph's lighting, transcribed from its generated code node for node. It calls the same custom-function files:

- `Assets/Art/Shaders/CustomNodes/GlobalVariables.hlsl` for the shadow colour, the shadow fade distance and interval, and the player position
- `MainLightNode.hlsl` for `MainLight_float`
- `AdditionalLightsNode.hlsl` for `SG_MobileVertexLights_float`
- `WindowInteriorParallax.hlsl` for the Window graph's room mapping (`WindowInteriorParallax_float`), and `GlobalVariables.hlsl`'s night blend, for Storey's windows (below)

What those files do, checked against the copies of 2026-10-28:

- **Shadows.** With `TRIBAND_SHADOWS_ON`, `MainLight_float` samples URP's own main-light shadow map: the cascade, the asset's soft-shadow quality, its strength and its fade. Storey's ShadowCaster pass draws into that map, so buildings cast onto props and props onto buildings. Without the keyword nothing is shadowed. The graph fades shadows out with distance a second time (`_GlobalShadowFadeDistanceStart`, `_GlobalShadowFadeInterval`), and so does this file.
- **Additional lights.** With the renderer on Forward+ (`_CLUSTER_LIGHT_LOOP`, as the project has been since 2026-10-02) the node takes the additional directional lights and the clustered ones; on Forward, only when the URP asset sets Additional Lights to *Per Vertex* (`_ADDITIONAL_LIGHTS_VERTEX`), from the per-object list. Either way they are evaluated per pixel, each light's colour both Lambert-shaded and unshaded, 30 : 70, so they lift the faces turned away from them too. These shaders declare the two keywords as one set (URP never has both: three variants, not four); plain *Per Pixel* on Forward adds nothing in the node, so `_ADDITIONAL_LIGHTS` isn't declared. The graph's other lighting keywords are declared as it declares them. After a player build, Storey's line in the Console gives each shader's programs and the keywords left in them.
- **From above.** The Lighting sub-graph adds a tenth of a white light from straight above (`0.1 × saturate(normal.y)`, after the rest is saturated), so roofs and sills read a little lighter (2026-10-08).
- **Globals.** `_GlobalShadowColor`, the shadow fade, `_GlobalPlayerPosition` (the blob) and `_GlobalNightBlend` are set by the project, and buildings follow them.
- **Windows.** Storey's panes that show a room (with a `StoreyWindows` in the scene) map it with the project's `WindowInteriorParallax_float` and light up as the project's night blend rises, as the Window graph's do (`STOREY_WINDOW_PARALLAX`, `STOREY_WINDOW_NIGHT_BLEND`, StoreyWindow.hlsl), and are fogged as the walls are. `StoreyWindows.nightBlend` is not used with this sample: set `_GlobalNightBlend` as the project does.

If those files move, change the four include paths. If the graph's lighting changes, change this file to match.

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
- Declare `#pragma multi_compile_local _ STOREY_STEADY` in every pass of the opaque and massing shaders but the selection pass, as these do. Buildings that nothing can hide draw with that variant, which has no discard, so tile-based GPUs treat them as opaque (docs/CITY.md §2). A shader without it still draws correctly, always with the discard.
- Copy the `SceneSelectionPass` pass into each shader as it is, so the Scene view outlines only the building being edited. Without it Unity outlines every building of the selected site.
- Put the material's own properties in `STOREY_MATERIAL_PROPERTIES`.
- Include `StoreyLit.hlsl`, define `float4 StoreyLighting(StoreySurface s)`, then include `StoreyFragment.hlsl`.
