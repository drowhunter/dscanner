using DirectInputWatcher;
using DirectInputWatcher.Configuration;
using DScanner.Configuration;
using DScanner.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

return await ScannerCommandLine.InvokeAsync(args, RunScannerAsync);

static async Task RunScannerAsync(ScannerCommandLineOverrides commandLine, CancellationToken cancellationToken)
{
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
    builder.Services.AddSingleton<ConsoleUiService>();
    builder.Services.AddSingleton<IConsoleUi>(services => services.GetRequiredService<ConsoleUiService>());
    builder.Services.AddHostedService(services => services.GetRequiredService<ConsoleUiService>());
    builder.Services.AddHostedService<ConsoleQuitService>();
    builder.Services.AddHostedService<ControllerScannerService>();

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
