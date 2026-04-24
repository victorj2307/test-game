using System.Drawing;
using Game.Audio;
using Game.Entities;
using Game.Rendering;
using Game.Systems;

namespace Game.Core;

/// <summary>Orchestrates game flow; delegates to GameState, EntityManager, and focused systems.</summary>
public sealed class GameManager
{
    public const int DamagePerHit = 20;
    private const int InitialBarSpeed = 1;
    private const int InitialSpawnIntervalFrames = 90;
    private const int FireCooldownMs = 100;
    private const int PlayerStepCooldownMs = 75;
    private const float MultiShotSpreadDrift = 0.85f;

    private readonly GameState _state = new();
    private readonly EntityManager _entities = new();
    private readonly Random _random = new();
    private readonly DifficultySystem _difficulty;
    private readonly CollisionSystem _collisions;
    private readonly SpawnSystem _spawn;
    private readonly RenderSystem _renderer = new();
    private int _lastAppliedBarSpeed = -1;
    private int _lastClientWidth = 480;
    private int _lastPlayHeight = 600;

    public GameManager()
    {
        _difficulty = new DifficultySystem(_state, _entities);
        _spawn = new SpawnSystem(_state, _entities, _random);
        _collisions = new CollisionSystem(_state, _entities, _random, _difficulty, _spawn);
    }

    public bool ShowDebug { get; set; }
    public int DebugFps { get; set; }

    public Player Player { get; private set; } = null!;

    public bool IsPlaying => _state.IsPlaying;
    public bool IsLifeLost => _state.IsLifeLost;
    public bool IsPaused => _state.IsPaused;
    public bool IsDevMode => _state.IsDevMode;
    public int Score => _state.Score;
    public bool IsGameOver => _state.IsGameOver;
    public int ElapsedFrames => _state.ElapsedFrames;
    public int HighScore => _state.HighScore;
    public int Lives => _state.Lives;

    public int ComboMultiplier => _state.ComboMultiplier;

    private static int PlayerStepPixels => SpawnSystem.BarWidth / 2;

    public void ToggleDebug() => ShowDebug = !ShowDebug;

    public void ToggleDevMode()
    {
#if DEBUG
        _state.IsDevMode = !_state.IsDevMode;
        if (_state.IsPlaying && !_state.IsGameOver)
        {
            _difficulty.SyncBarSpeedFromScore();
            SyncBarSpeedTargetForActiveEffects(force: true);
        }
#endif
    }

    /// <summary>Dev mode only: instant power-up (1=Rapid, 2=Multi, 3=Bomb, 4=Pierce, 5=Shield, 6=Slow). No-op in Release builds.</summary>
    public void TryDevActivatePowerUpDigit(int digit1To6)
    {
#if DEBUG
        if (!_state.IsDevMode || !_state.IsPlaying || _state.IsGameOver || _state.IsPaused || _state.IsLifeLost)
            return;
        PowerUpType? t = digit1To6 switch
        {
            1 => PowerUpType.RapidFire,
            2 => PowerUpType.MultiShot,
            3 => PowerUpType.BombShot,
            4 => PowerUpType.PiercingShot,
            5 => PowerUpType.Shield,
            6 => PowerUpType.SlowMotion,
            _ => null
        };
        if (t is not PowerUpType type) return;
        ActivateCollectedPowerUp(type, _lastClientWidth, _lastPlayHeight);
#endif
    }

    public void TogglePause()
    {
        if (!_state.IsPlaying || _state.IsGameOver || _state.IsLifeLost) return;
        _state.IsPaused = !_state.IsPaused;
    }

    public void Initialize(int clientWidth, int playHeight)
    {
        int pw = 48;
        int ph = 18;
        int x = (clientWidth - pw) / 2;
        Player = new Player(x, playHeight - ph, pw, ph);
        Player.SetBottom(playHeight);
        Player.ClampAndSnapToGrid(clientWidth, PlayerStepPixels);
        _state.SpawnCountdown = 30;
    }

    private void ResetState(int clientWidth, int playHeight)
    {
        _entities.Clear();
        _collisions.ClearBombKillQueue();
        _state.ResetRun(InitialBarSpeed, InitialSpawnIntervalFrames);
        _lastAppliedBarSpeed = -1;
        Initialize(clientWidth, playHeight);
    }

    public void EnterAttractMode(int clientWidth, int playHeight)
    {
        _state.IsPlaying = false;
        ResetState(clientWidth, playHeight);
    }

    public void StartNewGame(int clientWidth, int playHeight)
    {
        _state.IsPlaying = true;
        ResetState(clientWidth, playHeight);
    }

    public void TryFire()
    {
        if (!_state.IsPlaying || _state.IsGameOver || _state.IsPaused) return;
        const int bulletW = 4;
        const int bulletH = 10;
        int pierce = _state.GetPierceCount();
        int bw = pierce > 0 ? bulletW + 2 : bulletW;
        int bh = pierce > 0 ? bulletH + 2 : bulletH;
        Player.GetBulletSpawn(bw, bh, out int bx, out int by);
        if (_state.IsMultiShotActive)
        {
            _entities.Bullets.Add(new Bullet(bx - 10, by, bw, bh, 10, -MultiShotSpreadDrift, pierce));
            _entities.Bullets.Add(new Bullet(bx, by, bw, bh, 10, 0f, pierce));
            _entities.Bullets.Add(new Bullet(bx + 10, by, bw, bh, 10, MultiShotSpreadDrift, pierce));
        }
        else
        {
            _entities.Bullets.Add(new Bullet(bx, by, bw, bh, 10, 0f, pierce));
        }
        _state.MuzzleFlashFrames = 2;
        GameAudio.PlayShoot();
    }

    private void ProcessFiring(bool wantFire)
    {
        if (!wantFire) return;
        long now = Environment.TickCount64;
        if (now - _state.LastFireTimeMs < _state.GetFireCooldownMs(FireCooldownMs)) return;
        _state.LastFireTimeMs = now;
        TryFire();
    }

    public void Update(int clientWidth, int playHeight, bool moveLeft, bool moveRight, bool wantFire, float deltaSeconds)
    {
        _lastClientWidth = clientWidth;
        _lastPlayHeight = playHeight;
        if (!_state.IsPlaying) return;
        if (_state.IsPaused) return;
        if (playHeight < Player.Height + 40) return;

        _state.AdvanceFrame(deltaSeconds);
        if (_state.IsLifeLost) return;

        Player.SetBottom(playHeight);
        if (!_state.IsGameOver)
        {
            long stepNow = Environment.TickCount64;
            int step = PlayerStepPixels;
            if (moveLeft && !moveRight && stepNow - _state.LastPlayerStepLeftMs >= PlayerStepCooldownMs)
            {
                Player.TryStep(-1, clientWidth, step);
                _state.LastPlayerStepLeftMs = stepNow;
            }
            else if (moveRight && !moveLeft && stepNow - _state.LastPlayerStepRightMs >= PlayerStepCooldownMs)
            {
                Player.TryStep(1, clientWidth, step);
                _state.LastPlayerStepRightMs = stepNow;
            }
            ProcessFiring(wantFire);

            _entities.UpdateBullets();
            _entities.UpdateParticles();
            _entities.UpdatePowerUps(playHeight);
            CollectPowerUps(clientWidth, playHeight);
            if (_state.TickBombFuse())
                _collisions.ExecuteBombExplosionAt(_state.BombIndicatorX, _state.BombIndicatorY);
            SyncBarSpeedTargetForActiveEffects(force: false);
            _entities.UpdateBars();

            foreach (var b in _entities.Bars.ToList())
            {
                if (b.Y + b.Height >= playHeight)
                {
                    if (_state.TryConsumeShield())
                    {
                        _entities.Bars.Remove(b);
                        continue;
                    }
                    if (_state.IsDevMode)
                    {
                        _entities.Bars.Remove(b);
                        _state.ShakeUntilTickMs = Environment.TickCount64 + 80;
                        continue;
                    }
                    bool depleted = _state.LoseLife();
                    if (depleted)
                    {
                        _state.ApplyGameOverShakeAndClearMuzzle();
                        GameAudio.PlayGameOver();
                    }
                    else
                    {
                        HandleLifeLost();
                    }
                    return;
                }
            }

            _collisions.Resolve(DamagePerHit);
            _entities.UpdateExplosions();
            _collisions.TickPendingBombKills();
            if (!_state.IsGameOver) _spawn.TrySpawn(clientWidth);
        }

        _difficulty.Tick();
    }

    private void HandleLifeLost()
    {
        _entities.Clear();
        _collisions.ClearBombKillQueue();
        _state.ResetAfterLifeLost();
        _lastAppliedBarSpeed = -1;
        _state.IsLifeLost = true;
        _state.ShakeUntilTickMs = Environment.TickCount64 + 180;
        _state.SpawnIntervalFrames = Math.Min(InitialSpawnIntervalFrames, _state.SpawnIntervalFrames + 6);
    }

    private void SyncBarSpeedTargetForActiveEffects(bool force)
    {
        int effective = _state.GetEffectiveBarSpeed();
        if (!force && effective == _lastAppliedBarSpeed) return;
        _lastAppliedBarSpeed = effective;
        foreach (var bar in _entities.Bars) bar.SetTargetMoveSpeed(effective);
    }

    private void CollectPowerUps(int clientWidth, int playHeight)
    {
        Rectangle playerBounds = Player.GetBounds();
        for (int i = _entities.PowerUps.Count - 1; i >= 0; i--)
        {
            if (!_entities.PowerUps[i].GetBounds().IntersectsWith(playerBounds)) continue;
            ActivateCollectedPowerUp(_entities.PowerUps[i].Type, clientWidth, playHeight);
            _entities.PowerUps.RemoveAt(i);
        }
    }

    private void ActivateCollectedPowerUp(PowerUpType type, int clientWidth, int playHeight)
    {
        if (type == PowerUpType.BombShot)
        {
            _state.ActivatePowerUp(type);
            _state.BeginPendingBomb(_random, clientWidth, playHeight);
#if DEBUG
            System.Diagnostics.Debug.WriteLine(
                $"[Bomb] Fuse {_state.BombFuseFramesLeft}f at ({_state.BombIndicatorX:0},{_state.BombIndicatorY:0})");
#endif
            return;
        }
        _state.ActivatePowerUp(type);
        SyncBarSpeedTargetForActiveEffects(force: true);
    }

    public void ContinueAfterLifeLost(int clientWidth, int playHeight)
    {
        if (!_state.IsLifeLost || _state.IsGameOver) return;
        _state.IsLifeLost = false;
        Player.SetBottom(playHeight);
        Player.ClampAndSnapToGrid(clientWidth, PlayerStepPixels);
    }

    public void OnClientResize(int clientWidth, int playHeight)
    {
        Player.SetBottom(playHeight);
        Player.ClampAndSnapToGrid(clientWidth, PlayerStepPixels);
    }

    public void Draw(Graphics g, int clientWidth, int playHeight, Font uiFont)
    {
        _renderer.Draw(g, clientWidth, playHeight, uiFont, _state, _entities, Player, ShowDebug, DebugFps);
    }
}
