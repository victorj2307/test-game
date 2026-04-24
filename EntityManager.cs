namespace RetroArcade;

/// <summary>Enemy shots and debris lists plus per-frame motion and culling.</summary>
public sealed class EntityManager
{
    public List<Bar> Bars { get; } = new();
    public List<Bullet> Bullets { get; } = new();
    public List<Particle> Particles { get; } = new();

    public void Clear()
    {
        Bars.Clear();
        Bullets.Clear();
        Particles.Clear();
    }

    public void UpdateBullets()
    {
        for (int i = Bullets.Count - 1; i >= 0; i--)
        {
            Bullets[i].Update();
            if (Bullets[i].Y + Bullets[i].Height < 0) Bullets.RemoveAt(i);
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
