using Microsoft.Extensions.DependencyInjection;
using Mneme.Contracts;
using Mneme.Storage;
using Mneme.Writer;
using ClassificationLabel = Mneme.Contracts.Classification;

namespace Mneme.Code.Tests;

public sealed class CodeMemoryTests
{
    [Fact]
    public async Task Read_returns_immutable_knowledge_with_host_ownership_and_wal_timestamps()
    {
        using var host = new CodeTestHost();
        var knowledge = CodeTestHost.Payload(kind: CodeObservationKind.FixLesson, path: "src/Cache.cs",
            symbol: "Cache.Read", evidence: "test-run-17");
        var validAt = host.Clock.Now.AddDays(-1);
        var capture = host.Event("attribution", knowledge, validAt: validAt,
            recordedAt: host.Clock.Now.AddYears(1), source: "synthetic-code-host");
        var receipt = await host.Agent.IngestAsync(capture);

        var item = Assert.Single(await host.Memory.FindAsync(
            CodeTestHost.Query(path: "src/Cache.cs", symbol: "Cache.Read"), host.Token()));
        Assert.Equal(new CodeObservation(capture.EventId, capture.WorkstreamId, knowledge,
            capture.Provenance.Principal, capture.Provenance.Source, validAt, host.Clock.Now), item);
        Assert.Equal(receipt.RecordedAt, item.RecordedAt);
        Assert.NotEqual(capture.RecordedAt, item.RecordedAt);
        Assert.Equal(EpistemicCategory.Evidence, item.Knowledge.Category);
        Assert.Same(host.Memory, host.Services.GetRequiredService<ICodeMemory>());
    }

    [Fact]
    public async Task Repository_file_and_symbol_applicability_are_explicit_exact_and_case_sensitive()
    {
        using var host = new CodeTestHost();
        var observations = new (string Id, CodeObservationPayload Payload)[]
        {
            ("repo", CodeTestHost.Payload()),
            ("file", CodeTestHost.Payload(path: "src/Cache.cs")),
            ("symbol", CodeTestHost.Payload(path: "src/Cache.cs", symbol: "Cache.Read")),
            ("other-symbol", CodeTestHost.Payload(path: "src/Cache.cs", symbol: "Cache.Write")),
            ("other-file", CodeTestHost.Payload(path: "src/Cache.csx")),
            ("path-case", CodeTestHost.Payload(path: "src/cache.cs")),
            ("directory", CodeTestHost.Payload(path: "src")),
            ("other-repo", CodeTestHost.Payload(repository: CodeTestHost.OtherRepository)),
        };
        foreach (var (id, payload) in observations) await host.Agent.IngestAsync(host.Event(id, payload));
        await host.Agent.IngestAsync(host.Event("other-workstream", workstream: "other-code"));

        Assert.Equal(["repo"], await IdsAsync(host, CodeTestHost.Query()));
        Assert.Equal(["file", "repo"], await IdsAsync(host, CodeTestHost.Query(path: "src/Cache.cs")));
        Assert.Equal(["file", "repo", "symbol"], await IdsAsync(host,
            CodeTestHost.Query(path: "src/Cache.cs", symbol: "Cache.Read")));
        Assert.Equal(["file", "repo"], await IdsAsync(host,
            CodeTestHost.Query(path: "src/Cache.cs", symbol: "cache.read")));
        Assert.Equal(["path-case", "repo"], await IdsAsync(host, CodeTestHost.Query(path: "src/cache.cs")));
        Assert.Equal(["repo"], await IdsAsync(host, CodeTestHost.Query(path: "src/Absent.cs")));
        Assert.Equal(["other-repo"], await IdsAsync(host,
            CodeTestHost.Query(repository: CodeTestHost.OtherRepository)));
        Assert.Equal(["other-workstream"], (await host.Memory.FindAsync(
            CodeTestHost.Query(workstream: "other-code"), host.Token(workstream: "other-code")))
            .Select(item => item.EventId.Value).ToArray());
    }

    [Fact]
    public async Task Newer_ingest_does_not_make_an_old_revision_current()
    {
        using var host = new CodeTestHost();
        await host.Agent.IngestAsync(host.Event("current", CodeTestHost.Payload(revision: CodeTestHost.Revision)));
        host.Clock.Now = host.Clock.Now.AddMinutes(1);
        await host.Agent.IngestAsync(host.Event("backfill", CodeTestHost.Payload(revision: CodeTestHost.OtherRevision)));

        Assert.Equal(["current"], await IdsAsync(host, CodeTestHost.Query()));
        Assert.Equal(["backfill"], await IdsAsync(host, CodeTestHost.Query(revision: CodeTestHost.OtherRevision)));
        Assert.Empty(await IdsAsync(host, CodeTestHost.Query(revision: new string('a', 40))));
        host.Pipeline.RebuildAll();
        Assert.Equal(["current"], await IdsAsync(host, CodeTestHost.Query()));
    }

    [Fact]
    public async Task Event_id_is_global_first_write_wins_not_an_equality_receipt()
    {
        using var host = new CodeTestHost();
        var original = host.Event("opaque-id", CodeTestHost.Payload(content: "Keep the original observation."));
        Assert.False((await host.Agent.IngestAsync(original)).WasDuplicate);
        var stored = host.Scalar("SELECT payload_json FROM memory_events WHERE event_id = 'opaque-id';");
        Assert.True((await host.Agent.IngestAsync(original)).WasDuplicate);

        var changed = host.Event("opaque-id",
            CodeTestHost.Payload(revision: CodeTestHost.OtherRevision, repository: CodeTestHost.OtherRepository,
                content: "This changed body must not replace the first write."),
            workstream: "another-code", principal: "owner-b");
        Assert.True((await host.Agent.IngestAsync(changed)).WasDuplicate);
        Assert.Equal(stored, host.Scalar("SELECT payload_json FROM memory_events WHERE event_id = 'opaque-id';"));
        Assert.Equal(1, host.Count("memory_events"));
        Assert.Equal(1, host.Count("projection_code_observations"));
        Assert.Empty(await host.Memory.FindAsync(
            CodeTestHost.Query(repository: CodeTestHost.OtherRepository, revision: CodeTestHost.OtherRevision,
                workstream: "another-code"), host.Token(principal: "owner-b", workstream: "another-code")));

        Assert.False((await host.Agent.IngestAsync(
            host.Event("new-id", CodeTestHost.Payload(revision: CodeTestHost.OtherRevision)))).WasDuplicate);
        Assert.Equal(["new-id"], await IdsAsync(host, CodeTestHost.Query(revision: CodeTestHost.OtherRevision)));
        Assert.Equal(["opaque-id"], await IdsAsync(host, CodeTestHost.Query()));
        Assert.Equal(2, host.Count("memory_events"));
        Assert.Equal(2, host.Count("distillation_queue"));
    }

    [Theory]
    [InlineData(CodeObservationKind.Convention)]
    [InlineData(CodeObservationKind.ArchitectureObservation)]
    [InlineData(CodeObservationKind.FixLesson)]
    public async Task Contradicting_observations_remain_independent_advice(CodeObservationKind kind)
    {
        using var host = new CodeTestHost();
        await host.Agent.IngestAsync(host.Event("observation-a",
            CodeTestHost.Payload(kind: kind, content: "Prefer synchronous repository reads.", evidence: "run-a")));
        await host.Agent.IngestAsync(host.Event("observation-b",
            CodeTestHost.Payload(kind: kind, content: "Prefer asynchronous repository reads.", evidence: "run-b"),
            principal: "owner-b"));
        await host.Agent.IngestAsync(host.Event("other-kind",
            CodeTestHost.Payload(kind: kind == CodeObservationKind.Convention
                ? CodeObservationKind.FixLesson : CodeObservationKind.Convention)));

        var items = await host.Memory.FindAsync(CodeTestHost.Query(kind: kind), host.Token());
        Assert.Equal(2, items.Count);
        Assert.Equal(["observation-a", "observation-b"],
            items.Select(item => item.EventId.Value).Order(StringComparer.Ordinal).ToArray());
        Assert.All(items, item => Assert.Equal(EpistemicCategory.Evidence, item.Knowledge.Category));
        Assert.Equal(2, items.Select(item => item.Principal).Distinct().Count());
        Assert.Equal(2, items.Select(item => item.Knowledge.EvidenceReference).Distinct().Count());
        Assert.Equal(0, host.Count("projection_decisions"));
        Assert.Equal(0, host.Count("projection_skills"));
        Assert.Equal(0, host.Count("memory_contradictions"));
    }

    [Fact]
    public async Task Current_clock_window_is_independent_of_revision_and_producer_recording_time()
    {
        using var host = new CodeTestHost();
        var now = host.Clock.Now;
        await host.Agent.IngestAsync(host.Event("valid-now", recordedAt: now.AddYears(1)));
        await host.Agent.IngestAsync(host.Event("valid-later", validAt: now.AddMinutes(2)));
        Assert.Equal(["valid-now"], await IdsAsync(host, CodeTestHost.Query()));

        host.Clock.Now = now.AddSeconds(-1);
        Assert.Empty(await IdsAsync(host, CodeTestHost.Query()));
        host.Clock.Now = now.AddMinutes(3);
        Assert.Equal(["valid-later", "valid-now"], await IdsAsync(host, CodeTestHost.Query()));
    }

    [Fact]
    public async Task Results_are_bounded_and_newest_first_with_deterministic_ties()
    {
        using var host = new CodeTestHost();
        await host.Agent.IngestAsync(host.Event("old"));
        host.Clock.Now = host.Clock.Now.AddMinutes(1);
        await host.Agent.IngestAsync(host.Event("new-b"));
        await host.Agent.IngestAsync(host.Event("new-a"));

        var items = await host.Memory.FindAsync(CodeTestHost.Query(limit: 2), host.Token());
        Assert.Equal(["new-a", "new-b"], items.Select(item => item.EventId.Value).ToArray());
        Assert.Throws<NotSupportedException>(() => ((IList<CodeObservation>)items).Clear());
        Assert.Equal(3, (await host.Memory.FindAsync(CodeTestHost.Query(limit: 200), host.Token())).Count);
    }

    [Fact]
    public async Task Code_builtin_and_writer_payloads_coexist_and_rebuild_without_cross_projection()
    {
        using var host = new CodeTestHost(writer: true);
        EventPayload[] payloads =
        [
            CodeTestHost.Payload(),
            new EvidencePayload("A synthetic base observation.", "fixture"),
            new FactPayload("A synthetic base fact.", []),
            new DecisionPayload("A host-approved choice.", "Host rationale.", [], new PrincipalId("owner-a")),
            new HypothesisPayload("A base hypothesis.", HypothesisState.Open),
            new GoalPayload("A base goal.", GoalState.Active),
            new ActionPayload("A base action.", null, null),
            new OutcomePayload("A base outcome.", new EventId("coexist-6"), OutcomePolarity.Positive),
            new SkillPayload("A base skill.", "Inspect the synthetic fixture.", null, []),
            new AuthoringClaimPayload("The character has blue eyes.", "Character", "eye colour", "blue"),
            new NarrativeCommitmentPayload("commitment-1", CommitmentRole.Setup, "A narrative setup."),
        ];
        for (var i = 0; i < payloads.Length; i++)
        {
            var json = EventSerialization.SerializePayload(payloads[i]);
            Assert.Equal(payloads[i].GetType(), EventSerialization.DeserializePayload(json).GetType());
            await host.Agent.IngestAsync(host.Event($"coexist-{i}", payloads[i]));
        }

        Assert.Equal(["coexist-0"], await IdsAsync(host, CodeTestHost.Query()));
        Assert.Equal(1, host.Count("projection_code_observations"));
        Assert.Equal(1, host.Count("projection_skills"));
        Assert.Equal(1, host.Count("projection_narrative_commitments"));
        var counts = host.Pipeline.RebuildAll();
        Assert.Equal(1, counts["code-observations"]);
        Assert.Equal(payloads.Length, host.Count("memory_events"));
        Assert.Equal(["coexist-0"], await IdsAsync(host, CodeTestHost.Query()));
        var writer = host.Services.GetRequiredService<IWriterMemory>();
        Assert.Single(await writer.GetOpenCommitmentsAsync(host.Token() with
        {
            AllowedCategories = [EpistemicCategory.Goal],
        }));
    }

    [Fact]
    public async Task Private_newer_conflict_does_not_hide_an_older_visible_observation_before_limit()
    {
        using var host = new CodeTestHost(classifier: new FixedClassifier(ClassificationLabel.Confidential));
        await host.Agent.IngestAsync(host.Event("visible", principal: "owner-a"));
        host.Clock.Now = host.Clock.Now.AddMinutes(1);
        await host.Agent.IngestAsync(host.Event("hidden",
            CodeTestHost.Payload(content: "A conflicting private observation."), principal: "owner-b"));

        Assert.Equal(["visible"], await IdsAsync(host, CodeTestHost.Query(limit: 1)));
        var hiddenOwner = Assert.Single(await host.Memory.FindAsync(CodeTestHost.Query(limit: 1),
            host.Token(principal: "owner-b")));
        Assert.Equal("hidden", hiddenOwner.EventId.Value);
    }

    private static async Task<string[]> IdsAsync(CodeTestHost host, CodeMemoryQuery query) =>
        (await host.Memory.FindAsync(query, host.Token())).Select(item => item.EventId.Value)
            .Order(StringComparer.Ordinal).ToArray();
}
