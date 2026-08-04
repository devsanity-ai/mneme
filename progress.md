# Mneme session progress

Saved: 2026-09-17 (America/Los_Angeles).
Working directory: `C:\root\Mneme-agent-memory`.

This is a local resumption document, not a release announcement. No private
email/Teams contents, credentials, or publishing instructions are reproduced here.
Do not include this local coordination note in a package commit without review.

## Approach

Keep one Mneme substrate and add domain packages through the existing Profile SDK:

- **Mneme.Writer:** manuscript claims, structured continuity candidates, and
  setup/payoff commitments. Already committed.
- **Mneme.Assistant:** a waiting-on prototype exists, but the intended direction
  is now person-centered life memory: relationships, important dates, preferences,
  availability, travel, plans, and obligations. The broader design is still under
  discussion, particularly its personal-data storage boundary.
- **Mneme.Code:** the initial implementation is complete locally. It remembers
  revision-scoped repository conventions, architectural observations, and fix
  lessons. It is not a source archive, code index, LSP, or autonomous editor.

The user's latest requested division of work is explicit: a **new app child
session owns ongoing Mneme.Code changes**, while the **parent keeps Assistant
work here**. Creating that child is currently blocked by the app's repository
registration, not by missing Code implementation.

Preserve the user-owned, in-place checkout and all uncommitted packages. Do not
change Git remotes to bypass the registration error. Do not commit, push, tag,
publish, or upgrade consuming applications without explicit authorization.

## Steps Completed

1. **Pre-publish housekeeping and reviews.**
   - `7a54de1`: reconciled documentation and stabilized a load-sensitive latency test.
   - `a185590`: remediated six pre-publish findings, including cross-principal
     Private-data filtering, derived-memory visibility, a curation scope guard,
     MCP-generated event IDs, and fleet audit timing.
   - `95ec746`: added seven second-order behavior tests. The recorded solution
     total at that point was 412 passing tests.

2. **Prepared the preview release without publishing it.**
   - `8214248`: changed `Directory.Build.props` to `0.1.0-alpha`, derived MCP
     server version from the assembly, and updated the changelog.
   - `Mneme.Contracts` remains a separate BCL-only project but its assembly is
     bundled into the `Mneme` NuGet package; it is not a separate package dependency.
   - A source version or successful local pack does not mean that version was
     published. The user retained control of publishing.

3. **Built and recorded the ACP Studio demo.**
   - App: `src\Mneme.Studio.Agent`, Photino + Blazor, using Copilot over ACP.
   - `e62b25d`: added Clear to reset the demonstration conversation and memory.
   - Recording used headless browser page capture rather than capturing the
     shared desktop; a motion-based edit accelerated idle gaps.
   - Deliverables:
     - `%USERPROFILE%\Videos\mneme-acp-demo.mp4`: accelerated cut, about 52 seconds.
     - `%USERPROFILE%\Videos\mneme-acp-demo-realtime.mp4`: about 115 seconds.
   - The app was subsequently launched and closed intentionally by the user.
   - An older Debug demo database failed startup with SQLite
     `no such column: principal_id`. That demo store was recreated at the time.
     **The underlying schema migration ordering defect was not fixed.**
     Do not treat deleting a real user's database as an acceptable migration.

4. **Implemented the domain Profile SDK.**
   - `b788d0d`: ADR-0005 and Phase 15 planning.
   - `e928c2f`: `IPayloadDescriptor`, `PayloadDescriptorRegistry`,
     `ISchemaModule`, `IMnemeProfile`, and `AddMnemeProfile`.
   - Registered compiled satellite types extend the serializer's known set;
     unknown discriminators remain rejected.
   - Descriptors supply redaction, full-text extraction, and summaries.
     Profile projectors participate in ingest and rebuild.
   - `EventEnvelopeReader` became public so external profile projectors can replay.
   - The proposed CoreProfile refactor was deferred; do not assume all the original
     ADR's planned abstractions exist. Actual source is the API authority.

5. **Implemented the Writer profile in two slices.**
   - `fb56329`: `Mneme.Writer`, `AuthoringClaimPayload`, its descriptor and canon
     projector, `IWriterMemory.GetContinuityConflictsAsync`, profile composition,
     and tests. Extracted shared `FactTripleProjection` from core projectors.
   - `29cbd14`: `NarrativeCommitmentPayload`, Setup/Payoff roles, its own
     projection/schema module, and `GetOpenCommitmentsAsync`.
   - The recorded full solution total after these slices was 449 passing tests.
   - Remaining Writer tasks are listed below; do not silently expand into them.

6. **Removed the public maintainer runbook, with a private backup.**
   - Local commit:
     `dff4f69aab983e6e48f69c331a5fce1913bebd03`.
   - Deleted `docs\PUBLISHING.md`; removed incoming references from `README.md`
     and `CHANGELOG.md`.
   - No release workflow changes or history rewrite. Historical copies were not
     purged. This cleanup commit was explicitly authorized and is **not pushed**.
   - Exact backup:
     `%USERPROFILE%\.copilot\session-state\ad9f5f93-d777-4b5e-a654-05700369d41a\files\publishing-guide-backup\PUBLISHING.md`.
   - Backup SHA256:
     `5226C275FB33BD26F5CF12150C4914CFC7EDA55AE8DDC9215F00E40515741FDB`.
     Original Git blob: `21dcc5e739783788a8b2e126d3865d715753b919`.

7. **Assessed the MuxiMuxi personal-assistant adapter.**
   - The consumer identified its actual `Mneme.0.0.1-alpha.1.nupkg` repository
     commit as `a185590`. Public API evidence was supplied from that archive;
     source ancestry supplied additional context.
   - The agreed MVP uses existing Mneme APIs through a **restricted host-side
     ingress adapter**, an isolated observation workstream, and host-owned
     source/evidence mappings. No fork or consumer dependency bump was authorized.
   - Observations may be automatically stored and condensed without becoming
     accepted user decisions. The cockpit retains decision authority.
   - Full recall hydration is a real deferred API gap: `QueryResultItem` exposes
     a short summary, not full payload/provenance. The consumer maintains
     `EventId -> source/account/item/revision/permissions` at capture time and
     rehydrates from its own source only after a current permission check.
   - Other important findings: raw `IngestAsync` has no capability-token
     parameter; event-ID duplicates are content-blind first-write-wins;
     `Principal` is host-controlled ownership, not a model-extracted speaker;
     default regex redaction targets secrets, not arbitrary personal information.
   - The adapter design gate was accepted by the coordinating MuxiMuxi sessions.
     They requested further Mneme input only for concrete supported-API mismatches.
   - No live-source setup, dependency upgrade, publication, or consumer code
     changes were performed from this Mneme session.

8. **Implemented the uncommitted Assistant waiting-on prototype.**
   - The user authorized a separate domain package if substantially different.
   - Created `src\Mneme.Assistant`, its tests, and ADR-0006.
   - API: `AddMnemeAssistantProfile()`, `WaitingOnObservationPayload`,
     `WaitingOnStatus`, and `IAssistantMemory.GetOpenWaitingOnAsync`.
   - Own table: `projection_waiting_on`. Observations include counterparty,
     awaited item, since, optional expected-by, status, and confidence.
   - A recorded full solution run passed 465 tests, including 16 Assistant tests.
     This was **before adding Code tests**, not the latest combined-suite result.
   - Local pack verification found a `Mneme`-only dependency. Nothing was published
     or committed for this package.

9. **Reviewed the broader Assistant direction with the user.**
   - The user clarified that a personal assistant must remember relationships,
     birthdays, vacations, travel/flight plans, intentions, and daily-life context,
     not merely follow-ups.
   - A revised six-area model was discussed:
     people/relationships; preferences/constraints; important dates;
     availability/travel; plans/intentions; commitments/dependencies.
   - Recommended next design slice: shared person references and temporal/authority
     boundaries, then relationships plus recurring important dates.
   - The broader plan was **not implemented or written into ADR-0006**. The
     existing ADR still describes the narrower agenda/waiting-on package.
   - The September 15 plan review explicitly identified privacy, authority,
     recurrence, and historical-query issues to settle before more code.

10. **Completed Mneme.Code through a background subagent.**
    - The first agent handle became unavailable after leaving partial source.
      A check confirmed no background agent was running before a replacement
      resumed that existing work.
    - Replacement agent: `f25228ad-86ea-43b1-bb2e-80ea2f93969e`.
      It finished its task; it is not a separately created app session.
    - The agent owned only Code source/tests and ADR-0007. Assistant files stayed
      untouched.
    - Implemented typed advisory repository knowledge, exact-revision retrieval,
      redaction/validation, profile schema/projectors, scoped queries, and tests.
    - Parent added Code source/test projects to `Mneme.slnx` and the Proposed
      ADR-0007 row to `docs\adr\README.md`.
    - Agent and parent both ran the targeted Release tests: **113 passed**.
      Parent command:

      ```powershell
      dotnet test tests\Mneme.Code.Tests\Mneme.Code.Tests.csproj `
        -c Release --no-build --no-restore --nologo --verbosity quiet
      ```

    - `dotnet sln Mneme.slnx list` showed both Code project entries.
      Diff checking passed when treating the repository's existing CRLF endings
      as line endings (`core.whitespace=blank-at-eol,blank-at-eof,space-before-tab,cr-at-eol`).
    - The agent locally packed `Mneme.Code.0.1.0-alpha.nupkg` and symbols.
      Nuspec dependency: **Mneme only**, no phantom Contracts dependency or bundled
      duplicate Contracts assembly.
    - Nupkg SHA256, also checked by the parent:
      `AFD3B7388865148B6CC320F86EE83837F04C533E3E0C5601B90CE130E1EA08C8`.
    - **Validation limitation:** an ordinary restore hit NU1900 because the
      configured vulnerability-data feed was unavailable. The agent used
      command-scoped `-p:NuGetAudit=false` to restore, without persistent
      feed/credential changes. The dependency vulnerability audit was **not**
      verified. Later test/pack commands used `--no-restore`.
    - No new full-solution run was claimed after Code integration.
      Code remains uncommitted and unpublished.

11. **Prepared a safe recovery copy before app project re-registration.**
    - External recovery directory:
      `%USERPROFILE%\Mneme-recovery\20260917-112111`.
    - Copied and hash-checked all **38 then-uncommitted files**, preserving their
      relative paths under `files\`, plus `Code-memory-HANDOFF.txt`.
    - `SHA256.csv`, `HEAD.txt`, and `git-status.txt` record the snapshot.
      The copy operation did not change repository files or Git state.
    - This is **not an export of app session history**. This progress report is
      saved separately alongside that recovery snapshot.
    - User-provided app screenshots show the removal warning: the main checkout
      stays, but session worktrees are deleted. Both app session listing and
      `git worktree list --porcelain` showed only this in-place Mneme checkout.
    - Re-registering the same folder through `create_project` merely returned the
      old project association; it did not repair it. Direct control of this
      application's UI was unavailable. No removal/re-registration was completed.

12. **Attempted the requested dedicated Code owner session.**
    - Checked app sessions first: no existing Mneme.Code app session to reuse.
    - Every `create_session` attempt failed on the same repository-registration
      guard. No Code child app session was created, no fallback project was
      selected, and no Git remote was changed.
    - The user's latest clarification is that the child should own **ongoing
      Mneme.Code changes**, not merely be a read-only exploration.

## Current Task / Status

### Repository snapshot

| Item | Current state |
|---|---|
| Checkout | `C:\root\Mneme-agent-memory`, in-place, user-owned |
| Branch | `main` |
| HEAD | `dff4f69aab983e6e48f69c331a5fce1913bebd03` |
| Local tracking ref | `origin/main` at `29cbd14453ac0e93cbb740a3edb98833e09c3679` |
| Ahead / behind | 1 ahead, 0 behind the local tracking ref; no fresh fetch performed |
| Git origin | `https://github.com/sargemonkey/mneme.git` |
| App's saved repository | `devsanity-ai/mneme` |
| App project ID | `ee37e38f-27af-4bd9-843b-d5ff447049ac` |
| Parent app/project-session handle | `94ca3de6-21ce-4034-be8c-0f2d19f01d2c` |
| Parent CLI session ID | `ad9f5f93-d777-4b5e-a654-05700369d41a` |
| Worktrees | Only the main checkout in the last confirmed listing |
| Working tree | Dirty: uncommitted Assistant, Code, ADRs, solution/index changes, and this report |

### Immediate blocker: create the Code owner session

Exact error:

```text
Failed to create session: project ee37e38f-27af-4bd9-843b-d5ff447049ac origin points to sargemonkey/mneme, not the saved repository; restore origin or remove and re-add the project
```

The confirmed cause is the stale **app project registration**. The intended
repository is `sargemonkey/mneme`; changing origin to a different repository just
to satisfy the guard is not authorized and would be the wrong fix.

The user was shown how to find **Settings -> Remove repository**. They have not
confirmed completing removal/re-addition; repeated `/spawn` requests still
selected the old project ID and received the same error.

After the user re-registers the existing local folder, resolve its actual new
project ID rather than silently reusing the old ID. If a new request explicitly
selects a project, honor that exact target or report the conflict.

Requested child settings:

```text
name: Mneme.Code development
coordinate_with_creator: true
notify_on_idle: once
kickoff.mode: interactive
kickoff.model: gpt-6-astra
kickoff.reasoning_effort: max
kickoff.context_tier: long_context
base_branch: unset
```

Use a branch session on the existing checkout if preserving access to the
uncommitted implementation. A fresh worktree from HEAD will not contain these
untracked packages unless a transfer is explicitly arranged. Do not commit them
merely to make a new worktree possible.

Child ownership: `src\Mneme.Code`, `tests\Mneme.Code.Tests`, and ADR-0007.
Parent ownership: Assistant and its design. Shared files require coordination.
The Code implementation already exists; kickoff should read its handoff, report
ready, and take future user requests rather than recreate it.

### Other open work

- **Assistant:** revise the person-centered design before expanding implementation;
  settle personal-data storage and authority boundaries first.
- **Writer todos still pending:** `writer-thread`, `writer-bitemporal`,
  `writer-distiller`, and the remaining `writer-query` surface.
- **Code implementation todo:** done; only the separate owner-session handoff is
  blocked. No new feature, commit, or publication was authorized by `/spawn`.
- **Publishing cleanup commit:** remains local and unpushed.

### Durable handoffs and artifacts

- Code implementation handoff:
  `%USERPROFILE%\.copilot\session-state\ad9f5f93-d777-4b5e-a654-05700369d41a\files\code-memory-agent\HANDOFF.txt`.
- Local Code `.nupkg`, `.snupkg`, and test results are in that same directory.
- External source recovery copy and Code handoff:
  `%USERPROFILE%\Mneme-recovery\20260917-112111`.
- Earlier PA consumer contract:
  `%USERPROFILE%\.copilot\session-state\ad9f5f93-d777-4b5e-a654-05700369d41a\files\pa-supported-contract.md`.
  **Read with the corrections below:** some earlier discussion overstated core
  review/authorization guarantees; actual source and the newer Code handoff take
  precedence over those statements.
- Relevant MuxiMuxi coordination IDs, if a concrete consumer API issue arises:
  - Reconciliation: `c3f3559f-155d-43fc-a25e-e36b26ddbe16`.
  - Personal-assistant owner: `116cf8d5-c714-451a-8a26-a1421fd3fc91`.
  - Daemon owner: `dd4732c5-aa66-42e5-b806-a0ebde06fedc`.
  - Product-memory owner: `318ce49e-f726-49d2-b4ca-b480d5655d1f`.
  - Coordinating chat: `dd011312-6578-4a17-90e9-23e1b18bb33c`.
  Their existing-package MVP does not automatically adopt these new satellites.

## Key Decisions & Context

### Package boundaries

- No new engine/fork. Reuse SQLite, the append-only log, capabilities and the
  Profile SDK. `Mneme.Contracts` stays BCL-only.
- Satellite source projects reference Contracts with `PrivateAssets="all"` for
  compilation, while their NuGet dependency is the base `Mneme` package.
- Current source declares `0.1.0-alpha`; the known older consumer package was
  `0.0.1-alpha.1` built at `a185590`. Do not equate current source with installed
  consumer artifacts, or silently upgrade those consumers.
- Profile registration extends a trusted compiled set of payload types; it is not
  permission to deserialize arbitrary types or execute remembered instructions.

### Mneme.Code implemented contract

```csharp
services.AddMneme(...).AddMnemeCodeProfile();

// Actual public concepts; consult source/README for complete signatures.
CodeObservationPayload(
    Guid repositoryId, string sourceRevision,
    CodeObservationKind kind, string content,
    string? relativePath = null, string? symbol = null,
    string? evidenceReference = null);

// Kinds: Convention, ArchitectureObservation, FixLesson.
// Read service: ICodeMemory.FindAsync(CodeMemoryQuery, CapabilityToken, ct).
```

- Nonempty repository GUIDs; full lowercase 40/64-hex revisions.
- Exact revision matching; no stale fallback or inferred cross-revision validity.
- File/symbol matching is ordinal, with explicitly defined broader repo/file
  applicability. Query limits are 1-200.
- Conflicting observations remain independent advisory records.
- Live capability, channel, visibility, review and revocation checks apply to
  `ICodeMemory`. Generic core query/distill/dream paths do **not** inherit them.
- Missing visibility is owner-only on the Code read path. In review mode,
  epistemic observations require a positive approval; this also guards the
  post-WAL/pre-review-queue gap and rebuilding under stricter review policy.
- Core classification currently does not inspect satellite text the same way as
  built-in payloads; a no-hit Code observation can default to Shared. Hosts
  requiring private-by-default storage must configure a conservative classifier.
- Host owns write authentication, validated provenance and source permissions.
  Repository/file selectors are not additional token ACLs.
- Revocation hides reads; it does not purge append-only JSON or backups.

### Correctness rules for Assistant and consumers

- **No PII/secret storage is a repository rule.** `Private` visibility, encryption,
  or substituting a handle for a name does not automatically authorize storing
  birthdays, health details, home addresses, travel records, or linked personal
  information. Decide what remains in a host-controlled store and what derived
  data is permissible. Any relaxation needs an explicit architectural decision,
  not a silent package change.
- **Confidence is not authority.** Source assertions, extraction uncertainty, and
  user acceptance are different dimensions. Remembered suggestions cannot become
  accepted obligations just because they carry a citation or high confidence.
- **Ingest authorization is host-enforced.** Raw
  `IMemoryAgent.IngestAsync(CaptureEvent, ct)` does not take a token. A restricted
  facade and validating distiller/dreamer producers are needed; simply withholding
  `CurationCapability` does not prevent arbitrary payloads being appended.
- **ReviewBeforeDistill is not a universal read gate.** Skipping ingest observers
  does not prove every raw query, rebuild, or dreaming path filters pending items.
  Use the specific read path's actual guarantees and tests; do not reuse the
  earlier blanket claim that pending means invisible everywhere.
- **Automatic observations stay non-authoritative.** The selected consumer pattern
  uses isolated observation scopes on `AutoDistill`, with validated ingress and
  untrusted recall labels. Accepted cockpit decisions remain separate. No
  autonomous external writes, acceptance, completion, dismissal, or reassignment.
- **Principal is ownership, not speaker text.** The host supplies the authenticated
  principal used for visibility checks. Source speakers/counterparties are
  separately controlled references, not model-generated authorization identities.
- **Event IDs are globally first-write-wins.** Same ID with different content is
  not an update. Persist source/account/item/revision identity and fingerprint;
  reject inconsistent retries in the host. New revisions get new IDs. Finite
  `ListRecentAsync` scans are not durable deduplication.
- **Time dimensions are distinct.** Knowledge validity, recorded/ingest time,
  source revision and scheduled occurrence time are not interchangeable.
  Birthday date-only/unknown-year forms, recurrence exceptions, leap days,
  all-day vacations and flight time zones need explicit semantics.
- **Assistant's current `asOf` is not historical reconstruction.** It only changes
  the overdue calculation. The current ledger chooses latest visible state.
  Do not promise "what we knew then" from that parameter.
- **Raw citations are not proof of evidence.** Known source mappings, scope,
  current permission and support must be checked. Query summaries cannot recreate
  a missing provenance map; unavailable/stale evidence must be explicit.
- Treat the broader Assistant design as a proposal. ADR-0006 currently says
  Accepted for the narrower slice; respect ADR change/supersession conventions
  rather than silently declaring the broader personal-data model accepted.

## Files Modified

### Current uncommitted tracked edits

- `Mneme.slnx`: source/test entries for Assistant and Code (four additions).
- `docs\adr\README.md`: updated ADR-0005 status and added ADR-0006/0007 rows.

### Current new documentation

- `docs\adr\0006-personal-assistant-pillar.md`.
- `docs\adr\0007-code-memory-profile.md`.
- `progress.md` (this file).

### New Assistant package files

Under `src\Mneme.Assistant\`:

```text
AssistantMemory.cs
IAssistantMemory.cs
Mneme.Assistant.csproj
MnemeAssistantServiceCollectionExtensions.cs
PersonalAssistantProfile.cs
README.md
WaitingOnItem.cs
WaitingOnObservationDescriptor.cs
WaitingOnObservationPayload.cs
WaitingOnProjector.cs
WaitingOnSchemaModule.cs
```

Under `tests\Mneme.Assistant.Tests\`:

```text
Mneme.Assistant.Tests.csproj
WaitingOnLedgerTests.cs
WaitingOnPayloadTests.cs
```

### New Code package files

Under `src\Mneme.Code\`:

```text
CodeInput.cs
CodeMemory.cs
CodeMemoryQuery.cs
CodeObservation.cs
CodeObservationDescriptor.cs
CodeObservationKind.cs
CodeObservationPayload.cs
CodeObservationProjector.cs
CodeProfile.cs
CodeSchemaModule.cs
ICodeMemory.cs
Mneme.Code.csproj
MnemeCodeServiceCollectionExtensions.cs
README.md
```

Under `tests\Mneme.Code.Tests\`:

```text
CodeCapabilityTests.cs
CodeMemoryTests.cs
CodeObservationPayloadTests.cs
CodePersistenceTests.cs
CodeTestHost.cs
Mneme.Code.Tests.csproj
```

### Earlier committed file groups

The chronological commit references above are the authoritative historical diffs.
Important groups include `src\Mneme\Hosting\Profiles\`, serializer/redaction/search
integration, `src\Mneme.Writer\`, `tests\Mneme.Writer.Tests\`,
`src\Mneme\Projections\Projectors\FactTripleProjection.cs`, Studio.Agent's reset
feature, release metadata, security regression tests, and the planning docs.
The only deleted maintainer guide is `docs\PUBLISHING.md` in local commit
`dff4f69`; do not restore it into the public repository.

### Private recovery artifacts

The external backup under `%USERPROFILE%\Mneme-recovery\20260917-112111` contains
the 38-file pre-report snapshot, its SHA256 manifest, HEAD/status records, the
Code handoff, and a separate copy of this report. It does not guarantee recovery
of app session history or all per-session media/package artifacts.

## Next Step

**Finish correcting the app's repository registration without deleting
`C:\root\Mneme-agent-memory`.** Re-add that existing folder under its intended
`sargemonkey/mneme` identity, resolve/select the resulting project ID, check for
an existing Code owner session, and create exactly one `Mneme.Code development`
child using the requested settings above. Hand it this report and the Code
handoff; keep Assistant planning with the parent. Do not recreate the already
implemented Code package or change origin merely to bypass the guard.
