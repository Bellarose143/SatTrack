// M2-05: runs every official SGP4 test case through our SGP4 and compares it with tcppver.out.

using SatTracker.Core.Propagation;

namespace SatTracker.Core.Tests.Sgp4Verification
{
    public class Sgp4VerificationTests
    {
        private static IEnumerable<Sgp4VerificationCase> AllSatellites => Sgp4VerificationData.LoadFixtures();

        private const double PositionToleranceKm = 1e-6;
        private const double VelocityToleranceKmPerSec = 1e-9;

        [TestCaseSource(nameof(AllSatellites))]
        public void OfficialCase_MatchesTcppver(Sgp4VerificationCase testCase)
        {
            Sgp4Satellite satellite;

            try
            {
                satellite = Sgp4.Initialize(testCase.Tle.Snapshot);
            }
            catch (Sgp4DeepSpaceNotSupportedException ex)
            {
                Assert.Ignore(ex.Message);
                return;
            }

            for (int i = 0; i < testCase.Expected.Rows.Count; i++)
            {
                Sgp4State state = satellite.Propagate(testCase.PlannedMinutes[i]);
                var row = testCase.Expected.Rows[i];

                string where = $"Satellite {testCase.CatalogNumber}, minute {testCase.PlannedMinutes[i]:R}, tcppver.out line {row.LineNumber}";

                Assert.Multiple(() =>
                {
                    Assert.That(state.PositionKm.X, Is.EqualTo(row.PositionKm.X).Within(PositionToleranceKm), where);
                    Assert.That(state.PositionKm.Y, Is.EqualTo(row.PositionKm.Y).Within(PositionToleranceKm), where);
                    Assert.That(state.PositionKm.Z, Is.EqualTo(row.PositionKm.Z).Within(PositionToleranceKm), where);

                    Assert.That(state.VelocityKmPerSec.X, Is.EqualTo(row.VelocityKmPerSec.X).Within(VelocityToleranceKmPerSec), where);
                    Assert.That(state.VelocityKmPerSec.Y, Is.EqualTo(row.VelocityKmPerSec.Y).Within(VelocityToleranceKmPerSec), where);
                    Assert.That(state.VelocityKmPerSec.Z, Is.EqualTo(row.VelocityKmPerSec.Z).Within(VelocityToleranceKmPerSec), where);
                });
            }

            if (testCase.Sgp4ErrorAtMinutes is double errorAtMinutes)
            {
                Assert.Throws<Sgp4Exception>(() =>
                {
                    satellite.Propagate(errorAtMinutes);
                }, $"Satellite {testCase.CatalogNumber} should have reported an SGP4 error at minute {errorAtMinutes:R}");
            }
        }
    }
}
