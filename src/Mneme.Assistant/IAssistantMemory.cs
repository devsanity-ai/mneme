using Mneme.Contracts;

namespace Mneme.Assistant;

/// <summary>
/// The personal-assistant domain's read surface over Mneme (ADR-0006). The
/// thin-slice entry point is <see cref="GetOpenWaitingOnAsync"/> — the agenda
/// query "what am I waiting on, from whom, and what's overdue as of now?" which
/// base Mneme's <c>QueryAsync</c> cannot express (it filters semantically + on
/// <c>valid_at</c>, with no counterparty/due-date grouping). Later phases add
/// commitment and appointment reads here.
/// </summary>
/// <remarks>
/// <para>
/// Capability-guarded like every Mneme read (locked decision #8): the token
/// scopes the caller to a workstream and its principal is the viewer used to
/// enforce per-event visibility.
/// </para>
/// <para>
/// All results are <b>non-authoritative proposals</b> (candidate-first posture):
/// each carries a <see cref="WaitingOnItem.Confidence"/> and reflects what a
/// read-only daemon observed, not a user-accepted decision.
/// </para>
/// </remarks>
public interface IAssistantMemory
{
    /// <summary>
    /// List the open waiting-on items in the token's workstream: dependencies
    /// whose latest lifecycle status is still <see cref="WaitingOnStatus.Waiting"/>,
    /// ordered by due date (items with a deadline first, soonest first). Each is
    /// flagged <see cref="WaitingOnItem.IsOverdue"/> relative to
    /// <paramref name="asOf"/>. Only items the token's principal is authorized to
    /// see are returned.
    /// </summary>
    /// <param name="token">Capability token; must be valid, workstream-scoped, and allow the Hypothesis category.</param>
    /// <param name="asOf">Instant to compute "overdue" against. Defaults to now.</param>
    /// <param name="limit">Maximum items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="CapabilityDeniedError">If the token is expired, not workstream-scoped, or does not allow the Hypothesis category.</exception>
    Task<IReadOnlyList<WaitingOnItem>> GetOpenWaitingOnAsync(
        CapabilityToken token,
        DateTimeOffset? asOf = null,
        int limit = 100,
        CancellationToken ct = default);
}
