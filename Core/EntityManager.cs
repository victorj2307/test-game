using Game.Entities;

namespace Game.Core;

/// <summary>Enemy shots and debris lists plus per-frame motion and culling.</summary>
public sealed class EntityManager
{
    public List<Bar> Bars { get; } = new();
    public List<Bullet> Bullets { get; } = new();
    public List<Particle> Particles { get; } = new();
    public List<PowerUp> PowerUps { get; } = new();
    public List<ExplosionFx> Explosions { get; } = new();

    public void Clear()
    {
        Bars.Clear();
        Bullets.Clear();
        Particles.Clear();
        PowerUps.Clear();
        Explosions.Clear();
    }

    public void UpdateBullets()
    {
        for (int i = Bullets.Count - 1; i >= 0; i--)
        {
            Bullets[i].Update();
            if (Bullets[i].Y + Bullets[i].Height < 0) Bullets.RemoveAt(i);
        }
    }

    public void UpdatePowerUps(int playHeight)
    {
        for (int i = PowerUps.Count - 1; i >= 0; i--)
        {
            PowerUps[i].Update();
            if (PowerUps[i].Y > playHeight + 20) PowerUps.RemoveAt(i);
        }
    }

    public void UpdateParticles()
    {
        for (int i = Particles.Count - 1; i >= 0; i--)
        {
            Particles[i].Update();
            if (Particles[i].IsDead) Particles.RemoveAt(i);
        }
    }

    public void UpdateExplosions()
    {
        for (int i = Explosions.Count - 1; i >= 0; i--)
        {
            Explosions[i].Tick();
            if (Explosions[i].IsDead) Explosions.RemoveAt(i);
        }
    }

    public void UpdateBars()
    {
        foreach (var b in Bars)
        {
            b.TickSpeedTowardTarget();
            b.Move();
            b.TickEffect();
        }
    }
}
