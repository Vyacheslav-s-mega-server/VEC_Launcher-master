using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using VECLauncher.Models;
using VECLauncher.Services;
using VECLauncher.ViewModels;
using Xunit;

namespace VECLauncher.Tests;

public class ContentServiceTests
{
    private readonly HttpClient _http = new();

    private class TestInstanceService : IInstanceService
    {
        public bool Loaded => true;
        public string InstancesRoot => BaseDir;
        public string BaseDir { get; } = Path.Combine(Path.GetTempPath(), "vec_test_" + Path.GetRandomFileName());
        public List<GameInstance> LoadAll() => new();
        public void SaveAll(List<GameInstance> instances) { }
        public void EnsureFolders(GameInstance instance) { Directory.CreateDirectory(BaseDir); }
        public List<GameInstance> ScanOrphans(List<GameInstance> known) => new();
        public void Delete(GameInstance inst, bool deleteFiles) { }
        public string InstanceDir(GameInstance inst) => BaseDir;
        public string ModsDir(GameInstance instance) => Path.Combine(BaseDir, "mods");
        public string ResourcePacksDir(GameInstance instance) => Path.Combine(BaseDir, "resourcepacks");
        public string ShaderPacksDir(GameInstance instance) => Path.Combine(BaseDir, "shaderpacks");
        public string SavesDir(GameInstance instance) => Path.Combine(BaseDir, "saves");
        public string ScreenshotsDir(GameInstance inst) => Path.Combine(BaseDir, "screenshots");
        public string LogsDir(GameInstance inst) => Path.Combine(BaseDir, "logs");
        public string ConfigDir(GameInstance inst) => Path.Combine(BaseDir, "config");
        public void OpenFolder(string path) { }
        public void RevealFile(string path) { }
    }

    [Fact]
    public void GetCategoryCounts_CalculatesAccurately()
    {
        var svc = new ContentService(_http, new TestInstanceService());
        var files = new List<BuildManifestFile>
        {
            new() { Path = "mods/test1.jar" },
            new() { Path = "mods/test2.jar" },
            new() { Path = "resourcepacks/pack1.zip" },
            new() { Path = "custom/pack2.zip", Category = "ResourcePacks" },
            new() { Path = "shaderpacks/shader1.zip" }
        };

        var (mods, rp, shaders) = svc.GetCategoryCounts(files);

        Assert.Equal(2, mods);
        Assert.Equal(2, rp);
        Assert.Equal(1, shaders);
    }

    [Fact]
    public void SetPreloadedManifest_CachesManifest()
    {
        var svc = new ContentService(_http, new TestInstanceService());
        var files = new List<BuildManifestFile>
        {
            new() { Path = "mods/test.jar", FileName = "test.jar" }
        };

        svc.SetPreloadedManifest(files);
        var manifest = svc.FetchBuildManifestAsync(null, force: false).GetAwaiter().GetResult();

        Assert.Single(manifest);
        Assert.Equal("test.jar", manifest[0].FileName);
    }

    [Fact]
    public async Task ToggleContentItem_TogglesDisabledExtension()
    {
        var instSvc = new TestInstanceService();
        var svc = new ContentService(_http, instSvc);
        var tempDir = Path.Combine(Path.GetTempPath(), "vec_test_" + Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "sample.jar");
        await File.WriteAllTextAsync(filePath, "dummy");

        var item = new ContentTileItemViewModel
        {
            Name = "sample",
            Path = filePath,
            ToggleText = "Выключить"
        };

        await svc.ToggleContentItemAsync(item, null);

        Assert.True(item.Path.EndsWith(".disabled"));
        Assert.Equal("Включить", item.ToggleText);
        Assert.True(File.Exists(item.Path));
        Assert.False(File.Exists(filePath));

        // Toggle back
        await svc.ToggleContentItemAsync(item, null);

        Assert.False(item.Path.EndsWith(".disabled"));
        Assert.Equal("Выключить", item.ToggleText);
        Assert.True(File.Exists(filePath));

        try { Directory.Delete(tempDir, true); } catch { }
    }

    [Fact]
    public async Task ImportFiles_CopiesValidFiles()
    {
        var instSvc = new TestInstanceService();
        var svc = new ContentService(_http, instSvc);
        var tempDir = Path.Combine(Path.GetTempPath(), "vec_test_import_" + Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        var dummyMod = Path.Combine(tempDir, "mymod.jar");
        await File.WriteAllTextAsync(dummyMod, "mod data");

        var inst = new GameInstance { Id = "test-inst", Name = "Test" };
        var report = await svc.ImportFilesAsync(inst, new[] { dummyMod });

        Assert.Contains("Добавлено: 1", report);
        var targetFile = Path.Combine(instSvc.ModsDir(inst), "mymod.jar");
        Assert.True(File.Exists(targetFile));

        try { Directory.Delete(tempDir, true); } catch { }
        try { Directory.Delete(instSvc.BaseDir, true); } catch { }
    }
}
