namespace RetroArcade;

/// <summary>Spawn interval trends and continuous global bar speed from score (sqrt curve, clamped).</summary>
public sealed class DifficultySystem
{
    private const int MinBarSpeed = 1;
    /// <summary>Cap keeps late game manageable; sqrt(score) curve means most progression happens in early–mid score.</summary>
    private const int MaxBarSpeed = 4;
    /// <summary>Starting level before sqrt term (slightly &gt; 1 rounds to same early speeds, anchors the curve).</summary>
    private const double BaseBarSpeed = 1.05;
    /// <summary>Small factor so speed rises slowly; avoids overwhelming mid-game.</summary>
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

    /// <summary>Maps score to global bar speed: BaseBarSpeed + sqrt(score) * factor, clamped (no step jumps).</summary>
    public static int ComputeBarSpeedFromScore(int score)
    {
        double v = BaseBarSpeed + Math.Sqrt(Math.Max(0, score)) * SpeedSqrtFactor;
        return Math.Clamp((int)Math.Round(v), MinBarSpeed, MaxBarSpeed);
    }

    /// <summary>Call after score changes (e.g. bar destroyed) so fall speed tracks difficulty smoothly.</summary>
    public void SyncBarSpeedFromScore()
    {
        int next = ComputeBarSpeedFromScore(_state.Score);
        _state.BarSpeed = next;
        // Ease existing bars toward the new global level (SetTargetMoveSpeed); avoids instant speed spikes.
        foreach (var b in _entities.Bars) b.SetTargetMoveSpeed(_state.BarSpeed);
    }

    public void Tick()
    {
        if (!_state.IsPlaying || _state.IsGameOver) return;
        if (_state.ElapsedFrames % 120 != 0) return;
        if (_state.SpawnIntervalFrames > MinSpawnIntervalFrames)
            _state.SpawnIntervalFrames = Math.Max(MinSpawnIntervalFrames, _state.SpawnIntervalFrames - 4);
        if (_state.KillsInWindow >= KillsPerDifficultyWindow)
        {
            if (_state.SpawnIntervalFrames > MinSpawnIntervalFrames)
                _state.SpawnIntervalFrames = Math.Max(MinSpawnIntervalFrames, _state.SpawnIntervalFrames - 3);
            // Bar speed comes only from SyncBarSpeedFromScore (score curve), not extra steps here.
        }
        else if (_state.KillsInWindow == 0)
        {
            if (_state.SpawnIntervalFrames < InitialSpawnIntervalFrames)
                _state.SpawnIntervalFrames = Math.Min(InitialSpawnIntervalFrames, _state.SpawnIntervalFrames + 2);
        }
        _state.KillsInWindow = 0;
    }
}
