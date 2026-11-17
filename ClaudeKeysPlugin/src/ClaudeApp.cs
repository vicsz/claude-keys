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
                Run("/usr/bin/open", link);
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
                Run("/usr/bin/open", "-b", BundleId);
                Thread.Sleep(ActivationDelayMilliseconds);
            }

            return IsFront(BundleId);
        }

        public static Boolean IsFront(String bundleId)
        {
            var front = Run("/usr/bin/lsappinfo", "front").Trim();
            return Run("/usr/bin/lsappinfo", "info", "-only", "bundleid", front).Contains($"\"{bundleId}\"");
        }

        // Plays a built-in macOS sound without waiting for it to finish.
        public static void PlaySound(String name) =>
            Process.Start("/usr/bin/afplay", $"/System/Library/Sounds/{name}.aiff")?.Dispose();

        private static String Run(String command, params String[] arguments)
        {
            var start = new ProcessStartInfo(command)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
            };

            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start);

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return output;
        }
    }
}
