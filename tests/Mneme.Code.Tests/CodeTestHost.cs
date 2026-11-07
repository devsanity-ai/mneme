using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Mneme.Classification;
using Mneme.Contracts;
using Mneme.Hosting;
using Mneme.Ingest.Redaction;
using Mneme.Projections;
using Mneme.Storage;
using Mneme.Writer;
using ClassificationLabel = Mneme.Contracts.Classification;

namespace Mneme.Code.Tests;

internal sealed class CodeTestHost : IDisposable
{
    internal const string Workstream = "code-tests";
    internal const string Revision = "1111111111111111111111111111111111111111";
    internal const string OtherRevision = "2222222222222222222222222222222222222222";
    internal static readonly Guid Repository = new("11111111-1111-4111-8111-111111111111");
    internal static readonly Guid OtherRepository = new("22222222-2222-4222-8222-222222222222");

    private readonly string _directory;

    internal CodeTestHost(IRedactor? redactor = null, IClassifier? classifier = null, bool writer = false)
    {
        _directory = Path.Combine(AppContext.BaseDirectory, "code-fixtures", Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(Clock);
        if (redactor is not null) services.AddSingleton(redactor);
        if (classifier is not null) services.AddSingleton(classifier);
        services.AddMneme(options =>
        {
            options.WorkstreamId = Workstream;
            options.SqlitePath = Path.Combine(_directory, "test.mneme.db");
            options.UserId = "owner-a";
        }).AddMnemeCodeProfile().AddMnemeCodeProfile();
        if (writer) services.AddMnemeWriterProfile();
        Services = services.BuildServiceProvider();
    }

    internal TestClock Clock { get; } = new();
    internal ServiceProvider Services { get; }
    internal IMemoryAgent Agent => Services.GetRequiredService<IMemoryAgent>();
    internal ICodeMemory Memory => Services.GetRequiredService<ICodeMemory>();
    internal SqliteConnectionFactory Connections => Services.GetRequiredService<SqliteConnectionFactory>();
    internal ProjectorPipeline Pipeline => Services.GetRequiredService<ProjectorPipeline>();

    internal CapabilityToken Token(string principal = "owner-a", string workstream = Workstream) =>
        new(new PrincipalId(principal), new WorkstreamId(workstream),
            Clock.Now.AddHours(-1), Clock.Now.AddHours(1), [EpistemicCategory.Evidence]);

    internal CaptureEvent Event(
        string id, EventPayload? payload = null, string workstream = Workstream,
        string principal = "owner-a", EventChannel channel = EventChannel.Epistemic,
        DateTimeOffset? validAt = null, DateTimeOffset? recordedAt = null, string source = "code-host") =>
        new(new EventId(id), new WorkstreamId(workstream), channel,
            validAt ?? Clock.Now, recordedAt ?? Clock.Now, payload ?? Payload(),
            new CaptureProvenance(new CaptureSourceId(source), new PrincipalId(principal)));

    internal static CodeObservationPayload Payload(
        string revision = Revision, Guid? repository = null,
        CodeObservationKind kind = CodeObservationKind.Convention,
        string content = "Use cancellation tokens for repository reads.",
        string? path = null, string? symbol = null, string? evidence = null) =>
        new(repository ?? Repository, revision, kind, content, path, symbol, evidence);

    internal static CodeMemoryQuery Query(
        string revision = Revision, Guid? repository = null, string? path = null,
        string? symbol = null, CodeObservationKind? kind = null, int limit = 50,
        string workstream = Workstream) =>
        new(new WorkstreamId(workstream), repository ?? Repository, revision, kind, path, symbol, limit);

    internal object? Scalar(string sql, params (string Name, object Value)[] parameters)
    {
        using var connection = Connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return command.ExecuteScalar();
    }

    internal long Count(string table) =>
        Convert.ToInt64(Scalar($"SELECT COUNT(*) FROM {table};"), CultureInfo.InvariantCulture);

    internal void Execute(string sql, params (string Name, object Value)[] parameters)
    {
        using var connection = Connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        var connections = Connections;
        Services.Dispose();
        using (var connection = connections.Open()) SqliteConnection.ClearPool(connection);
        Directory.Delete(_directory, recursive: true);
    }
}

internal sealed class TestClock : TimeProvider
{
    internal DateTimeOffset Now { get; set; } = new(2026, 9, 17, 15, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class FixedClassifier(ClassificationLabel classification) : IClassifier
{
    public Task<ClassificationLabel> ClassifyAsync(
        string content, bool hadRedactionHits, EpistemicCategory category, CancellationToken ct = default) =>
        Task.FromResult(classification);
}

internal sealed class TestRedactor(Func<string, RedactionResult> redact) : IRedactor
{
    internal List<string> Inputs { get; } = [];

    public RedactionResult Redact(string content)
    {
        Inputs.Add(content);
        return redact(content);
    }
}
