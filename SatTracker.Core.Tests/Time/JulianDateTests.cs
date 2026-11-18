// M1-03: Julian Date tests (write these FIRST, then make them pass).
//
// Compare doubles with a tolerance:
//   Assert.That(actual, Is.EqualTo(expected).Within(1e-7));

using SatTracker.Core.Time;

namespace SatTracker.Core.Tests.Time
{
    public class JulianDateTests
    {
        const double ALLOWED_DEVIATION = 1e-7;

        [Test]
        public void Test_JulianDate_J2000()
        {
            DateTime dateTime = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            double ExpectedJulianDate = 2451545.0;
            JulianDate julianDate = JulianDate.FromUTC(dateTime);

            Assert.That(julianDate.GetDateTotal(), Is.EqualTo(ExpectedJulianDate).Within(ALLOWED_DEVIATION));
        }

        [Test]
        public void Test_JulianDate_1970()
        {
            DateTime dateTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            double ExpectedJulianDate = 2440587.5;
            JulianDate julianDate = JulianDate.FromUTC(dateTime);

            Assert.That(julianDate.GetDateTotal(), Is.EqualTo(ExpectedJulianDate).Within(ALLOWED_DEVIATION));
        }

        [Test]
        public void Test_JulianDate_1996Oct26_1420()
        {
            DateTime dateTime = new DateTime(1996, 10, 26, 14, 20, 0, DateTimeKind.Utc);
            double ExpectedJulianDate = 2450383.0972222;
            JulianDate julianDate = JulianDate.FromUTC(dateTime);

            Assert.That(julianDate.GetDateTotal(), Is.EqualTo(ExpectedJulianDate).Within(ALLOWED_DEVIATION));
        }

        [Test]
        public void Test_JulianDateTotal_J2000()
        {
            DateTime dateTime = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            double ExpectedJulianDay = 2451544.5;
            double ExpectedJulianFrac = 0.5;
            JulianDate julianDate = JulianDate.FromUTC(dateTime);

            Assert.That(julianDate.JdAtMidnightDays, Is.EqualTo(ExpectedJulianDay));
            Assert.That(julianDate.DayFraction, Is.EqualTo(ExpectedJulianFrac).Within(ALLOWED_DEVIATION));
        }

        [Test]
        public void Test_JulianDateTotal_1970()
        {
            DateTime dateTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            double ExpectedJulianDay = 2440587.5;
            double ExpectedJulianFrac = 0.0;
            JulianDate julianDate = JulianDate.FromUTC(dateTime);

            Assert.That(julianDate.JdAtMidnightDays, Is.EqualTo(ExpectedJulianDay));
            Assert.That(julianDate.DayFraction, Is.EqualTo(ExpectedJulianFrac).Within(ALLOWED_DEVIATION));
        }

        [Test]
        public void Test_JulianDateTotal_1996Oct26_1420()
        {
            DateTime dateTime = new DateTime(1996, 10, 26, 14, 20, 0, DateTimeKind.Utc);
            double ExpectedJulianDay = 2450382.5;
            double ExpectedJulianFrac = 0.5972222;
            JulianDate julianDate = JulianDate.FromUTC(dateTime);

            Assert.That(julianDate.JdAtMidnightDays, Is.EqualTo(ExpectedJulianDay));
            Assert.That(julianDate.DayFraction, Is.EqualTo(ExpectedJulianFrac).Within(ALLOWED_DEVIATION));
        }

        [Test]
        public void Test_JulianDate_OutOfRange_1776()
        {
            DateTime dateTime = new DateTime(1776, 10, 26, 14, 20, 0, DateTimeKind.Utc);
            Assert.Throws<ArgumentOutOfRangeException>(() => JulianDate.FromUTC(dateTime));
        }

        [Test]
        public void Test_JulianDate_OutOfRange_2776()
        {
            DateTime dateTime = new DateTime(2776, 10, 26, 14, 20, 0, DateTimeKind.Utc);
            Assert.Throws<ArgumentOutOfRangeException>(() => JulianDate.FromUTC(dateTime));
        }

        [Test]
        public void Test_JulianDate_OutOfRange_LowerBound()
        {
            DateTime dateTime = new DateTime(1900, 2, 28, 23, 59, 0, DateTimeKind.Utc);
            Assert.Throws<ArgumentOutOfRangeException>(() => JulianDate.FromUTC(dateTime));
        }

        [Test]
        public void Test_JulianDate_OutOfRange_UpperBound()
        {
            DateTime dateTime = new DateTime(2100, 3, 1, 0, 0, 0, DateTimeKind.Utc);
            Assert.Throws<ArgumentOutOfRangeException>(() => JulianDate.FromUTC(dateTime));
        }

        [Test]
        public void Test_JulianDate_InRange_2100Feb28()
        {
            DateTime dateTime = new DateTime(2100, 2, 28, 13, 0, 0, DateTimeKind.Utc);
            Assert.DoesNotThrow(() => JulianDate.FromUTC(dateTime));
        }

        [Test]
        public void Test_JulianDate_InRange_1900Mar1()
        {
            DateTime dateTime = new DateTime(1900, 3, 1, 0, 0, 0, DateTimeKind.Utc);
            Assert.DoesNotThrow(() => JulianDate.FromUTC(dateTime));
        }

        //   - Non-UTC DateTime -> throws? (only if you take a DateTime)
        [Test]
        public void Test_JulianDate_NonUTC_Local()
        {
            DateTime dateTime = new DateTime(1976, 10, 26, 14, 20, 0, DateTimeKind.Local);
            Assert.Throws<ArgumentException>(() => JulianDate.FromUTC(dateTime));
        }

        [Test]
        public void Test_JulianDate_NonUTC_Unspecified()
        {
            DateTime dateTime = new DateTime(1976, 10, 26, 14, 20, 0, DateTimeKind.Unspecified);
            Assert.Throws<ArgumentException>(() => JulianDate.FromUTC(dateTime));
        }
    }
}

