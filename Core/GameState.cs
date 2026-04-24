using Game.Audio;
using Game.Entities;

namespace Game.Core;

/// <summary>Mutable run data: score, combo, flags, difficulty knobs, and input cadence timers.</summary>
public sealed class GameState
{
    private const int LeaderboardMaxEntries = GameConfig.Persistence.LeaderboardMaxEntries;
    public const int ComboMaxMultiplier = GameConfig.Scoring.ComboMaxMultiplier;
    public const int ComboTimeWindowFrames = GameConfig.Scoring.ComboTimeWindowFrames;

    public bool IsPlaying { get; set; }
    public bool IsGameOver { get; set; }
    public bool IsLifeLost { get; set; }
    public bool IsPaused { get; set; }
    public bool ShowLeaderboard { get; set; }
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
    public int DynamicMaxBarsOnScreen { get; set; }
    public float Difficulty01 { get; set; }
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

    public int MaxBarsOnScreen => DynamicMaxBarsOnScreen;

    /// <summary>Spawn countdown uses this so dev mode keeps spawns from feeling frantic.</summary>
    public int EffectiveSpawnIntervalFrames =>
        IsDevMode ? Math.Max(GameConfig.Difficulty.DevMinSpawnIntervalFrames, SpawnIntervalFrames) : SpawnIntervalFrames;

    /// <summary>Fewer simultaneous bars in dev mode for clearer testing.</summary>
    public int EffectiveMaxBarsOnScreen =>
        IsDevMode ? Math.Min(GameConfig.Difficulty.DevMaxBarsOnScreen, MaxBarsOnScreen) : MaxBarsOnScreen;

    /// <summary>Controlled pause transition that avoids invalid paused states.</summary>
    public void SetPaused(bool paused)
    {
        if (!IsPlaying || IsGameOver || IsLifeLost)
        {
            IsPaused = false;
            return;
        }

        IsPaused = paused;
    }

    /// <summary>Controlled playing-state transition.</summary>
    public void SetPlaying(bool playing)
    {
        IsPlaying = playing;
        if (!playing)
            IsPaused = false;
    }

    /// <summary>Controlled life-lost transition.</summary>
    public void SetLifeLost(bool lifeLost)
    {
        IsLifeLost = lifeLost;
        if (lifeLost)
            IsPaused = false;
    }

    /// <summary>Controlled game-over transition preserving existing behavior.</summary>
    public void SetGameOver()
    {
        IsGameOver = true;
        IsLifeLost = false;
        IsPaused = false;
        IsPlaying = false;
        ShowLeaderboard = true;
    }

    /// <summary>Resets all run-scoped state to new-game defaults.</summary>
    public void ResetRun(int initialBarSpeed, int initialSpawnIntervalFrames)
    {
        Score = 0;
        Lives = GameConfig.Scoring.StartingLives;
        IsGameOver = false;
        IsLifeLost = false;
        IsPaused = false;
        ShowLeaderboard = false;
        ElapsedFrames = 0;
        BarSpeed = initialBarSpeed;
        SpawnIntervalFrames = initialSpawnIntervalFrames;
        DynamicMaxBarsOnScreen = GameConfig.Difficulty.MinBarsOnScreen;
        Difficulty01 = 0f;
        SpawnCountdown = GameConfig.Spawn.InitialSpawnCountdownFrames;
        NextSpawnLaneX = null;
        _combo = 0;
        _lastDestroyFrame = -1;
        MaxCombo = 0;
        KillsInWindow = 0;
        LastFireTimeMs = 0;
        HighScore = HighScoreStore.GetBestScore(LeaderboardMaxEntries);
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

    /// <summary>Resets volatile state after a non-terminal life loss.</summary>
    public void ResetAfterLifeLost()
    {
        SpawnCountdown = GameConfig.Spawn.InitialSpawnCountdownFrames;
        NextSpawnLaneX = null;
        KillsInWindow = 0;
        MuzzleFlashFrames = 0;
        LifeLostFlashFrames = GameConfig.Effects.LifeLostFlashFrames;
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

    /// <summary>Clears shield-related transient visual state.</summary>
    public void ClearShieldFx()
    {
        ShieldPickupFlashFrames = 0;
        ShieldBlockFlashFrames = 0;
        ShieldBlockRingFrames = 0;
        ShieldImpactShakeFrames = 0;
    }

    /// <summary>Sets bomb detonation flash/shake feedback with max-preserving behavior.</summary>
    public void TriggerBombImpactFx(int flashFrames = GameConfig.Effects.BombScreenFlashFrames, int heavyShakeFrames = GameConfig.Effects.BombHeavyShakeFrames)
    {
        BombScreenFlashFrames = Math.Max(BombScreenFlashFrames, flashFrames);
        BombHeavyShakeFrames = Math.Max(BombHeavyShakeFrames, heavyShakeFrames);
    }

    /// <summary>Clears active bomb impact feedback values.</summary>
    public void ClearBombImpactFx()
    {
        BombScreenFlashFrames = 0;
        BombHeavyShakeFrames = 0;
    }

    /// <summary>~200–500 ms at game tick rate: random point in play rect, then fuse counts down each frame.</summary>
    public void BeginPendingBomb(Random rng, int playWidth, int playHeight)
    {
        int margin = GameConfig.Effects.BombIndicatorMargin;
        int innerW = Math.Max(1, playWidth - 2 * margin);
        int innerH = Math.Max(1, playHeight - 2 * margin);
        BombIndicatorX = margin + (float)rng.NextDouble() * innerW;
        BombIndicatorY = margin + (float)rng.NextDouble() * innerH;
        BombFuseFramesLeft = rng.Next(GameConfig.Effects.BombFuseMinFrames, GameConfig.Effects.BombFuseMaxExclusiveFrames);
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

    /// <summary>Advances general per-frame timers and decays transient state.</summary>
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

    /// <summary>Registers one bar destruction for score/combo/high-score and feedback timing.</summary>
    public void RegisterBarDestroyed(bool isSpecial = false)
    {
        if (_lastDestroyFrame >= 0 && ElapsedFrames - _lastDestroyFrame <= ComboTimeWindowFrames)
            _combo++;
        else
            _combo = 1;
        _lastDestroyFrame = ElapsedFrames;
        if (_combo > MaxCombo) MaxCombo = _combo;
        int mult = ComboMultiplier;
        int specialMult = isSpecial ? GameConfig.Scoring.SpecialBarScoreMultiplier : 1;
        Score += mult * specialMult;
        if (!IsNewBestThisRun && Score > PreviousBestScore)
        {
            IsNewBestThisRun = true;
            NewBestFlashFrames = GameConfig.Effects.NewBestFlashFrames;
        }
        if (Score > HighScore) HighScore = Score;
        KillsInWindow++;
        ShakeUntilTickMs = Environment.TickCount64 + GameConfig.Effects.DestroyShakeMs;
    }

    /// <summary>Consumes one life and returns true when lives are depleted.</summary>
    public bool LoseLife()
    {
        if (Lives > 0) Lives--;
        return Lives <= 0;
    }

    /// <summary>Transitions state into terminal game-over mode.</summary>
    public void ApplyGameOverShakeAndClearMuzzle()
    {
        SetGameOver();
        MuzzleFlashFrames = 0;
        ShakeUntilTickMs = Environment.TickCount64 + GameConfig.Effects.GameOverShakeMs;
    }

    /// <summary>Activates a newly collected power-up and initializes its duration/state.</summary>
    public void ActivatePowerUp(PowerUpType type)
    {
        ActivePowerUp = type;
        switch (type)
        {
            case PowerUpType.RapidFire:
                PowerUpTimerFrames = GameConfig.PowerUps.RapidFireDurationFrames;
                ActivePowerUpDurationFrames = PowerUpTimerFrames;
                break;
            case PowerUpType.MultiShot:
                PowerUpTimerFrames = GameConfig.PowerUps.MultiShotDurationFrames;
                ActivePowerUpDurationFrames = PowerUpTimerFrames;
                break;
            case PowerUpType.PiercingShot:
                PowerUpTimerFrames = GameConfig.PowerUps.PiercingDurationFrames;
                ActivePowerUpDurationFrames = PowerUpTimerFrames;
                break;
            case PowerUpType.SlowMotion:
                PowerUpTimerFrames = GameConfig.PowerUps.SlowMotionDurationFrames;
                ActivePowerUpDurationFrames = PowerUpTimerFrames;
                break;
            case PowerUpType.Shield:
                ShieldCharges = GameConfig.PowerUps.ShieldCharges;
                PowerUpTimerFrames = 0;
                ActivePowerUpDurationFrames = 0;
                ShieldPickupFlashFrames = Math.Max(ShieldPickupFlashFrames, GameConfig.PowerUps.ShieldPickupFlashFrames);
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

    /// <summary>Returns current fire cooldown after active modifiers and difficulty scaling.</summary>
    public int GetFireCooldownMs(int defaultMs) =>
        ComputeDifficultyScaledFireCooldown(defaultMs);

    /// <summary>
    /// Player movement uses fixed grid steps, so speed scaling is applied as a shorter step cooldown at higher difficulty.
    /// Formula: <c>cooldown = base - barSpeedTerm - scoreTerm</c>, then clamped for control.
    /// </summary>
    public int GetPlayerStepCooldownMs(int baseCooldownMs)
    {
        int barSpeedTerm = Math.Max(0, GetEffectiveBarSpeed() - 1) * 10;
        int scoreTerm = Math.Min(12, Score / 30);
        int adjusted = baseCooldownMs - barSpeedTerm - scoreTerm;
        return Math.Clamp(adjusted, 34, baseCooldownMs);
    }

    private int ComputeDifficultyScaledFireCooldown(int defaultMs)
    {
        int baseCooldown = ActivePowerUp == PowerUpType.RapidFire && PowerUpTimerFrames > 0
            ? Math.Max(30, defaultMs / 2)
            : defaultMs;
        int barSpeedTerm = Math.Max(0, GetEffectiveBarSpeed() - 1) * 4;
        int scoreTerm = Math.Min(10, Score / 45);
        int adjusted = baseCooldown - barSpeedTerm - scoreTerm;
        return Math.Clamp(adjusted, 24, defaultMs);
    }

    /// <summary>
    /// Pierce budget for new bullets while Piercing Shot is active. Value <c>2</c> allows each bullet to damage up to <b>three</b> bars before removal (see <c>Bullet.ConsumePierce</c>).
    /// </summary>
    public int GetPierceCount() =>
        ActivePowerUp == PowerUpType.PiercingShot && PowerUpTimerFrames > 0 ? GameConfig.PowerUps.PierceCount : 0;

    /// <summary>Returns true when multishot should apply to newly fired bullets.</summary>
    public bool IsMultiShotActive =>
        ActivePowerUp == PowerUpType.MultiShot && PowerUpTimerFrames > 0;

    /// <summary>Returns bar speed after active slow-motion modifier.</summary>
    public int GetEffectiveBarSpeed() =>
        ActivePowerUp == PowerUpType.SlowMotion && PowerUpTimerFrames > 0
            ? Math.Max(1, BarSpeed / GameConfig.PowerUps.SlowMotionDivisor)
            : BarSpeed;

    /// <summary>Consumes active shield charge and triggers shield-block feedback.</summary>
    public bool TryConsumeShield()
    {
        if (ShieldCharges <= 0) return false;
        ShieldCharges--;
        if (ShieldCharges == 0 && ActivePowerUp == PowerUpType.Shield)
        {
            ActivePowerUp = null;
            ActivePowerUpDurationFrames = 0;
        }
        ShieldBlockFlashFrames = Math.Max(ShieldBlockFlashFrames, GameConfig.PowerUps.ShieldBlockFlashFrames);
        ShieldBlockRingFrames = Math.Max(ShieldBlockRingFrames, GameConfig.PowerUps.ShieldBlockRingFrames);
        ShieldImpactShakeFrames = Math.Max(ShieldImpactShakeFrames, GameConfig.PowerUps.ShieldImpactShakeFrames);
        long now = Environment.TickCount64;
        ShakeUntilTickMs = Math.Max(ShakeUntilTickMs, now + GameConfig.PowerUps.ShieldBlockShakeMs);
        GameAudio.PlayShieldBlock();
        return true;
    }

}
