using Game.Audio;
using Game.Entities;

namespace Game.Core;

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
    public bool IsLifeLost { get; set; }
    public bool IsPaused { get; set; }
    /// <summary>Relaxed difficulty, boosted drops, optional hotkeys; toggled at runtime (persists across new games).</summary>
    public bool IsDevMode { get; set; }
    public int Score { get; set; }
    public int ElapsedFrames { get; set; }
    public int HighScore { get; private set; }
    public int Lives { get; private set; }
    public int MaxCombo { get; private set; }
    public int PreviousBestScore { get; private set; }
    public bool IsNewBestThisRun { get; private set; }
    public int NewBestFlashFrames { get; private set; }

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
    public int LifeLostFlashFrames { get; set; }
    public int BarsSinceLastPowerUpDrop { get; private set; }
    public PowerUpType? ActivePowerUp { get; private set; }
    public int PowerUpTimerFrames { get; private set; }
    public int ActivePowerUpDurationFrames { get; private set; }
    public int ShieldCharges { get; private set; }

    /// <summary>Fuse frames remaining for a collected bomb (0 = none). At 0 after tick, explosion runs.</summary>
    public int BombFuseFramesLeft { get; private set; }

    /// <summary>Random detonation point in play space while fuse counts down.</summary>
    public float BombIndicatorX { get; private set; }

    public float BombIndicatorY { get; private set; }

    public bool HasPendingBombPickup => BombFuseFramesLeft > 0;

    /// <summary>Shield charge active (blocks one floor hit).</summary>
    public bool HasShieldActive => ShieldCharges > 0;

    /// <summary>Cyan pickup pulse on player after collecting shield.</summary>
    public int ShieldPickupFlashFrames { get; private set; }

    /// <summary>Brief cyan flash when shield absorbs a hit.</summary>
    public int ShieldBlockFlashFrames { get; private set; }

    /// <summary>Expanding ring frames at player when shield pops.</summary>
    public int ShieldBlockRingFrames { get; private set; }

    /// <summary>Short extra shake when shield absorbs a floor hit.</summary>
    public int ShieldImpactShakeFrames { get; private set; }

    /// <summary>Full-screen warm flash after bomb detonation; decremented in <see cref="AdvanceFrame"/>.</summary>
    public int BombScreenFlashFrames { get; private set; }

    /// <summary>Extra 2–3 px style shake from <see cref="RenderSystem"/> while &gt; 0.</summary>
    public int BombHeavyShakeFrames { get; private set; }

    public int ComboMultiplier => Math.Min(_combo, ComboMaxMultiplier);
    public int ComboStreak => _combo;

    public int MaxBarsOnScreen =>
        Math.Min(MaxBarsOnScreenHardCap, MaxBarsOnScreenBase + Score / MaxBarsOnScreenScoreStep);

    /// <summary>Spawn countdown uses this so dev mode keeps spawns from feeling frantic.</summary>
    public int EffectiveSpawnIntervalFrames =>
        IsDevMode ? Math.Max(72, SpawnIntervalFrames) : SpawnIntervalFrames;

    /// <summary>Fewer simultaneous bars in dev mode for clearer testing.</summary>
    public int EffectiveMaxBarsOnScreen =>
        IsDevMode ? Math.Min(4, MaxBarsOnScreen) : MaxBarsOnScreen;

    public void ResetRun(int initialBarSpeed, int initialSpawnIntervalFrames)
    {
        Score = 0;
        Lives = 3;
        IsGameOver = false;
        IsLifeLost = false;
        IsPaused = false;
        ElapsedFrames = 0;
        BarSpeed = initialBarSpeed;
        SpawnIntervalFrames = initialSpawnIntervalFrames;
        SpawnCountdown = 30;
        NextSpawnLaneX = null;
        _combo = 0;
        _lastDestroyFrame = -1;
        MaxCombo = 0;
        KillsInWindow = 0;
        LastFireTimeMs = 0;
        HighScore = HighScoreStore.Load();
        PreviousBestScore = HighScore;
        IsNewBestThisRun = false;
        NewBestFlashFrames = 0;
        MuzzleFlashFrames = 0;
        ShakeUntilTickMs = 0;
        LifeLostFlashFrames = 0;
        BarsSinceLastPowerUpDrop = 0;
        LastPlayerStepLeftMs = 0;
        LastPlayerStepRightMs = 0;
        LastDeltaSeconds = 0f;
        ActivePowerUp = null;
        PowerUpTimerFrames = 0;
        ActivePowerUpDurationFrames = 0;
        ShieldCharges = 0;
        ClearPendingBomb();
        ClearBombImpactFx();
        ClearShieldFx();
#if !DEBUG
        IsDevMode = false;
#endif
    }

    public void ResetAfterLifeLost()
    {
        SpawnCountdown = 30;
        NextSpawnLaneX = null;
        KillsInWindow = 0;
        MuzzleFlashFrames = 0;
        LifeLostFlashFrames = 18;
        LastFireTimeMs = 0;
        BarsSinceLastPowerUpDrop = 0;
        _combo = 0;
        _lastDestroyFrame = -1;
        ActivePowerUp = null;
        PowerUpTimerFrames = 0;
        ActivePowerUpDurationFrames = 0;
        ShieldCharges = 0;
        ClearPendingBomb();
        ClearBombImpactFx();
        ClearShieldFx();
    }

    public void ClearShieldFx()
    {
        ShieldPickupFlashFrames = 0;
        ShieldBlockFlashFrames = 0;
        ShieldBlockRingFrames = 0;
        ShieldImpactShakeFrames = 0;
    }

    public void TriggerBombImpactFx(int flashFrames = 14, int heavyShakeFrames = 22)
    {
        BombScreenFlashFrames = Math.Max(BombScreenFlashFrames, flashFrames);
        BombHeavyShakeFrames = Math.Max(BombHeavyShakeFrames, heavyShakeFrames);
    }

    public void ClearBombImpactFx()
    {
        BombScreenFlashFrames = 0;
        BombHeavyShakeFrames = 0;
    }

    /// <summary>~200–500 ms at game tick rate: random point in play rect, then fuse counts down each frame.</summary>
    public void BeginPendingBomb(Random rng, int playWidth, int playHeight)
    {
        const int margin = 28;
        int innerW = Math.Max(1, playWidth - 2 * margin);
        int innerH = Math.Max(1, playHeight - 2 * margin);
        BombIndicatorX = margin + (float)rng.NextDouble() * innerW;
        BombIndicatorY = margin + (float)rng.NextDouble() * innerH;
        BombFuseFramesLeft = rng.Next(12, 32);
    }

    public void ClearPendingBomb() => BombFuseFramesLeft = 0;

    /// <summary>Call once per sim frame while playing. Returns true the frame the fuse hits zero (run explosion).</summary>
    public bool TickBombFuse()
    {
        if (BombFuseFramesLeft <= 0) return false;
        BombFuseFramesLeft--;
        return BombFuseFramesLeft == 0;
    }

    /// <summary>Seconds since last simulation tick (clamped by <c>GameForm</c>). Exposed for debug HUD / future variable-step tuning.</summary>
    public float LastDeltaSeconds { get; private set; }

    public void AdvanceFrame(float deltaSeconds)
    {
        LastDeltaSeconds = deltaSeconds;
        ElapsedFrames++;
        if (MuzzleFlashFrames > 0) MuzzleFlashFrames--;
        if (LifeLostFlashFrames > 0) LifeLostFlashFrames--;
        if (NewBestFlashFrames > 0) NewBestFlashFrames--;
        if (BombScreenFlashFrames > 0) BombScreenFlashFrames--;
        if (BombHeavyShakeFrames > 0) BombHeavyShakeFrames--;
        if (ShieldPickupFlashFrames > 0) ShieldPickupFlashFrames--;
        if (ShieldBlockFlashFrames > 0) ShieldBlockFlashFrames--;
        if (ShieldBlockRingFrames > 0) ShieldBlockRingFrames--;
        if (ShieldImpactShakeFrames > 0) ShieldImpactShakeFrames--;
        if (PowerUpTimerFrames > 0)
        {
            PowerUpTimerFrames--;
            if (PowerUpTimerFrames == 0 && ActivePowerUp is not PowerUpType.Shield)
            {
                ActivePowerUp = null;
                ActivePowerUpDurationFrames = 0;
            }
        }
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
        if (_combo > MaxCombo) MaxCombo = _combo;
        int mult = ComboMultiplier;
        Score += mult;
        if (!IsNewBestThisRun && Score > PreviousBestScore)
        {
            IsNewBestThisRun = true;
            NewBestFlashFrames = 150;
        }
        if (Score > prevBest)
        {
            HighScore = Score;
            HighScoreStore.TrySaveIfBetter(Score, prevBest);
        }
        KillsInWindow++;
        ShakeUntilTickMs = Environment.TickCount64 + 120;
    }

    public bool LoseLife()
    {
        if (Lives > 0) Lives--;
        return Lives <= 0;
    }

    public void ApplyGameOverShakeAndClearMuzzle()
    {
        IsGameOver = true;
        IsLifeLost = false;
        IsPaused = false;
        IsPlaying = false;
        MuzzleFlashFrames = 0;
        ShakeUntilTickMs = Environment.TickCount64 + 280;
    }

    public void ActivatePowerUp(PowerUpType type)
    {
        ActivePowerUp = type;
        switch (type)
        {
            case PowerUpType.RapidFire:
                PowerUpTimerFrames = 320;
                ActivePowerUpDurationFrames = PowerUpTimerFrames;
                break;
            case PowerUpType.MultiShot:
                PowerUpTimerFrames = 220;
                ActivePowerUpDurationFrames = PowerUpTimerFrames;
                break;
            case PowerUpType.PiercingShot:
                PowerUpTimerFrames = 260;
                ActivePowerUpDurationFrames = PowerUpTimerFrames;
                break;
            case PowerUpType.SlowMotion:
                PowerUpTimerFrames = 320;
                ActivePowerUpDurationFrames = PowerUpTimerFrames;
                break;
            case PowerUpType.Shield:
                ShieldCharges = 1;
                PowerUpTimerFrames = 0;
                ActivePowerUpDurationFrames = 0;
                ShieldPickupFlashFrames = Math.Max(ShieldPickupFlashFrames, 22);
                GameAudio.PlayShieldPickup();
                break;
            case PowerUpType.BombShot:
                ActivePowerUp = null;
                ActivePowerUpDurationFrames = 0;
                PowerUpTimerFrames = 0;
                break;
        }
    }

    public void RegisterBarDestroyedForPowerUpRoll() => BarsSinceLastPowerUpDrop++;

    public void RegisterPowerUpDrop() => BarsSinceLastPowerUpDrop = 0;

    public int GetFireCooldownMs(int defaultMs) =>
        ActivePowerUp == PowerUpType.RapidFire && PowerUpTimerFrames > 0
            ? Math.Max(30, defaultMs / 2)
            : defaultMs;

    /// <summary>
    /// Pierce budget for new bullets while Piercing Shot is active. Value <c>2</c> allows each bullet to damage up to <b>three</b> bars before removal (see <c>Bullet.ConsumePierce</c>).
    /// </summary>
    public int GetPierceCount() =>
        ActivePowerUp == PowerUpType.PiercingShot && PowerUpTimerFrames > 0 ? 2 : 0;

    public bool IsMultiShotActive =>
        ActivePowerUp == PowerUpType.MultiShot && PowerUpTimerFrames > 0;

    public int GetEffectiveBarSpeed() =>
        ActivePowerUp == PowerUpType.SlowMotion && PowerUpTimerFrames > 0
            ? Math.Max(1, BarSpeed / 2)
            : BarSpeed;

    public bool TryConsumeShield()
    {
        if (ShieldCharges <= 0) return false;
        ShieldCharges--;
        if (ShieldCharges == 0 && ActivePowerUp == PowerUpType.Shield)
        {
            ActivePowerUp = null;
            ActivePowerUpDurationFrames = 0;
        }
        ShieldBlockFlashFrames = Math.Max(ShieldBlockFlashFrames, 14);
        ShieldBlockRingFrames = Math.Max(ShieldBlockRingFrames, 18);
        ShieldImpactShakeFrames = Math.Max(ShieldImpactShakeFrames, 12);
        long now = Environment.TickCount64;
        ShakeUntilTickMs = Math.Max(ShakeUntilTickMs, now + 140);
        GameAudio.PlayShieldBlock();
        return true;
    }

}
