using System.Globalization;
using Microsoft.Data.Sqlite;
using Mneme.Contracts;
using Mneme.Projections;

namespace Mneme.Assistant;

/// <summary>
/// Maintains the waiting-on ledger (<c>projection_waiting_on</c>) from
/// <see cref="WaitingOnObservationPayload"/> events (ADR-0006). One row per
/// event; the open-items query (<see cref="IAssistantMemory.GetOpenWaitingOnAsync"/>)
/// is "the latest event per <c>waiting_on_id</c> whose status is still Waiting".
/// </summary>
/// <remarks>
/// Matches <see cref="EpistemicCategory.Hypothesis"/> (the category waiting-on
/// observations ride under) and self-guards on payload type, so it ignores real
/// <see cref="HypothesisPayload"/> events — those stay with the built-in
/// hypotheses projector. It owns its table, so <see cref="Rebuild"/> wipes and
/// re-derives it. Idempotent per event via <c>ON CONFLICT(workstream_id, event_id)</c>.
/// </remarks>
public sealed class WaitingOnProjector : IProjector
{
    /// <inheritdoc/>
    public string Name => "assistant-waiting-on";

    /// <inheritdoc/>
    public EpistemicCategory Category => EpistemicCategory.Hypothesis;

    /// <inheritdoc/>
    public void Apply(SqliteConnection c, SqliteTransaction tx, EventEnvelope e)
    {
        if (e.Payload is not WaitingOnObservationPayload p) return;

        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO projection_waiting_on(workstream_id, event_id, waiting_on_id,
                counterparty, counterparty_key, awaited_item, since, expected_by,
                status, confidence, story_time, reveal_time, revoked_at)
            VALUES ($ws, $eid, $wid, $cp, $cpk, $item, $since, $exp,
                $status, $conf, $story, $reveal, $rev)
            ON CONFLICT(workstream_id, event_id) DO UPDATE SET
                waiting_on_id    = excluded.waiting_on_id,
                counterparty     = excluded.counterparty,
                counterparty_key = excluded.counterparty_key,
                awaited_item     = excluded.awaited_item,
                since            = excluded.since,
                expected_by      = excluded.expected_by,
                status           = excluded.status,
                confidence       = excluded.confidence,
                story_time       = excluded.story_time,
                reveal_time      = excluded.reveal_time,
                revoked_at       = excluded.revoked_at;
            """;
        cmd.Parameters.AddWithValue("$ws", e.WorkstreamId.Value);
        cmd.Parameters.AddWithValue("$eid", e.EventId.Value);
        cmd.Parameters.AddWithValue("$wid", p.WaitingOnId);
        cmd.Parameters.AddWithValue("$cp", p.Counterparty);
        cmd.Parameters.AddWithValue("$cpk", NormalizeKey(p.Counterparty));
        cmd.Parameters.AddWithValue("$item", p.AwaitedItem);
        cmd.Parameters.AddWithValue("$since", Fmt(p.Since));
        cmd.Parameters.AddWithValue("$exp", p.ExpectedBy.HasValue ? Fmt(p.ExpectedBy.Value) : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$status", (int)p.Status);
        cmd.Parameters.AddWithValue("$conf", p.Confidence);
        cmd.Parameters.AddWithValue("$story", Fmt(e.ValidAt));
        cmd.Parameters.AddWithValue("$reveal", Fmt(e.CreatedAt));
        cmd.Parameters.AddWithValue("$rev", e.RevokedAt.HasValue ? Fmt(e.RevokedAt.Value) : (object)DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc/>
    public int Rebuild(SqliteConnection c, SqliteTransaction tx)
    {
        using (var del = c.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM projection_waiting_on;";
            del.ExecuteNonQuery();
        }
        var n = 0;
        foreach (var e in EventEnvelopeReader.ReadAll(c, tx, Category))
        {
            if (e.Payload is not WaitingOnObservationPayload) continue;
            Apply(c, tx, e);
            n++;
        }
        return n;
    }

    /// <summary>Lowercase + whitespace-collapse a counterparty handle into a stable grouping key.</summary>
    internal static string NormalizeKey(string? counterparty)
    {
        if (string.IsNullOrWhiteSpace(counterparty)) return string.Empty;
        var s = counterparty.Trim().ToLowerInvariant();
        return string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string Fmt(DateTimeOffset t) =>
        t.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
}
