using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using Serilog;

namespace LogFileCollector
{
    /// <summary>
    /// The collector's own HTTP server: it serves the WebUI folder as static files
    /// and a small JSON API on top of the live FileProcessor and the SQLite history.
    ///
    /// Why HTTP and not a desktop window: the collector normally runs headless as a
    /// scheduled task or service, often on a machine nobody sits at. A page served
    /// over loopback can be opened from the same machine's browser, or — when the
    /// prefix is widened deliberately — from the engineer's desk. It also keeps the
    /// tool running unchanged on .NET 8 on Linux, which an embedded WebView2 host
    /// would not.
    ///
    /// Security: the default prefix is loopback-only and there is no authentication.
    /// Anything that mutates state (rescan, config write) is additionally refused
    /// unless the request came from a loopback address, so widening the prefix for
    /// read-only monitoring cannot hand the network a config editor.
    /// </summary>
    public class WebServer : IDisposable
    {
        private readonly AppSettings _config;
        private readonly string _configPath;
        private readonly Database _db;
        private readonly FileProcessor _processor;
        private readonly string _logPath;
        private readonly string _webRoot;
        private readonly DateTime _startedUtc = DateTime.UtcNow;

        private HttpListener _listener;
        private Thread _thread;
        private volatile bool _stopping;

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false
        };

        private static readonly Dictionary<string, string> MimeTypes =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { ".html", "text/html; charset=utf-8" },
                { ".css",  "text/css; charset=utf-8" },
                { ".js",   "application/javascript; charset=utf-8" },
                { ".json", "application/json; charset=utf-8" },
                { ".svg",  "image/svg+xml" },
                { ".png",  "image/png" },
                { ".ico",  "image/x-icon" },
                { ".woff2","font/woff2" },
                { ".map",  "application/json; charset=utf-8" }
            };

        public WebServer(AppSettings config, string configPath, Database db, FileProcessor processor, string logPath)
        {
            _config = config;
            _configPath = configPath;
            _db = db;
            _processor = processor;
            _logPath = logPath;

            _webRoot = Path.Combine(AppContext.BaseDirectory ?? ".", "WebUI");
        }

        /// <summary>
        /// Binds the listener and starts serving on a background thread.
        /// A failure to bind is logged and swallowed: the dashboard is a convenience,
        /// and losing it must never stop files being collected.
        /// </summary>
        public bool Start()
        {
            if (!Directory.Exists(_webRoot))
            {
                Log.Warning("Dashboard disabled: WebUI folder not found at {Root}", _webRoot);
                return false;
            }

            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add(_config.Dashboard.UrlPrefix);
                _listener.Start();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Dashboard could not bind {Prefix}. On Windows a non-loopback prefix needs a URL ACL: netsh http add urlacl url={Prefix} user=<account>", _config.Dashboard.UrlPrefix);
                _listener = null;
                return false;
            }

            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Name = "LogFileCollector dashboard";
            _thread.Start();

            Log.Information("Dashboard listening on {Prefix}", _config.Dashboard.UrlPrefix);
            return true;
        }

        private void Loop()
        {
            while (!_stopping)
            {
                HttpListenerContext context;
                try
                {
                    context = _listener.GetContext();
                }
                catch (Exception)
                {
                    if (_stopping) return;
                    continue;
                }

                try
                {
                    Handle(context);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Dashboard request failed: {Url}", context.Request.RawUrl);
                    TrySendError(context, 500, ex.Message);
                }
            }
        }

        private void Handle(HttpListenerContext ctx)
        {
            string path = (ctx.Request.Url.AbsolutePath ?? "/").TrimEnd('/');
            if (path.Length == 0) path = "/";

            if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            {
                HandleApi(ctx, path);
                return;
            }

            ServeStatic(ctx, path);
        }

        // ── API ──────────────────────────────────────────────────────────────

        private void HandleApi(HttpListenerContext ctx, string path)
        {
            bool isWrite = !string.Equals(ctx.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase);
            if (isWrite && !ctx.Request.IsLocal)
            {
                SendJson(ctx, 403, new { error = "Write operations are only accepted from the local machine." });
                return;
            }

            switch (path.ToLowerInvariant())
            {
                case "/api/status":
                    SendJson(ctx, 200, BuildStatus());
                    return;

                case "/api/files":
                    {
                        int limit = QueryInt(ctx, "limit", 200, 1, 2000);
                        string search = ctx.Request.QueryString["q"];
                        List<CopiedFile> rows = _db.GetRecentlyCopied(limit, search);
                        var payload = new List<object>(rows.Count);
                        foreach (CopiedFile f in rows)
                        {
                            payload.Add(new
                            {
                                source = f.SourcePath,
                                target = f.TargetPath,
                                length = f.Length,
                                copiedAtUtc = f.CopiedAtUtc == DateTime.MinValue
                                    ? null
                                    : f.CopiedAtUtc.ToString("o")
                            });
                        }
                        SendJson(ctx, 200, payload);
                        return;
                    }

                case "/api/activity":
                    {
                        int days = QueryInt(ctx, "days", 14, 1, 365);
                        SendJson(ctx, 200, _db.GetDailyCounts(days));
                        return;
                    }

                case "/api/log":
                    {
                        int lines = QueryInt(ctx, "lines", 200, 1, 5000);
                        SendJson(ctx, 200, new { path = _logPath, lines = TailLog(lines) });
                        return;
                    }

                case "/api/config":
                    if (string.Equals(ctx.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                    {
                        SendJson(ctx, 200, new
                        {
                            path = _configPath,
                            editable = _config.Dashboard.AllowConfigEdit,
                            json = File.Exists(_configPath) ? File.ReadAllText(_configPath) : "{}"
                        });
                        return;
                    }
                    if (string.Equals(ctx.Request.HttpMethod, "PUT", StringComparison.OrdinalIgnoreCase))
                    {
                        SaveConfig(ctx);
                        return;
                    }
                    SendJson(ctx, 405, new { error = "Use GET or PUT." });
                    return;

                case "/api/rescan":
                    if (!string.Equals(ctx.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                    {
                        SendJson(ctx, 405, new { error = "Use POST." });
                        return;
                    }
                    if (_processor.IsScanning)
                    {
                        SendJson(ctx, 409, new { error = "A rescan is already running." });
                        return;
                    }
                    StartBackgroundRescan();
                    SendJson(ctx, 202, new { started = true });
                    return;

                default:
                    SendJson(ctx, 404, new { error = "Unknown endpoint: " + path });
                    return;
            }
        }

        private object BuildStatus()
        {
            ProcessingStats total = _processor.TotalStats.Snapshot();
            bool sourceOk = SafeDirectoryExists(_config.SourceFolder);
            bool targetOk = SafeDirectoryExists(_config.TargetFolder);

            return new
            {
                version = Assembly.GetExecutingAssembly().GetName().Version.ToString(),
                framework = FrameworkDescription(),
                os = Environment.OSVersion.Platform.ToString(),
                machine = Environment.MachineName,
                startedUtc = _startedUtc.ToString("o"),
                nowUtc = DateTime.UtcNow.ToString("o"),
                watching = _processor.IsWatching,
                scanning = _processor.IsScanning,
                sourceFolder = _config.SourceFolder,
                sourceReachable = sourceOk,
                targetFolder = _config.TargetFolder,
                targetReachable = targetOk,
                filter = _config.Filter,
                includeSubdirectories = _config.IncludeSubdirectories,
                renameStrategy = _config.RenameStrategy,
                periodicRescanMinutes = _config.PeriodicRescanMinutes,
                fileCreatedDelayMs = _config.FileCreatedDelayMs,
                lastCopiedUtc = _processor.LastCopiedUtc.HasValue ? _processor.LastCopiedUtc.Value.ToString("o") : null,
                lastCopiedPath = _processor.LastCopiedPath,
                lastError = _processor.LastError,
                configEditable = _config.Dashboard.AllowConfigEdit,
                totals = new
                {
                    scanned = total.Scanned,
                    copied = total.Copied,
                    skipped = total.Skipped,
                    errors = total.Errors
                },
                databaseCount = _db.GetTotalCopiedCount()
            };
        }

        private void SaveConfig(HttpListenerContext ctx)
        {
            if (!_config.Dashboard.AllowConfigEdit)
            {
                SendJson(ctx, 403, new { error = "Config editing is disabled (Dashboard.AllowConfigEdit = false)." });
                return;
            }

            string body;
            using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
            {
                body = reader.ReadToEnd();
            }

            // Write to a temp file and parse it with the real loader first: a config
            // that would stop the collector from starting must never reach disk.
            string temp = _configPath + ".dashboard-tmp";
            try
            {
                File.WriteAllText(temp, body, new UTF8Encoding(false));
                ConfigLoader.Load(temp);
            }
            catch (Exception ex)
            {
                TryDelete(temp);
                SendJson(ctx, 400, new { error = "Rejected: " + ex.Message });
                return;
            }

            try
            {
                string backup = _configPath + ".bak";
                if (File.Exists(_configPath)) File.Copy(_configPath, backup, true);
                File.Copy(temp, _configPath, true);
            }
            finally
            {
                TryDelete(temp);
            }

            Log.Warning("appsettings.json was rewritten from the dashboard. Restart the collector to apply it.");
            SendJson(ctx, 200, new { saved = true, restartRequired = true });
        }

        private void StartBackgroundRescan()
        {
            var t = new Thread(() =>
            {
                try
                {
                    Log.Information("Rescan requested from the dashboard.");
                    _processor.ProcessAllFiles();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Dashboard-triggered rescan failed");
                }
            });
            t.IsBackground = true;
            t.Name = "LogFileCollector rescan";
            t.Start();
        }

        private List<string> TailLog(int lines)
        {
            var result = new List<string>();
            string file = ResolveCurrentLogFile();
            if (file == null) return result;

            try
            {
                // Serilog keeps the file open, so share both read and write.
                using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream))
                {
                    var ring = new Queue<string>();
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        ring.Enqueue(line);
                        if (ring.Count > lines) ring.Dequeue();
                    }
                    result.AddRange(ring);
                }
            }
            catch (Exception ex)
            {
                result.Add("(could not read log: " + ex.Message + ")");
            }
            return result;
        }

        /// <summary>
        /// Serilog's rolling sink appends a date to the configured file name, so the
        /// file actually being written is the newest match on that pattern.
        /// </summary>
        private string ResolveCurrentLogFile()
        {
            try
            {
                string dir = Path.GetDirectoryName(_logPath);
                if (string.IsNullOrEmpty(dir)) dir = ".";
                string stem = Path.GetFileNameWithoutExtension(_logPath);
                string ext = Path.GetExtension(_logPath);
                if (!Directory.Exists(dir)) return null;

                string newest = null;
                DateTime newestAt = DateTime.MinValue;
                foreach (string candidate in Directory.GetFiles(dir, stem + "*" + ext))
                {
                    DateTime at = File.GetLastWriteTimeUtc(candidate);
                    if (at > newestAt)
                    {
                        newestAt = at;
                        newest = candidate;
                    }
                }
                return newest;
            }
            catch
            {
                return null;
            }
        }

        // ── Static files ─────────────────────────────────────────────────────

        private void ServeStatic(HttpListenerContext ctx, string path)
        {
            string relative = path == "/" ? "index.html" : path.TrimStart('/');
            relative = Uri.UnescapeDataString(relative).Replace('/', Path.DirectorySeparatorChar);

            string full = Path.GetFullPath(Path.Combine(_webRoot, relative));
            string root = Path.GetFullPath(_webRoot);

            // Refuse anything that climbs out of the web root.
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            {
                TrySendError(ctx, 403, "Forbidden");
                return;
            }

            if (!File.Exists(full))
            {
                TrySendError(ctx, 404, "Not found: " + path);
                return;
            }

            string contentType;
            if (!MimeTypes.TryGetValue(Path.GetExtension(full), out contentType))
                contentType = "application/octet-stream";

            byte[] bytes = File.ReadAllBytes(full);
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = contentType;
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.OutputStream.Close();
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static int QueryInt(HttpListenerContext ctx, string key, int fallback, int min, int max)
        {
            int value;
            if (!int.TryParse(ctx.Request.QueryString[key], out value)) return fallback;
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static bool SafeDirectoryExists(string path)
        {
            try { return !string.IsNullOrWhiteSpace(path) && Directory.Exists(path); }
            catch { return false; }
        }

        private static string FrameworkDescription()
        {
#if NET48
            return ".NET Framework 4.8";
#else
            return System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
#endif
        }

        private static void SendJson(HttpListenerContext ctx, int status, object payload)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOptions));
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.Headers["Cache-Control"] = "no-store";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.OutputStream.Close();
        }

        private static void TrySendError(HttpListenerContext ctx, int status, string message)
        {
            try { SendJson(ctx, status, new { error = message }); }
            catch { /* client already gone */ }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* best effort */ }
        }

        public void Dispose()
        {
            _stopping = true;
            try { if (_listener != null) _listener.Close(); }
            catch { /* already closed */ }
        }
    }
}
