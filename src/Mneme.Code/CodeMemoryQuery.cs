using Mneme.Contracts;

namespace Mneme.Code;

/// <summary>
/// Current-clock read for one host-validated source revision, not an as-of snapshot.
/// A path includes repository-wide observations; a symbol includes file-wide ones.
/// Null path means repository-wide only. All scope matching is ordinal and exact.
/// </summary>
public sealed record CodeMemoryQuery
{
    public CodeMemoryQuery(
        WorkstreamId workstream, Guid repositoryId, string currentRevision,
        CodeObservationKind? kind = null, string? relativePath = null, string? symbol = null,
        int limit = 50)
    {
        if (!CodeInput.IsWorkstream(workstream.Value))
            throw new ArgumentException("A valid workstream identity is required.", nameof(workstream));
        CodeInput.Repository(repositoryId);
        CodeInput.Revision(currentRevision);
        if (kind.HasValue) CodeInput.Kind(kind.Value);
        CodeInput.Scope(relativePath, symbol);
        if (limit is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(limit));
        Workstream = workstream;
        RepositoryId = repositoryId;
        CurrentRevision = currentRevision;
        Kind = kind;
        RelativePath = relativePath;
        Symbol = symbol;
        Limit = limit;
    }

    public WorkstreamId Workstream { get; }
    public Guid RepositoryId { get; }
    public string CurrentRevision { get; }
    public CodeObservationKind? Kind { get; }
    public string? RelativePath { get; }
    public string? Symbol { get; }
    public int Limit { get; }
}
