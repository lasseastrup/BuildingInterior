#nullable enable
using System;
using System.Collections.Generic;
using Triband.ColorPipeline.Runtime;
using Triband.Core.Utils;
using Triband.Storey.Generate;
using Triband.Storey.Unity;
using UnityEngine;
using UnityEngine.Scripting;

namespace Triband.Storey.ColorPipeline
{
    /// <summary>
    /// Storey's colours from Color Pipeline 2.1.11 (docs/COLOURS.md §3.6, §3.7, §5): palette ids to palette
    /// indices, building remaps through <c>SetupColorRemap</c>, and <c>OnMappingsInvalidated</c> passed on so
    /// colour rows are written again. Color Pipeline binds the atlas itself. Created by name by
    /// <see cref="StoreyPalettes"/>, which cannot reference this optional assembly.
    /// </summary>
    [Preserve]
    public sealed class ColorPipelinePalette : IStoreyPalette, IDisposable
    {
        /// <summary>Atlas rows in 2.1.11 (<c>ColorMappingManager.k_MaxNumberOfRemaps</c>, private).</summary>
        public const int MaxRemapRows = 1024;
        static readonly int AtlasWidthId = Shader.PropertyToID("_ColorAtlasWidth");
        readonly HashSet<string> warned = new HashSet<string>(StringComparer.Ordinal);

        public event Action? Invalidated;
        public StyleDefaults Defaults { get; }

        [Preserve]
        public ColorPipelinePalette() : this(StoreyColorSettings.Load()) { }

        public ColorPipelinePalette(StoreyColorSettings? settings)
        {
            Defaults = settings != null ? settings.ToDefaults() : new StyleDefaults();
            ColorMappingManager.OnMappingsInvalidated += Raise;
        }

        void Raise() => Invalidated?.Invoke();
        public void Dispose() => ColorMappingManager.OnMappingsInvalidated -= Raise;

        /// <summary>Color Pipeline binds <c>_GlobalColorPaletteTex</c> and <c>_ColorAtlasWidth</c>; nothing to do.</summary>
        public void Bind() { }

        static ColorPaletteDefinition Palette
        {
            get
            {
                try { return ColorPaletteDefinition.Instance; }
                catch (Exception e)
                {
                    throw new InvalidOperationException("Storey reads its colours from Color Pipeline, which has no palette: create ColorPalette.palette in a Resources folder (Project Settings > Triband > Color Pipeline).", e);
                }
            }
        }

        public int IndexOf(string colorRef)
        {
            var palette = Palette;
            if (ColorRef.IsPaletteId(colorRef))
            {
                var id = new SerializableGUID(colorRef);
                // GetIndexOfColor alone answers 0 for an unknown id, so a deleted entry would turn silently into entry 0
                if (palette.TryGetColor(id, out _)) return palette.GetIndexOfColor(id);
                Warn(colorRef, $"Storey: palette colour {colorRef} is not in the Color Pipeline palette (deleted?); showing palette colour 0.");
                return 0;
            }
            if (!ColorUtility.TryParseHtmlString(colorRef, out var c)) throw new FormatException($"\"{colorRef}\" is not a colour reference");
            Warn(colorRef, $"Storey: {colorRef} is not mapped to the palette yet; showing the nearest palette colour until it is.");
            return palette.GetIndexOfColor(palette.GetIDOfClosestColor(c));
        }

        public int RegisterRemap(IReadOnlyList<string> original, IReadOnlyList<string> overwrite)
        {
            if (original.Count != overwrite.Count) throw new ArgumentException("a remap pairs each original colour with an overwrite colour");
            if (original.Count == 0) return 0;   // row 0 is "no remap"; default(ColorRemapDescriptor) would throw
            var o = new SerializableGUID[original.Count]; var w = new SerializableGUID[overwrite.Count];
            for (int i = 0; i < o.Length; i++) { o[i] = Id(original[i]); w[i] = Id(overwrite[i]); }
            // may raise OnMappingsInvalidated inside this call (the atlas is created lazily); ColorRowBook copes
            ColorMappingManager.SetupColorRemap(new ColorRemapDescriptor(o, w), out int offset);
            int width = Shader.GetGlobalInt(AtlasWidthId);
            int row = width > 0 ? offset / width : 0;
            if (row >= MaxRemapRows)
            {
                // a full atlas still hands out an offset past the texture
                Debug.LogWarning("Storey: Color Pipeline's remap atlas is full; the building keeps its own colours.");
                return 0;
            }
            return row;
        }

        /// <summary>
        /// A palette id's current name and colour, for <see cref="PaletteSnapshot.Refresh"/> before a layout is saved;
        /// null when the palette no longer has it (the block then keeps its last entry).
        /// </summary>
        public PaletteEntry? Entry(string id)
        {
            if (!ColorRef.IsPaletteId(id) || !Palette.TryGetColor(new SerializableGUID(id), out var def)) return null;
            return new PaletteEntry(def.Name, "#" + ColorUtility.ToHtmlStringRGB(def.Color));
        }

        static SerializableGUID Id(string s) =>
            ColorRef.IsPaletteId(s) ? new SerializableGUID(s) : throw new FormatException($"a remap takes palette ids; \"{s}\" is not one");

        void Warn(string key, string message) { if (warned.Add(key)) Debug.LogWarning(message); }
    }
}
