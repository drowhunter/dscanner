using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace DirectInputWatcher;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDirectInputWatcher(
        this IServiceCollection services,
        Action<DirectInputWatcherOptions>? setup = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        OptionsBuilder<DirectInputWatcherOptions> optionsBuilder =
            services.AddOptions<DirectInputWatcherOptions>();

        if (setup is not null)
        {
            // Post-configuration runs last, so the setup action overrides any binding or
            // configuration the consumer registered before calling this method.
            optionsBuilder.PostConfigure(setup);
        }

        optionsBuilder.Validate(
            options => options.Validate(),
            "Invalid DirectInputWatcherOptions configuration.");

        // Services that take the options directly resolve the same validated instance.
        services.TryAddSingleton(provider =>
            provider.GetRequiredService<IOptions<DirectInputWatcherOptions>>().Value);

        services.TryAddSingleton<DirectInputContext>();
        services.TryAddSingleton<CooperativeWindowHandle>();
        services.TryAddSingleton<DirectInputDeviceCache>();
        services.TryAddSingleton<DirectInputDeviceFilter>();
        services.TryAddSingleton<DirectInputDeviceEnumerator>();
        services.TryAddSingleton<DirectInputDeviceSessionFactory>();
        services.TryAddSingleton<IUsbDeviceChangeSource, UsbDeviceChangeSource>();
        services.TryAddSingleton<DirectInputWatcherService>();
        services.TryAddSingleton<IDirectInputWatcher>(
            provider => provider.GetRequiredService<DirectInputWatcherService>());

        return services;
    }
}
