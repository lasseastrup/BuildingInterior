// Hand-written declarations of the editor members the authoring package uses. Same bargain
// as UnityEngine.cs: empty bodies, exact signatures, only what is called.

using UnityEngine;

namespace UnityEditor
{
    public sealed class MenuItemAttribute : System.Attribute
    {
        public MenuItemAttribute(string itemName) { }
        public MenuItemAttribute(string itemName, bool isValidateFunction) { }
        public MenuItemAttribute(string itemName, bool isValidateFunction, int priority) { }
        public int priority { get; set; }
    }

    public static class EditorUtility
    {
        public static string OpenFilePanel(string title, string directory, string extension) => "";
        public static string SaveFilePanelInProject(string title, string defaultName, string extension, string message) => "";
        public static bool DisplayDialog(string title, string message, string ok) => true;
    }

    public static class AssetDatabase
    {
        public static void ImportAsset(string path) { }
        public static Object LoadMainAssetAtPath(string assetPath) => new Object();
    }

    public static class Selection
    {
        public static Object activeObject { get; set; } = new Object();
    }
}

namespace UnityEditor.AssetImporters
{
    public sealed class ScriptedImporterAttribute : System.Attribute
    {
        public ScriptedImporterAttribute(int version, string ext) { }
        public ScriptedImporterAttribute(int version, string[] exts) { }
    }

    public abstract class ScriptedImporter : Object
    {
        public abstract void OnImportAsset(AssetImportContext ctx);
    }

    public sealed class AssetImportContext
    {
        public string assetPath { get; } = "";
        public void AddObjectToAsset(string identifier, Object obj) { }
        public void SetMainObject(Object obj) { }
        public void LogImportWarning(string msg) { }
        public void LogImportError(string msg) { }
    }
}
