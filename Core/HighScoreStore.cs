using System.IO;
using System.Text.Json;

namespace Game.Core;

public static class HighScoreStore
{
    private const string FileName = "highscore.json";

    public static int Load()
    {
        try
        {
            string? dir = AppContext.BaseDirectory;
            if (string.IsNullOrEmpty(dir)) return 0;
            string path = Path.Combine(dir, FileName);
            if (!File.Exists(path)) return 0;
            var json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<HighScoreData>(json);
            return data?.Best ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    public static void TrySaveIfBetter(int newScore, int currentBest)
    {
        if (newScore <= currentBest) return;
        try
        {
            string dir = AppContext.BaseDirectory ?? ".";
            string path = Path.Combine(dir, FileName);
            var json = JsonSerializer.Serialize(new HighScoreData { Best = newScore });
            File.WriteAllText(path, json);
        }
        catch
        {
            // ignore
        }
    }

    private sealed class HighScoreData
    {
        public int Best { get; set; }
    }
}
