using System.Windows.Input;

namespace VECLauncher.Views;

public partial class MainWindow
{
    private void BorderSideUser_Click(object sender, MouseButtonEventArgs e)
    {
        Vm.ToggleSavedAccountsPopupCommand.Execute(null);
    }
}
