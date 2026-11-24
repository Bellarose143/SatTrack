// M2-03: A few smoke tests for the SGP4 port. NOT the full verification against every official
// case and time step: that's the M2-05 harness.

using SatTracker.Core.Propagation;
using SatTracker.Core.Snapshots;
using SatTracker.Core.Tests.Sgp4Verification;
using SatTracker.Core.Units;

namespace SatTracker.Core.Tests.Propagation
{
    public class Sgp4SmokeTests
    {
        // The project's tolerances (M2-05). Never loosen them.
        private const double PositionToleranceKm = 1e-6;
        private const double VelocityToleranceKmPerSec = 1e-9;

        private static Sgp4VerificationCase OfficialCase(int catalogNumber)
        {
            return Sgp4VerificationData.LoadFixtures().Single(c => c.CatalogNumber == catalogNumber);
        }

        private static void AssertMatchesRow(Sgp4State state, Sgp4ExpectedRow row)
        {
            Assert.Multiple(() =>
            {
                Assert.That(state.PositionKm.X, Is.EqualTo(row.PositionKm.X).Within(PositionToleranceKm), "position X");
                Assert.That(state.PositionKm.Y, Is.EqualTo(row.PositionKm.Y).Within(PositionToleranceKm), "position Y");
                Assert.That(state.PositionKm.Z, Is.EqualTo(row.PositionKm.Z).Within(PositionToleranceKm), "position Z");
                Assert.That(state.VelocityKmPerSec.X, Is.EqualTo(row.VelocityKmPerSec.X).Within(VelocityToleranceKmPerSec), "velocity X");
                Assert.That(state.VelocityKmPerSec.Y, Is.EqualTo(row.VelocityKmPerSec.Y).Within(VelocityToleranceKmPerSec), "velocity Y");
                Assert.That(state.VelocityKmPerSec.Z, Is.EqualTo(row.VelocityKmPerSec.Z).Within(VelocityToleranceKmPerSec), "velocity Z");
            });
        }

        /// <summary>A made-up near-Earth snapshot for input-checking tests (roughly ISS-like).</summary>
        private static OrbitSnapshot HandMadeSnapshot()
        {
            return new OrbitSnapshot(
                NoradCatalogId: 99999,
                ObjectName: "HAND-MADE TEST",
                EpochUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                MeanMotionRadPerMin: UnitConversions.RevPerDayToRadPerMin(15.5),
                Eccentricity: 0.0005,
                InclinationRad: UnitConversions.DegreesToRadians(51.6),
                RaanRad: UnitConversions.DegreesToRadians(100.0),
                ArgumentOfPerigeeRad: UnitConversions.DegreesToRadians(90.0),
                MeanAnomalyRad: UnitConversions.DegreesToRadians(270.0),
                BStarPerEarthRadii: 0.0002);
        }

        // ---------- matches the official answers ----------

        [TestCase(0.0)]
        [TestCase(360.0)]
        public void Case00005_MatchesOfficialRow(double minutesSinceEpoch)
        {
            var c = OfficialCase(5);
            var row = c.Expected.Rows.Single(r => r.MinutesSinceEpoch == minutesSinceEpoch);

            var state = Sgp4.Initialize(c.Tle.Snapshot).Propagate(minutesSinceEpoch);

            Assert.That(state.MinutesSinceEpoch, Is.EqualTo(minutesSinceEpoch));
            AssertMatchesRow(state, row);
        }

        [Test]
        public void Propagate_HasNoSideEffects_SameAnswerInAnyOrder()
        {
            var satellite = Sgp4.Initialize(OfficialCase(5).Tle.Snapshot);

            var first = satellite.Propagate(360.0);
            satellite.Propagate(4320.0);
            satellite.Propagate(-1000.0);
            satellite.Propagate(0.0);
            var again = satellite.Propagate(360.0);

            // Exactly equal, not "close": nothing about the satellite changed between calls.
            Assert.That(again.PositionKm, Is.EqualTo(first.PositionKm));
            Assert.That(again.VelocityKmPerSec, Is.EqualTo(first.VelocityKmPerSec));
        }

        // ---------- errors SGP4 itself reports ----------

        [Test]
        public void Case22312_ReportsAnErrorAtItsOfficialErrorTime_AndNotJustBefore()
        {
            // 22312 is a rocket body caught in its last days before re-entry (drag term ndot ≈ 1).
            // The official run stops at 494.2028672 min. Vallado's sgp4 checks eccentricity BEFORE it
            // checks for decay, and by then drag has pushed the average eccentricity to about
            // -0.0013, past his -0.001 limit. So the error is code 1, not code 6 ("decayed"),
            // even though re-entry is the physical cause.
            var c = OfficialCase(22312);
            double errorAt = c.Sgp4ErrorAtMinutes!.Value;
            var satellite = Sgp4.Initialize(c.Tle.Snapshot);

            var lastRow = c.Expected.Rows[c.Expected.Rows.Count - 1];
            AssertMatchesRow(satellite.Propagate(lastRow.MinutesSinceEpoch), lastRow);

            var ex = Assert.Throws<Sgp4Exception>(() => satellite.Propagate(errorAt))!;
            Assert.Multiple(() =>
            {
                Assert.That(ex.Code, Is.EqualTo(Sgp4ErrorCode.MeanEccentricityOutOfRange));
                Assert.That((int)ex.Code, Is.EqualTo(1));
                Assert.That(ex.NoradCatalogId, Is.EqualTo(22312));
                Assert.That(ex.MinutesSinceEpoch, Is.EqualTo(errorAt));
                Assert.That(ex.DuringInitialization, Is.False);
                Assert.That(ex.Message, Does.Contain("error 1").And.Contain("22312"));
            });
        }

        [Test]
        public void Case28872_ReportsDecayed_AtItsOfficialErrorTime()
        {
            // The Minotaur rocket body: "Sub-orbital case ... lost in 50 minutes". Here the
            // distance from Earth's center really drops below one Earth radius: code 6.
            var c = OfficialCase(28872);
            var satellite = Sgp4.Initialize(c.Tle.Snapshot);

            var ex = Assert.Throws<Sgp4Exception>(() => satellite.Propagate(c.Sgp4ErrorAtMinutes!.Value))!;

            Assert.That(ex.Code, Is.EqualTo(Sgp4ErrorCode.Decayed));
            Assert.That((int)ex.Code, Is.EqualTo(6));
            Assert.That(ex.Message, Does.Contain("decayed"));
        }

        // ---------- deep space is refused ----------

        [Test]
        public void DeepSpaceCase11801_IsRejected()
        {
            // 11801 goes around in about 10.6 hours, way over 225 minutes.
            var snapshot = OfficialCase(11801).Tle.Snapshot;

            var ex = Assert.Throws<Sgp4DeepSpaceNotSupportedException>(() => Sgp4.Initialize(snapshot))!;

            Assert.That(ex.NoradCatalogId, Is.EqualTo(11801));
            Assert.That(ex.OrbitPeriodMinutes, Is.GreaterThanOrEqualTo(225.0));
            Assert.That(ex, Is.InstanceOf<NotSupportedException>());
        }

        [Test]
        public void DeepSpaceCheck_UsesTheUnKozaiedMeanMotion_LikeVallado()
        {
            // For an orbit over the equator, un-Kozai-ing makes the mean motion slightly smaller, so
            // the period slightly longer (about +0.1 min here). A published period of 224.95 min is
            // under 225, but the period Vallado actually checks is over 225, so it's deep space.
            var snapshot = HandMadeSnapshot() with
            {
                MeanMotionRadPerMin = 2.0 * Math.PI / 224.95,
                Eccentricity = 0.0,
                InclinationRad = 0.0,
            };

            var ex = Assert.Throws<Sgp4DeepSpaceNotSupportedException>(() => Sgp4.Initialize(snapshot))!;
            Assert.That(ex.OrbitPeriodMinutes, Is.GreaterThanOrEqualTo(225.0).And.LessThan(225.2));

            // A bit faster still: the un-Kozai'd period stays under 225, so it's near-Earth.
            Assert.DoesNotThrow(() => Sgp4.Initialize(snapshot with { MeanMotionRadPerMin = 2.0 * Math.PI / 224.8 }));
        }

        // ---------- invalid input throws ----------

        [Test]
        public void Initialize_NullSnapshot_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => Sgp4.Initialize(null!));
        }

        [Test]
        public void Initialize_EccentricityOfOne_Throws()
        {
            var snapshot = HandMadeSnapshot() with { Eccentricity = 1.0 };

            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => Sgp4.Initialize(snapshot))!;
            Assert.That(ex.Message, Does.Contain(nameof(OrbitSnapshot.Eccentricity)));
        }

        [Test]
        public void Initialize_ZeroMeanMotion_Throws()
        {
            var snapshot = HandMadeSnapshot() with { MeanMotionRadPerMin = 0.0 };

            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => Sgp4.Initialize(snapshot))!;
            Assert.That(ex.Message, Does.Contain(nameof(OrbitSnapshot.MeanMotionRadPerMin)));
        }

        [Test]
        public void Initialize_NaNValue_Throws()
        {
            var snapshot = HandMadeSnapshot() with { BStarPerEarthRadii = double.NaN };

            var ex = Assert.Throws<ArgumentException>(() => Sgp4.Initialize(snapshot))!;
            Assert.That(ex.Message, Does.Contain(nameof(OrbitSnapshot.BStarPerEarthRadii)));
        }

        [Test]
        public void Propagate_NaNTime_Throws()
        {
            var satellite = Sgp4.Initialize(HandMadeSnapshot());

            Assert.Throws<ArgumentOutOfRangeException>(() => satellite.Propagate(double.NaN));
        }
    }
}
