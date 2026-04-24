using System.Diagnostics;
using System.Text.Json;

namespace Game.Core;

/// <summary>
/// Optional runtime overrides loaded from a JSON file.
/// Missing file or invalid values fall back to <see cref="GameConfig"/> defaults.
/// </summary>
public sealed class GameRuntimeConfig
{
    private const string FileName = "gameconfig.json";
    private static readonly Lazy<GameRuntimeConfig> CurrentLazy = new(Load);

    public static GameRuntimeConfig Current => CurrentLazy.Value;

    public int CollisionLaneWidth { get; init; } = GameConfig.Bars.Width;
    public int AudioVariantCount { get; init; } = 8;
    public float DifficultyTimeScaleFrames { get; init; } = GameConfig.Difficulty.DifficultyTimeScaleFrames;

    private static GameRuntimeConfig Load()
    {
        string? baseDir = AppContext.BaseDirectory;
        if (string.IsNullOrWhiteSpace(baseDir))
            return new GameRuntimeConfig();

        string path = Path.Combine(baseDir, FileName);
        if (!File.Exists(path))
            return new GameRuntimeConfig();

        try
        {
            string json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<GameRuntimeConfig>(json);
            if (loaded is null)
                return new GameRuntimeConfig();

            return new GameRuntimeConfig
            {
                CollisionLaneWidth = Math.Max(1, loaded.CollisionLaneWidth),
                AudioVariantCount = Math.Clamp(loaded.AudioVariantCount, 2, 32),
                DifficultyTimeScaleFrames = Math.Max(1f, loaded.DifficultyTimeScaleFrames)
            };
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"[GameRuntimeConfig] I/O error loading config: {ex}");
            return new GameRuntimeConfig();
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"[GameRuntimeConfig] Access denied loading config: {ex}");
            return new GameRuntimeConfig();
        }
        catch (JsonException ex)
        {
            Debug.WriteLine($"[GameRuntimeConfig] Invalid JSON config: {ex}");
            return new GameRuntimeConfig();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GameRuntimeConfig] Unexpected error loading config: {ex}");
            return new GameRuntimeConfig();
        }
    }
}
