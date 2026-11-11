namespace Loupedeck.ClaudeKeysPlugin
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Text.RegularExpressions;

    public record CodexThread(String Id, String Name, SessionStatus Status, Int64 RecencyMs);

    // Reads the Codex app's own thread list and transcripts, so Codex threads can sit on session keys.
    // These files are undocumented. All knowledge of their format stays in this class.

    public sealed class CodexThreads
    {
        public const String BundleId = "com.openai.codex";

        private const String ProcessName = "ChatGPT";
        private const Int32 TailBytes = 256 * 1024;
        private const Int32 MaxTurnBytes = 16 * 1024 * 1024;
        private const Int32 NameLength = 40;

        private static readonly String Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        private static readonly String Database = Path.Combine(Home, ".codex", "state_5.sqlite");
        private static readonly String Logs = Path.Combine(Home, "Library", "Logs", BundleId);

        // Codex lists a thread's queue once each time you open it, new threads included. Other thread lines also
        // fire for every sidebar thread when the window reloads, or for background review threads.
        private static readonly Regex OpenLine = new Regex(@"conversationId=([0-9a-f-]{36})[^\n]*method=thread/(queue/list)\b", RegexOptions.Compiled);

        // A thread untouched for this long is not working, whatever its transcript last said.
        private static readonly TimeSpan BusyQuietLimit = TimeSpan.FromMinutes(10);

        // Only threads with recent writes can be busy or waiting, so older transcripts are never opened.
        private static readonly TimeSpan ActiveLimit = TimeSpan.FromHours(12);

        private record Row(String Id, String Name, String RolloutPath, Int64 RecencyMs);

        private List<Row> _rows;
        private DateTime _databaseWritten;

        // Null until the thread list has been read once, so a failed first read never looks like no threads.
        public IReadOnlyList<CodexThread> Read()
        {
            this.RefreshRows();

            if (this._rows == null)
            {
                return null;
            }

            var running = IsRunning();
            return this._rows.Select(r => new CodexThread(r.Id, r.Name, running ? ReadStatus(r.RolloutPath) : SessionStatus.NotRunning, r.RecencyMs)).ToList();
        }

        public static void Open(String id)
        {
            if (Guid.TryParse(id, out _))
            {
                Process.Start("/usr/bin/open", $"codex://threads/{id}")?.Dispose();
            }
        }

        // The thread on screen: the last one Codex opened, from its newest log. Null if the log says nothing.
        public static String ReadOpenThreadId()
        {
            try
            {
                var logs = new DirectoryInfo(Logs).EnumerateFiles("*.log", SearchOption.AllDirectories)
                    .Where(f => f.Length > 0)
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Take(3);

                foreach (var log in logs)
                {
                    var id = ReadTail(log.FullName).Select(l => OpenLine.Match(l)).LastOrDefault(m => m.Success)?.Groups[1].Value;

                    if (id != null)
                    {
                        return id;
                    }
                }
            }
            catch (IOException)
            {
                // Fall back to the most recent thread.
            }

            return null;
        }

        // The database only changes when Codex writes, so it is read again only then.
        private void RefreshRows()
        {
            var written = new[] { Database, Database + "-wal" }.Where(File.Exists).Select(File.GetLastWriteTimeUtc).DefaultIfEmpty().Max();

            if (this._rows != null && written == this._databaseWritten)
            {
                return;
            }

            try
            {
                var json = RunSqlite(
                    "select id, name, title, rollout_path, recency_at_ms from threads " +
                    "where archived = 0 and source not like '{%' and preview <> ''");

                using var document = JsonDocument.Parse(String.IsNullOrWhiteSpace(json) ? "[]" : json);
                this._rows = document.RootElement.EnumerateArray()
                    .Select(t => new Row(GetString(t, "id"), ToName(GetString(t, "name"), GetString(t, "title")), GetString(t, "rollout_path"), t.GetProperty("recency_at_ms").GetInt64()))
                    .ToList();
                this._databaseWritten = written;
            }
            catch (Exception ex) when (ex is JsonException || ex is InvalidOperationException || ex is KeyNotFoundException || ex is System.ComponentModel.Win32Exception)
            {
                // Codex was mid-write. Keep the list we had and read it again next time.
                PluginLog.Warning(ex, "Could not read Codex threads");
            }
        }

        // Codex names a thread after a while. Until then, the start of your first message stands in.
        private static String ToName(String name, String title)
        {
            var text = (String.IsNullOrWhiteSpace(name) ? title : name)?.Trim() ?? "Untitled";
            return text.Length > NameLength ? text.Substring(0, NameLength) : text;
        }

        // A turn starts and then completes or is stopped. In between, a request for your input means it is waiting.
        private static SessionStatus ReadStatus(String path)
        {
            try
            {
                var info = new FileInfo(path);

                if (!info.Exists || DateTime.UtcNow - info.LastWriteTimeUtc > ActiveLimit)
                {
                    return SessionStatus.Idle;
                }

                var status = SessionStatus.Idle;
                String pendingInput = null;

                foreach (var line in ReadCurrentTurn(path))
                {
                    if (!line.Contains("task_") && !line.Contains("turn_aborted") && !line.Contains("function_call"))
                    {
                        continue;
                    }

                    using var json = JsonDocument.Parse(line);

                    if (!json.RootElement.TryGetProperty("payload", out var payload))
                    {
                        continue;
                    }

                    switch (GetString(payload, "type"))
                    {
                        case "task_started":
                            status = SessionStatus.Busy;
                            break;
                        case "task_complete":
                        case "turn_aborted":
                            status = SessionStatus.Idle;
                            pendingInput = null;
                            break;
                        case "function_call" when GetString(payload, "name")?.StartsWith("request_user_input") == true:
                            pendingInput = GetString(payload, "call_id");
                            break;
                        case "function_call_output" when pendingInput != null && GetString(payload, "call_id") == pendingInput:
                            pendingInput = null;
                            break;
                    }
                }

                if (status == SessionStatus.Busy && pendingInput != null)
                {
                    return SessionStatus.Waiting;
                }

                return status == SessionStatus.Busy && DateTime.UtcNow - info.LastWriteTimeUtc > BusyQuietLimit ? SessionStatus.Idle : status;
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException)
            {
                return SessionStatus.Idle;
            }
        }

        // Every step Codex takes is logged at length, so a long turn can start well before the tail.
        // Reads further back until the tail holds the turn's start or end.
        private static IReadOnlyList<String> ReadCurrentTurn(String path)
        {
            for (var bytes = TailBytes; ; bytes *= 4)
            {
                var lines = ReadTail(path, bytes);

                if (bytes >= MaxTurnBytes || lines.Any(IsTurnEdge) || new FileInfo(path).Length <= bytes)
                {
                    return lines;
                }
            }
        }

        private static Boolean IsTurnEdge(String line) =>
            line.Contains("\"task_started\"") || line.Contains("\"task_complete\"") || line.Contains("\"turn_aborted\"");

        // Whole lines only. The first one may be cut off by the tail, and the last one may be half written.
        private static IReadOnlyList<String> ReadTail(String path, Int32 tailBytes = TailBytes)
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var length = (Int32)Math.Min(file.Length, tailBytes);
            var bytes = new Byte[length];

            file.Seek(-length, SeekOrigin.End);
            file.ReadExactly(bytes);

            var lines = Encoding.UTF8.GetString(bytes).Split('\n');
            return lines.Skip(length < file.Length ? 1 : 0).Take(lines.Length - 1 - (length < file.Length ? 1 : 0)).Where(l => l.Length > 0).ToList();
        }

        private static Boolean IsRunning()
        {
            var processes = Process.GetProcessesByName(ProcessName);
            var running = processes.Length > 0;

            foreach (var process in processes)
            {
                process.Dispose();
            }

            return running;
        }

        private static String RunSqlite(String query)
        {
            var start = new ProcessStartInfo("/usr/bin/sqlite3")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            foreach (var argument in new[] { "-readonly", "-json", "-cmd", ".timeout 1000", Database, query })
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start);
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException("sqlite3: " + process.StandardError.ReadToEnd().Trim());
            }

            return output;
        }

        private static String GetString(JsonElement element, String name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
