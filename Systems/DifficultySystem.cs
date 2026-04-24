using Game.Core;

namespace Game.Systems;

/// <summary>Spawn interval trends and continuous global bar speed from score (sqrt curve, clamped).</summary>
public sealed class DifficultySystem
{
    private const int MinBarSpeed = 1;
    private const int MaxBarSpeed = 4;
    private const double BaseBarSpeed = 1.05;
    private const double SpeedSqrtFactor = 0.12;

    private const int InitialSpawnIntervalFrames = 90;
    private const int MinSpawnIntervalFrames = 24;
    private const int KillsPerDifficultyWindow = 3;

    private readonly GameState _state;
    private readonly EntityManager _entities;

    public DifficultySystem(GameState state, EntityManager entities)
    {
        _state = state;
        _entities = entities;
    }

    public static int ComputeBarSpeedFromScore(int score)
    {
        double v = BaseBarSpeed + Math.Sqrt(Math.Max(0, score)) * SpeedSqrtFactor;
        return Math.Clamp((int)Math.Round(v), MinBarSpeed, MaxBarSpeed);
    }

    public void SyncBarSpeedFromScore()
    {
        int next = ComputeBarSpeedFromScore(_state.Score);
        if (_state.IsDevMode)
            next = Math.Min(next, 2);
        _state.BarSpeed = next;
        foreach (var b in _entities.Bars) b.SetTargetMoveSpeed(_state.GetEffectiveBarSpeed());
    }

    public void Tick()
    {
        if (!_state.IsPlaying || _state.IsGameOver) return;
        if (_state.ElapsedFrames % 120 != 0) return;
        if (!_state.IsDevMode)
        {
            if (_state.SpawnIntervalFrames > MinSpawnIntervalFrames)
                _state.SpawnIntervalFrames = Math.Max(MinSpawnIntervalFrames, _state.SpawnIntervalFrames - 4);
            if (_state.KillsInWindow >= KillsPerDifficultyWindow)
            {
                if (_state.SpawnIntervalFrames > MinSpawnIntervalFrames)
                    _state.SpawnIntervalFrames = Math.Max(MinSpawnIntervalFrames, _state.SpawnIntervalFrames - 3);
            }
            else if (_state.KillsInWindow == 0)
            {
                if (_state.SpawnIntervalFrames < InitialSpawnIntervalFrames)
                    _state.SpawnIntervalFrames = Math.Min(InitialSpawnIntervalFrames, _state.SpawnIntervalFrames + 2);
            }
        }
        _state.KillsInWindow = 0;
    }
}
