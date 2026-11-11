namespace Loupedeck.ClaudeKeysPlugin
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Text.RegularExpressions;

    public record Progress(Int32 Step, Int32 Steps);

    // Reads a running session's transcript for its step list.
    // Like the session files, these are undocumented. All knowledge of their format stays in this class.

    public sealed class ClaudeTranscripts
    {
        private static readonly String Projects = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
        private static readonly Regex NotAlphanumeric = new Regex("[^A-Za-z0-9]", RegexOptions.Compiled);


        private sealed class Plan
        {
            public Int64 Offset;
            public List<String> Steps = new List<String>();
        }

        private readonly Dictionary<String, Plan> _plans = new Dictionary<String, Plan>();

        // Transcripts are read from where the last read stopped, so a long session costs nothing extra.
        public Progress ReadProgress(String cwd, String sessionId)
        {
            var path = Path.Combine(GetFolder(cwd), sessionId + ".jsonl");

            if (!this._plans.TryGetValue(path, out var plan))
            {
                plan = new Plan();
                this._plans[path] = plan;
            }

            try
            {
                ReadNewLines(path, plan);
            }
            catch (IOException)
            {
                // Keep what we had. The next refresh tries again.
            }

            return ToProgress(plan.Steps);
        }


        private static String GetFolder(String cwd) => Path.Combine(Projects, NotAlphanumeric.Replace(cwd, "-"));

        private static void ReadNewLines(String path, Plan plan)
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            if (file.Length < plan.Offset)
            {
                plan.Offset = 0;
                plan.Steps.Clear();
            }

            var bytes = new Byte[file.Length - plan.Offset];
            file.Seek(plan.Offset, SeekOrigin.Begin);
            file.ReadExactly(bytes);

            // Only whole lines. A line still being written is read next time.
            var end = Array.LastIndexOf(bytes, (Byte)'\n');

            if (end < 0)
            {
                return;
            }

            plan.Offset += end + 1;

            foreach (var line in Encoding.UTF8.GetString(bytes, 0, end).Split('\n'))
            {
                if (line.Contains("update_plan") || line.Contains("wrap_up") || line.Contains("TodoWrite") || line.Contains("\"origin\""))
                {
                    ApplyLine(line, plan.Steps);
                }
            }
        }

        private static void ApplyLine(String line, List<String> steps)
        {
            try
            {
                using var json = JsonDocument.Parse(line);
                var root = json.RootElement;
                var type = GetString(root, "type");

                if (!root.TryGetProperty("message", out var message) || !message.TryGetProperty("content", out var content))
                {
                    return;
                }

                // A new prompt from you starts a turn with no plan yet.
                if (type == "user" && root.TryGetProperty("origin", out _) && content.ValueKind == JsonValueKind.String && !content.GetString().StartsWith("<"))
                {
                    steps.Clear();
                    return;
                }

                if (type != "assistant" || content.ValueKind != JsonValueKind.Array)
                {
                    return;
                }

                foreach (var block in content.EnumerateArray())
                {
                    if (GetString(block, "type") == "tool_use" && block.TryGetProperty("input", out var input))
                    {
                        ApplyTool(GetString(block, "name") ?? String.Empty, input, steps);
                    }
                }
            }
            catch (JsonException)
            {
                // Not a line we understand.
            }
        }

        // The desktop app's task list, or the todo list in the terminal.
        private static void ApplyTool(String name, JsonElement input, List<String> steps)
        {
            if (name.EndsWith("__wrap_up"))
            {
                steps.Clear();
            }
            else if (name == "TodoWrite" && input.TryGetProperty("todos", out var todos))
            {
                SetSteps(todos, steps);
            }
            else if (name.EndsWith("__update_plan"))
            {
                if (input.TryGetProperty("items", out var items))
                {
                    SetSteps(items, steps);
                }

                if (input.TryGetProperty("done", out var done) && done.ValueKind == JsonValueKind.Array)
                {
                    foreach (var number in done.EnumerateArray())
                    {
                        SetStep(steps, number.GetInt32() - 1, "completed");
                    }
                }

                if (input.TryGetProperty("start", out var start) && start.ValueKind == JsonValueKind.Number)
                {
                    SetStep(steps, start.GetInt32() - 1, "in_progress");
                }

                if (input.TryGetProperty("advance", out var advance) && advance.ValueKind == JsonValueKind.True)
                {
                    var current = steps.IndexOf("in_progress");
                    SetStep(steps, current, "completed");
                    SetStep(steps, steps.IndexOf("pending", Math.Max(current, 0)), "in_progress");
                }
            }
        }

        private static void SetSteps(JsonElement list, List<String> steps)
        {
            if (list.ValueKind == JsonValueKind.Array)
            {
                steps.Clear();
                steps.AddRange(list.EnumerateArray().Select(item => GetString(item, "status") ?? "pending"));
            }
        }

        private static void SetStep(List<String> steps, Int32 index, String status)
        {
            if (index >= 0 && index < steps.Count)
            {
                steps[index] = status;
            }
        }

        // The step being worked on, or the next one. Nothing once every step is done.
        private static Progress ToProgress(List<String> steps)
        {
            var completed = steps.Count(s => s == "completed");

            if (completed == steps.Count)
            {
                return null;
            }

            var current = steps.IndexOf("in_progress");
            return new Progress(current >= 0 ? current + 1 : completed + 1, steps.Count);
        }


        private static String GetString(JsonElement element, String name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
