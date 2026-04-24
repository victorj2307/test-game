using System.Drawing;
using Game.Core;
using Game.Entities;

namespace Game.Systems;

/// <summary>Bar placement, cluster/lane patterns, and max-on-screen cap.</summary>
public sealed class SpawnSystem
{
    private const int MinBarHeight = 60;
    private const int MaxBarHeight = 200;
    public const int BarWidth = 24;
    private const int MaxBarSpawnPlaceAttempts = 10;
    private const int SpawnHorizontalPadding = 8;
    private const int BarSpawnRetryFramesWhenNoFit = 8;
    private const float ClusterChance = 0.15f;
    private const float LaneChance = 0.10f;
    private const double PowerUpDropChance = 0.10;
    private const int ForcePowerUpAfterBars = 12;
    private const double DevPowerUpDropChance = 0.55;
    private const int DevForcePowerUpAfterBars = 3;

    private readonly GameState _state;
    private readonly EntityManager _entities;
    private readonly Random _random;

    public SpawnSystem(GameState state, EntityManager entities, Random random)
    {
        _state = state;
        _entities = entities;
        _random = random;
    }

    public void TrySpawn(int clientWidth)
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

        if (TrySpawnOneBar(clientWidth, w, forcedX, out int placedX, out bool usedLane))
        {
            bool doCluster = _random.NextDouble() < ClusterChance;
            if (doCluster)
            {
                int extra = _random.Next(1, 3);
                for (int c = 0; c < extra; c++)
                {
                    int ox = _random.Next(-24, 25);
                    int nx = Math.Clamp(placedX + ox, 0, clientWidth - w);
                    BarType t = RollBarType();
                    int h = RollHeight(t);
                    TryCreateBarAt(nx, w, h, t, clientWidth, setCountdown: false);
                }
            }
            if (!usedLane && _random.NextDouble() < LaneChance)
                _state.NextSpawnLaneX = Math.Clamp(placedX, 0, clientWidth - w);

            _state.SpawnCountdown = _state.EffectiveSpawnIntervalFrames;
            return;
        }

        for (int attempt = 0; attempt < MaxBarSpawnPlaceAttempts; attempt++)
        {
            int x = clientWidth <= w ? 0 : _random.Next(0, clientWidth - w + 1);
            BarType t = RollBarType();
            int h = RollHeight(t);
            if (TryCreateBarAt(x, w, h, t, clientWidth, setCountdown: true)) return;
        }
        for (int x = 0; x <= clientWidth - w; x++)
        {
            if (TryCreateBarAt(x, w, 40, BarType.Fast, clientWidth, setCountdown: true)) return;
        }
        _state.SpawnCountdown = BarSpawnRetryFramesWhenNoFit;
    }

    private bool TrySpawnOneBar(int clientWidth, int w, int? forcedX, out int placedX, out bool usedLane)
    {
        placedX = 0;
        usedLane = false;
        if (forcedX is int fx)
        {
            BarType t = RollBarType();
            int h = RollHeight(t);
            if (TryCreateBarAt(fx, w, h, t, clientWidth, setCountdown: false))
            {
                placedX = fx;
                usedLane = true;
                return true;
            }
        }
        for (int attempt = 0; attempt < MaxBarSpawnPlaceAttempts; attempt++)
        {
            int x = clientWidth <= w ? 0 : _random.Next(0, clientWidth - w + 1);
            BarType t = RollBarType();
            int h = RollHeight(t);
            if (TryCreateBarAt(x, w, h, t, clientWidth, setCountdown: false))
            {
                placedX = x;
                return true;
            }
        }
        for (int x = 0; x <= clientWidth - w; x++)
        {
            if (TryCreateBarAt(x, w, 40, BarType.Fast, clientWidth, setCountdown: false))
            {
                placedX = x;
                return true;
            }
        }
        return false;
    }

    private BarType RollBarType()
    {
        int r = _random.Next(100);
        if (r < 50) return BarType.Normal;
        if (r < 80) return BarType.Fast;
        return BarType.Tank;
    }

    private int RollHeight(BarType type) => type switch
    {
        BarType.Fast => _random.Next(40, 101),
        BarType.Tank => _random.Next(120, 201),
        _ => _random.Next(MinBarHeight, MaxBarHeight + 1),
    };

    private static float TypeSpeedScale(BarType type) => type switch
    {
        BarType.Fast => 1.4f,
        BarType.Tank => 0.75f,
        _ => 1f,
    };

    private bool TryCreateBarAt(int x, int w, int h, BarType type, int clientWidth, bool setCountdown)
    {
        if (_entities.Bars.Count >= _state.EffectiveMaxBarsOnScreen) return false;

        h = Math.Clamp(h, 20, 300);
        w = Math.Min(w, clientWidth);
        if (w < 1) return false;
        x = Math.Clamp(x, 0, Math.Max(0, clientWidth - w));
        if (HasHorizontalSpacingConflict(x, w, _entities.Bars)) return false;
        var candidate = new Rectangle(x, 0, w, h);
        if (BarBoundsOverlapAnyExisting(candidate, _entities.Bars)) return false;
        float sc = TypeSpeedScale(type);
        _entities.Bars.Add(new Bar(x, 0, w, h, type, h, sc, _state.GetEffectiveBarSpeed()));
        if (setCountdown) _state.SpawnCountdown = _state.EffectiveSpawnIntervalFrames;
        return true;
    }

    public void TrySpawnPowerUpAt(Rectangle sourceBounds)
    {
        _state.RegisterBarDestroyedForPowerUpRoll();
        int forceAfter = _state.IsDevMode ? DevForcePowerUpAfterBars : ForcePowerUpAfterBars;
        double dropChance = _state.IsDevMode ? DevPowerUpDropChance : PowerUpDropChance;
        bool forceDrop = _state.BarsSinceLastPowerUpDrop >= forceAfter;
        if (!forceDrop && _random.NextDouble() > dropChance) return;
        const int size = 12;
        int x = sourceBounds.X + (sourceBounds.Width - size) / 2;
        int y = sourceBounds.Y + Math.Max(0, sourceBounds.Height / 3);
        var type = (PowerUpType)_random.Next(0, Enum.GetValues<PowerUpType>().Length);
        _entities.PowerUps.Add(new PowerUp(x, y, size, 2, type));
        _state.RegisterPowerUpDrop();
    }

    private static bool HasHorizontalSpacingConflict(int candidateX, int candidateWidth, List<Bar> bars)
    {
        int minDistance = candidateWidth + SpawnHorizontalPadding;
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
            if (AxisAlignedRectsOverlap(candidate, b.GetBounds())) return true;
        }
        return false;
    }

    private static bool AxisAlignedRectsOverlap(Rectangle a, Rectangle b) =>
        a.Left < b.Left + b.Width
        && a.Left + a.Width > b.Left
        && a.Top < b.Top + b.Height
        && a.Top + a.Height > b.Top;
}
