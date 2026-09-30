using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Serilog;

namespace LogFileCollector
{
    /// <summary>
    /// Simple stats container. We keep one per run (RunStats) and one cumulative (TotalStats).
    /// </summary>
    public class ProcessingStats
    {
        public int Scanned { get; set; }
        public int Copied { get; set; }
        public int Skipped { get; set; }
        public int Errors { get; set; }

        public override string ToString()
        {
            return string.Format("Scanned={0}, Copied={1}, Skipped={2}, Errors={3}", Scanned, Copied, Skipped, Errors);
        }

        public void Reset()
        {
            Scanned = 0;
            Copied = 0;
            Skipped = 0;
            Errors = 0;
        }

        public void Add(ProcessingStats other)
        {
            Scanned += other.Scanned;
            Copied += other.Copied;
            Skipped += other.Skipped;
            Errors += other.Errors;
        }

        public ProcessingStats Snapshot()
        {
            return new ProcessingStats { Scanned = Scanned, Copied = Copied, Skipped = Skipped, Errors = Errors };
        }
    }

    /// <summary>
    /// Encapsulates watcher + scanning logic.
    /// - Uses Created + Renamed events (Changed is noisy and can duplicate)
    /// - Increases InternalBufferSize to 64 KB for burst resistance
    /// - Respects FileCreatedDelayMs before copying to avoid partial files
    /// - Uses Database for duplicate prevention based on (path, lastWriteUtc, length)
    /// </summary>
    public class FileProcessor
    {
        /// <summary>How often to look for a source folder that was not there at startup.</summary>
        private const int RetrySeconds = 30;

        private readonly AppSettings _config;
        private readonly Database _db;

        // One watcher per source folder. A folder that is not reachable yet simply has
        // no entry, and the retry timer keeps trying to give it one.
        private readonly Dictionary<string, FileSystemWatcher> _watchers =
            new Dictionary<string, FileSystemWatcher>(StringComparer.OrdinalIgnoreCase);
        private readonly object _watchersLock = new object();
        private Timer _watcherRetryTimer;

        // A rescan can now also be triggered from the dashboard, so guard against
        // two full scans running at once.
        private int _scanInProgress;

        public ProcessingStats RunStats { get; private set; } = new ProcessingStats();
        public ProcessingStats TotalStats { get; private set; } = new ProcessingStats();

        /// <summary>True once at least one source folder is being watched.</summary>
        public bool IsWatching
        {
            get { lock (_watchersLock) { return _watchers.Count > 0; } }
        }

        /// <summary>The source folders currently attached, for the dashboard.</summary>
        public List<string> WatchedFolders
        {
            get { lock (_watchersLock) { return new List<string>(_watchers.Keys); } }
        }

        /// <summary>Source folders configured but not reachable right now.</summary>
        public List<string> PendingFolders
        {
            get
            {
                var pending = new List<string>();
                lock (_watchersLock)
                {
                    foreach (string folder in _config.SourceFolders)
                    {
                        if (!_watchers.ContainsKey(folder)) pending.Add(folder);
                    }
                }
                return pending;
            }
        }

        /// <summary>When the last file was copied, or null if none yet this process.</summary>
        public DateTime? LastCopiedUtc { get; private set; }

        /// <summary>Path of the last file copied, for the dashboard header.</summary>
        public string LastCopiedPath { get; private set; }

        /// <summary>Last error message seen, so the dashboard can show it without a log dig.</summary>
        public string LastError { get; private set; }

        /// <summary>True while a full scan is running.</summary>
        public bool IsScanning { get { return Volatile.Read(ref _scanInProgress) != 0; } }

        public FileProcessor(AppSettings config, Database db)
        {
            _config = config;
            _db = db;
        }

        /// <summary>
        /// Full pass over the source folder. Resets RunStats each time.
        /// Returns false if a scan was already running and this one was skipped.
        /// </summary>
        public bool ProcessAllFiles()
        {
            if (Interlocked.CompareExchange(ref _scanInProgress, 1, 0) != 0)
            {
                Log.Warning("Rescan requested while one is already running — ignored.");
                return false;
            }

            try
            {
                RunStats.Reset();

                SearchOption opt = _config.IncludeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

                // One unreachable folder must not stop the others being scanned.
                bool anyScanned = false;
                foreach (string sourceFolder in _config.SourceFolders)
                {
                    string[] files;
                    try
                    {
                        files = Directory.GetFiles(sourceFolder, _config.Filter, opt);
                    }
                    catch (Exception ex)
                    {
                        LastError = ex.Message;
                        Log.Error(ex, "Failed to enumerate files in source folder {Folder}. If this is a network or cloud path, verify UNC accessibility and permissions (read only is sufficient).", sourceFolder);
                        continue;
                    }

                    anyScanned = true;
                    foreach (string fullPath in files)
                    {
                        HandleOne(fullPath);
                    }
                }

                if (!anyScanned)
                {
                    Log.Warning("No source folder could be read; nothing was scanned.");
                    return false;
                }

                // After a run, fold into totals and log
                TotalStats.Add(RunStats);
                Log.Information("Processing summary: {RunStats}", RunStats);
                Log.Information("Total so far: {TotalStats}", TotalStats);
                return true;
            }
            finally
            {
                Volatile.Write(ref _scanInProgress, 0);
            }
        }

        /// <summary>
        /// Start the FileSystemWatcher for incremental changes.
        ///
        /// The source folder may legitimately not be there yet: as a service this runs
        /// at boot, before a network share is mounted or before the test system has
        /// created its log folder. That must not be fatal — a service that throws here
        /// is restarted by the SCM and throws again, forever. Instead the folder is
        /// polled until it appears, and the watcher attaches then.
        /// </summary>
        public void StartWatching()
        {
            var attached = new List<string>();
            foreach (string folder in _config.SourceFolders)
            {
                if (TryAttachWatcher(folder)) attached.Add(folder);
            }

            List<string> pending = PendingFolders;
            if (pending.Count > 0)
            {
                Log.Warning("Not available yet, retrying every {Seconds}s: {Folders}",
                    RetrySeconds, string.Join(", ", pending));
            }

            // One timer for all folders: it keeps running as long as any folder is
            // still missing, and stops once every one of them is attached.
            if (pending.Count > 0 && _watcherRetryTimer == null)
            {
                _watcherRetryTimer = new Timer(_ => RetryPendingFolders(), null,
                    TimeSpan.FromSeconds(RetrySeconds), TimeSpan.FromSeconds(RetrySeconds));
            }
        }

        private void RetryPendingFolders()
        {
            try
            {
                var newlyAttached = new List<string>();
                foreach (string folder in PendingFolders)
                {
                    if (TryAttachWatcher(folder)) newlyAttached.Add(folder);
                }

                if (newlyAttached.Count > 0)
                {
                    // A folder may have existed for a while before we noticed, so sweep
                    // it rather than waiting for the next new file to arrive in it.
                    ProcessAllFiles();
                }

                if (PendingFolders.Count == 0 && _watcherRetryTimer != null)
                {
                    _watcherRetryTimer.Dispose();
                    _watcherRetryTimer = null;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error while retrying source folders");
            }
        }

        /// <summary>
        /// Attaches a watcher to one folder if it is reachable. Returns false —
        /// without throwing — when it is not.
        /// </summary>
        private bool TryAttachWatcher(string sourceFolder)
        {
            try
            {
                lock (_watchersLock)
                {
                    if (_watchers.ContainsKey(sourceFolder)) return true;
                }

                if (!Directory.Exists(sourceFolder)) return false;

                var watcher = new FileSystemWatcher(sourceFolder, _config.Filter);
                watcher.IncludeSubdirectories = _config.IncludeSubdirectories;
                watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime;
                watcher.InternalBufferSize = 64 * 1024; // Max 64 KB

                watcher.Created += OnCreatedOrRenamed;
                watcher.Renamed += OnCreatedOrRenamed;
                watcher.Error += OnWatcherError;

                watcher.EnableRaisingEvents = true;

                lock (_watchersLock) { _watchers[sourceFolder] = watcher; }
                LastError = null;

                Log.Information("Watching {Source} (Filter={Filter}, Subdirs={Subdirs}, DelayMs={Delay})",
                    sourceFolder, _config.Filter, _config.IncludeSubdirectories, _config.FileCreatedDelayMs);
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Log.Debug(ex, "Could not attach a watcher to {Source}", sourceFolder);
                return false;
            }
        }

        /// <summary>
        /// A watcher dies when its buffer overflows or its share goes away. Drop that
        /// one and let the retry loop re-attach it — the other folders carry on.
        /// </summary>
        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            Exception ex = e.GetException();
            var watcher = sender as FileSystemWatcher;
            string folder = watcher != null ? watcher.Path : "(unknown)";

            LastError = ex != null ? ex.Message : "watcher error";
            Log.Error(ex, "Watcher stopped on {Source} — will try to re-attach.", folder);

            lock (_watchersLock)
            {
                foreach (var pair in _watchers)
                {
                    if (!ReferenceEquals(pair.Value, watcher)) continue;
                    _watchers.Remove(pair.Key);
                    break;
                }
            }
            try { if (watcher != null) watcher.Dispose(); } catch { /* already gone */ }

            if (_watcherRetryTimer == null)
            {
                _watcherRetryTimer = new Timer(_ => RetryPendingFolders(), null,
                    TimeSpan.FromSeconds(RetrySeconds), TimeSpan.FromSeconds(RetrySeconds));
            }
        }

        private void OnCreatedOrRenamed(object sender, FileSystemEventArgs e)
        {
            HandleOne(e.FullPath, fromWatcher: true);
        }

        /// <summary>
        /// Process a single file path:
        /// - wait FileCreatedDelayMs (to avoid partial writes)
        /// - check DB for duplication
        /// - copy to target (with rename strategy on collision)
        /// - record in DB
        /// Stats:
        /// - For watcher events, we reset RunStats to report a per-event summary (keeps logs informative)
        /// - For full scans, ProcessAllFiles() resets before the loop and aggregates.
        /// </summary>
        private void HandleOne(string fullPath, bool fromWatcher = false)
        {
            try
            {
                if (fromWatcher)
                {
                    // For single-event summaries in logs
                    RunStats.Reset();
                }

                // Wait out writer processes (cheap insurance)
                Thread.Sleep(_config.FileCreatedDelayMs);

                FileInfo fi = new FileInfo(fullPath);
                if (!fi.Exists) return; // vanished or temp

                RunStats.Scanned++;

                // Duplicate check against DB
                if (_db.IsFileAlreadyCopied(fullPath, fi.LastWriteTimeUtc, fi.Length))
                {
                    Log.Debug("Skipped (already copied): {File}", fullPath);
                    RunStats.Skipped++;
                    if (fromWatcher)
                    {
                        TotalStats.Add(RunStats);
                        Log.Information("Watcher event summary: {RunStats}", RunStats);
                        Log.Information("Total so far: {TotalStats}", TotalStats);
                    }
                    return;
                }

                // Ensure target folder exists
                if (!Directory.Exists(_config.TargetFolder))
                {
                    Directory.CreateDirectory(_config.TargetFolder);
                }

                // Resolve unique target path per strategy
                string targetPath = _db.GetUniqueTargetPath(_config.TargetFolder, fi.Name, _config.RenameStrategy);

                // Copy (no overwrite)
                File.Copy(fullPath, targetPath, false);

                // Record in DB
                _db.MarkFileCopied(fullPath, fi.LastWriteTimeUtc, fi.Length, targetPath);

                RunStats.Copied++;
                LastCopiedUtc = DateTime.UtcNow;
                LastCopiedPath = targetPath;
                Log.Information("Copied {Source} -> {Target}", fullPath, targetPath);

                if (fromWatcher)
                {
                    TotalStats.Add(RunStats);
                    Log.Information("Watcher event summary: {RunStats}", RunStats);
                    Log.Information("Total so far: {TotalStats}", TotalStats);
                }
            }
            catch (Exception ex)
            {
                RunStats.Errors++;
                TotalStats.Errors++;
                LastError = ex.Message;
                Log.Error(ex, "Error processing file {File}", fullPath);
            }
        }
    }
}
