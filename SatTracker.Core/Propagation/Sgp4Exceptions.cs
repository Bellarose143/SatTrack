using System;

namespace SatTracker.Core.Propagation
{
    /// <summary>
    /// The problems SGP4 itself can report. The numbers are Vallado's error codes (satrec.error),
    /// so a message from this library can be matched to his code and papers.
    /// </summary>
    public enum Sgp4ErrorCode
    {
        /// <summary>
        /// Code 1: after drag has been applied, the orbit's average eccentricity is 1 or more
        /// (no longer a closed oval) or below -0.001 (impossible).
        /// </summary>
        MeanEccentricityOutOfRange = 1,

        /// <summary>Code 2: after drag has been applied, the average speed around the orbit is zero or negative.</summary>
        MeanMotionNotPositive = 2,

        /// <summary>
        /// Code 3: after the Moon and Sun corrections, eccentricity is below 0 or above 1.
        /// Only the deep-space part of SGP4 can report this, and this library doesn't run that part.
        /// </summary>
        PerturbedEccentricityOutOfRange = 3,

        /// <summary>
        /// Code 4: the orbit's "semi-latus rectum" (the orbit's width measured straight out from
        /// Earth's center at right angles to the closest point) came out negative, so there is no real orbit.
        /// </summary>
        SemiLatusRectumNegative = 4,

        /// <summary>
        /// Code 5: the snapshot's orbit already passes below Earth's surface. Vallado's current
        /// code no longer reports this one (it relies on code 6 instead). Kept so every code has a name.
        /// </summary>
        EpochElementsSubOrbital = 5,

        /// <summary>Code 6: the satellite's predicted distance from Earth's center is less than one Earth radius. It has come down.</summary>
        Decayed = 6,
    }

    /// <summary>
    /// SGP4 reported that it can't give a position for this satellite at this time.
    /// No position or velocity is returned alongside it, because the numbers would be garbage.
    /// </summary>
    public sealed class Sgp4Exception : Exception
    {
        public Sgp4Exception(Sgp4ErrorCode code, int noradCatalogId, double minutesSinceEpoch, bool duringInitialization)
            : base(BuildMessage(code, noradCatalogId, minutesSinceEpoch, duringInitialization))
        {
            Code = code;
            NoradCatalogId = noradCatalogId;
            MinutesSinceEpoch = minutesSinceEpoch;
            DuringInitialization = duringInitialization;
        }

        /// <summary>Which problem it was. <c>(int)Code</c> is Vallado's number (1 to 6).</summary>
        public Sgp4ErrorCode Code { get; }

        public int NoradCatalogId { get; }

        /// <summary>The time asked for. During initialization this is 0 (SGP4 checks the epoch itself).</summary>
        public double MinutesSinceEpoch { get; }

        /// <summary>
        /// true: the problem showed up while setting the satellite up (Vallado's sgp4init also
        /// propagates to minute 0), so the snapshot itself can't be used at all.
        /// </summary>
        public bool DuringInitialization { get; }

        /// <summary>A plain-language explanation of each code.</summary>
        public static string Describe(Sgp4ErrorCode code)
        {
            switch (code)
            {
                case Sgp4ErrorCode.MeanEccentricityOutOfRange:
                    return "the orbit's average eccentricity left the valid range (it must stay from -0.001 up to just below 1: below 1 keeps it a closed oval, and drag can push it slightly negative but not past -0.001)";
                case Sgp4ErrorCode.MeanMotionNotPositive:
                    return "the orbit's average speed around the hoop dropped to zero or below";
                case Sgp4ErrorCode.PerturbedEccentricityOutOfRange:
                    return "the eccentricity left the 0 to 1 range after the Moon and Sun corrections (deep space only)";
                case Sgp4ErrorCode.SemiLatusRectumNegative:
                    return "the orbit's width (semi-latus rectum) came out negative, so there is no real orbit";
                case Sgp4ErrorCode.EpochElementsSubOrbital:
                    return "the snapshot's orbit already dips below Earth's surface";
                case Sgp4ErrorCode.Decayed:
                    return "the satellite has decayed: it is predicted to be closer to Earth's center than Earth's own radius";
                default:
                    return "unknown SGP4 error";
            }
        }

        private static string BuildMessage(Sgp4ErrorCode code, int noradCatalogId, double minutesSinceEpoch, bool duringInitialization)
        {
            string when = duringInitialization
                ? "while setting up from the snapshot (minute 0)"
                : $"at {minutesSinceEpoch:R} minutes since the snapshot epoch";
            return $"SGP4 error {(int)code} for satellite {noradCatalogId} {when}: {Describe(code)}.";
        }
    }

    /// <summary>
    /// The satellite takes 225 minutes or more to go around once, so it needs SGP4's "deep space"
    /// part (the Moon's and Sun's pull, and resonance with Earth's spin). This library is
    /// near-Earth only, so it refuses rather than give a wrong answer.
    /// </summary>
    public sealed class Sgp4DeepSpaceNotSupportedException : NotSupportedException
    {
        public Sgp4DeepSpaceNotSupportedException(int noradCatalogId, double orbitPeriodMinutes)
            : base($"Satellite {noradCatalogId} takes {orbitPeriodMinutes:R} minutes per orbit. " +
                   $"Orbits of {Sgp4.DeepSpacePeriodThresholdMinutes:R} minutes or more need SGP4's deep-space mode, which this library does not support (near-Earth only).")
        {
            NoradCatalogId = noradCatalogId;
            OrbitPeriodMinutes = orbitPeriodMinutes;
        }

        public int NoradCatalogId { get; }

        /// <summary>2π ÷ the un-Kozai'd mean motion, the exact number Vallado compares against 225.</summary>
        public double OrbitPeriodMinutes { get; }
    }
}
