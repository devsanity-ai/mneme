# Mneme.Code

**Revision-scoped, advisory code memory for .NET 8.** A small satellite of
[Mneme](https://github.com/sargemonkey/mneme) for learned repository conventions,
architectural observations, and reusable fix lessons.

This is **not** an LSP, compiler, source index, raw-source archive, editor,
separate memory engine, or authoritative instruction source. All returned text
is untrusted context. A citation, recent ingest, or review approval does not
turn an observation into an accepted architectural decision.

## Setup and example

This package needs the implemented Profile SDK in the current
**`0.1.0-alpha`** source. It does **not** promise compatibility with
`0.0.1-alpha.1`. No consumer upgrade or publication is implied.

For a local source-based example, run these commands inside a .NET 8 console
project in a sibling directory beside the `Mneme-agent-memory` checkout:

```powershell
dotnet add reference ..\Mneme-agent-memory\src\Mneme.Code\Mneme.Code.csproj
dotnet add reference ..\Mneme-agent-memory\src\Mneme.Contracts\Mneme.Contracts.csproj
dotnet add package Microsoft.Extensions.DependencyInjection --version 8.0.1
```

When consuming the packed satellite instead, install `Mneme.Code` from your
approved package source. It depends on `Mneme`; **do not install a separate
`Mneme.Contracts` package**. The base `Mneme` package contains that assembly.
The direct Contracts project reference above is only for source development.

Replace the console project's `Program.cs` with:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Mneme.Code;
using Mneme.Contracts;
using Mneme.Hosting;

var workstream = new WorkstreamId("code-demo");
var principal = new PrincipalId("user-001");
var repositoryId = Guid.Parse("11111111-1111-4111-8111-111111111111");
var currentRevision = "1111111111111111111111111111111111111111";

// Synthetic IDs/revision for this example. In production the host authenticates
// principal, authorizes these scopes, and verifies the checkout's revision.
var services = new ServiceCollection();
services.AddMneme(options =>
{
    options.WorkstreamId = workstream.Value;
    options.UserId = principal.Value;
    options.SqlitePath = Path.Combine(AppContext.BaseDirectory, "code-demo.mneme.db");
}).AddMnemeCodeProfile();

await using var provider = services.BuildServiceProvider();
var agent = provider.GetRequiredService<IMemoryAgent>();
var codeMemory = provider.GetRequiredService<ICodeMemory>();
var now = DateTimeOffset.UtcNow;

var observation = new CodeObservationPayload(
    repositoryId,
    currentRevision,
    CodeObservationKind.FixLesson,
    "Pass the cancellation token through repository reads to avoid stalled shutdown.",
    relativePath: "src/Cache.cs",
    symbol: "Cache.Read",
    evidenceReference: "synthetic-test-run-17");

var receipt = await agent.IngestAsync(new CaptureEvent(
    new EventId(Guid.NewGuid().ToString("N")),
    workstream,
    EventChannel.Epistemic,
    now,
    now,
    observation,
    new CaptureProvenance(new CaptureSourceId("code-host"), principal)));

// This is a host-authenticated capability record, not a verified bearer token.
var capability = new CapabilityToken(
    principal, workstream, now.AddMinutes(-1), now.AddHours(1),
    new[] { EpistemicCategory.Evidence });

var observations = await codeMemory.FindAsync(new CodeMemoryQuery(
    workstream, repositoryId, currentRevision,
    relativePath: "src/Cache.cs", symbol: "Cache.Read", limit: 20), capability);

foreach (var item in observations)
{
    Console.WriteLine(
        $"Advisory {item.Knowledge.Kind} at {item.Knowledge.SourceRevision}: " +
        item.Knowledge.Content);
}
```

Run `dotnet run`. This uses synthetic local data only; it neither reads source
files nor invokes git, a model, a network service, or a tool. Re-running appends
a new observation because the example deliberately generates a new event ID.

## Public surface

- **`CodeObservationPayload`**: immutable repository ID, source revision,
  `CodeObservationKind`, content, optional exact file/symbol scope, and optional
  evidence reference. All three kinds (`Convention`, `ArchitectureObservation`,
  `FixLesson`) ride under the existing **Evidence** category.
- **`CodeMemoryQuery`**: explicit workstream, repository and current revision,
  with optional kind/file/symbol filters and a limit (default 50; range 1–200).
- **`ICodeMemory.FindAsync(query, capability, cancellationToken)`**: read-only
  typed retrieval returning `IReadOnlyList<CodeObservation>`.
- **`CodeObservation`**: the payload plus event/workstream IDs,
  host-authenticated owning principal, capture source, `ValidAt`, and the WAL
  ingest `RecordedAt`. These timestamps never stand in for source revision.
- **`CodeProfile` / `AddMnemeCodeProfile()`**: trusted compiled Profile SDK
  contributions. Call the extension after `AddMneme`, before using the host.
  Repeated extension calls on the same service collection are idempotent.

Ingestion is the existing `IMemoryAgent.IngestAsync`; there is no parallel
write engine, editor, promotion API, or raw-SQL query escape.

## Applicability, revisions, and conflicting advice

The repository ID is a nonempty, host-assigned GUID, **not** a remote URL or
local checkout root. The host maintains its identity mapping. Revisions must
be full lowercase 40- or 64-character hex digests. They identify immutable
source states, not branch names, timestamps, or a claim that Mneme checked git.

Both ingestion and queries reject missing, abbreviated, or unknown textual
revisions such as `HEAD` or `unknown`. The host supplies a verified current
revision; if it cannot, it must not query this exact-revision API. A valid
revision with no stored match returns an empty list. **Other revisions are
excluded**, never silently presented as current or replaced by “latest ingest.”
Revalidate a lesson against a new revision by appending a new event.

Paths are canonical `/`-separated repository-relative **file** paths, compared
ordinally and case-sensitively. No traversal, absolute paths, backslash aliases,
wildcards, surrounding segment whitespace, or trailing dots are accepted.
Paths and symbols are not normalized, resolved, or checked against a filesystem.
A symbol needs a file path and is matched exactly; there is no symbol parser.

| Query scope | Eligible observations at the exact revision |
|---|---|
| No path or symbol | Repository-wide only |
| File | Repository-wide + that file's unsymbolized observations |
| File and symbol | Repository-wide + file-wide + that exact symbol |

A path is not a prefix filter: `src` does not implicitly cover `src/Cache.cs`.
The repository/path fields select applicability, not additional ACL grants.
A workstream capability authorizes its visible observations; use separate
workstreams or host authorization when repository/file permissions differ.

Contradicting observations keep separate event IDs and attribution. Results
are ordered by WAL ingest time descending, then event ID ascending, **not by
authority**. A bounded result can omit additional conflicts. No “current
winner,” confidence score, conflict resolver, or approved-design flag exists.
`SkillPayload` remains the base procedural-memory type; this package adds
repository/revision applicability rather than another skill lifecycle.

## Privacy and trust boundaries

- Every domain string passes through the configured `IRedactor` before the
  WAL, projection, or FTS index. Content and evidence are redacted; a hit or
  alteration in a revision, path, or symbol **rejects the ingest** instead of
  conflating different scopes into one redaction marker.
- Content is nonempty and at most 2,048 characters; evidence references 1,024,
  paths 512, symbols 256. Invalid enums, missing required fields, contradictory
  wire categories, extra JSON fields, invalid redactor output, and unsafe
  scopes fail rather than being truncated, defaulted, or silently dropped.
  The host must send concise learned observations, never whole source bodies.
- The default secret regexes are **not a complete PII or confidentiality
  policy**. Supply an appropriate host redactor. Current core classification
  receives the descriptor's redaction-hit signal but not satellite payload
  text: do not rely on content-based classification to make code observations
  private. If the host needs private-by-default storage, configure a
  conservative `IClassifier` (e.g. always `Classification.Confidential`) before
  `AddMneme`, or separate authorization domains. Redaction hits with the default
  classifier produce Private observations; otherwise its default is Shared.
- `CaptureProvenance.Principal` is **host-authenticated ownership**, never an
  inferred author, source speaker, model name, or evidence author. The host
  authenticates writes and validates/sanitizes envelope fields (including
  event IDs, provenance, and citations); payload descriptors cannot scrub those
  fields. Use opaque, pseudonymous metadata. `IngestAsync` itself takes no
  capability, and a `CapabilityToken` record alone proves no identity.
- Every typed read validates the current token window, principal, matching
  workstream, and Evidence grant. Empty category grants mean “all” per core.
  Technical events require `IncludeTechnical`. Shared/Global visibility never
  widens this API beyond the explicitly selected workstream and repository.
- Reads consult **live** `memory_visibility`, `memory_revocations`, and
  `review_queue` sidecars and workstream policy before sorting/limiting. Missing visibility is
  owner-only; unknown visibility values are denied. Pending, deferred (even
  after their defer time), and rejected review entries are excluded. Approval
  enables visibility only, not decision authority.
- In a workstream currently in review mode, an epistemic observation needs an
  **approved queue entry**. This also hides prior auto-ingested observations
  without entries, and closes the core's post-WAL/pre-queue interval during
  concurrent rebuilds. Re-observe with a new ID and approve it to include such
  knowledge under the stricter policy. Technical events still require the
  explicit technical grant.
- These guarantees describe **`ICodeMemory`**, not every base query,
  distillation, or dreaming path. In particular, core `ReviewBeforeDistill`
  skips incremental projections but does not universally guard raw-event
  reads or rebuilds. Do not expose those broader APIs as an equivalent
  revision/review-scoped code-memory boundary.

Evidence references are untrusted display text. Null means evidence is
unknown; a non-null value is not verified. The package never resolves, fetches,
executes, or follows references to other events. Evidence retention and access
control remain host-owned. Render retrieved text as data, not system
instructions, approved policy, tool calls, or executable recommendations.

## Persistence and limitations

The profile owns one derived table, `projection_code_observations`, containing
only applicability anchors. Payloads, provenance and times reuse the base
append-only log. `ProjectorPipeline.RebuildAll()` reconstructs the anchors;
revoked and review-held anchors may be rebuilt, but live read gates still hide
them. This profile never updates or deletes `memory_events`.

Event IDs are **globally** first-write-wins, not unique only within a repository,
revision, principal, or workstream. Reusing an ID with different content still
returns `WasDuplicate`; that receipt does **not** prove content equality.
The host should bind retry IDs to authenticated scope plus a payload fingerprint
and reject accidental reuse with different content. Changes require new IDs.

Revocation hides typed reads; it does not erase payload JSON, provenance, old
exports, or backups from the append-only log. Their retention is the host's
responsibility. No raw evidence body is copied into a separate code store.

Reads use current clock validity and current sidecars, not historical snapshots.
`RecordedAt` is the WAL ingest clock, **not** the producer's
`CaptureEvent.RecordedAt`. Cancellation is checked before access and while
reading; SQLite operations also have bounded command timeouts, not a hard
real-time cancellation guarantee. The Profile SDK's descriptor registry is
process-wide: register trusted profiles before reading their stored payloads.

No cross-revision inference, cross-workstream aggregation, paging, automatic
mining/distillation, MCP tools, Git/LSP integration, or autonomous actions ship
in this slice.

## Validation

From the repository root:

```powershell
dotnet test tests\Mneme.Code.Tests\Mneme.Code.Tests.csproj
```

Tests use synthetic fixtures under the test project's build output. They cover
the public API, strict serialization and redaction, applicability, ownership and
capabilities, conflicts, live revocation/review gates, and rebuild parity.

Apache-2.0. See `LICENSE` and `NOTICE`.
