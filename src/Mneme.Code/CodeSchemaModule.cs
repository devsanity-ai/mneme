using Microsoft.Data.Sqlite;
using Mneme.Hosting.Profiles;

namespace Mneme.Code;

internal sealed class CodeSchemaModule : ISchemaModule
{
    public string Name => "code-observations";
    public int Version => 1;

    public void ApplyDdl(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS projection_code_observations (
                event_id TEXT NOT NULL PRIMARY KEY,
                workstream_id TEXT NOT NULL,
                repository_id TEXT NOT NULL,
                source_revision TEXT NOT NULL,
                kind INTEGER NOT NULL CHECK (kind IN (0, 1, 2)),
                relative_path TEXT,
                symbol TEXT,
                FOREIGN KEY (event_id) REFERENCES memory_events(event_id)
            ) WITHOUT ROWID;
            CREATE INDEX IF NOT EXISTS idx_code_observations_scope
                ON projection_code_observations
                    (workstream_id, repository_id, source_revision, kind, relative_path, symbol);
            """;
        cmd.ExecuteNonQuery();
    }
}
