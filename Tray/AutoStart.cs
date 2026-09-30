using System;
using System.Reflection;
using Microsoft.Win32;

namespace LogFileCollector.Tray
{
    /// <summary>
    /// "Start with Windows" for the tray icon, via the per-user Run key.
    ///
    /// Per-user and not per-machine on purpose: the tray is UI, so it belongs to
    /// whoever is logged in, and writing HKCU needs no elevation. Collecting itself
    /// does not depend on this — the service starts at boot regardless of whether
    /// anyone logs in.
    /// </summary>
    internal static class AutoStart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "WATSLogFileCollectorTray";

        private static string ExecutablePath
        {
            get { return "\"" + Assembly.GetExecutingAssembly().Location + "\""; }
        }

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    return key?.GetValue(ValueName) != null;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Flips the setting and returns the new state.</summary>
        public static bool Toggle()
        {
            bool enable = !IsEnabled();
            Set(enable);
            return enable;
        }

        public static void Set(bool enable)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (key == null) throw new InvalidOperationException("The Run registry key is not available.");
                if (enable)
                {
                    key.SetValue(ValueName, ExecutablePath, RegistryValueKind.String);
                }
                else
                {
                    key.DeleteValue(ValueName, false);
                }
            }
        }
    }
}
