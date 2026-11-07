using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mneme.Hosting.Profiles;
using Mneme.Storage;

namespace Mneme.Assistant;

/// <summary>
/// DI ergonomics for the personal-assistant profile. Call <em>after</em>
/// <c>AddMneme</c>:
/// <code>
/// services.AddMneme(o => { … })
///         .AddMnemeAssistantProfile();
/// </code>
/// It registers the <see cref="PersonalAssistantProfile"/> (payload descriptor +
/// waiting-on schema module + projector) via the base <c>AddMnemeProfile</c> and
/// wires <see cref="IAssistantMemory"/> for the agenda read surface.
/// </summary>
public static class MnemeAssistantServiceCollectionExtensions
{
    /// <summary>Register the Mneme personal-assistant profile and its read surface.</summary>
    public static IServiceCollection AddMnemeAssistantProfile(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddMnemeProfile<PersonalAssistantProfile>();
        services.TryAddSingleton<IAssistantMemory>(sp => new AssistantMemory(
            sp.GetRequiredService<SqliteConnectionFactory>(),
            sp.GetRequiredService<TimeProvider>()));
        return services;
    }
}
