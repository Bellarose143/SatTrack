# SGP4 verification data (M2-02)

These two files are reference data and must never be edited. Both were downloaded on 2026-11-22.

## SGP4-VER.TLE: the inputs
- 33 test cases (TLEs) with start/stop/step minutes after line 2. `#` lines are comments.
- Taken from `AIAA-2006-6753.zip` (Vallado et al., "Revisiting Spacetrack Report #3", source code
  and test cases, 1,248,284 bytes, last modified 2023-05-10), from
  https://celestrak.org/publications/AIAA/2006-6753/
- Path inside the zip: `sgp4/cpp/testsgp4/SGP4-VER.TLE`. The copies under `TestSGP4/` and `mat/`
  in the same zip are identical (MD5 be5eef57e8bdda975918ee37a10807b6).
- 8,616 bytes, CRLF line endings.

## tcppver.out: the expected answers
- The output of Vallado's C++ test program for every case in SGP4-VER.TLE.
- **Not in the CelesTrak zip.** The current `AIAA-2006-6753.zip` ships C++ results only as per-satellite
  STK `.e` files, plus Fortran/MATLAB/Java versions of the combined output. Vallado's C++ book code
  (`https://celestrak.org/software/vallado/cpp.zip`) doesn't include it either.
- Downloaded instead from the python-sgp4 project (Brandon Rhodes), which keeps Vallado's file for
  its own tests:
  https://raw.githubusercontent.com/brandon-rhodes/python-sgp4/master/sgp4/tcppver.out
- 140,162 bytes, LF line endings, git blob SHA-1 98f20c4cdb6e3c2cf3db42a0cdfe698b69bebb13
  (matches the GitHub API's hash for that file).
- Cross-check against the official 2021 C++ `.e` files in the zip: all 9 near-Earth cases agree
  to the printed digits, except one row of 28057 that differs by 1e-8 km (one rounding step in the
  last printed digit). The only big differences are in deep-space cases (23599 up to ~0.96 km;
  20413 up to 1.2e-7 km). Deep space is out of scope for this project.

## Printed precision
- Time: 8 decimal places (minutes). Position: 8 decimal places (km, so 0.01 mm).
- Velocity: 9 decimal places (km/s, so 1e-9 km/s).

## How SGP4 errors show up
The C++ program prints errors to the console only. In tcppver.out, an error just makes a satellite's
block end before its stop time. `Sgp4VerificationData` works out the time of the error from the
TLE's start/stop/step (`Sgp4VerificationCase.Sgp4ErrorAtMinutes`).
