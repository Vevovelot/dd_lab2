using Microsoft.Data.Sqlite;

namespace iCode;

public record ContextMessage(
    string Role,
    string Content,
    DateTime Timestamp,
    string? ToolCalls = null,
    string? ToolCallId = null,
    string? Name = null
);

public class ContextStore : IDisposable
{
    private readonly SqliteConnection _connection;

    public ContextStore(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();
        EnsureSchema();
    }

    private void EnsureSchema()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS context (
                timestamp   TEXT NOT NULL,
                role        TEXT NOT NULL,
                content     TEXT NOT NULL
            )
            """;
        cmd.ExecuteNonQuery();

        AddColumnIfMissing("tool_calls",   "TEXT");
        AddColumnIfMissing("tool_call_id", "TEXT");
        AddColumnIfMissing("name",         "TEXT");
    }

    private void AddColumnIfMissing(string column, string type)
    {
        using var check = _connection.CreateCommand();
        check.CommandText = "PRAGMA table_info(context)";
        using var reader = check.ExecuteReader();
        while (reader.Read())
        {
            if (reader.GetString(1) == column) return;
        }
        using var alter = _connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE context ADD COLUMN {column} {type}";
        alter.ExecuteNonQuery();
    }

    public void Append(string role, string content)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "INSERT INTO context (timestamp, role, content) VALUES ($ts, $role, $content)";
        cmd.Parameters.AddWithValue("$ts", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$role", role);
        cmd.Parameters.AddWithValue("$content", content);
        cmd.ExecuteNonQuery();
    }

    public void AppendAssistantToolCalls(string toolCallsJson)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO context (timestamp, role, content, tool_calls)
            VALUES ($ts, 'assistant', '', $toolCalls)
            """;
        cmd.Parameters.AddWithValue("$ts", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$toolCalls", toolCallsJson);
        cmd.ExecuteNonQuery();
    }

    public void AppendToolResult(string toolCallId, string toolName, string content)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO context (timestamp, role, content, tool_call_id, name)
            VALUES ($ts, 'tool', $content, $toolCallId, $name)
            """;
        cmd.Parameters.AddWithValue("$ts", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$content", content);
        cmd.Parameters.AddWithValue("$toolCallId", toolCallId);
        cmd.Parameters.AddWithValue("$name", toolName);
        cmd.ExecuteNonQuery();
    }

    public List<ContextMessage> LoadAll()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT timestamp, role, content, tool_calls, tool_call_id, name FROM context ORDER BY rowid";
        using var reader = cmd.ExecuteReader();
        var messages = new List<ContextMessage>();
        while (reader.Read())
        {
            messages.Add(new ContextMessage(
                Role:       reader.GetString(1),
                Content:    reader.IsDBNull(2) ? "" : reader.GetString(2),
                Timestamp:  DateTime.Parse(reader.GetString(0)),
                ToolCalls:  reader.IsDBNull(3) ? null : reader.GetString(3),
                ToolCallId: reader.IsDBNull(4) ? null : reader.GetString(4),
                Name:       reader.IsDBNull(5) ? null : reader.GetString(5)
            ));
        }
        return messages;
    }

    public void Dispose() => _connection.Dispose();
}
