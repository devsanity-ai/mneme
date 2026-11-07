using Mneme.Contracts;
using ClassificationLabel = Mneme.Contracts.Classification;

namespace Mneme.Code.Tests;

public sealed class CodeCapabilityTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("../code")]
    [InlineData("Bad")]
    [InlineData("bad--scope")]
    [InlineData("code-tests\n")]
    public void Query_rejects_invalid_workstream(string? workstream)
    {
        Assert.Throws<ArgumentException>(() => CodeTestHost.Query(workstream: workstream!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(201)]
    public void Query_rejects_unbounded_or_empty_limits(int limit)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CodeTestHost.Query(limit: limit));
    }

    [Fact]
    public void Query_is_immutable_and_its_default_scope_and_limit_are_explicit()
    {
        var query = CodeTestHost.Query();
        Assert.Equal(new WorkstreamId(CodeTestHost.Workstream), query.Workstream);
        Assert.Equal(CodeTestHost.Repository, query.RepositoryId);
        Assert.Equal(CodeTestHost.Revision, query.CurrentRevision);
        Assert.Equal(50, query.Limit);
        Assert.Null(query.Kind);
        Assert.Null(query.RelativePath);
        Assert.Null(query.Symbol);
        Assert.All(typeof(CodeMemoryQuery).GetProperties(), property => Assert.Null(property.SetMethod));
    }

    [Fact]
    public async Task Null_arguments_and_pre_cancelled_reads_fail_before_returning_data()
    {
        using var host = new CodeTestHost();
        await Assert.ThrowsAsync<ArgumentNullException>(() => host.Memory.FindAsync(null!, host.Token()));
        await Assert.ThrowsAsync<ArgumentNullException>(() => host.Memory.FindAsync(CodeTestHost.Query(), null!));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            host.Memory.FindAsync(CodeTestHost.Query(), host.Token(), cancellation.Token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" owner-a")]
    [InlineData("owner-a ")]
    [InlineData("owner-a\n")]
    public async Task Token_requires_a_valid_host_authenticated_principal(string? principal)
    {
        using var host = new CodeTestHost();
        await Assert.ThrowsAsync<CapabilityDeniedError>(() => host.Memory.FindAsync(
            CodeTestHost.Query(), host.Token(principal: principal!)));
    }

    [Fact]
    public async Task Oversized_principal_invalid_scope_and_cross_workstream_tokens_are_denied()
    {
        using var host = new CodeTestHost();
        var token = host.Token();
        CapabilityToken[] denied =
        [
            token with { Principal = new PrincipalId(new string('a', 257)) },
            token with { Workstream = new WorkstreamId("other-code") },
            token with { Workstream = new WorkstreamId("code-tests\n") },
            token with { Workstream = default(WorkstreamId) },
            token with { Workstream = null },
            token with { Workstream = null, CrossWorkstream = true },
        ];
        foreach (var invalid in denied)
            await Assert.ThrowsAsync<CapabilityDeniedError>(() =>
                host.Memory.FindAsync(CodeTestHost.Query(), invalid));
    }

    [Fact]
    public async Task Token_validity_window_is_checked_at_the_injected_current_clock()
    {
        using var host = new CodeTestHost();
        await host.Agent.IngestAsync(host.Event("window"));
        var token = host.Token();
        CapabilityToken[] denied =
        [
            token with { NotAfter = host.Clock.Now.AddTicks(-1) },
            token with { NotBefore = host.Clock.Now.AddTicks(1) },
            token with { NotBefore = host.Clock.Now.AddHours(1), NotAfter = host.Clock.Now.AddHours(-1) },
        ];
        foreach (var invalid in denied)
            await Assert.ThrowsAsync<CapabilityDeniedError>(() => host.Memory.FindAsync(CodeTestHost.Query(), invalid));
        Assert.Single(await host.Memory.FindAsync(CodeTestHost.Query(), token with
        {
            NotBefore = host.Clock.Now,
            NotAfter = host.Clock.Now,
        }));
    }

    [Fact]
    public async Task Evidence_category_is_required_and_malformed_category_grants_fail_closed()
    {
        using var host = new CodeTestHost();
        await host.Agent.IngestAsync(host.Event("category"));
        var token = host.Token();
        CapabilityToken[] denied =
        [
            token with { AllowedCategories = [EpistemicCategory.Fact] },
            token with { AllowedCategories = null! },
            token with { AllowedCategories = [EpistemicCategory.Evidence, (EpistemicCategory)99] },
        ];
        foreach (var invalid in denied)
            await Assert.ThrowsAsync<CapabilityDeniedError>(() => host.Memory.FindAsync(CodeTestHost.Query(), invalid));
        Assert.Single(await host.Memory.FindAsync(CodeTestHost.Query(), token));
        Assert.Single(await host.Memory.FindAsync(CodeTestHost.Query(), token with { AllowedCategories = [] }));
    }

    [Fact]
    public async Task Technical_channel_requires_permission_and_is_filtered_before_limit()
    {
        using var host = new CodeTestHost();
        await host.Agent.IngestAsync(host.Event("epistemic"));
        host.Clock.Now = host.Clock.Now.AddMinutes(1);
        await host.Agent.IngestAsync(host.Event("technical", channel: EventChannel.Technical));

        Assert.Equal("epistemic", Assert.Single(await host.Memory.FindAsync(
            CodeTestHost.Query(limit: 1), host.Token())).EventId.Value);
        Assert.Equal("technical", Assert.Single(await host.Memory.FindAsync(
            CodeTestHost.Query(limit: 1), host.Token() with { IncludeTechnical = true })).EventId.Value);
        host.Pipeline.RebuildAll();
        Assert.Equal("epistemic", Assert.Single(await host.Memory.FindAsync(
            CodeTestHost.Query(limit: 1), host.Token())).EventId.Value);
    }

    [Theory]
    [InlineData(ClassificationLabel.Confidential)]
    [InlineData(ClassificationLabel.Secret)]
    [InlineData(ClassificationLabel.Pii)]
    public async Task Private_observations_belong_to_the_host_principal_not_source_or_speaker(
        ClassificationLabel classification)
    {
        using var host = new CodeTestHost(classifier: new FixedClassifier(classification));
        await host.Agent.IngestAsync(host.Event("private",
            CodeTestHost.Payload(content: "Speaker-b suggests a different repository constraint."),
            principal: "owner-a", source: "model-b"));

        var item = Assert.Single(await host.Memory.FindAsync(CodeTestHost.Query(), host.Token()));
        Assert.Equal(new PrincipalId("owner-a"), item.Principal);
        Assert.Equal(new CaptureSourceId("model-b"), item.Source);
        foreach (var principal in new[] { "speaker-b", "model-b", "owner-b", "Owner-a" })
            Assert.Empty(await host.Memory.FindAsync(CodeTestHost.Query(), host.Token(principal)));
    }

    [Theory]
    [InlineData(Visibility.Shared)]
    [InlineData(Visibility.Global)]
    public async Task Sharing_does_not_remove_the_workstream_and_repository_boundary(Visibility visibility)
    {
        using var host = new CodeTestHost();
        await host.Agent.IngestAsync(host.Event("shared"));
        host.Execute("UPDATE memory_visibility SET visibility = $visibility WHERE event_id = 'shared';",
            ("$visibility", (int)visibility));
        Assert.Single(await host.Memory.FindAsync(CodeTestHost.Query(), host.Token("owner-b")));
        Assert.Empty(await host.Memory.FindAsync(
            CodeTestHost.Query(repository: CodeTestHost.OtherRepository), host.Token("owner-b")));
        Assert.Empty(await host.Memory.FindAsync(
            CodeTestHost.Query(workstream: "other-code"), host.Token("owner-b", "other-code")));
    }

    [Fact]
    public async Task Missing_or_mis_scoped_visibility_is_owner_only_and_unknown_tiers_fail_closed()
    {
        using var host = new CodeTestHost();
        await host.Agent.IngestAsync(host.Event("missing"));
        await host.Agent.IngestAsync(host.Event("mis-scoped"));
        await host.Agent.IngestAsync(host.Event("unknown"));
        host.Execute("""
            DELETE FROM memory_visibility WHERE event_id = 'missing';
            UPDATE memory_visibility SET workstream_id = 'other-code' WHERE event_id = 'mis-scoped';
            UPDATE memory_visibility SET visibility = 99 WHERE event_id = 'unknown';
            """);

        Assert.Equal(["mis-scoped", "missing"], (await host.Memory.FindAsync(CodeTestHost.Query(), host.Token()))
            .Select(item => item.EventId.Value).Order(StringComparer.Ordinal).ToArray());
        Assert.Empty(await host.Memory.FindAsync(CodeTestHost.Query(), host.Token("owner-b")));
        host.Pipeline.RebuildAll();
        Assert.Empty(await host.Memory.FindAsync(CodeTestHost.Query(), host.Token("owner-b")));
    }
}
