// M2-03: Near-Earth SGP4, moving a satellite to a time. Port of Vallado's sgp4 function (SGP4.cpp),
// near-Earth part only. See Sgp4.cs for the source, the settings, and how to read this next to the C++.

using System;
using SatTracker.Core.Snapshots;
using SatTracker.Core.Vectors;

namespace SatTracker.Core.Propagation
{
    /// <summary>
    /// A satellite that's been set up for SGP4 (by <see cref="Sgp4.Initialize"/>). Ask it where the
    /// satellite is at any time with <see cref="Propagate"/>.
    /// <para>
    /// It never changes after setup: <see cref="Propagate"/> has no side effects, so calls can be
    /// made in any order, repeated, or made from several threads, and always give the same answer
    /// for the same time. (Vallado's C++ writes a few values back into its satrec on every call;
    /// none of them feed into later near-Earth calls, so they're simply kept local here.)
    /// </para>
    /// </summary>
    public sealed class Sgp4Satellite
    {
        private const double twopi = 2.0 * Math.PI;
        private const double x2o3 = 2.0 / 3.0;

        internal Sgp4Satellite(OrbitSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        /// <summary>The snapshot this satellite was set up from. Its epoch is "minute 0".</summary>
        public OrbitSnapshot Snapshot { get; }

        // ---- Values worked out once by Sgp4.Initialize. Names are Vallado's satrec fields in PascalCase. ----
        // Distances in Earth radii, time in minutes, angles in radians.

        internal double Ecco { get; init; }      // eccentricity at the epoch
        internal double Inclo { get; init; }     // inclination at the epoch
        internal double Nodeo { get; init; }     // RAAN at the epoch
        internal double Argpo { get; init; }     // argument of perigee at the epoch
        internal double Mo { get; init; }        // mean anomaly at the epoch
        internal double Bstar { get; init; }     // drag term
        internal double NoUnkozai { get; init; } // no_unkozai: mean motion SGP4 uses (Kozai correction undone), rad/min
        internal bool Isimp { get; init; }       // isimp: perigee under 220 km, use the cut-down drag formula
        internal double Con41 { get; init; }     // 3cos²(i) - 1
        internal double Cc1 { get; init; }       // main drag coefficient
        internal double Cc4 { get; init; }       // drag's effect on eccentricity
        internal double Cc5 { get; init; }       // drag's effect on eccentricity (periodic part)
        internal double D2 { get; init; }        // drag's higher-order terms
        internal double D3 { get; init; }
        internal double D4 { get; init; }
        internal double Delmo { get; init; }     // (1 + eta cos(mo))³, the drag-on-mean-anomaly term at the epoch
        internal double Eta { get; init; }       // drag building block (η)
        internal double Argpdot { get; init; }   // how fast the closest point turns around the hoop, rad/min
        internal double Omgcof { get; init; }    // drag's effect on the argument of perigee
        internal double Sinmao { get; init; }    // sin(mo)
        internal double T2cof { get; init; }     // drag's t², t³, t⁴, t⁵ coefficients
        internal double T3cof { get; init; }
        internal double T4cof { get; init; }
        internal double T5cof { get; init; }
        internal double X1mth2 { get; init; }    // 1 - cos²(i)
        internal double X7thm1 { get; init; }    // 7cos²(i) - 1
        internal double Mdot { get; init; }      // how fast the satellite moves along the hoop (bulge-corrected), rad/min
        internal double Nodedot { get; init; }   // how fast the whole hoop turns around Earth's axis, rad/min
        internal double Xlcof { get; init; }     // long-period (J3) coefficient for the mean longitude
        internal double Xmcof { get; init; }     // drag's effect on the mean anomaly
        internal double Nodecf { get; init; }    // drag's effect on the hoop's turning
        internal double Aycof { get; init; }     // long-period (J3) coefficient for e·sin(argp)

        /// <summary>
        /// Where the satellite is, and how fast it's moving, at a time measured from the snapshot's epoch.
        /// </summary>
        /// <param name="minutesSinceEpoch">Minutes after the snapshot's epoch. Negative = before it (allowed).</param>
        /// <returns>Position in km and velocity in km/s, TEME frame (see <see cref="Sgp4State"/>).</returns>
        /// <exception cref="ArgumentOutOfRangeException">The time is NaN or infinite.</exception>
        /// <exception cref="Sgp4Exception">SGP4 can't give a position at this time (e.g. the satellite
        /// has decayed). No numbers come back with it.</exception>
        public Sgp4State Propagate(double minutesSinceEpoch)
        {
            if (!double.IsFinite(minutesSinceEpoch))
                throw new ArgumentOutOfRangeException(nameof(minutesSinceEpoch), minutesSinceEpoch,
                    "The time must be a real number of minutes (not NaN or infinity).");

            Sgp4ErrorCode? error = TryPropagate(minutesSinceEpoch, out Vector3d positionKm, out Vector3d velocityKmPerSec);
            if (error.HasValue)
                throw new Sgp4Exception(error.Value, Snapshot.NoradCatalogId, minutesSinceEpoch, duringInitialization: false);

            return new Sgp4State(minutesSinceEpoch, positionKm, velocityKmPerSec);
        }

        /// <summary>
        /// Vallado's sgp4, near-Earth path. Returns null on success, or the error code (and zero
        /// vectors that the caller must not use). Reads only this satellite's set-up values and
        /// writes only local variables.
        /// </summary>
        /// <param name="t">Vallado's tsince / satrec.t: minutes since the epoch.</param>
        internal Sgp4ErrorCode? TryPropagate(double t, out Vector3d positionKm, out Vector3d velocityKmPerSec)
        {
            positionKm = default;
            velocityKmPerSec = default;

            Sgp4ErrorCode? error = UpdateForSecularGravityAndDrag(t, out MeanElements mean);
            if (error.HasValue)
                return error;

            LongPeriodElements lp = AddLongPeriodPeriodics(mean);

            KeplerSolution kepler = SolveKeplersEquation(lp);

            error = AddShortPeriodPeriodics(mean, lp, kepler, out ShortPeriodResult sp);
            if (error.HasValue)
                return error;

            return BuildPositionAndVelocity(sp, out positionKm, out velocityKmPerSec);
        }

        // ============================================================================================
        // SECTION 2: The slow changes from Earth's bulge and from drag ("secular" = steady drift)
        // ============================================================================================

        /// <summary>
        /// The orbit's "average" shape at time t. Earth's bulge makes the hoop turn (nodem), the
        /// closest point slide around it (argpm), and the satellite's pace along it change (mm).
        /// Drag shrinks the hoop (am) and makes it rounder (em). All smooth, steady changes.
        /// </summary>
        private struct MeanElements
        {
            public double am;    // average size of the orbit (semi-major axis), Earth radii
            public double em;    // average eccentricity
            public double inclm; // inclination (doesn't drift near Earth)
            public double nodem; // RAAN (where the hoop crosses the equator)
            public double argpm; // argument of perigee (where the closest point sits)
            public double mm;    // mean anomaly (where the satellite is along the hoop, "average" angle)
            public double nm;    // mean motion (average angle per minute), rad/min
        }

        /// <summary>
        /// SECTION 2: Vallado's "update for secular gravity and atmospheric drag".
        /// Can report error 2 (mean motion ≤ 0) or error 1 (eccentricity out of range).
        /// </summary>
        private Sgp4ErrorCode? UpdateForSecularGravityAndDrag(double t, out MeanElements m)
        {
            m = default;

            // Each angle at time t = its epoch value + its drift rate × t.
            // xmdf, argpdf, nodedf: mean anomaly, argument of perigee and RAAN with the bulge's drift only.
            double xmdf = Mo + Mdot * t;
            double argpdf = Argpo + Argpdot * t;
            double nodedf = Nodeo + Nodedot * t;
            double argpm = argpdf;
            double mm = xmdf;
            double t2 = t * t;
            double nodem = nodedf + Nodecf * t2;
            // tempa, tempe, templ: drag's effect on size (a), eccentricity (e), and position along the hoop (l).
            double tempa = 1.0 - Cc1 * t;
            double tempe = Bstar * Cc4 * t;
            double templ = T2cof * t2;

            if (!Isimp)
            {
                // Full drag formula (perigee 220 km or higher): extra t³..t⁵ terms, plus drag
                // nudging the closest point (delomg) and the mean anomaly (delm).
                double delomg = Omgcof * t;
                double delmtemp = 1.0 + Eta * Math.Cos(xmdf);
                double delm = Xmcof *
                    (delmtemp * delmtemp * delmtemp -
                    Delmo);
                double temp = delomg + delm;
                mm = xmdf + temp;
                argpm = argpdf - temp;
                double t3 = t2 * t;
                double t4 = t3 * t;
                tempa = tempa - D2 * t2 - D3 * t3 -
                    D4 * t4;
                tempe = tempe + Bstar * Cc5 * (Math.Sin(mm) -
                    Sinmao);
                templ = templ + T3cof * t3 + t4 * (T4cof +
                    t * T5cof);
            }

            double nm = NoUnkozai;
            double em = Ecco;
            double inclm = Inclo;
            // (Vallado calls dspace here for deep space. Not ported.)

            if (nm <= 0.0)
                return Sgp4ErrorCode.MeanMotionNotPositive; // error 2

            double am = Math.Pow((Wgs72.Xke / nm), x2o3) * tempa * tempa;
            nm = Wgs72.Xke / Math.Pow(am, 1.5);
            em = em - tempe;

            // Vallado: "fix tolerance for error recognition". The am < 0.95 check is commented out in his code too.
            if ((em >= 1.0) || (em < -0.001))
                return Sgp4ErrorCode.MeanEccentricityOutOfRange; // error 1

            // Keep eccentricity just above 0, to avoid dividing by zero later.
            if (em < 1.0e-6)
                em = 1.0e-6;
            mm = mm + NoUnkozai * templ;
            double xlm = mm + argpm + nodem; // mean longitude: RAAN + argp + mean anomaly
            // (Vallado also makes emsq = em² and temp = 1 - emsq here; nothing near-Earth reads them.)

            // Wrap the angles into -2π..2π. C's fmod = C#'s % (both keep the sign).
            nodem = nodem % twopi;
            argpm = argpm % twopi;
            xlm = xlm % twopi;
            mm = (xlm - argpm - nodem) % twopi;

            m.am = am;
            m.em = em;
            m.inclm = inclm;
            m.nodem = nodem;
            m.argpm = argpm;
            m.mm = mm;
            m.nm = nm;
            return null;
        }

        // ============================================================================================
        // SECTION 3: The long-period wobbles
        // ============================================================================================

        /// <summary>
        /// The orbit after the slow wobbles (periods of weeks to months) from J3, Earth being
        /// slightly pear-shaped. Eccentricity and perigee are carried as one 2D arrow:
        /// axnl = e·cos(argp), aynl = e·sin(argp) (this avoids trouble when e is near 0).
        /// </summary>
        private struct LongPeriodElements
        {
            public double axnl;  // e·cos(argp)
            public double aynl;  // e·sin(argp) + J3 wobble
            public double xl;    // mean longitude + J3 wobble
            public double nodep; // RAAN
            public double xincp; // inclination
            public double sinip; // sin(inclination)
            public double cosip; // cos(inclination)
        }

        /// <summary>SECTION 3: Vallado's "long period periodics" (near-Earth part).</summary>
        private LongPeriodElements AddLongPeriodPeriodics(MeanElements m)
        {
            // ----------------- compute extra mean quantities -------------
            double sinim = Math.Sin(m.inclm);
            double cosim = Math.Cos(m.inclm);

            // The "p" values are the elements after the Moon/Sun corrections (Vallado's dpper).
            // That's deep space only, so near Earth they're the same as the "m" values.
            double ep = m.em;
            double xincp = m.inclm;
            double argpp = m.argpm;
            double nodep = m.nodem;
            double mp = m.mm;
            double sinip = sinim;
            double cosip = cosim;

            var lp = new LongPeriodElements();
            lp.axnl = ep * Math.Cos(argpp);
            double temp = 1.0 / (m.am * (1.0 - ep * ep));
            lp.aynl = ep * Math.Sin(argpp) + temp * Aycof;
            lp.xl = mp + argpp + nodep + temp * Xlcof * lp.axnl;
            lp.nodep = nodep;
            lp.xincp = xincp;
            lp.sinip = sinip;
            lp.cosip = cosip;
            return lp;
        }

        // ============================================================================================
        // SECTION 4: Solving Kepler's equation for the satellite's place on the hoop
        // ============================================================================================

        /// <summary>
        /// The result of the Kepler solve. Note that sineo1 and coseo1 are from the LAST LOOP PASS,
        /// i.e. the sine and cosine of eo1 just BEFORE its final small correction. That's what
        /// Vallado's code uses afterwards, so this keeps it, rather than recomputing them.
        /// </summary>
        private struct KeplerSolution
        {
            public double sineo1;
            public double coseo1;
        }

        /// <summary>
        /// SECTION 4: Vallado's "solve kepler's equation".
        /// <para>
        /// The mean anomaly says where the satellite would be if it moved at a steady pace. On an
        /// oval it doesn't: it speeds up near Earth and slows down far away. Kepler's equation
        /// links the steady-pace angle to the real place, and has no direct solution, so it's
        /// solved by guess-and-improve (Newton's method): at most 10 passes, each step capped at
        /// 0.95 radians, stopping when a step is below 1e-12.
        /// </para>
        /// <para>
        /// This is a version of Kepler's equation measured from the ascending node: u is the mean
        /// anomaly + argp, and eo1 (the unknown) is the eccentric anomaly + argp. Eccentricity
        /// enters only through axnl/aynl (the 2D arrow from section 3).
        /// </para>
        /// </summary>
        private static KeplerSolution SolveKeplersEquation(LongPeriodElements lp)
        {
            double axnl = lp.axnl;
            double aynl = lp.aynl;

            double u = (lp.xl - lp.nodep) % twopi;
            double eo1 = u;      // first guess: as if the orbit were a circle
            double tem5 = 9999.9; // size of the last correction; starts big so the loop runs at least once
            int ktr = 1;          // pass counter
            double sineo1 = 0.0;  // (C++ leaves these unset; the loop always sets them on pass 1)
            double coseo1 = 0.0;
            //   sgp4fix for kepler iteration
            //   the following iteration needs better limits on corrections
            while ((Math.Abs(tem5) >= 1.0e-12) && (ktr <= 10))
            {
                sineo1 = Math.Sin(eo1);
                coseo1 = Math.Cos(eo1);
                tem5 = 1.0 - coseo1 * axnl - sineo1 * aynl;
                tem5 = (u - aynl * coseo1 + axnl * sineo1 - eo1) / tem5;
                if (Math.Abs(tem5) >= 0.95)
                    tem5 = tem5 > 0.0 ? 0.95 : -0.95;
                eo1 = eo1 + tem5;
                ktr = ktr + 1;
            }

            return new KeplerSolution { sineo1 = sineo1, coseo1 = coseo1 };
        }

        // ============================================================================================
        // SECTION 5: The short-period wobbles
        // ============================================================================================

        /// <summary>
        /// The satellite's place after the quick wobbles (twice per lap) from the bulge.
        /// Distances in Earth radii, speeds in Earth radii per SGP4 time unit.
        /// </summary>
        private struct ShortPeriodResult
        {
            public double mrt;   // distance from Earth's center
            public double mvt;   // speed straight away from Earth (radial)
            public double rvdot; // speed sideways along the orbit (r × angular rate)
            public double su;    // argument of latitude: angle along the hoop from the ascending node
            public double xnode; // RAAN
            public double xinc;  // inclination
        }

        /// <summary>
        /// SECTION 5: Vallado's "short period preliminary quantities" and "update for short period
        /// periodics". Can report error 4 (semi-latus rectum below 0).
        /// </summary>
        private Sgp4ErrorCode? AddShortPeriodPeriodics(MeanElements m, LongPeriodElements lp, KeplerSolution k, out ShortPeriodResult sp)
        {
            sp = default;
            double am = m.am;
            double nm = m.nm;
            double axnl = lp.axnl;
            double aynl = lp.aynl;
            double sineo1 = k.sineo1;
            double coseo1 = k.coseo1;

            // ------------- short period preliminary quantities -----------
            double ecose = axnl * coseo1 + aynl * sineo1; // e·cos(E)
            double esine = axnl * sineo1 - aynl * coseo1; // e·sin(E)
            double el2 = axnl * axnl + aynl * aynl;       // e²
            double pl = am * (1.0 - el2);                 // semi-latus rectum p = a(1 - e²)
            if (pl < 0.0)
                return Sgp4ErrorCode.SemiLatusRectumNegative; // error 4

            // The plain (unwobbled) oval: distance rl, its rate of change rdotl, sideways rate rvdotl,
            // and the angle along the hoop su (from sinu, cosu).
            double rl = am * (1.0 - ecose);
            double rdotl = Math.Sqrt(am) * esine / rl;
            double rvdotl = Math.Sqrt(pl) / rl;
            double betal = Math.Sqrt(1.0 - el2);
            double temp = esine / (1.0 + betal);
            double sinu = am / rl * (sineo1 - aynl - axnl * temp);
            double cosu = am / rl * (coseo1 - axnl + aynl * temp);
            double su = Math.Atan2(sinu, cosu);
            double sin2u = (cosu + cosu) * sinu;
            double cos2u = 1.0 - 2.0 * sinu * sinu;
            temp = 1.0 / pl;
            double temp1 = 0.5 * Wgs72.J2 * temp;
            double temp2 = temp1 * temp;

            // -------------- update for short period periodics ------------
            // The bulge's quick wobbles nudge distance, angle along the hoop, the hoop's turn and
            // tilt, and both speeds. (Vallado recomputes con41/x1mth2/x7thm1 here for deep space only.)
            sp.mrt = rl * (1.0 - 1.5 * temp2 * betal * Con41) +
                0.5 * temp1 * X1mth2 * cos2u;
            sp.su = su - 0.25 * temp2 * X7thm1 * sin2u;
            sp.xnode = lp.nodep + 1.5 * temp2 * lp.cosip * sin2u;
            sp.xinc = lp.xincp + 1.5 * temp2 * lp.cosip * lp.sinip * cos2u;
            sp.mvt = rdotl - nm * temp1 * X1mth2 * sin2u / Wgs72.Xke;
            sp.rvdot = rvdotl + nm * temp1 * (X1mth2 * cos2u +
                1.5 * Con41) / Wgs72.Xke;
            return null;
        }

        // ============================================================================================
        // SECTION 6: Building the final position and velocity in TEME
        // ============================================================================================

        /// <summary>
        /// SECTION 6: Vallado's "orientation vectors" and "position and velocity (in km and km/sec)".
        /// Turns "distance from Earth's center + angles" into X, Y, Z arrows, then into km and km/s.
        /// Can report error 6 (decayed).
        /// </summary>
        private static Sgp4ErrorCode? BuildPositionAndVelocity(ShortPeriodResult sp, out Vector3d positionKm, out Vector3d velocityKmPerSec)
        {
            positionKm = default;
            velocityKmPerSec = default;

            // --------------------- orientation vectors -------------------
            // (ux, uy, uz): unit arrow from Earth's center towards the satellite.
            // (vx, vy, vz): unit arrow at right angles to it, in the orbit plane, in the direction of travel.
            double sinsu = Math.Sin(sp.su);
            double cossu = Math.Cos(sp.su);
            double snod = Math.Sin(sp.xnode);
            double cnod = Math.Cos(sp.xnode);
            double sini = Math.Sin(sp.xinc);
            double cosi = Math.Cos(sp.xinc);
            double xmx = -snod * cosi;
            double xmy = cnod * cosi;
            double ux = xmx * sinsu + cnod * cossu;
            double uy = xmy * sinsu + snod * cossu;
            double uz = sini * sinsu;
            double vx = xmx * cossu - cnod * sinsu;
            double vy = xmy * cossu - snod * sinsu;
            double vz = sini * cossu;

            // Vallado: "sgp4fix for decaying satellites". Closer to Earth's center than one Earth
            // radius means it has come down. Vallado computes r and v first and then flags the
            // error; here the numbers are simply not handed out.
            if (sp.mrt < 1.0)
                return Sgp4ErrorCode.Decayed; // error 6

            // --------- position and velocity (in km and km/sec) ----------
            // Position = distance × direction, Earth radii → km.
            // Velocity = (radial speed × direction) + (sideways speed × sideways arrow), internal units → km/s.
            double radiusearthkm = Wgs72.RadiusEarthKm;
            double vkmpersec = Wgs72.KmPerSecPerInternalSpeedUnit;
            positionKm = new Vector3d(
                (sp.mrt * ux) * radiusearthkm,
                (sp.mrt * uy) * radiusearthkm,
                (sp.mrt * uz) * radiusearthkm);
            velocityKmPerSec = new Vector3d(
                (sp.mvt * ux + sp.rvdot * vx) * vkmpersec,
                (sp.mvt * uy + sp.rvdot * vy) * vkmpersec,
                (sp.mvt * uz + sp.rvdot * vz) * vkmpersec);
            return null;
        }
    }
}
