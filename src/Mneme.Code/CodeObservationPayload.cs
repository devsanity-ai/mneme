using System.Text.Json.Serialization;
using Mneme.Contracts;

namespace Mneme.Code;

/// <summary>
/// An attributed, advisory observation about one immutable source revision.
/// Does not grant authority, establish approved architecture, or encode a skill procedure.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CodeObservationPayload : EventPayload
{
    public CodeObservationPayload(
        Guid repositoryId, string sourceRevision, CodeObservationKind kind, string content,
        string? relativePath = null, string? symbol = null, string? evidenceReference = null)
    {
        CodeInput.Repository(repositoryId);
        CodeInput.Revision(sourceRevision);
        CodeInput.Kind(kind);
        CodeInput.Text(content, 2048, nameof(content));
        CodeInput.Scope(relativePath, symbol);
        if (evidenceReference is not null)
            CodeInput.Text(evidenceReference, 1024, nameof(evidenceReference));
        RepositoryId = repositoryId;
        SourceRevision = sourceRevision;
        Kind = kind;
        Content = content;
        RelativePath = relativePath;
        Symbol = symbol;
        EvidenceReference = evidenceReference;
    }

    // Reject a missing kind or a contradictory category instead of silently
    // defaulting wire input to a convention or dropping its claimed category.
    [JsonConstructor]
    private CodeObservationPayload(
        Guid repositoryId, string sourceRevision, string content,
        string? relativePath = null, string? symbol = null, string? evidenceReference = null,
        CodeObservationKind kind = (CodeObservationKind)(-1),
        EpistemicCategory category = EpistemicCategory.Evidence)
        : this(repositoryId, sourceRevision, kind, content, relativePath, symbol, evidenceReference)
    {
        if (category != EpistemicCategory.Evidence)
            throw new ArgumentException("Code observations must remain advisory Evidence.", nameof(category));
    }

    public override EpistemicCategory Category => EpistemicCategory.Evidence;
    public Guid RepositoryId { get; }
    public string SourceRevision { get; }
    public CodeObservationKind Kind { get; }
    public string Content { get; }
    public string? RelativePath { get; }
    public string? Symbol { get; }

    /// <summary>
    /// Untrusted reference text; null means unknown evidence. A provided reference
    /// is not verified, fetched, or treated as another principal's authority.
    /// </summary>
    public string? EvidenceReference { get; }
}
