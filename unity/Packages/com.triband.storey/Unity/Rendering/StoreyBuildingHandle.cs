#nullable enable
using UnityEngine;

namespace Triband.Storey.Unity
{
    /// <summary>
    /// One building of a Storey Site as a GameObject of its own (docs/PLAY.md §6), for a game's systems that switch
    /// buildings' objects off and on, as a converted scene's own occlusion switches off the building the player is behind.
    /// Switched off (its object, a parent of it, or the component), the building is hidden (<see cref="StoreySite.SetHidden"/>):
    /// it draws nothing, while its shadow is cast and its collider stays. Switched on, it shows again. Whatever goes first is
    /// safe: a site not loaded yet hides the building as it is built, and a site destroyed first is left alone. In Play mode
    /// only; in the editor it shows where its building is while selected. Nothing per frame. Storey's own.
    /// </summary>
    [AddComponentMenu("Storey/Storey Building Handle")]
    [DisallowMultipleComponent]
    public sealed class StoreyBuildingHandle : MonoBehaviour
    {
        [Tooltip("The Storey Site the building is in.")]
        public StoreySite? site;
        [Tooltip("The building's id in the site's layout. Switching this object off hides the building (its shadow and collider stay); switching it on shows it.")]
        public string buildingId = "";

        void OnEnable() { if (site != null) site.SetHidden(buildingId, false); }

        // a site destroyed first (its scene unloading, Play mode ending) is null here, and left alone
        void OnDisable() { if (site != null) site.SetHidden(buildingId, true); }

        /// <summary>Where the building is, while the handle is selected: its box, and a line to its foot. Nothing is moved.</summary>
        void OnDrawGizmosSelected()
        {
            if (site == null || !(site.Renderer?.Site is Generate.Site laid) || !(laid.ById(buildingId) is BuildingData b)) return;
            var (x0, z0, x1, z1) = laid.Bounds(b);
            float y0 = (float)b.elevation, y1 = (float)Derived.RoofY(b);
            var o = site.transform.position;
            var centre = o + new Vector3((float)(x0 + x1) / 2, (y0 + y1) / 2, (float)(z0 + z1) / 2);
            Gizmos.color = new Color(1f, 0.6f, 0.15f);
            Gizmos.DrawWireCube(centre, new Vector3((float)(x1 - x0), y1 - y0, (float)(z1 - z0)));
            Gizmos.DrawLine(transform.position, new Vector3(centre.x, o.y + y0, centre.z));
        }
    }
}
