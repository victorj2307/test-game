using System.Drawing;
using Game.Core;
using Game.Entities;

namespace Game.Systems;

/// <summary>Bar placement, cluster/lane patterns, max-on-screen cap, and player-reach filtering.</summary>
public sealed class SpawnSystem
{
    public const int BarWidth = GameConfig.Bars.Width;
    private static int PlayerStepPixels => GameConfig.Bars.Width / 2;

    private readonly GameState _state;
    private readonly EntityManager _entities;
    private readonly Random _random;

    public SpawnSystem(GameState state, EntityManager entities, Random random)
    {
        _state = state;
        _entities = entities;
        _random = random;
    }

    /// <summary>
    /// Attempts to spawn one primary bar (plus optional pattern extras) when countdown reaches zero.
    /// <paramref name="playerCenterX"/> drives reachability filtering near the concurrent bar cap.
    /// </summary>
    public void TrySpawn(int clientWidth, float playerCenterX)
    {
        if (--_state.SpawnCountdown > 0) return;

        int w = Math.Min(BarWidth, clientWidth);
        if (w < 1) w = 1;

        if (_entities.Bars.Count >= _state.EffectiveMaxBarsOnScreen)
        {
            _state.SpawnCountdown = _state.EffectiveSpawnIntervalFrames;
            return;
        }

        int? forcedX = _state.NextSpawnLaneX;
        _state.NextSpawnLaneX = null;

        if (TrySpawnOneBar(clientWidth, w, forcedX, playerCenterX, out int placedX, out bool usedLane))
        {
            bool doCluster = _random.NextDouble() < GameConfig.Spawn.ClusterChance;
            if (doCluster)
            {
                int extra = _random.Next(GameConfig.Spawn.ClusterExtraMin, GameConfig.Spawn.ClusterExtraMaxExclusive);
                for (int c = 0; c < extra; c++)
                {
                    int ox = _random.Next(GameConfig.Spawn.ClusterOffsetMin, GameConfig.Spawn.ClusterOffsetMaxExclusive);
                    int nx = Math.Clamp(placedX + ox, 0, clientWidth - w);
                    BarType t = RollBarType();
                    int h = RollHeight(t);
                    TryCreateBarAt(nx, w, h, t, clientWidth, playerCenterX, setCountdown: false);
                }
            }
            if (!usedLane && _random.NextDouble() < GameConfig.Spawn.LaneChance)
                _state.NextSpawnLaneX = Math.Clamp(placedX, 0, clientWidth - w);

            _state.SpawnCountdown = _state.EffectiveSpawnIntervalFrames;
            return;
        }

        for (int attempt = 0; attempt < GameConfig.Spawn.PlacementAttempts; attempt++)
        {
            int x = PickCandidateX(clientWidth, w, playerCenterX);
            BarType t = RollBarType();
            int h = RollHeight(t);
            if (TryCreateBarAt(x, w, h, t, clientWidth, playerCenterX, setCountdown: true)) return;
        }
        for (int x = 0; x <= clientWidth - w; x++)
        {
            if (TryCreateBarAt(x, w, GameConfig.Spawn.FallbackHeight, BarType.Fast, clientWidth, playerCenterX, setCountdown: true)) return;
        }
        _state.SpawnCountdown = GameConfig.Spawn.RetryFramesWhenNoFit;
    }

    private bool TrySpawnOneBar(int clientWidth, int w, int? forcedX, float playerCenterX, out int placedX, out bool usedLane)
    {
        placedX = 0;
        usedLane = false;
        if (forcedX is int fx)
        {
            BarType t = RollBarType();
            int h = RollHeight(t);
            if (TryCreateBarAt(fx, w, h, t, clientWidth, playerCenterX, setCountdown: false))
            {
                placedX = fx;
                usedLane = true;
                return true;
            }
        }
        for (int attempt = 0; attempt < GameConfig.Spawn.PlacementAttempts; attempt++)
        {
            int x = PickCandidateX(clientWidth, w, playerCenterX);
            BarType t = RollBarType();
            int h = RollHeight(t);
            if (TryCreateBarAt(x, w, h, t, clientWidth, playerCenterX, setCountdown: false))
            {
                placedX = x;
                return true;
            }
        }
        for (int x = 0; x <= clientWidth - w; x++)
        {
            if (TryCreateBarAt(x, w, GameConfig.Spawn.FallbackHeight, BarType.Fast, clientWidth, playerCenterX, setCountdown: false))
            {
                placedX = x;
                return true;
            }
        }
        return false;
    }

    private int PickCandidateX(int clientWidth, int w, float playerCenterX)
    {
        if (clientWidth <= w) return 0;
        if (!IsUnderSpawnPressure())
            return _random.Next(0, clientWidth - w + 1);

        int half = ComputeReachHalfWidthPx(clientWidth);
        int minX = (int)MathF.Floor(playerCenterX - half - w * 0.5f);
        int maxX = (int)MathF.Ceiling(playerCenterX + half - w * 0.5f);
        minX = Math.Clamp(minX, 0, clientWidth - w);
        maxX = Math.Clamp(maxX, 0, clientWidth - w);
        if (maxX < minX) return Math.Clamp((int)MathF.Round(playerCenterX - w * 0.5f), 0, clientWidth - w);
        return _random.Next(minX, maxX + 1);
    }

    private BarType RollBarType()
    {
        int r = _random.Next(100);
        if (r < GameConfig.Bars.RollNormalThreshold) return BarType.Normal;
        if (r < GameConfig.Bars.RollFastThreshold) return BarType.Fast;
        return BarType.Tank;
    }

    private int RollHeight(BarType type) => type switch
    {
        BarType.Fast => _random.Next(GameConfig.Bars.FastMinHeight, GameConfig.Bars.FastMaxHeightExclusive),
        BarType.Tank => _random.Next(GameConfig.Bars.TankMinHeight, GameConfig.Bars.TankMaxHeightExclusive),
        _ => _random.Next(GameConfig.Bars.MinHeight, GameConfig.Bars.MaxHeight + 1),
    };

    private static float TypeSpeedScale(BarType type) => type switch
    {
        BarType.Fast => GameConfig.Bars.FastSpeedScale,
        BarType.Tank => GameConfig.Bars.TankSpeedScale,
        _ => GameConfig.Bars.NormalSpeedScale,
    };

    private bool TryCreateBarAt(int x, int w, int h, BarType type, int clientWidth, float playerCenterX, bool setCountdown)
    {
        if (_entities.Bars.Count >= _state.EffectiveMaxBarsOnScreen) return false;

        h = Math.Clamp(h, GameConfig.Spawn.MinBarHeightClamp, GameConfig.Spawn.MaxBarHeightClamp);
        w = Math.Min(w, clientWidth);
        if (w < 1) return false;
        x = Math.Clamp(x, 0, Math.Max(0, clientWidth - w));
        if (!IsReachablePlacement(x, w, clientWidth, playerCenterX)) return false;
        if (HasHorizontalSpacingConflict(x, w, _entities.Bars)) return false;
        int spawnY = ComputeSpawnY(x, w, h, _entities.Bars);
        var candidate = new Rectangle(x, spawnY, w, h);
        if (BarBoundsOverlapAnyExisting(candidate, _entities.Bars)) return false;
        float sc = TypeSpeedScale(type);
        bool isSpecial = _random.NextDouble() < GameConfig.Bars.SpecialChance;
        _entities.Bars.Add(new Bar(x, spawnY, w, h, type, isSpecial, h, sc, _state.GetEffectiveBarSpeed(), _state.SlowMotionBarSpeedScale));
        if (setCountdown) _state.SpawnCountdown = _state.EffectiveSpawnIntervalFrames;
        return true;
    }

    /// <summary>
    /// Rolls and spawns a collectible drop after a bar destroy, including force-drop behavior.
    /// </summary>
    public void TrySpawnPowerUpAt(Rectangle sourceBounds)
    {
        _state.RegisterBarDestroyedForPowerUpRoll();
        int forceAfter = _state.IsDevMode ? GameConfig.PowerUps.DevForceDropAfterBars : GameConfig.PowerUps.ForceDropAfterBars;
        double dropChance = _state.IsDevMode ? GameConfig.PowerUps.DevDropChance : GameConfig.PowerUps.DropChance;
        bool forceDrop = _state.BarsSinceLastPowerUpDrop >= forceAfter;
        if (!forceDrop && _random.NextDouble() > dropChance) return;
        int size = GameConfig.PowerUps.DropSize;
        int x = sourceBounds.X + (sourceBounds.Width - size) / 2;
        int y = sourceBounds.Y + Math.Max(0, sourceBounds.Height / GameConfig.PowerUps.DropYDivisor);
        var type = (PowerUpType)_random.Next(0, Enum.GetValues<PowerUpType>().Length);
        _entities.PowerUps.Add(new PowerUp(x, y, size, GameConfig.PowerUps.DropFallSpeed, type));
        _state.RegisterPowerUpDrop();
    }

    private bool IsUnderSpawnPressure() =>
        _entities.Bars.Count >= Math.Max(0, _state.EffectiveMaxBarsOnScreen - GameConfig.Spawn.ReachabilityPressureSlotsFromCap);

    /// <summary>
    /// Reachable half-width ≈ fall time × player traverse speed × safety factor.
    /// </summary>
    internal int ComputeReachHalfWidthPx(int clientWidth)
    {
        int barSpeed = Math.Max(1, _state.GetEffectiveBarSpeed());
        float fallSpeedPxPerSec = barSpeed * GameConfig.Ui.TargetFps;
        float fallTimeSec = GameConfig.Spawn.ReachabilityFallDistancePx / fallSpeedPxPerSec;
        int stepCooldownMs = Math.Max(1, _state.GetPlayerStepCooldownMs(GameConfig.Player.StepCooldownMs));
        float playerPxPerSec = PlayerStepPixels * 1000f / stepCooldownMs;
        int half = (int)MathF.Round(fallTimeSec * playerPxPerSec * GameConfig.Spawn.ReachabilitySafetyFactor);
        half = Math.Clamp(half, GameConfig.Spawn.ReachabilityMinHalfWidth, GameConfig.Spawn.ReachabilityMaxHalfWidth);
        return Math.Min(half, Math.Max(0, clientWidth / 2));
    }

    /// <summary>
    /// Under pressure: placement must stay inside the player reach band.
    /// Otherwise: allow wider placement, but reject opening a far lane opposite an existing far extreme.
    /// </summary>
    internal bool IsReachablePlacement(int x, int w, int clientWidth, float playerCenterX)
    {
        int half = ComputeReachHalfWidthPx(clientWidth);
        float barCenter = x + w * 0.5f;
        float dist = MathF.Abs(barCenter - playerCenterX);

        if (IsUnderSpawnPressure())
            return dist <= half;

        if (dist <= half) return true;

        bool candidateLeft = barCenter < playerCenterX;
        foreach (var bar in _entities.Bars)
        {
            float bc = bar.X + bar.Width * 0.5f;
            if (MathF.Abs(bc - playerCenterX) <= half) continue;
            bool barLeft = bc < playerCenterX;
            if (barLeft != candidateLeft) return false;
        }
        return true;
    }

    private static bool HasHorizontalSpacingConflict(int candidateX, int candidateWidth, List<Bar> bars)
    {
        int minDistance = candidateWidth + GameConfig.Spawn.HorizontalPadding;
        foreach (var bar in bars)
        {
            if (Math.Abs(candidateX - bar.X) < minDistance) return true;
        }
        return false;
    }

    private static bool BarBoundsOverlapAnyExisting(Rectangle candidate, List<Bar> bars)
    {
        foreach (var b in bars)
        {
            if (AxisAlignedRectsOverlap(candidate, b.GetFullBounds())) return true;
        }
        return false;
    }

    private static bool AxisAlignedRectsOverlap(Rectangle a, Rectangle b) =>
        a.Left < b.Left + b.Width
        && a.Left + a.Width > b.Left
        && a.Top < b.Top + b.Height
        && a.Top + a.Height > b.Top;

    private static int ComputeSpawnY(int candidateX, int candidateWidth, int candidateHeight, List<Bar> bars)
    {
        int defaultY = -candidateHeight;
        bool foundOverlap = false;
        int highestY = int.MaxValue;
        int candidateRight = candidateX + candidateWidth;
        foreach (var bar in bars)
        {
            Rectangle b = bar.GetFullBounds();
            int barRight = b.X + b.Width;
            bool xOverlap = candidateX < barRight && candidateRight > b.X;
            if (!xOverlap) continue;
            foundOverlap = true;
            if (b.Y < highestY) highestY = b.Y;
        }
        if (!foundOverlap) return defaultY;
        return Math.Min(defaultY, highestY - candidateHeight);
    }
}
