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
        public static bool DisplayDialog(string title, string message, string ok, string cancel) => true;
    }

    public static class AssetDatabase
    {
        public static void ImportAsset(string path) { }
        public static string GetAssetPath(Object assetObject) => "";
        public static Object LoadMainAssetAtPath(string assetPath) => new Object();
    }

    public static class Selection
    {
        public static Object activeObject { get; set; } = new Object();
    }

    public class EditorWindow : ScriptableObject
    {
        public static T GetWindow<T>() where T : EditorWindow => null;
        public static T GetWindow<T>(bool utility, string title) where T : EditorWindow => null;
        public Vector2 minSize { get; set; }
        public Rect position { get; set; }
        public GUIContent titleContent { get; set; }
        public void Repaint() { }
    }

    public enum MessageType { None, Info, Warning, Error }

    public static class EditorStyles
    {
        public static GUIStyle boldLabel => new GUIStyle();
    }

    public static class EditorGUI
    {
        public sealed class DisabledScope : System.IDisposable
        {
            public DisabledScope(bool disabled) { }
            public void Dispose() { }
        }
    }

    public static class EditorGUILayout
    {
        public sealed class HorizontalScope : System.IDisposable
        {
            public HorizontalScope(params GUILayoutOption[] options) { }
            public void Dispose() { }
        }

        public static void HelpBox(string message, MessageType type) { }
        public static void LabelField(string label, params GUILayoutOption[] options) { }
        public static void LabelField(string label, GUIStyle style, params GUILayoutOption[] options) { }
        public static Vector2 BeginScrollView(Vector2 scrollPosition, params GUILayoutOption[] options) => scrollPosition;
        public static void EndScrollView() { }
        public static void Space() { }
        public static Color ColorField(GUIContent label, Color value, bool showEyedropper, bool showAlpha, bool hdr, params GUILayoutOption[] options) => value;
        public static System.Enum EnumPopup(System.Enum selected, params GUILayoutOption[] options) => selected;
        public static string TextField(string text, params GUILayoutOption[] options) => text;
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
