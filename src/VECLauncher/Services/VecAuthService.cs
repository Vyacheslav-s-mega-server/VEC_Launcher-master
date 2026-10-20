using System.Threading.Tasks;

namespace VECLauncher.Services;

public static class VecAuthService
{
    public static string DefaultVecServerUrl => VmsAuthService.DefaultVmsServerUrl;

    public static Task<string> GetActiveServerUrlAsync() => VmsAuthService.GetActiveServerUrlAsync();
}