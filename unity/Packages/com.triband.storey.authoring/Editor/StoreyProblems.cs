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
    /// worker thread so the editor never waits. The first check covers the whole layout; after that only the buildings
    /// changed since the last check, with their neighbours and bridges (<see cref="Problems.Scope"/>), are checked again
    /// and merged into the list. On the inspector's Check button, and by itself once an edit has settled (Auto; never
    /// during a drag, at most once per 0.4 s). Marked in the Scene view.
    /// </summary>
    internal static class StoreyProblems
    {
        const double Settle = 0.4;
        static StoreyEdit? of;
        static string? checkedText, seenText, runningText;
        static double seenAt, startedAt;
        static List<Problem> last = new List<Problem>();
        // the last checked layout, building by building, and as a site: what the next check compares with
        static Dictionary<string, string>? lastPrints;
        static Site? lastSite;
        static Task<(List<Problem> list, Dictionary<string, string> prints, Site site, int checkedCount)>? running;
        static bool waiting;

        /// <summary>Check again by itself after each edit. Per user, kept between sessions.</summary>
        public static bool Auto { get => EditorPrefs.GetBool("Storey.problems.auto", true); set => EditorPrefs.SetBool("Storey.problems.auto", value); }

        /// <summary>Whether this layout is checked as it is edited.</summary>
        public static bool AutoFor(StoreyEdit e) => Auto;

        /// <summary>How many buildings the last check looked at (all of them the first time).</summary>
        public static int LastChecked { get; private set; }

        /// <summary>The problems found by the last check of the layout being edited (empty before one). Starts one when checking as you edit.</summary>
        public static List<Problem> For(StoreyEdit e)
        {
            double now = EditorApplication.timeSinceStartup;
            if (!ReferenceEquals(of, e)) Forget(e);
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

        static void Forget(StoreyEdit e) { of = e; checkedText = null; last = new List<Problem>(); lastPrints = null; lastSite = null; running = null; runningText = null; }

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
            if (!ReferenceEquals(of, e)) Forget(e);
            // its own copy of the layout, read from the text: the edit can carry on while it runs
            string text = e.Text; runningText = text; startedAt = EditorApplication.timeSinceStartup;
            var prevList = last; var prevPrints = checkedText != null ? lastPrints : null; var prevSite = lastSite;
            var mine = Task.Run<(List<Problem> list, Dictionary<string, string> prints, Site site, int checkedCount)>(() =>
            {
                var doc = PrototypeJson.Read(text).Document;
                var site = new Site(doc.buildings); var prints = Problems.Prints(doc);
                if (prevPrints == null || prevSite == null) return (Problems.Check(site, new PlayWorld(site)), prints, site, doc.buildings.Count);
                // only what changed since the last check, with what can change with it
                var changed = Problems.Changed(prevPrints, prints);
                if (changed.Count == 0) return (prevList, prints, site, 0);
                var scope = Problems.Scope(site, prevSite, changed);
                var fresh = Problems.Check(site, new PlayWorld(site), scope);
                return (Problems.Merge(site, prevList, fresh, scope), prints, site, scope.Count);
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
                if (mine.Status == TaskStatus.RanToCompletion)
                {
                    if (ReferenceEquals(of, e)) { var r = mine.Result; last = r.list; lastPrints = r.prints; lastSite = r.site; LastChecked = r.checkedCount; checkedText = text; }
                }
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
