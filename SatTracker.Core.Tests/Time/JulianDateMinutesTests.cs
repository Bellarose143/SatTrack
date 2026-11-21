// M1-05: Minutes since snapshot tests (write these FIRST, then make them pass).
//
// Build every DateTime with DateTimeKind.Utc, or FromUTC will throw.
// Compare with a tolerance, actual first:
//   Assert.That(actual, Is.EqualTo(expected).Within(1e-6));

using SatTracker.Core.Time;
using SatTracker.Core.Units;

namespace SatTracker.Core.Tests.Time
{
    public class JulianDateMinutesTests
    {
        private const double ALLOWED_DEVIATION = 1e-7;

        [Test]
        public void Test_JulianDate_MinutesFrom_Epoch_After()
        {
            var epochUtc = new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);
            var targetUtc = new DateTime(2026, 11, 15, 13, 30, 0, DateTimeKind.Utc);
            double expectedMinutes = 90.0;

            Assert.That(JulianDate.FromUTC(targetUtc).MinutesSince(JulianDate.FromUTC(epochUtc)), Is.EqualTo(expectedMinutes).Within(ALLOWED_DEVIATION));
        }

        [Test]
        public void Test_JulianDate_MinutesFrom_Epoch_Before()
        {
            var epochUtc = new DateTime(2026, 11, 15, 13, 30, 0, DateTimeKind.Utc);
            var targetUtc = new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);
            double expectedMinutes = -90.0;

            Assert.That(JulianDate.FromUTC(targetUtc).MinutesSince(JulianDate.FromUTC(epochUtc)), Is.EqualTo(expectedMinutes).Within(ALLOWED_DEVIATION));
        }

        [Test]
        public void Test_JulianDate_MinutesFrom_Epoch_CrossMidnight_After()
        {
            var epochUtc = new DateTime(2026, 11, 15, 23, 0, 0, DateTimeKind.Utc);
            var targetUtc = new DateTime(2026, 11, 16, 0, 30, 0, DateTimeKind.Utc);
            double expectedMinutes = 90.0;

            Assert.That(JulianDate.FromUTC(targetUtc).MinutesSince(JulianDate.FromUTC(epochUtc)), Is.EqualTo(expectedMinutes).Within(ALLOWED_DEVIATION));
        }

        [Test]
        public void Test_JulianDate_MinutesFrom_Epoch_CrossMidnight_Before()
        {
            var epochUtc = new DateTime(2026, 11, 16, 0, 30, 0, DateTimeKind.Utc);
            var targetUtc = new DateTime(2026, 11, 15, 23, 0, 0, DateTimeKind.Utc);
            double expectedMinutes = -90.0;

            Assert.That(JulianDate.FromUTC(targetUtc).MinutesSince(JulianDate.FromUTC(epochUtc)), Is.EqualTo(expectedMinutes).Within(ALLOWED_DEVIATION));
        }

        [Test]
        public void Test_JulianDate_MinutesFrom_Epoch_OneMinuteAfter()
        {
            var epochUtc = new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);
            var targetUtc = new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc).AddMilliseconds(1);
            double expectedMinutes = 1.0 / UnitConversions.MillisecondsPerMinute;

            Assert.That(JulianDate.FromUTC(targetUtc).MinutesSince(JulianDate.FromUTC(epochUtc)), Is.EqualTo(expectedMinutes).Within(1e-9));
        }

        [Test]
        public void Test_JulianDate_MinutesFrom_Epoch_After_DateTime()
        {
            var epochUtc = new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);
            var targetUtc = new DateTime(2026, 11, 15, 13, 30, 0, DateTimeKind.Utc);
            double expectedMinutes = 90.0;

            Assert.That(JulianDate.FromUTC(targetUtc).MinutesSince(epochUtc), Is.EqualTo(expectedMinutes).Within(ALLOWED_DEVIATION));
        }
    }
}
