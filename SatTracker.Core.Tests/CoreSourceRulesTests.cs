using System.Text.RegularExpressions;

namespace SatTracker.Core.Tests
{
    /// <summary>
    /// Guards the "no clock reads in Core" rule. Core takes times as inputs;
    /// only the app edge (Unity side) may read the clock, and only with DateTime.UtcNow.
    /// </summary>
    public class CoreSourceRulesTests
    {
        private static readonly Regex ClockRead =
            new Regex(@"\bDateTime(Offset)?\s*\.\s*(Now|UtcNow|Today)\b", RegexOptions.Compiled);

        private static string FindCoreSourceDir()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "SatTracker.Core");
                if (File.Exists(Path.Combine(candidate, "SatTracker.Core.csproj")))
                    return candidate;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("Could not find SatTracker.Core next to the test output.");
        }

        [Test]
        public void CoreSource_NeverReadsTheClock()
        {
            var coreDir = FindCoreSourceDir();
            var sep = Path.DirectorySeparatorChar;

            var offenders = Directory.EnumerateFiles(coreDir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{sep}obj{sep}") && !f.Contains($"{sep}bin{sep}"))
                .SelectMany(f => File.ReadLines(f).Select((line, i) => (f, i, line)))
                .Where(x => ClockRead.IsMatch(x.line))
                .Select(x => $"{Path.GetRelativePath(coreDir, x.f)}:{x.i + 1}: {x.line.Trim()}")
                .ToList();

            Assert.That(offenders, Is.Empty);
        }
    }
}
