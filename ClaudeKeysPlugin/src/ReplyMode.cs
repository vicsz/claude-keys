namespace Loupedeck.ClaudeKeysPlugin
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    // Holding Return turns the whole keypad into quick replies, and holding Cancel into ways to end the session. Every key is ours, so each one
    // redraws itself as a reply and acts as one, then goes back to normal.
    // Slots run left to right, top to bottom, and match the key layout.

    // Archive is null for sessions it can't archive, and then its key stays dark.
    public record SessionKeyActions(Action PinCurrent, Action Unpin, Action Archive = null);

    public static class ReplyMode
    {
        private const Int32 CancelSlot = 8;
        private const Int32 TimeoutMilliseconds = 10000;

        private record Reply(String Icon, String Label, String Message, Action<ClientApplication> Run = null);

        // Everything here acts on the session on screen. The top row says where what you typed goes: New sends
        // it to a new session, Side chat to a side chat that keeps the thread clean, and Fork to a fork of this one.
        // Yes presses the main button of Claude's next-step box, or sends what is typed when there is no box.
        // Slots without a reply stay dark.
        private static readonly IReadOnlyList<Reply> Replies = new[]
        {
            new Reply(KeyArt.NewIcon, "New", null, Run: client => Task.Run(() => ClaudeApp.SendToNewSession(client))),
            new Reply(KeyArt.SideChatIcon, "Side chat", null, Run: client => Task.Run(() => ClaudeApp.SendAsSideChat(client))),
            new Reply(KeyArt.ForkIcon, "Fork", null, Run: client => Task.Run(() => ClaudeApp.SendToFork(client))),
            new Reply(KeyArt.CheckIcon, "Yes", null, Run: client => Press(client, VirtualKeyCode.Return, ModifierKey.Command)),
            new Reply(KeyArt.CommitIcon, "Commit", "Let's do a commit"),
            null,
            null,
            null,
            new Reply(KeyArt.CancelIcon, "Cancel", null),
        };

        private static IReadOnlyList<Reply> page = Replies;

        private static readonly Timer Timeout = new Timer(_ => Exit());

        public static event Action Changed;

        public static Boolean IsActive { get; private set; }

        public static void Enter() => Open(Replies);

        // The page for one session key. Only the keys with a job light up.
        public static void EnterSessionPage(SessionKeyActions actions)
        {
            var replies = new Reply[Replies.Count];

            replies[0] = new Reply(KeyArt.PinHereIcon, "Pin here", null, Run: _ => actions.PinCurrent());
            replies[1] = new Reply(KeyArt.UnpinIcon, "Unpin", null, Run: _ => actions.Unpin());

            if (actions.Archive != null)
            {
                replies[2] = new Reply(KeyArt.ArchiveIcon, "Archive", null, Run: _ => Task.Run(actions.Archive));
            }

            replies[CancelSlot] = Replies[CancelSlot];
            Open(replies);
        }

        // Ending the session on screen.
        public static void EnterStopPage()
        {
            var replies = new Reply[Replies.Count];

            replies[0] = new Reply(KeyArt.ArchiveIcon, "Archive", null, Run: client => Shortcut(client, VirtualKeyCode.KeyA));
            replies[CancelSlot] = Replies[CancelSlot];
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

            if (reply.Message != null)
            {
                Task.Run(() => ClaudeApp.SendMessage(client, reply.Message));
            }

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
