using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyHi.Companion.Core.Data;
using MyHi.Companion.Data;
using MyHi.Companion.Features.Bluetooth;
using MyHi.Companion.Features.Diagnostics;
using MyHi.Companion.Features.Shared;
using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;

namespace MyHi.Companion;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        var fileLoggerProvider = new RollingFileLoggerProvider(Path.Combine(FileSystem.AppDataDirectory, "logs"));
        builder.Services.AddSingleton(fileLoggerProvider);
        builder.Logging.AddProvider(fileLoggerProvider);

#if DEBUG
        builder.Logging.AddDebug();
#endif

        // ---- Bluetooth ----
        builder.Services.AddSingleton(CrossBluetoothLE.Current);
        builder.Services.AddSingleton<BluetoothReadinessService>();
        builder.Services.AddSingleton<BleScanner>();
        builder.Services.AddSingleton<TreadmillConnection>();
        builder.Services.AddSingleton<ControlPointClient>();

        // ---- Diagnostics ----
        builder.Services.AddSingleton(_ => new CaptureSessionManager(Path.Combine(FileSystem.AppDataDirectory, "captures")));

        // ---- Data ----
        builder.Services.AddSingleton(_ => new SqliteConnectionFactory(Path.Combine(FileSystem.AppDataDirectory, "myhi.db")));
        builder.Services.AddSingleton<AppDatabase>();

        // ---- Shared ----
        builder.Services.AddTransient<HomeViewModel>();
        builder.Services.AddTransient<HomePage>();

        // ---- Bluetooth pages ----
        builder.Services.AddTransient<ScanViewModel>();
        builder.Services.AddTransient<ScanPage>();

        // ---- Diagnostics pages ----
        builder.Services.AddTransient<GattTreeViewModel>();
        builder.Services.AddTransient<GattTreePage>();
        builder.Services.AddTransient<ReadDumpViewModel>();
        builder.Services.AddTransient<ReadDumpPage>();
        builder.Services.AddTransient<NotificationLogViewModel>();
        builder.Services.AddTransient<NotificationLogPage>();
        builder.Services.AddTransient<ControlConsoleViewModel>();
        builder.Services.AddTransient<ControlConsolePage>();
        builder.Services.AddTransient<ProbeChecklistViewModel>();
        builder.Services.AddTransient<ProbeChecklistPage>();
        builder.Services.AddTransient<CaptureSessionsViewModel>();
        builder.Services.AddTransient<CaptureSessionsPage>();

        var app = builder.Build();

        app.Services.GetRequiredService<AppDatabase>().Initialize();

        return app;
    }
}
