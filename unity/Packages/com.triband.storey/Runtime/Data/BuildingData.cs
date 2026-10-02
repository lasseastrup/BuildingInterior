#nullable enable
using System.Collections.Generic;

namespace Triband.Storey
{
    /// <summary>A point in the building's local XZ plane (metres, y up).</summary>
    public struct Vec2
    {
        public double x, z;
        public Vec2(double x, double z) { this.x = x; this.z = z; }
        public override string ToString() => $"({x}, {z})";
    }

    /// <summary>
    /// One saved layout: what the prototype keeps under <c>storey-builder-v9</c> and what
    /// <c>.storey</c> files hold. Field names follow the prototype's JSON (SPEC §3), because
    /// that JSON is the interchange format and a round trip must not rename anything.
    /// </summary>
    public sealed class StoreyDocument
    {
        /// <summary>Document layout version (the prototype's <c>v</c>; 2 today).</summary>
        public int v = 2;
        /// <summary>Id counter the prototype uses for new buildings.</summary>
        public int seq;
        public List<BuildingData> buildings = new List<BuildingData>();
        public Vec2 spawn;
        /// <summary>
        /// The palette block (docs/COLOURS.md §3.1): for every palette id the styles use, its name and colour when
        /// the file was saved, so the prototype and headless tools can show it. A snapshot, never the source of truth.
        /// </summary>
        public SortedDictionary<string, PaletteEntry> palette = new SortedDictionary<string, PaletteEntry>(System.StringComparer.Ordinal);
    }

    /// <summary>One entry of the palette block: a Color Pipeline colour's name and CSS colour.</summary>
    public sealed class PaletteEntry
    {
        public string name = "";
        public string hex = "";
        public PaletteEntry() { }
        public PaletteEntry(string name, string hex) { this.name = name; this.hex = hex; }
    }

    public sealed class BuildingData
    {
        public string id = "";
        public string name = "";
        /// <summary>World position of the local origin; every other coordinate is building-local.</summary>
        public Vec2 pos;
        /// <summary>Base outline: any simple polygon, either winding.</summary>
        public List<Vec2> footprint = new List<Vec2>();
        /// <summary>Index 0 = ground; the roof level is <c>floors.Count</c>.</summary>
        public List<FloorData> floors = new List<FloorData>();
        public List<CoreData> shafts = new List<CoreData>();
        public List<EntranceData> entrances = new List<EntranceData>();
        /// <summary>Hand-placed facade details; style rules add more at generation time (SPEC §4.4).</summary>
        public List<DetailData> details = new List<DetailData>();
        /// <summary>Blank (windowless) edges of the base outline.</summary>
        public List<int> blank = new List<int>();
        public double groundHeight = 3.6;
        public double floorHeight = 3.0;
        /// <summary>False = shell only: facade and roof, no slabs, rooms or cores.</summary>
        public bool interior = true;
        public FacadeStyle style = new FacadeStyle();
        /// <summary>True for buildings the prototype's city generator made; they are not part of the layout proper.</summary>
        public bool gen;
    }

    public sealed class FloorData
    {
        public List<WallData> walls = new List<WallData>();
        /// <summary>Storey height override; null = use the building's ground/floor height.</summary>
        public double? h;
        /// <summary>Setback outline starting a tier at this floor; empty = none.</summary>
        public List<Vec2> shape = new List<Vec2>();
        /// <summary>Blank edges of <see cref="shape"/>. Only meaningful with a shape.</summary>
        public List<int> blank = new List<int>();
        /// <summary>The setback's own exterior style; null = inherit the tier below.</summary>
        public FacadeStyle? style;
        /// <summary>Roof over the uncovered part of the tier below instead of a terrace; null = terrace.</summary>
        public TerraceRoofData? terraceRoof;

        /// <summary>
        /// A filled storey: nothing inside, as a shell-only building's storeys (opaque windows, closed doors, no rooms
        /// or stairs). Lifts pass through it without stopping. Storey's own; walk-in buildings only.
        /// </summary>
        public bool filled;

        public bool HasShape => shape.Count > 0;
        public bool HasHeight => h.HasValue;
    }

    public sealed class WallData
    {
        public Vec2 a, b;
        public List<DoorData> doors = new List<DoorData>();
    }

    public sealed class DoorData
    {
        /// <summary>Position along the wall, 0..1 from a to b.</summary>
        public double t;
    }

    /// <summary>
    /// Stairs: a switchback stair that serves every floor in its range. Lift. Flight: straight flights stacked one above
    /// the other over the same floor range, with no walls: up the flight from the front, off at the back, and back along
    /// the walkway beside it to the next flight.
    /// </summary>
    public enum CoreType { Stairs, Lift, Flight }

    public sealed class CoreData
    {
        public string id = "";
        public CoreType type;
        public double x, z;
        /// <summary>Rotation in degrees about y.</summary>
        public double rot;
        public int bottom;
        /// <summary>Top floor served; -1 = follow the top floor.</summary>
        public int top = -1;
        /// <summary>Continues to the roof.</summary>
        public bool roof;
    }

    public sealed class EntranceData
    {
        /// <summary>Edge index of the outline at floor <see cref="k"/>.</summary>
        public int edge;
        /// <summary>Position along the edge, 0..1.</summary>
        public double t;
        /// <summary>Floor: 0 = street door; above, a door onto that setback's terrace or a neighbour's roof.</summary>
        public int k;
    }

    public enum DetailKind { Ac, Vent, Dish, Escape, Awning }

    public sealed class DetailData
    {
        public DetailKind kind;
        public int k;
        public int edge;
        public double t;
        /// <summary>Height above the floor for wall-anchored kinds; null for kinds that do not use it.</summary>
        public double? y;
    }

    public sealed class TerraceRoofData
    {
        /// <summary>Roof pitch in degrees.</summary>
        public double pitch = 30;
    }

    public enum WindowType { Punched, Tall, Ribbon, Curtain, None }
    public enum GroundType { Storefront, Match, Solid }
    public enum RoofType { Flat, Hip, Gable, Shed }

    /// <summary>
    /// Exterior style. Colours are colour references (<see cref="ColorRef"/>): CSS hex as the prototype
    /// stores them, or Color Pipeline palette ids.
    /// </summary>
    public sealed class FacadeStyle
    {
        /// <summary>Preset key the style was taken from; null when edited freely.</summary>
        public string? preset;
        public string label = "";
        public string wall = "#9A4B38", trim = "#ECE5D8", interior = "#EFECE5", floor = "#B88D62", roof = "#6E716B", core = "#C9C4BB", glass = "#8DB3C8";
        /// <summary>
        /// The colours the prototype fixed for every building, now per style (docs/COLOURS.md §3.1); null = the
        /// project default (<c>Generate.StyleColors</c>). Door and the facade-detail colours follow the tier's style;
        /// the indoor ones (rail, metal, ceiling, lift interior and button) come from the building's, as interior does.
        /// </summary>
        public string? door, rail, metal, ceiling, liftInterior, liftButton, detailMetal, grille, detailDark, dish;
        public WindowType windows = WindowType.Punched;
        public double winW = 1.2;
        public double bay = 2.6;
        public GroundType ground = GroundType.Storefront;
        public bool bands = true;
        public bool parapet = true;
        /// <summary>The top tier's roof type; the prototype omits the key for Flat.</summary>
        public RoofType roofType = RoofType.Flat;
        /// <summary>Pitch in degrees; null = not stored (the generator then uses 30°, or 15° for a shed).</summary>
        public double? pitch;
        /// <summary>Eave overhang in metres; null = not stored (the generator then uses 0.35 m). Zero is a real value: no overhang.</summary>
        public double? eave;

        /// <summary>Densities for details placed by rule, as the prototype stores them (percentages); null = the defaults.</summary>
        public DetailRules? details;

        public FacadeStyle Clone() { var c = (FacadeStyle)MemberwiseClone(); c.details = details?.Clone(); return c; }
    }

    /// <summary>
    /// What a style colour field holds (docs/COLOURS.md §3.1): a CSS literal <c>#RRGGBB</c> (or <c>#RGB</c>),
    /// as the prototype writes it, or a palette id: a Color Pipeline <c>SerializableGUID</c>'s two halves
    /// (<c>m_Value0</c>, <c>m_Value1</c>, as the .palette file stores them) as 16 hex digits each. Storey's own
    /// format, so it never depends on how <c>Hash128</c> prints itself.
    /// </summary>
    public static class ColorRef
    {
        public static bool IsHex(string? s) => s != null && (s.Length == 7 || s.Length == 4) && s[0] == '#' && AllHex(s, 1);
        public static bool IsPaletteId(string? s) => s != null && s.Length == 32 && AllHex(s, 0);

        /// <summary>The palette id of a <c>SerializableGUID</c> given as its two halves.</summary>
        public static string PaletteId(ulong value0, ulong value1) => value0.ToString("x16") + value1.ToString("x16");

        /// <summary>A palette id's two halves, for <c>new SerializableGUID(ulong, ulong)</c>.</summary>
        public static (ulong value0, ulong value1) Parts(string id)
        {
            if (!IsPaletteId(id)) throw new System.FormatException($"\"{id}\" is not a palette id");
            return (ulong.Parse(id.Substring(0, 16), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture),
                    ulong.Parse(id.Substring(16), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture));
        }

        /// <summary>A CSS literal as <c>#RRGGBB</c> in capitals: <c>#abc</c> and <c>#AABBCC</c> are one colour.</summary>
        public static string Normalise(string hex)
        {
            if (!IsHex(hex)) throw new System.FormatException($"\"{hex}\" is not a CSS colour");
            string h = hex.Substring(1).ToUpperInvariant();
            if (h.Length == 3) h = new string(new[] { h[0], h[0], h[1], h[1], h[2], h[2] });
            return "#" + h;
        }

        static bool AllHex(string s, int from)
        {
            for (int i = from; i < s.Length; i++) if (!System.Uri.IsHexDigit(s[i])) return false;
            return true;
        }
    }

    /// <summary>How often the style's rules place details, in percent (SPEC §4.4).</summary>
    public sealed class DetailRules
    {
        /// <summary>Chance of an AC unit under an upper-floor window; the prototype's default is 30.</summary>
        public double? ac;
        /// <summary>Chance of a vent in a window-free stretch of wall; the prototype's default is 12.</summary>
        public double? vents;
        public DetailRules Clone() => (DetailRules)MemberwiseClone();
    }
}
