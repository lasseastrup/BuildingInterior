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

        public event Action? Invalidated;
        public StyleDefaults Defaults { get; }

        [Preserve]
        public ColorPipelinePalette() : this(StoreyColorSettings.Load()) { }

        // Storey asks for its palette by name, which a player can't rely on (stripping): the bridge says how to make it
        // before any scene loads. The editor doesn't strip, so the lookup by name serves there until Play
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration), Preserve]
        static void Register() => StoreyPalettes.RegisterBridge(() => new ColorPipelinePalette());

        public ColorPipelinePalette(StoreyColorSettings? settings)
        {
            Defaults = settings != null ? settings.ToDefaults() : new StyleDefaults();
            ColorMappingManager.OnMappingsInvalidated += Raise;
        }

        void Raise() { memo.Clear(); Invalidated?.Invoke(); }

        // colour reference -> palette index. Color Pipeline finds an id by walking the palette, twice a lookup, and a
        // colour row asks for every slot of every style: thousands of walks an edit. A remembered id is checked against
        // the palette before it is used (an entry moved or deleted is looked up again); a CSS colour's nearest entry is
        // kept until the palette changes size or Color Pipeline invalidates its mappings.
        readonly Dictionary<string, int> memo = new Dictionary<string, int>(StringComparer.Ordinal);
        ColorPaletteDefinition? memoFor; int memoCount = -1;
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
            var palette = Palette; var list = palette.Colors;
            if (!ReferenceEquals(palette, memoFor) || list.Count != memoCount) { memo.Clear(); memoFor = palette; memoCount = list.Count; }
            if (ColorRef.IsPaletteId(colorRef))
            {
                var id = Guid(colorRef);
                if (memo.TryGetValue(colorRef, out int at) && at < list.Count && list[at].ID == id) return at;
                for (int i = 0; i < list.Count; i++) if (list[i].ID == id) { memo[colorRef] = i; return i; }
                return 0;   // a deleted entry: palette colour 0, quietly
            }
            if (memo.TryGetValue(colorRef, out int hit)) return hit;
            if (!ColorUtility.TryParseHtmlString(colorRef, out _)) throw new FormatException($"\"{colorRef}\" is not a colour reference");
            // the editor conforms layouts as they are edited; a layout never opened since shows what conforming would pick
            var near = PaletteMatch.Nearest(colorRef, Entries(palette));
            int idx = near == null ? 0 : palette.GetIndexOfColor(Guid(near.Value.entry.id));
            memo[colorRef] = idx;
            return idx;
        }

        /// <summary>
        /// The palette for <see cref="PaletteMatch"/>, in the palette's order (the first of equally near entries wins, as
        /// in Color Pipeline's Model Remapper).
        /// </summary>
        public static List<PaletteColor> Entries(ColorPaletteDefinition palette)
        {
            var list = new List<PaletteColor>(palette.Colors.Count);
            foreach (var def in palette.Colors) list.Add(new PaletteColor(IdOf(def.ID), def.Name, def.Color.r, def.Color.g, def.Color.b));
            return list;
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
            if (!ColorRef.IsPaletteId(id) || !Palette.TryGetColor(Guid(id), out var def)) return null;
            return new PaletteEntry(def.Name, "#" + ColorUtility.ToHtmlStringRGB(def.Color));
        }

        static SerializableGUID Id(string s) =>
            ColorRef.IsPaletteId(s) ? Guid(s) : throw new FormatException($"a remap takes palette ids; \"{s}\" is not one");

        /// <summary>A palette id to Color Pipeline's id, through its two halves, never Hash128's string form.</summary>
        public static SerializableGUID Guid(string id) { var (a, b) = ColorRef.Parts(id); return new SerializableGUID(a, b); }

        /// <summary>Color Pipeline's id to a palette id.</summary>
        public static string IdOf(SerializableGUID g) { var (a, b) = g.ToParts(); return ColorRef.PaletteId(a, b); }

    }
}
