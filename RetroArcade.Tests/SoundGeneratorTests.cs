using Game.Audio;

namespace RetroArcade.Tests;

[TestClass]
public sealed class SoundGeneratorTests
{
    [TestMethod]
    public void PlayHitSound_RapidCalls_DoNotThrow()
    {
        SoundGenerator.Warmup();
        for (int i = 0; i < 40; i++)
            GameAudio.PlayHit();
    }

    [TestMethod]
    public void PlayShootAndPierce_RapidCalls_DoNotThrow()
    {
        SoundGenerator.Warmup();
        for (int i = 0; i < 40; i++)
        {
            GameAudio.PlayShoot();
            GameAudio.PlayPierceHit();
        }
    }

    [TestMethod]
    public void PlayBomb_DoesNotThrow()
    {
        SoundGenerator.Warmup();
        GameAudio.PlayBomb();
        GameAudio.PlayBomb();
    }
}
