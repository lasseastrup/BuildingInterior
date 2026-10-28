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
        /// <summary>
        /// The height of the ground floor's slab in the site (metres; 0 = the site's ground). Every floor, the roof and the
        /// interior stand on it: <see cref="Derived.FloorBase"/> is in site heights. Storey's own.
        /// </summary>
        public double elevation;
        /// <summary>Base outline: any simple polygon, either winding.</summary>
        public List<Vec2> footprint = new List<Vec2>();
        /// <summary>The cut corners of <see cref="footprint"/> (<see cref="CornerData"/>). Storey's own.</summary>
        public List<CornerData> corners = new List<CornerData>();
        /// <summary>Index 0 = ground; the roof level is <c>floors.Count</c>.</summary>
        public List<FloorData> floors = new List<FloorData>();
        public List<CoreData> shafts = new List<CoreData>();
        public List<EntranceData> entrances = new List<EntranceData>();
        /// <summary>Hand-placed facade details; style rules add more at generation time (SPEC §4.4).</summary>
        public List<DetailData> details = new List<DetailData>();
        /// <summary>Blank (windowless) edges of the base outline.</summary>
        public List<int> blank = new List<int>();
        /// <summary>
        /// Edges of the base outline that are also quiet (no details by rule) or bare (no details by rule, no floor bands and
        /// no plinth either): <see cref="EdgeState"/>. Each is in <see cref="blank"/> too, so the prototype shows it windowless.
        /// Null for none: most buildings have none, and an empty list each was memory every layout load kept. Storey's own.
        /// </summary>
        public List<int>? quiet, bare;
        public double groundHeight = 3.6;
        public double floorHeight = 3.0;
        /// <summary>False = shell only: facade and roof, no slabs, rooms or cores.</summary>
        public bool interior = true;
        public FacadeStyle style = new FacadeStyle();
        /// <summary>True for buildings the prototype's city generator made; they are not part of the layout proper.</summary>
        public bool gen;
        /// <summary>
        /// False: the site makes no collider for this building (docs/PLAY.md §5), with its LOD0, at load or in edit mode
        /// alike, and whatever waits for colliders takes it as ready. For the old BuildingGenerator's buildings with
        /// <c>_generateColliders</c> off. The walk model still walks it. Storey's own.
        /// </summary>
        public bool colliders = true;
        /// <summary>Courtyards and atria: openings through the storeys from <see cref="VoidData.bottom"/> up. Storey's own.</summary>
        public List<VoidData> voids = new List<VoidData>();
        /// <summary>Bridges from this building's walls to other buildings'. Storey's own.</summary>
        public List<BridgeData> bridges = new List<BridgeData>();
    }

    /// <summary>
    /// A bridge: from a point on this building's wall on storey <see cref="k"/>, straight out to building <see cref="to"/>'s
    /// wall facing it, on that building's storey <see cref="toK"/>, with a door at each end. Storey's own.
    /// </summary>
    public sealed class BridgeData
    {
        public string id = "";
        /// <summary>The building it goes to (its id).</summary>
        public string to = "";
        /// <summary>The storey it leaves from, and where on that storey's outline (building-local; the nearest wall within 1 m).</summary>
        public int k;
        public Vec2 at;
        /// <summary>The storey of the building it goes to that it arrives on.</summary>
        public int toK;
        /// <summary>Width in metres, rail to rail.</summary>
        public double width = 2.4;
        /// <summary>Open: a deck with rails. Otherwise enclosed: glass sides and a roof.</summary>
        public bool open;
    }

    /// <summary>
    /// A courtyard is open to the sky, with facades facing into it and a door onto it on its bottom storey. An atrium is
    /// inside: the floors above its bottom storey are open round it behind rails, under a skylight on a flat roof.
    /// </summary>
    public enum VoidKind { Courtyard, Atrium }

    /// <summary>
    /// An opening through the building (building-local outline, any simple polygon): from the floor of storey
    /// <see cref="bottom"/> up through the roof. Storey's own.
    /// </summary>
    public sealed class VoidData
    {
        public string id = "";
        public VoidKind kind;
        public List<Vec2> shape = new List<Vec2>();
        /// <summary>The storey whose floor is the void's floor: a courtyard's paving, an atrium's bottom floor.</summary>
        public int bottom;
    }

    /// <summary>How a corner is cut: a straight chamfer, or a rounded arc.</summary>
    public enum CornerShape { Chamfer, Round }

    /// <summary>
    /// A cut corner of an outline (docs/EDITOR.md §6.1). The outline holds the cut's points (<see cref="pts"/>, in order,
    /// as they stand in it); this keeps the sharp corner they replace and how it was cut, so the editor shows the corner
    /// as one point and can change or clear the cut in one step. A cut whose points are no longer in the outline, in
    /// order, is dropped. Storey's own.
    /// </summary>
    public sealed class CornerData
    {
        /// <summary>The sharp corner the cut replaces.</summary>
        public Vec2 at;
        public CornerShape shape;
        /// <summary>The chamfer's size along each edge, or the round's radius, in metres.</summary>
        public double size = 2;
        /// <summary>A street door in the middle of the chamfer (the base outline only).</summary>
        public bool door;
        /// <summary>The points in the outline the cut puts there, in the outline's order.</summary>
        public List<Vec2> pts = new List<Vec2>();
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
        /// <summary>Quiet and bare edges of <see cref="shape"/>, as the base's (<see cref="BuildingData.quiet"/>). Storey's own.</summary>
        public List<int>? quiet, bare;
        /// <summary>The cut corners of <see cref="shape"/>. Storey's own.</summary>
        public List<CornerData> corners = new List<CornerData>();
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

    public sealed class DetailData
    {
        /// <summary>
        /// Its kind's id (<see cref="Generate.DetailKinds"/>): the generator's own "ac", "vent", "dish", "escape" and
        /// "awning", as the prototype writes them, or an artist's Facade Detail asset's.
        /// </summary>
        public string kind = Generate.DetailKinds.Ac;
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

    /// <summary>
    /// What a wall of an outline has (docs/EDITOR.md §6.7): windows (Open); none (Blank: doors placed by hand stay, as do its
    /// strips and details); none and no details by rule either (Quiet); or none, no details by rule and no floor bands or
    /// plinth (Bare: a plain wall, its foundation and parapet still on). Not stored as such: the outline's blank, quiet and
    /// bare lists. Storey's own, for the old BuildingGenerator's suppressed edges, its sides left without details, and its blank edges.
    /// </summary>
    public enum EdgeState { Open, Blank, Quiet, Bare }
    /// <summary>What tops a window or door: a flat head, or an arched hood over it. Storey's own.</summary>
    public enum HeadType { Flat, Arch }
    public enum GroundType { Storefront, Match, Solid }
    /// <summary>The top tier's roof. Mansard is Storey's own: a steep lower slope up to a break, then a shallow hip.</summary>
    public enum RoofType { Flat, Hip, Gable, Shed, Mansard }

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
        /// <summary>Frames and glazing bars, and the foundation (Storey's own optional colours); null = the project default.</summary>
        public string? frame, foundationColor;
        /// <summary>The plinth's own colour; null = a shade of the wall, as the prototype has it.</summary>
        public string? plinth;
        /// <summary>
        /// The ground storey's wall colour; null = the walls' (docs/EDITOR.md §6.7). Its plinth, band shade and brick patches
        /// follow it. Storey's own (the old BuildingGenerator's ground-floor wall colour).
        /// </summary>
        public string? groundWall;
        /// <summary>The floor bands' own colour; null = the trim, or a shade of the wall with <see cref="bandWall"/>. Storey's own.</summary>
        public string? band;
        /// <summary>The cornice's own colour (<see cref="corniceH"/>); null = the trim. Storey's own.</summary>
        public string? cornice;
        /// <summary>The roof cap's colour (<see cref="capH"/>): its sides and strip; null = the trim. Storey's own.</summary>
        public string? cap;
        /// <summary>The underside of the roof cap's overhang; null = the cap's colour, shaded. Storey's own.</summary>
        public string? capRim;
        public WindowType windows = WindowType.Punched;
        public double winW = 1.2;
        public double bay = 2.6;
        /// <summary>
        /// How a wall's bays are laid out (docs/EDITOR.md §6.7): spread to fill the wall between its corners, or each exactly
        /// <see cref="bay"/> wide, as many as fit on the wall's outer face and centred on it, as the old BuildingGenerator lays
        /// out its tiles. Storey's own.
        /// </summary>
        public BayFit bayFit;
        public GroundType ground = GroundType.Storefront;
        public bool bands = true;
        public bool parapet = true;
        /// <summary>The top tier's roof type; the prototype omits the key for Flat.</summary>
        public RoofType roofType = RoofType.Flat;
        /// <summary>Pitch in degrees; null = not stored (the generator then uses 30°, or 15° for a shed).</summary>
        public double? pitch;
        /// <summary>Eave overhang in metres; null = not stored (the generator then uses 0.35 m). Zero is a real value: no overhang.</summary>
        public double? eave;
        // ---- facade details, Storey's own; the defaults are the prototype's look ----

        /// <summary>Window and door heads: flat (a thin head strip) or an arched hood.</summary>
        public HeadType head = HeadType.Flat;
        /// <summary>A sill under each window, and a head (flat or arched, <see cref="head"/>) over it; off, the window is a plain hole with its glass.</summary>
        public bool sills = true, heads = true;
        /// <summary>Glazing bars: columns and rows of panes per window; null = the prototype's single bar on wide windows.</summary>
        public int? paneCols, paneRows;
        /// <summary>Frames all round each window and door, in the frame colour (sills and heads too).</summary>
        public bool frames;
        /// <summary>The band under each storey's ceiling: height (null = 0.22 m), how far it stands out (null = 0.06 m), and whether it is a shade of the wall rather than the trim.</summary>
        public double? bandH, bandDepth;
        public bool bandWall;
        /// <summary>Only the band over the ground floor: the storeys above have none (docs/EDITOR.md §6.7). Storey's own.</summary>
        public bool bandGround;
        /// <summary>
        /// A cornice round the top of each tier's walls, under its roof or terrace (docs/EDITOR.md §6.7): its height (null =
        /// none) and how far it stands out (null = 0.1 m). It takes the band's place on that storey, whose windows stay under
        /// it. Storey's own (the old BuildingGenerator's top strip).
        /// </summary>
        public double? corniceH, corniceDepth;
        /// <summary>
        /// An artist-made window and street door (<see cref="Generate.OpeningKinds"/>), by id; null = the generator's own. The
        /// style's one window and one door: a set (<see cref="windowSet"/>, <see cref="doorSet"/>) takes their place.
        /// </summary>
        public string? windowKind, doorKind;
        /// <summary>
        /// Several windows, each picked as often as its weight says, opening by opening (docs/OPENINGS.md §7.1); null (or
        /// empty) = <see cref="windowKind"/> in every window. An entry can be the generator's window, or none (the wall left
        /// whole there). Storey's own.
        /// </summary>
        public List<OpeningEntry>? windowSet;
        /// <summary>The ground floor's own windows, as <see cref="windowSet"/>; null (or empty) = the floors above's. Storey's own.</summary>
        public List<OpeningEntry>? groundSet;
        /// <summary>Several street doors, as <see cref="windowSet"/> (an entry that is none is a plain door); null (or empty) = <see cref="doorKind"/>. Storey's own.</summary>
        public List<OpeningEntry>? doorSet;
        /// <summary>How alike a set's windows are (docs/OPENINGS.md §7.1): each picked on its own, or a column, a storey or a wall at a time. Storey's own.</summary>
        public WindowPick windowPick;
        /// <summary>
        /// How high an artist's window that keeps its own size sits over each floor, in metres (docs/EDITOR.md §6.9); null = the
        /// sill of its <see cref="windows"/> type (<see cref="Generate.Facade.SillOf"/>). Other windows take their type's.
        /// </summary>
        public double? sillH;
        /// <summary>
        /// An artist's window that keeps its own size sits centred in its storey's height, its hole's middle halfway up
        /// (docs/EDITOR.md §6.9), as the old BuildingGenerator centres its window prefabs in each floor, the ground floor's in its
        /// own height: over <see cref="sillH"/>, which it wins over. A plinth, a band, a cornice and the window's own dressing
        /// still keep it clear as they do a sill's. Storey's own.
        /// </summary>
        public bool winCentre;
        /// <summary>
        /// How deep windows (<see cref="recess"/>) and street doors (<see cref="doorRecess"/>) are set into the wall, in metres
        /// from its face (docs/EDITOR.md §6.7): the generator's pane or door panel goes that deep (its frame and bars with it), a
        /// shell storey's reveal as deep, and an artist's window or door is pushed back that far (its wall face to the
        /// recess's back). A recessed street door's threshold is in the foundation colour. Null = as before: the generator's
        /// at 16.5 cm (<see cref="Generate.OpeningKinds.ShellReveal"/>), an artist's at the wall's face. Storey's own (the old
        /// BuildingGenerator's window and door insets).
        /// </summary>
        public double? recess, doorRecess;
        /// <summary>
        /// How much bigger each side the hole of a recessed artist's street door is than the door's own (its Storey Cutout), in
        /// metres; below 0 smaller, the door's frame then behind the wall (docs/EDITOR.md §6.7). Its top goes up as far, its foot
        /// stays on the floor; held to its bay with fixed bays and under the storey's band. Only on a shell storey and with a
        /// recess (<see cref="doorRecess"/>, or the door's entry's); null = 0, the door's own hole. Storey's own (the old
        /// BuildingGenerator's door inset offset).
        /// </summary>
        public double? doorRecessOffset;
        /// <summary>
        /// A recess with an artist's window or door in it has a back in the wall's colour at its depth, across the whole hole,
        /// on a shell storey (docs/EDITOR.md §6.7): what of the window or door lies behind it is hidden, and round a hole bigger
        /// than what fills it the wall shows. Storey's own (the old BuildingGenerator caps its insets so).
        /// </summary>
        public bool recessBack;
        /// <summary>The ground floor's plinth height; null = 0.45 m.</summary>
        public double? plinthH;
        /// <summary>How far the plinth stands out from the wall, in metres; null = 0.04 m. Storey's own.</summary>
        public double? plinthDepth;
        /// <summary>A foundation along the foot of the ground floor: how deep it goes below ground (null = none), and how high it shows (null = 0.2 m).</summary>
        public double? foundation, foundationH;
        /// <summary>How far the foundation stands out from the wall, in metres; null = 0.08 m.</summary>
        public double? foundationOut;
        /// <summary>Brick patches scattered on the walls, 0 to 1 (null = none).</summary>
        public double? bricks;

        /// <summary>A mansard's break: how high its steep lower slope rises, in metres; null = 2.4 m. Storey's own.</summary>
        public double? mansard;
        /// <summary>Dormers along every eave of a pitched roof, this far apart (metres, centre to centre); null = none. Storey's own.</summary>
        public double? dormers;
        /// <summary>
        /// A cap on a flat roof's edge (docs/EDITOR.md §6.7; the old BuildingGenerator's roof insert): its height (null = none;
        /// above 0 it stands up from the roof, below 0 it hangs from the roof's edge as a fascia), how far its outer side is
        /// in from the wall's face (null = 0; below 0 it overhangs the wall), and the width of the strip round its top in its
        /// colour (null = none: its top is the roof's colour). Storey's own.
        /// </summary>
        public double? capH, capDepth, capStrip;

        /// <summary>Densities for details placed by rule, as the prototype stores them (percentages); null = the defaults.</summary>
        public DetailRules? details;

        public FacadeStyle Clone()
        {
            var c = (FacadeStyle)MemberwiseClone(); c.details = details?.Clone();
            c.windowSet = OpeningEntry.Copy(windowSet); c.groundSet = OpeningEntry.Copy(groundSet); c.doorSet = OpeningEntry.Copy(doorSet);
            return c;
        }
    }

    /// <summary>
    /// One entry of a style's set of windows or street doors (<see cref="FacadeStyle.windowSet"/>, docs/OPENINGS.md §7.1):
    /// what fills an opening, and how often. Storey's own.
    /// </summary>
    public sealed class OpeningEntry
    {
        /// <summary>An artist's window or door (<see cref="Generate.OpeningKinds"/>), by id; null = the generator's own.</summary>
        public string? kind;
        /// <summary>No window: the wall is left whole there. A door is always a door: a door entry that is none is the plain one.</summary>
        public bool none;
        /// <summary>How often it is picked, against the others' weights; 0 leaves it out, unless every weight is 0 (then each is picked as often).</summary>
        public double weight = 1;
        /// <summary>
        /// A street door's own recess, in metres from the wall's face: a door picked from this entry is set that deep in place of
        /// the style's <see cref="FacadeStyle.doorRecess"/> (as the old BuildingGenerator's door entries override their inset);
        /// null = the style's. Windows' entries leave it null. Storey's own.
        /// </summary>
        public double? recess;

        public OpeningEntry Clone() => (OpeningEntry)MemberwiseClone();

        /// <summary>A copy of a set, entry by entry; null stays null.</summary>
        public static List<OpeningEntry>? Copy(List<OpeningEntry>? set)
        {
            if (set == null) return null;
            var c = new List<OpeningEntry>(set.Count);
            foreach (var e in set) c.Add(e.Clone());
            return c;
        }
    }

    /// <summary>
    /// How alike a set's windows are: each window picked on its own (as the old BuildingGenerator picks them), each column of
    /// a wall the same window all the way up its tier, each storey of a wall the same along it, or each wall one window. Or
    /// picked as the old BuildingGenerator picks, by the wall's edge, the storey and the bay alone, so a building it made gets
    /// the same window in every bay (and the same street door), every building of the style the same pattern
    /// (<see cref="Generate.OpeningSets.PickAsGenerator"/>). Storey's own.
    /// </summary>
    public enum WindowPick { Window, Column, Storey, Wall, Generator }

    /// <summary>
    /// How a wall's bays are laid out: as many as round the style's bay to, stretched to fill the span between the corners;
    /// or as many whole bays as fit on the wall's outer face, each the style's bay exactly, centred on it, a window centred in
    /// each (as the old BuildingGenerator lays out its tiles; docs/EDITOR.md §6.7). Storey's own.
    /// </summary>
    public enum BayFit { Spread, Fixed }

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
            return (Hex64(id, 0), Hex64(id, 16));
        }

        // sixteen hex digits from s[from], read in place: no substring to parse (docs/PERFORMANCE.md §6.8)
        static ulong Hex64(string s, int from)
        {
            ulong v = 0;
            for (int i = from; i < from + 16; i++) { char c = s[i]; v = v << 4 | (uint)(c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10); }
            return v;
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
        /// <summary>
        /// The artist-made kinds' chances (<see cref="Generate.DetailKind.rule"/>), by kind id; a kind not here is placed
        /// by hand only. Layouts write them beside <c>ac</c> and <c>vents</c>. Storey's own.
        /// </summary>
        public SortedDictionary<string, double>? kinds;
        /// <summary>
        /// How the style places each artist-made kind beyond its chance (<see cref="KindRule"/>), by kind id; a kind not here
        /// takes the defaults. Layouts write a kind's as an object in place of its chance (<c>{"p": 50, "density": 1}</c>),
        /// and the chance alone as before when it has none. Storey's own.
        /// </summary>
        public SortedDictionary<string, KindRule>? rules;
        public DetailRules Clone()
        {
            var c = (DetailRules)MemberwiseClone();
            if (kinds != null) c.kinds = new SortedDictionary<string, double>(kinds, System.StringComparer.Ordinal);
            if (rules != null) { c.rules = new SortedDictionary<string, KindRule>(System.StringComparer.Ordinal); foreach (var kv in rules) c.rules[kv.Key] = kv.Value.Clone(); }
            return c;
        }
    }

    /// <summary>Which storeys a kind placed by rule goes on (docs/EDITOR.md §6.12): every one, all but the ground floor, or the ground floor alone. Storey's own.</summary>
    public enum DetailFloors { All, Upper, Ground }

    /// <summary>Which corners a kind up the corners goes on: those that stand out (convex), those set in (concave), or both. Storey's own.</summary>
    public enum DetailCorners { Outward, Inward, All }

    /// <summary>
    /// A style's settings for one artist-made kind placed by rule (docs/EDITOR.md §6.12), beside its chance in
    /// <see cref="DetailRules.kinds"/>; each null or default takes the rule's own. What each means depends on the kind's
    /// rule. Storey's own.
    /// </summary>
    public sealed class KindRule
    {
        /// <summary>Scattered: how many to the square metre of wall at 100 %; null = 0.5.</summary>
        public double? density;
        /// <summary>Scattered: the clear gap kept round each, metres (null = 0.1). Up the corners: how far apart they are up the corner (null = 1 m).</summary>
        public double? spacing;
        /// <summary>Along the top or foot of the wall: how many to each bay; null = 1.</summary>
        public int? per;
        /// <summary>Scattered or up the corners: which storeys it goes on.</summary>
        public DetailFloors floors;
        /// <summary>Up the corners: which corners.</summary>
        public DetailCorners corners;
        /// <summary>
        /// The colour its main colour (the one most of its triangles have) takes in this style: a palette id or a CSS colour;
        /// null keeps the kind's own. One detail then serves many styles.
        /// </summary>
        public string? color;

        /// <summary>Nothing set: the kind's chance alone says it all.</summary>
        public bool IsEmpty => density == null && spacing == null && per == null && floors == DetailFloors.All && corners == DetailCorners.Outward && color == null;

        public KindRule Clone() => (KindRule)MemberwiseClone();
    }
}
