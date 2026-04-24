using Game.Core;
using Game.Entities;
using Game.Systems;

namespace RetroArcade.Tests;

[TestClass]
public sealed class CollisionSystemTests
{
    [TestMethod]
    public void Resolve_PiercingBullet_DoesNotRepeatedlyHitSameBarInOneFrame()
    {
        var state = new GameState();
        var entities = new EntityManager();
        var difficulty = new DifficultySystem(state, entities);
        var spawn = new SpawnSystem(state, entities, new Random(42));
        var collisions = new CollisionSystem(state, entities, new Random(84), difficulty, spawn);
        state.ResetRun(GameConfig.Difficulty.MinBarSpeed, GameConfig.Spawn.InitialSpawnIntervalFrames);
        state.StartGame();

        var bar = new Bar(
            x: 100,
            y: 100,
            width: GameConfig.Bars.Width,
            height: 100,
            type: BarType.Normal,
            isSpecial: false,
            initialHeight: 100,
            speedScale: GameConfig.Bars.NormalSpeedScale,
            globalBarSpeed: state.BarSpeed);
        entities.Bars.Add(bar);
        entities.Bullets.Add(new Bullet(x: 104, y: 120, width: 4, height: 10, speed: 10, pierceCount: 2));

        collisions.Resolve(GameConfig.Scoring.DamagePerHit);

        Assert.AreEqual(80, bar.Height, "Piercing bullets should only damage a single bar once per frame.");
    }
}
