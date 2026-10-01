// Hand-written declarations of the engine members the engine-facing assemblies use.
//
// WHAT THIS PROVES: that every type, member and overload named in Unity/*.cs, Editor/*.cs
// and the play kit exists with the signature written here, and that the code around them
// compiles. Typos, renamed members, wrong argument order and wrong return types fail the
// build.
//
// WHAT IT DOES NOT PROVE: anything about behaviour. Every body here is empty. Nothing
// about Unity's semantics is tested by compiling against it, and cannot be without an
// editor.
//
// Signatures are transcribed from Unity's published API. Where one is wrong, this project
// will happily compile code that Unity rejects, which is the failure mode to keep in mind
// and the reason the declarations are kept to exactly what is used rather than filled out
// from memory. A stub for something unused is a claim about an API nobody checked.

namespace UnityEngine
{
    public class Object
    {
        public string name { get; set; } = "";
        public HideFlags hideFlags { get; set; }
        public static void Destroy(Object obj) { }
        public static void DestroyImmediate(Object obj) { }
    }

    [System.Flags]
    public enum HideFlags { None = 0, HideInHierarchy = 1, HideInInspector = 2, DontSaveInEditor = 4, NotEditable = 8, DontSaveInBuild = 16, DontUnloadUnusedAsset = 32, DontSave = 52, HideAndDontSave = 61 }

    public sealed class ExecuteAlways : System.Attribute { }


    public static class Application
    {
        public static bool isPlaying => false;
    }

    public class ScriptableObject : Object
    {
        public static T CreateInstance<T>() where T : ScriptableObject, new() => new T();
    }

    public static class Debug
    {
        public static void Log(object message) { }
        public static void LogWarning(object message) { }
        public static void LogError(object message) { }
    }

    public class PropertyAttribute : System.Attribute
    {
    }

    public sealed class HeaderAttribute : PropertyAttribute
    {
        public HeaderAttribute(string header) { }
    }

    public sealed class ContextMenu : System.Attribute
    {
        public ContextMenu(string itemName) { }
    }

    public sealed class TooltipAttribute : PropertyAttribute
    {
        public TooltipAttribute(string tooltip) { }
    }

    public sealed class MinAttribute : PropertyAttribute
    {
        public MinAttribute(float min) { }
    }

    public sealed class RangeAttribute : PropertyAttribute
    {
        public RangeAttribute(float min, float max) { }
    }

    public sealed class TextAreaAttribute : PropertyAttribute
    {
        public TextAreaAttribute() { }
        public TextAreaAttribute(int minLines, int maxLines) { }
    }

    public sealed class SerializeField : System.Attribute
    {
    }

    public sealed class CreateAssetMenuAttribute : System.Attribute
    {
        public string menuName { get; set; } = "";
        public string fileName { get; set; } = "";
        public int order { get; set; }
    }
}

namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => default;
        public float sqrMagnitude => 0;
        public Vector2 normalized => this;
        public static implicit operator Vector4(Vector2 v) => new Vector4(v.x, v.y, 0, 0);
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => default;
        public static Vector3 one => default;
        public static Vector3 up => default;
        public static Vector3 right => default;
        public static Vector3 forward => default;
        public static float Dot(Vector3 a, Vector3 b) => 0;
        public static Vector3 operator +(Vector3 a, Vector3 b) => a;
        public static Vector3 operator -(Vector3 a, Vector3 b) => a;
        public static Vector3 operator *(Vector3 a, float d) => a;
        public static bool operator ==(Vector3 a, Vector3 b) => true;
        public static bool operator !=(Vector3 a, Vector3 b) => false;
        public static implicit operator Vector4(Vector3 v) => new Vector4(v.x, v.y, v.z, 0);
    }

    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => default;
    }

    public struct Quaternion
    {
        public static Quaternion identity => default;
        public static Quaternion LookRotation(Vector3 forward, Vector3 upwards) => default;
        public static bool operator ==(Quaternion a, Quaternion b) => true;
        public static bool operator !=(Quaternion a, Quaternion b) => false;
    }

    public struct Matrix4x4
    {
        public static Matrix4x4 identity => default;
        public static Matrix4x4 TRS(Vector3 pos, Quaternion q, Vector3 s) => default;
        public static Matrix4x4 operator *(Matrix4x4 a, Matrix4x4 b) => a;
        public Vector3 MultiplyPoint(Vector3 point) => point;
        public Vector3 MultiplyVector(Vector3 vector) => vector;
    }

    public struct Ray
    {
        public Vector3 origin => default;
        public Vector3 direction => default;
        public Vector3 GetPoint(float distance) => default;
    }

    public struct Rect
    {
        public Rect(float x, float y, float width, float height) { this.x = x; this.yMax = y + height; this.width = width; }
        public float x, yMax, width;
    }

    public class Camera : Behaviour { }

    public sealed class GUIContent
    {
        public GUIContent(string text) { }
        public GUIContent(string text, string tooltip) { }
        public static GUIContent none => new GUIContent("");
    }

    public class GUIStyle
    {
        public static GUIStyle none => new GUIStyle();
    }

    public sealed class GUILayoutOption { }

    public static class GUILayout
    {
        public static bool Button(string text, params GUILayoutOption[] options) => false;
        public static bool Button(string text, GUIStyle style, params GUILayoutOption[] options) => false;
        public static bool Button(GUIContent content, params GUILayoutOption[] options) => false;
        public static bool Button(GUIContent content, GUIStyle style, params GUILayoutOption[] options) => false;
        public static bool Toggle(bool value, string text, GUIStyle style, params GUILayoutOption[] options) => value;
        public static bool Toggle(bool value, GUIContent content, GUIStyle style, params GUILayoutOption[] options) => value;
        public static int Toolbar(int selected, string[] texts, params GUILayoutOption[] options) => selected;
        public static void Label(string text, params GUILayoutOption[] options) { }
        public static void Label(string text, GUIStyle style, params GUILayoutOption[] options) { }
        public static void Space(float pixels) { }
        public static void FlexibleSpace() { }
        public static GUILayoutOption Width(float width) => new GUILayoutOption();
        public static GUILayoutOption Height(float height) => new GUILayoutOption();

        public sealed class HorizontalScope : System.IDisposable
        {
            public HorizontalScope(params GUILayoutOption[] options) { }
            public void Dispose() { }
        }
    }

    public static class GUI
    {
        public static void Label(Rect position, string text, GUIStyle style) { }
        public static bool Button(Rect position, GUIContent content, GUIStyle style) => false;
    }

    public static class GUILayoutUtility
    {
        public static Rect GetRect(float width, float height, params GUILayoutOption[] options) => default;
    }

    public enum FocusType { Keyboard = 0, Passive = 2 }

    public static class GUIUtility
    {
        public static int hotControl { get; set; }
        public static int GetControlID(FocusType focus) => 0;
        public static int GetControlID(int hint, FocusType focus) => 0;
        public static Vector2 GUIToScreenPoint(Vector2 guiPoint) => guiPoint;
    }

    public enum EventType { MouseDown = 0, MouseUp = 1, MouseMove = 2, MouseDrag = 3, KeyDown = 4, Repaint = 7, Layout = 8, Used = 12 }

    public enum KeyCode { Backspace = 8, Escape = 27, LeftBracket = 91, RightBracket = 93, D = 100, I = 105, L = 108, R = 114, S = 115, V = 118, W = 119, X = 120, Delete = 127 }

    public sealed class Event
    {
        public static Event current => new Event();
        public EventType type => default;
        public EventType rawType => default;
        public int button => 0;
        public int clickCount => 0;
        public bool alt => false;
        public bool control => false;
        public bool command => false;
        public KeyCode keyCode => default;
        public Vector2 mousePosition => default;
        public void Use() { }
    }

    public struct Bounds
    {
        public Bounds(Vector3 center, Vector3 size) { }
        public void Encapsulate(Vector3 point) { }
    }

    public static class Shader
    {
        public static int PropertyToID(string name) => 0;
        public static void SetGlobalBuffer(int nameID, GraphicsBuffer value) { }
        public static void SetGlobalVector(int nameID, Vector4 value) { }
        public static void SetGlobalFloat(int nameID, float value) { }
        public static void SetGlobalInt(int nameID, int value) { }
        public static int GetGlobalInt(int nameID) => 0;
        public static void SetGlobalTexture(int nameID, Texture value) { }
    }

    public enum TextureFormat { RGBAHalf = 17 }

    public static class ColorUtility
    {
        public static bool TryParseHtmlString(string htmlString, out Color color) { color = default; return false; }
        public static string ToHtmlStringRGB(Color color) => "";
    }

    public static class Resources
    {
        public static T Load<T>(string path) where T : Object => null;
    }

    public class Texture : Object
    {
    }

    public sealed class Texture2D : Texture
    {
        public Texture2D(int width, int height, TextureFormat textureFormat, bool mipChain) { }
        public void SetPixels(Color[] colors) { }
        public void Apply(bool updateMipmaps = true, bool makeNoLongerReadable = false) { }
    }

    public sealed class GraphicsBuffer : System.IDisposable
    {
        [System.Flags] public enum Target { Vertex = 1, Index = 2, Structured = 16, Raw = 32 }
        public GraphicsBuffer(Target target, int count, int stride) { }
        public void SetData(System.Array data) { }
        public void Dispose() { }
    }

    public sealed class Mesh : Object
    {
        public UnityEngine.Rendering.IndexFormat indexFormat { get; set; }
        public Bounds bounds { get; set; }
        public int subMeshCount { get; set; }
        public void SetVertexBufferParams(int vertexCount, params UnityEngine.Rendering.VertexAttributeDescriptor[] attributes) { }
        public void SetVertexBufferData<T>(T[] data, int dataStart, int meshBufferStart, int count, int stream = 0, UnityEngine.Rendering.MeshUpdateFlags flags = UnityEngine.Rendering.MeshUpdateFlags.Default) where T : struct { }
        public void SetIndexBufferParams(int indexCount, UnityEngine.Rendering.IndexFormat format) { }
        public void SetIndexBufferData<T>(T[] data, int dataStart, int meshBufferStart, int count, UnityEngine.Rendering.MeshUpdateFlags flags = UnityEngine.Rendering.MeshUpdateFlags.Default) where T : struct { }
        public void SetSubMesh(int index, UnityEngine.Rendering.SubMeshDescriptor desc, UnityEngine.Rendering.MeshUpdateFlags flags = UnityEngine.Rendering.MeshUpdateFlags.Default) { }
        public void UploadMeshData(bool markNoLongerReadable) { }
    }
}

namespace UnityEngine.Rendering
{
    public enum IndexFormat { UInt16 = 0, UInt32 = 1 }
    public enum VertexAttribute { Position, Normal, Tangent, Color, TexCoord0, TexCoord1, TexCoord2, TexCoord3, TexCoord4, TexCoord5, TexCoord6, TexCoord7, BlendWeight, BlendIndices }
    public enum VertexAttributeFormat { Float32, Float16, UNorm8, SNorm8, UNorm16, SNorm16, UInt8, SInt8, UInt16, SInt16, UInt32, SInt32 }

    [System.Flags]
    public enum MeshUpdateFlags { Default = 0, DontValidateIndices = 1, DontResetBoneBounds = 2, DontNotifyMeshUsers = 4, DontRecalculateBounds = 8 }

    public struct VertexAttributeDescriptor
    {
        public VertexAttributeDescriptor(VertexAttribute attribute = VertexAttribute.Position, VertexAttributeFormat format = VertexAttributeFormat.Float32, int dimension = 3, int stream = 0) { }
    }

    public struct SubMeshDescriptor
    {
        public SubMeshDescriptor(int indexStart, int indexCount, MeshTopology topology = MeshTopology.Triangles) { }
    }

    public enum MeshTopology { Triangles = 0, Quads = 2, Lines = 3, LineStrip = 4, Points = 5 }
}

namespace UnityEngine
{
    public sealed class AddComponentMenuAttribute : System.Attribute { public AddComponentMenuAttribute(string menuName) { } }

    public class Component : Object
    {
        public Transform transform { get; } = new Transform();
        public GameObject gameObject { get; } = new GameObject("");
    }

    public class Behaviour : Component { }

    public class MonoBehaviour : Behaviour { }

    public sealed class Transform : Component
    {
        public Vector3 position => default;
        public Quaternion rotation => default;
        public Vector3 lossyScale => default;
        public Matrix4x4 localToWorldMatrix => default;
        public Matrix4x4 worldToLocalMatrix => default;
        public void SetParent(Transform parent, bool worldPositionStays) { }
    }

    public sealed class GameObject : Object
    {
        public GameObject(string name) { }
        public Transform transform { get; } = new Transform();
        public T AddComponent<T>() where T : Component, new() => new T();
        public T GetComponent<T>() => default!;
    }

    public sealed class MeshFilter : Component
    {
        public Mesh sharedMesh { get; set; } = new Mesh();
    }

    public sealed class Material : Object { }

    public sealed class Renderer : Component { }

    public sealed class MeshRenderer : Component
    {
        public Material sharedMaterial { get; set; } = new Material();
        public UnityEngine.Rendering.ShadowCastingMode shadowCastingMode { get; set; }
        public UnityEngine.Rendering.LightProbeUsage lightProbeUsage { get; set; }
    }
}

namespace UnityEngine.Rendering
{
    public enum ShadowCastingMode { Off, On, TwoSided, ShadowsOnly }
    public enum LightProbeUsage { Off = 0, BlendProbes = 1, UseProxyVolume = 2, CustomProvided = 4 }
}

namespace UnityEngine.Scripting
{
    public class PreserveAttribute : System.Attribute
    {
    }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene { }
}
