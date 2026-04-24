namespace RetroArcade;

/// <summary>Game-facing SFX; all tones are generated in memory (no files).</summary>
public static class GameAudio
{
    public static void PlayShoot() => SoundGenerator.PlayShootSound();

    public static void PlayHit() => SoundGenerator.PlayHitSound();

    public static void PlayGameOver() => SoundGenerator.PlayGameOverSound();
}
