#nullable enable
using UnityEngine;

namespace Triband.Storey.Unity
{
    /// <summary>
    /// A Storey Site's automatic-LOD numbers in the Game view (<see cref="StoreySite.showLodStats"/>), in Play mode: a
    /// component of its own, added while they are shown, so the site has no <c>OnGUI</c>, which IMGUI calls for every
    /// event of every frame whether or not anything is drawn (docs/PERFORMANCE.md §3.4).
    /// </summary>
    [AddComponentMenu("")]
    sealed class StoreyLodStatsOverlay : MonoBehaviour
    {
        internal StoreySite? site;

        void Awake() => useGUILayout = false;

        void OnGUI()
        {
            if (site == null || site.LodStats is not SiteRenderer.LodStats st) return;
            GUI.Box(new Rect(10, 10, 400, 76), "");
            GUI.Label(new Rect(18, 14, 390, 72), st.ToString());
        }
    }
}
