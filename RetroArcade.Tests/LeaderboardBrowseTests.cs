using Game.Core;

namespace RetroArcade.Tests;

[TestClass]
public sealed class LeaderboardBrowseTests
{
    [TestMethod]
    public void TryOpenLeaderboardBrowse_OnlyWorksInAttract()
    {
        var game = new GameManager();
        game.Initialize(480, 600);
        game.EnterAttractMode(480, 600);

        Assert.IsTrue(game.IsAttractMode);
        Assert.IsTrue(game.TryOpenLeaderboardBrowse());
        Assert.IsTrue(game.IsBrowsingLeaderboard);

        game.CloseLeaderboardBrowse();
        Assert.IsFalse(game.IsBrowsingLeaderboard);

        game.StartNewGame(480, 600);
        Assert.IsFalse(game.TryOpenLeaderboardBrowse());
        Assert.IsFalse(game.IsBrowsingLeaderboard);
    }

    [TestMethod]
    public void StartNewGame_ClosesLeaderboardBrowse()
    {
        var game = new GameManager();
        game.Initialize(480, 600);
        game.EnterAttractMode(480, 600);
        Assert.IsTrue(game.TryOpenLeaderboardBrowse());

        game.StartNewGame(480, 600);

        Assert.IsFalse(game.IsBrowsingLeaderboard);
        Assert.IsTrue(game.IsPlaying);
    }

    [TestMethod]
    public void EnterAttractMode_FromGameOver_ReturnsToTitle()
    {
        var game = new GameManager();
        game.Initialize(480, 600);
        game.StartNewGame(480, 600);
        game.StateForTests.SetGameOver();

        Assert.IsTrue(game.IsGameOver);

        game.EnterAttractMode(480, 600);

        Assert.IsFalse(game.IsGameOver);
        Assert.IsTrue(game.IsAttractMode);
        Assert.IsFalse(game.IsBrowsingLeaderboard);
    }
}
