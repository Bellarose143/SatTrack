// M2-02: SGP4-VER.TLE reader tests (the real file plus hand-made bad lines).

namespace SatTracker.Core.Tests.Sgp4Verification
{
    public class Sgp4VerTleReaderTests
    {
        private static IReadOnlyList<Sgp4VerTleEntry> ReadRealFile() =>
            Sgp4VerTleReader.ReadFile(Path.Combine(Sgp4VerificationData.FixtureDirectory, "SGP4-VER.TLE"));

        private static Sgp4VerTleEntry First(int catalogNumber) =>
            ReadRealFile().First(e => e.CatalogNumber == catalogNumber);

        // ---------- The ticket's test: a known entry comes out right ----------

        // SGP4-VER.TLE, first case:
        // 1 00005U 58002B   00179.78495062  .00000023  00000-0  28098-4 0  4753
        // 2 00005  34.2682 348.7242 1859667 331.7664  19.3264 10.82419157413667     0.00      4320.0        360.00
        [Test]
        public void Entry00005_CatalogInclinationAndMeanMotion_AreCorrect()
        {
            var snapshot = First(5).Snapshot;

            Assert.That(snapshot.NoradCatalogId, Is.EqualTo(5));
            // 34.2682° × π/180, worked out separately (Python).
            Assert.That(snapshot.InclinationRad, Is.EqualTo(0.5980929187319208).Within(1e-15));
            // 10.82419157 rev/day × 2π / 1440 min/day, worked out separately (Python).
            Assert.That(snapshot.MeanMotionRadPerMin, Is.EqualTo(0.04722944544077856).Within(1e-16));
        }

        [Test]
        public void Entry00005_OtherFields_AreCorrect()
        {
            var entry = First(5);
            var s = entry.Snapshot;

            Assert.That(s.ObjectName, Is.EqualTo("SGP4-VER 00005"));
            Assert.That(s.Eccentricity, Is.EqualTo(0.1859667)); // "1859667" with the implied "0."
            Assert.That(s.BStarPerEarthRadii, Is.EqualTo(0.28098e-4)); // " 28098-4"
            Assert.That(s.RaanRad, Is.EqualTo(348.7242 * Math.PI / 180).Within(1e-15));
            Assert.That(s.ArgumentOfPerigeeRad, Is.EqualTo(331.7664 * Math.PI / 180).Within(1e-15));
            Assert.That(s.MeanAnomalyRad, Is.EqualTo(19.3264 * Math.PI / 180).Within(1e-15));
            Assert.That(entry.Line1Number, Is.EqualTo(3));
            Assert.That(entry.Description, Does.Contain("TEME example"));
        }

        [Test]
        public void Entry00005_Epoch_IsDay179Point78495062Of2000_InUtc()
        {
            // "00" → 2000. Day 179 of leap year 2000 is June 27th.
            // 0.78495062 day = 678,197,335,680 ticks (100 ns each) = 18:50:19.733568, exactly.
            var epoch = First(5).Snapshot.EpochUtc;

            Assert.That(epoch.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(epoch, Is.EqualTo(new DateTime(2000, 6, 27, 0, 0, 0, DateTimeKind.Utc).AddTicks(678_197_335_680)));
        }

        [TestCase(5, 2000)]     // "00179..."
        [TestCase(23333, 1994)] // "94305..."
        [TestCase(88888, 1980)] // "80275..."
        [TestCase(22312, 2006)] // "06094..."
        public void TwoDigitEpochYear_57To99Is19xx_OtherwiseIs20xx(int catalogNumber, int expectedYear)
        {
            Assert.That(First(catalogNumber).Snapshot.EpochUtc.Year, Is.EqualTo(expectedYear));
        }

        [Test]
        public void NegativeBStar_IsParsed()
        {
            // 21897: "-13525-3" → -0.13525e-3
            Assert.That(First(21897).Snapshot.BStarPerEarthRadii, Is.EqualTo(-0.13525e-3));
        }

        // ---------- The whole file ----------

        [Test]
        public void RealFile_Has33Entries_With20413ListedTwice()
        {
            var entries = ReadRealFile();

            Assert.That(entries, Has.Count.EqualTo(33));
            Assert.That(entries.Select(e => e.CatalogNumber).Distinct().Count(), Is.EqualTo(32));
            Assert.That(entries.Count(e => e.CatalogNumber == 20413), Is.EqualTo(2));
        }

        [Test]
        public void RealFile_StartStopStep_AreParsed()
        {
            var entries = ReadRealFile();

            AssertWindow(entries[0], 5, 0.0, 4320.0, 360.0);
            AssertWindow(entries[1], 4632, -5184.0, -4896.0, 120.0);   // negative times: before the epoch
            AssertWindow(entries.Single(e => e.CatalogNumber == 22312), 22312, 54.2028672, 1440.0, 20.0);
            AssertWindow(entries[32], 20413, 1844000.0, 1845100.0, 5.0); // the second 20413, last in the file
        }

        private static void AssertWindow(Sgp4VerTleEntry e, int catalog, double start, double stop, double step)
        {
            Assert.That(e.CatalogNumber, Is.EqualTo(catalog));
            Assert.That(e.StartMinutes, Is.EqualTo(start));
            Assert.That(e.StopMinutes, Is.EqualTo(stop));
            Assert.That(e.StepMinutes, Is.EqualTo(step));
        }

        [Test]
        public void RealFile_Has9NearEarthAnd24DeepSpaceEntries()
        {
            // Near-Earth = one lap takes under 225 minutes. That's the only kind this project supports.
            var entries = ReadRealFile();
            var nearEarth = entries.Where(e => e.PeriodMinutes < 225.0).Select(e => e.CatalogNumber).ToList();

            Assert.That(nearEarth, Is.EqualTo(new[] { 5, 6251, 22312, 28057, 28350, 28872, 29141, 29238, 88888 }));
            Assert.That(entries.Count(e => e.PeriodMinutes >= 225.0), Is.EqualTo(24));
        }

        // ---------- Hand-made input ----------

        private const string GoodLine1 = "1 00005U 58002B   00179.78495062  .00000023  00000-0  28098-4 0  4753";
        private const string GoodLine2 = "2 00005  34.2682 348.7242 1859667 331.7664  19.3264 10.82419157413667     0.00      4320.0        360.00";

        [Test]
        public void HandMade_CommentsAreSkipped_AndBecomeTheDescription()
        {
            string text = "# first comment\n#   second    comment\n" + GoodLine1 + "\n" + GoodLine2 + "\n";

            var entry = Sgp4VerTleReader.Parse(text).Single();

            Assert.That(entry.Description, Is.EqualTo("first comment second comment"));
            Assert.That(entry.Line1Number, Is.EqualTo(3));
        }

        [Test]
        public void HandMade_BStarExponentFormat_MinusSign()
        {
            // The example from the ticket notes: "-11606-4" means -0.11606e-4.
            string line1 = GoodLine1.Substring(0, 53) + "-11606-4" + GoodLine1.Substring(61);

            var entry = Sgp4VerTleReader.Parse(line1 + "\n" + GoodLine2).Single();

            Assert.That(entry.Snapshot.BStarPerEarthRadii, Is.EqualTo(-0.11606e-4));
        }

        [Test]
        public void HandMade_Year56Is2056_Year57Is1957()
        {
            string y56 = GoodLine1.Substring(0, 18) + "56" + GoodLine1.Substring(20);
            string y57 = GoodLine1.Substring(0, 18) + "57" + GoodLine1.Substring(20);

            Assert.That(Sgp4VerTleReader.Parse(y56 + "\n" + GoodLine2).Single().Snapshot.EpochUtc.Year, Is.EqualTo(2056));
            Assert.That(Sgp4VerTleReader.Parse(y57 + "\n" + GoodLine2).Single().Snapshot.EpochUtc.Year, Is.EqualTo(1957));
        }

        [Test]
        public void HandMade_CrlfLineEndings_Work()
        {
            var entry = Sgp4VerTleReader.Parse(GoodLine1 + "\r\n" + GoodLine2 + "\r\n").Single();

            Assert.That(entry.StepMinutes, Is.EqualTo(360.0));
        }

        private static void AssertThrowsMentioning(string text, params string[] fragments)
        {
            var ex = Assert.Throws<FormatException>(() => Sgp4VerTleReader.Parse(text));
            foreach (string fragment in fragments)
                Assert.That(ex!.Message, Does.Contain(fragment));
        }

        [Test]
        public void Malformed_MissingStartStopStep_Throws()
        {
            AssertThrowsMentioning(GoodLine1 + "\n" + GoodLine2.Substring(0, 69), "line 2", "start, stop and step");
        }

        [Test]
        public void Malformed_OnlyTwoOfStartStopStep_Throws()
        {
            AssertThrowsMentioning(GoodLine1 + "\n" + GoodLine2.Substring(0, 69) + "   0.0  4320.0", "line 2", "exactly 3 numbers");
        }

        [Test]
        public void Malformed_ZeroStep_Throws()
        {
            AssertThrowsMentioning(GoodLine1 + "\n" + GoodLine2.Substring(0, 69) + "   0.0  4320.0  0.0", "step minutes must be greater than 0");
        }

        [Test]
        public void Malformed_Line2Missing_Throws()
        {
            AssertThrowsMentioning("# c\n" + GoodLine1 + "\n", "line 2", "missing");
        }

        [Test]
        public void Malformed_CommentBetweenLine1AndLine2_Throws()
        {
            AssertThrowsMentioning(GoodLine1 + "\n# oops\n" + GoodLine2, "line 2", "expected TLE line 2");
        }

        [Test]
        public void Malformed_BlankLine_Throws()
        {
            AssertThrowsMentioning("\n" + GoodLine1 + "\n" + GoodLine2, "line 1", "expected a comment");
        }

        [Test]
        public void Malformed_CatalogNumbersDisagree_Throws()
        {
            string line2 = "2 00006" + GoodLine2.Substring(7);
            AssertThrowsMentioning(GoodLine1 + "\n" + line2, "line 2", "does not match");
        }

        [Test]
        public void Malformed_Eccentricity_Throws()
        {
            string line2 = GoodLine2.Substring(0, 26) + "18596x7" + GoodLine2.Substring(33);
            AssertThrowsMentioning(GoodLine1 + "\n" + line2, "line 2", "eccentricity");
        }

        [Test]
        public void Malformed_BStar_Throws()
        {
            string line1 = GoodLine1.Substring(0, 53) + " 2809-4 " + GoodLine1.Substring(61);
            AssertThrowsMentioning(line1 + "\n" + GoodLine2, "line 1", "BSTAR");
        }

        [Test]
        public void Malformed_BlankInclination_Throws()
        {
            string line2 = GoodLine2.Substring(0, 8) + "        " + GoodLine2.Substring(16);
            AssertThrowsMentioning(GoodLine1 + "\n" + line2, "line 2", "inclination", "blank");
        }

        [Test]
        public void Malformed_ZeroMeanMotion_Throws()
        {
            string line2 = GoodLine2.Substring(0, 52) + " 0.00000000" + GoodLine2.Substring(63);
            AssertThrowsMentioning(GoodLine1 + "\n" + line2, "line 2", "mean motion must be greater than 0");
        }

        [Test]
        public void Malformed_DayOfYear366InNonLeapYear_Throws()
        {
            string line1 = GoodLine1.Substring(0, 18) + "01366.50000000" + GoodLine1.Substring(32);
            AssertThrowsMentioning(line1 + "\n" + GoodLine2, "line 1", "day of year");
        }

        [Test]
        public void Malformed_ShortLine1_Throws()
        {
            AssertThrowsMentioning(GoodLine1.Substring(0, 60) + "\n" + GoodLine2, "line 1", "exactly 69 characters");
        }

        [Test]
        public void Empty_Throws()
        {
            AssertThrowsMentioning("# only a comment\n", "no TLE entries");
        }
    }
}
