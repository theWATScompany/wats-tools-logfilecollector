using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace LogFileCollector
{
    /// <summary>
    /// Loads AppSettings from a JSON file using System.Text.Json.
    /// </summary>
    public static class ConfigLoader
    {
        public static AppSettings Load(string path)
        {
            string json = File.ReadAllText(path);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };
            AppSettings settings = JsonSerializer.Deserialize<AppSettings>(json, options);

            // Minimal validation with clear error messages.
            if (settings == null) throw new InvalidOperationException("Configuration could not be parsed.");

            // Fold the old single-folder key into the list, so everything downstream
            // only ever deals with SourceFolders.
            settings.SourceFolders = NormaliseSourceFolders(settings);
            if (settings.SourceFolders.Count == 0)
                throw new InvalidOperationException("At least one source folder is required (SourceFolders, or the older SourceFolder).");

            if (string.IsNullOrWhiteSpace(settings.TargetFolder)) throw new InvalidOperationException("TargetFolder is required.");

            foreach (string source in settings.SourceFolders)
            {
                if (PathsEqual(source, settings.TargetFolder))
                    throw new InvalidOperationException("A source folder must not be the same as the target folder: " + source);
            }
            if (string.IsNullOrWhiteSpace(settings.Filter)) settings.Filter = "*.*";
            if (settings.FileCreatedDelayMs <= 0) settings.FileCreatedDelayMs = 1000;
            if (settings.Logging == null) throw new InvalidOperationException("Logging section is required.");
            if (string.IsNullOrWhiteSpace(settings.Logging.LogFilePath)) settings.Logging.LogFilePath = "log.txt";
            if (string.IsNullOrWhiteSpace(settings.Logging.LogOutputTemplate))
                settings.Logging.LogOutputTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";
            if (string.IsNullOrWhiteSpace(settings.Logging.LogLevel)) settings.Logging.LogLevel = "Information";
            if (string.IsNullOrWhiteSpace(settings.Logging.RollingInterval)) settings.Logging.RollingInterval = "Day";
            if (settings.Logging.RetainedFileCountLimit <= 0) settings.Logging.RetainedFileCountLimit = 10;

            // The Dashboard section is optional — a v1.1 appsettings.json has none,
            // and the collector should still come up with the dashboard on its default port.
            if (settings.Dashboard == null) settings.Dashboard = new DashboardSettings();
            if (string.IsNullOrWhiteSpace(settings.Dashboard.UrlPrefix))
                settings.Dashboard.UrlPrefix = "http://localhost:8787/";
            if (!settings.Dashboard.UrlPrefix.EndsWith("/"))
                settings.Dashboard.UrlPrefix += "/";

            return settings;
        }

        /// <summary>
        /// Builds the effective watch list: SourceFolders when present, otherwise the
        /// single SourceFolder. Blanks are dropped and duplicates collapsed, so a list
        /// with a stray empty row or the same folder twice does not create two watchers
        /// on one directory and copy everything twice.
        /// </summary>
        private static List<string> NormaliseSourceFolders(AppSettings settings)
        {
            var candidates = new List<string>();
            if (settings.SourceFolders != null) candidates.AddRange(settings.SourceFolders);
            if (candidates.Count == 0 && !string.IsNullOrWhiteSpace(settings.SourceFolder))
                candidates.Add(settings.SourceFolder);

            var result = new List<string>();
            foreach (string candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;
                string trimmed = candidate.Trim();
                bool alreadyThere = false;
                foreach (string existing in result)
                {
                    if (PathsEqual(existing, trimmed)) { alreadyThere = true; break; }
                }
                if (!alreadyThere) result.Add(trimmed);
            }
            return result;
        }

        /// <summary>
        /// Compares two folder paths for practical equality — trailing separators and
        /// case are not meaningful on Windows.
        /// </summary>
        private static bool PathsEqual(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            return string.Equals(
                a.TrimEnd('\\', '/'),
                b.TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
