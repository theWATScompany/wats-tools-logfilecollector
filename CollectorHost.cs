using Serilog;
using Serilog.Events;
using System;
using System.IO;
using System.Threading;

namespace LogFileCollector
{
    /// <summary>
    /// Everything the collector does, independent of how it was started.
    /// Console mode, Windows service mode and the scheduled task all drive this
    /// same object, so there is exactly one startup path to reason about.
    /// </summary>
    public class CollectorHost : IDisposable
    {
        private readonly string _configPath;
        private readonly string _dataDir;
        private readonly bool _resetDatabase;
        private readonly bool _rescanOnStart;
        private readonly bool _consoleLogging;

        private Timer _periodicRescanTimer;
        private WebServer _webServer;
        private FileProcessor _processor;

        public AppSettings Config { get; private set; }

        /// <summary>Dashboard address a human can actually click, once Start() has run.</summary>
        public string DashboardUrl
        {
            get
            {
                if (Config == null || Config.Dashboard == null || !Config.Dashboard.Enabled) return null;
                return Config.Dashboard.UrlPrefix.Replace("+", "localhost").Replace("*", "localhost");
            }
        }

        public CollectorHost(string configPath, string dataDir, bool resetDatabase, bool rescanOnStart, bool consoleLogging)
        {
            _configPath = configPath;
            _dataDir = dataDir;
            _resetDatabase = resetDatabase;
            _rescanOnStart = rescanOnStart;
            _consoleLogging = consoleLogging;
        }

        /// <summary>
        /// Loads configuration, configures logging, and starts the dashboard, the
        /// watcher and the periodic rescan. Throws if the configuration is unusable —
        /// the caller decides whether that is a console error or a failed service start.
        /// </summary>
        public void Start()
        {
            Config = ConfigLoader.Load(_configPath);

            // Relative DB/log paths live beside the config; absolute and UNC paths are used as-is.
            Func<string, string> resolveInData = p => Path.IsPathRooted(p) ? p : Path.Combine(_dataDir, p);
            string dbPath = resolveInData(Config.DatabasePath);
            string logPath = resolveInData(Config.Logging.LogFilePath);

            ConfigureLogging(logPath);

            Log.Information("LogFileCollector starting.");

            if (_resetDatabase && File.Exists(dbPath))
            {
                File.Delete(dbPath);
                Log.Warning("Database reset: {Db}", dbPath);
            }

            Database db = new Database(dbPath);
            _processor = new FileProcessor(Config, db);

            if (Config.Dashboard.Enabled)
            {
                _webServer = new WebServer(Config, _configPath, db, _processor, logPath);
                _webServer.Start();
            }
            else
            {
                Log.Information("Dashboard disabled by configuration.");
            }

            if (_rescanOnStart)
            {
                Log.Information("Rescan starting (Filter={Filter}, Subdirs={Subdirs})",
                    Config.Filter, Config.IncludeSubdirectories);
                _processor.ProcessAllFiles();
                Log.Information("Rescan completed. Run={RunStats} Total={TotalStats}", _processor.RunStats, _processor.TotalStats);
                Log.Information("Switching to watcher mode...");
            }

            _processor.StartWatching();

            // Periodic rescan is the safety net for file events the watcher missed.
            if (Config.PeriodicRescanMinutes > 0)
            {
                TimeSpan rescanInterval = TimeSpan.FromMinutes(Config.PeriodicRescanMinutes);
                _periodicRescanTimer = new Timer(state =>
                {
                    try
                    {
                        Log.Information("Starting periodic rescan (every {Minutes} minutes)...", Config.PeriodicRescanMinutes);
                        _processor.ProcessAllFiles();
                        Log.Information("Periodic rescan completed. Run={RunStats} Total={TotalStats}", _processor.RunStats, _processor.TotalStats);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Error during periodic rescan");
                    }
                }, null, rescanInterval, rescanInterval);

                Log.Information("Periodic rescan enabled (every {Minutes} minutes)", Config.PeriodicRescanMinutes);
            }
        }

        private void ConfigureLogging(string logPath)
        {
            LogEventLevel minLevel;
            switch ((Config.Logging.LogLevel ?? "Information").Trim().ToLowerInvariant())
            {
                case "debug": minLevel = LogEventLevel.Debug; break;
                case "warning": minLevel = LogEventLevel.Warning; break;
                case "error": minLevel = LogEventLevel.Error; break;
                case "verbose": minLevel = LogEventLevel.Verbose; break;
                default: minLevel = LogEventLevel.Information; break;
            }

            RollingInterval interval;
            switch ((Config.Logging.RollingInterval ?? "Day").Trim().ToLowerInvariant())
            {
                case "hour": interval = RollingInterval.Hour; break;
                case "month": interval = RollingInterval.Month; break;
                case "year": interval = RollingInterval.Year; break;
                case "infinite": interval = RollingInterval.Infinite; break;
                default: interval = RollingInterval.Day; break;
            }

            var logger = new LoggerConfiguration().MinimumLevel.Is(minLevel);

            // A service has no console; writing to one costs nothing but is noise in
            // the trace, and on some hosts throws.
            if (_consoleLogging)
            {
                logger = logger.WriteTo.Console(outputTemplate: Config.Logging.LogOutputTemplate);
            }

            Log.Logger = logger
                .WriteTo.File(
                    path: logPath,
                    outputTemplate: Config.Logging.LogOutputTemplate,
                    rollingInterval: interval,
                    retainedFileCountLimit: Config.Logging.RetainedFileCountLimit)
                .CreateLogger();
        }

        public void Stop()
        {
            if (_periodicRescanTimer != null)
            {
                _periodicRescanTimer.Dispose();
                _periodicRescanTimer = null;
            }
            if (_webServer != null)
            {
                _webServer.Dispose();
                _webServer = null;
            }
            if (_processor != null)
            {
                Log.Information("Shutdown. Final statistics: Total={TotalStats}", _processor.TotalStats);
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
