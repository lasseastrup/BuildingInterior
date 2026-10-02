// Hand-written declarations of the Color Pipeline 2.1.11 and Triband Core members Storey's bridge
// (Packages/com.triband.storey/ColorPipeline) and the editor's picker (com.triband.storey.authoring/Editor/ColorPipeline) use, transcribed from Bitbucket triband/colorpipeline
// (master, 2.1.11) and triband/core. Same rules as UnityEngine.cs: exactly what is used, bodies empty.

namespace Triband.Core.Utils
{
    public struct SerializableGUID
    {
        public SerializableGUID(ulong a, ulong b) { }
        public bool valid => false;
        public (ulong, ulong) ToParts() => (0, 0);
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
        public string Name => "";
        public UnityEngine.Color Color => default;
        public SerializableGUID ID => default;
    }

    public class ColorPaletteDefinition
    {
        public static ColorPaletteDefinition Instance => null;
        public System.Collections.Generic.List<ColorDefinition> Colors => null;
        public bool TryGetColor(SerializableGUID guid, out ColorDefinition colorDefinition) { colorDefinition = null; return false; }
        public int GetIndexOfColor(SerializableGUID id) => 0;
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

namespace Triband.ColorPipeline.Editor
{
    using Triband.Core.Utils;

    public class PaletteColorPickerWindow : UnityEditor.EditorWindow
    {
        public delegate void WindowClosedDelegate();
        public delegate void ColorSelectedDelegate(SerializableGUID colorId);
        public delegate void ColorPreviewedDelegate(SerializableGUID colorId);

        public static void Open(UnityEngine.Vector2 position, int selectedIndex, WindowClosedDelegate windowClosedCallback = null,
            ColorSelectedDelegate colorSelectedCallback = null, ColorPreviewedDelegate colorPreviewedCallback = null) { }
    }
}
