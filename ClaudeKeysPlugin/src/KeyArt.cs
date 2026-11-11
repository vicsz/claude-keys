namespace Loupedeck.ClaudeKeysPlugin
{
    using System;

    // Draws every key. The keypad asks for 116 x 116 images, so the layout is written at that size.

    public static class KeyArt
    {
        public const String PlusIcon = "<path d='M12 4v16M4 12h16'/>";
        public const String MicIcon = "<rect x='8.5' y='2' width='7' height='13' rx='3.5'/><path d='M4.5 11.5a7.5 7.5 0 0 0 15 0M12 19v3M8 22h8'/>";
        public const String ReturnIcon = "<path d='M20 4v7a4 4 0 0 1-4 4H4'/><path d='M9 10l-5 5 5 5'/>";
        public const String CheckIcon = "<path d='M4.5 12.5l5 5 10-11'/>";
        public const String CommitIcon = "<circle cx='12' cy='12' r='4'/><path d='M2 12h6M16 12h6'/>";
        public const String PinIcon = "<path d='M12 16v6M8 2.5h8l-1 7.5 4 5H5l4-5z'/>";
        public const String PinHereIcon = PinIcon + "<path d='M20.5 2v5M18 4.5h5'/>";
        public const String UnpinIcon = PinIcon + "<path d='M3 3l18 18'/>";

        // A Return arrow with a mark above it for where your text goes: a plus for a new session, a speech
        // bubble for a side chat, a branch for a fork. The arrow sits low and wide to leave room for the mark,
        // and each mark is placed by eye over the arrow's body.
        private const String LowReturn = "<path d='M21 11v3.5a3.5 3.5 0 0 1-3.5 3.5H3.5'/><path d='M8 13.5l-4.5 4.5 4.5 4.5'/>";
        public const String NewIcon = LowReturn + "<g transform='translate(13 5) scale(1.3) translate(-20 -5)' stroke-width='1.55'><path d='M20.5 2v5M18 4.5h5'/></g>";
        public const String SideChatIcon = LowReturn + "<g transform='translate(15 5) scale(1.3) translate(-20 -5)' stroke-width='1.55'><path d='M16.5 1.5h6a1 1 0 0 1 1 1V6a1 1 0 0 1-1 1H19l-2 1.8V7h-.5a1 1 0 0 1-1-1V2.5a1 1 0 0 1 1-1z'/></g>";
        public const String ForkIcon = LowReturn + "<g transform='translate(14 5) scale(1.3) translate(-20 -5)' stroke-width='1.55'><path d='M20 9V5.5M20 5.5L17 2M20 5.5L23 2'/></g>";

        // A lidded box with an arrow going in: put away, not gone.
        public const String ArchiveIcon = "<rect x='2.5' y='3.5' width='19' height='5' rx='1.2'/><path d='M4.5 8.5v10a2 2 0 0 0 2 2h11a2 2 0 0 0 2-2v-10'/><path d='M12 11v6M9 14.5l3 3 3-3'/>";

        public const String CancelIcon = "<path d='M6 6l12 12M18 6L6 18'/>";
        private const String QuestionMark = "<path d='M9.6 8.2a2.5 2.5 0 1 1 3.6 2.3c-.8.4-1.2 1-1.2 1.7'/><path d='M12 15v.01'/>";
        private const String CheckMark = "<path d='M7.5 11l3 3 6-6'/>";

        // The SDK turns one-colour SVGs white. This invisible second colour keeps ours.
        private const String KeepColor = "<circle r='0' fill='#000001' stroke='none'/>";

        private const UInt32 Surface = 0x11161E;
        private const UInt32 ReplySurface = 0x1B2330;
        private const UInt32 Off = 0x0C1016;
        private const UInt32 Text = 0xE7EDF5;
        private const UInt32 Muted = 0x8A95A5;
        private const UInt32 Blue = 0x49A8FF;
        private const UInt32 DimBlue = 0x2B5E8E;
        private const UInt32 UnlitDot = 0x24405C;
        private const UInt32 Amber = 0xFFB547;
        private const UInt32 Green = 0x37D39A;
        private const UInt32 Dim = 0x3A4653;
        private const UInt32 DimText = 0x66727F;
        private const UInt32 Ink = 0x14100A;

        private const Int32 DesignSize = 116;
        private const Int32 LineLength = 9;

        // Session keys keep the bubble and name area in the same place, whatever the name's length.
        // The bubble is nearly square, sized for a single-digit count like 3/5. Its tail ends just above the name.
        private const Int32 BubbleWidth = 54;
        private const Int32 BubbleTop = 11;
        private const Int32 BubbleBottom = 48;
        private const Int32 BubbleAreaHeight = 56;
        private const Single BubbleStroke = 38 / 12f;
        private const Single BubbleCorner = 38 / 8f;
        private const Single InsideScale = 38 / 24f * 1.25f;
        private const Int32 CountFontSize = 21;
        private const Int32 LongCountFontSize = 15;
        private const Int32 NameTop = 50;
        private const Int32 NameHeight = 60;
        private const Int32 NameFontSize = 20;
        private const Int32 NameLineHeight = 23;

        public static BitmapImage Empty(PluginImageSize imageSize)
        {
            using (var builder = new BitmapBuilder(imageSize))
            {
                builder.Clear(BitmapColor.FromRgb(Off));
                return builder.ToImage();
            }
        }

        public static BitmapImage Control(PluginImageSize imageSize, String icon, String label) =>
            DrawControl(imageSize, icon, label, Text, Surface);

        public static BitmapImage EmptySessionKey(PluginImageSize imageSize) =>
            DrawControl(imageSize, PinHereIcon, "Pin here", Dim, Off);

        // Replies use the same layout as the main keys, on a slightly lifted background.
        // Cancel is muted.
        public static BitmapImage Reply(PluginImageSize imageSize, String icon, String label, Boolean isCancel) =>
            DrawControl(imageSize, icon, label, isCancel ? Muted : Text, ReplySurface);

        private static BitmapImage DrawControl(PluginImageSize imageSize, String icon, String label, UInt32 color, UInt32 background)
        {
            using (var builder = new BitmapBuilder(imageSize))
            {
                var scale = (Single)builder.Width / DesignSize;

                builder.Clear(BitmapColor.FromRgb(background));
                DrawIcon(builder, icon, color, 33, 12, 50, scale);
                builder.DrawText(label, 0, Scaled(74, scale), builder.Width, Scaled(32, scale), BitmapColor.FromRgb(color), Scaled(19, scale));
                return builder.ToImage();
            }
        }

        // dotFrame picks which dot is lit while work is going on. A grey bubble with dots means the session is
        // ready for you, but a command it started is still running.
        public static BitmapImage Session(PluginImageSize imageSize, SessionInfo session, Int32 dotFrame)
        {
            using (var builder = new BitmapBuilder(imageSize))
            {
                var scale = (Single)builder.Width / DesignSize;

                builder.Clear(BitmapColor.FromRgb(GetBackground(session.Status)));
                DrawBubble(builder, session, dotFrame, scale);
                builder.DrawText(FitName(session.Name), 0, Scaled(NameTop, scale), builder.Width, Scaled(NameHeight, scale), BitmapColor.FromRgb(GetTextColor(session.Status)), Scaled(NameFontSize, scale), Scaled(NameLineHeight, scale));
                return builder.ToImage();
            }
        }

        // The bubble is wide on every key, so it sits the same on all of them whatever is inside.
        private static void DrawBubble(BitmapBuilder builder, SessionInfo session, Int32 dotFrame, Single scale)
        {
            switch (session.Status)
            {
                case SessionStatus.Busy when session.Progress != null:
                    DrawWideBubble(builder, session.IsCodex, String.Empty, Blue, null, scale);
                    DrawCount(builder, $"{session.Progress.Step}/{session.Progress.Steps}", dotFrame == 2 ? DimBlue : Blue, scale);
                    break;
                case SessionStatus.Busy:
                    DrawWideBubble(builder, session.IsCodex, Dots(dotFrame, Blue, UnlitDot), Blue, null, scale);
                    break;
                case SessionStatus.Background:
                    DrawWideBubble(builder, session.IsCodex, Dots(dotFrame, Blue, UnlitDot), Muted, null, scale);
                    break;
                case SessionStatus.Waiting:
                    DrawWideBubble(builder, session.IsCodex, Stroked(QuestionMark, Amber), Ink, Ink, scale);
                    break;
                case SessionStatus.Done:
                    DrawWideBubble(builder, session.IsCodex, Stroked(CheckMark, Green), Ink, Ink, scale);
                    break;
                case SessionStatus.Idle:
                    DrawWideBubble(builder, session.IsCodex, String.Empty, Muted, null, scale);
                    break;
                default:
                    DrawWideBubble(builder, session.IsCodex, String.Empty, Dim, null, scale);
                    break;
            }
        }

        // The outline is drawn at key size. What goes inside uses the 24-unit icon grid, a little larger than
        // the icons, with its centre on the bubble's centre.
        // Codex bubbles point their tail right, so you can tell them from Claude's at a glance.
        private static void DrawWideBubble(BitmapBuilder builder, Boolean tailRight, String inside, UInt32 color, UInt32? fill, Single scale)
        {
            const Single left = ((DesignSize - BubbleWidth) / 2f) + (BubbleStroke / 2);
            const Single right = ((DesignSize + BubbleWidth) / 2f) - (BubbleStroke / 2);
            const Single top = BubbleTop;
            const Single bottom = BubbleBottom;
            const Single r = BubbleCorner;
            const Single middle = (top + bottom) / 2;

            var outline = FormattableString.Invariant(
                $"M{left + r},{top} H{right - r} A{r},{r} 0 0 1 {right},{top + r} V{bottom - r} A{r},{r} 0 0 1 {right - r},{bottom} ") +
                FormattableString.Invariant($"H{left + 14} L{left + 6},{bottom + 5} V{bottom} H{left + r} A{r},{r} 0 0 1 {left},{bottom - r} V{top + r} A{r},{r} 0 0 1 {left + r},{top} Z");
            var width = Scaled(DesignSize, scale);
            var height = Scaled(BubbleAreaHeight, scale);
            var svg = FormattableString.Invariant(
                $"<svg xmlns='http://www.w3.org/2000/svg' width='{width}' height='{height}' viewBox='0 0 {DesignSize} {BubbleAreaHeight}' fill='none' stroke='{Hex(color)}' stroke-linecap='round' stroke-linejoin='round'>") +
                FormattableString.Invariant($"<path d='{outline}' fill='{(fill.HasValue ? Hex(fill.Value) : "none")}' stroke-width='{BubbleStroke}'{(tailRight ? $" transform='translate({DesignSize} 0) scale(-1 1)'" : String.Empty)}/>") +
                FormattableString.Invariant($"<g transform='translate({(DesignSize / 2f) - (12 * InsideScale)} {middle - (11 * InsideScale)}) scale({InsideScale})' stroke-width='{BubbleStroke / InsideScale}'>{inside}</g>{KeepColor}</svg>");

            builder.DrawImage(BitmapImage.FromSvg(svg), 0, 0, width, height);
        }

        // Plans are nearly always single digit. A longer one like 10/12 gets a smaller size to fit.
        private static void DrawCount(BitmapBuilder builder, String count, UInt32 color, Single scale)
        {
            var size = count.Length > 3 ? LongCountFontSize : CountFontSize;
            builder.DrawText(count, 0, Scaled(BubbleTop, scale), builder.Width, Scaled(BubbleBottom - BubbleTop, scale), BitmapColor.FromRgb(color), Scaled(size, scale));
        }

        private static void DrawIcon(BitmapBuilder builder, String body, UInt32 color, Int32 x, Int32 y, Int32 size, Single scale)
        {
            var pixels = Scaled(size, scale);
            var svg = $"<svg xmlns='http://www.w3.org/2000/svg' width='{pixels}' height='{pixels}' viewBox='0 0 24 24' fill='none' stroke='{Hex(color)}' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'>{body}{KeepColor}</svg>";

            builder.DrawImage(BitmapImage.FromSvg(svg), Scaled(x, scale), Scaled(y, scale), pixels, pixels);
        }

        private static String Filled(String body) => $"<g fill='{Hex(Ink)}'>{body}</g>";

        private static String Stroked(String body, UInt32 color) => $"<g stroke='{Hex(color)}'>{body}</g>";

        private static String Dots(Int32 litDot, UInt32 lit, UInt32 unlit, Single centre = 12, Single gap = 5, Single radius = 1.6f)
        {
            var dots = String.Empty;

            for (var dot = 0; dot < 3; dot++)
            {
                var color = dot == litDot ? lit : unlit;
                dots += FormattableString.Invariant($"<circle cx='{centre + ((dot - 1) * gap)}' cy='11' r='{radius}' fill='{Hex(color)}' stroke='none'/>");
            }

            return dots;
        }

        private static Int32 Scaled(Int32 value, Single scale) => (Int32)Math.Round(value * scale);

        private static String Hex(UInt32 color) => $"#{color:X6}";

        private static UInt32 GetBackground(SessionStatus status) => status switch
        {
            SessionStatus.Waiting => Amber,
            SessionStatus.Done => Green,
            SessionStatus.NotRunning => Off,
            _ => Surface,
        };

        private static UInt32 GetTextColor(SessionStatus status) => status switch
        {
            SessionStatus.Waiting => Ink,
            SessionStatus.Done => Ink,
            SessionStatus.NotRunning => DimText,
            _ => Text,
        };

        // Fits the name on two short lines, breaking at a space when one is available.
        private static String FitName(String name)
        {
            if (name.Length <= LineLength)
            {
                return name;
            }

            var split = name.LastIndexOf(' ', LineLength);
            var first = split > 0 ? name.Substring(0, split) : name.Substring(0, LineLength);
            var second = name.Substring(first.Length).Trim();

            if (second.Length > LineLength)
            {
                second = second.Substring(0, LineLength - 1) + "…";
            }

            return first + Environment.NewLine + second;
        }
    }
}
