using Microsoft.Extensions.DependencyInjection;
using Mneme.Contracts;
using Mneme.Hosting;
using Mneme.Projections;
using Mneme.Revocation;
using Mneme.Review;
using Mneme.Storage;

namespace Mneme.Assistant.Tests;

/// <summary>
/// End-to-end proof of the <c>Mneme.Assistant</c> waiting-on slice (ADR-0006):
/// a candidate <see cref="WaitingOnObservationPayload"/> projected into the
/// profile's own <c>projection_waiting_on</c> ledger yields the agenda query
/// "what am I waiting on, from whom, overdue as-of-now?" — which base Mneme's
/// <c>QueryAsync</c> cannot express — with append-only lifecycle, capability
/// guards, visibility filtering, and rebuildability from the log.
/// </summary>
public sealed class WaitingOnLedgerTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero));
    private const string Ws = "pa-observations-user";

    public WaitingOnLedgerTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "mneme-pa-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
    }

    public void Dispose() { try { Directory.Delete(_tmpDir, true); } catch { /* best effort */ } }

    // ── host + helpers ────────────────────────────────────────────────────

    private (ServiceProvider sp, IMemoryAgent agent, IAssistantMemory pa) BuildHost()
    {
        var services = new ServiceCollection();
        // Register the mutable clock BEFORE AddMneme so its TryAddSingleton keeps ours.
        services.AddSingleton<TimeProvider>(_clock);
        services.AddMneme(o =>
        {
            o.WorkstreamId = Ws;
            o.SqlitePath = Path.Combine(_tmpDir, "pa.mneme.db");
            o.UserId = "daemon";
        }).AddMnemeAssistantProfile();
        var sp = services.BuildServiceProvider();
        return (sp, sp.GetRequiredService<IMemoryAgent>(), sp.GetRequiredService<IAssistantMemory>());
    }

    private CaptureEvent WaitingOn(string eventId, string waitingOnId, string counterparty, string awaited,
        WaitingOnStatus status = WaitingOnStatus.Waiting, DateTimeOffset? expectedBy = null, string principal = "daemon")
    {
        var at = _clock.GetUtcNow();
        return new CaptureEvent(
            new EventId(eventId), new WorkstreamId(Ws), EventChannel.Epistemic,
            ValidAt: at, RecordedAt: at,
            new WaitingOnObservationPayload(waitingOnId, counterparty, awaited, Since: at, Status: status, ExpectedBy: expectedBy),
            new CaptureProvenance(new CaptureSourceId("daemon"), new PrincipalId(principal)));
    }

    private CapabilityToken Token(string principal = "daemon")
        => new(
            Principal: new PrincipalId(principal),
            Workstream: new WorkstreamId(Ws),
            NotBefore: _clock.GetUtcNow().AddYears(-1),
            NotAfter: _clock.GetUtcNow().AddYears(1),
            AllowedCategories: Array.Empty<EpistemicCategory>());

    // ── the headline: open waiting-on is a first-class agenda item ────────

    [Fact]
    public async Task A_waiting_on_with_no_resolution_is_open()
    {
        var (sp, agent, pa) = BuildHost();
        using var _ = sp;

        await agent.IngestAsync(WaitingOn("w1", "wait:budget", "alice", "budget sign-off"));

        var open = await pa.GetOpenWaitingOnAsync(Token());
        var item = Assert.Single(open);
        Assert.Equal("wait:budget", item.WaitingOnId);
        Assert.Equal("alice", item.Counterparty);
        Assert.Equal("budget sign-off", item.AwaitedItem);
        Assert.Equal(new EventId("w1"), item.SourceEvent);
        Assert.False(item.IsOverdue);
    }

    [Fact]
    public async Task A_fulfilled_waiting_on_is_no_longer_open()
    {
        var (sp, agent, pa) = BuildHost();
        using var _ = sp;

        await agent.IngestAsync(WaitingOn("w1", "wait:budget", "alice", "budget sign-off"));
        _clock.Advance(TimeSpan.FromMinutes(30)); // later reveal_time
        await agent.IngestAsync(WaitingOn("w2", "wait:budget", "alice", "budget sign-off", WaitingOnStatus.Fulfilled));

        Assert.Empty(await pa.GetOpenWaitingOnAsync(Token()));
    }

    [Fact]
    public async Task Released_also_closes_the_item_and_latest_status_wins()
    {
        var (sp, agent, pa) = BuildHost();
        using var _ = sp;

        // waiting -> released -> re-opened (waiting again): latest event wins => open.
        await agent.IngestAsync(WaitingOn("w1", "wait:doc", "bob", "the draft"));
        _clock.Advance(TimeSpan.FromMinutes(10));
        await agent.IngestAsync(WaitingOn("w2", "wait:doc", "bob", "the draft", WaitingOnStatus.Released));
        Assert.Empty(await pa.GetOpenWaitingOnAsync(Token()));

        _clock.Advance(TimeSpan.FromMinutes(10));
        await agent.IngestAsync(WaitingOn("w3", "wait:doc", "bob", "the draft", WaitingOnStatus.Waiting));
        var item = Assert.Single(await pa.GetOpenWaitingOnAsync(Token()));
        Assert.Equal(new EventId("w3"), item.SourceEvent);
    }

    [Fact]
    public async Task Distinct_waiting_on_ids_are_independent()
    {
        var (sp, agent, pa) = BuildHost();
        using var _ = sp;

        await agent.IngestAsync(WaitingOn("a1", "wait:a", "alice", "thing a"));
        await agent.IngestAsync(WaitingOn("b1", "wait:b", "bob", "thing b"));
        _clock.Advance(TimeSpan.FromMinutes(5));
        await agent.IngestAsync(WaitingOn("a2", "wait:a", "alice", "thing a", WaitingOnStatus.Fulfilled));

        var item = Assert.Single(await pa.GetOpenWaitingOnAsync(Token())); // only wait:b remains
        Assert.Equal("wait:b", item.WaitingOnId);
    }

    [Fact]
    public async Task Overdue_is_computed_against_the_as_of_instant_and_ordering_is_due_soonest_first()
    {
        var (sp, agent, pa) = BuildHost();
        using var _ = sp;

        var now = _clock.GetUtcNow();
        await agent.IngestAsync(WaitingOn("o1", "wait:overdue", "alice", "late reply", expectedBy: now.AddDays(-2)));
        await agent.IngestAsync(WaitingOn("o2", "wait:soon", "bob", "soon reply", expectedBy: now.AddDays(1)));
        await agent.IngestAsync(WaitingOn("o3", "wait:nodate", "carol", "someday reply", expectedBy: null));

        var open = await pa.GetOpenWaitingOnAsync(Token(), asOf: now);
        Assert.Equal(3, open.Count);
        // due-first, soonest first; null-due last.
        Assert.Equal("wait:overdue", open[0].WaitingOnId);
        Assert.True(open[0].IsOverdue);
        Assert.Equal("wait:soon", open[1].WaitingOnId);
        Assert.False(open[1].IsOverdue);
        Assert.Equal("wait:nodate", open[2].WaitingOnId);
        Assert.False(open[2].IsOverdue); // no due date => never overdue
    }

    [Fact]
    public async Task As_of_only_changes_overdue_calculation_not_lifecycle_or_capability_time()
    {
        var (sp, agent, pa) = BuildHost();
        using var _ = sp;
        var earlier = _clock.GetUtcNow();
        await agent.IngestAsync(WaitingOn("w1", "wait:budget", "alice", "initial candidate",
            expectedBy: earlier.AddDays(1)));
        _clock.Advance(TimeSpan.FromDays(2));
        await agent.IngestAsync(WaitingOn("w2", "wait:budget", "alice", "updated candidate",
            expectedBy: earlier.AddDays(1)));
        var item = Assert.Single(await pa.GetOpenWaitingOnAsync(Token(), asOf: earlier));
        Assert.Equal(new EventId("w2"), item.SourceEvent);
        Assert.False(item.IsOverdue);
        Assert.True(Assert.Single(await pa.GetOpenWaitingOnAsync(Token())).IsOverdue);
        await Assert.ThrowsAsync<CapabilityDeniedError>(() =>
            pa.GetOpenWaitingOnAsync(Token() with { NotAfter = earlier.AddHours(1) }, asOf: earlier));

        _clock.Advance(TimeSpan.FromMinutes(30));
        await agent.IngestAsync(WaitingOn("w3", "wait:budget", "alice", "updated candidate",
            WaitingOnStatus.Fulfilled));
        Assert.Empty(await pa.GetOpenWaitingOnAsync(Token(), asOf: earlier));
    }

    // ── invariant: rebuildable from the append-only log ───────────────────

    [Fact]
    public async Task The_ledger_is_rebuildable_from_the_log()
    {
        var (sp, agent, pa) = BuildHost();
        using var _ = sp;

        await agent.IngestAsync(WaitingOn("w1", "wait:budget", "alice", "budget sign-off"));
        await agent.IngestAsync(WaitingOn("w2", "wait:doc", "bob", "the draft"));
        _clock.Advance(TimeSpan.FromMinutes(30));
        await agent.IngestAsync(WaitingOn("w3", "wait:budget", "alice", "budget sign-off", WaitingOnStatus.Fulfilled));
        Assert.Single(await pa.GetOpenWaitingOnAsync(Token())); // only wait:doc open

        var factory = sp.GetRequiredService<SqliteConnectionFactory>();
        using (var conn = factory.Open())
        using (var del = conn.CreateCommand())
        {
            del.CommandText = "DELETE FROM projection_waiting_on;";
            del.ExecuteNonQuery();
        }
        Assert.Empty(await pa.GetOpenWaitingOnAsync(Token()));

        var counts = sp.GetRequiredService<ProjectorPipeline>().RebuildAll();
        Assert.True(counts.ContainsKey("assistant-waiting-on"),
            "assistant projector did not participate in RebuildAll");
        Assert.Equal(3, counts["assistant-waiting-on"]);

        var item = Assert.Single(await pa.GetOpenWaitingOnAsync(Token()));
        Assert.Equal("wait:doc", item.WaitingOnId);
    }

    [Fact]
    public async Task Live_revocation_filters_open_items_and_lifecycle_updates_before_limit_and_after_rebuild()
    {
        var (sp, agent, pa) = BuildHost();
        using var _ = sp;
        var now = _clock.GetUtcNow();
        await agent.IngestAsync(WaitingOn("w1", "wait:budget", "alice", "budget sign-off"));
        await agent.IngestAsync(WaitingOn("w2", "wait:withdrawn", "bob", "withdrawn reply",
            expectedBy: now.AddDays(-1)));
        _clock.Advance(TimeSpan.FromMinutes(30));
        await agent.IngestAsync(WaitingOn("w3", "wait:budget", "alice", "budget sign-off",
            WaitingOnStatus.Fulfilled));
        Assert.Equal(new EventId("w2"), Assert.Single(await pa.GetOpenWaitingOnAsync(Token())).SourceEvent);

        var factory = sp.GetRequiredService<SqliteConnectionFactory>();
        string ReadWal()
        {
            using var c = factory.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT event_id, payload_json FROM memory_events ORDER BY event_id;";
            using var reader = cmd.ExecuteReader();
            var rows = new List<string>();
            while (reader.Read()) rows.Add(reader.GetString(0) + ":" + reader.GetString(1));
            return string.Join("\n", rows);
        }
        var wal = ReadWal();
        var revocations = sp.GetRequiredService<IRevocationService>();
        foreach (var id in new[] { "w2", "w3" })
            await revocations.RevokeAsync(new EventId(id), new WorkstreamId(Ws),
                new PrincipalId("daemon"), "Observation withdrawn.");

        Assert.Equal(new EventId("w1"),
            Assert.Single(await pa.GetOpenWaitingOnAsync(Token(), limit: 1)).SourceEvent);
        Assert.Equal(wal, ReadWal());
        Assert.Equal(3, sp.GetRequiredService<ProjectorPipeline>().RebuildAll()["assistant-waiting-on"]);
        Assert.Equal(new EventId("w1"),
            Assert.Single(await pa.GetOpenWaitingOnAsync(Token(), limit: 1)).SourceEvent);
        Assert.Equal(wal, ReadWal());
    }

    [Fact]
    public async Task Review_held_candidates_and_fulfilled_updates_stay_hidden_after_rebuild_and_mode_changes()
    {
        var (sp, agent, pa) = BuildHost();
        using var _ = sp;
        var workstream = new WorkstreamId(Ws);
        var config = sp.GetRequiredService<WorkstreamConfigStore>();
        config.SetMode(workstream, WorkstreamMode.ReviewBeforeDistill);
        foreach (var id in new[] { "approved", "pending", "deferred", "rejected" })
        {
            await agent.IngestAsync(WaitingOn(id, "wait:" + id, "alice", "a proposed reply"));
            _clock.Advance(TimeSpan.FromMinutes(1));
        }
        await agent.IngestAsync(WaitingOn("pending-close", "wait:approved", "alice", "a proposed reply",
            WaitingOnStatus.Fulfilled));
        Assert.Empty(await pa.GetOpenWaitingOnAsync(Token()));

        var queue = sp.GetRequiredService<IReviewQueue>();
        var cap = new CurationCapability(new PrincipalId("reviewer"), workstream,
            DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1), CanReview: true);
        await queue.ApproveAsync(new EventId("approved"), cap);
        await queue.DeferAsync(new EventId("deferred"), _clock.GetUtcNow().AddHours(1), cap);
        await queue.RejectAsync(new EventId("rejected"), "Candidate rejected by the user.", cap);
        var before = Assert.Single(await pa.GetOpenWaitingOnAsync(Token()));
        Assert.Equal(new EventId("approved"), before.SourceEvent);

        Assert.Equal(5, sp.GetRequiredService<ProjectorPipeline>().RebuildAll()["assistant-waiting-on"]);
        Assert.Equal(before, Assert.Single(await pa.GetOpenWaitingOnAsync(Token())));
        _clock.Advance(TimeSpan.FromHours(2)); // Expiring a defer is not approval.
        config.SetMode(workstream, WorkstreamMode.AutoDistill);
        Assert.Equal(before, Assert.Single(await pa.GetOpenWaitingOnAsync(Token(), limit: 1)));
        Assert.Equal(before, Assert.Single(await pa.GetOpenWaitingOnAsync(Token())));

        await queue.ApproveAsync(new EventId("pending-close"), cap);
        Assert.Empty(await pa.GetOpenWaitingOnAsync(Token()));
        sp.GetRequiredService<ProjectorPipeline>().RebuildAll();
        Assert.Empty(await pa.GetOpenWaitingOnAsync(Token()));

        var factory = sp.GetRequiredService<SqliteConnectionFactory>();
        using var c = factory.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM projection_decisions;";
        Assert.Equal(0L, (long)cmd.ExecuteScalar()!); // Approval did not accept a user-owned decision.
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Review_mode_requires_positive_approval_even_for_unqueued_candidates_and_lifecycle_updates(
        bool simulatePostWalPreQueue)
    {
        var (sp, agent, pa) = BuildHost();
        using var _ = sp;
        var workstream = new WorkstreamId(Ws);
        var config = sp.GetRequiredService<WorkstreamConfigStore>();
        config.SetMode(workstream, WorkstreamMode.ReviewBeforeDistill);
        await agent.IngestAsync(WaitingOn("approved", "wait:budget", "alice", "budget sign-off"));
        var cap = new CurationCapability(new PrincipalId("reviewer"), workstream,
            DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1), CanReview: true);
        await sp.GetRequiredService<IReviewQueue>().ApproveAsync(new EventId("approved"), cap);
        if (!simulatePostWalPreQueue) config.SetMode(workstream, WorkstreamMode.AutoDistill);

        _clock.Advance(TimeSpan.FromMinutes(30));
        await agent.IngestAsync(WaitingOn("unqueued-close", "wait:budget", "alice", "budget sign-off",
            WaitingOnStatus.Fulfilled));
        await agent.IngestAsync(WaitingOn("unqueued-open", "wait:private", "bob", "an unreviewed candidate"));
        if (simulatePostWalPreQueue)
        {
            sp.GetRequiredService<ProjectorPipeline>().RebuildAll();
            var factory = sp.GetRequiredService<SqliteConnectionFactory>();
            using var c = factory.Open();
            using var cmd = c.CreateCommand();
            // Model a rebuild between the core WAL commit and review-queue insertion.
            cmd.CommandText = "DELETE FROM review_queue WHERE event_id IN ('unqueued-close', 'unqueued-open');";
            cmd.ExecuteNonQuery();
        }
        config.SetMode(workstream, WorkstreamMode.ReviewBeforeDistill);
        Assert.Equal(new EventId("approved"), Assert.Single(await pa.GetOpenWaitingOnAsync(Token())).SourceEvent);
        sp.GetRequiredService<ProjectorPipeline>().RebuildAll();
        Assert.Equal(new EventId("approved"), Assert.Single(await pa.GetOpenWaitingOnAsync(Token())).SourceEvent);
        config.SetMode(workstream, WorkstreamMode.AutoDistill);
        Assert.Equal(new EventId("unqueued-open"), Assert.Single(await pa.GetOpenWaitingOnAsync(Token())).SourceEvent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Technical_candidates_and_lifecycle_updates_require_permission_even_after_rebuild(
        bool reviewMode)
    {
        var (sp, agent, pa) = BuildHost();
        using var _ = sp;
        var workstream = new WorkstreamId(Ws);
        if (reviewMode)
            sp.GetRequiredService<WorkstreamConfigStore>().SetMode(workstream, WorkstreamMode.ReviewBeforeDistill);
        await agent.IngestAsync(WaitingOn("epistemic", "wait:budget", "alice", "budget sign-off"));
        if (reviewMode)
        {
            var cap = new CurationCapability(new PrincipalId("reviewer"), workstream,
                DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1), CanReview: true);
            await sp.GetRequiredService<IReviewQueue>().ApproveAsync(new EventId("epistemic"), cap);
        }
        _clock.Advance(TimeSpan.FromMinutes(30));
        await agent.IngestAsync(WaitingOn("technical-close", "wait:budget", "alice", "budget sign-off",
            WaitingOnStatus.Fulfilled) with { Channel = EventChannel.Technical });
        await agent.IngestAsync(WaitingOn("technical-open", "wait:technical", "bob", "technical reply",
            expectedBy: _clock.GetUtcNow().AddDays(-1)) with { Channel = EventChannel.Technical });

        async Task AssertViews()
        {
            Assert.Equal(new EventId("epistemic"),
                Assert.Single(await pa.GetOpenWaitingOnAsync(Token(), limit: 1)).SourceEvent);
            Assert.Equal(new EventId("technical-open"), Assert.Single(await pa.GetOpenWaitingOnAsync(
                Token() with { IncludeTechnical = true }, limit: 1)).SourceEvent);
        }
        await AssertViews();
        sp.GetRequiredService<ProjectorPipeline>().RebuildAll();
        await AssertViews();
    }

    // ── SDK: the profile's own schema module created its table ────────────

    [Fact]
    public void Schema_module_created_the_waiting_on_table_and_recorded_its_version()
    {
        var (sp, _, _) = BuildHost();
        using var _d = sp;
        var factory = sp.GetRequiredService<SqliteConnectionFactory>();
        using var c = factory.Open();

        using (var q = c.CreateCommand())
        {
            q.CommandText = "SELECT COUNT(*) FROM projection_waiting_on;";
            Assert.Equal(0L, (long)q.ExecuteScalar()!);
        }
        using (var v = c.CreateCommand())
        {
            v.CommandText = "SELECT value FROM schema_meta WHERE key = 'module:assistant-waiting-on';";
            Assert.Equal("1", v.ExecuteScalar() as string);
        }
    }

    // ── locked decision #11: secrets redacted inline at ingest ────────────

    [Fact]
    public async Task Secret_in_the_awaited_item_is_redacted_before_the_wal()
    {
        var (sp, agent, _) = BuildHost();
        using var _d = sp;

        await agent.IngestAsync(WaitingOn("sec1", "wait:x", "alice",
            "the deploy that uses password = supersecretvalue123"));

        var factory = sp.GetRequiredService<SqliteConnectionFactory>();
        using var c = factory.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT payload_json FROM memory_events WHERE event_id = 'sec1';";
        var raw = (string)cmd.ExecuteScalar()!;
        Assert.Contains("\"$type\":\"WaitingOnObservationPayload\"", raw);
        Assert.DoesNotContain("supersecretvalue123", raw);
    }

    // ── locked decision #8: capability-guarded read ───────────────────────

    [Fact]
    public async Task Expired_token_is_denied()
    {
        var (sp, _, pa) = BuildHost();
        using var _d = sp;
        var expired = new CapabilityToken(
            new PrincipalId("daemon"), new WorkstreamId(Ws),
            _clock.GetUtcNow().AddHours(-2), _clock.GetUtcNow().AddHours(-1),
            Array.Empty<EpistemicCategory>());
        await Assert.ThrowsAsync<CapabilityDeniedError>(() => pa.GetOpenWaitingOnAsync(expired));
    }

    [Fact]
    public async Task Cross_workstream_token_without_a_scope_is_denied()
    {
        var (sp, _, pa) = BuildHost();
        using var _d = sp;
        var crossWs = new CapabilityToken(
            new PrincipalId("daemon"), Workstream: null,
            _clock.GetUtcNow().AddHours(-1), _clock.GetUtcNow().AddHours(1),
            Array.Empty<EpistemicCategory>(), CrossWorkstream: true);
        await Assert.ThrowsAsync<CapabilityDeniedError>(() => pa.GetOpenWaitingOnAsync(crossWs));
    }

    [Fact]
    public async Task Token_that_disallows_the_hypothesis_category_is_denied()
    {
        // Waiting-on rides under Hypothesis; an Evidence-only token must not read it.
        var (sp, _, pa) = BuildHost();
        using var _d = sp;
        var evidenceOnly = new CapabilityToken(
            new PrincipalId("daemon"), new WorkstreamId(Ws),
            _clock.GetUtcNow().AddHours(-1), _clock.GetUtcNow().AddHours(1),
            AllowedCategories: new[] { EpistemicCategory.Evidence });
        await Assert.ThrowsAsync<CapabilityDeniedError>(() => pa.GetOpenWaitingOnAsync(evidenceOnly));
    }

    [Fact]
    public async Task Malformed_principals_are_explicitly_denied()
    {
        var (sp, _, pa) = BuildHost();
        using var _d = sp;
        string?[] principals = [null, "", " ", " daemon", "daemon ", "daemon\n", new string('a', 257)];
        foreach (var principal in principals)
            await Assert.ThrowsAsync<CapabilityDeniedError>(() =>
                pa.GetOpenWaitingOnAsync(Token() with { Principal = new PrincipalId(principal!) }));
    }

    [Fact]
    public async Task Malformed_category_grants_are_denied_without_changing_valid_hypothesis_grants()
    {
        var (sp, agent, pa) = BuildHost();
        using var _ = sp;
        await agent.IngestAsync(WaitingOn("category", "wait:budget", "alice", "budget sign-off"));
        IReadOnlyCollection<EpistemicCategory>?[] grants = [null, [EpistemicCategory.Hypothesis, (EpistemicCategory)99]];
        foreach (var grant in grants)
            await Assert.ThrowsAsync<CapabilityDeniedError>(() =>
                pa.GetOpenWaitingOnAsync(Token() with { AllowedCategories = grant! }));
        Assert.Single(await pa.GetOpenWaitingOnAsync(Token())); // Empty grants retain the all-categories meaning.
        Assert.Single(await pa.GetOpenWaitingOnAsync(Token() with { AllowedCategories = [EpistemicCategory.Hypothesis] }));
    }

    [Fact]
    public async Task Nonpositive_limits_are_rejected_and_positive_limits_remain_compatible()
    {
        var (sp, agent, pa) = BuildHost();
        using var _ = sp;
        await agent.IngestAsync(WaitingOn("due", "wait:budget", "alice", "budget sign-off",
            expectedBy: _clock.GetUtcNow()));
        await agent.IngestAsync(WaitingOn("undated", "wait:doc", "bob", "the draft"));
        foreach (var limit in new[] { 0, -1, int.MinValue })
        {
            var error = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                pa.GetOpenWaitingOnAsync(Token(), limit: limit));
            Assert.Equal("limit", error.ParamName);
        }
        Assert.Equal(new EventId("due"), Assert.Single(await pa.GetOpenWaitingOnAsync(Token(), limit: 1)).SourceEvent);
        Assert.Equal(2, (await pa.GetOpenWaitingOnAsync(Token(), limit: int.MaxValue)).Count);
    }

    [Fact]
    public async Task Null_tokens_and_cancelled_reads_fail_without_returning_candidates()
    {
        var (sp, agent, pa) = BuildHost();
        using var _ = sp;
        await agent.IngestAsync(WaitingOn("w1", "wait:budget", "alice", "budget sign-off"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => pa.GetOpenWaitingOnAsync(null!));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            pa.GetOpenWaitingOnAsync(Token(), ct: cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
    }

    // ── second-order: a hidden status update must not silently close ──────

    [Fact]
    public async Task A_private_fulfilled_update_by_another_principal_does_not_close_for_a_different_viewer()
    {
        var (sp, agent, pa) = BuildHost();
        using var _ = sp;

        await agent.IngestAsync(WaitingOn("w1", "wait:budget", "alice", "budget sign-off", principal: "daemon"));
        _clock.Advance(TimeSpan.FromMinutes(30));
        await agent.IngestAsync(WaitingOn("w2", "wait:budget", "alice", "budget sign-off",
            WaitingOnStatus.Fulfilled, principal: "ghost"));

        // Force the fulfilled update Private (owner-only) to "ghost".
        var factory = sp.GetRequiredService<SqliteConnectionFactory>();
        using (var conn = factory.Open())
        using (var vis = conn.CreateCommand())
        {
            vis.CommandText = """
                INSERT INTO memory_visibility(event_id, workstream_id, visibility, set_at)
                VALUES ('w2', $ws, 0, $at)
                ON CONFLICT(event_id) DO UPDATE SET visibility = 0;
                """;
            vis.Parameters.AddWithValue("$ws", Ws);
            vis.Parameters.AddWithValue("$at", _clock.GetUtcNow().ToString("O"));
            vis.ExecuteNonQuery();
        }

        // "daemon" can't see the private fulfilled update => still OPEN for daemon.
        var daemonView = await pa.GetOpenWaitingOnAsync(Token("daemon"));
        Assert.Equal("wait:budget", Assert.Single(daemonView).WaitingOnId);

        // "ghost" owns the fulfilled update => CLOSED for ghost.
        Assert.Empty(await pa.GetOpenWaitingOnAsync(Token("ghost")));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("mis-scoped")]
    [InlineData("private")]
    [InlineData("unknown-high")]
    [InlineData("unknown-negative")]
    public async Task Visibility_fails_closed_for_candidates_and_newer_lifecycle_events_before_and_after_rebuild(
        string visibilityState)
    {
        var (sp, agent, pa) = BuildHost();
        using var _ = sp;
        await agent.IngestAsync(WaitingOn("visible", "wait:budget", "alice", "budget sign-off"));
        _clock.Advance(TimeSpan.FromMinutes(30));
        await agent.IngestAsync(WaitingOn("hidden-close", "wait:budget", "alice", "budget sign-off",
            WaitingOnStatus.Fulfilled, principal: "ghost"));
        await agent.IngestAsync(WaitingOn("hidden-open", "wait:private", "bob", "private reply",
            principal: "ghost"));

        var factory = sp.GetRequiredService<SqliteConnectionFactory>();
        using (var c = factory.Open())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = visibilityState switch
            {
                "missing" => "DELETE FROM memory_visibility WHERE event_id IN ('hidden-close', 'hidden-open');",
                "mis-scoped" => """
                    UPDATE memory_visibility SET workstream_id = 'other-workstream'
                    WHERE event_id IN ('hidden-close', 'hidden-open');
                    """,
                _ => """
                    UPDATE memory_visibility SET visibility = $visibility
                    WHERE event_id IN ('hidden-close', 'hidden-open');
                    """
            };
            cmd.Parameters.AddWithValue("$visibility", visibilityState switch
            {
                "unknown-high" => 99,
                "unknown-negative" => -1,
                _ => 0
            });
            cmd.ExecuteNonQuery();
        }

        async Task AssertViews()
        {
            Assert.Equal(new EventId("visible"), Assert.Single(await pa.GetOpenWaitingOnAsync(Token())).SourceEvent);
            var unknown = visibilityState.StartsWith("unknown", StringComparison.Ordinal);
            Assert.Equal(new EventId(unknown ? "visible" : "hidden-open"),
                Assert.Single(await pa.GetOpenWaitingOnAsync(Token("ghost"))).SourceEvent);
        }
        await AssertViews();
        sp.GetRequiredService<ProjectorPipeline>().RebuildAll();
        await AssertViews();
    }

    // A controllable clock so append-only lifecycle ordering (reveal_time =
    // created_at, stamped by this clock at ingest) is deterministic.
    private sealed class MutableClock : TimeProvider
    {
        private DateTimeOffset _now;
        public MutableClock(DateTimeOffset start) => _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
