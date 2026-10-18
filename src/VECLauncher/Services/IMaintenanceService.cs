namespace VECLauncher.Services;

public interface IMaintenanceService
{
    List<MaintenanceService.TargetInfo> Enumerate();
    long TotalSize();
    long Clean(IEnumerable<MaintenanceService.TargetInfo> targets);
    string PrepareUninstall(bool removeExe);
    void RunUninstall(string script);
}

public sealed class MaintenanceServiceAdapter : IMaintenanceService
{
    public List<MaintenanceService.TargetInfo> Enumerate() => MaintenanceService.Enumerate();
    public long TotalSize() => MaintenanceService.TotalSize();
    public long Clean(IEnumerable<MaintenanceService.TargetInfo> targets) => MaintenanceService.Clean(targets);
    public string PrepareUninstall(bool removeExe) => MaintenanceService.PrepareUninstall(removeExe);
    public void RunUninstall(string script) => MaintenanceService.RunUninstall(script);
}
