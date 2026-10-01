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
        public static int DisplayDialogComplex(string title, string message, string ok, string cancel, string alt) => 0;
        public static void SetDirty(Object target) { }
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

    public sealed class InitializeOnLoadMethodAttribute : System.Attribute { }

    public sealed class CustomEditor : System.Attribute
    {
        public CustomEditor(System.Type inspectedType) { }
    }

    public class Editor : ScriptableObject
    {
        public Object target => new Object();
        public virtual void OnInspectorGUI() { }
        public bool DrawDefaultInspector() => false;
    }

    public class AssetPostprocessor { }

    public static class EditorApplication
    {
        public static event System.Func<bool> wantsToQuit;
        public static void QueuePlayerLoopUpdate() { }
    }

    public static class Undo
    {
        public delegate void UndoRedoCallback();
        public static UndoRedoCallback undoRedoPerformed;
        public static void RecordObject(Object objectToUndo, string name) { }
        public static void ClearUndo(Object identifier) { }
    }

    public class SceneView : EditorWindow
    {
        public static void RepaintAll() { }
    }

    public class EditorWindow : ScriptableObject { }

    public enum MessageType { None, Info, Warning, Error }

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
        public static void Space() { }
        public static int Popup(string label, int selectedIndex, string[] displayedOptions, params GUILayoutOption[] options) => selectedIndex;
        public static int DelayedIntField(string label, int value, params GUILayoutOption[] options) => value;
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

namespace UnityEditor.SceneManagement
{
    public static class EditorSceneManager
    {
        public delegate void SceneSavedCallback(UnityEngine.SceneManagement.Scene scene);
        public static event SceneSavedCallback sceneSaved;
    }
}
