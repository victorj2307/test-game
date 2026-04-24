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
}
