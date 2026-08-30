using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DirectInputWatcher.Tests;

public sealed class DirectInputWatcherRegistrationTests
{
    [Fact]
    public void ConfigurationSectionBindsOptions()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Watcher:PollFrequency"] = "12",
                    ["Watcher:Whitelist:0"] = "346E:0003"
                })
            .Build();
        ServiceCollection services = new();

        services
            .AddOptions<DirectInputWatcherOptions>()
            .Bind(configuration.GetSection("Watcher"));
        services.AddDirectInputWatcher();

        using ServiceProvider provider = services.BuildServiceProvider();
        DirectInputWatcherOptions options = provider
            .GetRequiredService<IOptions<DirectInputWatcherOptions>>()
            .Value;

        Assert.Equal(12, options.PollFrequency);
        Assert.Equal(
            new VidPid(0x346E, 0x0003),
            Assert.Single(options.Whitelist));
    }

    [Fact]
    public void SetupActionReceivesConfiguredOptionsAndOverridesThem()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Watcher:PollFrequency"] = "10",
                    ["Watcher:AxisChangeThreshold"] = "0.30",
                    ["Watcher:Blacklist:0"] = "045E:02FF"
                })
            .Build();
        ServiceCollection services = new();
        int configuredPollFrequency = 0;

        services
            .AddOptions<DirectInputWatcherOptions>()
            .Bind(configuration.GetSection("Watcher"));
        services.AddDirectInputWatcher(options =>
        {
            configuredPollFrequency = options.PollFrequency;
            options.PollFrequency = 20;
            options.AxisChangeThreshold = 0.40;
            options.AxisResetThreshold = 0.15;
            options.Whitelist.Add(new VidPid(0x346E, 0x0003));
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        DirectInputWatcherOptions options = provider
            .GetRequiredService<IOptions<DirectInputWatcherOptions>>()
            .Value;

        Assert.Equal(10, configuredPollFrequency);
        Assert.Equal(20, options.PollFrequency);
        Assert.Equal(0.40, options.AxisChangeThreshold);
        Assert.Equal(0.15, options.AxisResetThreshold);
        Assert.Contains(
            options.Whitelist,
            value => value == new VidPid(0x346E, 0x0003));
        Assert.Contains(
            options.Blacklist,
            value => value == new VidPid(0x045E, 0x02FF));
    }

    [Fact]
    public void RegistrationProvidesSensibleDefaults()
    {
        ServiceCollection services = new();
        services.AddDirectInputWatcher();

        using ServiceProvider provider = services.BuildServiceProvider();
        DirectInputWatcherOptions options = provider
            .GetRequiredService<IOptions<DirectInputWatcherOptions>>()
            .Value;

        Assert.Equal(15, options.PollFrequency);
        Assert.Equal(0.25, options.AxisChangeThreshold);
        Assert.Equal(0.20, options.AxisResetThreshold);
        Assert.Equal(TimeSpan.FromSeconds(1), options.AxisBaselineCalibrationDuration);
        Assert.Null(options.DeviceCachePath);
        Assert.Empty(options.Whitelist);
        Assert.Empty(options.Blacklist);
    }

    [Fact]
    public void InvalidSetupConfigurationFailsValidation()
    {
        ServiceCollection services = new();
        services.AddDirectInputWatcher(options =>
        {
            options.AxisChangeThreshold = 0.10;
            options.AxisResetThreshold = 0.20;
        });

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider
            .GetRequiredService<IOptions<DirectInputWatcherOptions>>()
            .Value);
    }
}
