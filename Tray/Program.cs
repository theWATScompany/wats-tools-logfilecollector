using System;
using System.Threading;
using System.Windows.Forms;

namespace LogFileCollector.Tray
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            // One tray icon per session. Without this, the Startup shortcut plus a
            // manual launch leaves two identical icons in the notification area.
            using (var single = new Mutex(true, @"Local\WATSLogFileCollectorTray", out bool isFirst))
            {
                if (!isFirst) return;

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                // --autostart is passed by the Run key / Startup shortcut. It only
                // exists so the setting can be turned on at install time without
                // launching a second copy here.
                if (Array.Exists(args, a => a.Equals("--enable-autostart", StringComparison.OrdinalIgnoreCase)))
                {
                    try { AutoStart.Set(true); } catch { /* non-fatal */ }
                }

                Application.Run(new TrayApplication());
            }
        }
    }
}
