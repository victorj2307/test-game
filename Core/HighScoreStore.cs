using System.IO;
using System.Linq;
using System.Text.Json;
using System.Diagnostics;

namespace Game.Core;

public static class HighScoreStore
{
    private const string FileName = GameConfig.Persistence.HighScoreFileName;
    private const string DefaultName = GameConfig.Persistence.DefaultPlayerName;
    private const int MaxFileBytes = GameConfig.Persistence.MaxLeaderboardFileBytes;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    internal static string? StorageRootOverride { get; set; }

    /// <summary>Loads, normalizes, and trims leaderboard entries from local storage.</summary>
    public static IReadOnlyList<LeaderboardEntry> LoadLeaderboard(int maxEntries)
    {
        try
        {
            string path = ResolveReadPath();
            if (!File.Exists(path)) return Array.Empty<LeaderboardEntry>();
            if (!IsReasonableSize(path)) return Array.Empty<LeaderboardEntry>();

            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return Array.Empty<LeaderboardEntry>();

            var listData = JsonSerializer.Deserialize<LeaderboardData>(json);
            if (listData?.Entries is { Count: > 0 })
                return Normalize(listData.Entries, maxEntries);

            // Backward compatibility: migrate old "{ Best: number }" shape.
            var legacy = JsonSerializer.Deserialize<LegacyHighScoreData>(json);
            if (legacy?.Best > 0)
            {
                return
                [
                    new LeaderboardEntry
                    {
                        Name = DefaultName,
                        Score = legacy.Best,
                        DateUtc = null
                    }
                ];
            }

            return Array.Empty<LeaderboardEntry>();
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"[HighScoreStore] I/O error loading leaderboard: {ex}");
            return Array.Empty<LeaderboardEntry>();
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"[HighScoreStore] Access denied loading leaderboard: {ex}");
            return Array.Empty<LeaderboardEntry>();
        }
        catch (JsonException ex)
        {
            Debug.WriteLine($"[HighScoreStore] Invalid leaderboard JSON: {ex}");
            return Array.Empty<LeaderboardEntry>();
        }
        catch (NotSupportedException ex)
        {
            Debug.WriteLine($"[HighScoreStore] Unsupported leaderboard JSON schema: {ex}");
            return Array.Empty<LeaderboardEntry>();
        }
    }

    /// <summary>Returns current best score (top row score) or zero when no entries exist.</summary>
    public static int GetBestScore(int maxEntries)
    {
        return LoadLeaderboard(maxEntries).FirstOrDefault()?.Score ?? 0;
    }

    /// <summary>Checks whether a score can enter the configured Top-N leaderboard.</summary>
    public static bool Qualifies(int score, int maxEntries)
    {
        if (score <= 0) return false;
        var entries = LoadLeaderboard(maxEntries);
        if (entries.Count < maxEntries) return true;
        return score > entries[^1].Score;
    }

    /// <summary>
    /// Adds a new player entry or updates an existing player only when a better score is achieved.
    /// </summary>
    public static bool TryAddScore(string name, int score, int maxEntries)
    {
        if (score <= 0) return false;

        try
        {
            string path = GetPrimaryStoragePath();
            var entries = LoadLeaderboard(maxEntries).ToList();
            string normalizedName = NormalizePlayerName(name);
            string key = ToNameKey(normalizedName);
            var existing = entries.FirstOrDefault(e => ToNameKey(e.Name) == key);
            if (existing is null)
            {
                entries.Add(new LeaderboardEntry
                {
                    Name = normalizedName,
                    Score = score,
                    DateUtc = DateTime.UtcNow
                });
            }
            else
            {
                // Keep only the player's best score across runs.
                if (score <= existing.Score)
                    return false;
                existing.Name = normalizedName;
                existing.Score = score;
                existing.DateUtc = DateTime.UtcNow;
            }

            var normalized = Normalize(entries, maxEntries);
            var json = JsonSerializer.Serialize(new LeaderboardData { Entries = normalized }, JsonOptions);
            WriteAllTextAtomic(path, json);
            return true;
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"[HighScoreStore] I/O error saving leaderboard: {ex}");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"[HighScoreStore] Access denied saving leaderboard: {ex}");
            return false;
        }
        catch (JsonException ex)
        {
            Debug.WriteLine($"[HighScoreStore] JSON error saving leaderboard: {ex}");
            return false;
        }
        catch (NotSupportedException ex)
        {
            Debug.WriteLine($"[HighScoreStore] Unsupported leaderboard JSON schema while saving: {ex}");
            return false;
        }
    }

    private static void WriteAllTextAtomic(string path, string content)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        string tempPath = path + ".tmp";
        string backupPath = path + ".bak";
        File.WriteAllText(tempPath, content);

        if (File.Exists(path))
        {
            File.Replace(tempPath, path, backupPath, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(tempPath, path);
        }
    }

    private static bool IsReasonableSize(string path)
    {
        long bytes = new FileInfo(path).Length;
        if (bytes <= MaxFileBytes) return true;
        Debug.WriteLine($"[HighScoreStore] Ignoring oversized leaderboard ({bytes} bytes): {path}");
        return false;
    }

    private static string ResolveReadPath()
    {
        string primaryPath = GetPrimaryStoragePath();
        string legacyPath = GetLegacyStoragePath();
        TryMigrateLegacyToPrimary(primaryPath, legacyPath);
        if (File.Exists(primaryPath))
            return primaryPath;
        return legacyPath;
    }

    private static string GetPrimaryStoragePath()
    {
        string root = GetStorageRootPath();
        return Path.Combine(root, FileName);
    }

    private static string GetLegacyStoragePath()
    {
        string? baseDir = AppContext.BaseDirectory;
        if (string.IsNullOrWhiteSpace(baseDir))
            return Path.Combine(GetStorageRootPath(), FileName);
        return Path.Combine(baseDir, FileName);
    }

    private static string GetStorageRootPath()
    {
        if (!string.IsNullOrWhiteSpace(StorageRootOverride))
            return StorageRootOverride!;

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
            return AppContext.BaseDirectory ?? ".";
        return Path.Combine(appData, GameConfig.Persistence.AppFolderName);
    }

    private static void TryMigrateLegacyToPrimary(string primaryPath, string legacyPath)
    {
        if (string.Equals(primaryPath, legacyPath, StringComparison.OrdinalIgnoreCase)) return;
        if (File.Exists(primaryPath) || !File.Exists(legacyPath)) return;
        if (!IsReasonableSize(legacyPath)) return;

        try
        {
            string legacyJson = File.ReadAllText(legacyPath);
            WriteAllTextAtomic(primaryPath, legacyJson);
            Debug.WriteLine($"[HighScoreStore] Migrated leaderboard to AppData: {primaryPath}");
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"[HighScoreStore] Migration I/O error: {ex}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"[HighScoreStore] Migration access denied: {ex}");
        }
        catch (NotSupportedException ex)
        {
            Debug.WriteLine($"[HighScoreStore] Migration JSON schema unsupported: {ex}");
        }
    }

    private static List<LeaderboardEntry> Normalize(IEnumerable<LeaderboardEntry> entries, int maxEntries)
    {
        int cap = Math.Max(1, maxEntries);
        return entries
            .Where(e => e.Score > 0)
            .Select(e => new LeaderboardEntry
            {
                Name = NormalizePlayerName(e.Name),
                Score = e.Score,
                DateUtc = e.DateUtc
            })
            .GroupBy(e => ToNameKey(e.Name))
            .Select(group =>
            {
                // Guarantee one row per normalized player, keeping only best score.
                var best = group
                    .OrderByDescending(e => e.Score)
                    .ThenByDescending(e => e.DateUtc ?? DateTime.MinValue)
                    .First();
                return new LeaderboardEntry
                {
                    Name = best.Name,
                    Score = best.Score,
                    DateUtc = best.DateUtc
                };
            })
            .OrderByDescending(e => e.Score)
            .ThenByDescending(e => e.DateUtc ?? DateTime.MinValue)
            .Take(cap)
            .ToList();
    }

    private static string NormalizePlayerName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? DefaultName : name.Trim();

    private static string ToNameKey(string? name) =>
        NormalizePlayerName(name).ToUpperInvariant();

    public sealed class LeaderboardEntry
    {
        public string Name { get; set; } = DefaultName;
        public int Score { get; set; }
        public DateTime? DateUtc { get; set; }
    }

    private sealed class LeaderboardData
    {
        public List<LeaderboardEntry> Entries { get; set; } = [];
    }

    private sealed class LegacyHighScoreData
    {
        public int Best { get; set; }
    }
}
