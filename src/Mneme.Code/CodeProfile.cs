using Mneme.Hosting.Profiles;
using Mneme.Projections;

namespace Mneme.Code;

/// <summary>Trusted compiled profile for revision-scoped advisory code observations.</summary>
public sealed class CodeProfile : IMnemeProfile
{
    public string Name => "code";
    public IReadOnlyList<IPayloadDescriptor> PayloadDescriptors { get; } =
        Array.AsReadOnly<IPayloadDescriptor>([new CodeObservationDescriptor()]);
    public IReadOnlyList<ISchemaModule> SchemaModules { get; } =
        Array.AsReadOnly<ISchemaModule>([new CodeSchemaModule()]);
    public IReadOnlyList<IProjector> Projectors { get; } =
        Array.AsReadOnly<IProjector>([new CodeObservationProjector()]);
}
