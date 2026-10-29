#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Unity;
using UnityEditor;
using UnityEngine;

namespace Triband.Storey.Editor
{
    /// <summary>
    /// A building drawn in an editor window, apart from every scene (docs/EDITOR.md §6.13): a preview scene of its own with a
    /// camera and two lights (Unity's PreviewRenderUtility), and Storey's own renderer (<see cref="SiteRenderer"/>) building
    /// a layout of one building in it, at a fixed LOD. So it looks as it would on a site: the site's materials, the palette,
    /// artists' windows, doors and details, the rooms behind the glass. The renderer shares the building table with the
    /// scene's sites, as every site does; the view globals they set (a building isolated, the cutaway, the clear glass's
    /// distance) are put aside while it draws and handed back after, so neither shows in the other.
    ///
    /// The mouse orbits (left), pans (right or middle) and zooms (the wheel); a double click frames it again. Also draws
    /// still images (<see cref="Still"/>) for the Storey Library's cards.
    /// </summary>
    internal sealed class StoreyPreview : IDisposable
    {
        /// <summary>What the camera frames: the whole building from a corner, a window on its front wall, or its street door.</summary>
        public enum Framing { Building, Window, Door }

        PreviewRenderUtility? pru;
        GameObject? root, shadow;
        Mesh? shadowMesh;
        Material? shadowMaterial;
        SiteRenderer? site;
        Material?[] builtWith = new Material?[3];
        string shownKey = "";
        BuildingData? shown;
        Bounds bounds;
        int built = -1;

        /// <summary>Why the building couldn't be built, or null.</summary>
        public string? Error { get; private set; }

        /// <summary>The LOD drawn: 0 full, 1 the shell, 2 the massing.</summary>
        public int Lod;

        /// <summary>The camera's turn round the building and its tilt down, in degrees, how near it is (1: the framing's own) and how far it is moved.</summary>
        public float Yaw = -38, Pitch = 18, Zoom = 1;
        public Vector3 Pan;

        /// <summary>The camera turns slowly round the building by itself.</summary>
        public bool Turntable;

        /// <summary>What the camera frames (set with the building, <see cref="Show"/>).</summary>
        public Framing Frames { get; private set; }

        /// <summary>What the building shown was shown for (<see cref="Show"/>'s key); empty with none.</summary>
        public string ShownKey => shownKey;

        /// <summary>A building is shown and built.</summary>
        public bool Ready => site != null && shown != null && Error == null;

        /// <summary>Changed every time the building is built again: what a still image is kept against.</summary>
        public int Built => built;

        /// <summary>
        /// Show the building of <paramref name="doc"/> (its first), built again only when <paramref name="key"/> changes:
        /// whatever describes it, so a repaint builds nothing. <paramref name="framing"/> is where the camera looks; a new
        /// framing puts the camera back to it.
        /// </summary>
        public void Show(StoreyDocument doc, string key, Framing framing)
        {
            var mats = Materials();
            bool same = mats.opaque == builtWith[0] && mats.glass == builtWith[1] && mats.massing == builtWith[2];
            bool reframe = framing != Frames;
            if (key == shownKey && same && site != null && !reframe) return;
            Frames = framing;
            Make();
            if (site != null && !same) { site.Dispose(); site = null; }
            site ??= new SiteRenderer(root!.transform, mats.opaque, mats.glass, mats.massing, HideFlags.HideAndDontSave, null);
            builtWith = new Material?[] { mats.opaque, mats.glass, mats.massing };
            shownKey = key; shown = doc.buildings.FirstOrDefault(); Error = null;
            try
            {
                site.Show(doc);
                // the meshes are written at the next frame: finish them now, so they can be measured
                var keep = Globals.Take();
                try { site.Lod = Lod; site.Frame(false, null); } finally { keep.Restore(); }
                bounds = Measure();
            }
            catch (Exception ex)
            {
                // a style or template the generator can't build: say so, rather than throwing every repaint
                Error = ex.Message; shown = null;
                site.Dispose(); site = null;
            }
            built++; drawnAt = default;
            PlaceShadow();
            if (reframe) Reframe();
        }

        /// <summary>Show nothing (no item picked): the next <see cref="Show"/> builds again.</summary>
        public void Clear()
        {
            site?.Dispose(); site = null;
            shown = null; shownKey = ""; Error = null;
        }

        /// <summary>The camera back to the framing's own view.</summary>
        public void Reframe()
        {
            Zoom = 1; Pan = Vector3.zero;
            (Yaw, Pitch) = Frames == Framing.Building ? (-38f, 18f) : (FrontYaw() - 22f, Frames == Framing.Door ? 4f : 7f);
        }

        // ---- drawing ----

        static readonly int Hint = "StoreyPreview".GetHashCode();

        /// <summary>
        /// The building in <paramref name="r"/>, and the mouse on it: orbit, pan, zoom, a double click to frame it again.
        /// Returns whether the view moved (the window repaints).
        /// </summary>
        public bool Draw(Rect r, IReadOnlyList<Rect>? holes = null)
        {
            bool moved = Mouse(r, holes);
            if (Event.current.type != EventType.Repaint || r.width < 8 || r.height < 8) return moved;
            if (!Ready) { EditorGUI.DrawRect(r, Background); return moved; }
            if (Turntable) { Yaw += (float)Math.Min(0.1, EditorApplication.timeSinceStartup - lastTurn) * 14f; moved = true; }
            lastTurn = EditorApplication.timeSinceStartup;
            // drawn again only when something it shows changed: the window repaints for every move of the pointer
            var view = (r.size, Yaw, Pitch, Zoom, Pan, Lod, built, EditorGUIUtility.isProSkin);
            if (view.Equals(drawnAt) && drawn != null && EditorApplication.timeSinceStartup - drawnTime < 1.0) GUI.DrawTexture(r, drawn, ScaleMode.StretchToFill, false);
            else { drawn = Render(r, still: false); drawnAt = view; drawnTime = EditorApplication.timeSinceStartup; }
            return moved;
        }
        double lastTurn, drawnTime;
        Texture? drawn;
        (Vector2, float, float, float, Vector3, int, int, bool) drawnAt;

        /// <summary>Draw it again at the next repaint, though nothing it shows changed (a material or the palette may have).</summary>
        public void Invalidate() => drawnAt = default;

        /// <summary>A still image of the building, <paramref name="w"/> × <paramref name="h"/> pixels, or null with nothing to show. The caller owns it.</summary>
        public Texture2D? Still(int w, int h)
        {
            if (!Ready) return null;
            var tex = Render(new Rect(0, 0, w, h), still: true) as Texture2D;
            if (tex != null) tex.hideFlags = HideFlags.HideAndDontSave;
            return tex;
        }

        Texture? Render(Rect r, bool still)
        {
            var p = pru!;
            var keep = Globals.Take();
            Texture? tex;
            try
            {
                site!.Lod = Lod;
                site.Frame(false, null);   // finishes the build's meshes and uploads the table
                Globals.Neutral();
                if (still) p.BeginStaticPreview(r); else p.BeginPreview(r, GUIStyle.none);
                Place(p, r.width / Math.Max(1, r.height));
                p.Render(true);
                tex = still ? p.EndStaticPreview() : p.EndPreview();
            }
            finally { keep.Restore(); }
            if (!still && tex != null) GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, false);
            return tex;
        }

        bool Mouse(Rect r, IReadOnlyList<Rect>? holes)
        {
            var ev = Event.current;
            int id = GUIUtility.GetControlID(Hint, FocusType.Passive, r);
            // the controls drawn over the preview (its holes) take their own clicks
            bool over = r.Contains(ev.mousePosition) && (holes == null || !holes.Any(h => h.Contains(ev.mousePosition)));
            switch (ev.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (!over) break;
                    if (ev.clickCount == 2) { Reframe(); ev.Use(); return true; }
                    GUIUtility.hotControl = id; ev.Use();
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != id) break;
                    if (ev.button == 0 && !ev.alt)
                    {
                        Yaw += ev.delta.x * 0.45f;
                        Pitch = Mathf.Clamp(Pitch + ev.delta.y * 0.35f, 3f, 85f);   // above the ground
                        Turntable = false;
                    }
                    else
                    {
                        // across the view, as far as the pointer moves at the building's distance
                        var rot = Quaternion.Euler(Pitch, Yaw, 0);
                        float k = Distance() * 2f * Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad) / Math.Max(1, r.height);
                        Pan += rot * new Vector3(-ev.delta.x * k, ev.delta.y * k, 0);
                    }
                    ev.Use(); return true;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; ev.Use(); }
                    break;
                case EventType.ScrollWheel:
                    if (!over) break;
                    Zoom = Mathf.Clamp(Zoom * (1 + ev.delta.y * 0.05f), 0.12f, 5f);
                    ev.Use(); return true;
            }
            if (GUIUtility.hotControl == id || over) EditorGUIUtility.AddCursorRect(r, GUIUtility.hotControl == id ? MouseCursor.Orbit : MouseCursor.Arrow);
            return false;
        }

        const float FieldOfView = 26f;

        /// <summary>The background: a quiet grey a shade apart from the window's, in either editor skin.</summary>
        public static Color Background => EditorGUIUtility.isProSkin ? new Color(0.17f, 0.18f, 0.2f) : new Color(0.72f, 0.73f, 0.75f);

        /// <summary>Where the camera looks and how much it takes in: the whole building, or a window's or the door's part of its front.</summary>
        (Vector3 at, float radius) Target()
        {
            if (Frames == Framing.Building || shown == null) return (bounds.center, Math.Max(2f, bounds.extents.magnitude * 0.8f));
            var (mid, _) = Front();
            if (Frames == Framing.Door) return (new Vector3(mid.x, 1.8f, mid.z), 3f);
            // a window: the storeys above the ground floor's, where an artist's window shows unlike a shopfront; two of them
            // where there are, so a window is seen with the one above it
            int n = shown.floors.Count;
            float y = n > 2 ? (float)Derived.FloorBase(shown, 2) : n > 1 ? (float)(Derived.FloorBase(shown, 1) + Derived.FloorH(shown, 1) * 0.5) : (float)Derived.FloorH(shown, 0) * 0.55f;
            return (new Vector3(mid.x, y, mid.z), n > 2 ? 4.3f : 3.6f);
        }

        float Distance() => Target().radius / Mathf.Sin(FieldOfView * 0.5f * Mathf.Deg2Rad) * Zoom;

        void Place(PreviewRenderUtility p, float aspect)
        {
            var (at, radius) = Target();
            var rot = Quaternion.Euler(Pitch, Yaw, 0);
            // wide views frame by height, narrow ones by width
            float d = Distance() * (aspect < 1 ? 1 / Math.Max(0.35f, aspect) : 1);
            var cam = p.camera;
            cam.fieldOfView = FieldOfView;
            cam.transform.SetPositionAndRotation(at + Pan - rot * Vector3.forward * d, rot);
            cam.nearClipPlane = Math.Max(0.05f, d - radius * 3);
            cam.farClipPlane = d + radius * 6 + 50;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Background;
            if (shadowMaterial != null) shadowMaterial.SetColor("_Color", Background);   // the ground's rim is the background
            // the key light keeps to the camera's side of the building, so the face looked at is lit; the fill from the other side
            p.lights[0].intensity = 1.15f;
            p.lights[0].transform.rotation = Quaternion.Euler(48, Yaw + 38, 0);
            p.lights[1].intensity = 0.45f;
            p.lights[1].transform.rotation = Quaternion.Euler(28, Yaw - 150, 0);
            p.ambientColor = EditorGUIUtility.isProSkin ? new Color(0.38f, 0.4f, 0.44f) : new Color(0.46f, 0.47f, 0.5f);
        }

        /// <summary>The middle of the building's front, its first wall (where the placeholder's street door is), and its outward direction.</summary>
        (Vector3 mid, Vector3 outward) Front()
        {
            var b = shown; if (b == null || b.footprint.Count < 2) return (bounds.center, Vector3.back);
            var a = b.footprint[0]; var c = b.footprint[1];
            var mid = new Vector3((float)((a.x + c.x) / 2 + b.pos.x), 0, (float)((a.z + c.z) / 2 + b.pos.z));
            var along = new Vector3((float)(c.x - a.x), 0, (float)(c.z - a.z)).normalized;
            var n = new Vector3(along.z, 0, -along.x);
            double cx = b.footprint.Average(q => q.x) + b.pos.x, cz = b.footprint.Average(q => q.z) + b.pos.z;
            if (Vector3.Dot(n, new Vector3((float)(mid.x - cx), 0, (float)(mid.z - cz))) < 0) n = -n;
            return (mid, n);
        }

        /// <summary>The yaw that looks straight at the front.</summary>
        float FrontYaw() { var (_, n) = Front(); return Mathf.Atan2(-n.x, -n.z) * Mathf.Rad2Deg; }

        /// <summary>The building's bounds: every renderer the site made but the ground's shadow.</summary>
        Bounds Measure()
        {
            bool any = false; var o = new Bounds();
            foreach (var r in root!.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.gameObject == shadow) continue;
                if (!any) { o = r.bounds; any = true; } else o.Encapsulate(r.bounds);
            }
            if (!any && shown != null) o = new Bounds(new Vector3((float)shown.pos.x, 5, (float)shown.pos.z), Vector3.one * 10);
            return o;
        }

        /// <summary>How many triangles each LOD has, and LOD0's rooms behind the glass (drawn with it where a window shows them).</summary>
        public (int lod0, int rooms, int lod1, int lod2) Triangles()
        {
            // counted once a build: the panel asks at every repaint
            if (trianglesOf == built) return triangles;
            trianglesOf = built;
            int l0 = 0, rooms = 0, l1 = 0, l2 = 0;
            if (root == null || !Ready) return triangles = default;
            foreach (var f in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = f.sharedMesh; if (m == null || f.gameObject == shadow) continue;
                int n = 0; for (int s = 0; s < m.subMeshCount; s++) n += (int)(m.GetIndexCount(s) / 3);
                string name = m.name;
                if (name.Contains(" LOD0 rooms")) rooms += n;
                else if (name.Contains(" LOD0")) l0 += n;
                else if (name.Contains(" LOD1")) l1 += n;
                else if (name.Contains(" LOD2")) l2 += n;
            }
            return triangles = (l0, rooms, l1, l2);
        }
        int trianglesOf = -2;
        (int, int, int, int) triangles;

        // ---- the preview scene ----

        void Make()
        {
            if (pru != null && root != null) return;
            pru = new PreviewRenderUtility();
            pru.camera.cameraType = CameraType.Preview;
            root = new GameObject("Storey Preview") { hideFlags = HideFlags.HideAndDontSave };
            pru.AddSingleGO(root);
            // the ground: a disc under the building, solid, so a foundation reaching below ground is hidden as a scene's ground
            // hides it; a shade darker round the building, as a soft shadow, and the background's own colour at its rim, so
            // it has no edge
            shadowMesh = Disc(); shadowMesh.hideFlags = HideFlags.HideAndDontSave;
            shadowMaterial = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
            shadowMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            shadowMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            shadowMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            shadowMaterial.SetInt("_ZWrite", 1);
            shadowMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);   // the shader's own is Always: it drew over the building
            shadow = new GameObject("Ground") { hideFlags = HideFlags.HideAndDontSave };
            shadow.AddComponent<MeshFilter>().sharedMesh = shadowMesh;
            shadow.AddComponent<MeshRenderer>().sharedMaterial = shadowMaterial;
            shadow.transform.SetParent(root.transform, false);
        }

        void PlaceShadow()
        {
            if (shadow == null) return;
            shadow.SetActive(Ready);
            if (!Ready) return;
            // under the building's middle, its shade reaching a little past its walls and the rim far beyond them; just under
            // the site's ground, clear of a floor or a plinth's foot lying on it
            float r = Math.Max(4, Math.Max(bounds.extents.x, bounds.extents.z));
            shadow.transform.localPosition = new Vector3(bounds.center.x, -0.01f, bounds.center.z);
            shadow.transform.localScale = new Vector3(r * 6, 1, r * 6);
        }

        static Mesh Disc()
        {
            // rings out from the middle: how far out (of the radius) and how bright, the rim the background's colour itself
            var rings = new (float at, float bright)[] { (0, 0.8f), (0.12f, 0.84f), (0.24f, 0.93f), (0.45f, 0.99f), (1, 1) };
            const int n = 64;
            var v = new List<Vector3> { Vector3.zero }; var c = new List<Color> { Grey(rings[0].bright) };
            var t = new List<int>();
            for (int ring = 1; ring < rings.Length; ring++)
                for (int i = 0; i < n; i++)
                {
                    float a = i * Mathf.PI * 2 / n;
                    v.Add(new Vector3(Mathf.Cos(a) * rings[ring].at, 0, Mathf.Sin(a) * rings[ring].at));
                    c.Add(Grey(rings[ring].bright));
                }
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                t.AddRange(new[] { 0, 1 + j, 1 + i });
                for (int ring = 1; ring < rings.Length - 1; ring++)
                {
                    int a = 1 + (ring - 1) * n, b = 1 + ring * n;
                    t.AddRange(new[] { a + i, a + j, b + j, a + i, b + j, b + i });
                }
            }
            var m = new Mesh { name = "Storey Preview Ground" };
            m.SetVertices(v); m.SetColors(c); m.SetTriangles(t, 0); m.RecalculateBounds();
            return m;
        }

        static Color Grey(float x) => new Color(x, x, x, 1);

        /// <summary>
        /// The materials it draws with: a Storey Site's in the open scenes (the project's own, lit as its sites are), or
        /// else Storey's own shaders.
        /// </summary>
        static (Material opaque, Material glass, Material massing) Materials()
        {
            foreach (var s in StoreySite.All)
                if (s != null && s.opaque != null && s.glass != null && s.massing != null) return (s.opaque, s.glass, s.massing);
            if (own.opaque == null || own.glass == null || own.massing == null)
                own = (Own("Storey/Opaque"), Own("Storey/Glass"), Own("Storey/Massing"));
            return (own.opaque!, own.glass!, own.massing!);
        }
        static (Material? opaque, Material? glass, Material? massing) own;

        static Material Own(string shader) => new Material(Shader.Find(shader)) { name = shader + " (Storey Library)", hideFlags = HideFlags.HideAndDontSave };

        public void Dispose()
        {
            site?.Dispose(); site = null;
            pru?.Cleanup(); pru = null;
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            if (shadowMesh != null) UnityEngine.Object.DestroyImmediate(shadowMesh);
            if (shadowMaterial != null) UnityEngine.Object.DestroyImmediate(shadowMaterial);
            root = shadow = null; shadowMesh = null; shadowMaterial = null;
            shown = null; shownKey = "";
        }

        /// <summary>
        /// The view globals every Storey material reads (<see cref="StoreyGlobals"/>), as the scene's sites left them: put
        /// aside while the preview draws, with neutral ones in their place, and handed back after. The static mirrors
        /// <see cref="StoreyGlobals"/> keeps of them aren't touched, so the scene's sites see no change.
        /// </summary>
        readonly struct Globals
        {
            readonly Vector4 cut, active, iso, glassFill, cam, focus, player;
            readonly float occMode, lodTint;

            Globals(bool _)
            {
                cut = Shader.GetGlobalVector(StoreyShaderIds.Cut); active = Shader.GetGlobalVector(StoreyShaderIds.Active); iso = Shader.GetGlobalVector(StoreyShaderIds.Iso);
                glassFill = Shader.GetGlobalVector(StoreyShaderIds.GlassFill); cam = Shader.GetGlobalVector(StoreyShaderIds.Cam); focus = Shader.GetGlobalVector(StoreyShaderIds.Focus);
                player = Shader.GetGlobalVector(StoreyShaderIds.Player);
                occMode = Shader.GetGlobalFloat(StoreyShaderIds.OccMode); lodTint = Shader.GetGlobalFloat(StoreyShaderIds.LodTint);
            }

            public static Globals Take() => new Globals(true);

            /// <summary>Nothing cut, isolated or in the way, the glass clear, no LOD tint.</summary>
            public static void Neutral()
            {
                Shader.SetGlobalVector(StoreyShaderIds.Cut, new Vector4(0, 1, 0, 0));
                Shader.SetGlobalVector(StoreyShaderIds.Active, new Vector4(-1, 1e9f, 0, 0));
                Shader.SetGlobalVector(StoreyShaderIds.Iso, new Vector4(0, -1, 0, 0));
                Shader.SetGlobalVector(StoreyShaderIds.GlassFill, Vector4.zero);
                Shader.SetGlobalFloat(StoreyShaderIds.OccMode, (float)StoreyGlobals.OcclusionMode.Off);
                Shader.SetGlobalFloat(StoreyShaderIds.LodTint, 0);
            }

            public void Restore()
            {
                Shader.SetGlobalVector(StoreyShaderIds.Cut, cut); Shader.SetGlobalVector(StoreyShaderIds.Active, active); Shader.SetGlobalVector(StoreyShaderIds.Iso, iso);
                Shader.SetGlobalVector(StoreyShaderIds.GlassFill, glassFill); Shader.SetGlobalVector(StoreyShaderIds.Cam, cam); Shader.SetGlobalVector(StoreyShaderIds.Focus, focus);
                Shader.SetGlobalVector(StoreyShaderIds.Player, player);
                Shader.SetGlobalFloat(StoreyShaderIds.OccMode, occMode); Shader.SetGlobalFloat(StoreyShaderIds.LodTint, lodTint);
            }
        }
    }
}
