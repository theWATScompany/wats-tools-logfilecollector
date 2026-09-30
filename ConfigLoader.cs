using System;
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
            if (string.IsNullOrWhiteSpace(settings.SourceFolder)) throw new InvalidOperationException("SourceFolder is required.");
            if (string.IsNullOrWhiteSpace(settings.TargetFolder)) throw new InvalidOperationException("TargetFolder is required.");
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
    }
}
