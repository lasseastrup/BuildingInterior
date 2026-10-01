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
        public static void Destroy(Object obj) { }
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
        public static implicit operator Vector4(Vector2 v) => new Vector4(v.x, v.y, 0, 0);
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => default;
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
        public static Color magenta => default;
        public static Color clear => default;
    }

    public struct Rect
    {
        public Rect(Vector2 position, Vector2 size) { }
    }

    public sealed class GUIContent
    {
        public GUIContent(string text) { }
        public static GUIContent none => new GUIContent("");
    }

    public class GUIStyle { }

    public sealed class GUILayoutOption { }

    public static class GUILayout
    {
        public static bool Button(string text, params GUILayoutOption[] options) => false;
        public static GUILayoutOption Width(float width) => new GUILayoutOption();
        public static void FlexibleSpace() { }
    }

    public static class GUIUtility
    {
        public static Vector2 GUIToScreenPoint(Vector2 guiPoint) => guiPoint;
    }

    public sealed class Event
    {
        public static Event current => new Event();
        public Vector2 mousePosition => default;
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
        public void SetParent(Transform parent, bool worldPositionStays) { }
    }

    public sealed class GameObject : Object
    {
        public GameObject(string name) { }
        public Transform transform { get; } = new Transform();
        public T AddComponent<T>() where T : Component, new() => new T();
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
