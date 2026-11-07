# ADR-0006: Personal-assistant domain profile (`Mneme.Assistant`)

- **Status:** Accepted (implemented — waiting-on thin slice; commitments + appointments are later phases)
- **Date:** 2026-09-10
- **Deciders:** sargeMonkey

## Context and Problem Statement

A new consumer surface — a **personal-assistant pillar** for MuxiMuxi —
wants a *read-only background daemon* that mines **commitments,
appointments, and waiting-on conditions** from bounded source batches
(email, chat, calendar) and surfaces them as **attention signals**,
while **all user-facing decisions stay in the cockpit**. The daemon
never accepts, completes, dismisses, or reassigns a user-owned task; it
only proposes evidence-backed candidates.

The question this ADR answers: does that need a **new memory engine /
fork**, a **domain package** (like `Mneme.Writer`, ADR-0005), or **just
a host adapter** on existing Mneme APIs?

Two facts bound the answer:

1. **For a single consumer on the currently-shipped package
   (`0.0.1-alpha.1`), a host adapter suffices** — a prior assessment
   confirmed the MVP is expressible with base primitives (candidate
   observations as `Hypothesis(Open)`, `ReviewBeforeDistill` gating, a
   scoped ingest-only capability, host-owned evidence rehydration). No
   library change is required for that path, and none was requested.
2. **But the personal-assistant *ontology* is genuinely distinct** from
   both base Mneme and the writing profile, so making it *first-class +
   reusable + tested once* earns a satellite package the same way
   `Mneme.Writer` did — for consumers who adopt the Profile SDK
   (base `0.1.0-alpha`+).

## Is it "substantially different"? (why a package, not just an adapter)

Genuinely new shapes that neither base Mneme nor `Mneme.Writer`
expresses:

- **Waiting-on — a dyadic external-dependency relation.** "I'm waiting on
  Alice for the budget sign-off since Monday, expected Friday." This is
  not a `Fact` (not true/false about the world) nor a `Goal` (the
  blocking action is the *counterparty's*, not the user's). The agenda
  query *"what am I waiting on, from whom, overdue as of now?"* cannot be
  expressed by base `QueryAsync` (semantic + `valid_at` range only) — it
  needs a projection keyed on counterparty + due date.
- **Future-time as an explicit attribute, not `valid_at`.** A due /
  appointment time is a domain attribute, deliberately **not** the
  event's `valid_at`. A future `valid_at` would *hide* the item from an
  as-of-now recall (base retrieval filters on `valid_at`). This inversion
  is what makes "overdue" and "upcoming" queries possible, and it is
  specific to agenda memory.
- **Candidate-first posture.** Every item carries an extraction
  confidence and is a *proposal*, never a user-accepted decision. Base
  and the writing profile author directly.

Overlap worth naming honestly: a personal-assistant **commitment** is a
cousin of `Mneme.Writer`'s setup→payoff ledger, but it carries
*counterparty + due + direction* (I-owe vs owed-to-me) the narrative one
does not — a different payload, not a duplicate.

Net: an **agenda / attention** ontology with a candidate-first posture.
Distinct enough to be a package; not *impossible* without one (a single
consumer can adapt base primitives). The package earns its keep exactly
as ADR-0005's writing profile does: it turns "every consumer
re-implements the projections + agenda queries" into "register a profile."

## Decision

Ship **`Mneme.Assistant`**, a satellite package built on the **Profile
SDK** (ADR-0005): `IMnemeProfile` bundling payload descriptors + schema
modules + projectors, registered via `AddMnemeAssistantProfile()`. No
change to `Mneme.Contracts`, the seven locked categories, or the closed
`[JsonDerivedType]` union — domain payloads ride under existing
categories and register through the descriptor registry, preserving the
closed-set deserialization safety property.

**Thin vertical slice first: the waiting-on relation** — the single most
distinctive primitive (least expressible in base/Writer):

- `WaitingOnObservationPayload` (rides under `Hypothesis` — an open
  question awaiting resolution). A `WaitingOnId` correlates the
  lifecycle; status advances `Waiting → Fulfilled | Released` by
  **appending** a new event (append-only; the projection resolves "open"
  as the latest event per id still in `Waiting`).
- `WaitingOnSchemaModule` + `projection_waiting_on` — the profile's own
  ledger table (exercises the SDK's `ISchemaModule` path).
- `WaitingOnProjector` — matches `Hypothesis`, self-guards on payload
  type (ignores real `HypothesisPayload`), owns its table (rebuildable
  from the log).
- `IAssistantMemory.GetOpenWaitingOnAsync` — the capability-guarded,
  visibility-filtered agenda query: open items, due-soonest first, each
  flagged overdue relative to an as-of instant.

Commitments (directional, counterparty + due) and appointments
(start / timezone / recurrence) are later phases.

## Authority model (read-only daemon; user owns decisions)

`Mneme.Assistant` is **additive and candidate-first**. The package does
not itself grant or withhold authority — that is the consumer's
composition responsibility — but its shapes are designed for it:

- The daemon holds an **ingest + query** capability scoped to an isolated
  observation workstream and **no `CurationCapability`**; acceptance /
  curation is a cockpit action under a separate capability.
- Because `IngestAsync` takes no token and does no category/state
  validation, candidate-only is enforced by a **host restricted-ingress
  facade** (accept only candidate payloads; reject accepted-decision
  shapes), not by the package. This ADR records the requirement; the
  facade lives in the consumer.
- `Confidence` on every observation makes the proposal-not-decision
  status explicit at the read surface.

## Relationship to the MuxiMuxi coordination

This package is **speculative/ahead-of-consumer**, exactly like
`Mneme.Writer` was. It **does not** change the agreed consumer contract:
MuxiMuxi's MVP stays on its **host-adapter** path on `0.0.1-alpha.1`
(the Profile SDK this package needs is only in `0.1.0-alpha`+). Adopting
`Mneme.Assistant` is a future option gated on the consumer choosing to
bump its base Mneme version; nothing here forces that.

## Consequences

**Positive.** First-class, reusable, tested agenda ontology; the
waiting-on overdue query is deterministic and rebuildable from the log;
the Profile SDK is exercised end-to-end (payload + descriptor + schema
module + projector + query), validating ADR-0005's extensibility a
second time on a domain quite unlike writing.

**Negative / risks.** A second satellite package to version alongside
base Mneme. The commitment/appointment phases must resist re-encoding
future times into `valid_at` (the temporal contract above). Name-PII
(counterparties) is the host's responsibility — Mneme's inline redactor
targets secrets, not names (locked decision #11); the package documents
this but cannot enforce it.

**Neutral.** Ships no change to base Mneme; the release workflow already
packs the whole solution, so `Mneme.Assistant` is picked up automatically.
