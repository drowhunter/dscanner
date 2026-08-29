using DScanner.Configuration;
using DScanner.DirectInput;
using DScanner.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using System.CommandLine;

Option<int?> pollFrequencyOption = new(
    "--poll-frequency-hz",
    ["--poll-frequency"])
{
    Description = "Controller polling frequency in samples per second. Default: 15.",
    HelpName = "HZ"
};

Option<double?> axisChangeOption = new("--axis-change-threshold")
{
    Description = "Normalized change from an axis baseline that triggers a log. Range: >0 to 2. Default: 0.25.",
    HelpName = "VALUE"
};

Option<double?> axisResetOption = new("--axis-reset-threshold")
{
    Description = "Normalized distance from baseline that rearms an axis. Must be lower than the change threshold. Default: 0.20.",
    HelpName = "VALUE"
};

RootCommand rootCommand = new(
    "Scans DirectInput game controllers and identifies button, axis, and POV input.");
rootCommand.Add(pollFrequencyOption);
rootCommand.Add(axisChangeOption);
rootCommand.Add(axisResetOption);

rootCommand.SetAction(async (parseResult, cancellationToken) =>
{
    ScannerCommandLineOverrides overrides = new(
        parseResult.GetValue(pollFrequencyOption),
        parseResult.GetValue(axisChangeOption),
        parseResult.GetValue(axisResetOption));

    await RunScannerAsync(overrides, cancellationToken);
});

return await rootCommand.Parse(args).InvokeAsync();

static async Task RunScannerAsync(
    ScannerCommandLineOverrides commandLine,
    CancellationToken cancellationToken)
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder();
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

    builder.Services
        .AddOptions<ScannerOptions>()
        .Bind(builder.Configuration.GetSection(ScannerOptions.SectionName))
        .Configure(options => commandLine.ApplyTo(options))
        .Validate(
            options => options.PollFrequencyHz > 0,
            $"{nameof(ScannerOptions.PollFrequencyHz)} must be greater than zero.")
        .Validate(
            options => options.AxisChangeThreshold is > 0 and <= 2,
            $"{nameof(ScannerOptions.AxisChangeThreshold)} must be between 0 and 2.")
        .Validate(
            options => options.AxisResetThreshold >= 0
                && options.AxisResetThreshold < options.AxisChangeThreshold,
            $"{nameof(ScannerOptions.AxisResetThreshold)} must be non-negative and lower than the change threshold.")
        .ValidateOnStart();

    builder.Services.AddSingleton<DirectInputContext>();
    builder.Services.AddSingleton<CooperativeWindowHandle>();
    builder.Services.AddSingleton<DirectInputDeviceCache>();
    builder.Services.AddSingleton<DirectInputDeviceEnumerator>();
    builder.Services.AddSingleton<DirectInputDeviceSessionFactory>();
    builder.Services.AddSingleton<IDeviceChangeObservable, UsbDeviceChangeObservable>();
    builder.Services.AddSingleton<IConsoleKeySource, ConsoleKeySource>();
    builder.Services.AddSingleton<ConsoleUiService>();
    builder.Services.AddSingleton<IConsoleUi>(
        services => services.GetRequiredService<ConsoleUiService>());
    builder.Services.AddHostedService(
        services => services.GetRequiredService<ConsoleUiService>());
    builder.Services.AddHostedService<ConsoleQuitService>();
    builder.Services.AddHostedService<ControllerScannerService>();

    await builder.Build().RunAsync(cancellationToken);
}
