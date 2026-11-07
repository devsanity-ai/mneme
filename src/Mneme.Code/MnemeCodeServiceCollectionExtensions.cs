using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mneme.Hosting.Profiles;

namespace Mneme.Code;

public static class MnemeCodeServiceCollectionExtensions
{
    /// <summary>Register after AddMneme; enables the profile and its read-only ICodeMemory.</summary>
    public static IServiceCollection AddMnemeCodeProfile(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(service => service.ServiceType == typeof(CodeProfile)))
            return services;
        var profile = new CodeProfile();
        services.AddMnemeProfile(profile);
        services.AddSingleton(profile);
        services.TryAddSingleton<ICodeMemory, CodeMemory>();
        return services;
    }
}
