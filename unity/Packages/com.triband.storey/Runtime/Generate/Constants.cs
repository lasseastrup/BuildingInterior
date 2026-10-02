#nullable enable
namespace Triband.Storey.Generate
{
    /// <summary>The prototype's dimensions (metres), SPEC §4.</summary>
    public static class Dim
    {
        /// <summary>Exterior wall thickness, built outside the footprint line.</summary>
        public const double T_EXT = 0.3;
        /// <summary>Interior wall thickness.</summary>
        public const double T_INT = 0.14;
        /// <summary>Floor slab thickness.</summary>
        public const double SLAB = 0.25;
        public const double DOOR_INT = 1.0, DOOR_EXT = 1.6;
        /// <summary>A core's walls sit this far outside its inner size.</summary>
        public const double CORE_T = 0.15;
        public const double STAIR_W = 2.6, STAIR_D = 5.2;
        public const double LIFT_W = 2.4, LIFT_D = 2.4;
        /// <summary>
        /// Stacked straight flights: the footprint (the flight lane and the walkway beside it), the flight lane's width,
        /// and the landing at each end (a 4.6 m run between them).
        /// </summary>
        public const double FLIGHT_W = 2.6, FLIGHT_D = 6.4, FLIGHT_LANE = 1.3, FLIGHT_LANDING = 0.9;
    }

    /// <summary>Faces a box can leave out because something covers them.</summary>
    [System.Flags]
    public enum Skip { None = 0, Out = 1, In = 2, UEnd = 4, UStart = 8, Top = 16, Bot = 32 }
}
