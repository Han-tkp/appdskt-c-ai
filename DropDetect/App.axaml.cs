using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using System;
using DropDetect.Services;
using DropDetect.ViewModels;

namespace DropDetect;

public partial class App : Application
{
    public static IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = Services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = new MainWindow
            {
                DataContext = vm
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Core Services
        services.AddSingleton<ICalibrationService, CalibrationService>();
        services.AddSingleton<IAnalysisService, AnalysisService>();
        services.AddSingleton<IInferenceService, InferenceService>();
        services.AddSingleton<IVisionService, VisionService>();
        services.AddSingleton<IExcelExportService, ExcelExportService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<AppStateManager>();
        services.AddSingleton<IProjectManagerService, ProjectManagerService>();
        services.AddSingleton<IAutoSaveService, AutoSaveService>();

        // ViewModels
        services.AddTransient<MainWindowViewModel>();
    }
}