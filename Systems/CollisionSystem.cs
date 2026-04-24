using System.Diagnostics;
using System.Drawing;
using Game.Audio;
using Game.Core;
using Game.Entities;

namespace Game.Systems;

/// <summary>Bullet vs bar hits, damage, particles, destroy scoring, and bomb wave queue.</summary>
public sealed class CollisionSystem
{
    private readonly GameState _state;
    private readonly EntityManager _entities;
    private readonly Random _random;
    private readonly DifficultySystem _difficulty;
    private readonly SpawnSystem _spawn;

    private readonly List<PendingBombKill> _bombKillQueue = new();

    private enum DestroyStyle
    {
        Default,
        Bomb
    }

    private sealed class PendingBombKill
    {
        public required Bar Bar { get; init; }
        public int FramesLeft { get; set; }
        public int ExplosionX { get; init; }
        public int ExplosionY { get; init; }
    }

    public CollisionSystem(GameState state, EntityManager entities, Random random, DifficultySystem difficulty, SpawnSystem spawn)
    {
        _state = state;
        _entities = entities;
        _random = random;
        _difficulty = difficulty;
        _spawn = spawn;
    }

    /// <summary>Clears pending delayed bomb-kill queue entries.</summary>
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
            if (idx >= 0 && pk.Bar.Height > 0)
                DestroyBarAt(idx, DestroyStyle.Bomb, pk.ExplosionX, pk.ExplosionY);
            _bombKillQueue.RemoveAt(i);
        }
    }

    /// <summary>
    /// Resolves bullet-bar collisions for the current frame, including damage, FX, and destruction.
    /// </summary>
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
                SpawnBulletHitFragments(bRect, hitBarRect, bl.IsPiercingVisual);

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

    /// <summary>Triggers bomb detonation flow at the given world position.</summary>
    public void ExecuteBombExplosionAt(float cx, float cy) =>
        ExplodeAt((int)MathF.Round(cx), (int)MathF.Round(cy));

    private void DestroyBarAt(int barIndex, DestroyStyle style = DestroyStyle.Default, int explosionX = 0, int explosionY = 0)
    {
        if (barIndex < 0 || barIndex >= _entities.Bars.Count) return;
        Bar bar = _entities.Bars[barIndex];
        Rectangle bounds = bar.GetBounds();
        bool isSpecial = bar.IsSpecial;
        if (style == DestroyStyle.Bomb)
            SpawnBombFragments(bounds, bar.HealthRatio, explosionX, explosionY);
        if (isSpecial)
            SpawnSpecialDestroyFeedback(bounds);
        _entities.Bars.RemoveAt(barIndex);
        _state.RegisterBarDestroyed(isSpecial);
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
        _state.ShakeUntilTickMs = Environment.TickCount64 + GameConfig.Effects.BombExplosionShakeMs;
        _state.TriggerBombImpactFx(flashFrames: GameConfig.Effects.BombScreenFlashFrames, heavyShakeFrames: GameConfig.Effects.BombHeavyShakeFrames);
        GameAudio.PlayHit();
        SpawnExplosionBurst(cx, cy);

        if (n == 0) return;

        if (n == 1)
        {
            var bar = _entities.Bars[0];
            bar.ApplyDamage(Math.Max(bar.InitialHeight + GameConfig.Effects.BombSingleTargetBonusDamage, GameConfig.Effects.BombSoleBarDamage));
            bar.RegisterHit();
            Rectangle r0 = bar.GetBounds();
            SpawnHitParticles(new Rectangle(cx - 2, cy - 2, 4, 4), r0, pierceVisual: false);
            if (bar.IsDestroyed) DestroyBarAt(0, DestroyStyle.Bomb, cx, cy);
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

        int killCount = Math.Max(1, Math.Min(ranked.Count - 1, (int)Math.Floor(ranked.Count * GameConfig.Effects.BombKillFraction)));

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

        long r2 = (long)GameConfig.Effects.BombRadius * GameConfig.Effects.BombRadius;
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
            bar.ApplyDamage(GameConfig.Effects.BombSplashDamage);
            bar.RegisterHit();
            SpawnHitParticles(new Rectangle(cx - 2, cy - 2, 4, 4), b, pierceVisual: false);
            if (bar.IsDestroyed) DestroyBarAt(i, DestroyStyle.Bomb, cx, cy);
        }

        for (int i = 0; i < killBars.Count; i++)
        {
            int delay = GameConfig.Effects.BombKillDelayStartFrames + i * GameConfig.Effects.BombKillStaggerFrames;
            _bombKillQueue.Add(new PendingBombKill
            {
                Bar = killBars[i].Bar,
                FramesLeft = delay,
                ExplosionX = cx,
                ExplosionY = cy
            });
        }
    }

    private void SpawnBombFragments(Rectangle barBounds, float healthRatio, int explosionX, int explosionY)
    {
        int count = _random.Next(4, 9);
        EnsureFragmentCapacity(count);
        Color baseColor = GetBarFragmentColor(healthRatio);
        for (int i = 0; i < count; i++)
        {
            float px = barBounds.Left + (float)_random.NextDouble() * barBounds.Width;
            float py = barBounds.Top + (float)_random.NextDouble() * Math.Max(1, barBounds.Height);
            float dx = px - explosionX;
            float dy = py - explosionY;
            float len = MathF.Sqrt(dx * dx + dy * dy);
            if (len < 0.001f)
            {
                dx = (float)(_random.NextDouble() * 2 - 1);
                dy = (float)(_random.NextDouble() * 2 - 1);
                len = MathF.Sqrt(dx * dx + dy * dy);
            }
            float nx = dx / len;
            float ny = dy / len;
            float speed = 2.6f + (float)_random.NextDouble() * 3.4f;
            float vx = nx * speed + (float)(_random.NextDouble() * 1.6 - 0.8);
            float vy = ny * speed + (float)(_random.NextDouble() * 1.4 - 0.5);
            int r = Math.Clamp(baseColor.R + _random.Next(-16, 17), 0, 255);
            int g = Math.Clamp(baseColor.G + _random.Next(-16, 17), 0, 255);
            int b = Math.Clamp(baseColor.B + _random.Next(-16, 17), 0, 255);
            _entities.Fragments.Add(new Fragment(
                x: px,
                y: py,
                vx: vx,
                vy: vy,
                width: _random.Next(3, 7),
                height: _random.Next(3, 7),
                lifetime: _random.Next(18, 32),
                gravity: 0.18f,
                baseColor: Color.FromArgb(240, r, g, b)));
        }
    }

    private void SpawnBulletHitFragments(Rectangle bullet, Rectangle bar, bool pierceVisual)
    {
        int cx = (Math.Min(bullet.Left, bar.Left) + Math.Max(bullet.Right, bar.Right)) / 2;
        int cy = (Math.Min(bullet.Top, bar.Top) + Math.Max(bullet.Bottom, bar.Bottom)) / 2;
        int count = _random.Next(2, 4);
        EnsureFragmentCapacity(count);
        for (int i = 0; i < count; i++)
        {
            float vx = (float)(_random.NextDouble() * 1.4 - 0.7);
            float vy = (float)(-2.8 - _random.NextDouble() * 1.6);
            Color c = pierceVisual
                ? Color.FromArgb(235, 220, 140, 255)
                : Color.FromArgb(230, 255, 225, 130);
            _entities.Fragments.Add(new Fragment(
                x: cx + (float)(_random.NextDouble() * 3 - 1.5),
                y: cy + (float)(_random.NextDouble() * 3 - 1.5),
                vx: vx,
                vy: vy,
                width: _random.Next(2, 4),
                height: _random.Next(2, 4),
                lifetime: _random.Next(8, 14),
                gravity: 0.14f,
                baseColor: c));
        }
    }

    private void SpawnSpecialDestroyFeedback(Rectangle bounds)
    {
        int cx = bounds.Left + bounds.Width / 2;
        int cy = bounds.Top + Math.Max(1, bounds.Height / 2);

        int particleCount = _random.Next(5, 8);
        for (int i = 0; i < particleCount; i++)
        {
            float vx = (float)(_random.NextDouble() * 5.4 - 2.7);
            float vy = (float)(-3.6 - _random.NextDouble() * 2.3);
            Color c = _random.Next(3) switch
            {
                0 => Color.FromArgb(255, 255, 245, 140),
                1 => Color.FromArgb(255, 255, 215, 95),
                _ => Color.FromArgb(255, 255, 190, 70)
            };
            _entities.Particles.Add(new Particle(cx, cy, vx, vy, _random.Next(12, 20), c));
        }

        int fragmentCount = _random.Next(2, 5);
        EnsureFragmentCapacity(fragmentCount);
        for (int i = 0; i < fragmentCount; i++)
        {
            float px = bounds.Left + (float)_random.NextDouble() * bounds.Width;
            float py = bounds.Top + (float)_random.NextDouble() * Math.Max(1, bounds.Height);
            float vx = (float)(_random.NextDouble() * 2.4 - 1.2);
            float vy = (float)(-2.6 - _random.NextDouble() * 1.6);
            _entities.Fragments.Add(new Fragment(
                x: px,
                y: py,
                vx: vx,
                vy: vy,
                width: _random.Next(2, 5),
                height: _random.Next(2, 5),
                lifetime: _random.Next(9, 16),
                gravity: 0.15f,
                baseColor: Color.FromArgb(235, 255, 210, 90)));
        }
    }

    private void EnsureFragmentCapacity(int incoming)
    {
        int overflow = _entities.Fragments.Count + incoming - GameConfig.Effects.MaxActiveFragments;
        if (overflow <= 0) return;
        int remove = Math.Min(overflow, _entities.Fragments.Count);
        _entities.Fragments.RemoveRange(0, remove);
    }

    private static Color GetBarFragmentColor(float healthRatio)
    {
        if (healthRatio > 0.5f)
        {
            float t = 2f * (1f - healthRatio);
            return LerpColor(Color.LimeGreen, Color.Gold, t);
        }
        return LerpColor(Color.Gold, Color.Firebrick, 1f - 2f * healthRatio);
    }

    private static Color LerpColor(Color from, Color to, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb(
            255,
            (int)(from.R + (to.R - from.R) * t),
            (int)(from.G + (to.G - from.G) * t),
            (int)(from.B + (to.B - from.B) * t));
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
        int count = pierceVisual ? 6 : 4;
        for (int i = 0; i < count; i++)
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
