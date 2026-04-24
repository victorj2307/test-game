using System.Drawing;
using System.Linq;
using Game.Audio;
using Game.Entities;
using Game.Rendering;
using Game.Systems;

namespace Game.Core;

/// <summary>
/// Orchestrates game flow; delegates to <see cref="GameState"/>, <see cref="EntityManager"/>, and focused systems.
/// Leaderboard qualification for UI uses the in-memory list; call <see cref="DisposeResources"/> when the host form closes.
/// </summary>
public sealed class GameManager
{
    public const int LeaderboardMaxEntries = GameConfig.Persistence.LeaderboardMaxEntries;

    private readonly GameState _state = new();
    private readonly EntityManager _entities = new();
    private readonly Random _random = new();
    private readonly DifficultySystem _difficulty;
    private readonly CollisionSystem _collisions;
    private readonly SpawnSystem _spawn;
    private readonly RenderSystem _renderer = new();
    private List<HighScoreStore.LeaderboardEntry> _leaderboard;
    private int _lastAppliedBarSpeed = -1;
    private int _lastClientWidth = 480;
    private int _lastPlayHeight = 600;

    public GameManager()
    {
        GameAudio.Initialize();
        _difficulty = new DifficultySystem(_state, _entities);
        _spawn = new SpawnSystem(_state, _entities, _random);
        _collisions = new CollisionSystem(_state, _entities, _random, _difficulty, _spawn);
        _leaderboard = HighScoreStore.LoadLeaderboard(LeaderboardMaxEntries).ToList();
    }

    public bool ShowDebug { get; set; }
    public int DebugFps { get; set; }

    /// <summary>Player entity currently active for this run.</summary>
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
    public IReadOnlyList<HighScoreStore.LeaderboardEntry> Leaderboard => _leaderboard;

    public int ComboMultiplier => _state.ComboMultiplier;

    /// <summary>
    /// Returns true when the current score can enter Top-N leaderboard.
    /// Uses in-memory leaderboard snapshot to avoid extra disk reads.
    /// </summary>
    public bool ScoreQualifiesForLeaderboard() =>
        ScoreQualifiesFromEntries(_state.Score, _leaderboard, LeaderboardMaxEntries);

    /// <summary>
    /// Returns whether <paramref name="score"/> would enter the top <paramref name="maxEntries"/> slots
    /// given an ordered leaderboard snapshot (highest scores first). Used for UI qualification without re-reading disk.
    /// </summary>
    /// <param name="score">Final or current run score; non-positive scores never qualify.</param>
    /// <param name="leaderboard">In-memory list, typically same order as <see cref="HighScoreStore.LoadLeaderboard"/>.</param>
    /// <param name="maxEntries">Board capacity (clamped to at least 1).</param>
    internal static bool ScoreQualifiesFromEntries(
        int score,
        IReadOnlyList<HighScoreStore.LeaderboardEntry> leaderboard,
        int maxEntries)
    {
        if (score <= 0) return false;
        int cap = Math.Max(1, maxEntries);
        if (leaderboard.Count < cap) return true;
        return score > leaderboard[^1].Score;
    }

    /// <summary>Persists a final score entry and refreshes in-memory leaderboard data.</summary>
    public void SubmitLeaderboardScore(string name)
    {
        if (HighScoreStore.TryAddScore(name, _state.Score, LeaderboardMaxEntries))
            _leaderboard = HighScoreStore.LoadLeaderboard(LeaderboardMaxEntries).ToList();
    }

    private static int PlayerStepPixels => GameConfig.Bars.Width / 2;

    /// <summary>Toggles debug rendering overlays.</summary>
    public void ToggleDebug() => ShowDebug = !ShowDebug;

    /// <summary>Toggles developer mode (debug builds only) and reapplies difficulty caps.</summary>
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

    /// <summary>Toggles pause when gameplay is in a pausable state.</summary>
    public void TogglePause()
    {
        if (!_state.IsPlaying || _state.IsGameOver || _state.IsLifeLost) return;
        if (_state.IsPaused) _state.Resume();
        else _state.Pause();
    }

    /// <summary>Creates player and resets per-run spawn countdown against the current viewport.</summary>
    public void Initialize(int clientWidth, int playHeight)
    {
        int pw = GameConfig.Player.Width;
        int ph = GameConfig.Player.Height;
        int x = (clientWidth - pw) / 2;
        Player = new Player(x, playHeight - ph, pw, ph);
        Player.SetBottom(playHeight);
        Player.ClampAndSnapToGrid(clientWidth, PlayerStepPixels);
        _state.SpawnCountdown = GameConfig.Spawn.InitialSpawnCountdownFrames;
    }

    /// <summary>Resets entities/state for a clean run startup.</summary>
    private void ResetState(int clientWidth, int playHeight)
    {
        _entities.Clear();
        _collisions.ClearBombKillQueue();
        _state.ResetRun(GameConfig.Difficulty.MinBarSpeed, GameConfig.Spawn.InitialSpawnIntervalFrames);
        _lastAppliedBarSpeed = -1;
        Initialize(clientWidth, playHeight);
    }

    /// <summary>Enters non-playing attract state and refreshes leaderboard cache.</summary>
    public void EnterAttractMode(int clientWidth, int playHeight)
    {
        _state.SetPlaying(false);
        _state.HideLeaderboardScreen();
        _leaderboard = HighScoreStore.LoadLeaderboard(LeaderboardMaxEntries).ToList();
        ResetState(clientWidth, playHeight);
    }

    /// <summary>Starts a new playable run.</summary>
    public void StartNewGame(int clientWidth, int playHeight)
    {
        _state.StartGame();
        ResetState(clientWidth, playHeight);
    }

    /// <summary>Spawns bullet(s) based on current active power-up state.</summary>
    public void TryFire()
    {
        if (!_state.IsPlaying || _state.IsGameOver || _state.IsPaused) return;
        const int bulletW = GameConfig.Player.BulletWidth;
        const int bulletH = GameConfig.Player.BulletHeight;
        int pierce = _state.GetPierceCount();
        int bw = pierce > 0 ? bulletW + GameConfig.Player.PiercingBulletSizeBoost : bulletW;
        int bh = pierce > 0 ? bulletH + GameConfig.Player.PiercingBulletSizeBoost : bulletH;
        Player.GetBulletSpawn(bw, bh, out int bx, out int by);
        if (_state.IsMultiShotActive)
        {
            _entities.Bullets.Add(new Bullet(bx - GameConfig.Player.MultiShotOffsetX, by, bw, bh, GameConfig.Player.BulletSpeed, -GameConfig.Player.MultiShotSpreadDrift, pierce));
            _entities.Bullets.Add(new Bullet(bx, by, bw, bh, GameConfig.Player.BulletSpeed, 0f, pierce));
            _entities.Bullets.Add(new Bullet(bx + GameConfig.Player.MultiShotOffsetX, by, bw, bh, GameConfig.Player.BulletSpeed, GameConfig.Player.MultiShotSpreadDrift, pierce));
        }
        else
        {
            _entities.Bullets.Add(new Bullet(bx, by, bw, bh, GameConfig.Player.BulletSpeed, 0f, pierce));
        }
        _state.MuzzleFlashFrames = GameConfig.Player.MuzzleFlashFrames;
        GameAudio.PlayShoot();
    }

    /// <summary>Handles hold-to-fire cadence based on cooldown timers.</summary>
    private void ProcessFiring(bool wantFire)
    {
        if (!wantFire) return;
        long now = Environment.TickCount64;
        if (now - _state.LastFireTimeMs < _state.GetFireCooldownMs(GameConfig.Player.FireCooldownMs)) return;
        _state.LastFireTimeMs = now;
        TryFire();
    }

    /// <summary>Main simulation step for one frame.</summary>
    public void Update(int clientWidth, int playHeight, bool moveLeft, bool moveRight, bool wantFire, float deltaSeconds)
    {
        _lastClientWidth = clientWidth;
        _lastPlayHeight = playHeight;
        if (!_state.IsPlaying) return;
        if (_state.IsPaused) return;
        if (playHeight < Player.Height + GameConfig.Player.MinPlayHeightPadding) return;

        _state.AdvanceFrame(deltaSeconds);
        if (_state.IsLifeLost) return;

        Player.SetBottom(playHeight);
        if (!_state.IsGameOver)
        {
            long stepNow = Environment.TickCount64;
            int step = PlayerStepPixels;
            int stepCooldownMs = _state.GetPlayerStepCooldownMs(GameConfig.Player.StepCooldownMs);
            if (moveLeft && !moveRight && stepNow - _state.LastPlayerStepLeftMs >= stepCooldownMs)
            {
                Player.TryStep(-1, clientWidth, step);
                _state.LastPlayerStepLeftMs = stepNow;
            }
            else if (moveRight && !moveLeft && stepNow - _state.LastPlayerStepRightMs >= stepCooldownMs)
            {
                Player.TryStep(1, clientWidth, step);
                _state.LastPlayerStepRightMs = stepNow;
            }
            ProcessFiring(wantFire);

            _entities.UpdateBullets(deltaSeconds);
            _entities.UpdateParticles(deltaSeconds);
            _entities.UpdateFragments(deltaSeconds);
            _entities.UpdatePowerUps(playHeight, deltaSeconds);
            CollectPowerUps(clientWidth, playHeight);
            if (_state.TickBombFuse())
                _collisions.ExecuteBombExplosionAt(_state.BombIndicatorX, _state.BombIndicatorY);
            SyncBarSpeedTargetForActiveEffects(force: false);
            _entities.UpdateBars(deltaSeconds);

            for (int i = _entities.Bars.Count - 1; i >= 0; i--)
            {
                var b = _entities.Bars[i];
                if (b.GetBounds().Bottom >= playHeight)
                {
                    if (_state.TryConsumeShield())
                    {
                        _entities.Bars.RemoveAt(i);
                        continue;
                    }
                    if (_state.IsDevMode)
                    {
                        _entities.Bars.RemoveAt(i);
                        _state.ShakeUntilTickMs = Environment.TickCount64 + GameConfig.Effects.FloorHitDevShakeMs;
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

            _collisions.Resolve(GameConfig.Scoring.DamagePerHit);
            _entities.UpdateExplosions(deltaSeconds);
            _collisions.TickPendingBombKills();
            if (!_state.IsGameOver) _spawn.TrySpawn(clientWidth);
        }

        _difficulty.Tick();
    }

    /// <summary>Applies life-lost transition effects and temporary pacing relief.</summary>
    private void HandleLifeLost()
    {
        GameAudio.PlayLifeLost();
        _entities.Clear();
        _collisions.ClearBombKillQueue();
        _state.ResetAfterLifeLost();
        _lastAppliedBarSpeed = -1;
        _state.SetLifeLost(true);
        _state.ShakeUntilTickMs = Environment.TickCount64 + GameConfig.Effects.LifeLostShakeMs;
        _state.SpawnIntervalFrames = Math.Min(GameConfig.Spawn.InitialSpawnIntervalFrames, _state.SpawnIntervalFrames + GameConfig.Effects.SpawnRelaxAfterLifeLostFrames);
    }

    /// <summary>Updates active bars with latest effective global speed target.</summary>
    private void SyncBarSpeedTargetForActiveEffects(bool force)
    {
        int effective = _state.GetEffectiveBarSpeed();
        if (!force && effective == _lastAppliedBarSpeed) return;
        _lastAppliedBarSpeed = effective;
        foreach (var bar in _entities.Bars) bar.SetTargetMoveSpeed(effective);
    }

    /// <summary>Collects intersecting power-ups and activates their effects.</summary>
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

    /// <summary>Applies one collected power-up effect.</summary>
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

    /// <summary>Resumes gameplay after life-lost pause.</summary>
    public void ContinueAfterLifeLost(int clientWidth, int playHeight)
    {
        if (!_state.IsLifeLost || _state.IsGameOver) return;
        _state.SetLifeLost(false);
        Player.SetBottom(playHeight);
        Player.ClampAndSnapToGrid(clientWidth, PlayerStepPixels);
    }

    /// <summary>Reclamps player position after viewport changes.</summary>
    public void OnClientResize(int clientWidth, int playHeight)
    {
        Player.SetBottom(playHeight);
        Player.ClampAndSnapToGrid(clientWidth, PlayerStepPixels);
    }

    /// <summary>Delegates full-frame rendering to <see cref="RenderSystem"/>.</summary>
    public void Draw(Graphics g, int clientWidth, int playHeight, Font uiFont)
    {
        _renderer.Draw(g, clientWidth, playHeight, uiFont, _state, _entities, Player, _leaderboard, ShowDebug, DebugFps);
    }

    /// <summary>
    /// Releases static GDI resources held by <see cref="Rendering.RenderSystem"/> (pen/brush/font caches, sky brush).
    /// Invoked from <see cref="Game.UI.GameForm"/> on <c>FormClosed</c>.
    /// </summary>
    public void DisposeResources() => RenderSystem.DisposeSharedResources();
}
