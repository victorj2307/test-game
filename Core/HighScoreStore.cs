using System.IO;
using System.Linq;
using System.Text.Json;
using System.Diagnostics;

namespace Game.Core;

public static class HighScoreStore
{
    private const string FileName = GameConfig.Persistence.HighScoreFileName;
    private const string DefaultName = GameConfig.Persistence.DefaultPlayerName;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Loads, normalizes, and trims leaderboard entries from local storage.</summary>
    public static IReadOnlyList<LeaderboardEntry> LoadLeaderboard(int maxEntries)
    {
        try
        {
            string? dir = AppContext.BaseDirectory;
            if (string.IsNullOrEmpty(dir)) return Array.Empty<LeaderboardEntry>();
            string path = Path.Combine(dir, FileName);
            if (!File.Exists(path)) return Array.Empty<LeaderboardEntry>();

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
        catch (Exception ex)
        {
            Debug.WriteLine($"[HighScoreStore] Unexpected error loading leaderboard: {ex}");
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
            string dir = AppContext.BaseDirectory ?? ".";
            string path = Path.Combine(dir, FileName);
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
        catch (Exception ex)
        {
            Debug.WriteLine($"[HighScoreStore] Unexpected error saving leaderboard: {ex}");
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
