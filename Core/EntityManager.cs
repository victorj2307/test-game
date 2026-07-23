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
    public List<ScorePopup> ScorePopups { get; } = new();

    /// <summary>Clears all active world entities.</summary>
    public void Clear()
    {
        Bars.Clear();
        Bullets.Clear();
        Particles.Clear();
        Fragments.Clear();
        PowerUps.Clear();
        Explosions.Clear();
        ScorePopups.Clear();
    }

    /// <summary>Evicts oldest particles so <paramref name="incoming"/> can be added under the global cap.</summary>
    public void EnsureParticleCapacity(int incoming)
    {
        int overflow = Particles.Count + incoming - GameConfig.Effects.MaxActiveParticles;
        if (overflow <= 0) return;
        EvictFromEnd(Particles, overflow);
    }

    /// <summary>Evicts oldest fragments so <paramref name="incoming"/> can be added under the global cap.</summary>
    public void EnsureFragmentCapacity(int incoming)
    {
        int overflow = Fragments.Count + incoming - GameConfig.Effects.MaxActiveFragments;
        if (overflow <= 0) return;
        EvictFromEnd(Fragments, overflow);
    }

    /// <summary>Evicts oldest score popups so <paramref name="incoming"/> can be added under the global cap.</summary>
    public void EnsureScorePopupCapacity(int incoming = 1)
    {
        int overflow = ScorePopups.Count + incoming - GameConfig.Effects.MaxActiveScorePopups;
        if (overflow <= 0) return;
        EvictFromEnd(ScorePopups, overflow);
    }

    /// <summary>
    /// Drops the newest <paramref name="count"/> entries from the end (cheap truncate; avoids mid-list shifts).
    /// FX order does not matter for gameplay fairness under pressure.
    /// </summary>
    private static void EvictFromEnd<T>(List<T> list, int count)
    {
        count = Math.Min(count, list.Count);
        if (count <= 0) return;
        list.RemoveRange(list.Count - count, count);
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

    /// <summary>Advances and cleans up simple spark particles (swap-remove dead entries).</summary>
    public void UpdateParticles(float deltaSeconds)
    {
        for (int i = Particles.Count - 1; i >= 0; i--)
        {
            Particles[i].Update(deltaSeconds);
            if (Particles[i].IsDead) SwapRemoveAt(Particles, i);
        }
    }

    /// <summary>Advances and cleans up rectangular debris fragments (swap-remove dead entries).</summary>
    public void UpdateFragments(float deltaSeconds)
    {
        for (int i = Fragments.Count - 1; i >= 0; i--)
        {
            Fragments[i].Update(deltaSeconds);
            if (Fragments[i].IsDead) SwapRemoveAt(Fragments, i);
        }
    }

    /// <summary>Advances and cleans up floating score popups (swap-remove dead entries).</summary>
    public void UpdateScorePopups(float deltaSeconds)
    {
        for (int i = ScorePopups.Count - 1; i >= 0; i--)
        {
            ScorePopups[i].Update(deltaSeconds);
            if (ScorePopups[i].IsDead) SwapRemoveAt(ScorePopups, i);
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

    /// <summary>O(1) unordered remove used for short-lived FX lists (order does not matter).</summary>
    private static void SwapRemoveAt<T>(List<T> list, int index)
    {
        int last = list.Count - 1;
        if (index < last)
            list[index] = list[last];
        list.RemoveAt(last);
    }
}
