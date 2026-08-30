using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DirectInputWatcher.Configuration;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDirectInputWatcher(this IServiceCollection services,
        IConfiguration configuration,
        Action<DirectInputWatcherOptions>? setup = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        return services.AddDirectInputWatcher(options =>
        {
            // Bind configuration from appsettings.json
            configuration
                .GetSection(DirectInputWatcherOptions.DefaultSectionName)
                .Bind(options);
            
            
            setup?.Invoke(options);
        });
    }
}
