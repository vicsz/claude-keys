namespace Loupedeck.ClaudeKeysPlugin
{
    using System;
    using System.Globalization;

    // Shows your Claude usage and whether you're on track. The ring is what you've used, the white tick is where an
    // even pace would put you today, and the colour is the forecast. Tap to switch between the 5-hour and weekly
    // limits, or to refresh when there's only one.
    // On hold pages it takes Cancel's slot, so it closes the page wherever it's placed, even when it replaces Cancel.

    public class UsageCommand : PluginDynamicCommand
    {
        private const Int32 ReplySlot = 8;

        public UsageCommand()
            : base(displayName: "Usage", description: "Your Claude usage and whether you're on track to stay within it. Tap to switch between 5 hours and the week", groupName: "Claude")
        {
            this.IsWidget = true;
        }

        private ClaudeUsage Usage => ((ClaudeKeysPlugin)this.Plugin).Usage;

        protected override Boolean OnLoad()
        {
            this.Usage.Changed += this.Redraw;
            ReplyMode.Changed += this.Redraw;
            return true;
        }

        protected override Boolean OnUnload()
        {
            this.Usage.Changed -= this.Redraw;
            ReplyMode.Changed -= this.Redraw;
            return true;
        }

        protected override void RunCommand(String actionParameter)
        {
            if (!ReplyMode.TryPress(ReplySlot, this.Plugin.ClientApplication))
            {
                this.Usage.Toggle();
            }
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            this.Usage.EnsureStarted();

            if (ReplyMode.IsActive)
            {
                return ReplyMode.Draw(ReplySlot, imageSize);
            }

            var reading = this.Usage.Latest;
            var pace = this.Usage.GetPace(reading);

            if (reading == null || pace == null)
            {
                return KeyArt.UsageMessage(imageSize, this.Usage.Problem ?? "Usage…");
            }

            var amount = reading.IsDollars ? Dollars(reading.Used) : $"{reading.Used:0}%";
            var footer = pace.Tone == UsageTone.Stale ? Age(reading.FetchedAt)
                : reading.IsDollars ? $"of {Dollars(reading.Limit)}" : reading.Label;

            return KeyArt.Usage(imageSize, reading.Used / reading.Limit, pace.TimeGone, pace.Tone, amount, Line(reading, pace), footer);
        }

        // One short line: when you'd run out, how far ahead of pace you are, or how much you have spare.
        private static String Line(UsageReading reading, UsagePace pace)
        {
            if (pace.Tone == UsageTone.Bad && pace.RunsOut is DateTime runsOut)
            {
                return "out ~" + When(runsOut, reading.PeriodEnd - reading.PeriodStart);
            }

            var ahead = reading.IsDollars ? Dollars(Math.Abs(pace.Ahead)) : $"{Math.Abs(pace.Ahead):0}%";

            if (Math.Abs(pace.Ahead) < reading.Limit * 0.02)
            {
                return "on pace";
            }

            return pace.Ahead > 0 ? $"+{ahead} ahead" : $"{ahead} spare";
        }

        // A month shows the date, a week the day and hour, five hours the time.
        private static String When(DateTime utc, TimeSpan period)
        {
            var local = utc.ToLocalTime();
            var format = period > TimeSpan.FromDays(8) ? "MMM d" : period > TimeSpan.FromDays(1) ? "ddd htt" : "h:mmtt";
            return local.ToString(format, CultureInfo.InvariantCulture).Replace("AM", "am").Replace("PM", "pm");
        }

        private static String Age(DateTime utc)
        {
            var age = DateTime.UtcNow - utc;
            return age.TotalHours >= 1 ? $"{(Int32)age.TotalHours}h old" : $"{(Int32)age.TotalMinutes}m old";
        }

        private static String Dollars(Double amount) => "$" + Math.Round(amount).ToString("0", CultureInfo.InvariantCulture);

        private void Redraw() => this.ActionImageChanged();
    }
}
