using DirectInputWatcher;
using DirectInputWatcher.Configuration;
using DScanner.Configuration;
using DScanner.Mapping;
using DScanner.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

return await ScannerCommandLine.InvokeAsync(args, RunScannerAsync);

static async Task RunScannerAsync(ScannerCommandLineOverrides commandLine, CancellationToken cancellationToken)
{
    if (commandLine.Map && Console.IsInputRedirected)
    {
        await Console.Error.WriteLineAsync(
            "--map needs an interactive console; console input is redirected.");
        Environment.ExitCode = 1;
        return;
    }

    var builder = Host.CreateApplicationBuilder();
    ConfigureLogging(builder);

    string cacheFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DScanner\\devices.json");

#if !USEAPPSETTINGS
    builder.Services.AddDirectInputWatcher(options =>
    {
        options.DeviceCachePath = cacheFolder;
        commandLine.ApplyTo(options);
    });
#else
    builder.Services.AddDirectInputWatcher(builder.Configuration, options =>
    {
        options.DeviceCachePath = cacheFolder;
        commandLine.ApplyTo(options);
    });
#endif

    builder.Services.AddSingleton<IConsoleKeySource, ConsoleKeySource>();
    builder.Services.AddSingleton<ConsoleKeyPump>();
    builder.Services.AddSingleton<IConsoleKeyDispatcher>(services => services.GetRequiredService<ConsoleKeyPump>());
    builder.Services.AddHostedService(services => services.GetRequiredService<ConsoleKeyPump>());
    builder.Services.AddSingleton<ConsoleUiService>();
    builder.Services.AddSingleton<IConsoleUi>(services => services.GetRequiredService<ConsoleUiService>());
    builder.Services.AddHostedService(services => services.GetRequiredService<ConsoleUiService>());
    builder.Services.AddHostedService<ControllerScannerService>();

    if (commandLine.Map)
    {
        builder.Services.Configure<DeviceMappingSettings>(commandLine.ApplyTo);
        builder.Services.AddSingleton<IDeviceMappingStore, DeviceMappingStore>();
        builder.Services.AddHostedService<DeviceMappingService>();
    }

    await builder.Build().RunAsync(cancellationToken);
}

static void ConfigureLogging(HostApplicationBuilder builder)
{
    string logDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "DScanner",
        "logs");

    Directory.CreateDirectory(logDirectory);

    string logFilePath = Path.Combine(logDirectory, "dscanner.log");

    File.Delete(logFilePath);

    builder.Logging.ClearProviders();
    builder.Services.AddSerilog((_, configuration) => configuration
        .MinimumLevel.Information()
        .Enrich.FromLogContext()
        .WriteTo.File(
            logFilePath,
            shared: false,
            outputTemplate: "[{Timestamp: HH:mm:ss.fff}] [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}"));
}
