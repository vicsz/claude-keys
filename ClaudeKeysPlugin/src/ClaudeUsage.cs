namespace Loupedeck.ClaudeKeysPlugin
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text.Json;
    using System.Threading;

    // Claude's real usage, from the same server call /usage makes, using the Claude CLI's sign-in.
    // All usage knowledge stays here. The sign-in is read from the Keychain each time, kept only in memory,
    // and never logged. When it has expired, running `claude -p /usage` makes the CLI refresh it (free, no model call).
    // Nothing starts until a Usage key is first drawn, so people who don't place one never have their sign-in read.
    // Plans have a 5-hour and a weekly limit, and tapping the key switches between them. Spend-limit accounts have the month only.

    public enum UsageTone { Good, Warn, Bad, Stale }

    // Used and Limit are dollars on accounts with a spend limit, otherwise percent of the plan limit (Limit = 100).
    public record UsageReading(Boolean IsDollars, Double Used, Double Limit, DateTime PeriodStart, DateTime PeriodEnd, String Severity, String Label, DateTime FetchedAt);

    // Where you stand against an even pace through the period. Ahead is how far over that pace you are (negative = spare).
    public record UsagePace(Double TimeGone, Double Ahead, DateTime? RunsOut, UsageTone Tone);

    public sealed class ClaudeUsage : IDisposable
    {
        private const String UsageUrl = "https://api.anthropic.com/api/oauth/usage";
        private static readonly TimeSpan PollEvery = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan CliRefreshCooldown = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan KeepHistory = TimeSpan.FromDays(31);
        private static readonly String Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        private static readonly String[] CliPlaces =
        {
            "/opt/homebrew/bin/claude",
            "/usr/local/bin/claude",
            Path.Combine(Home, ".local", "bin", "claude"),
            Path.Combine(Home, ".claude", "local", "claude"),
            Path.Combine(Home, ".npm-global", "bin", "claude"),
        };

        private readonly String _dataDirectory;
        private readonly String _historyPath;
        private readonly Object _gate = new Object();
        private Timer _timer;
        private Int32 _polling;
        private DateTime _lastCliRefresh = DateTime.MinValue;
        private String _lastLogged;
        private List<(DateTime At, Double Used, DateTime PeriodStart)> _history;
        private UsageReading _session;
        private UsageReading _long;
        private Boolean _showSession = true;
        private DateTime _acknowledgedRedWeek;

        public ClaudeUsage(String dataDirectory)
        {
            this._dataDirectory = dataDirectory;
            this._historyPath = Path.Combine(dataDirectory, "usage-history.json");
        }

        public event Action Changed;

        // The reading the key shows: the month on spend-limit accounts, otherwise the 5-hour or weekly limit you picked.
        // A weekly limit heading for red takes over the 5-hour view, until you tap past it once that week.
        public UsageReading Latest
        {
            get
            {
                if (this._long == null || this._long.IsDollars || this._session == null || !this._showSession)
                {
                    return this._long ?? this._session;
                }

                var weekIsRed = this.GetPace(this._long)?.Tone == UsageTone.Bad && this._acknowledgedRedWeek != this._long.PeriodStart;
                return weekIsRed ? this._long : this._session;
            }
        }

        // A short reason the key can show when there's no reading, like "Sign in to CLI".
        public String Problem { get; private set; }

        private static Boolean IsStale(UsageReading reading) => DateTime.UtcNow - reading.FetchedAt > StaleAfter;

        public void EnsureStarted()
        {
            lock (this._gate)
            {
                this._timer ??= new Timer(_ => this.Poll(), null, TimeSpan.Zero, PollEvery);
            }
        }

        public void RefreshNow() => ThreadPool.QueueUserWorkItem(_ => this.Poll());

        // Switches between the 5-hour and weekly limits. With only one to show, it refreshes instead.
        public void Toggle()
        {
            if (this._session == null || this._long == null || this._long.IsDollars)
            {
                this.RefreshNow();
                return;
            }

            var shown = this.Latest;

            if (shown == this._long && this._showSession)
            {
                this._acknowledgedRedWeek = this._long.PeriodStart; // The week had taken over: tapping means show the 5 hours anyway.
            }
            else
            {
                this._showSession = !this._showSession;
            }

            this.Changed?.Invoke();
        }

        public void Dispose() => this._timer?.Dispose();

        public UsagePace GetPace(UsageReading reading)
        {
            if (reading == null)
            {
                return null;
            }

            var now = DateTime.UtcNow;
            var period = reading.PeriodEnd - reading.PeriodStart;
            var timeGone = Math.Clamp((now - reading.PeriodStart) / period, 0, 1);
            var ahead = reading.Used - (reading.Limit * timeGone);
            var rate = this.RatePerHour(reading, now);
            DateTime? runsOut = reading.Used >= reading.Limit ? now
                : rate > 0 ? now.AddHours((reading.Limit - reading.Used) / rate) : null;

            UsageTone tone;

            if (IsStale(reading))
            {
                tone = UsageTone.Stale;
            }
            else if (runsOut != null && runsOut < reading.PeriodEnd - (period * 0.1))
            {
                tone = UsageTone.Bad;
            }
            else if (ahead > 0 && timeGone > 0.03)
            {
                tone = UsageTone.Warn;
            }
            else
            {
                tone = UsageTone.Good;
            }

            // Never show calmer than Anthropic does.
            if (tone != UsageTone.Stale && reading.Severity is not (null or "normal"))
            {
                var floor = reading.Severity == "warning" ? UsageTone.Warn : UsageTone.Bad;
                tone = (UsageTone)Math.Max((Int32)tone, (Int32)floor);
            }

            return new UsagePace(timeGone, ahead, runsOut < reading.PeriodEnd ? runsOut : null, tone);
        }

        // The recent rate: the last 7 days of this period when there's enough history, otherwise the average so far.
        // Five hours is too short for history to help, so it always uses the average.
        private Double RatePerHour(UsageReading reading, DateTime now)
        {
            var hours = (now - reading.PeriodStart).TotalHours;

            if (reading.PeriodEnd - reading.PeriodStart <= TimeSpan.FromDays(1))
            {
                return hours > 0 ? reading.Used / hours : 0;
            }

            var since = now - TimeSpan.FromDays(7);
            var minimumSpan = (reading.PeriodEnd - reading.PeriodStart) * 0.1;
            var oldest = this._history?
                .Where(h => h.PeriodStart == reading.PeriodStart && h.At >= since)
                .OrderBy(h => h.At)
                .FirstOrDefault();

            if (oldest is { } start && start.At != default && now - start.At >= minimumSpan)
            {
                return Math.Max(0, reading.Used - start.Used) / (now - start.At).TotalHours;
            }

            return hours > 0 ? reading.Used / hours : 0;
        }

        private void Poll()
        {
            if (Interlocked.Exchange(ref this._polling, 1) == 1)
            {
                return;
            }

            try
            {
                this._history ??= this.LoadHistory();
                var (session, longer) = this.Fetch();

                if (longer != null || session != null)
                {
                    this._session = session;
                    this._long = longer;
                    this.Problem = null;

                    if (longer != null)
                    {
                        this.Remember(longer);
                    }

                    this.LogOnce($"Usage: {Describe(longer)}{(session != null ? ", " + Describe(session) : String.Empty)}");
                }
            }
            catch (Exception ex)
            {
                this.Problem = "No usage";
                this.LogOnce("Could not read Claude usage: " + ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref this._polling, 0);
                this.Changed?.Invoke();
            }
        }

        private static String Describe(UsageReading reading) =>
            reading == null ? "none" : $"{reading.Used:0.##} of {reading.Limit:0.##} ({reading.Label})";

        private (UsageReading Session, UsageReading Long) Fetch()
        {
            var signIn = ReadSignIn();

            if (signIn == null)
            {
                this.Problem = "Sign in to CLI";
                this.LogOnce("No Claude CLI sign-in in the Keychain. Run `claude auth login`.");
                return (null, null);
            }

            if (signIn.Value.ExpiresAt <= DateTime.UtcNow && this.RefreshThroughCli())
            {
                signIn = ReadSignIn() ?? signIn;
            }

            var (status, body) = Call(signIn.Value.Token);

            if (status == HttpStatusCode.Unauthorized && this.RefreshThroughCli())
            {
                signIn = ReadSignIn();
                (status, body) = signIn == null ? (status, body) : Call(signIn.Value.Token);
            }

            if (status != HttpStatusCode.OK)
            {
                this.Problem = status != HttpStatusCode.Unauthorized ? "No usage"
                    : this.Problem == "No Claude CLI" ? this.Problem : "Sign in to CLI";
                this.LogOnce($"Claude usage call returned {(Int32)status}");
                return (null, null);
            }

            var reading = Parse(body);

            if (reading.Session == null && reading.Long == null)
            {
                this.Problem = "No usage";
                this.LogOnce("Claude usage had no spend limit or plan limit the key understands");
            }

            return reading;
        }

        private static (HttpStatusCode Status, String Body) Call(String token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
            request.Headers.UserAgent.ParseAdd("claude-keys");

            using var response = Http.Send(request);
            using var reader = new StreamReader(response.Content.ReadAsStream());
            return (response.StatusCode, reader.ReadToEnd());
        }

        // Accounts with a spend limit (like enterprise seats) get dollars for the calendar month, which resets
        // at midnight UTC on the 1st. Plans get their 5-hour limit and their weekly limit for all models.
        private static (UsageReading Session, UsageReading Long) Parse(String body)
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var now = DateTime.UtcNow;

            if (root.TryGetProperty("spend", out var spend) && spend.ValueKind == JsonValueKind.Object &&
                Money(spend, "limit") is Double limit && limit > 0)
            {
                var used = Money(spend, "used") ?? 0;
                var start = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                return (null, new UsageReading(true, used, limit, start, start.AddMonths(1), Text(spend, "severity"), "month", now));
            }

            if (!root.TryGetProperty("limits", out var limits) || limits.ValueKind != JsonValueKind.Array)
            {
                return (null, null);
            }

            var all = limits.EnumerateArray().ToList();
            return (Limit(all, "session", TimeSpan.FromHours(5), "5 hours", now), Limit(all, "weekly_all", TimeSpan.FromDays(7), "week", now));
        }

        private static UsageReading Limit(List<JsonElement> limits, String kind, TimeSpan length, String label, DateTime now)
        {
            var limit = limits.FirstOrDefault(l => Text(l, "kind") == kind);

            if (limit.ValueKind != JsonValueKind.Object ||
                !DateTime.TryParse(Text(limit, "resets_at"), null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var end))
            {
                return null;
            }

            var percent = limit.TryGetProperty("percent", out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0;
            return new UsageReading(false, percent, 100, end - length, end, Text(limit, "severity"), label, now);
        }

        // Amounts come as minor units with an exponent: 7518 with exponent 2 is $75.18.
        private static Double? Money(JsonElement parent, String name)
        {
            if (!parent.TryGetProperty(name, out var money) || money.ValueKind != JsonValueKind.Object ||
                !money.TryGetProperty("amount_minor", out var amount) || amount.ValueKind != JsonValueKind.Number)
            {
                return null;
            }

            var exponent = money.TryGetProperty("exponent", out var e) && e.ValueKind == JsonValueKind.Number ? e.GetInt32() : 2;
            return amount.GetDouble() / Math.Pow(10, exponent);
        }

        private static String Text(JsonElement parent, String name) =>
            parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        private static (String Token, DateTime ExpiresAt)? ReadSignIn()
        {
            var json = Run("/usr/bin/security", TimeSpan.FromSeconds(10), "find-generic-password", "-s", "Claude Code-credentials", "-w");

            if (String.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(json);
                var oauth = document.RootElement.GetProperty("claudeAiOauth");
                var token = oauth.GetProperty("accessToken").GetString();
                var expires = oauth.TryGetProperty("expiresAt", out var e) && e.ValueKind == JsonValueKind.Number
                    ? DateTimeOffset.FromUnixTimeMilliseconds(e.GetInt64()).UtcDateTime
                    : DateTime.MaxValue;
                return String.IsNullOrEmpty(token) ? null : (token, expires);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // `claude -p /usage` refreshes the CLI's own sign-in without calling a model. Limited to once per half hour.
        private Boolean RefreshThroughCli()
        {
            if (DateTime.UtcNow - this._lastCliRefresh < CliRefreshCooldown)
            {
                return false;
            }

            this._lastCliRefresh = DateTime.UtcNow;
            var cli = CliPlaces.FirstOrDefault(File.Exists);

            if (cli == null)
            {
                this.Problem = "No Claude CLI";
                this.LogOnce("Claude usage needs the Claude CLI to refresh its sign-in, and it wasn't found");
                return false;
            }

            Run(cli, TimeSpan.FromSeconds(60), "-p", "/usage");
            PluginLog.Info("Refreshed the Claude CLI sign-in for usage");
            return true;
        }

        private static String Run(String command, TimeSpan timeout, params String[] arguments)
        {
            var start = new ProcessStartInfo(command)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetTempPath(),
            };

            start.Environment["PATH"] = "/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin";

            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start);
            var output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(timeout))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }

            return process.ExitCode == 0 ? output.Result : null;
        }

        // One amount every poll, kept for a month, so the key can use your recent rate rather than the month's average.
        private void Remember(UsageReading reading)
        {
            var cutoff = DateTime.UtcNow - KeepHistory;
            this._history.RemoveAll(h => h.At < cutoff);
            this._history.Add((reading.FetchedAt, reading.Used, reading.PeriodStart));

            try
            {
                Directory.CreateDirectory(this._dataDirectory);
                var rows = this._history.Select(h => new[] { new DateTimeOffset(h.At).ToUnixTimeSeconds(), h.Used, new DateTimeOffset(h.PeriodStart).ToUnixTimeSeconds() });
                var temporary = this._historyPath + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(rows));
                File.Move(temporary, this._historyPath, overwrite: true);
            }
            catch (IOException ex)
            {
                this.LogOnce("Could not save usage history: " + ex.Message);
            }
        }

        private List<(DateTime At, Double Used, DateTime PeriodStart)> LoadHistory()
        {
            try
            {
                var rows = JsonSerializer.Deserialize<Double[][]>(File.ReadAllText(this._historyPath));
                return rows.Where(r => r.Length == 3)
                    .Select(r => (DateTimeOffset.FromUnixTimeSeconds((Int64)r[0]).UtcDateTime, r[1], DateTimeOffset.FromUnixTimeSeconds((Int64)r[2]).UtcDateTime))
                    .ToList();
            }
            catch (Exception)
            {
                return new List<(DateTime, Double, DateTime)>();
            }
        }

        private void LogOnce(String message)
        {
            if (message != this._lastLogged)
            {
                this._lastLogged = message;
                PluginLog.Info(message);
            }
        }
    }
}
