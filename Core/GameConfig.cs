namespace Game.Core;

/// <summary>
/// Centralized tuning values for gameplay, difficulty pacing, effects, and UI visuals.
/// Keep values behavior-compatible unless intentionally rebalancing.
/// </summary>
public static class GameConfig
{
    /// <summary>
    /// Save-file and leaderboard input settings.
    /// </summary>
    public static class Persistence
    {
        public const string AppFolderName = "RetroArcade"; // AppData subfolder used for local game persistence.
        public const int LeaderboardMaxEntries = 10; // Number of leaderboard rows kept after sorting.
        public const int MaxLeaderboardFileBytes = 64 * 1024; // Defensive size cap for leaderboard JSON loading.
        public const string HighScoreFileName = "highscore.json"; // Local filename used for persisted leaderboard data.
        public const string DefaultPlayerName = "PLAYER"; // Fallback nickname when user input is empty/whitespace.
        public const int NameInputMaxLength = 16; // Maximum nickname length accepted in the game-over prompt.
    }

    /// <summary>
    /// Core score/lives/combo balance knobs.
    /// </summary>
    public static class Scoring
    {
        public const int DamagePerHit = 20; // Height damage dealt to a bar by one normal bullet hit.
        public const int ComboMaxMultiplier = 4; // Hard cap for combo score multiplier growth.
        public const int ComboTimeWindowFrames = 90; // Max frame gap between destroys to keep combo alive.
        public const int SpecialBarScoreMultiplier = 2; // Extra score factor when destroying a special bar.
        public const int StartingLives = 3; // Lives granted at the start of a new run.
    }

    /// <summary>
    /// Player geometry, movement cadence, and bullet spawn defaults.
    /// </summary>
    public static class Player
    {
        public const int Width = 48; // Player ship width in pixels.
        public const int Height = 18; // Player ship height in pixels.
        public const int MuzzlePortWidth = 8; // Decorative muzzle-port width used by rendering.
        public const int MuzzlePortYOffset = 2; // How far above player top the muzzle port is drawn.
        public const int MuzzlePortHeight = 2; // Decorative muzzle-port thickness in pixels.
        public const int FireCooldownMs = 100; // Base time between shots before power-up/difficulty modifiers.
        public const int StepCooldownMs = 75; // Base time between horizontal movement steps.
        public const float MultiShotSpreadDrift = 0.85f; // Horizontal velocity drift for side bullets in multishot.
        public const int MultiShotOffsetX = 10; // Horizontal spawn offset for left/right multishot bullets.
        public const int BulletWidth = 4; // Standard bullet width.
        public const int BulletHeight = 10; // Standard bullet height.
        public const int BulletSpeed = 10; // Standard bullet upward speed per frame.
        public const int PiercingBulletSizeBoost = 2; // Added width/height for piercing bullets.
        public const int MuzzleFlashFrames = 2; // Frames of muzzle flash after firing.
        public const int MinPlayHeightPadding = 40; // Minimum headroom check to avoid running update in tiny playfields.
    }

    /// <summary>
    /// Spawn flow and placement behavior for bars.
    /// </summary>
    public static class Spawn
    {
        public const int InitialSpawnCountdownFrames = 30; // Startup delay before first spawn when a run begins/resets.
        public const int RetryFramesWhenNoFit = 8; // Delay before retry when no valid spawn slot is found.
        public const int PlacementAttempts = 10; // Random placement attempts before fallback scan.
        public const int HorizontalPadding = 8; // Required lane spacing between bars on spawn.
        public const float ClusterChance = 0.15f; // Chance that a successful spawn also creates nearby extras.
        public const float LaneChance = 0.10f; // Chance to reuse same X lane for the next primary spawn.
        public const int ClusterExtraMin = 1; // Minimum extra bars when cluster pattern triggers.
        public const int ClusterExtraMaxExclusive = 3; // Exclusive max (produces 1..2 extras).
        public const int ClusterOffsetMin = -24; // Minimum random X offset for clustered bars.
        public const int ClusterOffsetMaxExclusive = 25; // Exclusive maximum random X offset for clusters.
        public const int FallbackHeight = 40; // Height used by last-resort spawn fallback path.
        public const int MinBarHeightClamp = 20; // Safety minimum clamp for bar height.
        public const int MaxBarHeightClamp = 300; // Safety maximum clamp for bar height.
        public const int InitialSpawnIntervalFrames = 90; // Base spawn interval before difficulty interpolation.
    }

    /// <summary>
    /// Bar dimensions, type odds, per-type behavior, and hit-response timing.
    /// </summary>
    public static class Bars
    {
        public const int Width = 24; // Standard bar width used for collision lanes and spawn spacing.
        public const int MinHeight = 60; // Normal-bar minimum height.
        public const int MaxHeight = 200; // Normal-bar maximum height.
        public const int FastMinHeight = 40; // Fast-bar minimum height.
        public const int FastMaxHeightExclusive = 101; // Fast-bar exclusive max height.
        public const int TankMinHeight = 120; // Tank-bar minimum height.
        public const int TankMaxHeightExclusive = 201; // Tank-bar exclusive max height.
        public const float FastSpeedScale = 1.4f; // Multiplier applied to effective global speed for fast bars.
        public const float TankSpeedScale = 0.75f; // Multiplier applied to effective global speed for tank bars.
        public const float NormalSpeedScale = 1f; // Multiplier for normal bars.
        public const double SpecialChance = 0.12; // Probability that a newly spawned bar is marked special.
        public const int RollNormalThreshold = 50; // Roll < threshold => Normal bar.
        public const int RollFastThreshold = 80; // Roll < threshold => Fast bar (else Tank).
        public const float SpeedLerpFactor = 0.14f; // Per-frame blend factor toward target bar speed.
        public const float SpeedSnapEpsilon = 0.02f; // Snap threshold to avoid tiny oscillations near target speed.
        public const int HitFlashFrames = 6; // Regular hit flash duration.
        public const int HitPulseFrames = 4; // Regular hit pulse (inflate) duration.
        public const int PierceHitFlashFrames = 14; // Piercing hit flash duration.
        public const int PierceHitPulseFrames = 5; // Piercing hit pulse duration.
    }

    /// <summary>
    /// Power-up drop behavior, durations, and strength modifiers.
    /// </summary>
    public static class PowerUps
    {
        public const double DropChance = 0.10; // Base probability of dropping a power-up when a bar is destroyed.
        public const int ForceDropAfterBars = 12; // Guarantees a drop after this many misses (normal mode).
        public const double DevDropChance = 0.55; // Elevated drop probability used in dev mode.
        public const int DevForceDropAfterBars = 3; // Faster force-drop cadence used in dev mode.
        public const int DropSize = 12; // Power-up pickup circle size.
        public const int DropFallSpeed = 2; // Falling speed of dropped power-ups.
        public const int DropYDivisor = 3; // Vertical offset divisor from source bar for initial power-up Y.
        public const int OffscreenCullPadding = 20; // Extra bottom margin before culling dropped power-ups.
        public const int RapidFireDurationFrames = 320; // Rapid Fire active duration.
        public const int MultiShotDurationFrames = 220; // Multi Shot active duration.
        public const int PiercingDurationFrames = 260; // Piercing Shot active duration.
        public const int SlowMotionDurationFrames = 320; // Slow Motion active duration.
        public const int ShieldCharges = 1; // Number of floor hits shield can block before expiring.
        public const int ShieldPickupFlashFrames = 22; // Pickup flash duration when shield is collected.
        public const int ShieldBlockFlashFrames = 14; // Flash duration when shield blocks a hit.
        public const int ShieldBlockRingFrames = 18; // Expanding shield ring duration on block.
        public const int ShieldImpactShakeFrames = 12; // Extra shake frames after shield block.
        public const int ShieldBlockShakeMs = 140; // Base shake extension in milliseconds after shield block.
        public const int PierceCount = 2; // Pierce budget; 2 means up to three total impacted bars per bullet.
        /// <summary>Multiplies derived bar fall speed (pixels/frame) while Slow Motion is active; paired with <see cref="SlowMotionMinPixelsPerFrame"/>.</summary>
        public const float SlowMotionPixelSpeedScale = 0.46f;
        /// <summary>Lower clamp for bar speed during slow motion so motion stays smooth but clearly below normal.</summary>
        public const float SlowMotionMinPixelsPerFrame = 0.36f;
        /// <summary>Faster blend toward the slowed target so the effect is felt within a few frames.</summary>
        public const float SlowMotionSpeedLerpFactor = 0.38f;
    }

    /// <summary>
    /// Difficulty interpolation bounds, smoothing, and table keyframes.
    /// </summary>
    public static class Difficulty
    {
        public const int MinBarSpeed = 1; // Lower clamp for interpolated bar speed.
        public const int MaxBarSpeed = 4; // Upper clamp for interpolated bar speed.
        public const int DevMaxBarSpeed = 2; // Dev-mode cap for bar speed.
        public const int MinSpawnIntervalFrames = 34; // Fastest allowed spawn interval.
        public const int MaxSpawnIntervalFrames = 90; // Slowest allowed spawn interval.
        public const int MinBarsOnScreen = 3; // Lower bound of concurrent bar cap.
        public const int MaxBarsOnScreen = 9; // Upper bound of concurrent bar cap.
        public const int DevMaxBarsOnScreen = 4; // Dev-mode cap for concurrent bars.
        public const int DevMinSpawnIntervalFrames = 72; // Dev-mode floor for effective spawn interval.
        public const float BarSpeedBlend = 0.20f; // Smoothing alpha toward target bar speed each tick.
        public const float SpawnIntervalBlend = 0.16f; // Smoothing alpha toward target spawn interval.
        public const float MaxBarsBlend = 0.14f; // Smoothing alpha toward target max-bars cap.
        public const float DifficultyTimeScaleFrames = 60f; // Frames-per-second assumption for frame->seconds conversion.
        public const float InterpolationSpanEpsilon = 0.001f; // Minimum segment span to prevent divide-by-zero during interpolation.

        public static readonly DifficultyEntry[] Table =
        [
            new(0f,   0.85f, 90f, 3.0f), // Opening pace.
            new(30f,  0.98f, 85f, 3.3f), // Gentle early ramp to preserve onboarding control.
            new(60f,  1.12f, 79f, 3.8f),
            new(90f,  1.30f, 73f, 4.2f),
            new(120f, 1.48f, 68f, 4.6f), // Mid-game tuned to avoid stacked spikes.
            new(150f, 1.64f, 63f, 5.0f),
            new(180f, 1.78f, 59f, 5.4f),
            new(210f, 1.92f, 55f, 5.8f),
            new(240f, 2.04f, 52f, 6.1f),
            new(270f, 2.14f, 49f, 6.4f),
            new(300f, 2.22f, 47f, 6.6f),
            new(330f, 2.28f, 46f, 6.7f)  // Late-game challenge plateau: hard but stable.
        ];

        public readonly record struct DifficultyEntry(
            float Seconds, // Elapsed run time in seconds for this keyframe.
            float BarSpeed, // Target global bar speed at this keyframe.
            float SpawnIntervalFrames, // Target spawn cadence at this keyframe.
            float MaxBarsOnScreen); // Target concurrent-bar cap at this keyframe.
    }

    /// <summary>
    /// Gameplay impact feedback, bomb behavior, and effect lifetimes.
    /// </summary>
    public static class Effects
    {
        public const int BombRadius = 120; // Bomb splash radius used for secondary damage checks.
        public const int BombSplashDamage = 85; // Damage dealt to bars inside bomb splash area.
        public const int BombSoleBarDamage = 400; // Damage used when bomb affects only one bar.
        public const int BombSingleTargetBonusDamage = 80; // Extra damage added for single-target bomb case.
        public const float BombKillFraction = 0.72f; // Fraction of ranked bars selected for staged bomb kills.
        public const int BombKillDelayStartFrames = 2; // Initial delay before first queued bomb kill.
        public const int BombKillStaggerFrames = 3; // Frame spacing between queued bomb kills.
        public const int MaxActiveFragments = 320; // Global cap for active fragments to protect performance.
        public const int BombExplosionShakeMs = 320; // Base shake duration on bomb detonation.
        public const int BombScreenFlashFrames = 14; // Bomb full-screen flash duration.
        public const int BombHeavyShakeFrames = 22; // Heavy bomb shake frame duration.
        public const int LifeLostShakeMs = 180; // Shake duration when a life is lost.
        public const int FloorHitDevShakeMs = 80; // Dev-mode shake when a floor hit is ignored.
        public const int GameOverShakeMs = 280; // Shake duration when game over is triggered.
        /// <summary>Simulation speed at the start of last-life cannon destruction (lerps up to 1).</summary>
        public const float FinalDeathTimeScaleMin = 0.18f;
        /// <summary>Wall-clock milliseconds to ease <see cref="FinalDeathTimeScaleMin"/> → 1 before game over (~2s for dramatic pacing).</summary>
        public const int FinalDeathTimeScaleRecoverMs = 2000;
        /// <summary>Brief full-screen flash when last life is lost (destruction start).</summary>
        public const int FinalDeathFlashFrames = 10;
        /// <summary>Short shake while the final-death animation plays.</summary>
        public const int FinalDeathShakeMs = 220;
        /// <summary>Rectangular debris count for last-life cannon detonation (grid + radial + sparks).</summary>
        public const int CannonDestructionFragmentCount = 40;
        /// <summary>Omnidirectional spark particles at cannon center for last-life burst.</summary>
        public const int CannonDestructionParticleCount = 52;
        /// <summary>Hull-shard outward speed range (pixels/frame at 60 FPS baseline).</summary>
        public const float CannonDestructionChunkSpeedMin = 6.5f;
        public const float CannonDestructionChunkSpeedMax = 14.5f;
        /// <summary>Radial blast shard speed range (larger outward motion).</summary>
        public const float CannonDestructionBlastSpeedMin = 9f;
        public const float CannonDestructionBlastSpeedMax = 21f;
        /// <summary>Gravity for cannon-breakup fragments (slightly floaty).</summary>
        public const float CannonDestructionFragmentGravity = 0.17f;
        public const int DestroyShakeMs = 120; // Shake pulse duration on bar destroy.
        public const int SpawnRelaxAfterLifeLostFrames = 6; // Spawn interval relief applied after losing a life.
        public const int LifeLostFlashFrames = 18; // Life-lost overlay flash duration.
        public const int NewBestFlashFrames = 150; // "New best" banner lifetime.
        public const int BombIndicatorMargin = 28; // Margin from playfield edges for bomb indicator placement.
        public const int BombFuseMinFrames = 12; // Minimum bomb fuse length.
        public const int BombFuseMaxExclusiveFrames = 32; // Exclusive maximum bomb fuse length.
    }

    /// <summary>
    /// Rendering-only tunables for special bars and background visuals.
    /// </summary>
    public static class Visual
    {
        public const float SpecialBarPulseRate = 0.013f; // Time-rate multiplier for special bar fill pulse.
        public const float SpecialBarPulsePhaseByX = 0.1f; // Per-bar X-based phase offset for pulse desynchronization.
        public const float SpecialBarNeonMix = 0.78f; // Blend amount from health color toward neon base color.
        public const float SpecialBarBrightnessBase = 0.16f; // Base brightness contribution for special pulse.
        public const float SpecialBarBrightnessRange = 0.24f; // Additional pulsing brightness range.
        public const float SpecialOutlinePulseRate = 0.24f; // Pulse speed for special bar outline animation.
        public const int SpecialOutlineAlphaBase = 140; // Base alpha for special bar outline.
        public const int SpecialOutlineAlphaRange = 85; // Pulsing alpha range for outline.
        public const int SpecialOutlineGreenBase = 55; // Base green channel for outline color pulse.
        public const int SpecialOutlineGreenRange = 55; // Green channel pulse range.
        public const int SpecialOutlineBlueBase = 220; // Base blue channel for outline color pulse.
        public const int SpecialOutlineBlueRange = 35; // Blue channel pulse range.
        public const int BombGridStep = 40; // Synthwave background grid spacing in pixels.
    }

    /// <summary>
    /// Window host and frame-timing guardrails for WinForms loop stability.
    /// </summary>
    public static class Ui
    {
        public const string WindowTitle = "RetroArcade"; // Main form title for branding consistency.
        public const int TargetFps = 60; // Baseline used for delta-time scaling to preserve gameplay feel.
        public const int WindowWidth = 480; // Default form client width.
        public const int WindowHeight = 640; // Default form client height.
        public const int GameTimerIntervalMs = 16; // Timer interval targeting ~60 updates per second.
        public const int MinPlayHeight = 32; // Minimum allowed gameplay viewport height.
        public const float MinDeltaSeconds = 1f / 500f; // Lower clamp for frame delta to avoid tiny-step instability.
        public const float MaxDeltaSeconds = 0.25f; // Upper clamp for frame delta to avoid huge-step spikes.
        public const int FpsWindowMs = 1000; // Rolling window size for FPS counter updates.
    }
}
