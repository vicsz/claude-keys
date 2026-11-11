namespace Loupedeck.ClaudeKeysPlugin
{
    // Starts or stops the mic in the Claude session on screen.

    public class DictateCommand : ClaudeShortcutCommand
    {
        public DictateCommand()
            : base("Dictate", "Starts or stops the mic in Claude", KeyArt.MicIcon, replySlot: 6)
        {
        }

        protected override void SendShortcut() =>
            this.Plugin.ClientApplication.SendKeyboardShortcut(VirtualKeyCode.KeyD, ModifierKey.Command);
    }
}
