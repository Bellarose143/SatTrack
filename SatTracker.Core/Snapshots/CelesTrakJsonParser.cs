using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SatTracker.Core.Time;
using SatTracker.Core.Units;

namespace SatTracker.Core.Snapshots
{
    /// <summary>
    /// Reads CelesTrak's "GP" JSON (FORMAT=json) for ONE satellite into an <see cref="OrbitSnapshot"/>.
    /// This is the library's entry point for orbit data, so all unit conversion happens here:
    /// degrees -> radians, revolutions/day -> radians/minute, EPOCH text -> UTC.
    /// Anything missing or impossible throws <see cref="FormatException"/> naming the field.
    /// </summary>
    public static class CelesTrakJsonParser
    {
        // CelesTrak writes EPOCH like "2026-11-15T12:34:56.789000": no "Z", no offset.
        // We only accept these exact shapes, so nothing is guessed from the machine's settings.
        private static readonly string[] EpochFormats =
        {
            "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF",
            "yyyy-MM-dd'T'HH:mm:ss",
            "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
            "yyyy-MM-dd'T'HH:mm:ss'Z'",
        };

        /// <param name="json">The file contents: a JSON array with exactly one satellite (what
        /// CelesTrak returns for a CATNR query), or that single object on its own.</param>
        public static OrbitSnapshot Parse(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));

            JObject element = SingleElement(ReadJson(json));

            double meanMotionRevPerDay = ReadNumber(element, "MEAN_MOTION");
            if (meanMotionRevPerDay <= 0)
                throw Invalid("MEAN_MOTION", $"must be greater than 0 rev/day, got {Show(meanMotionRevPerDay)}");

            double eccentricity = ReadNumber(element, "ECCENTRICITY");
            if (eccentricity < 0 || eccentricity >= 1)
                throw Invalid("ECCENTRICITY", $"must be in [0, 1) for an orbit that comes back around, got {Show(eccentricity)}");

            double inclinationDeg = ReadNumber(element, "INCLINATION");
            if (inclinationDeg < 0 || inclinationDeg > 180)
                throw Invalid("INCLINATION", $"must be between 0 and 180 degrees, got {Show(inclinationDeg)}");

            DateTime epochUtc = ReadEpochUtc(element);

            // EpochJd isn't stored, but check the epoch converts now, so a bad date fails
            // here with a clear parse error instead of later, the first time EpochJd is read.
            EnsureEpochConvertsToJulianDate(epochUtc);

            return new OrbitSnapshot(
                NoradCatalogId: ReadInt(element, "NORAD_CAT_ID"),
                ObjectName: ReadString(element, "OBJECT_NAME"),
                EpochUtc: epochUtc,
                MeanMotionRadPerMin: UnitConversions.RevPerDayToRadPerMin(meanMotionRevPerDay),
                Eccentricity: eccentricity,
                InclinationRad: UnitConversions.DegreesToRadians(inclinationDeg),
                RaanRad: UnitConversions.DegreesToRadians(ReadNumber(element, "RA_OF_ASC_NODE")),
                ArgumentOfPerigeeRad: UnitConversions.DegreesToRadians(ReadNumber(element, "ARG_OF_PERICENTER")),
                MeanAnomalyRad: UnitConversions.DegreesToRadians(ReadNumber(element, "MEAN_ANOMALY")),
                BStarPerEarthRadii: ReadNumber(element, "BSTAR"));
        }

        private static JToken ReadJson(string json)
        {
            try
            {
                using (var reader = new JsonTextReader(new StringReader(json)))
                {
                    // IMPORTANT: Newtonsoft's default turns date-looking strings into DateTime
                    // using the machine's local time zone. Keep EPOCH as raw text; we parse it ourselves.
                    reader.DateParseHandling = DateParseHandling.None;
                    reader.FloatParseHandling = FloatParseHandling.Double;
                    reader.Culture = CultureInfo.InvariantCulture;

                    JToken root = JToken.ReadFrom(reader);
                    if (reader.Read())
                        throw new FormatException("CelesTrak JSON: unexpected content after the end of the JSON.");
                    return root;
                }
            }
            catch (JsonException e)
            {
                throw new FormatException($"CelesTrak JSON: not valid JSON ({e.Message})", e);
            }
        }

        private static JObject SingleElement(JToken root)
        {
            if (root is JObject single) return single;

            if (root is JArray array)
            {
                if (array.Count != 1)
                    throw new FormatException(
                        $"CelesTrak JSON: expected exactly 1 satellite in the array, found {array.Count}.");
                if (array[0] is JObject only) return only;
                throw new FormatException("CelesTrak JSON: the array's item is not a JSON object.");
            }

            throw new FormatException($"CelesTrak JSON: expected an array or object, got {root.Type}.");
        }

        private static JToken Required(JObject element, string field)
        {
            if (!element.TryGetValue(field, StringComparison.Ordinal, out JToken token))
                throw Invalid(field, "is missing");
            if (token.Type == JTokenType.Null)
                throw Invalid(field, "is null");
            return token;
        }

        private static double ReadNumber(JObject element, string field)
        {
            JToken token = Required(element, field);
            if (token.Type != JTokenType.Float && token.Type != JTokenType.Integer)
                throw Invalid(field, $"must be a JSON number, got {token.Type} ({token.ToString(Formatting.None)})");

            double value = token.Value<double>();
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw Invalid(field, $"must be a finite number, got {Show(value)}");
            return value;
        }

        private static int ReadInt(JObject element, string field)
        {
            JToken token = Required(element, field);
            if (token.Type != JTokenType.Integer)
                throw Invalid(field, $"must be a whole JSON number, got {token.Type} ({token.ToString(Formatting.None)})");

            try
            {
                return token.Value<int>();
            }
            catch (OverflowException e)
            {
                throw new FormatException($"CelesTrak JSON: field '{field}' is too large for an int.", e);
            }
        }

        private static string ReadString(JObject element, string field)
        {
            JToken token = Required(element, field);
            if (token.Type != JTokenType.String)
                throw Invalid(field, $"must be a JSON string, got {token.Type}");

            string value = token.Value<string>();
            if (string.IsNullOrWhiteSpace(value))
                throw Invalid(field, "is blank");
            return value;
        }

        private static DateTime ReadEpochUtc(JObject element)
        {
            string text = ReadString(element, "EPOCH");

            // AssumeUniversal: "this text has no zone marker; treat it as UTC" (not local time).
            // AdjustToUniversal: "and hand back a DateTime with Kind = Utc, with no shifting".
            // Together they make the parsed hour equal the hour in the string on every machine.
            const DateTimeStyles utc = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;

            if (!DateTime.TryParseExact(text, EpochFormats, CultureInfo.InvariantCulture, utc, out DateTime epochUtc))
                throw Invalid("EPOCH", $"must look like 2026-11-15T12:34:56.789000 (UTC), got \"{text}\"");

            return epochUtc;
        }

        private static void EnsureEpochConvertsToJulianDate(DateTime epochUtc)
        {
            try
            {
                JulianDate.FromUTC(epochUtc);
            }
            catch (ArgumentOutOfRangeException e)
            {
                throw new FormatException(
                    $"CelesTrak JSON: field 'EPOCH' ({epochUtc:o}) is outside the supported date range.", e);
            }
        }

        private static FormatException Invalid(string field, string problem)
        {
            return new FormatException($"CelesTrak JSON: field '{field}' {problem}.");
        }

        private static string Show(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
