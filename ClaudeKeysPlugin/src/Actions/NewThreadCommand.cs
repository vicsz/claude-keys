namespace Loupedeck.ClaudeKeysPlugin
{
    using System;
    using System.Diagnostics;

    // Opens a new Claude session with Claude's own deep link.

    public class NewThreadCommand : PluginDynamicCommand
    {
        private const Int32 ReplySlot = 3;
        private const String NewSessionLink = "claude://code/new?source=desktop_action";

        public NewThreadCommand()
            : base(displayName: "New", description: "Opens a new Claude session", groupName: "Claude")
        {
            this.IsWidget = true;
        }

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

        private void Redraw() => this.ActionImageChanged();

        protected override void RunCommand(String actionParameter)
        {
            if (ReplyMode.TryPress(ReplySlot, this.Plugin.ClientApplication))
            {
                return;
            }

            try
            {
                var open = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
                open.ArgumentList.Add(NewSessionLink);
                Process.Start(open)?.Dispose();
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Could not open a new session");
            }
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize) =>
            ReplyMode.IsActive ? ReplyMode.Draw(ReplySlot, imageSize) : KeyArt.Control(imageSize, KeyArt.PlusIcon, "New");
    }
}
