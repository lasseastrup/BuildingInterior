#nullable enable
using System;
using System.Collections.Generic;
using Triband.Storey.Generate;
using UnityEngine;

namespace Triband.Storey.Unity
{
    /// <summary>
    /// The palette Storey resolves colour references through (docs/COLOURS.md §3.7). With Color Pipeline
    /// installed its bridge (<c>Triband.Storey.ColorPipeline</c>) supplies one and Color Pipeline binds the atlas;
    /// without it this is the built-in <see cref="HexPalette"/>.
    /// </summary>
    public interface IStoreyPalette : IColorPalette
    {
        /// <summary>Bind the atlas globals if this palette owns them. Called once per frame before rendering.</summary>
        void Bind();
    }

    public static class StoreyPalettes
    {
        static IStoreyPalette? active;

        /// <summary>The bridge's palette type, found by name: the bridge is optional, so nothing here can reference it.</summary>
        const string BridgeType = "Triband.Storey.ColorPipeline.ColorPipelinePalette, Triband.Storey.ColorPipeline";

        /// <summary>The palette everything that writes colour rows uses. A project may set its own.</summary>
        public static IStoreyPalette Active
        {
            get => active ??= Default();
            set => active = value;
        }

        static IStoreyPalette Default()
        {
#if STOREY_HAS_COLORPIPELINE
            // Never the built-in palette here: it would replace the atlas every other Color Pipeline object samples.
            var t = Type.GetType(BridgeType);
            if (t == null)
                throw new InvalidOperationException("Color Pipeline is installed but Storey's bridge did not compile: it needs com.triband.colorpipeline 2.1.11 up to 3.0 (docs/COLOURS.md §3.7).");
            return (IStoreyPalette)Activator.CreateInstance(t)!;
#else
            return new HexPalette();
#endif
        }
    }

#if !STOREY_HAS_COLORPIPELINE
    /// <summary>
    /// The built-in palette when Color Pipeline is not installed: the CSS literals in use, as linear colours in a
    /// 512 × 1 RGBAHalf texture bound under Color Pipeline 2.1.11's names, so the shaders cannot tell the difference.
    /// No remaps. Used by the headless-tested parity harness and projects without a palette.
    /// </summary>
    public sealed class HexPalette : IStoreyPalette
    {
        readonly HexPaletteIndex index = new HexPaletteIndex();
        Texture2D? texture;
        int uploaded = -1;

        public event Action? Invalidated { add => index.Invalidated += value; remove => index.Invalidated -= value; }

        public StyleDefaults Defaults => index.Defaults;
        public int IndexOf(string colorRef) => index.IndexOf(colorRef);
        public int RegisterRemap(IReadOnlyList<string> original, IReadOnlyList<string> overwrite) => index.RegisterRemap(original, overwrite);

        public void Bind()
        {
            if (texture == null) texture = new Texture2D(HexPaletteIndex.Capacity, 1, TextureFormat.RGBAHalf, false) { name = "StoreyHexPalette" };
            if (uploaded != index.Version)
            {
                var px = new Color[HexPaletteIndex.Capacity];
                for (int i = 0; i < index.Colors.Count; i++) { var c = index.Colors[i]; px[i] = new Color((float)c.r, (float)c.g, (float)c.b, 1); }
                texture.SetPixels(px);
                texture.Apply(false);
                uploaded = index.Version;
            }
            Shader.SetGlobalTexture(StoreyShaderIds.PaletteTex, texture);
            Shader.SetGlobalInt(StoreyShaderIds.AtlasWidth, HexPaletteIndex.Capacity);
        }
    }
#endif
}
