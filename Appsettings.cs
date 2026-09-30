using System;

namespace LogFileCollector
{
    /// <summary>
    /// Strongly typed configuration bound from appsettings.json.
    /// </summary>
    public class AppSettings
    {
        public string SourceFolder { get; set; }
        public string TargetFolder { get; set; }
        public string Filter { get; set; }
        public bool IncludeSubdirectories { get; set; }
        public int FileCreatedDelayMs { get; set; } // default 1000 ms (set in JSON)
        public string DatabasePath { get; set; }
        public string RenameStrategy { get; set; } // "counter" | "timestamp" | "guid"
        public int PeriodicRescanMinutes { get; set; } // 0 = disabled

        public LoggingSettings Logging { get; set; }
        public DashboardSettings Dashboard { get; set; }
    }

    /// <summary>
    /// Logging configuration (Serilog)
    /// </summary>
    public class LoggingSettings
    {
        public string LogFilePath { get; set; }
        public string LogOutputTemplate { get; set; }
        public string LogLevel { get; set; }
        public string RollingInterval { get; set; }
        public int RetainedFileCountLimit { get; set; }
    }

    /// <summary>
    /// The built-in web dashboard. The collector serves it itself over HTTP, so
    /// any browser on any OS can open it — there is no desktop UI to install and
    /// nothing extra to run alongside the collector.
    /// </summary>
    public class DashboardSettings
    {
        /// <summary>Serve the dashboard at all. Default true.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// HttpListener prefix. Keep the loopback default unless the dashboard is
        /// deliberately being exposed on the network — there is no authentication.
        /// </summary>
        public string UrlPrefix { get; set; } = "http://localhost:8787/";

        /// <summary>Launch the default browser at startup. Off for service/task runs.</summary>
        public bool OpenBrowserOnStart { get; set; }

        /// <summary>
        /// Allow the dashboard to write appsettings.json. Only ever honoured for
        /// requests arriving from the loopback address.
        /// </summary>
        public bool AllowConfigEdit { get; set; } = true;
    }
}
