# Tickets

Status: ⬜ todo · 🟨 in progress · ✅ done
Owner: **you write** = learning piece, write it yourself (or understand every line) · **delegate** = plumbing, Claude Code can do it

---

## ✅ Milestone 1: Time + reading the snapshot (Nov 16 to 22), finished Nov 20

**Milestone done when:** the saved ISS JSON loads into a C# object with correct units, and you can ask "how many minutes since the snapshot?" for any moment. All tests green.

### ✅ M1-01 (Learn): Time
How this field tells time, and how a timestamp like `2026-11-15T12:34:56.789` becomes a number the math can use.
- ✅ Idea 1: Julian Dates (one running number; noon start; MJD vs J2000; two-double precision trick, like a floating origin for time)
- ✅ Idea 2: Multiple clocks (atomic, UT1, UTC) + the local-time parsing trap
- Takeaways: a 5-hour parsing bug puts the ISS ~0.23 laps (~10,000 km) off along the hoop AND ~75° off in longitude because Earth keeps spinning. The UT1 ≈ UTC shortcut costs at most ~420 m at the equator (less at higher latitudes, × cos(latitude)).

### ✅ M1-02 (Build, delegate): Project setup
Plain C# class library + NUnit test project, no Unity. Uses the `dotnet` command line on Windows.
**AC:**
- `dotnet test` runs from the command line
- One placeholder test passes
- Library project does NOT reference UnityEngine
- Library targets `netstandard2.1` with `LangVersion` 9 (so it drops into Unity later)

### ✅ M1-03 (Build, you write): Julian Date conversion
Convert a UTC calendar date/time into a Julian Date (a running count of days, explained in M1-01).
**AC / tests:**
- 2000-01-01 12:00:00 → 2451545.0
- 1970-01-01 00:00:00 → 2440587.5
- 1996-10-26 14:20:00 → 2450383.0972222 (match to 1e-7 days)
- Stored as two doubles (whole day + fraction) internally, explained in M1-01

### ✅ M1-04 (Build, delegate then review): Parse the ISS snapshot
Load a saved CelesTrak ISS JSON file into a C# record. Convert units once, here.
**AC / tests:**
- Fixture: a real ISS JSON file you download and save into the test project
- Parses with no errors; `NORAD_CAT_ID` = 25544
- Mean motion 15.5 rev/day → 0.0676315085 rad/min (test with a hand-made input)
- 51.6° → 0.9005898940 rad
- Field names include units (e.g. `MeanMotionRadPerMin`, `InclinationRad`)
- Missing required field → clear exception, never a silent zero
- EPOCH parsed as UTC into the M1-03 Julian Date type
- **EPOCH has no "Z" suffix. Parse it explicitly as UTC (e.g. `DateTimeStyles.AssumeUniversal | AdjustToUniversal`). Test: the parsed hour equals the hour in the string, regardless of the machine's time zone.**
- No `DateTime.Now` anywhere in the library. Only `DateTime.UtcNow`, and only at the app edge.

### ✅ M1-05 (Build, you write): Minutes since snapshot
Given the snapshot's epoch and any target moment, return minutes between them (this is what the prediction formula takes as input).
**AC / tests:**
- Target = epoch + 90 min → 90.0
- Target crosses midnight (epoch 23:30, target next day 00:45) → 75.0
- Target before epoch → negative number (allowed, not an error)
- Exact to 1e-6 minutes

### ✅ M1-06 (Verify): Sanity check against reality
Download today's ISS JSON, parse it with your code, print the fields.
**AC:**
- Printed values match the CelesTrak page by eye
- Orbit period = 1440 ÷ mean motion (rev/day) comes out near 92.9 minutes
- Snapshot age (minutes since epoch, right now) is less than ~2 days

---

## Milestone 2: The prediction formula, SGP4 (Nov 21 to Dec 6, Thanksgiving inside)

**Milestone done when:** given a snapshot and a time, your code says where the satellite is (position + velocity in space coordinates), it matches every official near-Earth test case, and you can explain what each section of the formula does in plain words.

**Decisions:**
- Claude Code ports SGP4 from David Vallado's official reference code. You review it and own the verification.
- **Near-Earth only.** Satellites with an orbit period of 225 minutes or more (the "deep space" mode) are rejected with a clear error. ISS and SatGus are both near-Earth.

### ⬜ M2-01 (Learn): What SGP4 does
Plain-language tour: what goes in, what comes out, and the three things it accounts for (the basic hoop motion, Earth's equatorial bulge slowly turning the hoop, and drag shrinking it). Units and the WGS-72 vs WGS-84 trap.
**Done when:** you've answered the check questions in chat.

### ⬜ M2-02 (Build, delegate): Official test data reader (test project only)
Read Vallado's verification files: `SGP4-VER.TLE` (the inputs, in old TLE text format) and `tcppver.out` (the expected answers). Download both from CelesTrak's "Revisiting Spacetrack Report #3" page and save them in the test project.
**AC:**
- Lives in the **test project**, not Core
- Parses every entry in `SGP4-VER.TLE`, including the extra start/stop/step times at the end of line 2
- Converts each TLE into the same element record M1-04 produces from JSON (same units, same field names)
- Parses `tcppver.out` into: catalog number → list of (minutes since epoch, position km, velocity km/s)
- Test: the ISS-like or any known entry's catalog number, inclination, and mean motion come out correct

### ⬜ M2-03 (Build, delegate then review): Port near-Earth SGP4
**AC:**
- Interface (roughly): `Sgp4.Initialize(elements) → Sgp4Satellite`, then `satellite.Propagate(minutesSinceEpoch) → (PositionKm, VelocityKmPerSec)` in TEME (space coordinates)
- Uses **WGS-72** constants inside SGP4 (not WGS-84), with a comment explaining why
- Uses the same SGP4 settings (operation mode, gravity model) that produced `tcppver.out`, and documents which
- Deep-space objects (period ≥ 225 min) → clear exception
- Errors the formula itself reports (e.g. satellite has decayed) → clear exception or result status, never garbage numbers
- Double precision throughout; no rounding; no packages
- Code split into clearly named methods/sections so it can be reviewed in pieces (M2-04)

### ⬜ M2-04 (Learn, you): Review the port
Walk through the port one section at a time with Claude Code explaining in plain language: setup, the slow hoop changes (bulge + drag), the short wobbles, solving for where the satellite is on the hoop, and building the final position/velocity.
**Done when:** you can say in one sentence what each section does, and you bring any "why?" questions back to chat.

### ⬜ M2-05 (Build, you write): Verification harness
Run every near-Earth official case through your SGP4 and compare against `tcppver.out`.
**AC:**
- Every near-Earth case, every time step is checked (one NUnit test case per satellite is fine)
- Position within **1e-6 km (1 mm)**, velocity within **1e-9 km/s**
- Cases where the official output reports an error must produce your error too
- Deep-space cases skipped with a visible reason, not silently
- **If anything fails: stop and bring it here. Never loosen tolerances.**

### ⬜ M2-06 (Verify): Where is the ISS right now?
**AC:**
- Before running code: calculate the ISS's altitude by hand from `MEAN_MOTION` (the formula is covered in M2-01)
- Propagate today's ISS snapshot to right now
- Distance from Earth's center minus 6378.137 km → altitude roughly 400 to 430 km, close to your hand calculation
- Speed (length of velocity) about 7.6 to 7.7 km/s

---

## Known simplifications (revisit later, not bugs)
- UT1 treated as equal to UTC (max 0.9 s difference, ~0.4 km on the ground). Fine for the demo.
- Leap seconds inside a propagation interval ignored (at most 1 s error, and rare).
