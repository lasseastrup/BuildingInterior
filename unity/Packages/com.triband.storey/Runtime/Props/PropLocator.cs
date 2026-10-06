#nullable enable
using Triband.Storey.Generate;
using Triband.Storey.Play;

namespace Triband.Storey.Props
{
    /// <summary>
    /// Which building a prop is in (docs/PROPS.md §2.3): a site's own building whose outline holds the point, from a metre
    /// below its ground floor to a few metres above its roof. Engine-free and garbage-free, so the props manager can ask
    /// for every moving prop every frame. The answer is a building, held by id: its index in the building table moves when
    /// a site is built again, so the caller turns it into a table index when it writes it.
    /// </summary>
    public static class PropLocator
    {
        /// <summary>How far above the roof's base a prop still counts as the building's (a roof terrace, a pitched roof's attic).</summary>
        public const double AboveRoof = 4.0;

        /// <summary>The site's own building a point (site space) is in, or null: in the street, or under a raised building.</summary>
        public static BuildingData? BuildingAt(Site site, double x, double y, double z)
        {
            var cell = site.InCell(x, z);
            for (int i = 0; i < cell.Count; i++)
            {
                var b = cell[i];
                if (y < b.elevation - 1 || y > Derived.RoofY(b) + AboveRoof) continue;
                if (!PlayWorld.Inside(b, x - b.pos.x, z - b.pos.z)) continue;
                if (site.IndexOf(b) >= site.Own) continue;   // a neighbouring district's, there for its walls: not drawn here
                return b;
            }
            return null;
        }

        /// <summary>The renderer's user value for a building's table index: index + 1, 0 for none (in the street).</summary>
        public static uint Encode(int tableIndex) => tableIndex < 0 ? 0u : (uint)(tableIndex + 1);

        /// <summary>The table index a user value stands for, -1 for none.</summary>
        public static int Decode(uint value) => (int)value - 1;
    }
}
