using Game.Core;

namespace Game.Systems;

/// <summary>Keyframed difficulty table with linear interpolation over elapsed time.</summary>
public sealed class DifficultySystem
{
    private readonly GameState _state;
    private readonly EntityManager _entities;

    public DifficultySystem(GameState state, EntityManager entities)
    {
        _state = state;
        _entities = entities;
    }

    /// <summary>
    /// Re-samples table-driven difficulty immediately and applies target values without smoothing delay.
    /// </summary>
    public void SyncBarSpeedFromScore()
    {
        ApplyTable(force: true);
    }

    /// <summary>
    /// Runs one difficulty update tick and applies interpolated targets with smoothing.
    /// </summary>
    public void Tick()
    {
        if (!_state.IsPlaying || _state.IsGameOver) return;
        ApplyTable(force: false);
    }

    private void ApplyTable(bool force)
    {
        float seconds = _state.ElapsedFrames / GameConfig.Difficulty.DifficultyTimeScaleFrames;
        GameConfig.Difficulty.DifficultyEntry sample = SampleByTime(seconds);
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

        foreach (var b in _entities.Bars) b.SetTargetMoveSpeed(_state.GetEffectiveBarSpeed());
        _state.KillsInWindow = 0;
    }

    private static GameConfig.Difficulty.DifficultyEntry SampleByTime(float seconds)
    {
        var table = GameConfig.Difficulty.Table;
        if (table.Length == 0)
            return new GameConfig.Difficulty.DifficultyEntry(
                0f,
                GameConfig.Difficulty.MinBarSpeed,
                GameConfig.Difficulty.MaxSpawnIntervalFrames,
                GameConfig.Difficulty.MinBarsOnScreen);

        if (seconds <= table[0].Seconds) return table[0];

        for (int i = 1; i < table.Length; i++)
        {
            GameConfig.Difficulty.DifficultyEntry b = table[i];
            if (seconds > b.Seconds) continue;
            GameConfig.Difficulty.DifficultyEntry a = table[i - 1];
            float span = Math.Max(GameConfig.Difficulty.InterpolationSpanEpsilon, b.Seconds - a.Seconds);
            float t = Math.Clamp((seconds - a.Seconds) / span, 0f, 1f);
            return new GameConfig.Difficulty.DifficultyEntry(
                seconds,
                Lerp(a.BarSpeed, b.BarSpeed, t),
                Lerp(a.SpawnIntervalFrames, b.SpawnIntervalFrames, t),
                Lerp(a.MaxBarsOnScreen, b.MaxBarsOnScreen, t));
        }

        return table[^1];
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
