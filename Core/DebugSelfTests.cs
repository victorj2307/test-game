using System.Diagnostics;
using Game.Systems;

namespace Game.Core;

/// <summary>
/// Lightweight debug-only checks for core gameplay invariants.
/// </summary>
internal static class DebugSelfTests
{
    public static void Run()
    {
        VerifyScoreProgression();
        VerifyDifficultyInterpolationShape();
        VerifySpawnRespectsCap();
    }

    private static void VerifyScoreProgression()
    {
        var state = new GameState();
        state.ResetRun(GameConfig.Difficulty.MinBarSpeed, GameConfig.Spawn.InitialSpawnIntervalFrames);
        state.RegisterBarDestroyed();
        int firstScore = state.Score;
        state.AdvanceFrame(1f / GameConfig.Ui.TargetFps);
        state.RegisterBarDestroyed();
        Debug.Assert(state.Score > firstScore, "Score should increase across destroys.");
        Debug.Assert(state.ComboMultiplier >= 1, "Combo multiplier should remain valid.");
    }

    private static void VerifyDifficultyInterpolationShape()
    {
        var state = new GameState();
        var entities = new EntityManager();
        var system = new DifficultySystem(state, entities);
        var early = system.SampleByTimeCached(10f);
        var mid = system.SampleByTimeCached(120f);
        var late = system.SampleByTimeCached(260f);
        Debug.Assert(early.BarSpeed <= mid.BarSpeed, "Difficulty bar speed should generally ramp up.");
        Debug.Assert(mid.BarSpeed <= late.BarSpeed, "Difficulty bar speed should generally ramp up.");
    }

    private static void VerifySpawnRespectsCap()
    {
        var state = new GameState();
        state.ResetRun(GameConfig.Difficulty.MinBarSpeed, GameConfig.Spawn.InitialSpawnIntervalFrames);
        state.SetPlaying(true);
        var entities = new EntityManager();
        var rng = new Random(42);
        var spawner = new SpawnSystem(state, entities, rng);
        for (int i = 0; i < 500; i++)
            spawner.TrySpawn(480);

        Debug.Assert(entities.Bars.Count <= state.EffectiveMaxBarsOnScreen, "Spawner exceeded configured bar cap.");
    }
}
