using System.Diagnostics;
using System.Drawing;
using Game.Audio;
using Game.Core;
using Game.Entities;

namespace Game.Systems;

/// <summary>
/// Bullet vs bar hits, damage, particles, destroy scoring, and bomb wave queue.
/// Piercing shots track bars already struck in the current <see cref="Resolve"/> pass so pierce budget is not consumed
/// on the same target repeatedly in one frame. Bomb explosion ranking reuses scratch collections to limit allocations.
/// </summary>
public sealed class CollisionSystem
{
    private const int MaxLaneProbeOffset = 1;
    private readonly GameState _state;
    private readonly EntityManager _entities;
    private readonly Random _random;
    private readonly DifficultySystem _difficulty;
    private readonly SpawnSystem _spawn;

    private readonly List<PendingBombKill> _bombKillQueue = new();
    private readonly Dictionary<int, List<int>> _barsByLane = new();
    private readonly HashSet<Bar> _hitBarsThisBullet = new();
    private readonly List<(int Idx, int Bottom, long Dist2)> _rankedScratch = new();
    private readonly List<(Bar Bar, long Dist2)> _killBarsScratch = new();
    private readonly HashSet<Bar> _killSetScratch = new();

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
    /// Piercing bullets may resolve multiple impacts in one frame while pierce budget remains, but each bar is hit at most once per bullet per call.
    /// </summary>
    public void Resolve(int damagePerHit)
    {
        BuildBarsByLane();
        for (int bi = _entities.Bullets.Count - 1; bi >= 0; bi--)
        {
            Bullet bl = _entities.Bullets[bi];
            bool removeBullet = false;
            _hitBarsThisBullet.Clear();
            while (!removeBullet)
            {
                Rectangle bRect = bl.GetBounds();
                int hitIndex = FindHitBarIndexInBulletLanes(bRect, _hitBarsThisBullet);

                if (hitIndex < 0) break;

                Bar hitBar = _entities.Bars[hitIndex];
                _hitBarsThisBullet.Add(hitBar);
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
                int hx = (Math.Min(bRect.Left, hitBarRect.Left) + Math.Max(bRect.Right, hitBarRect.Right)) / 2;
                int hy = (Math.Min(bRect.Top, hitBarRect.Top) + Math.Max(bRect.Bottom, hitBarRect.Bottom)) / 2;
                _state.TriggerImpactFlash(hx, hy);

                if (!bl.ConsumePierce())
                    removeBullet = true;

                if (hitIndex < _entities.Bars.Count && _entities.Bars[hitIndex].IsDestroyed)
                {
                    DestroyBarAt(hitIndex);
                    BuildBarsByLane();
                }

                if (removeBullet)
                {
                    _entities.Bullets.RemoveAt(bi);
                    break;
                }
            }
        }
    }

    private int GetLaneIndexFromX(int x)
    {
        int laneWidth = Math.Max(1, GameRuntimeConfig.Current.CollisionLaneWidth);
        return x / laneWidth;
    }

    private void BuildBarsByLane()
    {
        foreach (var kvp in _barsByLane)
            kvp.Value.Clear();

        for (int i = 0; i < _entities.Bars.Count; i++)
        {
            Bar bar = _entities.Bars[i];
            if (bar.Height <= 0) continue;
            Rectangle r = bar.GetBounds();
            if (r.Width <= 0 || r.Height <= 0) continue;
            int startLane = GetLaneIndexFromX(r.Left);
            int endLane = GetLaneIndexFromX(Math.Max(r.Left, r.Right - 1));
            for (int lane = startLane; lane <= endLane; lane++)
            {
                if (!_barsByLane.TryGetValue(lane, out var list))
                {
                    list = [];
                    _barsByLane[lane] = list;
                }

                list.Add(i);
            }
        }
    }

    /// <summary>
    /// Broad-phase lane probe then AABB test; returns lowest bar list index among intersecting candidates, or -1.
    /// When <paramref name="ignoreBars"/> is non-null, those bars are skipped (piercing same-frame repeat guard).
    /// </summary>
    private int FindHitBarIndexInBulletLanes(Rectangle bulletRect, IReadOnlySet<Bar>? ignoreBars = null)
    {
        int startLane = GetLaneIndexFromX(bulletRect.Left) - MaxLaneProbeOffset;
        int endLane = GetLaneIndexFromX(Math.Max(bulletRect.Left, bulletRect.Right - 1)) + MaxLaneProbeOffset;
        int hitIndex = -1;
        for (int lane = startLane; lane <= endLane; lane++)
        {
            if (!_barsByLane.TryGetValue(lane, out var candidates)) continue;
            for (int c = 0; c < candidates.Count; c++)
            {
                int idx = candidates[c];
                if (idx < 0 || idx >= _entities.Bars.Count) continue;
                Bar bar = _entities.Bars[idx];
                if (bar.Height <= 0) continue;
                if (ignoreBars is not null && ignoreBars.Contains(bar)) continue;
                if (!bar.GetBounds().IntersectsWith(bulletRect)) continue;
                if (hitIndex < 0 || idx < hitIndex)
                    hitIndex = idx;
            }
        }

        return hitIndex;
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
        int points = _state.RegisterBarDestroyed(isSpecial);
        float popupX = bounds.X + bounds.Width * 0.5f;
        float popupY = Math.Max(8f, bounds.Y + bounds.Height * 0.35f);
        _entities.EnsureScorePopupCapacity();
        _entities.ScorePopups.Add(new ScorePopup(popupX, popupY, points, GameConfig.Effects.ScorePopupLifetimeFrames));
        _spawn.TrySpawnPowerUpAt(bounds);
        _difficulty.SyncDifficultyAfterDestroy();
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
        GameAudio.PlayBomb();
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

        _rankedScratch.Clear();
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
            _rankedScratch.Add((i, bottom, d2));
        }

        if (_rankedScratch.Count == 0) return;

        int killCount = Math.Max(1, Math.Min(_rankedScratch.Count - 1, (int)Math.Floor(_rankedScratch.Count * GameConfig.Effects.BombKillFraction)));

        _rankedScratch.Sort(static (a, b) =>
        {
            int c = b.Bottom.CompareTo(a.Bottom);
            if (c != 0) return c;
            return a.Dist2.CompareTo(b.Dist2);
        });

        _killBarsScratch.Clear();
        for (int k = 0; k < killCount; k++)
        {
            int idx = _rankedScratch[k].Idx;
            _killBarsScratch.Add((_entities.Bars[idx], _rankedScratch[k].Dist2));
        }

        _killBarsScratch.Sort(static (a, b) => a.Dist2.CompareTo(b.Dist2));

        _killSetScratch.Clear();
        foreach ((Bar bar, _) in _killBarsScratch)
            _killSetScratch.Add(bar);

        long r2 = (long)GameConfig.Effects.BombRadius * GameConfig.Effects.BombRadius;
        for (int i = _entities.Bars.Count - 1; i >= 0; i--)
        {
            Bar bar = _entities.Bars[i];
            if (_killSetScratch.Contains(bar)) continue;
            Rectangle b = bar.GetBounds();
            int bx = b.Left + b.Width / 2;
            int by = b.Top + b.Height / 2;
            long dx = bx - cx;
            long dy = by - cy;
            if (dx * dx + dy * dy > r2) continue;
            bar.ApplyDamage(GameConfig.Effects.BombSplashDamage);
            bar.RegisterHit();
            // Skip per-bar hit particles on splash — ring + center burst already sell the bomb.
            if (bar.IsDestroyed) DestroyBarAt(i, DestroyStyle.Bomb, cx, cy);
        }

        for (int i = 0; i < _killBarsScratch.Count; i++)
        {
            int delay = GameConfig.Effects.BombKillDelayStartFrames + i * GameConfig.Effects.BombKillStaggerFrames;
            _bombKillQueue.Add(new PendingBombKill
            {
                Bar = _killBarsScratch[i].Bar,
                FramesLeft = delay,
                ExplosionX = cx,
                ExplosionY = cy
            });
        }
    }

    private void SpawnBombFragments(Rectangle barBounds, float healthRatio, int explosionX, int explosionY)
    {
        int count = _random.Next(3, 7);
        _entities.EnsureFragmentCapacity(count);
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
        int count = _random.Next(1, 3);
        _entities.EnsureFragmentCapacity(count);
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
                width: _random.Next(3, 5),
                height: _random.Next(3, 5),
                lifetime: _random.Next(8, 14),
                gravity: 0.14f,
                baseColor: c));
        }
    }

    private void SpawnSpecialDestroyFeedback(Rectangle bounds)
    {
        int cx = bounds.Left + bounds.Width / 2;
        int cy = bounds.Top + Math.Max(1, bounds.Height / 2);

        int particleCount = _random.Next(3, 6);
        _entities.EnsureParticleCapacity(particleCount);
        for (int i = 0; i < particleCount; i++)
        {
            float vx = (float)(_random.NextDouble() * 5.4 - 2.7);
            float vy = (float)(-3.6 - _random.NextDouble() * 2.3);
            Color c = _random.Next(3) switch
            {
                0 => Color.FromArgb(255, 255, 255, 200),
                1 => Color.FromArgb(255, 255, 230, 120),
                _ => Color.FromArgb(255, 255, 200, 80)
            };
            int size = i == 0 ? 5 : _random.Next(3, 5);
            _entities.Particles.Add(new Particle(cx, cy, vx, vy, _random.Next(14, 22), c, size));
        }

        // 1–2 large gold chunks so specials read clearly vs normal hits.
        int fragmentCount = _random.Next(1, 3);
        _entities.EnsureFragmentCapacity(fragmentCount);
        for (int i = 0; i < fragmentCount; i++)
        {
            float px = bounds.Left + (float)_random.NextDouble() * bounds.Width;
            float py = bounds.Top + (float)_random.NextDouble() * Math.Max(1, bounds.Height);
            float vx = (float)(_random.NextDouble() * 2.8 - 1.4);
            float vy = (float)(-3.0 - _random.NextDouble() * 1.8);
            _entities.Fragments.Add(new Fragment(
                x: px,
                y: py,
                vx: vx,
                vy: vy,
                width: _random.Next(6, 11),
                height: _random.Next(6, 11),
                lifetime: _random.Next(12, 20),
                gravity: 0.15f,
                baseColor: Color.FromArgb(250, 255, 220, 100)));
        }
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
        int count = GameConfig.Effects.BombExplosionParticleCount;
        _entities.EnsureParticleCapacity(count);
        for (int i = 0; i < count; i++)
        {
            double ang = _random.NextDouble() * Math.PI * 2;
            float sp = (float)(3.5 + _random.NextDouble() * 6.5);
            float vx = MathF.Cos((float)ang) * sp;
            float vy = MathF.Sin((float)ang) * sp - 2f;
            Color c = _random.Next(4) switch
            {
                0 => Color.FromArgb(255, 255, 240, 120),
                1 => Color.FromArgb(255, 255, 160, 50),
                2 => Color.FromArgb(255, 255, 100, 40),
                _ => Color.FromArgb(255, 255, 220, 180)
            };
            int size = _random.Next(4, 7);
            _entities.Particles.Add(new Particle(cx, cy, vx, vy, _random.Next(18, 34), c, size));
        }
    }

    private void SpawnHitParticles(Rectangle bullet, Rectangle bar, bool pierceVisual)
    {
        int cx = (Math.Min(bullet.Left, bar.Left) + Math.Max(bullet.Right, bar.Right)) / 2;
        int cy = (Math.Min(bullet.Top, bar.Top) + Math.Max(bullet.Bottom, bar.Bottom)) / 2;
        int count = pierceVisual ? 4 : 3;
        _entities.EnsureParticleCapacity(count);
        for (int i = 0; i < count; i++)
        {
            float vx = (float)(_random.NextDouble() * 4 - 2);
            float vy = (float)(-_random.NextDouble() * 3 - 1);
            Color c = pierceVisual
                ? _random.Next(3) switch
                {
                    0 => Color.FromArgb(255, 255, 140, 255),
                    1 => Color.FromArgb(255, 220, 100, 255),
                    _ => Color.FromArgb(255, 200, 230, 255)
                }
                : _random.Next(3) == 0 ? Color.Orange : Color.Gold;
            // First spark is the bright core; others are slightly smaller trails.
            int size = i == 0 ? 5 : _random.Next(3, 5);
            _entities.Particles.Add(new Particle(cx, cy, vx, vy, _random.Next(12, 24), c, size));
        }
    }
}
