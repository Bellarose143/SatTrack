// M2-02: pairing SGP4-VER.TLE with tcppver.out, and spotting the cases where SGP4 reported an error.

namespace SatTracker.Core.Tests.Sgp4Verification
{
    public class Sgp4VerificationDataTests
    {
        [Test]
        public void Fixtures_Load_As33PairedCases()
        {
            var cases = Sgp4VerificationData.LoadFixtures();

            Assert.That(cases, Has.Count.EqualTo(33));
            Assert.That(cases.Select(c => c.CatalogNumber), Is.EqualTo(cases.Select(c => c.Expected.CatalogNumber)));
        }

        [Test]
        public void Fixtures_ErrorCases_AreTheRunsThatEndEarly()
        {
            // Satellite → time of the planned step that has no row (the official SGP4 errored there).
            var errors = Sgp4VerificationData.LoadFixtures()
                .Where(c => c.ExpectsSgp4Error)
                .Select(c => (c.CatalogNumber, c.Sgp4ErrorAtMinutes!.Value))
                .ToList();

            Assert.That(errors, Is.EqualTo(new[]
            {
                (22312, 494.2028672),   // near-Earth, eccentricity out of range (code 1).
                (28350, 1560.0),        // near-Earth
                (28872, 55.0),          // near-Earth, sub-orbital, "lost in 50 minutes"
                (29141, 440.0),         // near-Earth, "lost in under 420 min"
                (33333, 25.0),          // deep space
                (33334, 1.0),           // deep space
                (20413, 1844345.0),     // deep space, the second 20413 entry
            }).Using<(int, double)>((a, b) => a.Item1 == b.Item1 && Math.Abs(a.Item2 - b.Item2) < 1e-9));
        }

        [Test]
        public void Fixtures_NearEarthErrorCase28872_HasRowsUpTo50Minutes()
        {
            var c = Sgp4VerificationData.LoadFixtures().Single(x => x.CatalogNumber == 28872);

            Assert.That(c.PlannedMinutes, Has.Count.EqualTo(13)); // 0, 5, 10, ..., 60
            Assert.That(c.Expected.Rows, Has.Count.EqualTo(11));  // 0 ... 50
            Assert.That(c.Expected.Rows[^1].MinutesSinceEpoch, Is.EqualTo(50.0));
            Assert.That(c.Sgp4ErrorAtMinutes, Is.EqualTo(55.0));
        }

        [Test]
        public void Fixtures_CompleteCase_HasNoError()
        {
            var c = Sgp4VerificationData.LoadFixtures().Single(x => x.CatalogNumber == 5);

            Assert.That(c.ExpectsSgp4Error, Is.False);
            Assert.That(c.Sgp4ErrorAtMinutes, Is.Null);
            Assert.That(c.Expected.Rows, Has.Count.EqualTo(c.PlannedMinutes.Count));
            Assert.That(c.ToString(), Is.EqualTo("SGP4-VER 00005 (0 to 4320 min)"));
        }

        // ---------- The C++ program's time steps ----------

        [Test]
        public void PlannedMinutes_StartAtZero_DoesNotRepeatZero()
        {
            Assert.That(Sgp4VerificationData.PlannedMinutes(0, 1440, 360), Is.EqualTo(new[] { 0.0, 360, 720, 1080, 1440 }));
        }

        [Test]
        public void PlannedMinutes_StartNotZero_StillBeginsWithZero_AndLastStepLandsOnStop()
        {
            // 04632 in SGP4-VER.TLE: start -5184, stop -4896, step 120.
            Assert.That(Sgp4VerificationData.PlannedMinutes(-5184, -4896, 120),
                Is.EqualTo(new[] { 0.0, -5184, -5064, -4944, -4896 }));
        }

        // ---------- The two files must belong together ----------

        private const string Line1 = "1 00005U 58002B   00179.78495062  .00000023  00000-0  28098-4 0  4753";
        private const string Line2 = "2 00005  34.2682 348.7242 1859667 331.7664  19.3264 10.82419157413667     0.00       720.0        360.00";
        private const string Row0 = "       0.00000000    7022.46529266   -1400.08296755       0.03995155  1.893841015  6.405893759  4.534807250";
        private const string Row360 = "     360.00000000   -7154.03120202   -3783.17682504   -3536.19412294  4.741887409 -4.151817765 -2.093935425";
        private const string Row720 = "     720.00000000   -7134.59340119    6531.68641334    3260.27186483 -4.113793027 -2.911922039 -2.557327851";

        private static IReadOnlyList<Sgp4VerificationCase> Combine(string tle, string output) =>
            Sgp4VerificationData.Combine(Sgp4VerTleReader.Parse(tle), TcppverReader.Parse(output));

        [Test]
        public void HandMade_FullRun_HasNoError_ShortRun_ErrorsAtNextStep()
        {
            string tle = Line1 + "\n" + Line2;

            Assert.That(Combine(tle, $"5 xx\n{Row0}\n{Row360}\n{Row720}").Single().ExpectsSgp4Error, Is.False);
            Assert.That(Combine(tle, $"5 xx\n{Row0}\n{Row360}").Single().Sgp4ErrorAtMinutes, Is.EqualTo(720.0));
        }

        [Test]
        public void HandMade_DifferentSatelliteOrder_Throws()
        {
            var ex = Assert.Throws<InvalidDataException>(() => Combine(Line1 + "\n" + Line2, $"6 xx\n{Row0}"));
            Assert.That(ex!.Message, Does.Contain("satellite 6"));
        }

        [Test]
        public void HandMade_DifferentCounts_Throws()
        {
            Assert.Throws<InvalidDataException>(() => Combine(Line1 + "\n" + Line2, $"5 xx\n{Row0}\n5 xx\n{Row0}"));
        }

        [Test]
        public void HandMade_RowAtUnplannedTime_Throws()
        {
            var ex = Assert.Throws<InvalidDataException>(() => Combine(Line1 + "\n" + Line2, $"5 xx\n{Row0}\n{Row720}"));
            Assert.That(ex!.Message, Does.Contain("line 3"));
        }

        [Test]
        public void HandMade_TooManyRows_Throws()
        {
            Assert.Throws<InvalidDataException>(() => Combine(Line1 + "\n" + Line2, $"5 xx\n{Row0}\n{Row360}\n{Row720}\n{Row720}"));
        }
    }
}
