using System;

namespace SatTracker.Core.Propagation
{
    /// <summary>
    /// SECTION 1 (setup and constants): Earth's size, pull, and bulge, as SGP4 sees them.
    /// Port of the <c>wgs72</c> case of Vallado's <c>getgravconst</c> (SGP4.cpp).
    /// <para>
    /// <b>Why WGS-72 and not the newer WGS-84?</b> The published orbit snapshots (TLEs / CelesTrak
    /// elements) were fitted by running SGP4 with these WGS-72 numbers. The elements only make
    /// sense together with the constants they were fitted with. Feeding them into SGP4 with
    /// WGS-84 numbers gives a slightly different (wrong) answer, even though WGS-84 describes
    /// the real Earth better. WGS-84 belongs later, when turning SGP4's output into a spot on
    /// the ground, not inside SGP4.
    /// </para>
    /// <para>
    /// Vallado's code also has <c>wgs72old</c> (the rounded numbers from the 1980 report) and
    /// <c>wgs84</c>. The official answers in tcppver.out were made with <c>wgs72</c> (this one).
    /// See <see cref="Sgp4"/> for the evidence.
    /// </para>
    /// <para>
    /// Inside SGP4, distances are measured in "Earth radii" (1.0 = <see cref="RadiusEarthKm"/>)
    /// and time in minutes. Only the final answer is turned into km and km/s.
    /// </para>
    /// </summary>
    internal static class Wgs72
    {
        /// <summary>Vallado <c>mus</c>: Earth's gravity strength (G × Earth's mass), km³/s².</summary>
        public const double MuKm3PerSec2 = 398600.8;

        /// <summary>Vallado <c>radiusearthkm</c>: Earth's equator radius, km. The "Earth radius" unit.</summary>
        public const double RadiusEarthKm = 6378.135;

        /// <summary>
        /// Vallado <c>xke</c>: Earth's gravity strength again, but in SGP4's own units
        /// (Earth radii and minutes): √(mu / R³) × 60. Unit: (Earth radii)^1.5 per minute.
        /// Written exactly as Vallado computes it, so the last digits match.
        /// </summary>
        public static readonly double Xke = 60.0 / Math.Sqrt(RadiusEarthKm * RadiusEarthKm * RadiusEarthKm / MuKm3PerSec2);

        /// <summary>Vallado <c>tumin</c>: how many minutes are in one of SGP4's internal time units (1 / xke).</summary>
        public static readonly double TuMin = 1.0 / Xke;

        /// <summary>Vallado <c>j2</c>: the size of Earth's equatorial bulge (no unit). The biggest effect after plain gravity.</summary>
        public const double J2 = 0.001082616;

        /// <summary>Vallado <c>j3</c>: Earth is slightly pear-shaped (north/south lopsided). No unit.</summary>
        public const double J3 = -0.00000253881;

        /// <summary>Vallado <c>j4</c>: a finer correction to the bulge's shape. No unit.</summary>
        public const double J4 = -0.00000165597;

        /// <summary>Vallado <c>j3oj2</c>: J3 divided by J2, used often enough to keep ready.</summary>
        public const double J3OverJ2 = J3 / J2;

        /// <summary>
        /// Vallado <c>vkmpersec</c>: turns a speed in SGP4's units (Earth radii per time unit) into km/s.
        /// Same expression as in Vallado's <c>sgp4</c>: radiusearthkm × xke / 60.
        /// </summary>
        public static readonly double KmPerSecPerInternalSpeedUnit = RadiusEarthKm * Xke / 60.0;
    }
}
