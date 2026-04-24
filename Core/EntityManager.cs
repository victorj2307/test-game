using Game.Entities;

namespace Game.Core;

/// <summary>Enemy shots and debris lists plus per-frame motion and culling.</summary>
public sealed class EntityManager
{
    public List<Bar> Bars { get; } = new();
    public List<Bullet> Bullets { get; } = new();
    public List<Particle> Particles { get; } = new();
    public List<Fragment> Fragments { get; } = new();
    public List<PowerUp> PowerUps { get; } = new();
    public List<ExplosionFx> Explosions { get; } = new();

    /// <summary>Clears all active world entities.</summary>
    public void Clear()
    {
        Bars.Clear();
        Bullets.Clear();
        Particles.Clear();
        Fragments.Clear();
        PowerUps.Clear();
        Explosions.Clear();
    }

    /// <summary>Updates bullets and removes ones that leave the top of the playfield.</summary>
    public void UpdateBullets(float deltaSeconds)
    {
        for (int i = Bullets.Count - 1; i >= 0; i--)
        {
            Bullets[i].Update(deltaSeconds);
            if (Bullets[i].Y + Bullets[i].Height < 0) Bullets.RemoveAt(i);
        }
    }

    /// <summary>Updates falling power-ups and culls off-screen entries.</summary>
    public void UpdatePowerUps(int playHeight, float deltaSeconds)
    {
        for (int i = PowerUps.Count - 1; i >= 0; i--)
        {
            PowerUps[i].Update(deltaSeconds);
            if (PowerUps[i].Y > playHeight + GameConfig.PowerUps.OffscreenCullPadding) PowerUps.RemoveAt(i);
        }
    }

    /// <summary>Advances and cleans up simple spark particles.</summary>
    public void UpdateParticles(float deltaSeconds)
    {
        for (int i = Particles.Count - 1; i >= 0; i--)
        {
            Particles[i].Update(deltaSeconds);
            if (Particles[i].IsDead) Particles.RemoveAt(i);
        }
    }

    /// <summary>Advances and cleans up rectangular debris fragments.</summary>
    public void UpdateFragments(float deltaSeconds)
    {
        for (int i = Fragments.Count - 1; i >= 0; i--)
        {
            Fragments[i].Update(deltaSeconds);
            if (Fragments[i].IsDead) Fragments.RemoveAt(i);
        }
    }

    /// <summary>Ticks active explosion ring effects and removes finished ones.</summary>
    public void UpdateExplosions(float deltaSeconds)
    {
        for (int i = Explosions.Count - 1; i >= 0; i--)
        {
            Explosions[i].Tick(deltaSeconds);
            if (Explosions[i].IsDead) Explosions.RemoveAt(i);
        }
    }

    /// <summary>Advances all bars (speed easing, movement, and hit effects).</summary>
    public void UpdateBars(float deltaSeconds, float barSpeedLerpFactor = -1f)
    {
        foreach (var b in Bars)
        {
            b.TickSpeedTowardTarget(deltaSeconds, barSpeedLerpFactor);
            b.Move(deltaSeconds);
            b.TickEffect(deltaSeconds);
        }
    }
}
