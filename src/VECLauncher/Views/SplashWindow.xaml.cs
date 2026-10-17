using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using System.Windows.Threading;
using VECLauncher.Services;
using VECLauncher.ViewModels;

namespace VECLauncher.Views;

public partial class SplashWindow : Window
{
    private sealed class SlimeParticle
    {
        public FrameworkElement Element { get; set; } = null!;
        public double X { get; set; }
        public double Y { get; set; }
        public double Vx { get; set; }
        public double Vy { get; set; }
        public double Life { get; set; }
        public double MaxLife { get; set; }
        public double Size { get; set; }
    }

    private readonly SplashViewModel _viewModel;
    private DispatcherTimer? _splashSlimeTimer;
    private double _slimeTime = 0;
    private bool _isExitingSplash = false;
    private double _exitProgress = 0;
    private bool _wasInAir = false;
    private double _impactWobbleTime = 999;
    private readonly Random _rand = new();
    private readonly List<SlimeParticle> _particles = new();

    public SplashWindow(SplashViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
        Loaded += SplashWindow_Loaded;
    }

    public SplashWindow() : this(App.Services.GetRequiredService<SplashViewModel>())
    {
    }

    private async void SplashWindow_Loaded(object sender, RoutedEventArgs e)
    {
        InitSplashSlimeAnimation();
        await RunLoadingSequenceAsync();
    }

    private void InitSplashSlimeAnimation()
    {
        try
        {
            var slimeModel = Slime3DBuilder.BuildSlimeModel();
            SplashSlimeVisual.Content = slimeModel;

            _splashSlimeTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };

            _splashSlimeTimer.Tick += (s, e) =>
            {
                UpdateParticles();

                if (_isExitingSplash)
                {
                    _exitProgress += 0.035;
                    double jumpY = _exitProgress * 6.5;
                    double jumpX = _exitProgress * 5.8;
                    double jumpZ = -_exitProgress * 4.0;

                    SlimeTranslate.OffsetY = jumpY;
                    SlimeTranslate.OffsetX = jumpX;
                    SlimeTranslate.OffsetZ = jumpZ;

                    SlimeRotateY.Angle += 8;
                    SlimeRotateX.Angle += 4;

                    double shrink = Math.Max(0, 1.0 - _exitProgress * 0.7);
                    SlimeScaleTransform.ScaleX = shrink;
                    SlimeScaleTransform.ScaleY = shrink * 1.25;
                    SlimeScaleTransform.ScaleZ = shrink;

                    SlimeShadowScale.ScaleX = Math.Max(0, 1.0 - _exitProgress * 1.5);
                    SlimeShadowScale.ScaleY = Math.Max(0, 1.0 - _exitProgress * 1.5);
                    SlimeGroundShadow.Opacity = Math.Max(0, 0.5 * (1.0 - _exitProgress * 1.5));
                    return;
                }

                _slimeTime += 0.055;

                double cycle = _slimeTime % (Math.PI * 1.45);

                if (cycle < Math.PI)
                {
                    _wasInAir = true;

                    double progress = cycle / Math.PI;
                    double height = Math.Sin(progress) * 1.15;
                    SlimeTranslate.OffsetY = height;
                    SlimeTranslate.OffsetX = 0;

                    double stretchY = 1.0 + Math.Sin(progress) * 0.20;
                    double stretchXZ = 1.0 / Math.Sqrt(stretchY);
                    SlimeScaleTransform.ScaleX = stretchXZ;
                    SlimeScaleTransform.ScaleY = stretchY;
                    SlimeScaleTransform.ScaleZ = stretchXZ;

                    double shadowFactor = Math.Max(0.1, 1.0 - (height / 1.15) * 0.5);
                    SlimeShadowScale.ScaleX = shadowFactor;
                    SlimeShadowScale.ScaleY = shadowFactor;
                    SlimeGroundShadow.Opacity = 0.5 * shadowFactor;
                }
                else
                {
                    if (_wasInAir)
                    {
                        _wasInAir = false;
                        _impactWobbleTime = 0;
                        SpawnLandingParticles();
                    }

                    _impactWobbleTime += 0.055;

                    double decay = Math.Exp(-_impactWobbleTime * 4.5);
                    double wobble = Math.Sin(_impactWobbleTime * 14.0) * decay;

                    SlimeTranslate.OffsetY = -Math.Max(0, wobble * 0.24);

                    double squashY = 1.0 - wobble * 0.40;
                    double squashXZ = 1.0 + wobble * 0.32;

                    SlimeScaleTransform.ScaleX = squashXZ;
                    SlimeScaleTransform.ScaleY = squashY;
                    SlimeScaleTransform.ScaleZ = squashXZ;

                    double shadowFactor = 1.0 + wobble * 0.4;
                    SlimeShadowScale.ScaleX = shadowFactor;
                    SlimeShadowScale.ScaleY = shadowFactor;
                    SlimeGroundShadow.Opacity = Math.Clamp(0.5 + wobble * 0.3, 0.2, 0.8);
                }

                SlimeRotateY.Angle = 22 + Math.Sin(_slimeTime * 0.8) * 8;
                SlimeRotateX.Angle = 8 + Math.Cos(_slimeTime * 1.1) * 3;
            };

            _splashSlimeTimer.Start();
        }
        catch (Exception ex)
        {
            Log.Warn($"SplashWindow: failed to initialize 3D slime: {ex.Message}");
        }
    }

    private void SpawnLandingParticles()
    {
        double originX = 180.0;
        double originY = 175.0;

        Color[] slimeColors = new[]
        {
            Color.FromRgb(0x4A, 0xDE, 0x80),
            Color.FromRgb(0x22, 0xC5, 0x5E),
            Color.FromRgb(0x86, 0xEF, 0xAC),
            Color.FromRgb(0x16, 0xA3, 0x4A)
        };

        int count = _rand.Next(12, 18);
        for (int i = 0; i < count; i++)
        {
            double angle = _rand.NextDouble() * Math.PI + Math.PI;
            double speed = _rand.NextDouble() * 4.5 + 2.0;
            double size = _rand.NextDouble() * 4.0 + 3.0;

            var color = slimeColors[_rand.Next(slimeColors.Length)];
            var rect = new Rectangle
            {
                Width = size,
                Height = size,
                Fill = new SolidColorBrush(color),
                RadiusX = 1.5,
                RadiusY = 1.5
            };

            Canvas.SetLeft(rect, originX);
            Canvas.SetTop(rect, originY);
            ParticleCanvas.Children.Add(rect);

            _particles.Add(new SlimeParticle
            {
                Element = rect,
                X = originX,
                Y = originY,
                Vx = Math.Cos(angle) * speed * 1.4,
                Vy = Math.Sin(angle) * speed * 0.85,
                Life = 0,
                MaxLife = _rand.Next(18, 30),
                Size = size
            });
        }
    }

    private void UpdateParticles()
    {
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Life++;

            p.X += p.Vx;
            p.Y += p.Vy;
            p.Vy += 0.28;

            double progress = p.Life / p.MaxLife;
            p.Element.Opacity = Math.Max(0, 1.0 - progress);

            Canvas.SetLeft(p.Element, p.X);
            Canvas.SetTop(p.Element, p.Y);

            if (p.Life >= p.MaxLife)
            {
                ParticleCanvas.Children.Remove(p.Element);
                _particles.RemoveAt(i);
            }
        }
    }

    private async Task RunLoadingSequenceAsync()
    {
        try
        {
            var result = await _viewModel.RunPipelineAsync();

            _isExitingSplash = true;
            await Task.Delay(80);

            var mainWindow = App.Services.GetRequiredService<MainWindow>();
            mainWindow.ApplyPreloadResult(result);
            Application.Current.MainWindow = mainWindow;
            mainWindow.Opacity = 1;
            mainWindow.Show();
            mainWindow.Activate();
            mainWindow.Focus();

            _splashSlimeTimer?.Stop();
            _splashSlimeTimer = null;
            Close();
        }
        catch (Exception ex)
        {
            Log.Error("RunLoadingSequenceAsync error: " + ex);
            MessageBox.Show("Ошибка запуска MainWindow: " + ex.Message + "\n" + ex.StackTrace, "VEC Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}