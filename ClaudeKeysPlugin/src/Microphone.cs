namespace Loupedeck.ClaudeKeysPlugin
{
    using System;
    using System.Diagnostics;
    using System.Linq;
    using System.Runtime.InteropServices;

    // Asks Core Audio which apps are recording (macOS 14.2 or later). It never listens to the mic itself.

    public static class Microphone
    {
        private const String CoreAudio = "/System/Library/Frameworks/CoreAudio.framework/CoreAudio";
        private const UInt32 SystemObject = 1;

        [StructLayout(LayoutKind.Sequential)]
        private struct Address
        {
            public UInt32 Selector;
            public UInt32 Scope;
            public UInt32 Element;
        }

        [DllImport(CoreAudio)]
        private static extern Int32 AudioObjectGetPropertyDataSize(UInt32 obj, ref Address address, UInt32 qualifierSize, IntPtr qualifier, out UInt32 size);

        [DllImport(CoreAudio)]
        private static extern Int32 AudioObjectGetPropertyData(UInt32 obj, ref Address address, UInt32 qualifierSize, IntPtr qualifier, ref UInt32 size, IntPtr data);

        // True if an app whose process name starts with this is recording. False if Core Audio can't say.
        public static Boolean IsRecording(String processPrefix)
        {
            try
            {
                return GetArray(SystemObject, "prs#")
                    .Where(p => GetArray(p, "piri").FirstOrDefault() != 0)
                    .Select(p => (Int32)GetArray(p, "ppid").FirstOrDefault())
                    .Any(pid => ProcessName(pid).StartsWith(processPrefix, StringComparison.Ordinal));
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
            {
                PluginLog.Warning(ex, "Could not ask Core Audio which apps are recording");
                return false;
            }
        }

        private static String ProcessName(Int32 pid)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                return process.ProcessName;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
                // It exited between the two calls.
                return "";
            }
        }

        // Selectors are four-character codes: 'prs#' lists audio processes, 'piri' is "recording", 'ppid' is the PID.
        private static UInt32[] GetArray(UInt32 obj, String selector)
        {
            var address = new Address { Selector = (UInt32)(selector[0] << 24 | selector[1] << 16 | selector[2] << 8 | selector[3]), Scope = 0x676C6F62, Element = 0 };

            if (AudioObjectGetPropertyDataSize(obj, ref address, 0, IntPtr.Zero, out var size) != 0 || size == 0)
            {
                return Array.Empty<UInt32>();
            }

            var buffer = Marshal.AllocHGlobal((Int32)size);

            try
            {
                if (AudioObjectGetPropertyData(obj, ref address, 0, IntPtr.Zero, ref size, buffer) != 0)
                {
                    return Array.Empty<UInt32>();
                }

                var result = new UInt32[size / 4];

                for (var i = 0; i < result.Length; i++)
                {
                    result[i] = (UInt32)Marshal.ReadInt32(buffer, i * 4);
                }

                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }
}
