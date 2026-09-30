using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LogFileCollector.Tray
{
    /// <summary>
    /// The dashboard in an application window.
    ///
    /// The collector serves its UI over HTTP because the service is headless and may
    /// be looked at from another machine — and because that is what still works on
    /// .NET 8 on Linux. But on a Windows install there is already a process in the
    /// user's session, so making people type a localhost URL into a browser is a
    /// worse experience than giving them a window. This hosts exactly the same page
    /// in WebView2: one UI, two ways in.
    ///
    /// WebView2 needs the Evergreen runtime. It is present on Windows 11 and comes
    /// with Edge on Windows 10, but when it is missing this falls back to the default
    /// browser rather than failing.
    /// </summary>
    internal class DashboardWindow : Form
    {
        private readonly string _url;
        private WebView2 _view;

        public DashboardWindow(string url)
        {
            _url = url;

            Text = "WATS Log File Collector";
            Width = 1180;
            Height = 860;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(900, 600);
            BackColor = WatsColors.Background;

            try
            {
                Icon = TrayApplication.LoadAppIcon();
            }
            catch { /* the default icon will do */ }
        }

        /// <summary>
        /// True when WebView2 initialised and the page is loading. False means the
        /// caller should fall back to the browser.
        /// </summary>
        public async Task<bool> TryLoadAsync()
        {
            try
            {
                _view = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = WatsColors.Background };
                Controls.Add(_view);

                // Keep the browser profile with the rest of the tool's per-user state
                // rather than next to the executable, which is in Program Files and
                // not writable.
                string userData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Virinco", "WATS", "LogFileCollector", "WebView2");
                Directory.CreateDirectory(userData);

                CoreWebView2Environment environment =
                    await CoreWebView2Environment.CreateAsync(null, userData);
                await _view.EnsureCoreWebView2Async(environment);

                CoreWebView2Settings settings = _view.CoreWebView2.Settings;
                settings.AreDefaultContextMenusEnabled = false;
                settings.IsStatusBarEnabled = false;
                settings.AreDevToolsEnabled = false;

                // This window is for the collector's own dashboard. Anything that tries
                // to navigate elsewhere opens in the real browser instead.
                _view.CoreWebView2.NewWindowRequested += (s, e) =>
                {
                    e.Handled = true;
                    OpenInBrowser(e.Uri);
                };
                _view.CoreWebView2.NavigationStarting += (s, e) =>
                {
                    if (IsOwnUrl(e.Uri)) return;
                    e.Cancel = true;
                    OpenInBrowser(e.Uri);
                };

                _view.CoreWebView2.Navigate(_url);
                return true;
            }
            catch (Exception ex)
            {
                // Missing runtime, a locked profile folder, a policy block — all end up
                // here, and all mean the same thing: use the browser.
                System.Diagnostics.Debug.WriteLine("WebView2 unavailable: " + ex);
                if (_view != null)
                {
                    Controls.Remove(_view);
                    _view.Dispose();
                    _view = null;
                }
                return false;
            }
        }

        private bool IsOwnUrl(string uri)
        {
            try
            {
                return new Uri(uri).GetLeftPart(UriPartial.Authority)
                    .Equals(new Uri(_url).GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void OpenInBrowser(string uri)
        {
            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true });
            }
            catch { /* nothing useful to do if the shell refuses */ }
        }

        /// <summary>Navigates to a page within the dashboard, e.g. "#settings".</summary>
        public void Show(string fragment)
        {
            if (_view != null && _view.CoreWebView2 != null)
            {
                _view.CoreWebView2.Navigate(_url.TrimEnd('/') + "/" + (fragment ?? string.Empty));
            }
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Show();
            BringToFront();
            Activate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _view != null)
            {
                _view.Dispose();
                _view = null;
            }
            base.Dispose(disposing);
        }
    }
}
