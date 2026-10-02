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
        public static string[] FindAssets(string filter) => System.Array.Empty<string>();
        public static string GUIDToAssetPath(string guid) => "";
        public static T LoadAssetAtPath<T>(string assetPath) where T : Object => null;
        public static void CreateAsset(Object asset, string path) { }
        public static void SaveAssets() { }
        public static Object LoadMainAssetAtPath(string assetPath) => new Object();
    }

    public static class Selection
    {
        public static Object activeObject { get; set; } = new Object();
        public static GameObject activeGameObject => new GameObject("");
    }

    public sealed class InitializeOnLoadMethodAttribute : System.Attribute { }
    public sealed class InitializeOnLoadAttribute : System.Attribute { }

    public static class ProjectWindowUtil
    {
        public static void CreateAssetWithContent(string filename, string content) { }
    }

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
        public delegate void CallbackFunction();
        public static CallbackFunction delayCall;
    }

    public enum Tool { View = 0, Move = 1, Rotate = 2, Scale = 3, Rect = 4, Transform = 5, Custom = 6, None = -1 }

    public static class Undo
    {
        public delegate void UndoRedoCallback();
        public static UndoRedoCallback undoRedoPerformed;
        public static void RecordObject(Object objectToUndo, string name) { }
        public static void IncrementCurrentGroup() { }
        public static void SetCurrentGroupName(string name) { }
        public static int GetCurrentGroup() => 0;
        public static void CollapseUndoOperations(int groupIndex) { }
        public static void ClearUndo(Object identifier) { }
    }

    public class SceneView : EditorWindow
    {
        public static void RepaintAll() { }
        public static SceneView lastActiveSceneView => null;
        public Camera camera => null;
        public Vector3 pivot => default;
    }

    public class EditorWindow : ScriptableObject
    {
        public void Repaint() { }
        public void ShowNotification(GUIContent notification, double fadeoutWait) { }
    }

    public static class EditorGUIUtility
    {
        public static float labelWidth => 0;
    }

    public static class EditorStyles
    {
        public static GUIStyle boldLabel => new GUIStyle();
        public static GUIStyle miniButton => new GUIStyle();
        public static GUIStyle miniLabel => new GUIStyle();
        public static GUIStyle whiteBoldLabel => new GUIStyle();
        public static GUIStyle helpBox => new GUIStyle();
        public static GUIStyle wordWrappedMiniLabel => new GUIStyle();
    }

    public sealed class GenericMenu
    {
        public delegate void MenuFunction();
        public void AddItem(GUIContent content, bool on, MenuFunction func) { }
        public void ShowAsContext() { }
    }

    public static class Handles
    {
        public delegate void CapFunction(int controlID, Vector3 position, Quaternion rotation, float size, EventType eventType);
        public static Color color { get; set; }
        public static Matrix4x4 matrix { get; set; }
        public static void DrawAAPolyLine(float width, params Vector3[] points) { }
        public static void DrawDottedLines(Vector3[] lineSegments, float screenSpaceSize) { }
        public static void DrawDottedLine(Vector3 p1, Vector3 p2, float screenSpaceSize) { }
        public static void DrawWireDisc(Vector3 center, Vector3 normal, float radius) { }
        public static void DrawWireCube(Vector3 center, Vector3 size) { }
        public static void Label(Vector3 position, string text, GUIStyle style) { }
        public static Vector3 Slider2D(int id, Vector3 handlePos, Vector3 offset, Vector3 handleDir, Vector3 slideDir1, Vector3 slideDir2, float handleSize, CapFunction capFunction, Vector2 snap, bool drawHelper) => handlePos;
        public static Vector3 Slider(int controlID, Vector3 position, Vector3 direction, float size, CapFunction capFunction, float snap) => position;
        public static void DotHandleCap(int controlID, Vector3 position, Quaternion rotation, float size, EventType eventType) { }
        public static void RectangleHandleCap(int controlID, Vector3 position, Quaternion rotation, float size, EventType eventType) { }
        public static void CubeHandleCap(int controlID, Vector3 position, Quaternion rotation, float size, EventType eventType) { }
        public static void CircleHandleCap(int controlID, Vector3 position, Quaternion rotation, float size, EventType eventType) { }
        public static void BeginGUI() { }
        public static void EndGUI() { }

        public struct DrawingScope : System.IDisposable
        {
            public DrawingScope(Matrix4x4 matrix) { }
            public void Dispose() { }
        }
    }

    public static class HandleUtility
    {
        public static int nearestControl => 0;
        public static Ray GUIPointToWorldRay(Vector2 position) => default;
        public static float GetHandleSize(Vector3 position) => 1;
        public static void AddDefaultControl(int controlId) { }
    }

    public enum MessageType { None, Info, Warning, Error }

    public static class EditorGUI
    {
        public static void BeginChangeCheck() { }
        public static bool EndChangeCheck() => false;
        public static void DrawRect(Rect rect, Color color) { }
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
        public static int Popup(int selectedIndex, string[] displayedOptions, params GUILayoutOption[] options) => selectedIndex;
        public static int DelayedIntField(string label, int value, params GUILayoutOption[] options) => value;
        public static int DelayedIntField(int value, params GUILayoutOption[] options) => value;
        public static string DelayedTextField(string label, string text, params GUILayoutOption[] options) => text;
        public static double DelayedDoubleField(string label, double value, params GUILayoutOption[] options) => value;
        public static float Slider(string label, float value, float leftValue, float rightValue, params GUILayoutOption[] options) => value;
        public static bool Toggle(string label, bool value, params GUILayoutOption[] options) => value;
        public static System.Enum EnumPopup(string label, System.Enum selected, params GUILayoutOption[] options) => selected;
        public static bool Foldout(bool foldout, string content, bool toggleOnLabelClick) => foldout;
        public static void LabelField(string label, params GUILayoutOption[] options) { }
        public static void LabelField(string label, GUIStyle style, params GUILayoutOption[] options) { }
        public static void LabelField(string label, string label2, params GUILayoutOption[] options) { }
        public static void PrefixLabel(string label) { }
        public static Color ColorField(GUIContent label, Color value, bool showEyedropper, bool showAlpha, bool hdr, params GUILayoutOption[] options) => value;
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

namespace UnityEditor.EditorTools
{
    public sealed class EditorToolAttribute : System.Attribute
    {
        public EditorToolAttribute(string displayName, System.Type componentToolTarget, System.Type editorToolContext) { }
    }

    public sealed class EditorToolContextAttribute : System.Attribute
    {
        public EditorToolContextAttribute(string displayName, System.Type targetType) { }
    }

    public abstract class EditorToolContext : ScriptableObject
    {
        public virtual void OnActivated() { }
        protected virtual System.Type GetEditorToolType(Tool tool) => null;
    }

    public sealed class GameObjectToolContext : EditorToolContext { }

    public abstract class EditorTool : ScriptableObject
    {
        public Object target => null;
        public virtual GUIContent toolbarIcon => GUIContent.none;
        public virtual void OnToolGUI(EditorWindow window) { }
        public virtual void OnWillBeDeactivated() { }
    }

    public static class ToolManager
    {
        public static System.Type activeToolType => typeof(object);
        public static void SetActiveTool<T>() where T : EditorTool { }
        public static System.Type activeContextType => typeof(object);
        public static void SetActiveContext<T>() where T : EditorToolContext { }
        public static void RestorePreviousPersistentTool() { }
    }
}

namespace UnityEditor.Overlays
{
    public sealed class OverlayAttribute : System.Attribute
    {
        public OverlayAttribute(System.Type editorWindowType, string displayName, bool defaultDisplay) { }
    }

    public abstract class IMGUIOverlay
    {
        public abstract void OnGUI();
    }
}

namespace UnityEditorInternal
{
    public static class InternalEditorUtility
    {
        public static void RepaintAllViews() { }
    }
}
