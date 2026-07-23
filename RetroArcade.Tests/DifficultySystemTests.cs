using Game.Core;
using Game.Systems;

namespace RetroArcade.Tests;

[TestClass]
public sealed class DifficultySystemTests
{
    [TestMethod]
    public void Tick_RampsBarSpeedOverTime()
    {
        var state = new GameState();
        var entities = new EntityManager();
        var difficulty = new DifficultySystem(state, entities);
        state.ResetRun(GameConfig.Difficulty.MinBarSpeed, GameConfig.Spawn.InitialSpawnIntervalFrames);
        state.StartGame();

        int early = state.BarSpeed;
        for (int i = 0; i < 60 * 200; i++)
        {
            state.AdvanceFrame(1f / GameConfig.Ui.TargetFps);
            difficulty.Tick();
        }

        int late = state.BarSpeed;
        Assert.IsTrue(late >= early, "Bar speed should ramp up or stay flat over time.");
        Assert.IsTrue(late <= GameConfig.Difficulty.MaxBarSpeed, "Bar speed should remain clamped.");
    }

    [TestMethod]
    public void SyncDifficultyAfterDestroy_DoesNotForceSnapInOneCall()
    {
        var state = new GameState();
        var entities = new EntityManager();
        var difficulty = new DifficultySystem(state, entities);
        state.ResetRun(GameConfig.Difficulty.MinBarSpeed, GameConfig.Spawn.InitialSpawnIntervalFrames);
        state.StartGame();

        // Jump elapsed time into mid-table where rounded targets differ from start values.
        for (int i = 0; i < 60 * 150; i++)
            state.AdvanceFrame(1f / GameConfig.Ui.TargetFps);

        var sample = difficulty.SampleByTimeCached(state.ElapsedFrames / GameConfig.Difficulty.DifficultyTimeScaleFrames);
        int roundedSpeed = Math.Clamp((int)MathF.Round(sample.BarSpeed), GameConfig.Difficulty.MinBarSpeed, GameConfig.Difficulty.MaxBarSpeed);
        int roundedSpawn = Math.Clamp((int)MathF.Round(sample.SpawnIntervalFrames), GameConfig.Difficulty.MinSpawnIntervalFrames, GameConfig.Difficulty.MaxSpawnIntervalFrames);
        int roundedMaxBars = Math.Clamp((int)MathF.Round(sample.MaxBarsOnScreen), GameConfig.Difficulty.MinBarsOnScreen, GameConfig.Difficulty.MaxBarsOnScreen);

        // Reset live values away from table so a force-snap would jump all the way.
        state.BarSpeed = GameConfig.Difficulty.MinBarSpeed;
        state.SpawnIntervalFrames = GameConfig.Difficulty.MaxSpawnIntervalFrames;
        state.DynamicMaxBarsOnScreen = GameConfig.Difficulty.MinBarsOnScreen;

        difficulty.SyncDifficultyAfterDestroy();

        bool speedSnapped = state.BarSpeed == roundedSpeed && roundedSpeed != GameConfig.Difficulty.MinBarSpeed;
        bool spawnSnapped = state.SpawnIntervalFrames == roundedSpawn && roundedSpawn != GameConfig.Difficulty.MaxSpawnIntervalFrames;
        bool maxBarsSnapped = state.DynamicMaxBarsOnScreen == roundedMaxBars && roundedMaxBars != GameConfig.Difficulty.MinBarsOnScreen;

        Assert.IsFalse(speedSnapped && spawnSnapped && maxBarsSnapped,
            "Destroy sync should blend toward the table, not force all ints in one call.");
    }

    [TestMethod]
    public void DifficultySample_IsMonotonicAcrossEarlyMidWindow()
    {
        var state = new GameState();
        var entities = new EntityManager();
        var difficulty = new DifficultySystem(state, entities);

        float prevSpeed = float.MinValue;
        float prevSpawn = float.MaxValue;
        float prevMaxBars = float.MinValue;
        for (int sec = 30; sec <= 150; sec += 15)
        {
            var sample = difficulty.SampleByTimeCached(sec);
            Assert.IsTrue(sample.BarSpeed + 0.001f >= prevSpeed, $"BarSpeed should not drop at {sec}s.");
            Assert.IsTrue(sample.SpawnIntervalFrames <= prevSpawn + 0.001f, $"Spawn interval should not rise at {sec}s.");
            Assert.IsTrue(sample.MaxBarsOnScreen + 0.001f >= prevMaxBars, $"MaxBars should not drop at {sec}s.");
            prevSpeed = sample.BarSpeed;
            prevSpawn = sample.SpawnIntervalFrames;
            prevMaxBars = sample.MaxBarsOnScreen;
        }

        // Early spawn drops stay gentler than the old ~6 frames / 30s pace.
        var at30 = difficulty.SampleByTimeCached(30f);
        var at60 = difficulty.SampleByTimeCached(60f);
        float spawnDrop = at30.SpawnIntervalFrames - at60.SpawnIntervalFrames;
        Assert.IsTrue(spawnDrop <= 5.5f, $"Early spawn drop should stay modest (was {spawnDrop}).");
    }
}
