using Game.Core;
using Game.Systems;

namespace RetroArcade.Tests;

[TestClass]
public sealed class SpawnSystemTests
{
    [TestMethod]
    public void TrySpawn_DoesNotCreateOverlappingBars()
    {
        var state = new GameState();
        var entities = new EntityManager();
        var spawn = new SpawnSystem(state, entities, new Random(42));
        state.ResetRun(GameConfig.Difficulty.MinBarSpeed, GameConfig.Spawn.InitialSpawnIntervalFrames);
        state.StartGame();

        for (int i = 0; i < 800; i++)
            spawn.TrySpawn(480);

        for (int i = 0; i < entities.Bars.Count; i++)
        {
            var a = entities.Bars[i].GetFullBounds();
            for (int j = i + 1; j < entities.Bars.Count; j++)
            {
                var b = entities.Bars[j].GetFullBounds();
                Assert.IsFalse(a.IntersectsWith(b), "Spawned bars should not overlap.");
            }
        }
    }
}
