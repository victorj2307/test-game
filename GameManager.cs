using System.Drawing;

namespace RetroArcade;

/// <summary>Orchestrates game flow; delegates to GameState, EntityManager, and focused systems.</summary>
public sealed class GameManager
{
    public const int DamagePerHit = 20;
    private const int InitialBarSpeed = 1;
    private const int InitialSpawnIntervalFrames = 90;
    private const int FireCooldownMs = 100;
    private const int PlayerStepCooldownMs = 75;

    private readonly GameState _state = new();
    private readonly EntityManager _entities = new();
    private readonly Random _random = new();
    private readonly DifficultySystem _difficulty;
    private readonly CollisionSystem _collisions;
    private readonly SpawnSystem _spawn;
    private readonly RenderSystem _renderer = new();

    public GameManager()
    {
        _difficulty = new DifficultySystem(_state, _entities);
        _collisions = new CollisionSystem(_state, _entities, _random, _difficulty);
        _spawn = new SpawnSystem(_state, _entities, _random);
    }

    public bool ShowDebug { get; set; }
    public int DebugFps { get; set; }

    public Player Player { get; private set; } = null!;

    public bool IsPlaying => _state.IsPlaying;
    public int Score => _state.Score;
    public bool IsGameOver => _state.IsGameOver;
    public int ElapsedFrames => _state.ElapsedFrames;
    public int HighScore => _state.HighScore;

    public int ComboMultiplier => _state.ComboMultiplier;

    private static int PlayerStepPixels => SpawnSystem.BarWidth / 2;

    public void ToggleDebug() => ShowDebug = !ShowDebug;

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
        _state.ResetRun(InitialBarSpeed, InitialSpawnIntervalFrames);
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
        if (!_state.IsPlaying || _state.IsGameOver) return;
        const int bulletW = 4;
        const int bulletH = 10;
        Player.GetBulletSpawn(bulletW, bulletH, out int bx, out int by);
        _entities.Bullets.Add(new Bullet(bx, by, bulletW, bulletH, 10));
        _state.MuzzleFlashFrames = 2;
        GameAudio.PlayShoot();
    }

    private void ProcessFiring(bool wantFire)
    {
        if (!wantFire) return;
        long now = Environment.TickCount64;
        if (now - _state.LastFireTimeMs < FireCooldownMs) return;
        _state.LastFireTimeMs = now;
        TryFire();
    }

    public void Update(int clientWidth, int playHeight, bool moveLeft, bool moveRight, bool wantFire)
    {
        if (!_state.IsPlaying) return;
        if (playHeight < Player.Height + 40) return;

        _state.AdvanceFrame();

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
            _entities.UpdateBars();

            foreach (var b in _entities.Bars)
            {
                if (b.Y + b.Height >= playHeight)
                {
                    _state.ApplyGameOverShakeAndClearMuzzle();
                    GameAudio.PlayGameOver();
                    return;
                }
            }

            _collisions.Resolve(DamagePerHit);
            if (!_state.IsGameOver) _spawn.TrySpawn(clientWidth);
        }

        _difficulty.Tick();
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
