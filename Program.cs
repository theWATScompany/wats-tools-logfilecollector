using Serilog;
using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace LogFileCollector
{
    /// <summary>
    /// Application entry point. Resolves the configuration location, then runs
    /// <see cref="CollectorHost"/> either in the foreground (console / scheduled task)
    /// or as a Windows service (<c>--service</c>).
    ///
    /// Options:
    ///   --rescan          full scan of the source folder first, then keep watching
    ///   --reset           delete the tracking database before starting
    ///   --config &lt;path&gt;   use this appsettings.json instead of the ProgramData one
    ///   --service         run as a Windows service (started by the SCM, not by hand)
    /// </summary>
    internal class Program
    {
        static int Main(string[] args)
        {
            bool runAsService = args.Any(a => a.Equals("--service", StringComparison.OrdinalIgnoreCase));

            // --config <path> overrides the machine-wide location. Without it, the
            // config lives in ProgramData, which works for both service and user contexts.
            string configPath = GetArgValue(args, "--config");
            string dataDir;
            if (!string.IsNullOrWhiteSpace(configPath))
            {
                configPath = Path.GetFullPath(configPath);
                dataDir = Path.GetDirectoryName(configPath);
            }
            else
            {
                dataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Virinco", "WATS", "LogFileCollector");
                configPath = Path.Combine(dataDir, "appsettings.json");
            }
            Directory.CreateDirectory(dataDir);

            if (!File.Exists(configPath))
            {
                Console.Error.WriteLine("Config not found: " + configPath);
                return 2;
            }

            bool reset = args.Any(a => a.Equals("--reset", StringComparison.OrdinalIgnoreCase));
            bool rescan = args.Any(a => a.Equals("--rescan", StringComparison.OrdinalIgnoreCase));

            var host = new CollectorHost(configPath, dataDir, reset, rescan, consoleLogging: !runAsService);

            if (runAsService)
            {
#if NET48
                System.ServiceProcess.ServiceBase.Run(new CollectorService(host));
                return 0;
#else
                Console.Error.WriteLine("--service is only supported on the .NET Framework build (Windows).");
                return 3;
#endif
            }

            try
            {
                host.Start();

                if (host.Config.Dashboard.Enabled && host.Config.Dashboard.OpenBrowserOnStart)
                {
                    OpenBrowser(host.DashboardUrl);
                }

                // The collector has no window of its own. Say where the dashboard is,
                // so an operator who just launched it from the Start Menu knows where
                // to look rather than staring at a console that appears to do nothing.
                if (host.DashboardUrl != null)
                {
                    Log.Information("Dashboard: {Url}  (open it in a browser)", host.DashboardUrl);
                }

                Log.Information("Watcher running. Press Ctrl+C to stop.");
                ManualResetEvent exitEvent = new ManualResetEvent(false);
                Console.CancelKeyPress += (s, e) => { e.Cancel = true; exitEvent.Set(); };
                exitEvent.WaitOne();

                host.Stop();
                return 0;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Fatal error");
                return 1;
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        /// <summary>
        /// Reads "--name value" from the argument list. Returns null when absent.
        /// </summary>
        private static string GetArgValue(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }
            return null;
        }

        /// <summary>
        /// Opens the dashboard in the default browser. Best effort — a headless or
        /// service context has no browser and that is not an error.
        /// </summary>
        private static void OpenBrowser(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(url);
                psi.UseShellExecute = true;
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Could not open a browser for {Url} — open it manually.", url);
            }
        }
    }
}
