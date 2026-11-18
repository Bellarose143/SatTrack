// M1-04: Parse the ISS snapshot (CelesTrak GP JSON -> OrbitSnapshot).
//
// The hand-made JSON here is built with System.Text.Json (built into .NET 9), so the
// tests don't share a JSON library with the code under test.

using System.Text.Json.Nodes;
using SatTracker.Core.Snapshots;
using SatTracker.Core.Time;

namespace SatTracker.Core.Tests.Snapshots
{
    public class CelesTrakJsonParserTests
    {
        // Every field the parser requires, with plain hand-picked values.
        private static JsonObject ValidElement() => new JsonObject
        {
            ["OBJECT_NAME"] = "ISS (ZARYA)",
            ["OBJECT_ID"] = "1998-067A",
            ["EPOCH"] = "2026-11-15T23:34:56.789000",
            ["MEAN_MOTION"] = 15.5,
            ["ECCENTRICITY"] = 0.0004,
            ["INCLINATION"] = 51.6,
            ["RA_OF_ASC_NODE"] = 90.0,
            ["ARG_OF_PERICENTER"] = 180.0,
            ["MEAN_ANOMALY"] = 270.0,
            ["EPHEMERIS_TYPE"] = 0,
            ["CLASSIFICATION_TYPE"] = "U",
            ["NORAD_CAT_ID"] = 25544,
            ["ELEMENT_SET_NO"] = 999,
            ["REV_AT_EPOCH"] = 48000,
            ["BSTAR"] = 0.00015,
            ["MEAN_MOTION_DOT"] = 0.0001,
            ["MEAN_MOTION_DDOT"] = 0,
        };

        // CelesTrak serves a JSON array, even for a single satellite.
        private static string AsCelesTrakArray(JsonObject element) => new JsonArray(element).ToJsonString();

        private static OrbitSnapshot ParseOne(JsonObject element) =>
            CelesTrakJsonParser.Parse(AsCelesTrakArray(element));

        // ---------- Unit conversions ----------

        [Test]
        public void MeanMotion_15_5RevPerDay_IsRadPerMin()
        {
            var snapshot = ParseOne(ValidElement());

            Assert.That(snapshot.MeanMotionRadPerMin, Is.EqualTo(0.0676315085).Within(1e-10));
        }

        [Test]
        public void Inclination_51_6Deg_IsRad()
        {
            var snapshot = ParseOne(ValidElement());

            Assert.That(snapshot.InclinationRad, Is.EqualTo(0.9005898940).Within(1e-10));
        }

        [Test]
        public void OtherAngles_AreConvertedToRad()
        {
            var snapshot = ParseOne(ValidElement());

            Assert.That(snapshot.RaanRad, Is.EqualTo(Math.PI / 2).Within(1e-12));
            Assert.That(snapshot.ArgumentOfPerigeeRad, Is.EqualTo(Math.PI).Within(1e-12));
            Assert.That(snapshot.MeanAnomalyRad, Is.EqualTo(3 * Math.PI / 2).Within(1e-12));
        }

        [Test]
        public void UnitlessAndIdentityFields_PassThroughUnchanged()
        {
            var snapshot = ParseOne(ValidElement());

            Assert.That(snapshot.NoradCatalogId, Is.EqualTo(25544));
            Assert.That(snapshot.ObjectName, Is.EqualTo("ISS (ZARYA)"));
            Assert.That(snapshot.Eccentricity, Is.EqualTo(0.0004));
            Assert.That(snapshot.BStarPerEarthRadii, Is.EqualTo(0.00015));
        }

        // ---------- EPOCH: no "Z" suffix, must still be read as UTC ----------

        [TestCase("2026-11-15T00:00:00.000000", 0)]
        [TestCase("2026-11-15T05:30:00.000000", 5)]
        [TestCase("2026-11-15T12:00:00.000000", 12)]
        [TestCase("2026-11-15T23:34:56.789000", 23)]
        public void Epoch_HourMatchesString_RegardlessOfMachineTimeZone(string epoch, int expectedHour)
        {
            var element = ValidElement();
            element["EPOCH"] = epoch;

            var snapshot = ParseOne(element);

            // If the string were read as local time and shifted, the hour (and maybe the day) would move.
            Assert.That(snapshot.EpochUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(snapshot.EpochUtc.Hour, Is.EqualTo(expectedHour),
                $"Machine time zone: {TimeZoneInfo.Local.Id}");
            Assert.That(snapshot.EpochUtc.Day, Is.EqualTo(15));
        }

        [Test]
        public void Epoch_KeepsMicroseconds()
        {
            var element = ValidElement();
            element["EPOCH"] = "2026-11-15T12:00:00.123456";

            var snapshot = ParseOne(element);

            var expected = new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc).AddTicks(1234560);
            Assert.That(snapshot.EpochUtc, Is.EqualTo(expected));
        }

        [Test]
        public void Epoch_IsStoredAsJulianDate()
        {
            var snapshot = ParseOne(ValidElement());

            var expected = JulianDate.FromUTC(new DateTime(2026, 11, 15, 23, 34, 56, 789, DateTimeKind.Utc));
            Assert.That(snapshot.EpochJd.JdAtMidnightDays, Is.EqualTo(expected.JdAtMidnightDays));
            Assert.That(snapshot.EpochJd.DayFraction, Is.EqualTo(expected.DayFraction));
        }

        [Test]
        public void EpochJd_FollowsEpochUtc_AfterWith()
        {
            // EpochJd is computed from EpochUtc, not stored, so copying the record with a new
            // EpochUtc can't leave a stale Julian Date behind.
            var original = ParseOne(ValidElement());
            var newEpoch = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            var moved = original with { EpochUtc = newEpoch };

            Assert.That(moved.EpochJd.JdAtMidnightDays, Is.EqualTo(2451544.5));
            Assert.That(moved.EpochJd.DayFraction, Is.EqualTo(0.5));
        }

        [Test]
        public void Epoch_OutsideJulianDateRange_ThrowsAtParseTime()
        {
            var element = ValidElement();
            element["EPOCH"] = "1899-06-01T00:00:00.000000";

            var ex = Assert.Throws<FormatException>(() => ParseOne(element));
            Assert.That(ex!.Message, Does.Contain("EPOCH"));
        }

        [Test]
        public void Epoch_WithoutFraction_Parses()
        {
            var element = ValidElement();
            element["EPOCH"] = "2026-11-15T12:00:00";

            Assert.That(ParseOne(element).EpochUtc.Hour, Is.EqualTo(12));
        }

        [TestCase("not a date")]
        [TestCase("2026-11-15 12:00:00")]
        [TestCase("15/11/2026 12:00:00")]
        [TestCase("2026-11-15T12:00:00+05:00")]
        [TestCase("")]
        public void Epoch_BadFormat_Throws(string epoch)
        {
            var element = ValidElement();
            element["EPOCH"] = epoch;

            var ex = Assert.Throws<FormatException>(() => ParseOne(element));
            Assert.That(ex!.Message, Does.Contain("EPOCH"));
        }

        // ---------- Missing / invalid data: clear exception, never a silent zero ----------

        private static readonly string[] RequiredFields =
        {
            "OBJECT_NAME", "NORAD_CAT_ID", "EPOCH", "MEAN_MOTION", "ECCENTRICITY",
            "INCLINATION", "RA_OF_ASC_NODE", "ARG_OF_PERICENTER", "MEAN_ANOMALY", "BSTAR",
        };

        [TestCaseSource(nameof(RequiredFields))]
        public void MissingRequiredField_Throws_NamingTheField(string field)
        {
            var element = ValidElement();
            element.Remove(field);

            var ex = Assert.Throws<FormatException>(() => ParseOne(element));
            Assert.That(ex!.Message, Does.Contain(field));
        }

        [TestCaseSource(nameof(RequiredFields))]
        public void NullRequiredField_Throws_NamingTheField(string field)
        {
            var element = ValidElement();
            element[field] = null;

            var ex = Assert.Throws<FormatException>(() => ParseOne(element));
            Assert.That(ex!.Message, Does.Contain(field));
        }

        [Test]
        public void NumberSentAsString_Throws()
        {
            var element = ValidElement();
            element["MEAN_MOTION"] = "15.5";

            var ex = Assert.Throws<FormatException>(() => ParseOne(element));
            Assert.That(ex!.Message, Does.Contain("MEAN_MOTION"));
        }

        [Test]
        public void FractionalNoradId_Throws()
        {
            var element = ValidElement();
            element["NORAD_CAT_ID"] = 25544.5;

            var ex = Assert.Throws<FormatException>(() => ParseOne(element));
            Assert.That(ex!.Message, Does.Contain("NORAD_CAT_ID"));
        }

        [TestCase("MEAN_MOTION", 0.0)]
        [TestCase("MEAN_MOTION", -15.5)]
        [TestCase("ECCENTRICITY", -0.1)]
        [TestCase("ECCENTRICITY", 1.0)]
        [TestCase("INCLINATION", -1.0)]
        [TestCase("INCLINATION", 180.5)]
        public void ImpossibleOrbitValue_Throws(string field, double value)
        {
            var element = ValidElement();
            element[field] = value;

            var ex = Assert.Throws<FormatException>(() => ParseOne(element));
            Assert.That(ex!.Message, Does.Contain(field));
        }

        [Test]
        public void BlankObjectName_Throws()
        {
            var element = ValidElement();
            element["OBJECT_NAME"] = "   ";

            var ex = Assert.Throws<FormatException>(() => ParseOne(element));
            Assert.That(ex!.Message, Does.Contain("OBJECT_NAME"));
        }

        // ---------- Overall shape of the file ----------

        [Test]
        public void SingleObjectWithoutArray_Parses()
        {
            var snapshot = CelesTrakJsonParser.Parse(ValidElement().ToJsonString());

            Assert.That(snapshot.NoradCatalogId, Is.EqualTo(25544));
        }

        [Test]
        public void EmptyArray_Throws()
        {
            Assert.Throws<FormatException>(() => CelesTrakJsonParser.Parse("[]"));
        }

        [Test]
        public void ArrayWithTwoSatellites_Throws()
        {
            var json = new JsonArray(ValidElement(), ValidElement()).ToJsonString();

            Assert.Throws<FormatException>(() => CelesTrakJsonParser.Parse(json));
        }

        [TestCase("{ this is not json")]
        [TestCase("42")]
        [TestCase("\"hello\"")]
        public void NotASatelliteJson_Throws(string json)
        {
            Assert.Throws<FormatException>(() => CelesTrakJsonParser.Parse(json));
        }

        [Test]
        public void NullInput_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => CelesTrakJsonParser.Parse(null!));
        }

        // ---------- The real file ----------

        private static string FixturePath =>
            Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "iss-25544.json");

        [Test]
        public void RealIssFixture_Parses()
        {
            Assert.That(File.Exists(FixturePath), Is.True,
                "Missing fixture. Save CelesTrak's ISS JSON as SatTracker.Core.Tests/Fixtures/iss-25544.json " +
                "(https://celestrak.org/NORAD/elements/gp.php?CATNR=25544&FORMAT=json).");

            var snapshot = CelesTrakJsonParser.Parse(File.ReadAllText(FixturePath));

            Assert.That(snapshot.NoradCatalogId, Is.EqualTo(25544));
            Assert.That(snapshot.EpochUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
            // ISS does ~15.5 rev/day, so roughly 0.067 rad/min. Loose bounds: just "is it the ISS-ish".
            Assert.That(snapshot.MeanMotionRadPerMin, Is.InRange(0.065, 0.070));
            Assert.That(snapshot.InclinationRad, Is.EqualTo(0.9).Within(0.01));
        }

        [Test]
        public void RealIssFixture_MatchesSavedValues()
        {
            // Pinned to the file saved on 2026-11-17. If you replace the fixture, update these.
            var snapshot = CelesTrakJsonParser.Parse(File.ReadAllText(FixturePath));

            Assert.That(snapshot.ObjectName, Is.EqualTo("ISS (ZARYA)"));
            Assert.That(snapshot.EpochUtc,
                Is.EqualTo(new DateTime(2026, 11, 17, 20, 24, 46, DateTimeKind.Utc).AddTicks(1531520)));
            Assert.That(snapshot.MeanMotionRadPerMin,
                Is.EqualTo(15.49442652 * 2 * Math.PI / 1440).Within(1e-15));
            Assert.That(snapshot.InclinationRad, Is.EqualTo(51.6318 * Math.PI / 180).Within(1e-15));
            Assert.That(snapshot.Eccentricity, Is.EqualTo(0.0007364));
            Assert.That(snapshot.BStarPerEarthRadii, Is.EqualTo(0.00020179));
        }
    }
}
