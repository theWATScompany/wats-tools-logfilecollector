using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using System.IO;

namespace LogFileCollector
{
    /// <summary>
    /// One row of the copy history, as shown in the dashboard.
    /// </summary>
    public class CopiedFile
    {
        public string SourcePath { get; set; }
        public string TargetPath { get; set; }
        public long Length { get; set; }
        public DateTime CopiedAtUtc { get; set; }
    }

    /// <summary>
    /// One day of copy activity, for the dashboard's throughput chart.
    /// </summary>
    public class DailyCount
    {
        public string Day { get; set; }   // yyyy-MM-dd (UTC)
        public int Count { get; set; }
    }

    /// <summary>
    /// SQLite persistence wrapper.
    /// Schema:
    ///   CREATE TABLE IF NOT EXISTS Copied(
    ///     FullPath TEXT NOT NULL,
    ///     LastWriteTimeUtc INTEGER NOT NULL, -- ticks
    ///     Length INTEGER NOT NULL,
    ///     TargetPath TEXT,                   -- added v1.2 (dashboard)
    ///     CopiedAtUtc INTEGER,               -- added v1.2 (dashboard), ticks
    ///     PRIMARY KEY(FullPath, LastWriteTimeUtc, Length)
    ///   );
    /// The two trailing columns are nullable so a database written by v1.1 keeps
    /// working after an in-place upgrade.
    /// </summary>
    public class Database
    {
        private readonly string _dbPath;
        private readonly string _connectionString;

        public Database(string dbPath)
        {
            _dbPath = dbPath;
            // Built rather than concatenated so a path containing ';' or '=' cannot
            // corrupt the connection string.
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = _dbPath,
                Cache = SqliteCacheMode.Shared
            };
            _connectionString = builder.ToString();
            EnsureSchema();
        }

        private void EnsureSchema()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_dbPath) ?? ".");
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "CREATE TABLE IF NOT EXISTS Copied(" +
                        " FullPath TEXT NOT NULL," +
                        " LastWriteTimeUtc INTEGER NOT NULL," +
                        " Length INTEGER NOT NULL," +
                        " PRIMARY KEY(FullPath, LastWriteTimeUtc, Length)" +
                        ");";
                    cmd.ExecuteNonQuery();
                }

                // Migrate databases created before the dashboard existed.
                AddColumnIfMissing(conn, "TargetPath", "TEXT");
                AddColumnIfMissing(conn, "CopiedAtUtc", "INTEGER");

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_Copied_CopiedAtUtc ON Copied(CopiedAtUtc);";
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private static void AddColumnIfMissing(SqliteConnection conn, string column, string type)
        {
            bool exists = false;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info(Copied);";
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                        {
                            exists = true;
                            break;
                        }
                    }
                }
            }

            if (exists) return;

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "ALTER TABLE Copied ADD COLUMN " + column + " " + type + ";";
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Returns true if a file with the same identity has already been copied.
        /// Identity = (FullPath, LastWriteTimeUtc, Length)
        /// </summary>
        public bool IsFileAlreadyCopied(string fullPath, DateTime lastWriteUtc, long length)
        {
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT 1 FROM Copied WHERE FullPath=$p AND LastWriteTimeUtc=$t AND Length=$l LIMIT 1;";
                    cmd.Parameters.AddWithValue("$p", fullPath);
                    cmd.Parameters.AddWithValue("$t", lastWriteUtc.Ticks);
                    cmd.Parameters.AddWithValue("$l", length);
                    object o = cmd.ExecuteScalar();
                    return o != null;
                }
            }
        }

        /// <summary>
        /// Inserts a record for the provided file identity, together with where it
        /// landed and when — the dashboard reads those two columns.
        /// </summary>
        public void MarkFileCopied(string fullPath, DateTime lastWriteUtc, long length, string targetPath)
        {
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "INSERT OR IGNORE INTO Copied(FullPath, LastWriteTimeUtc, Length, TargetPath, CopiedAtUtc) " +
                        "VALUES($p,$t,$l,$g,$c);";
                    cmd.Parameters.AddWithValue("$p", fullPath);
                    cmd.Parameters.AddWithValue("$t", lastWriteUtc.Ticks);
                    cmd.Parameters.AddWithValue("$l", length);
                    cmd.Parameters.AddWithValue("$g", (object)targetPath ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$c", DateTime.UtcNow.Ticks);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>Total number of files this collector has copied, ever.</summary>
        public long GetTotalCopiedCount()
        {
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM Copied;";
                    return Convert.ToInt64(cmd.ExecuteScalar());
                }
            }
        }

        /// <summary>
        /// Most recently copied files, newest first. Rows written by v1.1 carry no
        /// CopiedAtUtc and sort last.
        /// </summary>
        public List<CopiedFile> GetRecentlyCopied(int limit, string search)
        {
            var result = new List<CopiedFile>();
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    bool filtered = !string.IsNullOrWhiteSpace(search);
                    cmd.CommandText =
                        "SELECT FullPath, TargetPath, Length, CopiedAtUtc FROM Copied " +
                        (filtered ? "WHERE FullPath LIKE $s OR IFNULL(TargetPath, '') LIKE $s " : "") +
                        "ORDER BY IFNULL(CopiedAtUtc, 0) DESC, rowid DESC LIMIT $n;";
                    if (filtered) cmd.Parameters.AddWithValue("$s", "%" + search + "%");
                    cmd.Parameters.AddWithValue("$n", limit);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            result.Add(new CopiedFile
                            {
                                SourcePath = reader.IsDBNull(0) ? null : reader.GetString(0),
                                TargetPath = reader.IsDBNull(1) ? null : reader.GetString(1),
                                Length = reader.IsDBNull(2) ? 0L : reader.GetInt64(2),
                                CopiedAtUtc = reader.IsDBNull(3) ? DateTime.MinValue : new DateTime(reader.GetInt64(3), DateTimeKind.Utc)
                            });
                        }
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Files copied per UTC day over the last <paramref name="days"/> days,
        /// oldest first, with zero-filled gaps so the chart has no holes.
        /// </summary>
        public List<DailyCount> GetDailyCounts(int days)
        {
            var buckets = new Dictionary<string, int>();
            DateTime from = DateTime.UtcNow.Date.AddDays(-(days - 1));

            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT CopiedAtUtc FROM Copied WHERE CopiedAtUtc >= $from;";
                    cmd.Parameters.AddWithValue("$from", from.Ticks);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            if (reader.IsDBNull(0)) continue;
                            string key = new DateTime(reader.GetInt64(0), DateTimeKind.Utc).ToString("yyyy-MM-dd");
                            int current;
                            buckets[key] = buckets.TryGetValue(key, out current) ? current + 1 : 1;
                        }
                    }
                }
            }

            var result = new List<DailyCount>();
            for (int i = 0; i < days; i++)
            {
                string key = from.AddDays(i).ToString("yyyy-MM-dd");
                int count;
                buckets.TryGetValue(key, out count);
                result.Add(new DailyCount { Day = key, Count = count });
            }
            return result;
        }

        /// <summary>
        /// Generates a unique path in the target directory when a name collision happens.
        /// Strategies:
        ///  - counter   => file_1.ext, file_2.ext
        ///  - timestamp => file_yyyyMMdd_HHmmssfff.ext
        ///  - guid      => file_XXXXXXXX.ext
        /// </summary>
        public string GetUniqueTargetPath(string targetFolder, string fileName, string renameStrategy)
        {
            string name = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            string candidate = Path.Combine(targetFolder, fileName);

            if (!File.Exists(candidate)) return candidate;

            string strategy = (renameStrategy ?? "counter").Trim().ToLowerInvariant();
            if (strategy == "timestamp")
            {
                string ts = DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff");
                return Path.Combine(targetFolder, name + "_" + ts + ext);
            }
            else if (strategy == "guid")
            {
                string g = Guid.NewGuid().ToString("N").Substring(0, 8);
                return Path.Combine(targetFolder, name + "_" + g + ext);
            }
            else
            {
                // counter (default)
                int i = 1;
                while (true)
                {
                    candidate = Path.Combine(targetFolder, string.Format("{0}_{1}{2}", name, i, ext));
                    if (!File.Exists(candidate)) return candidate;
                    i++;
                    if (i > 1000000) throw new IOException("Could not find a unique file name.");
                }
            }
        }
    }
}
