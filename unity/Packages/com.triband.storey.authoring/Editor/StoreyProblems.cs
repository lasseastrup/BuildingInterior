#nullable enable
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Generate;
using Triband.Storey.Play;
using Triband.Storey.Validate;
using UnityEditor;
using UnityEngine;

namespace Triband.Storey.Editor
{
    /// <summary>
    /// The problem list in the editor (docs/EDITOR.md §6.5): <see cref="Problems"/> run on the layout being edited, once an
    /// edit has settled (never during a drag, and at most once per 0.4 s), with the site's own LOD0 meshes where they are
    /// current, so nothing is generated twice. Marked in the Scene view.
    /// </summary>
    internal static class StoreyProblems
    {
        const double Settle = 0.4;
        static StoreyEdit? of;
        static string? checkedText, seenText;
        static double seenAt;
        static List<Problem> last = new List<Problem>();
        static bool waiting;

        /// <summary>The problems of the layout being edited (the last ones checked while an edit settles).</summary>
        public static List<Problem> For(StoreyEdit e)
        {
            double now = EditorApplication.timeSinceStartup;
            if (!ReferenceEquals(of, e)) { of = e; checkedText = null; last = new List<Problem>(); }
            if (e.Text != checkedText && !e.Dragging)
            {
                if (e.Text != seenText) { seenText = e.Text; seenAt = now; }
                if (checkedText == null || now - seenAt >= Settle) Run(e);
                else if (!waiting)
                {
                    // look again once it has settled
                    waiting = true;
                    void Tick() { if (EditorApplication.timeSinceStartup - seenAt < Settle) return; EditorApplication.update -= Tick; waiting = false; UnityEditorInternal.InternalEditorUtility.RepaintAllViews(); }
                    EditorApplication.update += Tick;
                }
            }
            return last;
        }

        /// <summary>The list is older than the layout (an edit is settling, or a drag is under way).</summary>
        public static bool Stale(StoreyEdit e) => e.Text != checkedText;

        static void Run(StoreyEdit e)
        {
            var site = new Site(e.Document.buildings); var world = new PlayWorld(site);
            if (e.Site != null) foreach (var b in site.Buildings) { var l0 = e.Site.BuiltLod0(b.id); if (l0 != null) world.UseLod0(b, l0); }
            last = Problems.Check(site, world);
            checkedText = e.Text;
        }

        static readonly Color Warn = new Color(0.95f, 0.7f, 0.15f), Err = new Color(0.85f, 0.2f, 0.15f);

        public static Color ColorOf(Problem p) => p.severity == Severity.Error ? Err : Warn;

        /// <summary>A marker at every problem (repaint only, in the site's space); the selected building's say what is wrong.</summary>
        public static void Draw(StoreyEdit e)
        {
            if (Event.current.type != EventType.Repaint || !e.View.showProblems) return;
            var keep = Handles.color;
            foreach (var p in For(e))
            {
                var at = new Vector3((float)p.x, (float)p.y, (float)p.z);
                float s = HandleUtility.GetHandleSize(at) * 0.09f;
                Handles.color = ColorOf(p);
                Handles.DrawSolidDisc(at, Camera.current != null ? -Camera.current.transform.forward : Vector3.up, s);
                if (p.buildingId == e.View.selectedId) Handles.Label(at + Vector3.up * s * 1.5f, p.message, EditorStyles.whiteMiniLabel);
            }
            Handles.color = keep;
        }
    }
}
