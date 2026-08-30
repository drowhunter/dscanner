using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DirectInputWatcher;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDirectInputWatcher(
        this IServiceCollection services,
        Action<DirectInputWatcherOptions>? setup = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new DirectInputWatcherOptions();
        setup?.Invoke(options);

        if (string.IsNullOrWhiteSpace(options.DeviceCachePath))
        {
            options.DeviceCachePath = Path.Combine(AppContext.BaseDirectory, "devices.json");
        }

        if (!options.Validate())
        {
            throw new ArgumentException("Invalid DirectInputWatcherOptions configuration.");
        }


        //services.AddLogging();
        services.AddSingleton(options);
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
