// M1-03: Julian Date conversion (you write this one).
//
// Reminder: C# 9 only here. Use a block-scoped namespace `namespace X { ... }`,
// not the file-scoped `namespace X;` (that's C# 10).

using System;

namespace SatTracker.Core.Time
{
    public readonly struct JulianDate
    {
        private readonly double dayPart;
        private readonly double fracPart;

        public double JdAtMidnightDays => dayPart;

        public double DayFraction => fracPart;

        public readonly double GetDateTotal()
        {
            return CalculateFullJulianNumber();
        }

        public JulianDate(double day, double frac)
        {
            dayPart = day;
            fracPart = frac;
        }

        public static JulianDate FromUTC(DateTime dateTime)
        {
            dateTime = dateTime.Kind == DateTimeKind.Utc ? dateTime : throw new ArgumentException("Time Zone Must Be UTC!", nameof(dateTime));

            if (DateTime.Compare(dateTime, new DateTime(1900, 3, 1)) < 0 || DateTime.Compare(dateTime, new DateTime(2100, 3, 1, 0, 0, 0)) >= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(dateTime), dateTime, "Date is out of Range!");
            }

            double year = dateTime.Year;
            double month = dateTime.Month;
            double day = dateTime.Day;

            double julianDay = 367 * year - Math.Floor(7 * (year + Math.Floor((month + 9) / 12)) / 4) + Math.Floor(275 * month / 9) + day + 1721013.5;

            double julianFrac = dateTime.TimeOfDay.Ticks / (double)TimeSpan.TicksPerDay;

            return new JulianDate(julianDay, julianFrac);
        }

        private double CalculateFullJulianNumber()
        {
            return dayPart + fracPart;
        }
    }
}
