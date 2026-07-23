using Game.Core;
using Game.Entities;
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
            spawn.TrySpawn(480, 240f);

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

    [TestMethod]
    public void IsReachablePlacement_UnderPressure_RequiresPlayerBand()
    {
        var state = new GameState();
        var entities = new EntityManager();
        var spawn = new SpawnSystem(state, entities, new Random(1));
        state.ResetRun(GameConfig.Difficulty.MinBarSpeed, GameConfig.Spawn.InitialSpawnIntervalFrames);
        state.StartGame();
        state.DynamicMaxBarsOnScreen = 4;

        // Fill to pressure threshold (cap - 1).
        for (int i = 0; i < 3; i++)
        {
            entities.Bars.Add(new Bar(
                200 + i * 40,
                -60,
                GameConfig.Bars.Width,
                60,
                BarType.Normal,
                false,
                60,
                1f,
                state.GetEffectiveBarSpeed(),
                1f));
        }

        const int clientWidth = 480;
        float playerCenterX = 240f;
        int half = spawn.ComputeReachHalfWidthPx(clientWidth);
        int w = GameConfig.Bars.Width;

        int insideX = Math.Clamp((int)MathF.Round(playerCenterX - w * 0.5f), 0, clientWidth - w);
        int farX = 0;
        Assert.IsTrue(spawn.IsReachablePlacement(insideX, w, clientWidth, playerCenterX));
        Assert.IsFalse(spawn.IsReachablePlacement(farX, w, clientWidth, playerCenterX));
        Assert.IsTrue(half >= GameConfig.Spawn.ReachabilityMinHalfWidth);
    }

    [TestMethod]
    public void IsReachablePlacement_RejectsOppositeFarExtreme()
    {
        var state = new GameState();
        var entities = new EntityManager();
        var spawn = new SpawnSystem(state, entities, new Random(1));
        state.ResetRun(GameConfig.Difficulty.MinBarSpeed, GameConfig.Spawn.InitialSpawnIntervalFrames);
        state.StartGame();
        state.DynamicMaxBarsOnScreen = 9;

        const int clientWidth = 480;
        float playerCenterX = 240f;
        int w = GameConfig.Bars.Width;
        int half = spawn.ComputeReachHalfWidthPx(clientWidth);

        // Existing far-left extreme outside the reach band.
        entities.Bars.Add(new Bar(
            0,
            -60,
            w,
            60,
            BarType.Normal,
            false,
            60,
            1f,
            state.GetEffectiveBarSpeed(),
            1f));

        int farRightX = clientWidth - w;
        float farRightCenter = farRightX + w * 0.5f;
        Assert.IsTrue(MathF.Abs(farRightCenter - playerCenterX) > half, "Test setup: far right should be outside band.");
        Assert.IsFalse(spawn.IsReachablePlacement(farRightX, w, clientWidth, playerCenterX));
    }
}
