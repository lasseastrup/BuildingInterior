#nullable enable
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Triband.Storey.Editor
{
    /// <summary>
    /// Tools > Storey > Import Prototype Layout: copies a JSON layout saved from the prototype's
    /// "Layout data" panel into the project as a <c>.storey</c> file, which the importer picks up.
    /// </summary>
    internal static class PrototypeImportMenu
    {
        [MenuItem(StoreyEditorInfo.MenuRoot + "Import Prototype Layout...", priority = 100)]
        static void Import()
        {
            string source = EditorUtility.OpenFilePanel("Prototype layout", "", "json,storey");
            if (string.IsNullOrEmpty(source)) return;

            string text = File.ReadAllText(source);
            try
            {
                PrototypeJson.Read(text);
            }
            catch (System.FormatException e)
            {
                EditorUtility.DisplayDialog("Not a Storey layout", e.Message, "OK");
                return;
            }

            string target = EditorUtility.SaveFilePanelInProject("Save layout", Path.GetFileNameWithoutExtension(source), StoreyImporter.Extension, "Where the .storey file goes");
            if (string.IsNullOrEmpty(target)) return;
            File.WriteAllText(target, text);
            AssetDatabase.ImportAsset(target);
            Selection.activeObject = AssetDatabase.LoadMainAssetAtPath(target);
        }

        /// <summary>Assets > Create > Storey > Layout: an empty layout to start from (add buildings with New in the Storey Site inspector).</summary>
        [MenuItem("Assets/Create/Storey/Layout", priority = 80)]
        static void CreateLayout() =>
            ProjectWindowUtil.CreateAssetWithContent("New Layout." + StoreyImporter.Extension, PrototypeJson.Write(new StoreyDocument()));
    }
}
