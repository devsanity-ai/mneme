using Microsoft.Extensions.DependencyInjection;
using Mneme.Contracts;
using Mneme.Hosting.Profiles;
using Mneme.Ingest.Redaction;
using Mneme.Projections;
using Mneme.Review;
using Mneme.Revocation;
using Mneme.Storage;

namespace Mneme.Code.Tests;

public sealed class CodePersistenceTests
{
    [Fact]
    public void Profile_composition_is_idempotent_and_contributions_are_read_only()
    {
        using var host = new CodeTestHost();
        var profile = host.Services.GetRequiredService<CodeProfile>();
        Assert.IsAssignableFrom<IMnemeProfile>(profile);
        Assert.Equal("code", profile.Name);
        var schema = Assert.Single(profile.SchemaModules);
        Assert.Equal("code-observations", schema.Name);
        Assert.Equal(1, schema.Version);
        Assert.Equal("1", host.Scalar("SELECT value FROM schema_meta WHERE key = 'module:code-observations';"));
        Assert.Equal(EpistemicCategory.Evidence, Assert.Single(profile.Projectors).Category);
        Assert.Single(host.Services.GetServices<IProjector>(), projector => projector.Name == "code-observations");
        Assert.Throws<NotSupportedException>(() => ((IList<IPayloadDescriptor>)profile.PayloadDescriptors).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<ISchemaModule>)profile.SchemaModules).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<IProjector>)profile.Projectors).Clear());
        using var connection = host.Connections.Open();
        schema.ApplyDdl(connection);
        Assert.Equal(0, host.Count("projection_code_observations"));
    }

    [Fact]
    public void Composition_requires_services_and_base_mneme_first()
    {
        Assert.Throws<ArgumentNullException>(() =>
            MnemeCodeServiceCollectionExtensions.AddMnemeCodeProfile(null!));
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddMnemeCodeProfile());
    }

    [Fact]
    public async Task Default_secret_redaction_precedes_wal_projection_and_text_index()
    {
        using var host = new CodeTestHost();
        const string content = "Rotate password = syntheticcontent123";
        const string evidence = "test report password = syntheticevidence456";
        var payload = CodeTestHost.Payload(content: content, evidence: evidence, path: "src/Cache.cs",
            symbol: "Cache.Read");
        await host.Agent.IngestAsync(host.Event("redaction", payload));

        var wal = Assert.IsType<string>(host.Scalar("SELECT payload_json FROM memory_events WHERE event_id = 'redaction';"));
        var indexed = Assert.IsType<string>(host.Scalar(
            "SELECT content FROM event_text_index WHERE event_id = 'redaction';"));
        foreach (var text in new[] { wal, indexed })
        {
            Assert.DoesNotContain("syntheticcontent123", text);
            Assert.DoesNotContain("syntheticevidence456", text);
            Assert.Contains("<REDACTED:", text);
        }
        Assert.Equal("src/Cache.cs", host.Scalar("SELECT relative_path FROM projection_code_observations;"));
        Assert.Equal("Cache.Read", host.Scalar("SELECT symbol FROM projection_code_observations;"));
        var item = Assert.Single(await host.Memory.FindAsync(
            CodeTestHost.Query(path: "src/Cache.cs", symbol: "Cache.Read"), host.Token()));
        Assert.Equal(EventSerialization.DeserializePayload(wal), item.Knowledge);
        Assert.Empty(await host.Memory.FindAsync(
            CodeTestHost.Query(path: "src/Cache.cs", symbol: "Cache.Read"), host.Token("owner-b")));
        Assert.Equal(content, payload.Content);
        Assert.Equal(evidence, payload.EvidenceReference);
    }

    [Theory]
    [InlineData("revision", false)]
    [InlineData("path", false)]
    [InlineData("symbol", false)]
    [InlineData("revision", true)]
    [InlineData("path", true)]
    [InlineData("symbol", true)]
    public async Task Redaction_hits_or_silent_changes_to_scopes_reject_before_any_persistence(
        string field, bool silentChange)
    {
        var sensitive = field switch
        {
            "revision" => CodeTestHost.Revision,
            "path" => "src/sensitive.cs",
            _ => "Sensitive.Read",
        };
        var redactor = new TestRedactor(value => value == sensitive
            ? new RedactionResult(silentChange ? "changed" : value,
                silentChange ? [] : [new RedactionHit("fixture", "[REDACTED]", 0, value.Length)])
            : new RedactionResult(value, []));
        using var host = new CodeTestHost(redactor);
        var payload = CodeTestHost.Payload(path: "src/sensitive.cs", symbol: "Sensitive.Read");

        var error = await Assert.ThrowsAsync<ArgumentException>(() => host.Agent.IngestAsync(host.Event("unsafe", payload)));
        Assert.DoesNotContain(sensitive, error.Message);
        Assert.Equal(0, host.Count("memory_events"));
        Assert.Equal(0, host.Count("projection_code_observations"));
        Assert.Equal(0, host.Count("distillation_queue"));
        Assert.Equal(0, host.Count("event_text_index"));
        Assert.Equal(0, host.Count("review_queue"));
    }

    [Theory]
    [InlineData("content")]
    [InlineData("evidence")]
    public async Task Invalid_redactor_output_fails_instead_of_silently_dropping_a_field(string field)
    {
        var sensitive = field == "content" ? "synthetic-content" : "synthetic-evidence";
        using var host = new CodeTestHost(new TestRedactor(value =>
            new RedactionResult(value == sensitive ? "" : value, [])));
        await Assert.ThrowsAsync<ArgumentException>(() => host.Agent.IngestAsync(host.Event("invalid-redaction",
            CodeTestHost.Payload(content: "synthetic-content", evidence: "synthetic-evidence"))));
        Assert.Equal(0, host.Count("memory_events"));
        Assert.Equal(0, host.Count("projection_code_observations"));
    }

    [Fact]
    public async Task Live_revocation_hides_an_unchanged_projection_before_ranking_and_after_rebuild()
    {
        using var host = new CodeTestHost();
        await host.Agent.IngestAsync(host.Event("retained"));
        host.Clock.Now = host.Clock.Now.AddMinutes(1);
        await host.Agent.IngestAsync(host.Event("revoked"));
        var wal = host.Scalar("SELECT payload_json FROM memory_events WHERE event_id = 'revoked';");
        var revocations = host.Services.GetRequiredService<IRevocationService>();
        await revocations.RevokeAsync(new EventId("revoked"), new WorkstreamId(CodeTestHost.Workstream),
            new PrincipalId("owner-a"), "Synthetic observation withdrawn.");

        Assert.Equal(2, host.Count("projection_code_observations"));
        Assert.Equal("retained", Assert.Single(await host.Memory.FindAsync(
            CodeTestHost.Query(limit: 1), host.Token())).EventId.Value);
        Assert.Equal(wal, host.Scalar("SELECT payload_json FROM memory_events WHERE event_id = 'revoked';"));

        host.Execute("DELETE FROM projection_code_observations;");
        Assert.Empty(await host.Memory.FindAsync(CodeTestHost.Query(), host.Token()));
        Assert.Equal(2, host.Pipeline.RebuildAll()["code-observations"]);
        Assert.Equal("retained", Assert.Single(await host.Memory.FindAsync(CodeTestHost.Query(), host.Token())).EventId.Value);
        Assert.Equal(2, host.Count("memory_events"));
        Assert.Equal(1, host.Count("memory_revocations"));
        Assert.Equal(wal, host.Scalar("SELECT payload_json FROM memory_events WHERE event_id = 'revoked';"));
    }

    [Fact]
    public async Task Review_gate_survives_rebuild_and_approval_does_not_confer_decision_authority()
    {
        using var host = new CodeTestHost();
        var workstream = new WorkstreamId(CodeTestHost.Workstream);
        host.Services.GetRequiredService<WorkstreamConfigStore>().SetMode(workstream, WorkstreamMode.ReviewBeforeDistill);
        foreach (var id in new[] { "approved", "pending", "deferred", "rejected" })
        {
            await host.Agent.IngestAsync(host.Event(id,
                CodeTestHost.Payload(kind: CodeObservationKind.ArchitectureObservation,
                    content: "An untrusted suggestion, not an accepted design.", evidence: "unverified-reference")));
            host.Clock.Now = host.Clock.Now.AddMinutes(1);
        }
        Assert.Equal(0, host.Count("projection_code_observations"));
        Assert.Empty(await host.Memory.FindAsync(CodeTestHost.Query(), host.Token()));

        var queue = host.Services.GetRequiredService<IReviewQueue>();
        var cap = new CurationCapability(new PrincipalId("reviewer"), workstream,
            DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1), CanReview: true);
        await queue.ApproveAsync(new EventId("approved"), cap);
        await queue.DeferAsync(new EventId("deferred"), host.Clock.Now.AddHours(-1), cap);
        await queue.RejectAsync(new EventId("rejected"), "Synthetic rejection.", cap);
        var before = Assert.Single(await host.Memory.FindAsync(CodeTestHost.Query(limit: 1), host.Token()));
        Assert.Equal("approved", before.EventId.Value);

        var eventCount = host.Count("memory_events");
        Assert.Equal(4, host.Pipeline.RebuildAll()["code-observations"]);
        Assert.Equal(4, host.Count("projection_code_observations"));
        var after = Assert.Single(await host.Memory.FindAsync(CodeTestHost.Query(limit: 1), host.Token()));
        Assert.Equal(before, after);
        Assert.Equal(EpistemicCategory.Evidence, after.Knowledge.Category);
        Assert.Equal(new PrincipalId("owner-a"), after.Principal);
        Assert.Equal(eventCount, host.Count("memory_events"));
        Assert.Equal(0, host.Count("projection_decisions"));
    }

    [Fact]
    public async Task Review_mode_fails_closed_when_rebuild_exposes_an_event_before_it_is_queued()
    {
        using var host = new CodeTestHost();
        var workstream = new WorkstreamId(CodeTestHost.Workstream);
        host.Services.GetRequiredService<WorkstreamConfigStore>().SetMode(workstream, WorkstreamMode.ReviewBeforeDistill);
        await host.Agent.IngestAsync(host.Event("awaiting-queue"));
        host.Pipeline.RebuildAll();
        // Model the core's post-WAL, pre-queue interval without changing the log.
        host.Execute("DELETE FROM review_queue WHERE event_id = 'awaiting-queue';");
        Assert.Equal(1, host.Count("projection_code_observations"));
        Assert.Empty(await host.Memory.FindAsync(CodeTestHost.Query(), host.Token()));

        await host.Agent.IngestAsync(host.Event("technical", channel: EventChannel.Technical));
        Assert.Empty(await host.Memory.FindAsync(CodeTestHost.Query(), host.Token()));
        var technical = Assert.Single(await host.Memory.FindAsync(CodeTestHost.Query(),
            host.Token() with { IncludeTechnical = true }));
        Assert.Equal("technical", technical.EventId.Value);
    }

    [Fact]
    public async Task Switching_to_review_mode_also_hides_prior_unreviewed_auto_observations()
    {
        using var host = new CodeTestHost();
        await host.Agent.IngestAsync(host.Event("auto"));
        Assert.Single(await host.Memory.FindAsync(CodeTestHost.Query(), host.Token()));
        var config = host.Services.GetRequiredService<WorkstreamConfigStore>();
        var workstream = new WorkstreamId(CodeTestHost.Workstream);
        config.SetMode(workstream, WorkstreamMode.ReviewBeforeDistill);
        Assert.Empty(await host.Memory.FindAsync(CodeTestHost.Query(), host.Token()));
        host.Pipeline.RebuildAll();
        Assert.Empty(await host.Memory.FindAsync(CodeTestHost.Query(), host.Token()));
        config.SetMode(workstream, WorkstreamMode.AutoDistill);
        Assert.Single(await host.Memory.FindAsync(CodeTestHost.Query(), host.Token()));
    }
}
