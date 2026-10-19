using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;
using VECLauncher.Services;

namespace VECLauncher.Views;

public partial class MainWindow
{
    public void SetPreloadedManifest(List<BuildManifestFile> files)
    {
        Vm.Content?.SetPreloadedManifest(files);
    }

    private void ContentKind_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _ = Vm.SelectContentKindCommand.ExecuteAsync((sender as FrameworkElement)?.Tag?.ToString());
    }

    private void RefreshContent(bool force = false)
    {
        _ = Vm.RefreshContentAsync(force);
    }

    private void Content_DragOver(object sender, DragEventArgs e)
    {
        var hasFiles = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = hasFiles && _selectedInstance is not null ? DragDropEffects.Copy : DragDropEffects.None;

        if (hasFiles && _selectedInstance is not null)
        {
            DropHint.Visibility = Visibility.Visible;
            TxtDropTarget.Text = $"в сборку «{_selectedInstance.Name}»  ·  .jar → моды, .zip → ресурспаки или шейдеры";
        }

        e.Handled = true;
    }

    private void Content_Drop(object sender, DragEventArgs e)
    {
        DropHint.Visibility = Visibility.Collapsed;

        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (_selectedInstance is null)
        {
            _dialog.ShowMessage("Сначала выберите сборку.", "Сборка не выбрана");
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            _ = Vm.ImportContentFilesCommand.ExecuteAsync(files);

        e.Handled = true;
    }
}
