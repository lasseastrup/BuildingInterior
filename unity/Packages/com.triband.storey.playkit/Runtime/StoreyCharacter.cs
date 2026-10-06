#nullable enable
using System.Linq;
using Triband.Storey.Play;
using Triband.Storey.Unity;
using UnityEngine;

namespace Triband.Storey.PlayKit
{
    /// <summary>
    /// The play kit's character (docs/PLAY.md, slice 5.4): walks the layout with the engine-free walk model
    /// (<see cref="Walker"/> on the occlusion system's <see cref="PlayWorld"/>), relative to the camera's facing, with no
    /// physics. Tells the occlusion system where it is. Shows the lift panel in a lift car, and which building and storey
    /// it is on. Its body is a capsule unless it has renderers of its own, drawn a second time through walls when the
    /// settings ask for the silhouette.
    /// </summary>
    [AddComponentMenu("Storey/Play Kit/Storey Character")]
    public sealed class StoreyCharacter : MonoBehaviour
    {
        [Tooltip("The occlusion system of the site walked in.")]
        public StoreyOcclusion? occlusion;
        [Tooltip("The camera whose facing 'forward' follows; the main camera when empty.")]
        public Camera? viewCamera;
        [Tooltip("The colour the character shows through walls.")]
        public Color silhouetteColour = new Color(1f, 0.49f, 0.2f, 0.85f);
        [Tooltip("A material with the Storey/Silhouette shader (so builds include it); found by name in the editor when empty.")]
        public Material? silhouette;

        public readonly PlayerState State = new PlayerState();
        public readonly PlayInput Input = new PlayInput();
        Renderer[] silhouettes = new Renderer[0];
        bool spawned;
        GUIStyle? label;

        /// <summary>Put the character at a point (world space, the feet).</summary>
        public void Spawn(Vector3 feet, float yaw = 0)
        {
            var o = Origin;
            State.Spawn(feet.x - o.x, feet.y - o.y, feet.z - o.z); State.face = yaw;
            spawned = true; Place();
        }

        Vector3 lastOrigin; bool hasOrigin;
        Vector3 Origin => occlusion != null && occlusion.site != null ? occlusion.site.transform.position : Vector3.zero;

        void Start()
        {
            useGUILayout = false;   // the HUD draws with GUI, not GUILayout: skips the layout pass's garbage
            if (!spawned) Spawn(transform.position, transform.eulerAngles.y * Mathf.Deg2Rad);
            if (GetComponentInChildren<Renderer>() == null) Body();
        }

        /// <summary>A capsule body, and its silhouette.</summary>
        void Body()
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(transform, false);
            body.transform.localPosition = new Vector3(0, 0.9f, 0); body.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);
            var shader = silhouette != null ? silhouette.shader : Shader.Find("Storey/Silhouette");
            if (shader != null)
            {
                var sil = Instantiate(body, transform); sil.name = "Silhouette";
                var mat = silhouette != null ? new Material(silhouette) : new Material(shader); mat.SetColor("_Color", silhouetteColour);
                var r = sil.GetComponent<Renderer>(); r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                silhouettes = new[] { r };
            }
        }

        void Update()
        {
            var world = occlusion != null ? occlusion.World : null;
            var cam = viewCamera != null ? viewCamera : Camera.main;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            Input.Read(dt);
            if (world == null) return;   // the site has not been built yet
            // the occlusion moved to another district (StoreyOcclusion.followPlayer): the walk state is in the site's own
            // space, so it moves by the difference between the two sites' origins, and the character stays put
            var o = Origin;
            if (hasOrigin && o != lastOrigin) { State.x += lastOrigin.x - o.x; State.y += lastOrigin.y - o.y; State.z += lastOrigin.z - o.z; }
            lastOrigin = o; hasOrigin = true;
            double yaw = 0;
            if (cam != null) { var f = cam.transform.forward; yaw = Mathf.Atan2(-f.x, -f.z); }   // the walk model's yaw: forward is (-sin, -cos)
            Walker.Step(world, State, new MoveInput(Input.Move.x, Input.Move.y, Input.Run), yaw, dt);
            Place();
            bool sil = occlusion != null && (occlusion.settings == null || occlusion.settings.silhouette);
            foreach (var r in silhouettes) if (r != null) r.enabled = sil;
        }

        void Place()
        {
            transform.position = Origin + new Vector3((float)State.x, (float)State.y, (float)State.z);
            transform.rotation = Quaternion.Euler(0, (float)State.face * Mathf.Rad2Deg, 0);
            if (occlusion != null) occlusion.SetPlayer(transform.position);
        }

        void OnGUI()
        {
            var world = occlusion != null ? occlusion.World : null;
            label ??= new GUIStyle(GUI.skin.box) { fontSize = Mathf.Max(12, Screen.height / 50), alignment = TextAnchor.MiddleLeft };
            float s = label.fontSize * 2.2f;
            // where the character is
            var loc = world?.Locate(State.x, State.y, State.z);
            // the label made again only when the place changes (OnGUI runs several times a frame)
            var at = loc == null ? (null, -1) : (loc.Value.b, loc.Value.floor);
            if (whereText == null || at.Item1 != whereFor.Item1 || at.Item2 != whereFor.Item2)
            {
                whereFor = at;
                whereText = loc == null ? "Outside" : $"{loc.Value.b.name} · {Floor(loc.Value.b, loc.Value.floor)}";
            }
            GUI.Box(new Rect(10, 10, s * 7, s), whereText, label);
            // the lift panel, in a car
            var lift = world?.LiftAt(State.x, State.y, State.z);
            if (lift != null && State.ride == null)
            {
                var (b, sh, floor) = lift.Value; var levels = Generate.Cores.Stops(b, sh);
                GUI.Box(new Rect(10, 20 + s, s * 1.4f + 10, (s + 4) * levels.Count + 10), "");
                for (int i = levels.Count - 1, row = 0; i >= 0; i--, row++)
                {
                    int k = levels[i];
                    GUI.enabled = k != floor;
                    if (GUI.Button(new Rect(15, 25 + s + row * (s + 4), s * 1.4f, s), Short(b, k))) Walker.CallLift(world!, State, k);
                }
                GUI.enabled = true;
            }
            // the on-screen joystick (touch)
            if (Application.isMobilePlatform || Input.joyActive)
            {
                var c = new Vector2(Input.joyCentre.x, Screen.height - Input.joyCentre.y); float r = Input.joyRadius;
                GUI.Box(new Rect(c.x - r, c.y - r, 2 * r, 2 * r), "");
                var t = c + new Vector2(Input.joyThumb.x, -Input.joyThumb.y);
                GUI.Box(new Rect(t.x - r * 0.35f, t.y - r * 0.35f, r * 0.7f, r * 0.7f), "");
            }
        }

        static string Floor(BuildingData b, int k) => k == b.floors.Count ? "Roof" : k == 0 ? "Ground floor" : "Floor " + k;
        static string Short(BuildingData b, int k) => k == b.floors.Count ? "R" : k == 0 ? "G" : k < Numbers.Length ? Numbers[k] : k.ToString();
        static readonly string[] Numbers = Enumerable.Range(0, 100).Select(i => i.ToString()).ToArray();
        string? whereText; (BuildingData?, int) whereFor;
    }
}
