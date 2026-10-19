#nullable enable
using System;
using System.Linq;
using System.Text;
using Triband.Storey.Generate;
using UnityEditor;
using UnityEngine;

namespace Triband.Storey.Editor
{
    /// <summary>
    /// A part's colour in an artist's window, door or facade detail (docs/OPENINGS.md §6): one of the style's colours, or a
    /// palette colour of its own that it keeps whatever the style, picked with the project's colour picker (Color
    /// Pipeline's, when it is installed). Glass in a window or door is always the style's: it is the window shader's.
    /// </summary>
    internal static class StoreyPartColours
    {
        static GUIContent[]? choices;

        /// <summary>"Its own colour", then the style's colours, named as Unity names an enum's.</summary>
        static GUIContent[] Choices => choices ??= new[] { new GUIContent("Its own colour", "A palette colour it keeps whatever the style") }
            .Concat(Enum.GetNames(typeof(ColorSlot)).Select(n => new GUIContent(Nice(n)))).ToArray();

        /// <summary>
        /// One part's row: its colour (<paramref name="colour"/>, or <paramref name="own"/> when set), and below it, while it
        /// has its own, the picker. <paramref name="suggested"/> is the colour a new own colour starts as (a Model Remapper
        /// part's palette colour). Records <paramref name="owner"/> for undo. Returns whether anything changed.
        /// </summary>
        public static bool Row(UnityEngine.Object owner, string key, string label, ref ColorSlot colour, ref string own, bool glassIsTheStyles, string? suggested)
        {
            bool mine = own.Length > 0 && !(glassIsTheStyles && colour == ColorSlot.Glass);
            int at = mine ? 0 : 1 + (int)colour;
            int pick = EditorGUILayout.Popup(new GUIContent(label, mine ? "Keeps its own palette colour, whatever the style" : "Takes this colour of the building's style"), at, Choices);
            bool changed = false;
            if (pick != at)
            {
                Undo.RecordObject(owner, "The part's colour");
                if (pick == 0)
                {
                    own = own.Length > 0 ? own : suggested ?? "#808080";
                    if (glassIsTheStyles && colour == ColorSlot.Glass) colour = ColorSlot.Frame;   // glass can't keep its own
                }
                else { colour = (ColorSlot)(pick - 1); own = ""; }
                changed = true;
            }
            if (own.Length > 0 && !(glassIsTheStyles && colour == ColorSlot.Glass))
            {
                var v = StoreyColorField.Picker.Draw("part:" + owner.GetInstanceID() + ":" + key, "   Colour", own, "", _ => null);
                if (v != null && v != own) { Undo.RecordObject(owner, "The part's own colour"); own = v; changed = true; }
            }
            return changed;
        }

        /// <summary>The palette id a Model Remapper part's key ends in (the colour it kept); null for any other part.</summary>
        public static string? PaletteOf(string key)
        {
            int bar = key.LastIndexOf('|');
            if (bar < 0) return null;
            string id = key.Substring(bar + 1);
            return ColorRef.IsPaletteId(id) ? id : null;
        }

        // "DetailMetal" to "Detail metal"
        static string Nice(string name)
        {
            var b = new StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (i > 0 && char.IsUpper(c)) { b.Append(' '); b.Append(char.ToLowerInvariant(c)); }
                else b.Append(c);
            }
            return b.ToString();
        }
    }
}
