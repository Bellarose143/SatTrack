// M2-03: Near-Earth SGP4, ported from David Vallado's C++ reference code.
//
// Source: SGP4.cpp in AIAA-2006-6753.zip ("Revisiting Spacetrack Report #3", Vallado, Crawford,
// Hujsak, Kelso 2006), from https://celestrak.org/publications/AIAA/2006-6753/ (zip file
// 1,248,284 bytes; the SGP4.cpp in it is dated "12 mar 20").
//
// This file: setting up a satellite (Vallado's sgp4init + initl, near-Earth part only).
// Sgp4Satellite.cs: moving it to a time (Vallado's sgp4, near-Earth part only).
// Wgs72.cs: the Earth constants (Vallado's getgravconst, wgs72 case).
//
// How to read it next to the C++:
// - Formulas are copied in Vallado's order with his brackets. Don't "tidy" the algebra: doubles
//   round after every step, so a reordered sum can change the last digits, and the official
//   test answers are checked to 1 mm.
// - Local variables keep Vallado's names (ao, con41, cc1, eta, ...), each explained once where
//   it is first made. Stored values use the same names in PascalCase (satrec.cc1 → Cc1).
// - Inside SGP4, distance is in Earth radii (1.0 = 6378.135 km), time in minutes, angles in
//   radians. Only the final answer is turned into km and km/s.
// - C's fmod(a, b) is C#'s a % b for doubles (both keep the sign of a). C's pow is Math.Pow.
//
// SETTINGS THAT MADE THE OFFICIAL ANSWERS (tcppver.out)
// Vallado's test program (TestSGP4.cpp) asks for both at the keyboard, so the code alone doesn't
// say. The evidence:
// - Gravity model: WGS-72 ("72"). Vallado's sgp4_CodeReadme.pdf (in the same zip), "To verify
//   your installation": "At the input constants prompt, enter '72'". The program's own prompt
//   marks it as the default: "input which constants 721 (72) 84". Checked by running all 9
//   near-Earth cases both ways during M2-03: WGS-72 matches; the older rounded "721" set does not.
// - Operation mode: 'a' ("AFSPC", Vallado's best guess at how the US Air Force runs it). Same
//   readme: "At the input mode prompt, enter 'a' for the afspc mode". Prompt: "input operation
//   mode (a), i".
// - Does the mode matter for near-Earth satellites? No. In this version of SGP4.cpp the mode is
//   only read in dpper (a deep-space-only step that adds the Moon's and Sun's pull). The one
//   near-Earth place it used to matter, how initl finds Earth's rotation angle at the epoch
//   (gsto), is commented out, and gsto itself is only used by the deep-space steps. So this port
//   has no operation-mode switch at all: for near-Earth, 'a' and 'i' give identical numbers.

using System;
using SatTracker.Core.Snapshots;

namespace SatTracker.Core.Propagation
{
    /// <summary>
    /// SGP4, the prediction formula that turns a published orbit snapshot into a position and
    /// velocity at any time. Near-Earth satellites only (orbit period under 225 minutes).
    /// <code>
    /// Sgp4Satellite iss = Sgp4.Initialize(snapshot);
    /// Sgp4State state = iss.Propagate(minutesSinceEpoch: 90.0);
    /// // state.PositionKm, state.VelocityKmPerSec: TEME frame (see Sgp4State)
    /// </code>
    /// </summary>
    public static class Sgp4
    {
        /// <summary>
        /// Orbits this slow or slower (in minutes per lap) need SGP4's deep-space part, which this
        /// library doesn't have. Same number as Vallado's sgp4init.
        /// </summary>
        public const double DeepSpacePeriodThresholdMinutes = 225.0;

        private const double x2o3 = 2.0 / 3.0;

        /// <summary>
        /// Vallado's temp4: stands in for (1 + cos i) when the inclination is 180°, to avoid
        /// dividing by zero. (No real satellite has exactly 180°.)
        /// </summary>
        private const double temp4 = 1.5e-12;

        /// <summary>
        /// Prepares a satellite for prediction: works out every number that doesn't depend on
        /// time, once. Port of Vallado's sgp4init (with initl), near-Earth only.
        /// </summary>
        /// <exception cref="ArgumentNullException">No snapshot.</exception>
        /// <exception cref="ArgumentException">A snapshot value is NaN, infinite, or out of range
        /// (e.g. eccentricity 1 or more, mean motion zero or less).</exception>
        /// <exception cref="Sgp4DeepSpaceNotSupportedException">Orbit period is 225 minutes or more.</exception>
        /// <exception cref="Sgp4Exception">SGP4 reports an error already at minute 0. Like Vallado's
        /// sgp4init, this ends by predicting minute 0, and refuses the snapshot if that fails.</exception>
        public static Sgp4Satellite Initialize(OrbitSnapshot snapshot)
        {
            ValidateSnapshot(snapshot);

            // ---------- SECTION 1: setup and constants ----------
            // Vallado's names for the snapshot values (the "o" ending means "at the epoch").
            double ecco = snapshot.Eccentricity;          // eccentricity
            double inclo = snapshot.InclinationRad;       // inclination
            double nodeo = snapshot.RaanRad;              // RAAN, where the hoop crosses the equator going north
            double argpo = snapshot.ArgumentOfPerigeeRad; // argument of perigee, where the closest point sits
            double mo = snapshot.MeanAnomalyRad;          // mean anomaly, where the satellite is along the hoop
            double no_kozai = snapshot.MeanMotionRadPerMin; // mean motion as published (see Initl)
            double bstar = snapshot.BStarPerEarthRadii;   // drag term

            double radiusearthkm = Wgs72.RadiusEarthKm;
            double j2 = Wgs72.J2;
            double j4 = Wgs72.J4;
            double j3oj2 = Wgs72.J3OverJ2;

            // ss and qzms2t describe a simple model of the air: its density falls off with height
            // above a reference height of 78 km. qzms2t = ((120 - 78) km in Earth radii)^4.
            // "Multiply instead of pow", as Vallado does, for the same last digits.
            double ss = 78.0 / radiusearthkm + 1.0;
            double qzms2ttemp = (120.0 - 78.0) / radiusearthkm;
            double qzms2t = qzms2ttemp * qzms2ttemp * qzms2ttemp * qzms2ttemp;

            InitlResult e = Initl(ecco, inclo, no_kozai);

            // Deep-space check, exactly as Vallado: 2π ÷ the UN-Kozai'd mean motion (from initl,
            // not the published number) ≥ 225 minutes. Vallado makes this check further down,
            // after the near-Earth numbers below; doing it here instead changes nothing, because
            // for deep space we stop anyway.
            double periodMinutes = 2.0 * Math.PI / e.no_unkozai;
            if (periodMinutes >= DeepSpacePeriodThresholdMinutes)
                throw new Sgp4DeepSpaceNotSupportedException(snapshot.NoradCatalogId, periodMinutes);

            // ---------- SECTION 2 (setup half): the slow changes from Earth's bulge and from drag ----------
            // Vallado wraps the rest in "if ((omeosq >= 0.0) || (no_unkozai >= 0.0))". After
            // ValidateSnapshot, omeosq = 1 - e² is always > 0, so that test is always true and is left out.

            // isimp = "simple": for perigees under 220 km the drag formula is cut down to fewer
            // terms (Spacetrack Report #3 does this; the satellite is about to come down anyway).
            bool isimp = false;
            if (e.rp < (220.0 / radiusearthkm + 1.0))
                isimp = true;

            // sfour, qzms24: the air model's reference height and strength, adjusted when the
            // perigee is very low (under 156 km), because the standard 78 km reference would be
            // too close to the perigee.
            double sfour = ss;
            double qzms24 = qzms2t;
            double perige = (e.rp - 1.0) * radiusearthkm; // perigee height above the equator, km

            if (perige < 156.0)
            {
                sfour = perige - 78.0;
                if (perige < 98.0)
                    sfour = 20.0;
                double qzms24temp = (120.0 - sfour) / radiusearthkm;
                qzms24 = qzms24temp * qzms24temp * qzms24temp * qzms24temp;
                sfour = sfour / radiusearthkm + 1.0;
            }

            double pinvsq = 1.0 / e.posq;   // 1 / p², p = semi-latus rectum

            // tsi, eta, coef, coef1: building blocks of the drag model (Spacetrack Report #3 names: ξ, η).
            double tsi = 1.0 / (e.ao - sfour);
            double eta = e.ao * ecco * tsi;
            double etasq = eta * eta;
            double eeta = ecco * eta;
            double psisq = Math.Abs(1.0 - etasq);
            double coef = qzms24 * Math.Pow(tsi, 4.0);
            double coef1 = coef / Math.Pow(psisq, 3.5);

            // cc1..cc5: the drag coefficients. cc1 is the main one: how fast drag shrinks the orbit.
            double cc2 = coef1 * e.no_unkozai * (e.ao * (1.0 + 1.5 * etasq + eeta *
                (4.0 + etasq)) + 0.375 * j2 * tsi / psisq * e.con41 *
                (8.0 + 3.0 * etasq * (8.0 + etasq)));
            double cc1 = bstar * cc2;
            double cc3 = 0.0;
            if (ecco > 1.0e-4)
                cc3 = -2.0 * coef * tsi * j3oj2 * e.no_unkozai * e.sinio / ecco;
            double x1mth2 = 1.0 - e.cosio2;  // 1 - cos²(i)
            double cc4 = 2.0 * e.no_unkozai * coef1 * e.ao * e.omeosq *
                (eta * (2.0 + 0.5 * etasq) + ecco *
                (0.5 + 2.0 * etasq) - j2 * tsi / (e.ao * psisq) *
                (-3.0 * e.con41 * (1.0 - 2.0 * eeta + etasq *
                (1.5 - 0.5 * eeta)) + 0.75 * x1mth2 *
                (2.0 * etasq - eeta * (1.0 + etasq)) * Math.Cos(2.0 * argpo)));
            double cc5 = 2.0 * coef1 * e.ao * e.omeosq * (1.0 + 2.75 *
                (etasq + eeta) + eeta * etasq);

            // The bulge (J2, J4) makes three angles drift at steady rates, in radians per minute:
            // mdot (how fast the satellite moves along the hoop), argpdot (the closest point
            // turns around the hoop), nodedot (the whole hoop turns around Earth's axis).
            double cosio4 = e.cosio2 * e.cosio2;
            double temp1 = 1.5 * j2 * pinvsq * e.no_unkozai;
            double temp2 = 0.5 * temp1 * j2 * pinvsq;
            double temp3 = -0.46875 * j4 * pinvsq * pinvsq * e.no_unkozai;
            double mdot = e.no_unkozai + 0.5 * temp1 * e.rteosq * e.con41 + 0.0625 *
                temp2 * e.rteosq * (13.0 - 78.0 * e.cosio2 + 137.0 * cosio4);
            double argpdot = -0.5 * temp1 * e.con42 + 0.0625 * temp2 *
                (7.0 - 114.0 * e.cosio2 + 395.0 * cosio4) +
                temp3 * (3.0 - 36.0 * e.cosio2 + 49.0 * cosio4);
            double xhdot1 = -temp1 * e.cosio;
            double nodedot = xhdot1 + (0.5 * temp2 * (4.0 - 19.0 * e.cosio2) +
                2.0 * temp3 * (3.0 - 7.0 * e.cosio2)) * e.cosio;
            // (Vallado also makes xpidot = argpdot + nodedot here; only deep space uses it.)

            // Drag's effect on the closest point (omgcof), on the mean anomaly (xmcof), and on the
            // hoop's turning (nodecf); t2cof is the first "time squared" drag term.
            double omgcof = bstar * cc3 * Math.Cos(argpo);
            double xmcof = 0.0;
            if (ecco > 1.0e-4)
                xmcof = -x2o3 * coef * bstar / eeta;
            double nodecf = 3.5 * e.omeosq * xhdot1 * cc1;
            double t2cof = 1.5 * cc1;

            // ---------- SECTION 3 (setup half): long-period wobble coefficients ----------
            // From J3 (Earth slightly pear-shaped). xlcof and aycof are used in Sgp4Satellite's section 3.
            double xlcof;
            if (Math.Abs(e.cosio + 1.0) > 1.5e-12)
                xlcof = -0.25 * j3oj2 * e.sinio * (3.0 + 5.0 * e.cosio) / (1.0 + e.cosio);
            else
                xlcof = -0.25 * j3oj2 * e.sinio * (3.0 + 5.0 * e.cosio) / temp4;
            double aycof = -0.5 * j3oj2 * e.sinio;

            // delmo, sinmao: the drag-on-mean-anomaly term's value at the epoch, so it can be
            // subtracted later (making that term exactly 0 at minute 0).
            double delmotemp = 1.0 + eta * Math.Cos(mo);
            double delmo = delmotemp * delmotemp * delmotemp;
            double sinmao = Math.Sin(mo);
            double x7thm1 = 7.0 * e.cosio2 - 1.0;  // 7 cos²(i) - 1, used in section 5

            // ---------- SECTION 2 (setup half, continued): higher-order drag terms ----------
            // d2, d3, d4 and t3cof..t5cof: drag's t³, t⁴, t⁵ terms. Skipped (left 0) for the "simple" low-perigee case.
            double d2 = 0.0, d3 = 0.0, d4 = 0.0, t3cof = 0.0, t4cof = 0.0, t5cof = 0.0;
            if (!isimp)
            {
                double cc1sq = cc1 * cc1;
                d2 = 4.0 * e.ao * tsi * cc1sq;
                double temp = d2 * tsi * cc1 / 3.0;
                d3 = (17.0 * e.ao + sfour) * temp;
                d4 = 0.5 * temp * e.ao * tsi * (221.0 * e.ao + 31.0 * sfour) *
                    cc1;
                t3cof = d2 + 2.0 * cc1sq;
                t4cof = 0.25 * (3.0 * d3 + cc1 *
                    (12.0 * d2 + 10.0 * cc1sq));
                t5cof = 0.2 * (3.0 * d4 +
                    12.0 * cc1 * d3 +
                    6.0 * d2 * d2 +
                    15.0 * cc1sq * (2.0 * d2 + cc1sq));
            }

            var satellite = new Sgp4Satellite(snapshot)
            {
                Ecco = ecco,
                Inclo = inclo,
                Nodeo = nodeo,
                Argpo = argpo,
                Mo = mo,
                Bstar = bstar,
                NoUnkozai = e.no_unkozai,
                Isimp = isimp,
                Con41 = e.con41,
                Cc1 = cc1,
                Cc4 = cc4,
                Cc5 = cc5,
                D2 = d2,
                D3 = d3,
                D4 = d4,
                Delmo = delmo,
                Eta = eta,
                Argpdot = argpdot,
                Omgcof = omgcof,
                Sinmao = sinmao,
                T2cof = t2cof,
                T3cof = t3cof,
                T4cof = t4cof,
                T5cof = t5cof,
                X1mth2 = x1mth2,
                X7thm1 = x7thm1,
                Mdot = mdot,
                Nodedot = nodedot,
                Xlcof = xlcof,
                Xmcof = xmcof,
                Nodecf = nodecf,
                Aycof = aycof,
            };

            // Vallado's sgp4init ends by predicting minute 0 ("propagate to zero epoch to
            // initialize all others"). His code then carries on even if that reported an error;
            // here an error at minute 0 means the snapshot is unusable, so refuse it.
            Sgp4ErrorCode? error = satellite.TryPropagate(0.0, out _, out _);
            if (error.HasValue)
                throw new Sgp4Exception(error.Value, snapshot.NoradCatalogId, 0.0, duringInitialization: true);

            return satellite;
        }

        /// <summary>The outputs of <see cref="Initl"/> that the rest of setup uses.</summary>
        private struct InitlResult
        {
            public double ao;        // semi-major axis (average size of the orbit), Earth radii
            public double con41;     // 3cos²(i) - 1, a common bulge factor
            public double con42;     // 1 - 5cos²(i)
            public double cosio;     // cos(inclination)
            public double cosio2;    // cos²(inclination)
            public double omeosq;    // 1 - e² ("one minus e squared")
            public double posq;      // p², p = semi-latus rectum = ao × (1 - e²), Earth radii
            public double rp;        // perigee radius (closest distance from Earth's center), Earth radii
            public double rteosq;    // √(1 - e²)
            public double sinio;     // sin(inclination)
            public double no_unkozai; // mean motion with the Kozai correction undone, rad/min
        }

        /// <summary>
        /// SECTION 1: Vallado's initl. Works out basic orbit quantities and "un-Kozais" the mean motion.
        /// <para>
        /// <b>Un-Kozai?</b> The mean motion in a published snapshot isn't the plain "laps per
        /// minute". It was averaged a particular way (Kozai's method) that already folds in some of
        /// the bulge's effect. SGP4 wants it averaged the other way (Brouwer's method), so this
        /// undoes the difference: no_kozai (as published) → no_unkozai (what SGP4 uses). The
        /// change is tiny (parts per thousand) but it matters for both the orbit size and the
        /// deep-space check.
        /// </para>
        /// Left out from Vallado's initl: <c>method</c> (always 'n' here), <c>ainv</c> and
        /// <c>eccsq</c> as outputs (nothing near-Earth uses them), and <c>gsto</c> (Earth's rotation
        /// angle at the epoch; only deep space uses it).
        /// </summary>
        private static InitlResult Initl(double ecco, double inclo, double no_kozai)
        {
            double xke = Wgs72.Xke;
            double j2 = Wgs72.J2;
            var r = new InitlResult();

            // ------------- calculate auxillary epoch quantities ----------
            double eccsq = ecco * ecco;
            r.omeosq = 1.0 - eccsq;
            r.rteosq = Math.Sqrt(r.omeosq);
            r.cosio = Math.Cos(inclo);
            r.cosio2 = r.cosio * r.cosio;

            // ------------------ un-kozai the mean motion -----------------
            // ak: orbit size from the published mean motion (Kepler's third law, in Earth radii).
            // d1, del, adel: the Kozai-to-Brouwer correction, worked out in two passes.
            double ak = Math.Pow(xke / no_kozai, x2o3);
            double d1 = 0.75 * j2 * (3.0 * r.cosio2 - 1.0) / (r.rteosq * r.omeosq);
            double del = d1 / (ak * ak);
            double adel = ak * (1.0 - del * del - del *
                (1.0 / 3.0 + 134.0 * del * del / 81.0));
            del = d1 / (adel * adel);
            r.no_unkozai = no_kozai / (1.0 + del);

            r.ao = Math.Pow(xke / (r.no_unkozai), x2o3);
            r.sinio = Math.Sin(inclo);
            double po = r.ao * r.omeosq;
            r.con42 = 1.0 - 5.0 * r.cosio2;
            r.con41 = -r.con42 - r.cosio2 - r.cosio2;
            r.posq = po * po;
            r.rp = r.ao * (1.0 - ecco);
            return r;
        }

        /// <summary>
        /// Missing or invalid input throws, never a silent default. Vallado's code doesn't check
        /// these: bad values just turn into NaN (not-a-number) inside the formulas.
        /// </summary>
        private static void ValidateSnapshot(OrbitSnapshot snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            RequireFinite(snapshot.MeanMotionRadPerMin, nameof(OrbitSnapshot.MeanMotionRadPerMin));
            RequireFinite(snapshot.Eccentricity, nameof(OrbitSnapshot.Eccentricity));
            RequireFinite(snapshot.InclinationRad, nameof(OrbitSnapshot.InclinationRad));
            RequireFinite(snapshot.RaanRad, nameof(OrbitSnapshot.RaanRad));
            RequireFinite(snapshot.ArgumentOfPerigeeRad, nameof(OrbitSnapshot.ArgumentOfPerigeeRad));
            RequireFinite(snapshot.MeanAnomalyRad, nameof(OrbitSnapshot.MeanAnomalyRad));
            RequireFinite(snapshot.BStarPerEarthRadii, nameof(OrbitSnapshot.BStarPerEarthRadii));

            if (snapshot.MeanMotionRadPerMin <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(snapshot), snapshot.MeanMotionRadPerMin,
                    $"Satellite {snapshot.NoradCatalogId}: {nameof(OrbitSnapshot.MeanMotionRadPerMin)} must be greater than 0 (it is how fast the satellite goes around).");

            if (snapshot.Eccentricity < 0.0 || snapshot.Eccentricity >= 1.0)
                throw new ArgumentOutOfRangeException(nameof(snapshot), snapshot.Eccentricity,
                    $"Satellite {snapshot.NoradCatalogId}: {nameof(OrbitSnapshot.Eccentricity)} must be at least 0 and less than 1 (a closed orbit).");

            if (snapshot.InclinationRad < 0.0 || snapshot.InclinationRad > Math.PI)
                throw new ArgumentOutOfRangeException(nameof(snapshot), snapshot.InclinationRad,
                    $"Satellite {snapshot.NoradCatalogId}: {nameof(OrbitSnapshot.InclinationRad)} must be between 0 and π (0° to 180°).");
        }

        private static void RequireFinite(double value, string fieldName)
        {
            if (!double.IsFinite(value))
                throw new ArgumentException($"{fieldName} is {value}. Every orbit value must be a real number (not NaN or infinity).", "snapshot");
        }
    }
}
