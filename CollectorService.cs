#if NET48
using System;
using System.ServiceProcess;
using Serilog;

namespace LogFileCollector
{
    /// <summary>
    /// Windows service wrapper around <see cref="CollectorHost"/>.
    ///
    /// The service is what actually collects files: it starts at boot, before anyone
    /// logs in, and keeps running when they log out — which a scheduled task tied to a
    /// user session does not reliably do. It also serves the dashboard, so the tray
    /// application in the user's session is a thin client over HTTP rather than a
    /// second copy of the collector.
    ///
    /// A service cannot show UI (session 0 isolation), which is exactly why the tray
    /// application exists as a separate process.
    /// </summary>
    internal class CollectorService : ServiceBase
    {
        public const string ServiceName_ = "WATSLogFileCollector";

        private readonly CollectorHost _host;

        public CollectorService(CollectorHost host)
        {
            _host = host;
            ServiceName = ServiceName_;
            CanStop = true;
            CanShutdown = true;
            CanPauseAndContinue = false;
            AutoLog = true;
        }

        protected override void OnStart(string[] args)
        {
            try
            {
                _host.Start();
            }
            catch (Exception ex)
            {
                // Surface the reason in the log and in the event log, then fail the
                // start rather than sitting there "running" while collecting nothing.
                Log.Fatal(ex, "Service failed to start");
                Log.CloseAndFlush();
                ExitCode = 1;
                throw;
            }
        }

        protected override void OnStop()
        {
            Shutdown();
        }

        protected override void OnShutdown()
        {
            Shutdown();
        }

        private void Shutdown()
        {
            try
            {
                _host.Stop();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during service shutdown");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}
#endif
