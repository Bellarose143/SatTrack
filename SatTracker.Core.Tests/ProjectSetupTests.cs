using System;
using System.Linq;
using System.Reflection;

namespace SatTracker.Core.Tests;

/// <summary>
/// Placeholder tests for M1-02. They also guard the Unity drop-in rules,
/// so they stay useful after the real math arrives.
/// </summary>
public class ProjectSetupTests
{
    private static Assembly LoadCore() => Assembly.Load("SatTracker.Core");

    [Test]
    public void Placeholder_TestRunnerWorks()
    {
        Assert.That(1 + 1, Is.EqualTo(2));
    }

    [Test]
    public void Core_TargetsNetStandard21()
    {
        // .NET doesn't stamp a TargetFramework attribute on netstandard libraries,
        // so check the "netstandard" facade it compiled against instead (2.1.0.0 = netstandard2.1).
        var netstandard = LoadCore().GetReferencedAssemblies().SingleOrDefault(a => a.Name == "netstandard");

        Assert.That(netstandard, Is.Not.Null, "Core should compile against netstandard");
        Assert.That(netstandard!.Version, Is.EqualTo(new Version(2, 1, 0, 0)));
    }

    [Test]
    public void Core_DoesNotReferenceUnity()
    {
        var referenced = LoadCore().GetReferencedAssemblies().Select(a => a.Name);

        Assert.That(referenced, Has.None.StartsWith("UnityEngine"));
        Assert.That(referenced, Has.None.StartsWith("UnityEditor"));
    }
}
