namespace Loupedeck.ClaudeKeysPlugin
{
    using System;

    // Tap sends what is in the message box by pressing Enter. Hold opens the quick reply page.

    public class GoCommand : ClaudeShortcutCommand
    {
        private Boolean _held;

        public GoCommand()
            : base("Go", "Tap to press Enter in Claude. Hold for quick replies", KeyArt.SendIcon, replySlot: 7)
        {
        }

        protected override void SendShortcut() =>
            this.Plugin.ClientApplication.SendKeyboardShortcut(VirtualKeyCode.Return);

        // Tap acts on release, so that a hold never sends anything.
        protected override Boolean ProcessButtonEvent2(String actionParameter, DeviceButtonEvent2 buttonEvent)
        {
            switch (buttonEvent.EventType)
            {
                case DeviceButtonEventType.Press:
                    this._held = false;
                    return true;

                case DeviceButtonEventType.LongPress:
                    this._held = true;
                    ReplyMode.Enter();
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
