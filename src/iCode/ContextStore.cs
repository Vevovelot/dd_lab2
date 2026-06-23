using Microsoft.Data.Sqlite;

namespace iCode;

public record ContextMessage(string Role, string Content, DateTime Timestamp);

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
                timestamp TEXT NOT NULL,
                role      TEXT NOT NULL,
                content   TEXT NOT NULL
            )
            """;
        cmd.ExecuteNonQuery();
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

    public List<ContextMessage> LoadAll()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT timestamp, role, content FROM context ORDER BY rowid";
        using var reader = cmd.ExecuteReader();
        var messages = new List<ContextMessage>();
        while (reader.Read())
        {
            messages.Add(new ContextMessage(
                reader.GetString(1),
                reader.GetString(2),
                DateTime.Parse(reader.GetString(0))
            ));
        }
        return messages;
    }

    public void Dispose() => _connection.Dispose();
}
