# SatTracker

A real-time satellite tracker. The end goal is a Unity app showing real
satellites (ISS, SatGus) on a 3D globe. This repo starts as a plain C#
library: the orbit math is built and tested here first, then dropped into
Unity later.

I'm a game developer (Unity/C#) learning orbital mechanics as I go.
This is a LEARNING project, so how you help matters as much as what you build.

## How to work with me
- Tickets live in `TICKETS.md`. Work one ticket at a time, and only the one I name.
- Each Build ticket is marked **you write** or **delegate**:
  - **delegate**: go ahead and implement it.
  - **you write**: do NOT write the implementation. Help me write it myself:
    explain, answer questions, review my code, and write or fix tests if I ask.
    If I get stuck, give hints before full answers.
- Explain anything orbit- or time-related in plain language. Don't assume I
  know the jargon.
- A ticket is done when every acceptance criterion passes. Don't mark
  tickets done in `TICKETS.md` yourself; I'll do that.

## Project structure
- `SatTracker.Core`: class library with all the math. **No UnityEngine reference, ever.**
- `SatTracker.Core.Tests`: NUnit tests. Run with `dotnet test`.

## Technical rules (these matter, don't relax them)
- `SatTracker.Core` targets **netstandard2.1** and **C# 9** (LangVersion 9)
  so it can be dropped into Unity 6 later. The test project can target any
  installed modern .NET.
- Use `double` for all math. Never `float` in Core.
- Never round orbit values. Keep full precision.
- Put units in names: `MeanMotionRadPerMin`, `InclinationRad`, `AltitudeKm`.
  Convert units once, at the point where data enters the library.
- All times are UTC. Never use `DateTime.Now` in Core. CelesTrak EPOCH strings
  have no `Z` suffix: always parse them explicitly as UTC.
- Missing or invalid input data throws a clear exception. Never a silent default or zero.

## Environment
- Windows, Unity 6.4 (6000.4.8f1), dotnet CLI.
