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
    }

    public class ScriptableObject : Object
    {
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
