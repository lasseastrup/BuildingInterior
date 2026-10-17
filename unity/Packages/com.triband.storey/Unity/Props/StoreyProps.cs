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
    /// <item>for a prop set to Hide Whole, switches it off while its point is in a storey the occlusion hides;</item>
    /// <item>in Play mode, gives its renderers their materials' steady copies while nothing can hide its building's
    /// pixels, for a graph with the steady variant (docs/PROPS.md §2.2, docs/PERFORMANCE.md §11.1).</item>
    /// </list>
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(1001)]   // after the sites (1000): the table indices and the occlusion's rows are this frame's
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
            public SiteRenderer? renderer;   // the site's renderer it was filed with: one made anew starts its versions again
            public uint written = uint.MaxValue;
            public Group? group;
            public bool lodOff, wholeOff, applied;
            // its places in the lists it is in (-1: not in it), so it leaves one by swapping with the last
            public int at = -1, movingAt = -1, wholeAt = -1;
            // per renderer, its own materials and those with their steady copies (the same array when none has one); none
            // looked at yet (0), none with a copy (-1), some (1); and whether it has the copies now
            public Material[][]? own, steadyMats;
            public sbyte copies;
            public bool steady;
        }

        internal sealed class Group
        {
            public readonly List<Entry> members = new List<Entry>();
            public (StoreySite site, int idx) key;
            public bool off, steady;
        }

        /// <summary>
        /// Entries with each one's place in the list on the entry: one leaves by the last taking its place, where
        /// <c>List.Remove</c> searched the list (10,000 props took 19–148 ms to unregister, docs/PERFORMANCE.md §11.1).
        /// </summary>
        sealed class EntryList
        {
            readonly List<Entry> items = new List<Entry>();
            readonly int kind;   // which of the entry's places: 0 at, 1 movingAt, 2 wholeAt
            public EntryList(int kind) { this.kind = kind; }
            ref int Place(Entry e)
            {
                switch (kind) { case 0: return ref e.at; case 1: return ref e.movingAt; default: return ref e.wholeAt; }
            }
            public int Count => items.Count;
            public Entry this[int i] => items[i];
            public void Add(Entry e) { ref int p = ref Place(e); if (p >= 0) return; p = items.Count; items.Add(e); }
            public void Remove(Entry e)
            {
                ref int p = ref Place(e); if (p < 0) return;
                int last = items.Count - 1; var moved = items[last];
                items[p] = moved; Place(moved) = p;
                items.RemoveAt(last); p = -1;
            }
        }

        static StoreyProps? instance;
        readonly EntryList entries = new EntryList(0), moving = new EntryList(1), whole = new EntryList(2);
        readonly Dictionary<StoreyProp, Entry> byProp = new Dictionary<StoreyProp, Entry>();
        readonly Dictionary<(StoreySite, int), Group> groups = new Dictionary<(StoreySite, int), Group>();
        int seenVersion = -1;
        // each site's renderer and version when its props were last filed, and the sites built anew since (a rebuild
        // re-files only their props, and the props in the street against them alone)
        readonly Dictionary<StoreySite, (SiteRenderer? r, int v)> siteVersions = new Dictionary<StoreySite, (SiteRenderer?, int)>();
        readonly List<StoreySite> rebuiltSites = new List<StoreySite>(), goneSites = new List<StoreySite>();
        bool wasPlaying;

        /// <summary>How many props are kept (for the inspector and tests).</summary>
        public static int Count => instance != null ? instance.entries.Count : 0;

        /// <summary>Renderers given their materials' steady copies or their own again, since <see cref="StoreyDiagnostics"/> last read it (main thread).</summary>
        internal static int SteadySwaps;

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
            m.byProp.Remove(p); m.entries.Remove(e); m.moving.Remove(e); m.whole.Remove(e); p.entry = null;
            SetSteady(e, false);   // its own materials back
            m.Join(e, null, leaving: true);
            Write(e, 0); e.lodOff = e.wholeOff = false; Apply(e);
        }

        internal static void Refresh(StoreyProp p)
        {
            if (instance == null || !instance.byProp.TryGetValue(p, out var e)) return;
            SetSteady(e, false); e.copies = 0; e.own = e.steadyMats = null;   // its renderers or their materials may be others now
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
            if (e.moving) moving.Add(e); else moving.Remove(e);
            if (p.hideWhole) whole.Add(e); else whole.Remove(e);
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
            foreach (var s in StoreySite.AllSet) if (BuildingIn(s, at, out id)) { site = s; break; }
            e.site = site; e.buildingId = id;
            Resolve(e);
        }

        /// <summary>A prop in the street, looked for again in the sites built anew alone: the others' buildings haven't changed.</summary>
        void LocateIn(Entry e, List<StoreySite> sites)
        {
            var at = PointOf(e); e.last = at; e.placed = true;
            foreach (var s in sites)
                if (BuildingIn(s, at, out var id)) { e.site = s; e.buildingId = id; Resolve(e); return; }
        }

        static bool BuildingIn(StoreySite s, Vector3 at, out string? id)
        {
            id = null;
            var layout = s.Renderer?.Site;
            if (layout == null || !s.isActiveAndEnabled) return false;
            var o = s.transform.position;
            var b = PropLocator.BuildingAt(layout, at.x - o.x, at.y - o.y, at.z - o.z);
            if (b == null) return false;
            id = b.id; return true;
        }

        /// <summary>The building's table index (it moves when its site is built again), written into the renderers.</summary>
        void Resolve(Entry e)
        {
            var r = e.site != null ? e.site.Renderer : null;
            e.index = r != null && e.buildingId != null ? r.TableIndexOf(e.buildingId) : -1;
            e.siteVersion = r != null ? r.Version : -1; e.renderer = r;
            if (e.index < 0) { e.site = null; e.buildingId = null; }
            Join(e, e.site != null && e.index >= 0 ? GroupOf(e.site, e.index) : null);
            Write(e, PropLocator.Encode(e.index));
        }

        Group GroupOf(StoreySite s, int idx)
        {
            if (!groups.TryGetValue((s, idx), out var g)) groups[(s, idx)] = g = new Group { key = (s, idx) };
            return g;
        }

        void Join(Entry e, Group? g, bool leaving = false)
        {
            if (e.group != g)
            {
                var was = e.group;
                if (was != null)
                {
                    was.members.Remove(e);   // a building's props: a few
                    if (was.members.Count == 0) groups.Remove(was.key);   // no empty groups are kept
                }
                e.group = g;
                if (g != null) { g.members.Add(e); e.lodOff = g.off; } else e.lodOff = false;
                Apply(e);
            }
            // as its building's props are; one in the street is never hidden: steady throughout Play mode
            if (!leaving) SetSteady(e, g != null ? g.steady : Application.isPlaying);
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
            using var _t = StoreyTimings.Time("props: frame");
            bool playing = Application.isPlaying;
            if (playing != wasPlaying) Played(playing);
            // a site built again (an edit, a district streamed in): indices may have moved, and props in the street may be
            // in one of its buildings now. Only the props of the sites built anew are filed again
            int any = SiteRenderer.AnyVersion;
            if (any != seenVersion)
            {
                seenVersion = any;
                Rebuilt();
            }
            // a moving prop: found again once it has gone 5 cm (a rigidbody's only while awake); in the editor, a still one
            // moved by hand. A still prop costs nothing a frame in Play mode
            if (playing)
                for (int i = 0; i < moving.Count; i++)
                {
                    var e = moving[i];
                    if (e.prop == null || (e.body != null && e.body.IsSleeping())) continue;
                    var at = PointOf(e);
                    if (!e.placed || (at - e.last).sqrMagnitude > MoveThreshold * MoveThreshold) Locate(e);
                }
            else
                for (int i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];
                    if (e.prop == null) continue;
                    bool look = e.moving ? e.body == null || !e.body.IsSleeping() : e.prop.transform.hasChanged;
                    if (!look) continue;
                    if (!e.moving) e.prop.transform.hasChanged = false;
                    var at = PointOf(e);
                    if (!e.placed || (at - e.last).sqrMagnitude > MoveThreshold * MoveThreshold) Locate(e);
                }

            // a building's props off once it has settled at LOD1 or LOD2 (Play mode: the editor shows everything); on the
            // steady copies while nothing can hide its pixels
            foreach (var kv in groups)
            {
                var (site, idx) = kv.Key; var g = kv.Value;
                var r = site != null ? site.Renderer : null;
                bool off = playing && r != null && !r.DetailShown(idx);
                if (off != g.off)
                {
                    g.off = off;
                    foreach (var e in g.members) { e.lodOff = off; Apply(e); }
                }
                bool steady = playing && r != null && !off && r.PropSteady(idx);
                if (steady != g.steady)
                {
                    g.steady = steady;
                    foreach (var e in g.members) SetSteady(e, steady);
                }
            }

            // props drawn whole: off while their point is in a storey the occlusion hides
            for (int i = 0; i < whole.Count; i++)
            {
                var e = whole[i];
                var core = e.site != null && e.site.Occlusion != null ? e.site.Occlusion.Core : null;
                var b = e.buildingId != null && e.site != null ? e.site.Renderer?.Site?.ById(e.buildingId) : null;
                bool off = playing && core != null && b != null && core.HidesPoint(b, e.last.y - e.site!.transform.position.y);
                if (off == e.wholeOff) continue;
                e.wholeOff = off; Apply(e);
            }

            if (playing && copies.Count > 0 && Time.frameCount - copiesSynced >= 30) SyncCopies();
        }

        /// <summary>
        /// After a site was built again: the props of each site built anew filed again (their buildings' indices may have
        /// moved, or the building gone), and the props in the street looked for in those sites. The others stay as they are.
        /// </summary>
        void Rebuilt()
        {
            rebuiltSites.Clear();
            foreach (var s in StoreySite.AllSet)
            {
                var r = s.Renderer; int v = r != null ? r.Version : -1;
                if (!siteVersions.TryGetValue(s, out var seen) || !ReferenceEquals(seen.r, r) || seen.v != v) { siteVersions[s] = (r, v); rebuiltSites.Add(s); }
            }
            // sites gone or switched off are forgotten (their props are filed again below)
            goneSites.Clear();
            foreach (var s in siteVersions.Keys) if (s == null || !s.isActiveAndEnabled) goneSites.Add(s!);   // destroyed is == null, but still the key
            foreach (var s in goneSites) siteVersions.Remove(s);
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e.prop == null) continue;
                if (e.site == null) { if (rebuiltSites.Count > 0) LocateIn(e, rebuiltSites); continue; }
                var r = e.site.Renderer;
                if (r == null || !e.site.isActiveAndEnabled) { Locate(e); continue; }
                if (ReferenceEquals(r, e.renderer) && r.Version == e.siteVersion) continue;
                if (r.Site?.ById(e.buildingId!) == null) Locate(e); else Resolve(e);
            }
        }

        /// <summary>Play mode started or ended: the props in the street on their steady copies, or every prop on its own materials again.</summary>
        void Played(bool playing)
        {
            wasPlaying = playing;
            for (int i = 0; i < entries.Count; i++) { var e = entries[i]; if (e.group == null || !playing) SetSteady(e, playing && e.group == null); }
            if (playing) return;
            foreach (var g in groups.Values) g.steady = false;
            ReleaseCopies();
        }

        // ---- prop materials' steady copies (docs/PROPS.md §2.2, docs/PERFORMANCE.md §11.1) ----

        // every prop graph is alpha-tested (it clips for the occlusion), so on Apple GPUs every prop draw was a discarding
        // one, the cost the buildings' steady variant avoids. A graph with the STOREY_STEADY keyword gets a copy of each
        // material with it on, its alpha clip off where a material may turn it off, in the opaque queue
        sealed class Copy { public Material source = null!, copy = null!; public UnityEngine.Rendering.LocalKeyword steady, clip; }
        static readonly Dictionary<Material, Copy?> copyOf = new Dictionary<Material, Copy?>();
        static readonly List<Copy> copies = new List<Copy>();
        static readonly List<Material> current = new List<Material>();
        static readonly int AlphaClip = Shader.PropertyToID("_AlphaClip");
        static int copiesSynced = -1;

        /// <summary>
        /// A material's steady copy: none for a graph without the STOREY_STEADY keyword, for a transparent one, or for one
        /// whose alpha clip is baked into its shader (Alpha Clipping on without Allow Material Override), which would discard
        /// all the same.
        /// </summary>
        static Material? SteadyCopy(Material? m)
        {
            if (m == null) return null;
            if (copyOf.TryGetValue(m, out var c)) return c?.copy;
            c = null;
            var sh = m.shader;
            if (sh != null && m.renderQueue <= (int)UnityEngine.Rendering.RenderQueue.GeometryLast)
            {
                var steady = sh.keywordSpace.FindKeyword("STOREY_STEADY");
                var clip = sh.keywordSpace.FindKeyword("_ALPHATEST_ON");
                bool baked = !clip.isValid && m.renderQueue >= (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                if (steady.isValid && !baked)
                {
                    c = new Copy { source = m, copy = new Material(m) { name = m.name + " (steady)", hideFlags = HideFlags.HideAndDontSave }, steady = steady, clip = clip };
                    SteadyOn(c);
                    copies.Add(c);
                }
            }
            copyOf[m] = c;
            return c?.copy;
        }

        static void SteadyOn(Copy c)
        {
            var m = c.copy;
            m.SetKeyword(c.steady, true);
            if (c.clip.isValid) m.SetKeyword(c.clip, false);
            if (m.HasProperty(AlphaClip)) m.SetFloat(AlphaClip, 0);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
        }

        /// <summary>The copies follow their materials' properties and keywords, twice a second.</summary>
        static void SyncCopies()
        {
            copiesSynced = Time.frameCount;
            foreach (var c in copies)
            {
                if (c.source == null || c.copy == null) continue;
                c.copy.CopyMatchingPropertiesFromMaterial(c.source);
                c.copy.shaderKeywords = c.source.shaderKeywords;
                SteadyOn(c);
            }
        }

        static void ReleaseCopies()
        {
            foreach (var c in copies) if (c.copy != null) DestroyImmediate(c.copy);
            copies.Clear(); copyOf.Clear();
        }

        /// <summary>A prop's renderers' materials, and their steady copies, as they are now.</summary>
        static void Capture(Entry e)
        {
            int n = e.renderers.Length;
            e.own = new Material[n][]; e.steadyMats = new Material[n][]; e.copies = -1;
            for (int i = 0; i < n; i++) if (CaptureOne(e, i)) e.copies = 1;
        }

        static bool CaptureOne(Entry e, int i)
        {
            var r = e.renderers[i];
            var own = r != null ? r.sharedMaterials : System.Array.Empty<Material>();
            Material[]? steady = null;
            for (int k = 0; k < own.Length; k++)
            {
                var c = SteadyCopy(own[k]); if (c == null) continue;
                steady ??= (Material[])own.Clone();
                steady[k] = c;
            }
            e.own![i] = own; e.steadyMats![i] = steady ?? own;
            return steady != null;
        }

        static bool Same(List<Material> a, Material[] b)
        {
            if (a.Count != b.Length) return false;
            for (int i = 0; i < b.Length; i++) if (!ReferenceEquals(a[i], b[i])) return false;
            return true;
        }

        /// <summary>
        /// A prop's renderers on their materials' steady copies, or on their own again. Materials the game gave a renderer
        /// meanwhile are its own from then on.
        /// </summary>
        static void SetSteady(Entry e, bool on)
        {
            if (e.steady == on) return;
            if (on && e.copies == 0) Capture(e);
            e.steady = on;
            if (e.copies <= 0) return;
            for (int i = 0; i < e.renderers.Length; i++)
            {
                var r = e.renderers[i]; if (r == null) continue;
                var own = e.own![i]; var steady = e.steadyMats![i];
                if (ReferenceEquals(own, steady)) continue;   // none of its materials has a copy
                r.GetSharedMaterials(current);
                if (!Same(current, on ? own : steady))
                {
                    CaptureOne(e, i);
                    own = e.own[i]; steady = e.steadyMats[i];
                    if (!on || ReferenceEquals(own, steady)) continue;   // leaving the copies: it keeps the game's
                }
                r.sharedMaterials = on ? steady : own;
                SteadySwaps++;
            }
        }

        // after a script reload the hidden manager is still there with nothing in it: it carries on, and a second goes
        void OnEnable() { if (instance == null) instance = this; else if (instance != this) DestroyImmediate(gameObject); }
        void OnDestroy() { if (instance == this) { instance = null; ReleaseCopies(); } }

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
