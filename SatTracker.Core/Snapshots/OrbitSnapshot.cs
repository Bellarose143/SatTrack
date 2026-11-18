using System;
using SatTracker.Core.Time;

namespace SatTracker.Core.Snapshots
{
    /// <summary>
    /// One satellite's orbit as published at a single moment (the epoch): the
    /// "snapshot" the prediction formula starts from. All values are already in
    /// library units (radians, minutes, UTC). Build it with <see cref="CelesTrakJsonParser"/>.
    /// </summary>
    /// <param name="NoradCatalogId">The satellite's catalog number. ISS = 25544.</param>
    /// <param name="ObjectName">Human-readable name, e.g. "ISS (ZARYA)".</param>
    /// <param name="EpochUtc">The moment this snapshot describes, in UTC. The single stored copy
    /// of the epoch; <see cref="EpochJd"/> is always worked out from it.</param>
    /// <param name="MeanMotionRadPerMin">How fast it goes around: average angle covered per minute.</param>
    /// <param name="Eccentricity">How stretched the orbit is. 0 = perfect circle, just under 1 = very long oval. No unit.</param>
    /// <param name="InclinationRad">Tilt of the orbit's hoop relative to the equator.</param>
    /// <param name="RaanRad">Right Ascension of the Ascending Node: which way the hoop is turned, measured
    /// along the equator to where the satellite crosses it going north.</param>
    /// <param name="ArgumentOfPerigeeRad">Where on the hoop the closest point to Earth sits, measured from that northward crossing.</param>
    /// <param name="MeanAnomalyRad">Where the satellite is along the hoop at the epoch, as an "average" angle from the closest point.</param>
    /// <param name="BStarPerEarthRadii">Drag term (how much thin air slows it). Unit is 1 / Earth radii, used as-is by the prediction formula.</param>
    public sealed record OrbitSnapshot(
        int NoradCatalogId,
        string ObjectName,
        DateTime EpochUtc,
        double MeanMotionRadPerMin,
        double Eccentricity,
        double InclinationRad,
        double RaanRad,
        double ArgumentOfPerigeeRad,
        double MeanAnomalyRad,
        double BStarPerEarthRadii)
    {
        /// <summary>
        /// The epoch as a Julian Date (JD at midnight + fraction of the day), for the math.
        /// Computed from <see cref="EpochUtc"/> on every read rather than stored, so the two can
        /// never disagree, even after <c>snapshot with { EpochUtc = ... }</c>.
        /// </summary>
        public JulianDate EpochJd => JulianDate.FromUTC(EpochUtc);
    }
}
