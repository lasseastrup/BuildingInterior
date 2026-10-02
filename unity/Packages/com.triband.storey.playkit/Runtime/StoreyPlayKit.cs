#nullable enable
using Triband.Storey.Unity;
using UnityEngine;

namespace Triband.Storey.PlayKit
{
    /// <summary>
    /// The play kit in one component (docs/PLAY.md, slice 5.4): add it next to a Storey Site, press Play, and walk the
    /// layout. It adds the site's occlusion system if there is none, and makes the character and the camera (the main
    /// camera, or a new one). The character starts where this object is, or where <b>Play from this floor</b> asked.
    /// A project with its own character and camera uses <see cref="StoreyOcclusion"/> directly instead.
    /// </summary>
    [AddComponentMenu("Storey/Play Kit/Storey Play Kit")]
    public sealed class StoreyPlayKit : MonoBehaviour
    {
        [Tooltip("The layout to walk; the one on this object, or the first in the scene.")]
        public StoreySite? site;
        [Tooltip("The project's occlusion settings.")]
        public StoreyOcclusionSettings? settings;

        public StoreyCharacter? Character { get; private set; }
        public StoreyPlayCamera? PlayCamera { get; private set; }

        void Awake()
        {
            var request = PlayFrom.Take();
            if (site == null) site = GetComponent<StoreySite>();
            if (site == null && request != null) foreach (var s in FindObjectsByType<StoreySite>(FindObjectsSortMode.None)) if (s.name == request.Value.site) site = s;
            if (site == null) site = FindAnyObjectByType<StoreySite>();
            if (site == null) { Debug.LogWarning("Storey Play Kit: no Storey Site in the scene to walk."); enabled = false; return; }

            var occ = site.GetComponent<StoreyOcclusion>();
            if (occ == null) occ = site.gameObject.AddComponent<StoreyOcclusion>();
            if (settings != null) occ.settings = settings;
            occ.Attach(site);

            var cam = Camera.main;
            if (cam == null) { var go = new GameObject("Storey Play Camera"); cam = go.AddComponent<Camera>(); go.tag = "MainCamera"; }
            occ.viewCamera = cam;

            Character = new GameObject("Storey Character").AddComponent<StoreyCharacter>();
            Character.occlusion = occ; Character.viewCamera = cam;
            occ.player = Character.transform;
            if (request != null) Character.Spawn(request.Value.feet, request.Value.yaw);
            else Character.Spawn(transform.position, transform.eulerAngles.y * Mathf.Deg2Rad);

            PlayCamera = cam.GetComponent<StoreyPlayCamera>() ?? cam.gameObject.AddComponent<StoreyPlayCamera>();
            PlayCamera.character = Character; PlayCamera.occlusion = occ;
        }

        /// <summary>
        /// Play from this floor (the editor's Interior tab): when Play mode starts with a request and the scene has no
        /// play kit, one is made, so a layout can be walked with no setup.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void PlayFromRequest()
        {
            if (!PlayFrom.Pending || FindAnyObjectByType<StoreyPlayKit>() != null) return;
            new GameObject("Storey Play Kit").AddComponent<StoreyPlayKit>();
        }
    }
}
