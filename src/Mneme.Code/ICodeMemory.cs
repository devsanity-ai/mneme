using Mneme.Contracts;

namespace Mneme.Code;

/// <summary>Read-only typed code memory; ingestion uses the existing IMemoryAgent.</summary>
public interface ICodeMemory
{
    /// <summary>
    /// Returns at most query.Limit independently visible advisory observations.
    /// Other source revisions, revoked items, and non-approved review entries
    /// are excluded. Conflicts are preserved, not ranked into an authority.
    /// In a review-mode workstream, epistemic observations require an approved
    /// queue entry even when they predate the current review policy.
    /// </summary>
    Task<IReadOnlyList<CodeObservation>> FindAsync(
        CodeMemoryQuery query, CapabilityToken capability, CancellationToken ct = default);
}
