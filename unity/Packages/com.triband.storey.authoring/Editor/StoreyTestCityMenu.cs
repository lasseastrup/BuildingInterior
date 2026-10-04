#nullable enable
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Unity;
using UnityEditor;
using UnityEngine;

namespace Triband.Storey.Editor
{
    /// <summary>
    /// The prototype's stress-test city (docs/CITY.md §1), from <i>Tools ▸ Storey ▸ Test City</i> rather than the site's
    /// inspector, so nobody adds a few thousand buildings to a layout by accident. Acts on the selected Storey Site; making
    /// one asks first. Saved with the layout, like any edit, and undone the same way.
    /// </summary>
    internal static class StoreyTestCityMenu
    {
        const string Root = "Tools/Storey/Test City/";

        static StoreySite? Selected => Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<StoreySite>() : null;

        static bool HasLayout() { var s = Selected; return s != null && s.layout != null; }

        [MenuItem(Root + "Make 300 Buildings")] static void Make300() => Make(300);
        [MenuItem(Root + "Make 1,000 Buildings")] static void Make1000() => Make(1000);
        [MenuItem(Root + "Make 3,000 Buildings")] static void Make3000() => Make(3000);
        [MenuItem(Root + "Make 300 Buildings", true)] static bool Can300() => HasLayout();
        [MenuItem(Root + "Make 1,000 Buildings", true)] static bool Can1000() => HasLayout();
        [MenuItem(Root + "Make 3,000 Buildings", true)] static bool Can3000() => HasLayout();

        [MenuItem(Root + "Remove Test City")]
        static void Remove()
        {
            var e = Edit(); if (e == null) return;
            int gen = e.Document.buildings.Count(b => b.gen);
            if (gen == 0) { Debug.Log("Storey: this layout has no test city."); return; }
            e.Apply("Removed the test city", d => TestCity.Clear(d) > 0);
        }
        [MenuItem(Root + "Remove Test City", true)] static bool CanRemove() => HasLayout();

        static void Make(int n)
        {
            var e = Edit(); if (e == null) return;
            if (!EditorUtility.DisplayDialog("Make a test city",
                    $"Add {n:N0} generated buildings round \"{e.Site!.layout!.name}\"'s own? Any test city already there is replaced. The layout saves as usual; undo or Tools ▸ Storey ▸ Test City ▸ Remove Test City takes them out.",
                    "Make it", "Cancel")) return;
            e.Apply($"Made a test city of {n:N0}", d => { TestCity.Generate(d, n); return true; });
            if (e.Site.lodMode == StoreySite.LodMode.Fixed)
                Debug.LogWarning("Storey: the site's LOD mode is Fixed, so every building builds every LOD. Set it to Automatic in Site settings for a city this size.");
        }

        static StoreyEdit? Edit()
        {
            var s = Selected;
            if (s == null || s.layout == null) { Debug.LogWarning("Storey: select a Storey Site with a layout first."); return null; }
            return StoreyEdit.Of(s) ?? StoreyEdit.Begin(s);
        }
    }
}
