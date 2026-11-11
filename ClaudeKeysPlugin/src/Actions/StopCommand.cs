namespace Loupedeck.ClaudeKeysPlugin
{
    using System;

    // Tap stops Claude by pressing Escape. Hold for ways to end the session, such as Archive.

    public class StopCommand : ClaudeShortcutCommand
    {
        private Boolean _held;

        public StopCommand()
            : base("Stop", "Tap to press Escape in Claude. Hold to archive the session", KeyArt.StopIcon, replySlot: 8)
        {
        }

        protected override void SendShortcut() =>
            this.Plugin.ClientApplication.SendKeyboardShortcut(VirtualKeyCode.Escape);

        // Tap acts on release, so that a hold never stops Claude as well.
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
