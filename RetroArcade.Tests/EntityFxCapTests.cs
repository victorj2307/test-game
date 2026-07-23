using System.Drawing;
using Game.Core;
using Game.Entities;

namespace RetroArcade.Tests;

[TestClass]
public sealed class EntityFxCapTests
{
    [TestMethod]
    public void EnsureParticleCapacity_EvictsFromEndUnderCap()
    {
        var entities = new EntityManager();
        for (int i = 0; i < GameConfig.Effects.MaxActiveParticles; i++)
            entities.Particles.Add(new Particle(i, 0, 0, 0, 10, Color.White));

        entities.EnsureParticleCapacity(5);
        Assert.AreEqual(GameConfig.Effects.MaxActiveParticles - 5, entities.Particles.Count);
        // End-eviction keeps the earliest entries.
        Assert.AreEqual(0f, entities.Particles[0].X);

        for (int i = 0; i < 5; i++)
            entities.Particles.Add(new Particle(100 + i, 0, 0, 0, 10, Color.Orange));

        Assert.AreEqual(GameConfig.Effects.MaxActiveParticles, entities.Particles.Count);
    }

    [TestMethod]
    public void EnsureScorePopupCapacity_RespectsCap()
    {
        var entities = new EntityManager();
        for (int i = 0; i < GameConfig.Effects.MaxActiveScorePopups; i++)
            entities.ScorePopups.Add(new ScorePopup(0, 0, 1, 10));

        entities.EnsureScorePopupCapacity();
        Assert.AreEqual(GameConfig.Effects.MaxActiveScorePopups - 1, entities.ScorePopups.Count);

        entities.ScorePopups.Add(new ScorePopup(10, 10, 4, 10));
        Assert.AreEqual(GameConfig.Effects.MaxActiveScorePopups, entities.ScorePopups.Count);
    }
}
