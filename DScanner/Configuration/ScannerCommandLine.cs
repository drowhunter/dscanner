using System.CommandLine;

namespace DScanner.Configuration;

public static class ScannerCommandLine
{
    public static Task<int> InvokeAsync(string[] args, Func<ScannerCommandLineOverrides, CancellationToken, Task> runScanner)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(runScanner);

        Option<int?> pollFrequencyOption = new("--poll-frequency-hz", ["--poll-frequency"])
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

        RootCommand rootCommand = new("Scans DirectInput game controllers and identifies button, axis, and POV input.")
        {
            pollFrequencyOption,
            axisChangeOption,
            axisResetOption
        };

        rootCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            ScannerCommandLineOverrides overrides = new(
                parseResult.GetValue(pollFrequencyOption),
                parseResult.GetValue(axisChangeOption),
                parseResult.GetValue(axisResetOption));

            await runScanner(overrides, cancellationToken);
        });

        return rootCommand.Parse(args).InvokeAsync();
    }
}
