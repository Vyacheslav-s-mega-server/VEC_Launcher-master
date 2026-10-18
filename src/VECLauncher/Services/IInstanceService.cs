using VECLauncher.Models;

namespace VECLauncher.Services;

public interface IInstanceService
{
    bool Loaded { get; }
    string InstancesRoot { get; }
    List<GameInstance> LoadAll();
    void SaveAll(List<GameInstance> instances);
    List<GameInstance> ScanOrphans(List<GameInstance> known);
    void EnsureFolders(GameInstance inst);
    void Delete(GameInstance inst, bool deleteFiles);
    string InstanceDir(GameInstance inst);
    string ModsDir(GameInstance inst);
    string ResourcePacksDir(GameInstance inst);
    string ShaderPacksDir(GameInstance inst);
    string SavesDir(GameInstance inst);
    string ScreenshotsDir(GameInstance inst);
    string LogsDir(GameInstance inst);
    string ConfigDir(GameInstance inst);
    void OpenFolder(string path);
    void RevealFile(string path);
}

public sealed class InstanceServiceAdapter : IInstanceService
{
    public bool Loaded => InstanceService.Loaded;
    public string InstancesRoot => InstanceService.InstancesRoot;
    public List<GameInstance> LoadAll() => InstanceService.LoadAll();
    public void SaveAll(List<GameInstance> instances) => InstanceService.SaveAll(instances);
    public List<GameInstance> ScanOrphans(List<GameInstance> known) => InstanceService.ScanOrphans(known);
    public void EnsureFolders(GameInstance inst) => InstanceService.EnsureFolders(inst);
    public void Delete(GameInstance inst, bool deleteFiles) => InstanceService.Delete(inst, deleteFiles);
    public string InstanceDir(GameInstance inst) => InstanceService.InstanceDir(inst);
    public string ModsDir(GameInstance inst) => InstanceService.ModsDir(inst);
    public string ResourcePacksDir(GameInstance inst) => InstanceService.ResourcePacksDir(inst);
    public string ShaderPacksDir(GameInstance inst) => InstanceService.ShaderPacksDir(inst);
    public string SavesDir(GameInstance inst) => InstanceService.SavesDir(inst);
    public string ScreenshotsDir(GameInstance inst) => InstanceService.ScreenshotsDir(inst);
    public string LogsDir(GameInstance inst) => InstanceService.LogsDir(inst);
    public string ConfigDir(GameInstance inst) => InstanceService.ConfigDir(inst);
    public void OpenFolder(string path) => InstanceService.OpenFolder(path);
    public void RevealFile(string path) => InstanceService.RevealFile(path);
}
