namespace Loupedeck.ClaudeKeysPlugin
{
    using System;

    // Tap cancels by pressing Escape: it stops dictation or a running turn. Hold for ways to end the session, such as Archive.

    public class StopCommand : ClaudeShortcutCommand
    {
        private Boolean _held;

        public StopCommand()
            : base("Cancel", "Tap to cancel in Claude (Escape): stops dictation or a running turn. Hold to archive the session", KeyArt.CancelIcon, replySlot: 8)
        {
        }

        protected override void SendShortcut() =>
            this.Plugin.ClientApplication.SendKeyboardShortcut(VirtualKeyCode.Escape);

        // Tap acts on release, so that a hold never cancels as well.
        protected override Boolean ProcessButtonEvent2(String actionParameter, DeviceButtonEvent2 buttonEvent)
        {
            switch (buttonEvent.EventType)
            {
                case DeviceButtonEventType.Press:
                    this._held = false;
                    return true;

                case DeviceButtonEventType.LongPress:
                    if (!ReplyMode.IsActive)
                    {
                        this._held = true;
                        ReplyMode.EnterStopPage();
                    }

                    return true;

                case DeviceButtonEventType.Release:
                    if (!this._held)
                    {
                        this.RunCommand(actionParameter);
                    }

                    return true;

                default:
                    return true;
            }
        }
    }
}
