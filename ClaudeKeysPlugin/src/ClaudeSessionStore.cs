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
    using System.Threading;

    public enum SessionStatus
    {
        NotRunning,
        Idle,
        Busy,
        Waiting,
        Done,
        Background,
    }

    // Progress is the step a busy session is on, when it has a plan.
    public record SessionInfo(String Id, String Name, SessionStatus Status, String BridgeId, Progress Progress = null, Boolean IsCodex = false);

    // Reads Claude's own session files, so nothing has to be configured in Claude.
    // These files are undocumented. All knowledge of their format stays in this class.

    public sealed class ClaudeSessionStore : IDisposable
    {
        public const Int32 PinCount = 3;

        private const Int32 RefreshMilliseconds = 2000;
        private const Int32 LogTailBytes = 256 * 1024;

        private static readonly String Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        private static readonly String DesktopSessions = Path.Combine(Home, "Library", "Application Support", "Claude", "claude-code-sessions");
        private static readonly String LiveSessions = Path.Combine(Home, ".claude", "sessions");
        private static readonly String AppLog = Path.Combine(Home, "Library", "Logs", "Claude", "main.log");
        private static readonly Regex FocusLine = new Regex(@"setFocusedSession: sessionId=(local_[0-9a-f-]+)", RegexOptions.Compiled);

        private record SessionFile(DateTime Modified, String Id, String Title, Int64 CreatedAt, Int64 LastFocusedAt, Boolean IsPinned, Boolean IsArchived, Boolean IsSideChat, String BridgeId, String ParentId);

        private record LiveSession(String Status, String BridgeId, String SessionId, String Cwd, String Name, Int32 Pid);

        private record Snapshot(IReadOnlyList<SessionInfo> All, IReadOnlyList<SessionInfo> Pinned, String CurrentId, String LatestCodexId = null);

        private readonly Dictionary<String, SessionFile> _cache = new Dictionary<String, SessionFile>();
        private readonly Dictionary<String, SessionStatus> _lastStatus = new Dictionary<String, SessionStatus>();
        private readonly HashSet<String> _done = new HashSet<String>();
        private readonly Object _tickLock = new Object();
        private readonly System.Collections.Concurrent.ConcurrentQueue<String> _seen = new System.Collections.Concurrent.ConcurrentQueue<String>();
        private readonly CodexThreads _codex = new CodexThreads();
        private volatile Boolean _codexRead;
        private readonly Dictionary<String, String> _titles = new Dictionary<String, String>();
        private readonly ClaudeTranscripts _transcripts = new ClaudeTranscripts();
        private Timer _timer;
        private volatile Snapshot _snapshot = new Snapshot(Array.Empty<SessionInfo>(), Array.Empty<SessionInfo>(), null);

        public event Action Changed;

        public SessionInfo Current => this.Get(this._snapshot.CurrentId);

        public void Start() => this._timer = new Timer(_ => this.Tick(), null, 0, Timeout.Infinite);

        public void Dispose() => this._timer?.Dispose();

        public SessionInfo GetPin(Int32 slot)
        {
            var pinned = this._snapshot.Pinned;
            return slot >= 0 && slot < pinned.Count ? pinned[slot] : null;
        }

        // The session on screen right now. Claude logs a switch at once, but its session file
        // catches up about a second later, so this reads the log when a key needs the answer now.
        // When Codex is in front, the thread it last opened. If its log says nothing, its most recently active thread.
        public SessionInfo FocusedNow() =>
            ClaudeApp.IsFront(CodexThreads.BundleId)
                ? this.Get(CodexThreads.ReadOpenThreadId()) ?? this.Get(this._snapshot.LatestCodexId)
                : this.Get(ReadFocusedIdFromLog()) ?? this.Current;

        // A key forgets its session only once the session is surely gone. Codex threads count only after Codex has been read.
        public Boolean IsGone(String id) => this.Get(id) == null && (id.StartsWith("local_") || this._codexRead);

        public SessionInfo Get(String id) => id == null ? null : this._snapshot.All.FirstOrDefault(s => s.Id == id);

        // A key press opened this session, so its green clears now rather than on the next refresh.
        public void MarkSeen(String id)
        {
            this._seen.Enqueue(id);
            this._timer?.Change(0, Timeout.Infinite);
        }

        private void Tick()
        {
            lock (this._tickLock)
            {
                this.Refresh();
            }
        }

        private void Refresh()
        {
            try
            {
                var snapshot = this.BuildSnapshot();
                var before = this._snapshot;

                if (!snapshot.All.SequenceEqual(before.All) || snapshot.CurrentId != before.CurrentId || snapshot.LatestCodexId != before.LatestCodexId)
                {
                    this._snapshot = snapshot;

                    if (!snapshot.Pinned.SequenceEqual(before.Pinned))
                    {
                        PluginLog.Info("Pinned: " + String.Join(", ", snapshot.Pinned.Select(p => $"{p.Name}={p.Status}")));
                    }

                    this.Changed?.Invoke();
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "Could not read Claude sessions");
            }

            try
            {
                this._timer.Change(RefreshMilliseconds, Timeout.Infinite);
            }
            catch (ObjectDisposedException)
            {
                // The plugin unloaded during this refresh.
            }
        }

        private Snapshot BuildSnapshot()
        {
            var live = ReadLiveSessions();
            var withShells = ReadPidsRunningShells();
            var files = this.ReadSessionFiles().Where(s => !s.IsArchived).ToList();
            var sessions = files.Where(s => !s.IsSideChat).OrderBy(s => s.CreatedAt).ToList();
            var children = ReadChildStatuses(files, live);
            var currentId = sessions.OrderByDescending(s => s.LastFocusedAt).FirstOrDefault()?.Id;

            while (this._seen.TryDequeue(out var seenId))
            {
                this._done.Remove(seenId);
            }

            var onScreenId = this.ReadOnScreenId(currentId);

            var all = sessions
                .Select(s =>
                {
                    live.TryGetValue(s.Id, out var running);
                    var status = this.TrackDone(s.Id, ToStatus(running), s.Id == onScreenId);
                    children.TryGetValue(s.Id, out var childStatuses);
                    var shown = RollUp(status, childStatuses);

                    // Ready for you, but a command it started is still running.
                    if (shown == SessionStatus.Idle && running != null && withShells.Contains(running.Pid))
                    {
                        shown = SessionStatus.Background;
                    }

                    var progress = shown == SessionStatus.Busy && running?.Cwd != null && running.SessionId != null ? this._transcripts.ReadProgress(running.Cwd, running.SessionId) : null;
                    return new SessionInfo(s.Id, this.GetTitle(s, running), shown, running?.BridgeId ?? s.BridgeId, progress);
                })
                .ToList();

            var codex = this._codex.Read();
            this._codexRead |= codex != null;

            foreach (var thread in codex ?? Array.Empty<CodexThread>())
            {
                all.Add(new SessionInfo(thread.Id, thread.Name, this.TrackDone(thread.Id, thread.Status, thread.Id == onScreenId), null, IsCodex: true));
            }

            var latestCodexId = codex?.OrderByDescending(t => t.RecencyMs).FirstOrDefault()?.Id;
            var pinnedIds = sessions.Where(s => s.IsPinned).Select(s => s.Id).ToHashSet();
            return new Snapshot(all, all.Where(s => pinnedIds.Contains(s.Id)).Take(PinCount).ToList(), currentId, latestCodexId);
        }

        // Claude sometimes saves a session file for a moment without its title, or replaces the file while
        // saving. The key keeps the last title it saw, and on a fresh start uses the running session's name.
        private String GetTitle(SessionFile file, LiveSession running)
        {
            if (file.Title != null)
            {
                this._titles[file.Id] = file.Title;
                return file.Title;
            }

            return this._titles.GetValueOrDefault(file.Id) ?? running?.Name ?? "Untitled";
        }

        // Sessions this one started, such as research it handed off, grouped by parent.
        private static Dictionary<String, List<SessionStatus>> ReadChildStatuses(List<SessionFile> files, Dictionary<String, LiveSession> live) =>
            files
                .Where(f => f.ParentId != null)
                .GroupBy(f => f.ParentId)
                .ToDictionary(g => g.Key, g => g.Select(f => ToStatus(live.GetValueOrDefault(f.Id))).ToList());

        // A child waiting for you turns the parent amber.
        private static SessionStatus RollUp(SessionStatus own, List<SessionStatus> children)
        {
            if (own == SessionStatus.Busy || own == SessionStatus.Waiting)
            {
                return own;
            }

            if (children?.Contains(SessionStatus.Waiting) == true)
            {
                return SessionStatus.Waiting;
            }

            return own;
        }


        // Every finish turns the key green. It clears once the session is on screen, however you got there,
        // or when it starts working again. This lives in memory, so restarting the plugin clears it.
        private SessionStatus TrackDone(String id, SessionStatus status, Boolean isOnScreen)
        {
            var justFinished = this._lastStatus.TryGetValue(id, out var last) && last == SessionStatus.Busy && status == SessionStatus.Idle;
            this._lastStatus[id] = status;

            if (status != SessionStatus.Idle || isOnScreen)
            {
                this._done.Remove(id);
                return status;
            }

            if (justFinished)
            {
                this._done.Add(id);
            }

            return this._done.Contains(id) ? SessionStatus.Done : SessionStatus.Idle;
        }

        // The session you're looking at: the one open in Claude or Codex, but only while that app is in front.
        // Skipped when nothing is green, so most refreshes don't check.
        private String ReadOnScreenId(String currentId)
        {
            if (this._done.Count == 0)
            {
                return null;
            }

            if (ClaudeApp.IsFront(ClaudeApp.BundleId))
            {
                return ReadFocusedIdFromLog() ?? currentId;
            }

            return ClaudeApp.IsFront(CodexThreads.BundleId) ? CodexThreads.ReadOpenThreadId() : null;
        }

        // Where Claude's log ends now, so WasArchivedSince reads only what Claude writes after it.
        public static Int64 AppLogLength()
        {
            try
            {
                return new FileInfo(AppLog).Length;
            }
            catch (IOException)
            {
                return 0;
            }
        }

        // Claude logs each archive, so this tells whether an archive shortcut actually worked.
        public static Boolean WasArchivedSince(Int64 offset, String id)
        {
            try
            {
                using var log = new FileStream(AppLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

                if (log.Length < offset)
                {
                    offset = 0; // The log was rotated.
                }

                var bytes = new Byte[Math.Min(log.Length - offset, LogTailBytes)];
                log.Seek(-bytes.Length, SeekOrigin.End);
                log.ReadExactly(bytes);

                return Encoding.UTF8.GetString(bytes).Contains($"LocalSessions.archive: sessionId={id}");
            }
            catch (IOException)
            {
                return false;
            }
        }

        private static String ReadFocusedIdFromLog()
        {
            try
            {
                using var log = new FileStream(AppLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var length = (Int32)Math.Min(log.Length, LogTailBytes);
                var bytes = new Byte[length];

                log.Seek(-length, SeekOrigin.End);
                log.ReadExactly(bytes);

                return FocusLine.Matches(Encoding.UTF8.GetString(bytes)).LastOrDefault()?.Groups[1].Value;
            }
            catch (IOException)
            {
                return null;
            }
        }

        private static SessionStatus ToStatus(LiveSession running)
        {
            if (running == null)
            {
                return SessionStatus.NotRunning;
            }

            return running.Status switch
            {
                "busy" => SessionStatus.Busy,
                "waiting" => SessionStatus.Waiting,
                _ => SessionStatus.Idle,
            };
        }

        private List<SessionFile> ReadSessionFiles()
        {
            if (!Directory.Exists(DesktopSessions))
            {
                return new List<SessionFile>();
            }

            var paths = Directory.EnumerateFiles(DesktopSessions, "local_*.json", SearchOption.AllDirectories).ToHashSet();

            foreach (var gone in this._cache.Keys.Where(p => !paths.Contains(p)).ToList())
            {
                this._cache.Remove(gone);
            }

            foreach (var path in paths)
            {
                var modified = File.GetLastWriteTimeUtc(path);

                if (this._cache.TryGetValue(path, out var cached) && cached.Modified == modified)
                {
                    continue;
                }

                var file = ParseSessionFile(path, modified);

                if (file != null)
                {
                    this._cache[path] = file;
                }
            }

            return this._cache.Values.ToList();
        }

        private static SessionFile ParseSessionFile(String path, DateTime modified)
        {
            try
            {
                using var json = JsonDocument.Parse(File.ReadAllText(path));
                var root = json.RootElement;

                return new SessionFile(
                    modified,
                    GetString(root, "sessionId"),
                    GetString(root, "title"),
                    GetInt64(root, "createdAt"),
                    GetInt64(root, "lastFocusedAt"),
                    GetBool(root, "isStarred"),
                    GetBool(root, "isArchived"),
                    root.TryGetProperty("btwLoweredFrom", out _),
                    GetLastString(root, "bridgeSessionIds"),
                    root.TryGetProperty("spawnedFrom", out var parent) && parent.ValueKind == JsonValueKind.Object ? GetString(parent, "sessionId") : null);
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException)
            {
                return null; // Caught mid-write. The next refresh reads it again.
            }
        }


        // A command Claude leaves running in the background, such as a long test run, is a shell under the
        // session's process. An idle session has none, so one there means work is still going on.
        private static HashSet<Int32> ReadPidsRunningShells()
        {
            var parents = new HashSet<Int32>();

            try
            {
                var start = new ProcessStartInfo("/bin/ps", "-A -o ppid=,comm=") { UseShellExecute = false, RedirectStandardOutput = true };
                using var ps = Process.Start(start);

                foreach (var line in ps.StandardOutput.ReadToEnd().Split('\n'))
                {
                    var parts = line.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

                    if (parts.Length == 2 && Path.GetFileName(parts[1].Trim()) is "zsh" or "bash" or "sh" && Int32.TryParse(parts[0], out var parent))
                    {
                        parents.Add(parent);
                    }
                }

                ps.WaitForExit();
            }
            catch (Exception ex) when (ex is IOException || ex is System.ComponentModel.Win32Exception)
            {
                // No background check this refresh.
            }

            return parents;
        }

        private static Dictionary<String, LiveSession> ReadLiveSessions()
        {
            var live = new Dictionary<String, LiveSession>();

            if (!Directory.Exists(LiveSessions))
            {
                return live;
            }

            foreach (var path in Directory.EnumerateFiles(LiveSessions, "*.json"))
            {
                try
                {
                    using var json = JsonDocument.Parse(File.ReadAllText(path));
                    var root = json.RootElement;
                    var hostId = GetString(root, "hostSessionId");

                    if (hostId != null && root.TryGetProperty("pid", out var pid) && IsRunning(pid.GetInt32()))
                    {
                        live[hostId] = new LiveSession(GetString(root, "status"), GetString(root, "bridgeSessionId"), GetString(root, "sessionId"), GetString(root, "cwd"), GetString(root, "name"), pid.GetInt32());
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is JsonException || ex is InvalidOperationException || ex is FormatException)
                {
                    // Skip it for this refresh.
                }
            }

            return live;
        }

        private static Boolean IsRunning(Int32 pid)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                return !process.HasExited;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static String GetString(JsonElement element, String name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        private static Int64 GetInt64(JsonElement element, String name) =>
            element.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number : 0;

        private static Boolean GetBool(JsonElement element, String name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

        private static String GetLastString(JsonElement element, String name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0
                ? value[value.GetArrayLength() - 1].GetString()
                : null;
    }
}
