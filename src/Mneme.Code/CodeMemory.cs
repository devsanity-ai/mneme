using System.Globalization;
using Mneme.Contracts;
using Mneme.Storage;

namespace Mneme.Code;

internal sealed class CodeMemory(SqliteConnectionFactory connections, TimeProvider clock) : ICodeMemory
{
    public async Task<IReadOnlyList<CodeObservation>> FindAsync(
        CodeMemoryQuery query, CapabilityToken capability, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(capability);
        ct.ThrowIfCancellationRequested();
        var now = clock.GetUtcNow();
        ValidateCapability(query, capability, now);
        using var connection = connections.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandTimeout = 5;
        // Core queues review after its WAL commit. Positive approval in review
        // mode also covers an unqueued event exposed by a concurrent rebuild.
        cmd.CommandText = """
            SELECT e.event_id, e.payload_json, e.principal_id, e.provenance_json,
                   e.valid_at, e.created_at
            FROM projection_code_observations p
            JOIN memory_events e ON e.event_id = p.event_id AND e.workstream_id = p.workstream_id
            LEFT JOIN memory_visibility v ON v.event_id = e.event_id AND v.workstream_id = e.workstream_id
            LEFT JOIN memory_revocations r ON r.event_id = e.event_id
            LEFT JOIN review_queue q ON q.event_id = e.event_id
            LEFT JOIN workstream_config config ON config.workstream_id = e.workstream_id
            WHERE p.workstream_id = $ws AND p.repository_id = $repo AND p.source_revision = $rev
              AND ($kind IS NULL OR p.kind = $kind)
              AND (p.relative_path IS NULL OR p.relative_path = $path)
              AND (p.symbol IS NULL OR p.symbol = $symbol)
              AND e.category = $category
              AND (e.event_channel = $epistemic OR ($technical = 1 AND e.event_channel = $technicalChannel))
              AND (v.visibility IN (1, 2) OR (COALESCE(v.visibility, 0) = 0 AND e.principal_id = $viewer))
              AND r.event_id IS NULL
              AND (q.event_id IS NULL OR q.status = 'approved')
              AND (e.event_channel = $technicalChannel OR COALESCE(config.mode, $autoDistill) = $autoDistill
                   OR q.status = 'approved')
              AND e.valid_at <= $now AND (e.invalid_at IS NULL OR e.invalid_at > $now)
              AND e.created_at <= $now AND (e.expired_at IS NULL OR e.expired_at > $now)
            ORDER BY e.created_at DESC, e.event_id ASC
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$ws", query.Workstream.Value);
        cmd.Parameters.AddWithValue("$repo", query.RepositoryId.ToString("D"));
        cmd.Parameters.AddWithValue("$rev", query.CurrentRevision);
        cmd.Parameters.AddWithValue("$kind", query.Kind is { } kind ? (int)kind : DBNull.Value);
        cmd.Parameters.AddWithValue("$path", (object?)query.RelativePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$symbol", (object?)query.Symbol ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$category", (int)EpistemicCategory.Evidence);
        cmd.Parameters.AddWithValue("$epistemic", (int)EventChannel.Epistemic);
        cmd.Parameters.AddWithValue("$technicalChannel", (int)EventChannel.Technical);
        cmd.Parameters.AddWithValue("$technical", capability.IncludeTechnical ? 1 : 0);
        cmd.Parameters.AddWithValue("$autoDistill", (int)WorkstreamMode.AutoDistill);
        cmd.Parameters.AddWithValue("$viewer", capability.Principal.Value);
        cmd.Parameters.AddWithValue("$now", now.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$limit", query.Limit);
        var results = new List<CodeObservation>();
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            var payload = (CodeObservationPayload)EventSerialization.DeserializePayload(reader.GetString(1));
            var provenance = EventSerialization.DeserializeProvenance(reader.GetString(3));
            results.Add(new CodeObservation(new EventId(reader.GetString(0)), query.Workstream, payload,
                new PrincipalId(reader.GetString(2)), provenance.Source, Parse(reader.GetString(4)),
                Parse(reader.GetString(5))));
        }
        ct.ThrowIfCancellationRequested();
        return results.AsReadOnly();
    }

    private static void ValidateCapability(CodeMemoryQuery query, CapabilityToken token, DateTimeOffset now)
    {
        if (!token.IsValidAt(now))
            throw new CapabilityDeniedError("Capability validity window excludes now.");
        if (string.IsNullOrWhiteSpace(token.Principal.Value) || token.Principal.Value.Length > 256 ||
            token.Principal.Value != token.Principal.Value.Trim() || token.Principal.Value.Any(char.IsControl))
            throw new CapabilityDeniedError("A valid authenticated principal is required.");
        if (token.Workstream is not { } scope || !CodeInput.IsWorkstream(scope.Value) ||
            scope != query.Workstream)
            throw new CapabilityDeniedError("Code memory requires a matching workstream-scoped capability.");
        if (token.AllowedCategories is null || token.AllowedCategories.Any(c => !Enum.IsDefined(c)) ||
            !token.Allows(EpistemicCategory.Evidence))
            throw new CapabilityDeniedError("Capability must allow Evidence.");
    }

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
