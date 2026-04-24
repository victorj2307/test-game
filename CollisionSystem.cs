using System.Drawing;

namespace RetroArcade;

/// <summary>Bullet vs bar hits, damage, particles, and destroy scoring hooks.</summary>
public sealed class CollisionSystem
{
    private readonly GameState _state;
    private readonly EntityManager _entities;
    private readonly Random _random;
    private readonly DifficultySystem _difficulty;

    public CollisionSystem(GameState state, EntityManager entities, Random random, DifficultySystem difficulty)
    {
        _state = state;
        _entities = entities;
        _random = random;
        _difficulty = difficulty;
    }

    public void Resolve(int damagePerHit)
    {
        for (int bi = _entities.Bullets.Count - 1; bi >= 0; bi--)
        {
            var bl = _entities.Bullets[bi];
            Rectangle bRect = bl.GetBounds();
            for (int j = 0; j < _entities.Bars.Count; j++)
            {
                if (!_entities.Bars[j].GetBounds().IntersectsWith(bRect)) continue;
                _entities.Bars[j].ApplyDamage(damagePerHit);
                _entities.Bars[j].RegisterHit();
                GameAudio.PlayHit();
                SpawnHitParticles(bl.GetBounds(), _entities.Bars[j].GetBounds());
                _entities.Bullets.RemoveAt(bi);
                if (_entities.Bars[j].IsDestroyed)
                {
                    _state.RegisterBarDestroyed();
                    _difficulty.SyncBarSpeedFromScore();
                    _entities.Bars.RemoveAt(j);
                }
                break;
            }
        }
    }

    private void SpawnHitParticles(Rectangle bullet, Rectangle bar)
    {
        int cx = (Math.Min(bullet.Left, bar.Left) + Math.Max(bullet.Right, bar.Right)) / 2;
        int cy = (Math.Min(bullet.Top, bar.Top) + Math.Max(bullet.Bottom, bar.Bottom)) / 2;
        for (int i = 0; i < 10; i++)
        {
            float vx = (float)(_random.NextDouble() * 4 - 2);
            float vy = (float)(-_random.NextDouble() * 3 - 1);
            var c = _random.Next(3) == 0 ? Color.Orange : Color.Gold;
            _entities.Particles.Add(new Particle(cx, cy, vx, vy, _random.Next(12, 24), c));
        }
    }
}
