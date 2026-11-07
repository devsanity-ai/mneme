using Mneme.Contracts;

namespace Mneme.Assistant;

/// <summary>
/// An open waiting-on — an external dependency the user is still blocked on
/// (latest lifecycle status is <see cref="WaitingOnStatus.Waiting"/>). Surfaced
/// by <see cref="IAssistantMemory.GetOpenWaitingOnAsync"/> as a non-authoritative
/// attention signal for the cockpit; never auto-resolved.
/// </summary>
/// <param name="WaitingOnId">Stable slug of the dependency.</param>
/// <param name="Counterparty">Who the user is waiting on (pseudonymous handle).</param>
/// <param name="AwaitedItem">What the user is waiting for.</param>
/// <param name="Since">When the waiting started.</param>
/// <param name="ExpectedBy">Due date, if known.</param>
/// <param name="IsOverdue">True when <see cref="ExpectedBy"/> is known and earlier than the query's as-of instant.</param>
/// <param name="Confidence">Extraction confidence in <c>[0,1]</c> (candidate-first — treat as a proposal, not an accepted fact).</param>
/// <param name="SourceEvent">The event this open state was read from (feed into the host source map for permission-checked rehydration).</param>
public sealed record WaitingOnItem(
    string WaitingOnId,
    string Counterparty,
    string AwaitedItem,
    DateTimeOffset Since,
    DateTimeOffset? ExpectedBy,
    bool IsOverdue,
    double Confidence,
    EventId SourceEvent);
