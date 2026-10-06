#nullable enable
using System.Collections.Generic;
using Triband.Storey.Props;
using UnityEngine;

namespace Triband.Storey.Unity
{
    /// <summary>
    /// Every Storey Prop in the scene, kept in one list and updated in one loop after the sites have drawn (docs/PROPS.md
    /// §2.4): no Update per prop, and no garbage per frame. Made on demand, hidden. For each prop it
    /// <list type="bullet">
    /// <item>finds its building (<see cref="PropLocator"/>): once for a still prop, again as a moving one goes;</item>
    /// <item>writes the building's table index into its renderers' user value, again when a site is built anew;</item>
    /// <item>switches the building's props off once it has settled at LOD1 or LOD2 (Play mode), on again at LOD0;</item>
    /// <item>for a prop set to Hide Whole, switches it off while its point is in a storey the occlusion hides.</item>
    /// </list>
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(1000)]   // after the sites: the table indices are this frame's
    [AddComponentMenu("")]
    public sealed class StoreyProps : MonoBehaviour
    {
        /// <summary>How far a moving prop goes before it is found again (metres).</summary>
        public const float MoveThreshold = 0.05f;

        internal sealed class Entry
        {
            public StoreyProp prop = null!;
            public Renderer[] renderers = System.Array.Empty<Renderer>();
            public Rigidbody? body;
            public bool moving;
            public Vector3 last; public bool placed;
            public StoreySite? site; public string? buildingId; public int index = -1, siteVersion = -1;
            public uint written = uint.MaxValue;
            public Group? group;
            public bool lodOff, wholeOff, applied;
        }

        internal sealed class Group { public readonly List<Entry> members = new List<Entry>(); public bool off; }

        static StoreyProps? instance;
        readonly List<Entry> entries = new List<Entry>();
        readonly Dictionary<StoreyProp, Entry> byProp = new Dictionary<StoreyProp, Entry>();
        readonly Dictionary<(StoreySite, int), Group> groups = new Dictionary<(StoreySite, int), Group>();
        readonly List<Entry> whole = new List<Entry>();
        int seenVersion = -1;

        /// <summary>How many props are kept (for the inspector and tests).</summary>
        public static int Count => instance != null ? instance.entries.Count : 0;

        /// <summary>Tint every prop by its building, magenta for none: shows which props' shaders read their building.</summary>
        public static bool ShowBuildings
        {
            get => Shader.GetGlobalFloat(PropDebug) > 0.5f;
            set => Shader.SetGlobalFloat(PropDebug, value ? 1 : 0);
        }
        static readonly int PropDebug = Shader.PropertyToID("_StoreyPropDebug");

        static StoreyProps Instance
        {
            get
            {
                if (instance != null) return instance;
                var go = new GameObject("Storey Props") { hideFlags = HideFlags.HideAndDontSave };
                instance = go.AddComponent<StoreyProps>();
                return instance;
            }
        }

        internal static void Register(StoreyProp p)
        {
            var m = Instance;
            if (m.byProp.ContainsKey(p)) return;
            var e = new Entry { prop = p };
            m.byProp[p] = e; m.entries.Add(e); p.entry = e;
            m.Gather(e);
            m.Locate(e);
        }

        internal static void Unregister(StoreyProp p)
        {
            if (instance == null || !instance.byProp.TryGetValue(p, out var e)) return;
            var m = instance;
            m.byProp.Remove(p); m.entries.Remove(e); m.whole.Remove(e); p.entry = null;
            m.Join(e, null);
            Write(e, 0); e.lodOff = e.wholeOff = false; Apply(e);
        }

        internal static void Refresh(StoreyProp p)
        {
            if (instance == null || !instance.byProp.TryGetValue(p, out var e)) return;
            Write(e, 0); e.lodOff = e.wholeOff = false; Apply(e);
            instance.Gather(e);
            e.written = uint.MaxValue; e.placed = false;
            instance.Locate(e);
        }

        void Gather(Entry e)
        {
            var p = e.prop;
            e.renderers = p.CollectRenderers();
            e.body = p.GetComponentInParent<Rigidbody>() ?? p.GetComponentInChildren<Rigidbody>();
            e.moving = p.motion == StoreyProp.Motion.Moving || (p.motion == StoreyProp.Motion.Auto && e.body != null);
            whole.Remove(e); if (p.hideWhole) whole.Add(e);
        }

        static Vector3 PointOf(Entry e)
        {
            if (e.prop.locateBy == StoreyProp.Point.Pivot || e.renderers.Length == 0) return e.prop.transform.position;
            var b = e.renderers[0].bounds;
            for (int i = 1; i < e.renderers.Length; i++) b.Encapsulate(e.renderers[i].bounds);
            return b.center;
        }

        /// <summary>Which site and building the prop is in now, and its renderers told.</summary>
        void Locate(Entry e)
        {
            var at = PointOf(e); e.last = at; e.placed = true;
            StoreySite? site = null; string? id = null;
            foreach (var s in StoreySite.AllSet)
            {
                var r = s.Renderer; var layout = r?.Site;
                if (layout == null || !s.isActiveAndEnabled) continue;
                var o = s.transform.position;
                var b = PropLocator.BuildingAt(layout, at.x - o.x, at.y - o.y, at.z - o.z);
                if (b == null) continue;
                site = s; id = b.id; break;
            }
            e.site = site; e.buildingId = id;
            Resolve(e);
        }

        /// <summary>The building's table index (it moves when its site is built again), written into the renderers.</summary>
        void Resolve(Entry e)
        {
            var r = e.site != null ? e.site.Renderer : null;
            e.index = r != null && e.buildingId != null ? r.TableIndexOf(e.buildingId) : -1;
            e.siteVersion = r != null ? r.Version : -1;
            if (e.index < 0) { e.site = null; e.buildingId = null; }
            Join(e, e.site != null && e.index >= 0 ? GroupOf(e.site, e.index) : null);
            Write(e, PropLocator.Encode(e.index));
        }

        Group GroupOf(StoreySite s, int idx)
        {
            if (!groups.TryGetValue((s, idx), out var g)) groups[(s, idx)] = g = new Group();
            return g;
        }

        void Join(Entry e, Group? g)
        {
            if (e.group == g) return;
            e.group?.members.Remove(e);
            e.group = g;
            if (g != null) { g.members.Add(e); e.lodOff = g.off; } else e.lodOff = false;
            Apply(e);
        }

        static void Write(Entry e, uint value)
        {
            if (e.written == value) return;
            e.written = value;
            foreach (var r in e.renderers)
            {
                if (r == null) continue;
                if (r is MeshRenderer mr) mr.SetShaderUserValue(value);
                else if (r is SkinnedMeshRenderer sr) sr.SetShaderUserValue(value);
            }
        }

        static void Apply(Entry e)
        {
            bool off = e.lodOff || e.wholeOff;
            if (e.applied == off) return;
            e.applied = off;
            foreach (var r in e.renderers) if (r != null) r.forceRenderingOff = off;
        }

        void LateUpdate()
        {
            // a site built again (an edit, a district streamed in): indices may have moved, and props placed before their
            // site had built find it now
            int any = SiteRenderer.AnyVersion;
            bool rebuilt = any != seenVersion; seenVersion = any;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e.prop == null) continue;
                if (rebuilt)
                {
                    var r = e.site != null ? e.site.Renderer : null;
                    if (e.site == null || r == null) { Locate(e); continue; }
                    if (r.Version != e.siteVersion) { if (r.Site?.ById(e.buildingId!) == null) Locate(e); else Resolve(e); continue; }
                }
                // a moving prop: found again once it has gone 5 cm (a rigidbody's only while awake); a still one only when
                // moved in the editor
                bool look = e.moving ? (e.body == null || !e.body.IsSleeping()) : !Application.isPlaying && e.prop.transform.hasChanged;
                if (!look) continue;
                if (!e.moving) e.prop.transform.hasChanged = false;
                var at = PointOf(e);
                if (!e.placed || (at - e.last).sqrMagnitude > MoveThreshold * MoveThreshold) Locate(e);
            }

            // a building's props off once it has settled at LOD1 or LOD2 (Play mode: the editor shows everything)
            foreach (var kv in groups)
            {
                var (site, idx) = kv.Key; var g = kv.Value;
                var r = site != null ? site.Renderer : null;
                bool off = Application.isPlaying && r != null && !r.DetailShown(idx);
                if (off == g.off) continue;
                g.off = off;
                foreach (var e in g.members) { e.lodOff = off; Apply(e); }
            }

            // props drawn whole: off while their point is in a storey the occlusion hides
            for (int i = 0; i < whole.Count; i++)
            {
                var e = whole[i];
                var core = e.site != null && e.site.Occlusion != null ? e.site.Occlusion.Core : null;
                var b = e.buildingId != null && e.site != null ? e.site.Renderer?.Site?.ById(e.buildingId) : null;
                bool off = Application.isPlaying && core != null && b != null && core.HidesPoint(b, e.last.y - e.site!.transform.position.y);
                if (off == e.wholeOff) continue;
                e.wholeOff = off; Apply(e);
            }
        }

        // after a script reload the hidden manager is still there with nothing in it: it carries on, and a second goes
        void OnEnable() { if (instance == null) instance = this; else if (instance != this) DestroyImmediate(gameObject); }
        void OnDestroy() { if (instance == this) instance = null; }

        /// <summary>The building a prop is in, by name, and the storey (for the inspector); null in the street.</summary>
        public static (string building, int storey)? Where(StoreyProp p)
        {
            var e = p.entry; var r = e?.site != null ? e.site.Renderer : null;
            var b = e?.buildingId != null ? r?.Site?.ById(e.buildingId) : null;
            if (e == null || b == null) return null;
            return (b.name, Play.PlayWorld.FloorAtY(b, e.last.y - e.site!.transform.position.y));
        }
    }
}
