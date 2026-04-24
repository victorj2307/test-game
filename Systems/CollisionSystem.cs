using System.Diagnostics;
using System.Drawing;
using Game.Audio;
using Game.Core;
using Game.Entities;

namespace Game.Systems;

/// <summary>Bullet vs bar hits, damage, particles, destroy scoring, and bomb wave queue.</summary>
public sealed class CollisionSystem
{
    /// <summary>Visual / secondary splash only; primary cull uses sorted global fraction.</summary>
    private const int BombRadius = 120;

    private const int BombSplashDamage = 85;
    private const int BombSoleBarDamage = 400;
    private const int BombKillStaggerFrames = 3;

    private readonly GameState _state;
    private readonly EntityManager _entities;
    private readonly Random _random;
    private readonly DifficultySystem _difficulty;
    private readonly SpawnSystem _spawn;

    private readonly List<PendingBombKill> _bombKillQueue = new();

    private sealed class PendingBombKill
    {
        public required Bar Bar { get; init; }
        public int FramesLeft { get; set; }
    }

    public CollisionSystem(GameState state, EntityManager entities, Random random, DifficultySystem difficulty, SpawnSystem spawn)
    {
        _state = state;
        _entities = entities;
        _random = random;
        _difficulty = difficulty;
        _spawn = spawn;
    }

    public void ClearBombKillQueue() => _bombKillQueue.Clear();

    /// <summary>Staggered destroys after bomb detonation (closest scheduled first).</summary>
    public void TickPendingBombKills()
    {
        for (int i = _bombKillQueue.Count - 1; i >= 0; i--)
        {
            PendingBombKill pk = _bombKillQueue[i];
            pk.FramesLeft--;
            if (pk.FramesLeft > 0) continue;
            int idx = _entities.Bars.IndexOf(pk.Bar);
            if (idx >= 0 && pk.Bar.Height > 0) DestroyBarAt(idx);
            _bombKillQueue.RemoveAt(i);
        }
    }

    public void Resolve(int damagePerHit)
    {
        for (int bi = _entities.Bullets.Count - 1; bi >= 0; bi--)
        {
            Bullet bl = _entities.Bullets[bi];
            bool removeBullet = false;
            while (!removeBullet)
            {
                Rectangle bRect = bl.GetBounds();
                int hitIndex = -1;
                for (int j = 0; j < _entities.Bars.Count; j++)
                {
                    Bar bar = _entities.Bars[j];
                    if (bar.Height <= 0) continue;
                    if (!bar.GetBounds().IntersectsWith(bRect)) continue;
                    hitIndex = j;
                    break;
                }

                if (hitIndex < 0) break;

                Bar hitBar = _entities.Bars[hitIndex];
                Rectangle hitBarRect = hitBar.GetBounds();
                hitBar.ApplyDamage(damagePerHit);
                if (bl.IsPiercingVisual)
                {
                    hitBar.RegisterPierceHit();
                    GameAudio.PlayPierceHit();
                }
                else
                {
                    hitBar.RegisterHit();
                    GameAudio.PlayHit();
                }

                SpawnHitParticles(bRect, hitBarRect, bl.IsPiercingVisual);

                if (!bl.ConsumePierce())
                    removeBullet = true;

                if (hitIndex < _entities.Bars.Count && _entities.Bars[hitIndex].IsDestroyed)
                    DestroyBarAt(hitIndex);

                if (removeBullet)
                {
                    _entities.Bullets.RemoveAt(bi);
                    break;
                }
            }
        }
    }

    public void ExecuteBombExplosionAt(float cx, float cy) =>
        ExplodeAt((int)MathF.Round(cx), (int)MathF.Round(cy));

    private void DestroyBarAt(int barIndex)
    {
        if (barIndex < 0 || barIndex >= _entities.Bars.Count) return;
        Rectangle bounds = _entities.Bars[barIndex].GetBounds();
        _entities.Bars.RemoveAt(barIndex);
        _state.RegisterBarDestroyed();
        _spawn.TrySpawnPowerUpAt(bounds);
        _difficulty.SyncBarSpeedFromScore();
    }

    private void ExplodeAt(int cx, int cy)
    {
        int n = _entities.Bars.Count;
#if DEBUG
        Debug.WriteLine($"[Bomb] ExplodeAt ({cx},{cy}) bars={n} queue cleared");
#endif
        _bombKillQueue.Clear();
        _entities.Explosions.Add(new ExplosionFx(cx, cy));
        _state.ShakeUntilTickMs = Environment.TickCount64 + 320;
        _state.TriggerBombImpactFx(flashFrames: 14, heavyShakeFrames: 22);
        GameAudio.PlayHit();
        SpawnExplosionBurst(cx, cy);

        if (n == 0) return;

        if (n == 1)
        {
            var bar = _entities.Bars[0];
            bar.ApplyDamage(Math.Max(bar.InitialHeight + 80, BombSoleBarDamage));
            bar.RegisterHit();
            Rectangle r0 = bar.GetBounds();
            SpawnHitParticles(new Rectangle(cx - 2, cy - 2, 4, 4), r0, pierceVisual: false);
            if (bar.IsDestroyed) DestroyBarAt(0);
            return;
        }

        var ranked = new List<(int Idx, int Bottom, long Dist2)>(n);
        for (int i = 0; i < n; i++)
        {
            Bar bar = _entities.Bars[i];
            if (bar.Height <= 0) continue;
            Rectangle r = bar.GetBounds();
            int bottom = r.Bottom;
            int bx = r.Left + r.Width / 2;
            int by = r.Top + r.Height / 2;
            long dx = bx - cx;
            long dy = by - cy;
            long d2 = dx * dx + dy * dy;
            ranked.Add((i, bottom, d2));
        }

        if (ranked.Count == 0) return;

        int killCount = Math.Max(1, Math.Min(ranked.Count - 1, (int)Math.Floor(ranked.Count * 0.72)));

        ranked.Sort(static (a, b) =>
        {
            int c = b.Bottom.CompareTo(a.Bottom);
            if (c != 0) return c;
            return a.Dist2.CompareTo(b.Dist2);
        });

        var killBars = new List<(Bar Bar, long Dist2)>(killCount);
        for (int k = 0; k < killCount; k++)
        {
            int idx = ranked[k].Idx;
            killBars.Add((_entities.Bars[idx], ranked[k].Dist2));
        }

        killBars.Sort(static (a, b) => a.Dist2.CompareTo(b.Dist2));

        var killSet = new HashSet<Bar>();
        foreach ((Bar bar, _) in killBars)
            killSet.Add(bar);

        long r2 = (long)BombRadius * BombRadius;
        for (int i = _entities.Bars.Count - 1; i >= 0; i--)
        {
            Bar bar = _entities.Bars[i];
            if (killSet.Contains(bar)) continue;
            Rectangle b = bar.GetBounds();
            int bx = b.Left + b.Width / 2;
            int by = b.Top + b.Height / 2;
            long dx = bx - cx;
            long dy = by - cy;
            if (dx * dx + dy * dy > r2) continue;
            bar.ApplyDamage(BombSplashDamage);
            bar.RegisterHit();
            SpawnHitParticles(new Rectangle(cx - 2, cy - 2, 4, 4), b, pierceVisual: false);
            if (bar.IsDestroyed) DestroyBarAt(i);
        }

        for (int i = 0; i < killBars.Count; i++)
        {
            int delay = 2 + i * BombKillStaggerFrames;
            _bombKillQueue.Add(new PendingBombKill { Bar = killBars[i].Bar, FramesLeft = delay });
        }
    }

    private void SpawnExplosionBurst(int cx, int cy)
    {
        for (int i = 0; i < 32; i++)
        {
            double ang = _random.NextDouble() * Math.PI * 2;
            float sp = (float)(2.5 + _random.NextDouble() * 5.5);
            float vx = MathF.Cos((float)ang) * sp;
            float vy = MathF.Sin((float)ang) * sp - 2f;
            Color c = _random.Next(4) switch
            {
                0 => Color.FromArgb(255, 255, 220, 80),
                1 => Color.FromArgb(255, 255, 140, 40),
                2 => Color.FromArgb(255, 255, 90, 30),
                _ => Color.FromArgb(255, 255, 200, 160)
            };
            _entities.Particles.Add(new Particle(cx, cy, vx, vy, _random.Next(18, 34), c));
        }
    }

    private void SpawnHitParticles(Rectangle bullet, Rectangle bar, bool pierceVisual)
    {
        int cx = (Math.Min(bullet.Left, bar.Left) + Math.Max(bullet.Right, bar.Right)) / 2;
        int cy = (Math.Min(bullet.Top, bar.Top) + Math.Max(bullet.Bottom, bar.Bottom)) / 2;
        for (int i = 0; i < 10; i++)
        {
            float vx = (float)(_random.NextDouble() * 4 - 2);
            float vy = (float)(-_random.NextDouble() * 3 - 1);
            Color c = pierceVisual
                ? _random.Next(3) switch
                {
                    0 => Color.FromArgb(255, 255, 120, 255),
                    1 => Color.FromArgb(255, 200, 80, 255),
                    _ => Color.FromArgb(255, 180, 220, 255)
                }
                : _random.Next(3) == 0 ? Color.Orange : Color.Gold;
            _entities.Particles.Add(new Particle(cx, cy, vx, vy, _random.Next(12, 24), c));
        }
    }
}
