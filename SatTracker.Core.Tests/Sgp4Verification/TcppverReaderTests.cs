// M2-02: tcppver.out reader tests (the real file plus hand-made bad lines).

namespace SatTracker.Core.Tests.Sgp4Verification
{
    public class TcppverReaderTests
    {
        private static Sgp4ExpectedOutput ReadRealFile() =>
            TcppverReader.ReadFile(Path.Combine(Sgp4VerificationData.FixtureDirectory, "tcppver.out"));

        [Test]
        public void RealFile_Has33Runs_For32Satellites()
        {
            var output = ReadRealFile();

            Assert.That(output.Runs, Has.Count.EqualTo(33));
            Assert.That(output.RunsByCatalogNumber, Has.Count.EqualTo(32));
            Assert.That(output.RunsByCatalogNumber[20413], Has.Count.EqualTo(2));
            Assert.That(output.Runs.Sum(r => r.Rows.Count), Is.EqualTo(667));
        }

        [Test]
        public void RealFile_Satellite5_KnownRow_At360Minutes()
        {
            // tcppver.out line 3:
            //      360.00000000   -7154.03120202   -3783.17682504   -3536.19412294  4.741887409 -4.151817765 -2.093935425 ...
            var run = ReadRealFile().RunsByCatalogNumber[5].Single();
            var row = run.Rows[1];

            Assert.That(run.Rows, Has.Count.EqualTo(13));
            Assert.That(row.LineNumber, Is.EqualTo(3));
            Assert.That(row.MinutesSinceEpoch, Is.EqualTo(360.0));
            Assert.That(row.PositionKm, Is.EqualTo((-7154.03120202, -3783.17682504, -3536.19412294)));
            Assert.That(row.VelocityKmPerSec, Is.EqualTo((4.741887409, -4.151817765, -2.093935425)));
        }

        [Test]
        public void RealFile_EveryRunStartsWithTheMinuteZeroRow()
        {
            Assert.That(ReadRealFile().Runs.Select(r => r.Rows[0].MinutesSinceEpoch), Is.All.EqualTo(0.0));
        }

        [Test]
        public void RealFile_RowWithSpacePaddedClock_IsParsed()
        {
            // 9998's first timed row ends "2005  5 27 19: 3:37.089777": the minutes are space-padded.
            var row = ReadRealFile().RunsByCatalogNumber[9998].Single().Rows[1];

            Assert.That(row.MinutesSinceEpoch, Is.EqualTo(-1440.0));
            Assert.That(row.PositionKm.X, Is.EqualTo(-11362.18265118));
            Assert.That(row.VelocityKmPerSec.Z, Is.EqualTo(0.267510059));
        }

        // ---------- Hand-made input ----------

        private const string Row0 = "       0.00000000    7022.46529266   -1400.08296755       0.03995155  1.893841015  6.405893759  4.534807250";
        private const string Row360 = "     360.00000000   -7154.03120202   -3783.17682504   -3536.19412294  4.741887409 -4.151817765 -2.093935425    8635.341424 0.185684   34.26805  347.97998  332.85746  252.46796  273.52819  2000  6 28  0:50:19.733571";

        [Test]
        public void HandMade_CommentLinesAreSkipped()
        {
            var output = TcppverReader.Parse("# hello\n5 xx\n" + Row0 + "\n# between rows\n" + Row360 + "\n");

            Assert.That(output.Runs.Single().Rows, Has.Count.EqualTo(2));
        }

        private static void AssertThrowsMentioning(string text, params string[] fragments)
        {
            var ex = Assert.Throws<FormatException>(() => TcppverReader.Parse(text));
            foreach (string fragment in fragments)
                Assert.That(ex!.Message, Does.Contain(fragment));
        }

        [Test]
        public void Malformed_RowBeforeAnyHeader_Throws()
        {
            AssertThrowsMentioning(Row0 + "\n", "line 1", "before the first");
        }

        [Test]
        public void Malformed_HeaderWithNoRows_Throws()
        {
            AssertThrowsMentioning("5 xx\n6 xx\n" + Row0, "line 1", "no rows");
        }

        [Test]
        public void Malformed_RowWithMissingVelocity_Throws()
        {
            AssertThrowsMentioning("5 xx\n       0.00000000    7022.46529266   -1400.08296755       0.03995155  1.893841015  6.405893759\n", "line 2");
        }

        [Test]
        public void Malformed_RowWithBrokenDate_Throws()
        {
            AssertThrowsMentioning("5 xx\n" + Row0 + "\n" + Row360.Replace("2000  6 28", "2000  6"), "line 3");
        }

        [Test]
        public void Malformed_NonNumber_Throws()
        {
            AssertThrowsMentioning("5 xx\n" + Row0.Replace("7022.46529266", "7022.4652926x"), "line 2");
        }

        [Test]
        public void Malformed_BlankLine_Throws()
        {
            AssertThrowsMentioning("5 xx\n" + Row0 + "\n\n" + Row360, "line 3");
        }

        [Test]
        public void Empty_Throws()
        {
            AssertThrowsMentioning("", "no satellites");
        }
    }
}
