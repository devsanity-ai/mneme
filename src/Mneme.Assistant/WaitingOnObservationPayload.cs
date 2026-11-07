using Mneme.Contracts;

namespace Mneme.Assistant;

/// <summary>
/// Lifecycle of a <see cref="WaitingOnObservationPayload"/>. A waiting-on is an
/// external dependency the user is blocked on; its status advances by
/// <em>appending</em> a new observation event that shares the same
/// <see cref="WaitingOnObservationPayload.WaitingOnId"/> (append-only — the prior
/// event is never mutated).
/// </summary>
public enum WaitingOnStatus
{
    /// <summary>Still outstanding — the user is blocked on the counterparty. This is the "open" state.</summary>
    Waiting = 0,

    /// <summary>The awaited item arrived / the counterparty delivered. No longer open.</summary>
    Fulfilled = 1,

    /// <summary>The user released the dependency (no longer needed / abandoned). No longer open.</summary>
    Released = 2,
}

/// <summary>
/// A <em>candidate observation</em> that the user is waiting on someone else for
/// something — the personal-assistant domain's dyadic external-dependency
/// relation (ADR-0006). This is deliberately NOT a base epistemic shape: it is
/// not a <see cref="FactPayload"/> (not an assertion that is true/false about the
/// world) nor a <see cref="GoalPayload"/> (the blocking action is the
/// <em>counterparty's</em>, not the user's). It rides under
/// <see cref="EpistemicCategory.Hypothesis"/> — an open question awaiting
/// resolution — while the profile's projector recognises it by payload type
/// (the same "ride under a locked category" pattern the writing profile uses).
/// </summary>
/// <remarks>
/// <para>
/// <b>Candidate-first.</b> A waiting-on is surfaced by a read-only daemon as a
/// PROPOSAL (see <see cref="Confidence"/>); it is never an accepted user
/// decision. Acceptance/curation is a cockpit action under a separate curation
/// capability, never the daemon's.
/// </para>
/// <para>
/// <b>Temporal contract (ADR-0006).</b> <see cref="ExpectedBy"/> and
/// <see cref="Since"/> are explicit domain attributes on the payload — they are
/// deliberately NOT the event envelope's <c>ValidAt</c>. A future
/// <see cref="ExpectedBy"/> placed in <c>ValidAt</c> would hide the item from an
/// as-of-now recall (base retrieval filters on <c>valid_at</c>); keeping due/since
/// as attributes is what makes the "overdue as-of-now" agenda query possible.
/// </para>
/// <para>
/// <b>PII.</b> <see cref="Counterparty"/> is a person and must be a pseudonymous
/// reference (Mneme's inline redactor targets secrets, not names — name-PII is
/// the host's responsibility, locked decision #11).
/// </para>
/// </remarks>
/// <param name="WaitingOnId">Stable slug correlating a waiting-on across its lifecycle events (e.g. <c>waiting:budget-signoff-alice</c>). Reused verbatim on the follow-up Fulfilled/Released event.</param>
/// <param name="Counterparty">Who the user is waiting on (pseudonymous handle, not a raw name).</param>
/// <param name="AwaitedItem">What the user is waiting for (e.g. "budget sign-off").</param>
/// <param name="Since">When the waiting started.</param>
/// <param name="Status">Current lifecycle status. The open/attention state is <see cref="WaitingOnStatus.Waiting"/>.</param>
/// <param name="ExpectedBy">Optional due date — the driver of the overdue signal. Null when no deadline is known.</param>
/// <param name="Confidence">Extraction confidence in <c>[0,1]</c> (candidate-first). Defaults to 1.0 for a directly-asserted item.</param>
/// <param name="SourceRef">Optional external reference (e.g. a message id / thread) beyond the event's <see cref="Citation"/>.</param>
public sealed record WaitingOnObservationPayload(
    string WaitingOnId,
    string Counterparty,
    string AwaitedItem,
    DateTimeOffset Since,
    WaitingOnStatus Status = WaitingOnStatus.Waiting,
    DateTimeOffset? ExpectedBy = null,
    double Confidence = 1.0,
    string? SourceRef = null)
    : EventPayload
{
    /// <inheritdoc/>
    public override EpistemicCategory Category => EpistemicCategory.Hypothesis;
}
