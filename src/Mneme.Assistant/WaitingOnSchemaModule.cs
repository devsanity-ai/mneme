using Microsoft.Data.Sqlite;
using Mneme.Hosting.Profiles;

namespace Mneme.Assistant;

/// <summary>
/// Schema for the personal-assistant profile's own projection table,
/// <c>projection_waiting_on</c> — the waiting-on ledger (ADR-0006). The profile
/// owns this table (base Mneme has no dyadic external-dependency projection), so
/// it contributes this <see cref="ISchemaModule"/>.
/// </summary>
/// <remarks>
/// Idempotent (<c>CREATE TABLE/INDEX IF NOT EXISTS</c>) and namespaced under
/// <see cref="Name"/> in <c>schema_meta</c>. One row per waiting-on <em>event</em>
/// (PK <c>event_id</c>); a single <c>waiting_on_id</c> may have several rows as
/// its status advances, so "open" = the latest event for a <c>waiting_on_id</c>
/// whose status is still <c>Waiting</c>. <c>expected_by</c> (the overdue driver)
/// and <c>since</c> are stored as explicit domain timestamps, distinct from the
/// event's bi-temporal <c>story_time</c>/<c>reveal_time</c> (ADR-0006 temporal
/// contract).
/// </remarks>
public sealed class WaitingOnSchemaModule : ISchemaModule
{
    /// <inheritdoc/>
    public string Name => "assistant-waiting-on";

    /// <inheritdoc/>
    public int Version => 1;

    /// <inheritdoc/>
    public void ApplyDdl(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS projection_waiting_on (
                workstream_id    TEXT NOT NULL,
                event_id         TEXT NOT NULL,
                waiting_on_id    TEXT NOT NULL,
                counterparty     TEXT NOT NULL,
                counterparty_key TEXT NOT NULL,
                awaited_item     TEXT NOT NULL,
                since            TEXT NOT NULL,     -- when the waiting started (domain time)
                expected_by      TEXT,             -- due date; NULL when unknown (overdue driver)
                status           INTEGER NOT NULL,  -- 0 = waiting (open), 1 = fulfilled, 2 = released
                confidence       REAL NOT NULL,     -- extraction confidence [0,1] (candidate-first)
                story_time       TEXT NOT NULL,     -- event valid_at
                reveal_time      TEXT NOT NULL,     -- event created_at (lifecycle ordering)
                revoked_at       TEXT,
                PRIMARY KEY (workstream_id, event_id),
                FOREIGN KEY (event_id) REFERENCES memory_events(event_id)
            ) WITHOUT ROWID;

            CREATE INDEX IF NOT EXISTS idx_waiting_on_lifecycle
                ON projection_waiting_on(workstream_id, waiting_on_id, reveal_time);
            CREATE INDEX IF NOT EXISTS idx_waiting_on_counterparty
                ON projection_waiting_on(workstream_id, counterparty_key, status);
            """;
        cmd.ExecuteNonQuery();
    }
}
