using Game.Core;

namespace RetroArcade.Tests;

[TestClass]
public sealed class HighScoreStoreTests
{
    private string _testRoot = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "RetroArcadeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
        HighScoreStore.StorageRootOverride = _testRoot;
    }

    [TestCleanup]
    public void Cleanup()
    {
        HighScoreStore.StorageRootOverride = null;
        if (Directory.Exists(_testRoot))
            Directory.Delete(_testRoot, recursive: true);
    }

    [TestMethod]
    public void TryAddScore_OnlyReplacesWhenBetterScore()
    {
        bool addedFirst = HighScoreStore.TryAddScore("Alice", 100, 10);
        bool addedLower = HighScoreStore.TryAddScore("Alice", 80, 10);
        var leaderboard = HighScoreStore.LoadLeaderboard(10);

        Assert.IsTrue(addedFirst);
        Assert.IsFalse(addedLower);
        Assert.AreEqual(1, leaderboard.Count);
        Assert.AreEqual(100, leaderboard[0].Score);
    }
}
