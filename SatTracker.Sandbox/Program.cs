// M1-06: Sanity check against reality.
//
// This is the "app edge": the one place allowed to read the clock (DateTime.UtcNow).
// SatTracker.Core never does; it only takes times as inputs.
//
// Usage (from the repo root):
//   dotnet run --project SatTracker.Sandbox -- path\to\iss.json

using System.Globalization;
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

            PrintFields(snapshot, jsonPath);
            PrintChecks(snapshot, DateTime.UtcNow);
            return 0;
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
