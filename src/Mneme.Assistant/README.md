# Mneme.Assistant

The **personal-assistant domain profile** for
[Mneme](https://github.com/devsanity-ai/mneme) — a satellite package that teaches
the memory substrate an *agenda / attention* ontology: the things a **read-only
assistant daemon** discovers from bounded source batches (email, chat, calendar)
and surfaces as **non-authoritative candidates** for a human cockpit to act on.

It composes onto base Mneme with one line (the Profile SDK / ADR-0005 pattern,
the same way `Mneme.Writer` does) and is **candidate-first**: nothing it stores
is a user-accepted decision — acceptance and curation remain cockpit actions
under a separate curation capability.

```csharp
services.AddMneme(o =>
{
    o.WorkstreamId = "pa-observations-<user>";
    o.SqlitePath = "assistant.mneme.db";
    o.UserId = "assistant-daemon";
})
.AddMnemeAssistantProfile();   // ← agenda/attention ontology
```

## Why a package (what base Mneme + `Mneme.Writer` can't express)

The personal-assistant domain has genuinely new shapes:

- **Waiting-on** — a *dyadic external-dependency* relation ("I'm waiting on Alice
  for the budget sign-off since Monday, expected Friday"). It is not a base Fact
  (not true/false about the world) nor a Goal (the blocking action is the
  *counterparty's*). The agenda query *"what am I waiting on, from whom, and
  what's overdue as of now?"* cannot be expressed by base `QueryAsync` (semantic
  + `valid_at` range only) — it needs a projection keyed on counterparty + due
  date.
- **Future-time attributes** — due/appointment times live as explicit
  attributes, deliberately **not** the event's `valid_at` (a future `valid_at`
  would hide the item from as-of-now recall). This is what makes the "overdue"
  and "upcoming" queries possible.
- **Candidate-first posture** — every item carries an extraction confidence and
  is a proposal, not an accepted decision.

## What it adds (thin slice)

- **`WaitingOnObservationPayload`** — one lifecycle observation of a waiting-on
  (rides under the `Hypothesis` category — an open question awaiting
  resolution). A `WaitingOnId` correlates the lifecycle; status advances
  `Waiting → Fulfilled | Released` by **appending** a new event (append-only).
- **`WaitingOnProjector` + `projection_waiting_on`** — the profile's own ledger
  table (exercises the Profile SDK's schema-module path).
- **`IAssistantMemory.GetOpenWaitingOnAsync`** — the capability-guarded,
  visibility-filtered agenda query: open items (latest status still `Waiting`),
  due-soonest first, each flagged overdue relative to an as-of instant.

## Read contract

The host supplies an authenticated capability principal (nonblank, trimmed,
no control characters, at most 256 characters). Tokens must be valid **now**,
workstream-scoped, and grant `Hypothesis`; an empty category list still grants
all categories, but null lists or unknown category values are denied.
`limit` must be positive; the default remains 100, with no new positive cap.
Malformed capabilities throw `CapabilityDeniedError`, null tokens throw
`ArgumentNullException`, and nonpositive limits throw `ArgumentOutOfRangeException`.
Cancellation is checked before reading.

Both open candidates and newer lifecycle events must pass the same current
read gates **before** latest-status selection and limiting:

- Live revocations exclude events immediately, without a projection rebuild.
- Workstream-matching `Shared`/`Global` visibility permits readers in that
  workstream. Private, missing, or mis-scoped visibility is author-only;
  unknown visibility tiers are hidden, including from the author.
- Pending, deferred (even after its deadline), rejected, or unknown review
  states are hidden. In `ReviewBeforeDistill`, epistemic events require
  explicit approval even if not yet queued or exposed by a rebuild.
- Technical events require `IncludeTechnical`, including technical status
  updates. As in core Mneme, they are exempt from the workstream-mode
  approval requirement, not from the other read gates.

A hidden `Fulfilled` update cannot close an older visible `Waiting` item.
`asOf` changes **only overdue calculation**, not lifecycle selection or token
validity; it is not a historical snapshot. All returned items remain
non-authoritative candidates; review approval does not accept a user-owned
task or decision.

## Roadmap

Commitment (directional, with counterparty + due) and appointment
(start/timezone/recurrence) ledgers, and their agenda queries, are later phases.
This slice proves the ontology + the candidate-first, capability-guarded read
surface end to end.

## License

Apache-2.0. See `LICENSE` and `NOTICE`.
