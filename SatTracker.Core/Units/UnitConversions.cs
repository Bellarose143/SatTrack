using System;

namespace SatTracker.Core.Units
{
    /// <summary>
    /// Converts the units data arrives in (degrees, revolutions per day) into the units
    /// the math uses (radians, minutes). Call these once, where data enters the library.
    /// </summary>
    public static class UnitConversions
    {
        public const double MinutesPerDay = 1440.0;
        public const double MillisecondsPerMinute = 60000.0;

        public static double DegreesToRadians(double degrees)
        {
            return degrees * (Math.PI / 180.0);
        }

        /// <summary>
        /// One revolution is one full lap = 2π radians. One day = 1440 minutes.
        /// So rev/day × 2π / 1440 = rad/min.
        /// </summary>
        public static double RevPerDayToRadPerMin(double revPerDay)
        {
            return revPerDay * (2.0 * Math.PI) / MinutesPerDay;
        }
    }
}
