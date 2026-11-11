namespace Loupedeck.ClaudeKeysPlugin
{
    using System;
    using System.Threading.Tasks;

    // A key that presses one keyboard shortcut in Claude.
    // These keys mean something else in other apps, so they are only sent when Claude is in front.

    public abstract class ClaudeShortcutCommand : PluginDynamicCommand
    {
        private static readonly TimeSpan RepeatPressWindow = TimeSpan.FromMilliseconds(300);

        private readonly String _label;
        private readonly String _icon;
        private readonly Int32 _replySlot;
        private DateTime _lastPress = DateTime.MinValue;

        protected ClaudeShortcutCommand(String label, String description, String icon, Int32 replySlot)
            : base(displayName: label, description: description, groupName: "Claude")
        {
            this._label = label;
            this._icon = icon;
            this._replySlot = replySlot;
            this.IsWidget = true;
        }

        protected abstract void SendShortcut();

        protected override Boolean OnLoad()
        {
            ReplyMode.Changed += this.Redraw;
            return true;
        }

        protected override Boolean OnUnload()
        {
            ReplyMode.Changed -= this.Redraw;
            return true;
        }

        protected override void RunCommand(String actionParameter)
        {
            if (ReplyMode.TryPress(this._replySlot, this.Plugin.ClientApplication))
            {
                return;
            }

            var now = DateTime.UtcNow;

            if (now - this._lastPress < RepeatPressWindow)
            {
                return;
            }

            this._lastPress = now;
            Task.Run(this.SendToClaude);
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize) =>
            ReplyMode.IsActive ? ReplyMode.Draw(this._replySlot, imageSize) : KeyArt.Control(imageSize, this._icon, this._label);

        private void Redraw() => this.ActionImageChanged();

        private void SendToClaude()
        {
            try
            {
                if (!ClaudeApp.BringToFront())
                {
                    PluginLog.Warning($"Claude did not come to the front, so {this._label} was not sent");
                    return;
                }

                this.SendShortcut();
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, $"Could not send {this._label}");
            }
        }
    }
}
