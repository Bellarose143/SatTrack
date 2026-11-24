// M2-02: Pairs each SGP4-VER.TLE entry with its expected answers from tcppver.out, and works out
// which cases ended in an SGP4 error.

namespace SatTracker.Core.Tests.Sgp4Verification
{
    /// <summary>
    /// One official test case: the input (TLE entry) and the expected answers (rows from tcppver.out).
    /// </summary>
    public sealed class Sgp4VerificationCase
    {
        internal Sgp4VerificationCase(Sgp4VerTleEntry tle, Sgp4ExpectedRun expected,
            IReadOnlyList<double> plannedMinutes, double? sgp4ErrorAtMinutes)
        {
            Tle = tle;
            Expected = expected;
            PlannedMinutes = plannedMinutes;
            Sgp4ErrorAtMinutes = sgp4ErrorAtMinutes;
        }

        public Sgp4VerTleEntry Tle { get; }

        public Sgp4ExpectedRun Expected { get; }

        public int CatalogNumber => Tle.CatalogNumber;

        /// <summary>
        /// Every time (minutes since epoch) the official C++ test program tries, in order:
        /// minute 0 first, then start, start + step, ... with the last step cut short to land exactly on stop.
        /// The file holds one row per planned time, unless SGP4 reported an error part way.
        /// </summary>
        public IReadOnlyList<double> PlannedMinutes { get; }

        /// <summary>
        /// null: the official run reached the stop time with no error.
        /// A number: the official SGP4 reported an error (e.g. the satellite decayed) at this time, so
        /// the run stopped and this and every later planned time have no row. Your SGP4 should give
        /// correct answers at every row's time, and an error at this time.
        /// <para>
        /// The file doesn't say WHICH error it was (the C++ program prints that to the console only),
        /// so only "an error happens here" can be checked, not the error code.
        /// </para>
        /// </summary>
        public double? Sgp4ErrorAtMinutes { get; }

        public bool ExpectsSgp4Error => Sgp4ErrorAtMinutes.HasValue;

        // NUnit uses this to name test cases, e.g. "SGP4-VER 22312 (54.2028672 to 1440 min)".
        public override string ToString()
        {
            return $"{Tle.Snapshot.ObjectName} ({Tle.StartMinutes:R} to {Tle.StopMinutes:R} min)";
        }
    }

    /// <summary>
    /// Loads the official SGP4 verification data and checks the two files agree with each other.
    /// </summary>
    public static class Sgp4VerificationData
    {
        /// <summary>
        /// The row times in tcppver.out are printed with 8 decimal places, so a row's time can differ
        /// from the exact planned time by up to half of 1e-8 minutes. Allow 1e-8 to be safe.
        /// This only checks that the two files line up. It is not a tolerance on any orbit value.
        /// </summary>
        private const double PrintedTimeToleranceMinutes = 1e-8;

        public static string FixtureDirectory =>
            Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Sgp4Verification");

        /// <summary>Loads the copies saved in the test project (Fixtures/Sgp4Verification).</summary>
        public static IReadOnlyList<Sgp4VerificationCase> LoadFixtures()
        {
            return Load(
                Path.Combine(FixtureDirectory, Sgp4VerTleReader.DefaultSourceName),
                Path.Combine(FixtureDirectory, TcppverReader.DefaultSourceName));
        }

        public static IReadOnlyList<Sgp4VerificationCase> Load(string tlePath, string tcppverPath)
        {
            return Combine(Sgp4VerTleReader.ReadFile(tlePath), TcppverReader.ReadFile(tcppverPath));
        }

        /// <summary>
        /// Pairs entries and runs by position (both files are in the same order) and checks every row
        /// time matches the time the C++ program would have used. Any mismatch throws
        /// <see cref="InvalidDataException"/>: it would mean the files don't belong together.
        /// </summary>
        public static IReadOnlyList<Sgp4VerificationCase> Combine(IReadOnlyList<Sgp4VerTleEntry> tles, Sgp4ExpectedOutput expected)
        {
            if (tles.Count != expected.Runs.Count)
                throw new InvalidDataException(
                    $"SGP4-VER.TLE has {tles.Count} entries but tcppver.out has {expected.Runs.Count} satellites. They should match one to one.");

            var cases = new List<Sgp4VerificationCase>(tles.Count);
            for (int i = 0; i < tles.Count; i++)
            {
                Sgp4VerTleEntry tle = tles[i];
                Sgp4ExpectedRun run = expected.Runs[i];

                if (tle.CatalogNumber != run.CatalogNumber)
                    throw new InvalidDataException(
                        $"Entry #{i + 1}: SGP4-VER.TLE line {tle.Line1Number} is satellite {tle.CatalogNumber}, " +
                        $"but tcppver.out line {run.HeaderLineNumber} is satellite {run.CatalogNumber}.");

                IReadOnlyList<double> planned = PlannedMinutes(tle.StartMinutes, tle.StopMinutes, tle.StepMinutes);

                if (run.Rows.Count > planned.Count)
                    throw new InvalidDataException(
                        $"Satellite {tle.CatalogNumber} (tcppver.out line {run.HeaderLineNumber}): {run.Rows.Count} rows, " +
                        $"but its start/stop/step only allow {planned.Count}.");

                for (int k = 0; k < run.Rows.Count; k++)
                {
                    Sgp4ExpectedRow row = run.Rows[k];
                    if (Math.Abs(row.MinutesSinceEpoch - planned[k]) > PrintedTimeToleranceMinutes)
                        throw new InvalidDataException(
                            $"tcppver.out line {row.LineNumber}: satellite {tle.CatalogNumber} row {k + 1} is at minute " +
                            $"{row.MinutesSinceEpoch:R}, but the C++ test program would use minute {planned[k]:R} there.");
                }

                double? errorAt = run.Rows.Count < planned.Count ? planned[run.Rows.Count] : (double?)null;
                cases.Add(new Sgp4VerificationCase(tle, run, planned, errorAt));
            }

            return cases;
        }

        /// <summary>
        /// Rebuilds the list of times Vallado's C++ test program (TestSGP4.cpp) propagates to, step by
        /// step the same way it does.
        /// <list type="number">
        /// <item>It always propagates to minute 0 first and prints that row.</item>
        /// <item>Then it walks from start to stop, adding the step each time. If start is 0, it skips
        /// it, so minute 0 isn't printed twice.</item>
        /// <item>If a step would go past stop, that last time is cut back to exactly stop.</item>
        /// <item>If SGP4 reports an error at a time, it prints nothing for that time and stops.</item>
        /// </list>
        /// Example: start -5184, stop -4896, step 120 → 0, -5184, -5064, -4944, -4896.
        /// </summary>
        public static IReadOnlyList<double> PlannedMinutes(double startMinutes, double stopMinutes, double stepMinutes)
        {
            if (stepMinutes <= 0)
                throw new ArgumentOutOfRangeException(nameof(stepMinutes), stepMinutes, "Step must be greater than 0.");

            var times = new List<double> { 0.0 };

            double t = startMinutes;
            if (Math.Abs(t) > 1.0e-8) // same check as the C++ program
                t -= stepMinutes;

            while (t < stopMinutes)
            {
                t += stepMinutes;
                if (t > stopMinutes)
                    t = stopMinutes;
                times.Add(t);
            }

            return times;
        }
    }
}
