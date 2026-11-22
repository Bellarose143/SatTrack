// M2-02: Reader for Vallado's SGP4 expected answers (tcppver.out).
//
// Test project only. See Fixtures/Sgp4Verification/SOURCES.md for where the file came from.

using System.Globalization;
using System.Text.RegularExpressions;

namespace SatTracker.Core.Tests.Sgp4Verification
{
    /// <summary>
    /// One expected answer: where the official C++ SGP4 put the satellite at one moment.
    /// Coordinates are TEME, the "space" frame SGP4 works in (Earth's center, not rotating with Earth).
    /// </summary>
    /// <param name="MinutesSinceEpoch">Printed with 8 decimal places.</param>
    /// <param name="PositionKm">Printed with 8 decimal places (1e-8 km = 0.01 mm).</param>
    /// <param name="VelocityKmPerSec">Printed with 9 decimal places (1e-9 km/s = 1 micrometre per second).</param>
    /// <param name="LineNumber">1-based line in tcppver.out, for pointing at a failing row.</param>
    public sealed record Sgp4ExpectedRow(
        double MinutesSinceEpoch,
        (double X, double Y, double Z) PositionKm,
        (double X, double Y, double Z) VelocityKmPerSec,
        int LineNumber);

    /// <summary>
    /// All the rows the official program printed for one test case (one TLE entry), in file order.
    /// </summary>
    /// <param name="CatalogNumber">From the "NNNNN xx" header line.</param>
    /// <param name="Rows">Always starts with the minute-0 row.</param>
    /// <param name="HeaderLineNumber">1-based line of the "NNNNN xx" header.</param>
    public sealed record Sgp4ExpectedRun(int CatalogNumber, IReadOnlyList<Sgp4ExpectedRow> Rows, int HeaderLineNumber);

    /// <summary>The whole of tcppver.out.</summary>
    public sealed class Sgp4ExpectedOutput
    {
        public Sgp4ExpectedOutput(IReadOnlyList<Sgp4ExpectedRun> runs)
        {
            Runs = runs;
            RunsByCatalogNumber = runs
                .GroupBy(r => r.CatalogNumber)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<Sgp4ExpectedRun>)g.ToList());
        }

        /// <summary>One run per TLE entry, in the same order as SGP4-VER.TLE.</summary>
        public IReadOnlyList<Sgp4ExpectedRun> Runs { get; }

        /// <summary>
        /// Catalog number → its runs. Almost every satellite has exactly one run; 20413 has two
        /// (SGP4-VER.TLE lists it twice, with different time windows), so the value is a list of runs
        /// rather than one merged list of rows that would mix the two windows.
        /// </summary>
        public IReadOnlyDictionary<int, IReadOnlyList<Sgp4ExpectedRun>> RunsByCatalogNumber { get; }
    }

    /// <summary>
    /// Reads tcppver.out, the output of Vallado's C++ test program for every case in SGP4-VER.TLE.
    /// <para>The format, one block per TLE entry:</para>
    /// <code>
    /// 5 xx                                                    ← header: catalog number + " xx"
    ///        0.00000000    7022.46529266 ...  4.534807250     ← minute 0: time, x y z (km), vx vy vz (km/s)
    ///      360.00000000   -7154.03120202 ... -2.093935425    8635.341424 0.185684 ... 2000  6 28  0:50:19.733571
    /// </code>
    /// Every row after the first also has 7 rounded orbit-shape numbers (semi-major axis km,
    /// eccentricity, inclination, RAAN, argument of perigee, true anomaly, mean anomaly, in degrees)
    /// and the UTC calendar date/time. Those are checked for shape but not kept: they're rounded
    /// to 5-6 decimals and SGP4 doesn't output them.
    /// <para>
    /// SGP4 errors (e.g. "the satellite has decayed") are NOT written in this file. The C++
    /// program prints them to the console and stops that satellite, so in the file an error just
    /// looks like the block ending early. <see cref="Sgp4VerificationData"/> works out which
    /// blocks ended early by comparing against the TLE's start/stop/step.
    /// </para>
    /// Malformed lines throw <see cref="FormatException"/> naming the line. Lines starting with
    /// "#" are skipped as comments (the real file has none).
    /// </summary>
    public static class TcppverReader
    {
        public const string DefaultSourceName = "tcppver.out";

        private const string Num = @"-?\d+\.\d+";

        private static readonly Regex HeaderPattern = new Regex(@"^\s*(\d+) xx\s*$", RegexOptions.Compiled);

        // 7 numbers: time, position x y z, velocity x y z. Then, optionally, 7 orbit-shape numbers
        // and a date like "2000  6 28  0:50:19.733571" (hours/minutes/seconds can be space-padded: "19: 3: 7.08").
        private static readonly Regex RowPattern = new Regex(
            $@"^\s*(?<t>{Num})\s+(?<rx>{Num})\s+(?<ry>{Num})\s+(?<rz>{Num})\s+(?<vx>{Num})\s+(?<vy>{Num})\s+(?<vz>{Num})" +
            $@"(?<extra>(\s+{Num}){{7}}\s+\d{{4}}\s+\d{{1,2}}\s+\d{{1,2}}\s+\d{{1,2}}:\s*\d{{1,2}}:\s*\d{{1,2}}\.\d+)?\s*$",
            RegexOptions.Compiled);

        public static Sgp4ExpectedOutput ReadFile(string path)
        {
            return Parse(File.ReadAllText(path), Path.GetFileName(path));
        }

        /// <param name="text">The whole file's contents (CRLF or LF line endings).</param>
        /// <param name="sourceName">Used in error messages only.</param>
        public static Sgp4ExpectedOutput Parse(string text, string sourceName = DefaultSourceName)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));

            string[] lines = Sgp4VerTleReader.SplitLines(text);
            var runs = new List<Sgp4ExpectedRun>();

            int currentCatalog = -1;
            int currentHeaderLine = 0;
            List<Sgp4ExpectedRow>? currentRows = null;

            void FinishRun()
            {
                if (currentRows == null) return;
                if (currentRows.Count == 0)
                    throw Error(sourceName, currentHeaderLine, $"satellite {currentCatalog} has a header but no rows (expected at least the minute-0 row)");
                runs.Add(new Sgp4ExpectedRun(currentCatalog, currentRows, currentHeaderLine));
            }

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                int lineNumber = i + 1;

                if (line.StartsWith("#", StringComparison.Ordinal))
                    continue;

                Match header = HeaderPattern.Match(line);
                if (header.Success)
                {
                    FinishRun();
                    if (!int.TryParse(header.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out currentCatalog))
                        throw Error(sourceName, lineNumber, $"catalog number \"{header.Groups[1].Value}\" is too large");
                    currentHeaderLine = lineNumber;
                    currentRows = new List<Sgp4ExpectedRow>();
                    continue;
                }

                Match row = RowPattern.Match(line);
                if (!row.Success)
                    throw Error(sourceName, lineNumber, $"not a \"NNNNN xx\" header or a data row: \"{line}\"");
                if (currentRows == null)
                    throw Error(sourceName, lineNumber, "data row before the first \"NNNNN xx\" header");

                currentRows.Add(new Sgp4ExpectedRow(
                    MinutesSinceEpoch: Number(row, "t", sourceName, lineNumber),
                    PositionKm: (Number(row, "rx", sourceName, lineNumber), Number(row, "ry", sourceName, lineNumber), Number(row, "rz", sourceName, lineNumber)),
                    VelocityKmPerSec: (Number(row, "vx", sourceName, lineNumber), Number(row, "vy", sourceName, lineNumber), Number(row, "vz", sourceName, lineNumber)),
                    LineNumber: lineNumber));
            }

            FinishRun();

            if (runs.Count == 0)
                throw new FormatException($"{sourceName}: no satellites found.");

            return new Sgp4ExpectedOutput(runs);
        }

        private static double Number(Match row, string group, string source, int lineNumber)
        {
            // The pattern already guarantees "-?digits.digits", so parsing can't fail; only an
            // absurdly long number could overflow to infinity.
            string text = row.Groups[group].Value;
            double value = double.Parse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
            if (!double.IsFinite(value))
                throw Error(source, lineNumber, $"number \"{text}\" is too large");
            return value;
        }

        private static FormatException Error(string source, int lineNumber, string problem)
        {
            return new FormatException($"{source} line {lineNumber}: {problem}.");
        }
    }
}
