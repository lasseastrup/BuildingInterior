#nullable enable
using System.Collections.Generic;
using Triband.Storey.Play;
using Triband.Storey.Unity;
using UnityEngine;

namespace Triband.Storey.PlayKit
{
    /// <summary>
    /// The play kit's character (docs/PLAY.md, slice 5.4): walks the layout with the engine-free walk model
    /// (<see cref="Walker"/> on the occlusion system's <see cref="PlayWorld"/>), relative to the camera's facing, with no
    /// physics. Tells the occlusion system where it is. Shows the lift panel in a lift car, and which building and storey
    /// it is on (<see cref="StoreyHud"/>, while <see cref="showHud"/>). Its body is a capsule unless it has renderers of its
    /// own, drawn a second time through walls when the settings ask for the silhouette.
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
        [Tooltip("Show where the character is, the lift panel in a lift car, and the joystick on a touch screen. Off for measurement runs: then nothing is drawn or asked for it.")]
        public bool showHud = true;

        public readonly PlayerState State = new PlayerState();
        public readonly PlayInput Input = new PlayInput();
        Renderer[] silhouettes = new Renderer[0];
        bool spawned;
        StoreyHud? hud;

        /// <summary>The building and storey the character is on, worked out once a frame (null: outside).</summary>
        public (BuildingData b, int floor)? Here { get; private set; }
        /// <summary>The lift car it stands in and the storey, once a frame (null: none, or riding).</summary>
        public (BuildingData b, CoreData s, int floor)? Lift { get; private set; }
        /// <summary>The storeys the lift it stands in stops at, kept while it stays in that lift.</summary>
        public IReadOnlyList<int> Stops => stops;
        List<int> stops = new List<int>();
        (BuildingData? b, CoreData? s) stopsFor;

        /// <summary>
        /// Put the character at a point (world space, the feet). The site builds full detail round it ahead of the rest
        /// (<see cref="StoreySite.PrefetchAround"/>); a game teleporting behind a fade waits for <see cref="StoreySite.DetailReadyAround"/>.
        /// </summary>
        public void Spawn(Vector3 feet, float yaw = 0)
        {
            var o = Origin;
            State.Spawn(feet.x - o.x, feet.y - o.y, feet.z - o.z); State.face = yaw;
            spawned = true; Place();
            if (occlusion != null && occlusion.site != null) occlusion.site.PrefetchAround(feet);
        }

        Vector3 lastOrigin; bool hasOrigin;
        Vector3 Origin => occlusion != null && occlusion.site != null ? occlusion.site.transform.position : Vector3.zero;

        void Start()
        {
            if (!spawned) Spawn(transform.position, transform.eulerAngles.y * Mathf.Deg2Rad);
            if (GetComponentInChildren<Renderer>() == null) Body();
        }

        /// <summary>A capsule body, and its silhouette.</summary>
        void Body()
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            // at once: Destroy waits for the frame's end, and the silhouette cloned from it kept a collider moving every frame
            DestroyImmediate(body.GetComponent<Collider>());
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
            using var _t = StoreyTimings.Time("walk: character");
            var world = occlusion != null ? occlusion.World : null;
            var cam = viewCamera != null ? viewCamera : Camera.main;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            Input.Read(dt);
            // the HUD: a component of its own, on while shown (docs/PERFORMANCE.md §11.4)
            if (showHud && hud == null) { hud = gameObject.AddComponent<StoreyHud>(); hud.character = this; }
            if (hud != null && hud.enabled != showHud) hud.enabled = showHud;
            if (world == null) { Here = null; Lift = null; return; }   // the site has not been built yet
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
            // where it is and the lift it stands in, once a frame for the HUD, which asked on every GUI event; a lift's
            // stops made again only for another lift
            if (!showHud) { Here = null; Lift = null; return; }
            Here = world.Locate(State.x, State.y, State.z);
            Lift = State.ride == null ? world.LiftAt(State.x, State.y, State.z) : null;
            if (Lift is var (lb, ls, _) && (!ReferenceEquals(lb, stopsFor.b) || !ReferenceEquals(ls, stopsFor.s)))
            {
                stops = Generate.Cores.Stops(lb, ls); stopsFor = (lb, ls);
            }
        }

        void Place()
        {
            var at = Origin + new Vector3((float)State.x, (float)State.y, (float)State.z);
            transform.SetPositionAndRotation(at, Quaternion.Euler(0, (float)State.face * Mathf.Rad2Deg, 0));
            if (occlusion != null) occlusion.SetPlayer(at);
        }

        /// <summary>The lift it stands in called to a storey (the HUD's panel).</summary>
        internal void CallLift(int storey)
        {
            var world = occlusion != null ? occlusion.World : null;
            if (world != null && State.ride == null) Walker.CallLift(world, State, storey);
        }
    }
}
