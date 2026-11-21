# Tickets

Status: ⬜ todo · 🟨 in progress · ✅ done
Owner: **you write** = learning piece, write it yourself (or understand every line) · **delegate** = plumbing, Claude Code can do it

---

## Milestone 1: Time + reading the snapshot (Nov 16 to 22)

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

### ⬜ M1-06 (Verify): Sanity check against reality

Download today's ISS JSON, parse it with your code, print the fields.
**AC:**

- Printed values match the CelesTrak page by eye
- Orbit period = 1440 ÷ mean motion (rev/day) comes out near 92.9 minutes
- Snapshot age (minutes since epoch, right now) is less than ~2 days

---

## Known simplifications (revisit later, not bugs)

- UT1 treated as equal to UTC (max 0.9 s difference, ~0.4 km on the ground). Fine for the demo.
- Leap seconds inside a propagation interval ignored (at most 1 s error, and rare).
