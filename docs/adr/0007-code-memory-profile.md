# ADR-0007: Revision-scoped advisory code memory

- **Status:** Proposed
- **Date:** 2026-09-17
- **Deciders:** Repository owner (pending)

## Context

An engineering host needs learned repository conventions, architectural
observations, and fix lessons without confusing them with an index of source
code or an approved architectural decision. Generic facts and skills lack
typed repository/revision applicability. Recency is not a source revision:
an older revision may be ingested after a newer one.

## Proposed decision

Add a .NET 8 satellite, `Mneme.Code`, using the implemented Profile SDK from
ADR-0005. The bounded slice is one immutable `CodeObservationPayload`, with
three kinds: `Convention`, `ArchitectureObservation`, and `FixLesson`.
All ride under **Evidence**, never Decision or accepted canon. No confidence,
approval, executable procedure, automatic conflict resolution, or promotion
surface is introduced. Contradictory observations remain separate attributed
records; absence of an evidence reference remains explicitly unknown.

Each observation has a nonempty host-assigned repository GUID, a required
immutable source revision (full lowercase 40- or 64-digit hex digest), concise
content, optional repository-relative file path and symbol, and an optional
unresolved evidence reference. Paths are canonical slash-separated, ordinal,
case-sensitive exact paths, not globs or directory prefixes. A symbol requires
a path. Repository IDs are opaque, not remote URLs or local filesystem roots.

`ICodeMemory.FindAsync` requires an explicit workstream, repository, and
host-validated current revision. It returns only exact-revision observations
applicable to the requested path/symbol, including broader repository/file
observations. Without a path it returns repository-wide observations only.
Other revisions are **excluded**, not assumed current and not silently mapped
to the newest ingested item. Hosts must issue a new event ID when the source
revision or observation changes. Core event-ID first-write-wins behavior stays
unchanged and global across workstreams/principals/repositories. A duplicate
receipt does not prove equal content: hosts should bind retry IDs to
authenticated scope plus a payload fingerprint. Unknown or abbreviated
revisions are rejected; a valid revision with no matching observation returns
an empty result rather than a stale fallback.

The profile contributes a trusted compiled descriptor, a small owned
`projection_code_observations` anchor index, and a rebuildable projector.
Queries read payload/provenance/times from the existing event log and check
current visibility, revocation, and review sidecars. No content archive,
second store, event-log mutation, or base/Contracts change is required.
`SkillPayload` remains the general procedural-memory contract; code memory
adds typed applicability/evidence rather than duplicating a skill lifecycle.

### Public API and composition

`services.AddMneme(...).AddMnemeCodeProfile()` installs the compiled
`CodeProfile : IMnemeProfile`, its `IPayloadDescriptor`, `ISchemaModule`, and
`IProjector`, and the read-only `ICodeMemory` service. Payload, query, and
observation DTOs are immutable. The existing `IMemoryAgent.IngestAsync` is the
only write path; it remains a host-authenticated operation, not a new
package-specific token-checked ingest wrapper.

```csharp
Task<IReadOnlyList<CodeObservation>> FindAsync(
    CodeMemoryQuery query, CapabilityToken capability, CancellationToken ct = default);
```

The query carries workstream, repository ID, current revision, optional kind,
relative file path and symbol, and a limit. The result includes the typed
knowledge plus event ID, workstream, owning principal, capture source, and
timestamps. Repository/file filters are applicability selectors, not narrower
ACL grants inside a workstream; use separate workstreams or host authorization
for those trust boundaries. See `src/Mneme.Code/README.md` for runnable setup
and the precise applicability rules.

## Trust and safety boundaries

- Hosts authenticate the caller and stamp `CaptureProvenance.Principal` and
  `Source`; neither learned text nor an evidence reference authenticates a
  principal. A capability record is trusted host input, not a bearer-token
  verification mechanism. Hosts validate/sanitize envelope metadata as well as
  domain inputs; the core redactor does not sanitize arbitrary provenance.
- Every domain string goes through the configured core redactor before
  persistence. Content/evidence references are redacted; any redaction hit or
  alteration of revision/path/symbol identifiers rejects the ingest rather
  than collapsing distinct scopes to the same redaction marker. Structural
  validation also rejects invalid enums, empty content, unsafe paths and
  invalid digests. Wire input rejects missing required fields, extra fields,
  and contradictory categories. Content is bounded to 2,048 characters,
  evidence references 1,024, paths 512, and symbols 256, including after
  redaction; invalid output is rejected, never truncated.
- The default secret regexes are not a complete PII detector. Hosts must
  supply suitable redaction policy and pseudonymous metadata. Actual core
  `MemoryAgent.PayloadText` does not extract satellite text for classification:
  only the descriptor's redaction-hit signal reaches the default classifier.
  Confidentiality cannot be inferred from arbitrary code text by that path.
  A private-by-default host should register a conservative `IClassifier`;
  default redaction hits already result in Private visibility.
- Reads validate token time window, principal, exact workstream scope, category,
  and channel. Cross-workstream aggregation is intentionally unsupported.
  Missing visibility is treated as author-only. Revocations are checked against
  `memory_revocations` on each read, not a cached `revoked_at` column.
- Pending, deferred, and rejected review entries are excluded on each typed
  read, including after a projection rebuild. Current review-mode workstreams
  require an approved queue entry for every epistemic observation, even
  previously auto-ingested ones. This fail-closed rule covers the core's
  post-WAL/pre-queue interval if a concurrent rebuild exposes an anchor early.
  Re-observing with a new ID and approving makes old unqueued knowledge
  eligible under the stricter policy. Review approval enables visibility,
  **not decision authority**. This is not a claim that core raw queries or
  dreaming universally enforce the same review gate.
- Evidence references are untrusted display context, never resolved, fetched,
  executed, or joined to other events. There is no paired-conflict API leaking
  a hidden observation: each returned observation passes its own visibility
  checks. A future cross-event operation must authorize both sides.
- Reads are bounded to 1–200 items and accept cancellation. They use current
  clock validity and current sidecars, not historical snapshots. `ValidAt`
  remains world-valid time; returned `RecordedAt` is core `created_at` (WAL
  ingest clock), not the producer's `CaptureEvent.RecordedAt`. Source revision
  remains independent of both.
- Revocation is read-time exclusion, not physical erasure of the append-only
  payload JSON, provenance, backups, or exports. Retention and evidence access
  remain host responsibilities. Register trusted profiles before reading their
  stored payloads; the actual SDK descriptor registry is process-wide.

## Alternatives and consequences

Generic metadata over facts/skills avoids a package but leaves applicability
and retrieval untyped. A source index or broad code ontology duplicates host
compiler/LSP responsibilities. A latest-write-wins convention store loses
conflicts, attribution, and revision correctness. The proposed slice keeps the
useful domain distinction while deliberately declining these larger designs.

Costs: one derived table, exact-revision revalidation work for hosts, and no
cross-revision reuse until the host explicitly re-observes/revalidates the
lesson. This package needs a base build containing the Profile SDK (current
source version `0.1.0-alpha`); it does not promise compatibility with installed
`0.0.1-alpha.1` consumers or authorize a base version change.

No compiler/LSP, git/network access, raw source retention, cross-revision
inference, cross-workstream aggregation, automatic mining, historical-as-of
queries, or automatic actions are provided. The bounded result order is WAL
ingest time descending and event ID ascending, not authority; limits may omit
additional conflicting observations.

## Validation

Targeted package tests cover descriptor serialization and closed-set safety,
redaction before persistence, validation, exact applicability, idempotency,
capabilities, sidecars, conflict preservation, and rebuild parity. Local pack
verification must show a dependency on `Mneme` only, with no phantom
`Mneme.Contracts` dependency. Solution and ADR-index registration are separate
integration changes owned by the coordinating agent.
