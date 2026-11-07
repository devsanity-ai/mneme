using Microsoft.Data.Sqlite;
using Mneme.Contracts;
using Mneme.Projections;

namespace Mneme.Code;

internal sealed class CodeObservationProjector : IProjector
{
    public string Name => "code-observations";
    public EpistemicCategory Category => EpistemicCategory.Evidence;

    public void Apply(SqliteConnection c, SqliteTransaction tx, EventEnvelope e)
    {
        if (e.Payload is not CodeObservationPayload p) return;
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO projection_code_observations
                (event_id, workstream_id, repository_id, source_revision, kind, relative_path, symbol)
            VALUES ($id, $ws, $repo, $rev, $kind, $path, $symbol)
            ON CONFLICT(event_id) DO NOTHING;
            """;
        cmd.Parameters.AddWithValue("$id", e.EventId.Value);
        cmd.Parameters.AddWithValue("$ws", e.WorkstreamId.Value);
        cmd.Parameters.AddWithValue("$repo", p.RepositoryId.ToString("D"));
        cmd.Parameters.AddWithValue("$rev", p.SourceRevision);
        cmd.Parameters.AddWithValue("$kind", (int)p.Kind);
        cmd.Parameters.AddWithValue("$path", (object?)p.RelativePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$symbol", (object?)p.Symbol ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public int Rebuild(SqliteConnection c, SqliteTransaction tx)
    {
        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM projection_code_observations;";
            cmd.ExecuteNonQuery();
        }
        var count = 0;
        foreach (var e in EventEnvelopeReader.ReadAll(c, tx, Category))
        {
            if (e.Payload is not CodeObservationPayload) continue;
            Apply(c, tx, e);
            count++;
        }
        return count;
    }
}
