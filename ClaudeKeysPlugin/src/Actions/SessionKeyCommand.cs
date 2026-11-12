namespace Loupedeck.ClaudeKeysPlugin
{
    using System;
    using System.Threading;

    // A key you lock to any session. Tap an empty key to lock the session on screen, tap a locked
    // key to open its session, and hold a locked key to pin it here, unpin it or archive it. Keys 1 to 6 fill the top two
    // rows in order, so each key's number is also its position on the reply page.

    public class SessionKeyCommand : PluginDynamicCommand
    {
        private const Int32 KeyCount = 6;
        private const Int32 ArchiveSwitchLimitMilliseconds = 1500;
        private const Int32 ArchiveSettleMilliseconds = 300;
        private const Int32 ArchiveCheckMilliseconds = 500;
        private const Int32 ArchiveAttempts = 3;
        private const Int32 DotCount = 3;
        private const Int32 DotMilliseconds = 500;

        private SessionKeyLocks _locks;
        private Timer _dots;
        private Int32 _litDot;
        private Boolean _held;

        public SessionKeyCommand()
            : base()
        {
            this.IsWidget = true;

            for (var key = 0; key < KeyCount; key++)
            {
                this.AddParameter(key.ToString(), $"Session key {key + 1}", "Session keys");
            }
        }

        private ClaudeSessionStore Sessions => ((ClaudeKeysPlugin)this.Plugin).Sessions;

        protected override Boolean OnLoad()
        {
            this._locks = new SessionKeyLocks(this.Plugin.GetPluginDataDirectory(), KeyCount);
            this.Sessions.Changed += this.OnSessionsChanged;
            ReplyMode.Changed += this.RedrawAll;
            this._dots = new Timer(_ => this.AdvanceDots(), null, DotMilliseconds, DotMilliseconds);
            return true;
        }

        protected override Boolean OnUnload()
        {
            this.Sessions.Changed -= this.OnSessionsChanged;
            ReplyMode.Changed -= this.RedrawAll;
            this._dots?.Dispose();
            return true;
        }

        // Tap acts on release, so that a hold never opens the session.
        protected override Boolean ProcessButtonEvent2(String actionParameter, DeviceButtonEvent2 buttonEvent)
        {
            switch (buttonEvent.EventType)
            {
                case DeviceButtonEventType.Press:
                    this._held = false;
                    return true;

                case DeviceButtonEventType.LongPress:
                    this._held = true;
                    this.Hold(actionParameter);
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

        protected override void RunCommand(String actionParameter)
        {
            if (!Int32.TryParse(actionParameter, out var key) || ReplyMode.TryPress(key, this.Plugin.ClientApplication))
            {
                return;
            }

            var session = this.GetSession(key);

            if (session != null)
            {
                if (session.IsCodex)
                {
                    CodexThreads.Open(session.Id);
                }
                else
                {
                    ClaudeApp.OpenSession(session.BridgeId, session.Id);
                }

                this.Sessions.MarkSeen(session.Id);
            }
            else
            {
                this.LockCurrent(key);
            }
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            if (!Int32.TryParse(actionParameter, out var key))
            {
                return KeyArt.Empty(imageSize);
            }

            if (ReplyMode.IsActive)
            {
                return ReplyMode.Draw(key, imageSize);
            }

            var session = this.GetSession(key);
            return session == null ? KeyArt.EmptySessionKey(imageSize) : KeyArt.Session(imageSize, session, this._litDot);
        }

        private SessionInfo GetSession(Int32 key) => this.Sessions.Get(this._locks?.Get(key));

        private void Hold(String actionParameter)
        {
            if (!Int32.TryParse(actionParameter, out var key))
            {
                return;
            }

            var session = this.GetSession(key);

            if (session == null)
            {
                this.LockCurrent(key);
                return;
            }

            ReplyMode.EnterSessionPage(new SessionKeyActions(
                PinCurrent: () => this.LockCurrent(key),
                Unpin: () => this.Clear(key),
                Archive: session.IsCodex ? null : () => this.Archive(session)));
        }

        // Claude's archive shortcut acts on the session on screen, so this opens the key's session first and
        // archives only once Claude confirms it's showing. If it doesn't show in time, nothing is archived.
        // Claude says a session is showing before its page can take shortcuts, so a freshly opened session
        // gets a moment to settle, and the shortcut is pressed again until Claude's log shows the archive.
        // Every press first checks the session is still on screen. The key then clears itself, since an
        // archived session is gone from the list.
        private void Archive(SessionInfo session)
        {
            Boolean IsOnScreen() => ClaudeApp.IsFront(ClaudeApp.BundleId) && this.Sessions.FocusedNow()?.Id == session.Id;

            if (!IsOnScreen())
            {
                ClaudeApp.OpenSession(session.BridgeId, session.Id);
                var deadline = DateTime.UtcNow.AddMilliseconds(ArchiveSwitchLimitMilliseconds);

                while (!IsOnScreen())
                {
                    if (DateTime.UtcNow > deadline)
                    {
                        PluginLog.Warning($"'{session.Name}' did not come on screen, so it was not archived");
                        return;
                    }

                    Thread.Sleep(100);
                }

                Thread.Sleep(ArchiveSettleMilliseconds);
            }

            for (var attempt = 1; attempt <= ArchiveAttempts; attempt++)
            {
                if (!IsOnScreen())
                {
                    PluginLog.Warning($"'{session.Name}' left the screen, so it was not archived");
                    return;
                }

                var logEnd = ClaudeSessionStore.AppLogLength();
                this.Plugin.ClientApplication.SendKeyboardShortcut(VirtualKeyCode.KeyA, ModifierKey.Command | ModifierKey.Alt);
                Thread.Sleep(ArchiveCheckMilliseconds);

                if (ClaudeSessionStore.WasArchivedSince(logEnd, session.Id))
                {
                    PluginLog.Info($"Archived '{session.Name}' on press {attempt}");
                    return;
                }
            }

            PluginLog.Warning($"Claude did not archive '{session.Name}' after {ArchiveAttempts} presses");
        }

        private void LockCurrent(Int32 key)
        {
            var current = this.Sessions.FocusedNow();

            if (current != null)
            {
                this._locks.Lock(key, current.Id);
                this.RedrawAll();
            }
        }

        private void Clear(Int32 key)
        {
            this._locks.Clear(key);
            this.RedrawAll();
        }

        private void OnSessionsChanged()
        {
            this.StartFromPins();
            this.ClearGoneSessions();
            this.RedrawAll();
        }

        // On first use, the keys start with your Claude pins. After that they are yours.
        private void StartFromPins()
        {
            if (!this._locks.IsNew)
            {
                return;
            }

            for (var key = 0; key < KeyCount; key++)
            {
                var pin = this.Sessions.GetPin(key);

                if (pin != null)
                {
                    this._locks.Lock(key, pin.Id);
                }
            }

            this._locks.Save();
        }

        private void ClearGoneSessions()
        {
            for (var key = 0; key < KeyCount; key++)
            {
                if (this._locks.Get(key) is String id && this.Sessions.IsGone(id))
                {
                    this._locks.Clear(key);
                }
            }
        }

        private void RedrawAll()
        {
            for (var key = 0; key < KeyCount; key++)
            {
                this.ActionImageChanged(key.ToString());
            }
        }

        private void AdvanceDots()
        {
            this._litDot = (this._litDot + 1) % DotCount;

            for (var key = 0; key < KeyCount; key++)
            {
                var session = this.GetSession(key);

                if (session?.Status is SessionStatus.Busy or SessionStatus.Background)
                {
                    this.ActionImageChanged(key.ToString());
                }
            }
        }
    }
}
