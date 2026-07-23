using Game.Core;
using Game.Entities;
using Game.Systems;

namespace RetroArcade.Tests;

[TestClass]
public sealed class GameFlowTests
{
    [TestMethod]
    public void ActivatePowerUp_TimedBuff_KeepsExistingShieldCharges()
    {
        var state = new GameState();
        state.ResetRun(GameConfig.Difficulty.MinBarSpeed, GameConfig.Spawn.InitialSpawnIntervalFrames);

        state.ActivatePowerUp(PowerUpType.Shield);
        Assert.IsTrue(state.HasShieldActive);
        Assert.AreEqual(PowerUpType.Shield, state.ActivePowerUp);

        state.ActivatePowerUp(PowerUpType.RapidFire);

        Assert.AreEqual(PowerUpType.RapidFire, state.ActivePowerUp);
        Assert.IsTrue(state.HasShieldActive, "Shield charges should survive a timed buff replacing the HUD slot.");
        Assert.IsTrue(state.PowerUpTimerFrames > 0);
    }

    [TestMethod]
    public void TryConsumeShield_BlocksBeforeLifeLoss()
    {
        var state = new GameState();
        state.ResetRun(GameConfig.Difficulty.MinBarSpeed, GameConfig.Spawn.InitialSpawnIntervalFrames);
        state.ActivatePowerUp(PowerUpType.Shield);
        int livesBefore = state.Lives;

        Assert.IsTrue(state.TryConsumeShield());
        Assert.IsFalse(state.HasShieldActive);
        Assert.AreEqual(livesBefore, state.Lives, "Shield consume must not spend a life.");
        Assert.IsFalse(state.TryConsumeShield(), "No charges left after one consume.");
    }

    [TestMethod]
    public void LifeLost_ResetClearsPowerUpsAndContinueClearsFlag()
    {
        var state = new GameState();
        state.ResetRun(GameConfig.Difficulty.MinBarSpeed, GameConfig.Spawn.InitialSpawnIntervalFrames);
        state.StartGame();
        state.ActivatePowerUp(PowerUpType.Shield);
        state.ActivatePowerUp(PowerUpType.RapidFire);

        int lives = state.Lives;
        Assert.IsFalse(state.LoseLife());
        Assert.AreEqual(lives - 1, state.Lives);

        state.ResetAfterLifeLost();
        state.SetLifeLost(true);

        Assert.IsTrue(state.IsLifeLost);
        Assert.IsFalse(state.IsPaused, "Life-lost must not leave IsPaused set.");
        Assert.IsNull(state.ActivePowerUp);
        Assert.IsFalse(state.HasShieldActive);

        state.SetLifeLost(false);
        Assert.IsFalse(state.IsLifeLost);
        Assert.IsTrue(state.IsPlaying);
    }

    [TestMethod]
    public void GameManager_LifeLostContinue_ResumesPlay()
    {
        var game = CreatePlayingGameWithFloorBar(out int width, out int height, shield: false);

        game.Update(width, height, false, false, false, 1f / GameConfig.Ui.TargetFps);

        Assert.IsTrue(game.IsLifeLost, "Floor contact without shield should enter life-lost.");
        Assert.IsFalse(game.IsPaused);
        Assert.IsFalse(game.IsGameOver);

        game.ContinueAfterLifeLost(width, height);

        Assert.IsFalse(game.IsLifeLost);
        Assert.IsTrue(game.IsPlaying);
    }

    [TestMethod]
    public void GameManager_ShieldFloorHit_DoesNotEnterLifeLost()
    {
        var game = CreatePlayingGameWithFloorBar(out int width, out int height, shield: true);
        int livesBefore = game.Lives;

        game.Update(width, height, false, false, false, 1f / GameConfig.Ui.TargetFps);

        Assert.IsFalse(game.IsLifeLost);
        Assert.AreEqual(livesBefore, game.Lives);
    }

    [TestMethod]
    public void Update_WhilePaused_DoesNotAdvanceScore()
    {
        var game = new GameManager();
        const int width = 480;
        const int height = 600;
        game.Initialize(width, height);
        game.StartNewGame(width, height);
        game.TogglePause();
        Assert.IsTrue(game.IsPaused);

        int scoreBefore = game.Score;
        game.Update(width, height, false, false, false, 1f / GameConfig.Ui.TargetFps);
        game.Update(width, height, true, false, true, 1f / GameConfig.Ui.TargetFps);

        Assert.IsTrue(game.IsPaused);
        Assert.AreEqual(scoreBefore, game.Score, "Paused update must skip simulation side effects.");
    }

    [TestMethod]
    public void BombExplosion_QueuesStaggeredKills()
    {
        var state = new GameState();
        var entities = new EntityManager();
        var difficulty = new DifficultySystem(state, entities);
        var spawn = new SpawnSystem(state, entities, new Random(7));
        var collisions = new CollisionSystem(state, entities, new Random(11), difficulty, spawn);
        state.ResetRun(GameConfig.Difficulty.MinBarSpeed, GameConfig.Spawn.InitialSpawnIntervalFrames);
        state.StartGame();

        // Place bars far apart so splash does not destroy survivors on the arming frame;
        // hybrid cull still queues the closest danger-ranked bars for staggered kills.
        entities.Bars.Add(new Bar(40, 40, GameConfig.Bars.Width, 80, BarType.Normal, false, 80, GameConfig.Bars.NormalSpeedScale, state.BarSpeed));
        entities.Bars.Add(new Bar(40, 400, GameConfig.Bars.Width, 80, BarType.Normal, false, 80, GameConfig.Bars.NormalSpeedScale, state.BarSpeed));
        entities.Bars.Add(new Bar(400, 40, GameConfig.Bars.Width, 80, BarType.Normal, false, 80, GameConfig.Bars.NormalSpeedScale, state.BarSpeed));
        entities.Bars.Add(new Bar(400, 400, GameConfig.Bars.Width, 80, BarType.Normal, false, 80, GameConfig.Bars.NormalSpeedScale, state.BarSpeed));

        int before = entities.Bars.Count;
        // Detonate near the bottom-left bar so ranking prefers that lane; others stay outside splash.
        collisions.ExecuteBombExplosionAt(52f, 440f);

        Assert.AreEqual(before, entities.Bars.Count, "Bomb kills are queued, not immediate for multi-bar blasts.");

        int minDelay = GameConfig.Effects.BombKillDelayStartFrames;
        for (int f = 0; f < minDelay; f++)
            collisions.TickPendingBombKills();

        Assert.IsTrue(entities.Bars.Count < before, "After start delay, staggered kills should begin removing bars.");

        for (int f = 0; f < 60; f++)
            collisions.TickPendingBombKills();

        Assert.IsTrue(entities.Bars.Count >= 1);
        Assert.IsTrue(entities.Bars.Count < before);
    }

    [TestMethod]
    public void BombFuse_TicksUntilDetonationSignal()
    {
        var state = new GameState();
        state.ResetRun(GameConfig.Difficulty.MinBarSpeed, GameConfig.Spawn.InitialSpawnIntervalFrames);
        var rng = new Random(0);
        state.BeginPendingBomb(rng, playWidth: 480, playHeight: 600);
        Assert.IsTrue(state.HasPendingBombPickup);
        int fuse = state.BombFuseFramesLeft;
        Assert.IsTrue(fuse >= GameConfig.Effects.BombFuseMinFrames);

        bool detonated = false;
        for (int i = 0; i < fuse; i++)
            detonated = state.TickBombFuse();

        Assert.IsTrue(detonated);
        Assert.IsFalse(state.HasPendingBombPickup);
    }

    private static GameManager CreatePlayingGameWithFloorBar(out int width, out int height, bool shield)
    {
        width = 480;
        height = 600;
        var game = new GameManager();
        game.Initialize(width, height);
        game.StartNewGame(width, height);

        if (shield)
            game.StateForTests.ActivatePowerUp(PowerUpType.Shield);

        game.EntitiesForTests.Bars.Add(new Bar(
            x: 100,
            y: height - 40,
            width: GameConfig.Bars.Width,
            height: 80,
            type: BarType.Normal,
            isSpecial: false,
            initialHeight: 80,
            speedScale: GameConfig.Bars.NormalSpeedScale,
            globalBarSpeed: GameConfig.Difficulty.MinBarSpeed));

        return game;
    }
}
