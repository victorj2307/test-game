namespace Game.Audio;

/// <summary>Game-facing SFX; all tones are generated in memory (no files).</summary>
public static class GameAudio
{
    public static void PlayShoot() => SoundGenerator.PlayShootSound();

    public static void PlayHit() => SoundGenerator.PlayHitSound();

    public static void PlayGameOver() => SoundGenerator.PlayGameOverSound();

    public static void PlayShieldPickup() => SoundGenerator.PlayShieldPickupSound();

    public static void PlayShieldBlock() => SoundGenerator.PlayShieldBlockSound();

    public static void PlayPierceHit() => SoundGenerator.PlayPierceHitSound();
}
