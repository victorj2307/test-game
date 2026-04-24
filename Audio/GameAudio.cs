namespace Game.Audio;

/// <summary>Game-facing SFX; all tones are generated in memory (no files).</summary>
public static class GameAudio
{
    /// <summary>Plays the primary shooting one-shot.</summary>
    public static void PlayShoot() => SoundGenerator.PlayShootSound();

    /// <summary>Plays the standard bar-hit impact one-shot.</summary>
    public static void PlayHit() => SoundGenerator.PlayHitSound();

    /// <summary>Plays the game-over one-shot.</summary>
    public static void PlayGameOver() => SoundGenerator.PlayGameOverSound();

    /// <summary>Plays the life-lost one-shot.</summary>
    public static void PlayLifeLost() => SoundGenerator.PlayLifeLostSound();

    /// <summary>Plays shield pickup feedback sound.</summary>
    public static void PlayShieldPickup() => SoundGenerator.PlayShieldPickupSound();

    /// <summary>Plays shield block/absorb feedback sound.</summary>
    public static void PlayShieldBlock() => SoundGenerator.PlayShieldBlockSound();

    /// <summary>Plays piercing-shot hit feedback sound.</summary>
    public static void PlayPierceHit() => SoundGenerator.PlayPierceHitSound();
}
