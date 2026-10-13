using System.Net.Http;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RaceIngenior.Models;
using RaceIngenior.Radio;
using RaceIngenior.Services;
using RaceIngenior.Telemetry;
using RaceIngenior.ViewModels;

namespace RaceIngenior;

public partial class App : System.Windows.Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = Host.CreateDefaultBuilder(e.Args)
            .ConfigureAppConfiguration((_, config) =>
            {
                config.SetBasePath(AppContext.BaseDirectory);
                config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                config.AddEnvironmentVariables(prefix: "RACEINGENIOR_");
            })
            .ConfigureServices((context, services) =>
            {
                services.AddSingleton<UserPreferencesStore>();
                services.AddOptions<AppSettings>()
                    .Bind(context.Configuration.GetSection(AppSettings.SectionName))
                    .Configure<UserPreferencesStore>((settings, prefs) =>
                    {
                        settings.GeminiApiKey = "";
                        prefs.ApplyTo(settings);
                    });

                services.AddSingleton<TelemetryHub>();
                services.AddSingleton<SharedMemoryTelemetrySource>();
                services.AddSingleton<TrackLayoutStore>();
                services.AddSingleton<TrackLayoutLearner>();
                services.AddSingleton<TrackRecordsStore>();
                services.AddSingleton<BrandLogoStore>();
                services.AddSingleton<SessionReportService>();
                services.AddHttpClient<LmuRestClient>();
                services.AddHttpClient("gemini");

                services.AddSingleton<ITtsService, WindowsTtsService>();
                services.AddSingleton<ISttService, WhisperSttService>();
                services.AddSingleton<RuleEngineService>();
                services.AddSingleton<EngineerMemory>();
                services.AddSingleton<TelemetryPromptBuilder>();

                services.AddSingleton<GeminiEngineerService>(sp =>
                {
                    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("gemini");
                    return new GeminiEngineerService(
                        http,
                        sp.GetRequiredService<IOptions<AppSettings>>(),
                        sp.GetRequiredService<EngineerMemory>(),
                        sp.GetRequiredService<TelemetryPromptBuilder>(),
                        sp.GetRequiredService<ILogger<GeminiEngineerService>>());
                });
                services.AddSingleton<IEngineerLlm>(sp => sp.GetRequiredService<GeminiEngineerService>());

                services.AddSingleton<WheelPttService>();
                services.AddHostedService(sp => sp.GetRequiredService<WheelPttService>());

                services.AddSingleton<RadioPipelineService>();
                services.AddHostedService(sp => sp.GetRequiredService<RadioPipelineService>());

                services.AddHostedService<TelemetryHostedService>();

                services.AddSingleton<OverlayViewModel>();
                services.AddSingleton<OverlayService>();
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        await _host.StartAsync();

        // Seed personal bests from any existing session reports.
        var records = _host.Services.GetRequiredService<TrackRecordsStore>();
        var reports = _host.Services.GetRequiredService<SessionReportService>();
        records.MergeFromReports(reports.ListReports(), reports.TryLoadReport);

        var window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();

        _host.Services.GetRequiredService<OverlayService>().RestoreFromPreferences();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(3));
            _host.Dispose();
        }

        base.OnExit(e);
    }
}
