using System.IO;
using VECLauncher.Models;
using VECLauncher.Services;
using Xunit;

namespace VECLauncher.Tests;

public class InstanceServiceTests
{
    [Fact]
    public void LauncherPaths_Root_EndsWithDotVecLauncher()
    {
        var root = LauncherPaths.Root;
        Assert.EndsWith(".veclauncher", root);
    }

    [Theory]
    [InlineData("My Instance", "My Instance")]
    [InlineData("Instance: With/Bad?Chars*", "Instance_ With_Bad_Chars_")]
    [InlineData("...TrimDots...", "TrimDots")]
    [InlineData("", "Instance")]
    [InlineData("   ", "Instance")]
    public void SanitizeFolderName_SanitizesCorrectly(string input, string expected)
    {
        var result = InstanceService.SanitizeFolderName(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void InstanceDir_UsesSanitizedName()
    {
        var inst = new GameInstance
        {
            Id = "abc12345",
            Name = "VEC Fabric 1.21.1"
        };

        var dir = InstanceService.InstanceDir(inst);
        var expectedDir = Path.Combine(InstanceService.InstancesRoot, "VEC Fabric 1.21.1");

        Assert.Equal(expectedDir, dir);
    }

    [Fact]
    public void InstanceDir_CreatesCleanNamedDirectory()
    {
        var inst = new GameInstance
        {
            Id = "inst-123",
            Name = "VEC Build 1.21"
        };

        var dir = InstanceService.InstanceDir(inst);
        Assert.EndsWith(Path.Combine("instances", "VEC Build 1.21"), dir);
    }

    [Fact]
    public void ScanOrphans_DoesNotFlagKnownInstances()
    {
        var known = new List<GameInstance>
        {
            new() { Id = "inst-1", Name = "Main Build" },
            new() { Id = "inst-2", Name = "Dev Build" }
        };

        var orphans = InstanceService.ScanOrphans(known);

        Assert.DoesNotContain(orphans, o => o.Name == "Main Build" || o.Name == "Dev Build");
    }
}
