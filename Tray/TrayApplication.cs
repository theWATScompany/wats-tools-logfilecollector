using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.ServiceProcess;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LogFileCollector.Tray
{
    /// <summary>
    /// The tray icon and its menu.
    ///
    /// This process holds no collector logic. The service owns the watcher, the
    /// database and the dashboard; the tray polls the dashboard's own HTTP API over
    /// loopback and renders the answer as a tooltip and a menu. That means there is
    /// no IPC to maintain, and the tray shows exactly what the browser dashboard
    /// would show.
    /// </summary>
    internal class TrayApplication : ApplicationContext
    {
        private const string ServiceName = "WATSLogFileCollector";
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

        private readonly NotifyIcon _icon;
        private readonly System.Windows.Forms.Timer _timer;
        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

        private readonly ToolStripMenuItem _statusItem;
        private readonly ToolStripMenuItem _openDashboardItem;
        private readonly ToolStripMenuItem _rescanItem;
        private readonly ToolStripMenuItem _settingsItem;
        private readonly ToolStripMenuItem _serviceItem;

        private string _dashboardUrl;
        private bool _online;
        private DashboardWindow _window;

        public TrayApplication()
        {
            _dashboardUrl = ReadDashboardUrlFromConfig();

            _statusItem = new ToolStripMenuItem("Checking...") { Enabled = false };
            _openDashboardItem = new ToolStripMenuItem("Open dashboard", null, (s, e) => OpenDashboard());
            _rescanItem = new ToolStripMenuItem("Rescan now", null, async (s, e) => await RescanAsync());
            _settingsItem = new ToolStripMenuItem("Settings...", null, (s, e) => OpenSettings());
            _serviceItem = new ToolStripMenuItem("Start service", null, (s, e) => ToggleService());

            var menu = new ContextMenuStrip
            {
                Renderer = new WatsMenuRenderer(),
                BackColor = WatsColors.Background,
                ForeColor = WatsColors.Foreground,
                ShowImageMargin = false,
                Font = new Font("Segoe UI", 9f)
            };
            menu.Items.Add(_statusItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_openDashboardItem);
            menu.Items.Add(_settingsItem);
            menu.Items.Add(_rescanItem);
            menu.Items.Add(new ToolStripMenuItem("Open in browser", null, (s, e) => OpenInBrowser()));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_serviceItem);
            menu.Items.Add(new ToolStripMenuItem("Restart service", null, (s, e) => RestartService()));
            menu.Items.Add(new ToolStripMenuItem("Start with Windows", null, (s, e) => ToggleAutoStart())
            {
                Name = "autostart",
                CheckOnClick = true,
                Checked = AutoStart.IsEnabled()
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Exit", null, (s, e) => ExitThread()));

            _icon = new NotifyIcon
            {
                Icon = LoadAppIcon(),
                Text = "WATS Log File Collector",
                Visible = true,
                ContextMenuStrip = menu
            };
            _icon.DoubleClick += (s, e) => OpenDashboard();

            _timer = new System.Windows.Forms.Timer { Interval = (int)PollInterval.TotalMilliseconds };
            _timer.Tick += async (s, e) => await RefreshAsync();
            _timer.Start();

            // Don't wait a full interval before the first answer.
            _ = RefreshAsync();
        }

        internal static Icon LoadAppIcon()
        {
            // Embedded so the tray icon survives even if the install folder is
            // partially removed; falls back to the application icon.
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream("LogFileCollector.Tray.WatsBee.ico"))
                {
                    if (stream != null) return new Icon(stream);
                }
            }
            catch { /* fall through */ }
            return SystemIcons.Application;
        }

        /// <summary>
        /// The dashboard address comes from the same appsettings.json the service
        /// reads, so changing the port in one place moves both.
        /// </summary>
        private static string ReadDashboardUrlFromConfig()
        {
            const string fallback = "http://localhost:8787/";
            try
            {
                string path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Virinco", "WATS", "LogFileCollector", "appsettings.json");
                if (!File.Exists(path)) return fallback;

                using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path)))
                {
                    if (doc.RootElement.TryGetProperty("Dashboard", out JsonElement dash) &&
                        dash.TryGetProperty("UrlPrefix", out JsonElement prefix))
                    {
                        string url = prefix.GetString();
                        if (!string.IsNullOrWhiteSpace(url))
                        {
                            return url.Replace("+", "localhost").Replace("*", "localhost");
                        }
                    }
                }
            }
            catch { /* a malformed config is the service's problem to report */ }
            return fallback;
        }

        private async Task RefreshAsync()
        {
            string tooltip;
            try
            {
                string json = await _http.GetStringAsync(_dashboardUrl.TrimEnd('/') + "/api/status");
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    JsonElement root = doc.RootElement;
                    bool watching = root.GetProperty("watching").GetBoolean();
                    bool scanning = root.GetProperty("scanning").GetBoolean();
                    long dbCount = root.GetProperty("databaseCount").GetInt64();
                    JsonElement totals = root.GetProperty("totals");
                    int copied = totals.GetProperty("copied").GetInt32();
                    int errors = totals.GetProperty("errors").GetInt32();
                    bool sourceOk = root.GetProperty("sourceReachable").GetBoolean();

                    _online = true;
                    string state = scanning ? "Rescanning" : watching ? "Watching" : "Idle";
                    if (!sourceOk) state += " (source unreachable)";

                    _statusItem.Text = string.Format("{0} - {1} collected, {2} this session", state, dbCount, copied);
                    tooltip = string.Format("WATS Log File Collector\n{0}\n{1} files collected{2}",
                        state, dbCount, errors > 0 ? "\n" + errors + " error(s)" : string.Empty);
                }
            }
            catch
            {
                _online = false;
                _statusItem.Text = "Service not responding";
                tooltip = "WATS Log File Collector\nNot running";
            }

            // NotifyIcon truncates past 63 characters and throws on longer text in
            // some Windows versions.
            _icon.Text = tooltip.Length > 63 ? tooltip.Substring(0, 60) + "..." : tooltip;

            _openDashboardItem.Enabled = _online;
            _settingsItem.Enabled = _online;
            _rescanItem.Enabled = _online;
            _serviceItem.Text = IsServiceRunning() ? "Stop service" : "Start service";
        }

        /// <summary>
        /// Opens the dashboard in its own window, falling back to the browser when
        /// WebView2 is not available on the machine.
        /// </summary>
        private async void OpenDashboard(string fragment = null)
        {
            if (_window != null && !_window.IsDisposed)
            {
                _window.Show(fragment);
                return;
            }

            var window = new DashboardWindow(_dashboardUrl);
            if (!await window.TryLoadAsync())
            {
                window.Dispose();
                OpenInBrowser(fragment);
                return;
            }

            window.FormClosed += (s, e) => { _window = null; };
            _window = window;
            _window.Show(fragment);
        }

        private void OpenInBrowser(string fragment = null)
        {
            try
            {
                string url = _dashboardUrl.TrimEnd('/') + "/" + (fragment ?? string.Empty);
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ShowBalloon("Could not open the dashboard: " + ex.Message, ToolTipIcon.Error);
            }
        }

        /// <summary>
        /// Settings are edited on the dashboard, not in a text editor — the form
        /// validates before it writes, which hand-editing JSON does not.
        /// </summary>
        private void OpenSettings()
        {
            if (!_online)
            {
                ShowBalloon("The service is not responding, so settings cannot be opened. Start the service first.",
                    ToolTipIcon.Warning);
                return;
            }
            OpenDashboard("#settings");
        }

        private async Task RescanAsync()
        {
            try
            {
                // An empty JSON body, because HttpListener answers 411 to a POST
                // without a Content-Length.
                var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _http.PostAsync(_dashboardUrl.TrimEnd('/') + "/api/rescan", content);
                if (response.IsSuccessStatusCode)
                {
                    ShowBalloon("Rescan started.", ToolTipIcon.Info);
                }
                else
                {
                    ShowBalloon("Rescan refused: " + (int)response.StatusCode, ToolTipIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                ShowBalloon("Rescan failed: " + ex.Message, ToolTipIcon.Error);
            }
            await RefreshAsync();
        }

        private static bool IsServiceRunning()
        {
            try
            {
                using (var sc = new ServiceController(ServiceName))
                {
                    return sc.Status == ServiceControllerStatus.Running ||
                           sc.Status == ServiceControllerStatus.StartPending;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Starting and stopping a service needs elevation, so this shells out to
        /// `sc` with a UAC prompt rather than failing silently in a user process.
        /// </summary>
        private void ToggleService()
        {
            RunServiceCommand(IsServiceRunning() ? "stop" : "start");
        }

        /// <summary>
        /// Settings only take effect at startup, so this is how a configuration change
        /// is applied without going to services.msc.
        /// </summary>
        private void RestartService()
        {
            // One elevation prompt for both halves, rather than two.
            RunServiceCommand("stop " + ServiceName + " & sc.exe start", viaShell: true);
        }

        private void RunServiceCommand(string command, bool viaShell = false)
        {
            try
            {
                ProcessStartInfo psi;
                if (viaShell)
                {
                    psi = new ProcessStartInfo("cmd.exe", "/c sc.exe " + command + " " + ServiceName);
                }
                else
                {
                    psi = new ProcessStartInfo("sc.exe", command + " " + ServiceName);
                }
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                // The user cancelling the UAC prompt lands here too; that is not an error.
                ShowBalloon("Could not control the service: " + ex.Message, ToolTipIcon.Warning);
            }
        }

        private void ToggleAutoStart()
        {
            try
            {
                bool nowEnabled = AutoStart.Toggle();
                ShowBalloon(nowEnabled
                    ? "The tray icon will start with Windows."
                    : "The tray icon will no longer start with Windows.", ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                ShowBalloon("Could not change the startup setting: " + ex.Message, ToolTipIcon.Error);
            }
        }

        private void ShowBalloon(string text, ToolTipIcon icon)
        {
            _icon.BalloonTipTitle = "WATS Log File Collector";
            _icon.BalloonTipText = text;
            _icon.BalloonTipIcon = icon;
            _icon.ShowBalloonTip(4000);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer?.Stop();
                _timer?.Dispose();
                if (_icon != null)
                {
                    _icon.Visible = false;   // otherwise a ghost icon lingers until hover
                    _icon.Dispose();
                }
                _http?.Dispose();
                if (_window != null && !_window.IsDisposed) _window.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
