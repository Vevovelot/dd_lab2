using System.Text.Json.Nodes;

namespace iCode;

public static class ToolDefinitions
{
    // All tools available to the main agent (including run_subagent).
    public static readonly IReadOnlyList<JsonObject> All;

    // Tools available to subagents — excludes run_subagent to prevent nesting.
    public static readonly IReadOnlyList<JsonObject> WithoutSubagent = new List<JsonObject>
    {
        Tool("read_file", "Read the contents of a file in the working directory",
            Props(Str("path", "File path relative to the working directory")),
            required: ["path"]),

        Tool("write_file", "Create a new file with the given content. Fails if the file already exists.",
            Props(
                Str("path", "File path relative to the working directory"),
                Str("content", "Content to write")),
            required: ["path", "content"]),

        Tool("update_file", "Replace the first occurrence of a specific string in an existing file",
            Props(
                Str("path", "File path relative to the working directory"),
                Str("old_text", "Exact text to find (must be unique enough to identify the location)"),
                Str("new_text", "Replacement text")),
            required: ["path", "old_text", "new_text"]),

        Tool("delete_file", "Delete a file",
            Props(Str("path", "File path relative to the working directory")),
            required: ["path"]),

        Tool("list_files", "List files and directories in the working directory or a subdirectory",
            Props(Str("path", "Relative subdirectory path to list; omit or leave empty for the root")),
            required: []),

        Tool("execute_command", "Execute a shell command in the working directory",
            Props(Str("command", "Shell command to execute")),
            required: ["command"]),

        Tool("load_skill", "Load full instructions for a skill listed in the system prompt",
            Props(Str("name", "Skill name exactly as shown in the Available Skills list")),
            required: ["name"])
    };

    static ToolDefinitions()
    {
        var all = new List<JsonObject>(WithoutSubagent)
        {
            Tool("run_subagent", "Run a subtask in an isolated agent with a clean context and return its result",
                Props(Str("task", "Natural language description of the subtask for the subagent")),
                required: ["task"])
        };
        All = all;
    }

    private static JsonObject Tool(string name, string description, JsonObject properties, string[] required) =>
        new()
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = name,
                ["description"] = description,
                ["parameters"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = properties,
                    ["required"] = new JsonArray(required.Select(r => (JsonNode)JsonValue.Create(r)!).ToArray())
                }
            }
        };

    private static JsonObject Props(params (string Name, JsonObject Schema)[] props)
    {
        var obj = new JsonObject();
        foreach (var (name, schema) in props)
            obj[name] = schema;
        return obj;
    }

    private static (string, JsonObject) Str(string name, string description) =>
        (name, new JsonObject { ["type"] = "string", ["description"] = description });
}
