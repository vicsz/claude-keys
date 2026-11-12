namespace Loupedeck.ClaudeKeysPlugin
{
    using System;
    using System.Diagnostics;
    using System.Text.RegularExpressions;
    using System.Threading;

    // Everything that acts on the Claude app window.

    public static class ClaudeApp
    {
        public const String BundleId = "com.anthropic.claudefordesktop";
        private const Int32 ActivationDelayMilliseconds = 150;
        private const Int32 ClipboardDelayMilliseconds = 60;
        private const Int32 PasteDelayMilliseconds = 200;
        private const Int32 NewSessionDelayMilliseconds = 800;
        private const Int32 ForkDelayMilliseconds = 1500;

        private static readonly Regex ValidBridgeId = new Regex("^session_[A-Za-z0-9]+$");
        private static readonly Regex ValidLocalId = new Regex("^local_[A-Za-z0-9-]{1,64}$");

        // Bridge IDs exist only while Remote Control is on, so the local link is the fallback.
        public static void OpenSession(String bridgeId, String localId)
        {
            String link;

            if (bridgeId != null && ValidBridgeId.IsMatch(bridgeId))
            {
                link = $"claude://claude.ai/code/{bridgeId}";
            }
            else if (localId != null && ValidLocalId.IsMatch(localId))
            {
                link = $"claude://code/continue?session={localId}&source=desktop_action";
            }
            else
            {
                PluginLog.Warning($"No usable ID to open the session: {localId}");
                return;
            }

            try
            {
                Run("/usr/bin/open", null, link);
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Could not open the session");
            }
        }

        // Keys mean something else in other apps, so nothing is sent unless Claude is in front.
        public static Boolean BringToFront()
        {
            if (!IsFront(BundleId))
            {
                Run("/usr/bin/open", null, "-b", BundleId);
                Thread.Sleep(ActivationDelayMilliseconds);
            }

            return IsFront(BundleId);
        }

        // Pastes the text into the message box and sends it.
        public static void SendMessage(ClientApplication client, String text)
        {
            if (!BringToFront())
            {
                PluginLog.Warning("Claude did not come to the front, so the reply was not sent");
                return;
            }

            PasteAndEnter(client, text);
        }

        // Sends what you have typed or dictated to a side chat, by putting /btw in front of it.
        public static void SendAsSideChat(ClientApplication client)
        {
            if (!BringToFront())
            {
                PluginLog.Warning("Claude did not come to the front, so the side chat was not sent");
                return;
            }

            client.SendKeyboardShortcut(VirtualKeyCode.ArrowUp, ModifierKey.Command);
            Thread.Sleep(ClipboardDelayMilliseconds);
            PasteAndEnter(client, "/btw ");
        }

        // Moves what you have typed or dictated into a new session, or a fork of this one, and sends it there.
        public static void SendToNewSession(ClientApplication client) =>
            MoveTextTo(client, VirtualKeyCode.KeyN, ModifierKey.Command, NewSessionDelayMilliseconds, "new session");

        public static void SendToFork(ClientApplication client) =>
            MoveTextTo(client, VirtualKeyCode.KeyO, ModifierKey.Command | ModifierKey.Alt, ForkDelayMilliseconds, "fork");

        // The clipboard is emptied first, so an empty box, or focus outside it, only opens the new session.
        private static void MoveTextTo(ClientApplication client, VirtualKeyCode key, ModifierKey modifiers, Int32 delay, String destination)
        {
            if (!BringToFront())
            {
                PluginLog.Warning($"Claude did not come to the front, so no {destination} was started");
                return;
            }

            var clipboard = Run("/usr/bin/pbpaste", null);
            Run("/usr/bin/pbcopy", "");
            client.SendKeyboardShortcut(VirtualKeyCode.KeyA, ModifierKey.Command);
            Thread.Sleep(ClipboardDelayMilliseconds);
            client.SendKeyboardShortcut(VirtualKeyCode.KeyX, ModifierKey.Command);
            Thread.Sleep(ClipboardDelayMilliseconds);
            var text = Run("/usr/bin/pbpaste", null);

            client.SendKeyboardShortcut(key, modifiers);
            Thread.Sleep(delay);

            if (!String.IsNullOrWhiteSpace(text))
            {
                PasteAndEnter(client, text);
            }

            Run("/usr/bin/pbcopy", clipboard);
        }

        // Pastes the text, presses Enter, then puts the clipboard text back.
        private static void PasteAndEnter(ClientApplication client, String text)
        {
            var clipboard = Run("/usr/bin/pbpaste", null);

            Run("/usr/bin/pbcopy", text);
            Thread.Sleep(ClipboardDelayMilliseconds);
            client.SendKeyboardShortcut(VirtualKeyCode.KeyV, ModifierKey.Command);
            Thread.Sleep(PasteDelayMilliseconds);
            client.SendKeyboardShortcut(VirtualKeyCode.Return);
            Thread.Sleep(PasteDelayMilliseconds);
            Run("/usr/bin/pbcopy", clipboard);
        }

        public static Boolean IsFront(String bundleId)
        {
            var front = Run("/usr/bin/lsappinfo", null, "front").Trim();
            return Run("/usr/bin/lsappinfo", null, "info", "-only", "bundleid", front).Contains($"\"{bundleId}\"");
        }

        private static String Run(String command, String input, params String[] arguments)
        {
            var start = new ProcessStartInfo(command)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardInput = input != null,
            };

            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start);

            if (input != null)
            {
                process.StandardInput.Write(input);
                process.StandardInput.Close();
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return output;
        }
    }
}
