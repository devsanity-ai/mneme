using Mneme.Hosting.Profiles;
using Mneme.Projections;

namespace Mneme.Assistant;

/// <summary>
/// The personal-assistant <see cref="IMnemeProfile"/> (ADR-0006). A satellite
/// package that teaches base Mneme an <em>agenda / attention</em> ontology: the
/// things a read-only assistant daemon discovers from bounded source batches and
/// surfaces as non-authoritative candidates. The thin slice contributes the
/// waiting-on relation (payload + descriptor + its own projection table +
/// projector); commitments and appointments are later phases.
/// </summary>
/// <remarks>
/// Register with <c>services.AddMneme(…).AddMnemeAssistantProfile();</c>
/// (see <see cref="MnemeAssistantServiceCollectionExtensions"/>). Additive and
/// candidate-first: nothing here is a user-accepted decision; acceptance/curation
/// stays a cockpit action under a separate curation capability.
/// </remarks>
public sealed class PersonalAssistantProfile : IMnemeProfile
{
    /// <inheritdoc/>
    public string Name => "assistant";

    /// <inheritdoc/>
    public IReadOnlyList<IPayloadDescriptor> PayloadDescriptors { get; } =
        new IPayloadDescriptor[] { new WaitingOnObservationDescriptor() };

    /// <inheritdoc/>
    public IReadOnlyList<ISchemaModule> SchemaModules { get; } =
        new ISchemaModule[] { new WaitingOnSchemaModule() };

    /// <inheritdoc/>
    public IReadOnlyList<IProjector> Projectors { get; } =
        new IProjector[] { new WaitingOnProjector() };
}
