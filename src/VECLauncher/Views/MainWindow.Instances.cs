using System.Threading.Tasks;
using VECLauncher.Models;

namespace VECLauncher.Views;

public partial class MainWindow
{
    private async Task LoadVersionsAsync()
    {
        await Vm.LoadVersionsCommand.ExecuteAsync(null);
    }

    private void LoadInstances()
    {
        Vm.LoadInstancesCommand.Execute(null);
    }

    private void SelectInstance(GameInstance inst)
    {
        Vm.SelectInstance(inst);
    }
}
