#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Triband.Storey.Generate;
using Triband.Storey.Play;
using Triband.Storey.Validate;
using UnityEditor;
using UnityEngine;

namespace Triband.Storey.Editor
{
    /// <summary>
    /// The problem list in the editor (docs/EDITOR.md §6.5): <see cref="Problems"/> run on the layout being edited, on a
    /// worker thread so the editor never waits. On the inspector's Check button, and by itself once an edit has settled
    /// (never during a drag, at most once per 0.4 s) for layouts small enough to check quickly: the test city's 3,000
    /// buildings take about 20 s. Marked in the Scene view.
    /// </summary>
    internal static class StoreyProblems
    {
        const double Settle = 0.4;
        /// <summary>Layouts with more buildings than this are only checked on the button.</summary>
        public const int AutoMost = 150;
        static StoreyEdit? of;
        static string? checkedText, seenText, runningText;
        static double seenAt, startedAt;
        static List<Problem> last = new List<Problem>();
        static Task<List<Problem>>? running;
        static bool waiting;

        /// <summary>Check as the layout is edited (small layouts only, <see cref="AutoMost"/>). Per user, kept between sessions.</summary>
        public static bool Auto { get => EditorPrefs.GetBool("Storey.problems.auto", true); set => EditorPrefs.SetBool("Storey.problems.auto", value); }

        /// <summary>Whether this layout is checked as it is edited.</summary>
        public static bool AutoFor(StoreyEdit e) => Auto && e.Document.buildings.Count <= AutoMost;

        /// <summary>The problems found by the last check of the layout being edited (empty before one). Starts one when checking as you edit.</summary>
        public static List<Problem> For(StoreyEdit e)
        {
            double now = EditorApplication.timeSinceStartup;
            if (!ReferenceEquals(of, e)) { of = e; checkedText = null; last = new List<Problem>(); running = null; runningText = null; }
            if (AutoFor(e) && e.Text != checkedText && e.Text != runningText && !e.Dragging)
            {
                if (e.Text != seenText) { seenText = e.Text; seenAt = now; }
                if (checkedText == null || now - seenAt >= Settle) CheckNow(e);
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

        /// <summary>The layout has been checked at least once since it was opened.</summary>
        public static bool Checked(StoreyEdit e) => ReferenceEquals(of, e) && checkedText != null;

        /// <summary>The list is older than the layout (it was edited since the check).</summary>
        public static bool Stale(StoreyEdit e) => e.Text != checkedText;

        /// <summary>A check is running, and for how many seconds.</summary>
        public static bool Running => running != null;
        public static double RunningFor => EditorApplication.timeSinceStartup - startedAt;

        /// <summary>Check the layout as it is now, on a worker thread (a check already running for the same layout carries on).</summary>
        public static void CheckNow(StoreyEdit e)
        {
            if (running != null && runningText == e.Text) return;
            of = e;
            // its own copy of the layout, read from the text: the edit can carry on while it runs
            string text = e.Text; runningText = text; startedAt = EditorApplication.timeSinceStartup;
            var mine = Task.Run(() =>
            {
                var site = new Site(PrototypeJson.Read(text).Document.buildings);
                return Problems.Check(site, new PlayWorld(site));
            });
            running = mine;
            double shown = 0;
            void Poll()
            {
                if (!mine.IsCompleted)
                {
                    // the inspector's "Checking … s" counts up
                    if (EditorApplication.timeSinceStartup - shown > 0.5) { shown = EditorApplication.timeSinceStartup; UnityEditorInternal.InternalEditorUtility.RepaintAllViews(); }
                    return;
                }
                EditorApplication.update -= Poll;
                if (!ReferenceEquals(running, mine)) return;   // a newer check took its place
                running = null; runningText = null;
                if (mine.Status == TaskStatus.RanToCompletion) { if (ReferenceEquals(of, e)) { last = mine.Result; checkedText = text; } }
                else if (mine.Exception != null) Debug.LogException(mine.Exception.InnerException ?? mine.Exception);
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            }
            EditorApplication.update += Poll;
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
