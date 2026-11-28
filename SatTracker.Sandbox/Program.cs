// M1-06: Sanity check against reality.
// M2-06: also propagates the snapshot to right now with SGP4 (CHECK 4).
//
// This is the "app edge": the one place allowed to read the clock (DateTime.UtcNow).
// SatTracker.Core never does; it only takes times as inputs.
//
// Usage (from the repo root):
//   dotnet run --project SatTracker.Sandbox -- path\to\iss.json

using System.Globalization;
using SatTracker.Core.Propagation;
using SatTracker.Core.Snapshots;
using SatTracker.Core.Time;
using SatTracker.Core.Units;

namespace SatTracker.Sandbox
{
    public static class Program
    {
        private const double MinutesPerHour = 60.0;
        private const double RadiansToDegrees = 180.0 / Math.PI;
        private const double RadiansPerRevolution = 2.0 * Math.PI;

        // Earth's radius at the equator (WGS-84), the value M2-06 uses for "altitude".
        private const double EarthEquatorialRadiusKm = 6378.137;

        public static int Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.Error.WriteLine("Usage: dotnet run --project SatTracker.Sandbox -- <path to CelesTrak JSON>");
                return 1;
            }

            string jsonPath = args[0];
            if (!File.Exists(jsonPath))
            {
                Console.Error.WriteLine($"File not found: {Path.GetFullPath(jsonPath)}");
                return 1;
            }

            OrbitSnapshot snapshot;
            try
            {
                snapshot = CelesTrakJsonParser.Parse(File.ReadAllText(jsonPath));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Could not parse {jsonPath}: {ex.Message}");
                return 1;
            }

            // Read the clock once, so every check uses the same "now".
            DateTime nowUtc = DateTime.UtcNow;

            PrintFields(snapshot, jsonPath);
            PrintChecks(snapshot, nowUtc);
            return PrintPositionNow(snapshot, nowUtc) ? 0 : 1;
        }

        // Check 1: every field, in the units the library stores AND converted back
        // to CelesTrak's units (degrees, rev/day), so you can compare with the page by eye.
        private static void PrintFields(OrbitSnapshot s, string jsonPath)
        {
            Console.WriteLine($"File: {Path.GetFullPath(jsonPath)}");
            Console.WriteLine();
            Console.WriteLine("CHECK 1: fields (compare the right-hand column with CelesTrak)");
            Console.WriteLine($"  {"CelesTrak field",-18} {"Library value",-34} CelesTrak units");
            Row("OBJECT_NAME", s.ObjectName, s.ObjectName);
            Row("NORAD_CAT_ID", Show(s.NoradCatalogId), Show(s.NoradCatalogId));
            Row("EPOCH", s.EpochUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture) + " (" + s.EpochUtc.Kind + ")",
                s.EpochUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff", CultureInfo.InvariantCulture));
            Row("MEAN_MOTION", Show(s.MeanMotionRadPerMin) + " rad/min",
                Show(s.MeanMotionRadPerMin * UnitConversions.MinutesPerDay / RadiansPerRevolution) + " rev/day");
            Row("ECCENTRICITY", Show(s.Eccentricity), Show(s.Eccentricity));
            Row("INCLINATION", Show(s.InclinationRad) + " rad", Show(s.InclinationRad * RadiansToDegrees) + " deg");
            Row("RA_OF_ASC_NODE", Show(s.RaanRad) + " rad", Show(s.RaanRad * RadiansToDegrees) + " deg");
            Row("ARG_OF_PERICENTER", Show(s.ArgumentOfPerigeeRad) + " rad", Show(s.ArgumentOfPerigeeRad * RadiansToDegrees) + " deg");
            Row("MEAN_ANOMALY", Show(s.MeanAnomalyRad) + " rad", Show(s.MeanAnomalyRad * RadiansToDegrees) + " deg");
            Row("BSTAR", Show(s.BStarPerEarthRadii) + " /earth radii", Show(s.BStarPerEarthRadii));
            Console.WriteLine();
        }

        private static void PrintChecks(OrbitSnapshot s, DateTime nowUtc)
        {
            // Check 2: one lap is 2π radians, so minutes per lap = 2π / (radians per minute).
            // Same as CelesTrak's 1440 / (rev per day), just in the library's units.
            double orbitPeriodMinutes = RadiansPerRevolution / s.MeanMotionRadPerMin;
            Console.WriteLine("CHECK 2: orbit period (expect near 92.9 min)");
            Console.WriteLine($"  2π / MeanMotionRadPerMin = {Show(orbitPeriodMinutes)} min");
            Console.WriteLine();

            // Check 3: how old the snapshot is right now, using M1-05's MinutesSince.
            double snapshotAgeMinutes = JulianDate.FromUTC(nowUtc).MinutesSince(s.EpochJd);
            Console.WriteLine("CHECK 3: snapshot age (expect positive and under ~2 days = 2880 min)");
            Console.WriteLine($"  Now (UTC):    {nowUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)}");
            Console.WriteLine($"  Epoch (UTC):  {s.EpochUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)}");
            Console.WriteLine($"  Age:          {Show(snapshotAgeMinutes)} min  (= {Show(snapshotAgeMinutes / MinutesPerHour)} h)");
            Console.WriteLine();
        }

        // Check 4 (M2-06): where SGP4 says the satellite is right now.
        // Returns false (after printing why) if SGP4 can't give a position for this snapshot.
        private static bool PrintPositionNow(OrbitSnapshot s, DateTime nowUtc)
        {
            double minutesSinceEpoch = JulianDate.FromUTC(nowUtc).MinutesSince(s.EpochJd);

            Sgp4State state;
            try
            {
                state = Sgp4.Initialize(s).Propagate(minutesSinceEpoch);
            }
            catch (Exception ex) when (ex is Sgp4Exception || ex is Sgp4DeepSpaceNotSupportedException)
            {
                Console.Error.WriteLine($"CHECK 4 failed: {ex.Message}");
                return false;
            }

            // Distance from Earth's center minus Earth's radius at the equator. Earth is flatter towards
            // the poles, so away from the equator this reads a little low (up to ~13 km at the ISS's
            // 51.6° maximum latitude). Fine for a sanity check.
            double distanceFromCenterKm = state.PositionKm.Length;
            double altitudeKm = distanceFromCenterKm - EarthEquatorialRadiusKm;
            double speedKmPerSec = state.VelocityKmPerSec.Length;

            Console.WriteLine("CHECK 4: where is it right now? (expect altitude ~400 to 430 km, speed ~7.6 to 7.7 km/s)");
            Console.WriteLine($"  Minutes since epoch:   {Show(minutesSinceEpoch)} min");
            Console.WriteLine($"  Position (TEME, km):   x {Show(state.PositionKm.X)}, y {Show(state.PositionKm.Y)}, z {Show(state.PositionKm.Z)}");
            Console.WriteLine($"  Velocity (TEME, km/s): x {Show(state.VelocityKmPerSec.X)}, y {Show(state.VelocityKmPerSec.Y)}, z {Show(state.VelocityKmPerSec.Z)}");
            Console.WriteLine($"  Distance from center:  {Show(distanceFromCenterKm)} km");
            Console.WriteLine($"  Altitude:              {Show(altitudeKm)} km  (distance - {Show(EarthEquatorialRadiusKm)}; compare with your hand calculation)");
            Console.WriteLine($"  Speed:                 {Show(speedKmPerSec)} km/s");
            return true;
        }

        private static void Row(string field, string libraryValue, string celesTrakValue)
        {
            Console.WriteLine($"  {field,-18} {libraryValue,-34} {celesTrakValue}");
        }

        // Full precision, invariant culture (so a comma-decimal Windows locale can't change the output).
        private static string Show(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        private static string Show(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
