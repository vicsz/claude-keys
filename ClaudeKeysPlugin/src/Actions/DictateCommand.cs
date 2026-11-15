namespace Loupedeck.ClaudeKeysPlugin
{
    using System;

    // Starts or stops dictation in the app in front, without switching.
    // In Claude it's Claude's mic (Cmd+D). Anywhere else it's ChatGPT's single-tap dictation shortcut,
    // which has to be set to Ctrl+Opt+Cmd+D in ChatGPT's settings.

    public class DictateCommand : ClaudeShortcutCommand
    {
        public DictateCommand()
            : base("Dictate", "Starts or stops dictation: Claude's mic in Claude, ChatGPT's dictation anywhere else", KeyArt.MicIcon, replySlot: 6)
        {
        }

        protected override Boolean SwitchesToClaude => false;

        protected override void SendShortcut()
        {
            if (ClaudeApp.IsFront(ClaudeApp.BundleId))
            {
                this.Plugin.ClientApplication.SendKeyboardShortcut(VirtualKeyCode.KeyD, ModifierKey.Command);
            }
            else
            {
                this.Plugin.ClientApplication.SendKeyboardShortcut(VirtualKeyCode.KeyD, ModifierKey.Control | ModifierKey.Alt | ModifierKey.Command);
            }
        }
    }
}
