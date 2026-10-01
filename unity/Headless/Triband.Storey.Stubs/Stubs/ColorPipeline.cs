// Hand-written declarations of the Color Pipeline 2.1.11 and Triband Core members Storey's bridge
// (Packages/com.triband.storey/ColorPipeline) uses, transcribed from Bitbucket triband/colorpipeline
// (master, 2.1.11) and triband/core. Same rules as UnityEngine.cs: exactly what is used, bodies empty.

namespace Triband.Core.Utils
{
    public struct SerializableGUID
    {
        public SerializableGUID(string hashString) { }
        public SerializableGUID(ulong a, ulong b) { }
        public bool valid => false;
        public override string ToString() => "";
    }
}

namespace Triband.ColorPipeline.Runtime
{
    using Triband.Core.Utils;

    public class ColorReferenceAttribute : UnityEngine.PropertyAttribute
    {
    }

    public class ColorDefinition
    {
    }

    public class ColorPaletteDefinition
    {
        public static ColorPaletteDefinition Instance => null;
        public bool TryGetColor(SerializableGUID guid, out ColorDefinition colorDefinition) { colorDefinition = null; return false; }
        public int GetIndexOfColor(SerializableGUID id) => 0;
        public SerializableGUID GetIDOfClosestColor(UnityEngine.Color originalColor) => default;
    }

    public struct ColorRemapDescriptor
    {
        public ColorRemapDescriptor(SerializableGUID[] originalColors, SerializableGUID[] overwriteColors) { }
    }

    public static class ColorMappingManager
    {
        public static event System.Action OnMappingsInvalidated;
        public static void SetupColorRemap(ColorRemapDescriptor colorRemap, out int colorPaletteOffset) { colorPaletteOffset = 0; }
    }
}
