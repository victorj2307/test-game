using Game.Core;
using Game.Systems;

namespace RetroArcade.Tests;

[TestClass]
public sealed class CoreInvariantTests
{
    [TestMethod]
    public void RegisterBarDestroyed_IncreasesScoreAndKeepsValidCombo()
    {
        var state = new GameState();
        state.ResetRun(GameConfig.Difficulty.MinBarSpeed, GameConfig.Spawn.InitialSpawnIntervalFrames);

        state.RegisterBarDestroyed();
        int firstScore = state.Score;
        state.AdvanceFrame(1f / GameConfig.Ui.TargetFps);
        state.RegisterBarDestroyed();

        Assert.IsTrue(state.Score > firstScore, "Score should increase across destroys.");
        Assert.IsTrue(state.ComboMultiplier >= 1, "Combo multiplier should stay within valid range.");
    }

    [TestMethod]
    public void DifficultySample_RampsBarSpeedAcrossTimeline()
    {
        var state = new GameState();
        var entities = new EntityManager();
        var system = new DifficultySystem(state, entities);

        var early = system.SampleByTimeCached(10f);
        var mid = system.SampleByTimeCached(120f);
        var late = system.SampleByTimeCached(260f);

        Assert.IsTrue(early.BarSpeed <= mid.BarSpeed, "Midgame speed should not be below early speed.");
        Assert.IsTrue(mid.BarSpeed <= late.BarSpeed, "Late speed should not be below mid speed.");
    }

    [TestMethod]
    public void SpawnSystem_RespectsEffectiveBarCap()
    {
        var state = new GameState();
        state.ResetRun(GameConfig.Difficulty.MinBarSpeed, GameConfig.Spawn.InitialSpawnIntervalFrames);
        state.StartGame();
        var entities = new EntityManager();
        var spawner = new SpawnSystem(state, entities, new Random(42));

        for (int i = 0; i < 500; i++)
            spawner.TrySpawn(480);

        Assert.IsTrue(
            entities.Bars.Count <= state.EffectiveMaxBarsOnScreen,
            "Spawner should not exceed effective bar cap.");
    }

    [TestMethod]
    public void ScoreQualifiesFromEntries_UsesInMemoryRules()
    {
        var entries = new List<HighScoreStore.LeaderboardEntry>
        {
            new() { Name = "A", Score = 100 },
            new() { Name = "B", Score = 80 }
        };

        Assert.IsTrue(GameManager.ScoreQualifiesFromEntries(120, entries, 2));
        Assert.IsFalse(GameManager.ScoreQualifiesFromEntries(80, entries, 2));
        Assert.IsTrue(GameManager.ScoreQualifiesFromEntries(1, entries, 3));
    }
}
