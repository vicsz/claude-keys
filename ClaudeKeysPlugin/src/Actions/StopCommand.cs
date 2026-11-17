namespace Loupedeck.ClaudeKeysPlugin
{
    using System;

    // Tap presses Escape in whatever app is in front: it stops dictation or a running turn. Hold for ways to end the session, such as Archive.

    public class StopCommand : ClaudeShortcutCommand
    {
        private Boolean _held;

        public StopCommand()
            : base("Cancel", "Tap to press Escape in the app in front: stops dictation or a running turn. Hold to archive the Claude session", KeyArt.CancelIcon, replySlot: 8)
        {
        }

        protected override Boolean SwitchesToClaude => false;

        protected override void SendShortcut() =>
            DictateCommand.PressAndSoundIfStopping(() => this.Plugin.ClientApplication.SendKeyboardShortcut(VirtualKeyCode.Escape));

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
