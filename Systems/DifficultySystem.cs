using Game.Core;

namespace Game.Systems;

/// <summary>Keyframed difficulty table with linear interpolation over elapsed time.</summary>
public sealed class DifficultySystem
{
    private readonly GameState _state;
    private readonly EntityManager _entities;
    private int _cachedSegmentStartIdx;
    private int _lastAppliedEffectiveBarSpeed = int.MinValue;
    private float _lastSlowMotionBarSpeedScale = -1f;

    public DifficultySystem(GameState state, EntityManager entities)
    {
        _state = state;
        _entities = entities;
    }

    /// <summary>
    /// Re-samples the time-based difficulty table after a destroy.
    /// Uses smoothed blends (does not force-snap) so mid-run destroys do not spike difficulty.
    /// </summary>
    public void SyncDifficultyAfterDestroy() => ApplyTable(force: false);

    /// <summary>
    /// Immediately applies table targets (reset / dev-mode toggle). Prefer <see cref="SyncDifficultyAfterDestroy"/> during play.
    /// </summary>
    public void ApplyDifficultyImmediate() => ApplyTable(force: true);

    /// <summary>
    /// Runs one difficulty update tick and applies interpolated targets with smoothing.
    /// </summary>
    public void Tick()
    {
        if (!_state.IsPlaying || _state.IsGameOver || _state.IsFinalDeathAnimating) return;
        ApplyTable(force: false);
    }

    private void ApplyTable(bool force)
    {
        float seconds = _state.ElapsedFrames / GameRuntimeConfig.Current.DifficultyTimeScaleFrames;
        GameConfig.Difficulty.DifficultyEntry sample = SampleByTimeCached(seconds);
        float lastTime = GameConfig.Difficulty.Table[^1].Seconds;
        _state.Difficulty01 = lastTime > 0f ? Math.Clamp(seconds / lastTime, 0f, 1f) : 0f;

        int targetBarSpeed = Math.Clamp((int)MathF.Round(sample.BarSpeed), GameConfig.Difficulty.MinBarSpeed, GameConfig.Difficulty.MaxBarSpeed);
        int targetSpawnInterval = Math.Clamp((int)MathF.Round(sample.SpawnIntervalFrames), GameConfig.Difficulty.MinSpawnIntervalFrames, GameConfig.Difficulty.MaxSpawnIntervalFrames);
        int targetMaxBars = Math.Clamp((int)MathF.Round(sample.MaxBarsOnScreen), GameConfig.Difficulty.MinBarsOnScreen, GameConfig.Difficulty.MaxBarsOnScreen);

        if (_state.IsDevMode)
        {
            targetBarSpeed = Math.Min(targetBarSpeed, GameConfig.Difficulty.DevMaxBarSpeed);
            targetMaxBars = Math.Min(targetMaxBars, GameConfig.Difficulty.DevMaxBarsOnScreen);
        }

        // Keep transitions smooth by blending toward the interpolated targets each frame.
        _state.BarSpeed = SmoothInt(_state.BarSpeed, targetBarSpeed, force ? 1f : GameConfig.Difficulty.BarSpeedBlend);
        _state.SpawnIntervalFrames = SmoothInt(_state.SpawnIntervalFrames, targetSpawnInterval, force ? 1f : GameConfig.Difficulty.SpawnIntervalBlend);
        _state.DynamicMaxBarsOnScreen = SmoothInt(_state.DynamicMaxBarsOnScreen, targetMaxBars, force ? 1f : GameConfig.Difficulty.MaxBarsBlend);

        int effectiveSpeed = _state.GetEffectiveBarSpeed();
        float sm = _state.SlowMotionBarSpeedScale;
        if (force || effectiveSpeed != _lastAppliedEffectiveBarSpeed || MathF.Abs(sm - _lastSlowMotionBarSpeedScale) > 0.001f)
        {
            _lastAppliedEffectiveBarSpeed = effectiveSpeed;
            _lastSlowMotionBarSpeedScale = sm;
            foreach (var b in _entities.Bars) b.SetTargetMoveSpeed(effectiveSpeed, sm);
        }
    }

    internal GameConfig.Difficulty.DifficultyEntry SampleByTimeCached(float seconds)
    {
        var table = GameConfig.Difficulty.Table;
        if (table.Length == 0)
            return new GameConfig.Difficulty.DifficultyEntry(
                0f,
                GameConfig.Difficulty.MinBarSpeed,
                GameConfig.Difficulty.MaxSpawnIntervalFrames,
                GameConfig.Difficulty.MinBarsOnScreen);

        if (seconds <= table[0].Seconds)
        {
            _cachedSegmentStartIdx = 0;
            return table[0];
        }

        if (table.Length == 1)
        {
            _cachedSegmentStartIdx = 0;
            return table[0];
        }

        _cachedSegmentStartIdx = Math.Clamp(_cachedSegmentStartIdx, 0, table.Length - 2);
        while (_cachedSegmentStartIdx < table.Length - 2 && seconds > table[_cachedSegmentStartIdx + 1].Seconds)
            _cachedSegmentStartIdx++;
        while (_cachedSegmentStartIdx > 0 && seconds < table[_cachedSegmentStartIdx].Seconds)
            _cachedSegmentStartIdx--;

        GameConfig.Difficulty.DifficultyEntry a = table[_cachedSegmentStartIdx];
        GameConfig.Difficulty.DifficultyEntry b = table[_cachedSegmentStartIdx + 1];
        if (seconds >= table[^1].Seconds)
            return table[^1];

        float span = Math.Max(GameConfig.Difficulty.InterpolationSpanEpsilon, b.Seconds - a.Seconds);
        float t = Math.Clamp((seconds - a.Seconds) / span, 0f, 1f);
        return new GameConfig.Difficulty.DifficultyEntry(
            seconds,
            Lerp(a.BarSpeed, b.BarSpeed, t),
            Lerp(a.SpawnIntervalFrames, b.SpawnIntervalFrames, t),
            Lerp(a.MaxBarsOnScreen, b.MaxBarsOnScreen, t));
    }

    private static float Lerp(float from, float to, float t) => from + (to - from) * Math.Clamp(t, 0f, 1f);

    private static int SmoothInt(int current, int target, float alpha)
    {
        alpha = Math.Clamp(alpha, 0f, 1f);
        if (current == target || alpha <= 0f) return current;
        if (alpha >= 1f) return target;
        float blended = current + (target - current) * alpha;
        int next = (int)MathF.Round(blended);
        if (next == current)
            next += target > current ? 1 : -1;
        return next;
    }
}
