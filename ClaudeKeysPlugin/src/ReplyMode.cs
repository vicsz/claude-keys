namespace Loupedeck.ClaudeKeysPlugin
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    // Holding a session key or Cancel turns the whole keypad into a page of buttons. Every key is ours, so each one
    // redraws itself as a button and acts as one, then goes back to normal.
    // Slots run left to right, top to bottom, and match the key layout.

    public record SessionKeyActions(Action PinCurrent, Action Unpin);

    public static class ReplyMode
    {
        private const Int32 CancelSlot = 8;
        private const Int32 TimeoutMilliseconds = 10000;

        private record Reply(String Icon, String Label, Action<ClientApplication> Run = null);

        private const Int32 SlotCount = 9;
        private static readonly Reply Close = new Reply(KeyArt.CancelIcon, "Cancel");

        private static IReadOnlyList<Reply> page = new Reply[SlotCount];

        private static readonly Timer Timeout = new Timer(_ => Exit());

        public static event Action Changed;

        public static Boolean IsActive { get; private set; }

        // The page for one session key. Only the keys with a job light up.
        public static void EnterSessionPage(SessionKeyActions actions)
        {
            var replies = new Reply[SlotCount];

            replies[0] = new Reply(KeyArt.PinHereIcon, "Pin here", Run: _ => actions.PinCurrent());
            replies[1] = new Reply(KeyArt.UnpinIcon, "Unpin", Run: _ => actions.Unpin());

            replies[CancelSlot] = Close;
            Open(replies);
        }

        // Ending the session on screen.
        public static void EnterStopPage()
        {
            var replies = new Reply[SlotCount];

            replies[0] = new Reply(KeyArt.ArchiveIcon, "Archive", Run: client => Shortcut(client, VirtualKeyCode.KeyA));
            replies[CancelSlot] = Close;
            Open(replies);
        }

        private static void Open(IReadOnlyList<Reply> replies)
        {
            page = replies;
            IsActive = true;
            Timeout.Change(TimeoutMilliseconds, System.Threading.Timeout.Infinite);
            Changed?.Invoke();
        }

        public static void Exit()
        {
            Timeout.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            IsActive = false;
            Changed?.Invoke();
        }

        // Returns true when the press was a reply, so the key skips its normal job.
        public static Boolean TryPress(Int32 slot, ClientApplication client)
        {
            if (!IsActive)
            {
                return false;
            }

            var reply = page[slot];

            if (reply == null)
            {
                return true;
            }

            Exit();
            reply.Run?.Invoke(client);

            return true;
        }

        // Cmd+Opt plus a letter: Claude's session shortcuts.
        private static void Shortcut(ClientApplication client, VirtualKeyCode key) => Press(client, key, ModifierKey.Command | ModifierKey.Alt);

        // Sent only once Claude is in front.
        private static void Press(ClientApplication client, VirtualKeyCode key, ModifierKey modifiers) => Task.Run(() =>
        {
            if (ClaudeApp.BringToFront())
            {
                client.SendKeyboardShortcut(key, modifiers);
            }
        });

        public static BitmapImage Draw(Int32 slot, PluginImageSize imageSize)
        {
            var reply = page[slot];

            if (reply == null)
            {
                return KeyArt.Empty(imageSize);
            }

            return KeyArt.Reply(imageSize, reply.Icon, reply.Label, slot == CancelSlot);
        }
    }
}
