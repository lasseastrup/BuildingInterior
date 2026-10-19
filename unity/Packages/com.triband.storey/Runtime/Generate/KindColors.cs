#nullable enable
using System;
using System.Collections.Generic;

namespace Triband.Storey.Generate
{
    /// <summary>
    /// The palette colours that parts of artists' windows, doors and facade details keep whatever the style
    /// (docs/OPENINGS.md §6): each colour reference a part names gets a slot of its own, from 24 up, which its vertices
    /// carry in their slot byte. The shaders read its palette index from <c>_StoreyDetailColors</c> (StoreyPalette.hlsl),
    /// which <see cref="Fill"/> writes, and the building's remap row still applies. A slot is handed out for the session and
    /// never taken back, so a mesh built with it stays right; past the last, a part takes its style colour instead.
    /// Storey's own.
    /// </summary>
    public static class KindColors
    {
        /// <summary>The first slot: past a colour row's entries (<see cref="ColorRows.Entries"/>).</summary>
        public const int First = ColorRows.Entries;
        /// <summary>How many there can be: one for each slot a vertex's byte can name past a row's (the detail table's size).</summary>
        public const int Capacity = 256 - First;

        static readonly object gate = new object();
        static readonly Dictionary<string, int> slots = new Dictionary<string, int>(StringComparer.Ordinal);
        static readonly List<string> refs = new List<string>();

        /// <summary>Goes up with every colour handed out, so the table knows to write them again.</summary>
        public static int Version { get; private set; }

        /// <summary>Colours asked for after the last slot went: each part drew its style colour instead.</summary>
        public static int Refused { get; private set; }

        /// <summary>The slot of a colour reference (a palette id or a CSS colour), a new one the first time; null when none is left.</summary>
        public static ColorSlot? SlotOf(string colorRef)
        {
            lock (gate)
            {
                if (slots.TryGetValue(colorRef, out int i)) return (ColorSlot)(First + i);
                if (refs.Count == Capacity) { Refused++; return null; }
                i = refs.Count; refs.Add(colorRef); slots[colorRef] = i; Version++;
                return (ColorSlot)(First + i);
            }
        }

        /// <summary>Whether a slot is one of these (a part's own colour), not one of a style's.</summary>
        public static bool Is(ColorSlot slot) => (int)slot >= First;

        /// <summary>The colour reference a slot was handed out for; null for a style's slot or one not handed out.</summary>
        public static string? RefOf(ColorSlot slot)
        {
            int i = (int)slot - First;
            if (i < 0) return null;
            lock (gate) return i < refs.Count ? refs[i] : null;
        }

        /// <summary>
        /// The palette index of every colour handed out, into <paramref name="dest"/> from its start (slot 24 at 0, as the
        /// shaders read <c>_StoreyDetailColors</c>). Returns how many were written.
        /// </summary>
        public static int Fill(IColorPalette palette, uint[] dest)
        {
            string[] now;
            lock (gate) now = refs.ToArray();
            int n = Math.Min(now.Length, dest.Length);
            for (int i = 0; i < n; i++) dest[i] = (uint)Math.Max(0, palette.IndexOf(now[i]));
            return n;
        }
    }
}
