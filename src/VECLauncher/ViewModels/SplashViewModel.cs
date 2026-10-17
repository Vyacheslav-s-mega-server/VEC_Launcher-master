using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using VECLauncher.Services;

namespace VECLauncher.ViewModels;

public partial class SplashViewModel : ObservableObject
{
    private readonly IPreloadService _preloadService;

    [ObservableProperty]
    private double _progressPercent = 0;

    [ObservableProperty]
    private string _progressPercentText = "0%";

    [ObservableProperty]
    private string _statusText = "Инициализация...";

    [ObservableProperty]
    private string _tipText = "Подготовка VEC Launcher...";

    [ObservableProperty]
    private double _progressBarWidth = 0;

    public SplashViewModel(IPreloadService preloadService)
    {
        _preloadService = preloadService;
    }

    public async Task<PreloadResult> RunPipelineAsync(CancellationToken ct = default)
    {
        var progress = new Progress<PreloadStep>(step =>
        {
            ProgressPercent = step.Percent;
            ProgressPercentText = $"{(int)step.Percent}%";
            StatusText = step.Status;
            TipText = step.Tip;
            ProgressBarWidth = 360.0 * Math.Clamp(step.Percent / 100.0, 0, 1.0);
        });

        return await _preloadService.ExecutePipelineAsync(progress, ct);
    }
}
