using UnityEditor;
using UnityEngine;

namespace Triband.Storey.Editor
{
    /// <summary>Menu anchor for the authoring package. Real tools land here from workstream 1 on.</summary>
    internal static class StoreyEditorInfo
    {
        public const string MenuRoot = "Tools/Storey/";

        [MenuItem(MenuRoot + "About", priority = 10000)]
        private static void About()
        {
            Debug.Log($"Storey data format {StoreyVersion.DataFormat}, prototype schema {StoreyVersion.PrototypeSchema}.");
        }
    }
}
