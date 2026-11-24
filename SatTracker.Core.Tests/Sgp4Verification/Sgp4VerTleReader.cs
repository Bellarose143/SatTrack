// M2-02: Reader for Vallado's SGP4 verification inputs (SGP4-VER.TLE).
//
// Test project only. SatTracker.Core never reads TLE text. It gets its orbit data from
// CelesTrak JSON. This reader exists so the official SGP4 test cases can be fed to Core.

using System.Globalization;
using System.Text.RegularExpressions;
using SatTracker.Core.Snapshots;
using SatTracker.Core.Time;
using SatTracker.Core.Units;

namespace SatTracker.Core.Tests.Sgp4Verification
{
    /// <summary>
    /// One test case from SGP4-VER.TLE: the satellite's orbit snapshot, plus the time window the
    /// official test program propagates it over (the three extra numbers at the end of line 2).
    /// </summary>
    /// <param name="Snapshot">The TLE converted exactly like <see cref="CelesTrakJsonParser"/> converts
    /// JSON: radians, radians per minute, UTC epoch.</param>
    /// <param name="StartMinutes">First time to propagate to, in minutes since the epoch.</param>
    /// <param name="StopMinutes">Last time to propagate to, in minutes since the epoch.</param>
    /// <param name="StepMinutes">Gap between propagation times, in minutes.</param>
    /// <param name="Description">The "#" comment lines just above the TLE in the file (what the case
    /// is testing), joined into one line. Can be empty.</param>
    /// <param name="Line1Number">1-based line number of the TLE's line 1 in the file.</param>
    public sealed record Sgp4VerTleEntry(
        OrbitSnapshot Snapshot,
        double StartMinutes,
        double StopMinutes,
        double StepMinutes,
        string Description,
        int Line1Number)
    {
        public int CatalogNumber => Snapshot.NoradCatalogId;

        /// <summary>
        /// Time for one lap, from the TLE's mean motion (one lap = 2π radians).
        /// SGP4's own "deep space" test (period of 225 minutes or more) uses a slightly adjusted
        /// mean motion, but no case in SGP4-VER.TLE is anywhere near the 225-minute line.
        /// </summary>
        public double PeriodMinutes => 2.0 * Math.PI / Snapshot.MeanMotionRadPerMin;
    }

    /// <summary>
    /// Reads SGP4-VER.TLE: pairs of "two-line element" (TLE) lines, the old fixed-column text format
    /// for orbit snapshots, with "#" comment lines in between. Each line 2 ends with three extra
    /// numbers (start, stop and step minutes) that only exist in this verification file.
    /// <para>
    /// Every field the snapshot needs is read from its fixed column range (1-based columns, as in the
    /// TLE format documentation) and converted once, here. Anything malformed throws
    /// <see cref="FormatException"/> naming the line and field. Only "#" comment lines are skipped.
    /// </para>
    /// <para>
    /// Fields SGP4 doesn't use are not read: classification, international designator, the
    /// mean-motion derivatives, ephemeris type, element set number, revolution number. Checksums
    /// are not verified either.
    /// </para>
    /// </summary>
    public static class Sgp4VerTleReader
    {
        public const string DefaultSourceName = "SGP4-VER.TLE";

        // A TLE line is 69 characters. Line 2 in SGP4-VER.TLE has the start/stop/step numbers after that.
        private const int TleLineLength = 69;

        // BSTAR is written in a squeezed scientific notation with an implied leading "0.":
        // " 28098-4" means +0.28098e-4, "-11606-4" means -0.11606e-4.
        private static readonly Regex BStarPattern = new Regex(@"^([ +-])(\d{5})([+-])(\d)$", RegexOptions.Compiled);
        private static readonly Regex DigitsOnly = new Regex(@"^\d+$", RegexOptions.Compiled);

        public static IReadOnlyList<Sgp4VerTleEntry> ReadFile(string path)
        {
            return Parse(File.ReadAllText(path), Path.GetFileName(path));
        }

        /// <param name="text">The whole file's contents (CRLF or LF line endings).</param>
        /// <param name="sourceName">Used in error messages only.</param>
        public static IReadOnlyList<Sgp4VerTleEntry> Parse(string text, string sourceName = DefaultSourceName)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));

            string[] lines = SplitLines(text);
            var entries = new List<Sgp4VerTleEntry>();
            var comments = new List<string>();

            int i = 0;
            while (i < lines.Length)
            {
                string line = lines[i];
                int lineNumber = i + 1;

                if (line.StartsWith("#", StringComparison.Ordinal))
                {
                    comments.Add(CommentText(line));
                    i++;
                    continue;
                }

                if (!line.StartsWith("1 ", StringComparison.Ordinal))
                    throw Error(sourceName, lineNumber, $"expected a comment (#) or TLE line 1 (starting \"1 \"), got \"{line}\"");

                if (i + 1 >= lines.Length)
                    throw Error(sourceName, lineNumber, "TLE line 1 is the last line of the file, so its line 2 is missing");

                string line2 = lines[i + 1];
                if (!line2.StartsWith("2 ", StringComparison.Ordinal))
                    throw Error(sourceName, lineNumber + 1, $"expected TLE line 2 (starting \"2 \") right after line 1, got \"{line2}\"");

                string description = string.Join(" ", comments.Where(c => c.Length > 0));
                entries.Add(ParseEntry(line, line2, lineNumber, description, sourceName));
                comments.Clear();
                i += 2;
            }

            if (entries.Count == 0)
                throw new FormatException($"{sourceName}: no TLE entries found.");

            return entries;
        }

        private static Sgp4VerTleEntry ParseEntry(string line1, string line2, int line1Number, string description, string source)
        {
            int line2Number = line1Number + 1;

            if (line1.Length != TleLineLength)
                throw Error(source, line1Number, $"TLE line 1 must be exactly {TleLineLength} characters, got {line1.Length}");
            if (line2.Length <= TleLineLength)
                throw Error(source, line2Number,
                    $"TLE line 2 must be {TleLineLength} characters followed by start, stop and step minutes, but it is only {line2.Length} characters");

            // ---- Line 1 ----
            int catalogNumber = ReadCatalogNumber(line1, line1Number, source);
            DateTime epochUtc = ReadEpochUtc(line1, line1Number, source);
            double bStar = ReadBStar(line1, line1Number, source);

            // ---- Line 2 ----
            int catalogNumber2 = ReadCatalogNumber(line2, line2Number, source);
            if (catalogNumber2 != catalogNumber)
                throw Error(source, line2Number, $"catalog number {catalogNumber2} does not match line 1's {catalogNumber}");

            double inclinationDeg = ReadUnsignedNumber(line2, 9, 16, "inclination (deg)", line2Number, source);
            if (inclinationDeg > 180)
                throw Error(source, line2Number, $"inclination must be between 0 and 180 degrees, got {Show(inclinationDeg)}");

            double raanDeg = ReadUnsignedNumber(line2, 18, 25, "RAAN (deg)", line2Number, source);
            double eccentricity = ReadEccentricity(line2, line2Number, source);
            double argumentOfPerigeeDeg = ReadUnsignedNumber(line2, 35, 42, "argument of perigee (deg)", line2Number, source);
            double meanAnomalyDeg = ReadUnsignedNumber(line2, 44, 51, "mean anomaly (deg)", line2Number, source);

            double meanMotionRevPerDay = ReadUnsignedNumber(line2, 53, 63, "mean motion (rev/day)", line2Number, source);
            if (meanMotionRevPerDay <= 0)
                throw Error(source, line2Number, $"mean motion must be greater than 0 rev/day, got {Show(meanMotionRevPerDay)}");

            (double start, double stop, double step) = ReadStartStopStep(line2, line2Number, source);

            // Same conversions, same field names as CelesTrakJsonParser: degrees -> radians,
            // rev/day -> rad/min, BSTAR and eccentricity used as-is.
            var snapshot = new OrbitSnapshot(
                NoradCatalogId: catalogNumber,
                ObjectName: $"SGP4-VER {catalogNumber:D5}", // The file has no names, so make it obvious where this one came from.
                EpochUtc: epochUtc,
                MeanMotionRadPerMin: UnitConversions.RevPerDayToRadPerMin(meanMotionRevPerDay),
                Eccentricity: eccentricity,
                InclinationRad: UnitConversions.DegreesToRadians(inclinationDeg),
                RaanRad: UnitConversions.DegreesToRadians(raanDeg),
                ArgumentOfPerigeeRad: UnitConversions.DegreesToRadians(argumentOfPerigeeDeg),
                MeanAnomalyRad: UnitConversions.DegreesToRadians(meanAnomalyDeg),
                BStarPerEarthRadii: bStar);

            return new Sgp4VerTleEntry(snapshot, start, stop, step, description, line1Number);
        }

        // Columns 3-7 on both lines.
        private static int ReadCatalogNumber(string line, int lineNumber, string source)
        {
            string text = Field(line, 3, 7);
            if (!DigitsOnly.IsMatch(text))
                throw Error(source, lineNumber, $"catalog number (columns 3-7) must be 5 digits, got \"{text}\"");
            return int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Columns 19-20: two-digit year. Columns 21-32: day of the year with a fraction, where
        /// day 1.0 is January 1st at 00:00 UTC. So 179.78495062 is 178.78495062 days after Jan 1st.
        /// </summary>
        private static DateTime ReadEpochUtc(string line1, int lineNumber, string source)
        {
            string yearText = Field(line1, 19, 20);
            if (!DigitsOnly.IsMatch(yearText))
                throw Error(source, lineNumber, $"epoch year (columns 19-20) must be 2 digits, got \"{yearText}\"");

            // TLEs only have two digits for the year. The convention: 57-99 are 1957-1999
            // (Sputnik launched in 1957), 00-56 are 2000-2056.
            int twoDigitYear = int.Parse(yearText, NumberStyles.None, CultureInfo.InvariantCulture);
            int year = twoDigitYear >= 57 ? 1900 + twoDigitYear : 2000 + twoDigitYear;

            string dayText = Field(line1, 21, 32);

            // decimal, not double: the 8 decimal places of the day convert to DateTime ticks
            // (100 ns each) with no rounding at all, because 1e-8 day = 8,640 ticks (0.864 ms) exactly.
            if (!decimal.TryParse(dayText, NumberStyles.AllowLeadingWhite | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out decimal dayOfYear))
                throw Error(source, lineNumber, $"epoch day of year (columns 21-32) must be a number like 179.78495062, got \"{dayText}\"");

            int daysInYear = DateTime.IsLeapYear(year) ? 366 : 365;
            if (dayOfYear < 1m || dayOfYear >= daysInYear + 1)
                throw Error(source, lineNumber, $"epoch day of year must be from 1 up to (not including) {daysInYear + 1} in {year}, got {dayText.Trim()}");

            decimal ticksAfterJan1 = (dayOfYear - 1m) * TimeSpan.TicksPerDay;
            if (ticksAfterJan1 != decimal.Truncate(ticksAfterJan1))
                throw Error(source, lineNumber,
                    $"epoch day of year {dayText.Trim()} is more precise than DateTime can hold (100 ns), and it won't be rounded");

            DateTime epochUtc = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks((long)ticksAfterJan1);

            // EpochJd isn't stored, but check the epoch converts now (like CelesTrakJsonParser does),
            // so an out-of-range date fails here with a clear message.
            try
            {
                JulianDate.FromUTC(epochUtc);
            }
            catch (ArgumentOutOfRangeException e)
            {
                throw new FormatException($"{source} line {lineNumber}: epoch {epochUtc:o} is outside the supported date range.", e);
            }

            return epochUtc;
        }

        /// <summary>Columns 54-61, e.g. " 28098-4" = +0.28098 × 10^-4, "-13525-3" = -0.13525 × 10^-3.</summary>
        private static double ReadBStar(string line1, int lineNumber, string source)
        {
            string text = Field(line1, 54, 61);
            Match m = BStarPattern.Match(text);
            if (!m.Success)
                throw Error(source, lineNumber, $"BSTAR (columns 54-61) must look like \" 28098-4\" or \"-11606-4\", got \"{text}\"");

            string sign = m.Groups[1].Value == "-" ? "-" : "";
            // Build the normal scientific-notation text and parse it once, e.g. "-0.11606e-4",
            // so no multiplication by a power of ten adds its own rounding.
            string scientific = $"{sign}0.{m.Groups[2].Value}e{m.Groups[3].Value}{m.Groups[4].Value}";
            return double.Parse(scientific, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        /// <summary>Columns 27-33: 7 digits with an implied "0." in front, so "1859667" = 0.1859667.</summary>
        private static double ReadEccentricity(string line2, int lineNumber, string source)
        {
            string text = Field(line2, 27, 33);
            if (text.Length != 7 || !DigitsOnly.IsMatch(text))
                throw Error(source, lineNumber, $"eccentricity (columns 27-33) must be 7 digits (implied leading \"0.\"), got \"{text}\"");
            return double.Parse("0." + text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        }

        private static double ReadUnsignedNumber(string line, int firstColumn, int lastColumn, string name, int lineNumber, string source)
        {
            string text = Field(line, firstColumn, lastColumn);
            if (string.IsNullOrWhiteSpace(text))
                throw Error(source, lineNumber, $"{name} (columns {firstColumn}-{lastColumn}) is blank");

            const NumberStyles style = NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite | NumberStyles.AllowDecimalPoint;
            if (!double.TryParse(text, style, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
                throw Error(source, lineNumber, $"{name} (columns {firstColumn}-{lastColumn}) must be a plain number, got \"{text}\"");
            return value;
        }

        /// <summary>
        /// The three numbers after column 69 of line 2, e.g. "0.00      4320.0        360.00":
        /// propagate from minute 0 to minute 4320, every 360 minutes.
        /// </summary>
        private static (double Start, double Stop, double Step) ReadStartStopStep(string line2, int lineNumber, string source)
        {
            string[] parts = line2.Substring(TleLineLength).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3)
                throw Error(source, lineNumber,
                    $"expected exactly 3 numbers (start, stop, step minutes) after column {TleLineLength}, got {parts.Length}: \"{line2.Substring(TleLineLength).Trim()}\"");

            const NumberStyles style = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
            var values = new double[3];
            string[] names = { "start minutes", "stop minutes", "step minutes" };
            for (int k = 0; k < 3; k++)
            {
                if (!double.TryParse(parts[k], style, CultureInfo.InvariantCulture, out values[k]) || !double.IsFinite(values[k]))
                    throw Error(source, lineNumber, $"{names[k]} must be a number, got \"{parts[k]}\"");
            }

            if (values[2] <= 0)
                throw Error(source, lineNumber, $"step minutes must be greater than 0, got {Show(values[2])}");
            if (values[1] < values[0])
                throw Error(source, lineNumber, $"stop minutes ({Show(values[1])}) is before start minutes ({Show(values[0])})");

            return (values[0], values[1], values[2]);
        }

        /// <summary>The text in 1-based columns first..last (inclusive), as the TLE format documents them.</summary>
        private static string Field(string line, int firstColumn, int lastColumn)
        {
            return line.Substring(firstColumn - 1, lastColumn - firstColumn + 1);
        }

        private static string CommentText(string line)
        {
            // "#   DELTA 1 DEB         # near earth normal drag equation" -> "DELTA 1 DEB # near earth normal drag equation"
            return Regex.Replace(line.Substring(1), @"\s+", " ").Trim();
        }

        internal static string[] SplitLines(string text)
        {
            string[] lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
            // A final newline leaves one empty string at the end. That's not a real blank line.
            if (lines.Length > 0 && lines[lines.Length - 1].Length == 0)
                lines = lines.Take(lines.Length - 1).ToArray();
            return lines;
        }

        private static FormatException Error(string source, int lineNumber, string problem)
        {
            return new FormatException($"{source} line {lineNumber}: {problem}.");
        }

        private static string Show(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
