using System.Globalization;
using Mneme.Contracts;
using Mneme.Storage;

namespace Mneme.Assistant;

/// <summary>
/// SQLite-backed <see cref="IAssistantMemory"/>. Reads the profile's own
/// <c>projection_waiting_on</c> ledger that <see cref="WaitingOnProjector"/>
/// feeds, scoped and visibility-filtered by the caller's capability token.
/// </summary>
public sealed class AssistantMemory : IAssistantMemory
{
    private readonly SqliteConnectionFactory _connections;
    private readonly TimeProvider _clock;

    /// <summary>Construct over the Mneme connection factory and clock.</summary>
    public AssistantMemory(SqliteConnectionFactory connections, TimeProvider clock)
    {
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<WaitingOnItem>> GetOpenWaitingOnAsync(
        CapabilityToken token, DateTimeOffset? asOf = null, int limit = 100, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (!token.IsValidAt(_clock.GetUtcNow()))
        {
            throw new CapabilityDeniedError(
                $"token validity window [{token.NotBefore:O}..{token.NotAfter:O}] excludes now");
        }
        if (string.IsNullOrWhiteSpace(token.Principal.Value) || token.Principal.Value.Length > 256 ||
            token.Principal.Value != token.Principal.Value.Trim() || token.Principal.Value.Any(char.IsControl))
        {
            throw new CapabilityDeniedError("a valid authenticated principal is required");
        }
        // Workstream-scoped read; a null-workstream cross token is not meaningful
        // (a user's agenda is one workstream).
        if (token.Workstream is not { } workstream)
        {
            throw new CapabilityDeniedError("open waiting-on requires a workstream-scoped capability token");
        }
        // Waiting-on observations ride under Hypothesis; the token must allow it.
        if (token.AllowedCategories is null || token.AllowedCategories.Any(category => !Enum.IsDefined(category)) ||
            !token.Allows(EpistemicCategory.Hypothesis))
        {
            throw new CapabilityDeniedError("token must contain valid category grants allowing Hypothesis");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        ct.ThrowIfCancellationRequested();
        var nowInstant = asOf ?? _clock.GetUtcNow();

        var results = new List<WaitingOnItem>();
        using var c = _connections.Open();
        using var cmd = c.CreateCommand();
        // Open = the LATEST visible lifecycle event per waiting_on_id whose status
        // is still Waiting (0). "Latest" = greatest reveal_time, tie-broken by
        // event_id. Both the candidate row and the NOT-EXISTS "newer event" probe
        // use the same live revocation, visibility, review, and channel gates,
        // so an unreadable status update never silently closes (or re-opens)
        // an item. Review mode requires positive approval even in the core's
        // post-WAL, pre-queue interval exposed by a concurrent rebuild.
        // Ordered due-first (items with a deadline), soonest deadline first.
        cmd.CommandText = """
            SELECT w.waiting_on_id, w.counterparty, w.awaited_item, w.since,
                   w.expected_by, w.confidence, w.event_id
            FROM projection_waiting_on w
            JOIN memory_events ew ON ew.event_id = w.event_id AND ew.workstream_id = w.workstream_id
            LEFT JOIN memory_visibility vw ON vw.event_id = w.event_id AND vw.workstream_id = w.workstream_id
            LEFT JOIN review_queue qw ON qw.event_id = w.event_id
            LEFT JOIN workstream_config config ON config.workstream_id = w.workstream_id
            WHERE w.workstream_id = $ws
              AND w.status = 0
              AND (ew.event_channel = $epistemic OR ($technical = 1 AND ew.event_channel = $technicalChannel))
              AND w.revoked_at IS NULL
              AND NOT EXISTS (SELECT 1 FROM memory_revocations r WHERE r.event_id = w.event_id)
              AND (vw.visibility IN (1, 2) OR (COALESCE(vw.visibility, 0) = 0 AND ew.principal_id = $viewer))
              AND (qw.event_id IS NULL OR qw.status = 'approved')
              AND (ew.event_channel = $technicalChannel OR COALESCE(config.mode, $autoDistill) = $autoDistill
                   OR qw.status = 'approved')
              AND NOT EXISTS (
                  SELECT 1
                  FROM projection_waiting_on w2
                  JOIN memory_events ew2 ON ew2.event_id = w2.event_id AND ew2.workstream_id = w2.workstream_id
                  LEFT JOIN memory_visibility vw2 ON vw2.event_id = w2.event_id AND vw2.workstream_id = w2.workstream_id
                  LEFT JOIN review_queue qw2 ON qw2.event_id = w2.event_id
                  LEFT JOIN workstream_config config2 ON config2.workstream_id = w2.workstream_id
                  WHERE w2.workstream_id = w.workstream_id
                    AND w2.waiting_on_id = w.waiting_on_id
                    AND (ew2.event_channel = $epistemic OR ($technical = 1 AND ew2.event_channel = $technicalChannel))
                    AND w2.revoked_at IS NULL
                    AND NOT EXISTS (SELECT 1 FROM memory_revocations r2 WHERE r2.event_id = w2.event_id)
                    AND (vw2.visibility IN (1, 2) OR (COALESCE(vw2.visibility, 0) = 0 AND ew2.principal_id = $viewer))
                    AND (qw2.event_id IS NULL OR qw2.status = 'approved')
                    AND (ew2.event_channel = $technicalChannel OR COALESCE(config2.mode, $autoDistill) = $autoDistill
                         OR qw2.status = 'approved')
                    AND (w2.reveal_time > w.reveal_time
                         OR (w2.reveal_time = w.reveal_time AND w2.event_id > w.event_id))
              )
            ORDER BY (w.expected_by IS NULL), w.expected_by ASC, w.since ASC
            LIMIT $n;
            """;
        cmd.Parameters.AddWithValue("$ws", workstream.Value);
        cmd.Parameters.AddWithValue("$viewer", token.Principal.Value);
        cmd.Parameters.AddWithValue("$epistemic", (int)EventChannel.Epistemic);
        cmd.Parameters.AddWithValue("$technical", token.IncludeTechnical ? 1 : 0);
        cmd.Parameters.AddWithValue("$technicalChannel", (int)EventChannel.Technical);
        cmd.Parameters.AddWithValue("$autoDistill", (int)WorkstreamMode.AutoDistill);
        cmd.Parameters.AddWithValue("$n", limit);
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var expectedBy = rd.IsDBNull(4) ? (DateTimeOffset?)null : ParseTs(rd.GetString(4));
            results.Add(new WaitingOnItem(
                WaitingOnId: rd.GetString(0),
                Counterparty: rd.GetString(1),
                AwaitedItem: rd.GetString(2),
                Since: ParseTs(rd.GetString(3)),
                ExpectedBy: expectedBy,
                IsOverdue: expectedBy.HasValue && expectedBy.Value < nowInstant,
                Confidence: rd.GetDouble(5),
                SourceEvent: new EventId(rd.GetString(6))));
        }
        return Task.FromResult<IReadOnlyList<WaitingOnItem>>(results);
    }

    private static DateTimeOffset ParseTs(string s) =>
        DateTimeOffset.Parse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
