namespace RetroArcade;

/// <summary>Mutable run data: score, combo, flags, difficulty knobs, and input cadence timers.</summary>
public sealed class GameState
{
    public const int ComboMaxMultiplier = 4;
    public const int ComboTimeWindowFrames = 90;
    private const int MaxBarsOnScreenBase = 3;
    private const int MaxBarsOnScreenScoreStep = 5;
    private const int MaxBarsOnScreenHardCap = 10;

    public bool IsPlaying { get; set; }
    public bool IsGameOver { get; set; }
    public int Score { get; set; }
    public int ElapsedFrames { get; set; }
    public int HighScore { get; private set; }

    private int _combo;
    private int _lastDestroyFrame = -1;
    public int KillsInWindow { get; set; }

    public int BarSpeed { get; set; }
    public int SpawnCountdown { get; set; }
    public int SpawnIntervalFrames { get; set; }
    public int? NextSpawnLaneX { get; set; }

    public long LastFireTimeMs { get; set; }
    public long LastPlayerStepLeftMs { get; set; }
    public long LastPlayerStepRightMs { get; set; }

    public int MuzzleFlashFrames { get; set; }
    public long ShakeUntilTickMs { get; set; }

    public int ComboMultiplier => Math.Min(_combo, ComboMaxMultiplier);

    /// <summary>Raw streak length for HUD (e.g. show COMBO when &gt; 1).</summary>
    public int ComboStreak => _combo;

    public int MaxBarsOnScreen =>
        Math.Min(MaxBarsOnScreenHardCap, MaxBarsOnScreenBase + Score / MaxBarsOnScreenScoreStep);

    public void ResetRun(int initialBarSpeed, int initialSpawnIntervalFrames)
    {
        Score = 0;
        IsGameOver = false;
        ElapsedFrames = 0;
        BarSpeed = initialBarSpeed;
        SpawnIntervalFrames = initialSpawnIntervalFrames;
        SpawnCountdown = 30;
        NextSpawnLaneX = null;
        _combo = 0;
        _lastDestroyFrame = -1;
        KillsInWindow = 0;
        LastFireTimeMs = 0;
        HighScore = HighScoreStore.Load();
        MuzzleFlashFrames = 0;
        ShakeUntilTickMs = 0;
        LastPlayerStepLeftMs = 0;
        LastPlayerStepRightMs = 0;
    }

    public void AdvanceFrame()
    {
        ElapsedFrames++;
        if (MuzzleFlashFrames > 0) MuzzleFlashFrames--;
        if (_lastDestroyFrame >= 0 && ElapsedFrames - _lastDestroyFrame > ComboTimeWindowFrames)
        {
            _combo = 0;
            _lastDestroyFrame = -1;
        }
    }

    public void RegisterBarDestroyed()
    {
        int prevBest = HighScore;
        if (_lastDestroyFrame >= 0 && ElapsedFrames - _lastDestroyFrame <= ComboTimeWindowFrames)
            _combo++;
        else
            _combo = 1;
        _lastDestroyFrame = ElapsedFrames;
        int mult = ComboMultiplier;
        Score += mult;
        if (Score > prevBest)
        {
            HighScore = Score;
            HighScoreStore.TrySaveIfBetter(Score, prevBest);
        }
        KillsInWindow++;
        ShakeUntilTickMs = Environment.TickCount64 + 120;
    }

    public void ApplyGameOverShakeAndClearMuzzle()
    {
        IsGameOver = true;
        IsPlaying = false;
        MuzzleFlashFrames = 0;
        ShakeUntilTickMs = Environment.TickCount64 + 280;
    }
}
