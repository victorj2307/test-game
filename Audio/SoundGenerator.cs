using System.Media;
using System.Text;
using System.Diagnostics;

namespace Game.Audio;

/// <summary>
/// Builds short PCM sine tones in memory and plays them with <see cref="SoundPlayer"/>.
/// A small pool of players avoids blocking the UI thread and lets one-shots overlap.
/// </summary>
public static class SoundGenerator
{
    private const int SampleRate = 44100;
    private const int BitsPerSample = 16;
    private const int Channels = 1;
    private const int PoolSize = 12;
    private const double MasterGain = 0.35;

    private static readonly SoundPlayer[] Players = new SoundPlayer[PoolSize];
    private static readonly MemoryStream?[] HeldStreams = new MemoryStream[PoolSize];
    private static readonly Dictionary<string, byte[][]> SoundBank = new();
    private static int _nextSlot;

    static SoundGenerator()
    {
        for (int i = 0; i < PoolSize; i++)
            Players[i] = new SoundPlayer();
        WarmupBank();
    }

    /// <summary>Ensures static warmup executes before gameplay.</summary>
    public static void Warmup()
    {
        _ = Players.Length;
    }

    /// <summary>
    /// Generates and plays a short sine-wave tone with slight randomized pitch jitter.
    /// </summary>
    public static void PlayTone(int frequencyHz, int durationMs)
    {
        int jitter = Random.Shared.Next(-95, 96);
        int f = Math.Clamp(frequencyHz + jitter, 40, 16000);
        int ms = Math.Max(1, durationMs);

        int slot = Interlocked.Increment(ref _nextSlot);
        if (slot < 0) slot = -slot;
        slot %= PoolSize;

        MemoryStream wav;
        try
        {
            wav = BuildWavSine(f, ms);
        }
        catch (OutOfMemoryException ex)
        {
            Debug.WriteLine($"[SoundGenerator] Out of memory building tone: {ex}");
            return;
        }
        catch (ArgumentOutOfRangeException ex)
        {
            Debug.WriteLine($"[SoundGenerator] Invalid tone arguments: {ex}");
            return;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SoundGenerator] Unexpected error building tone: {ex}");
            return;
        }

        SoundPlayer player = Players[slot];
        try
        {
            player.Stop();
            HeldStreams[slot]?.Dispose();
            HeldStreams[slot] = wav;
            wav.Position = 0;
            player.Stream = wav;
            player.Load();
            player.Play();
        }
        catch (InvalidOperationException ex)
        {
            Debug.WriteLine($"[SoundGenerator] Invalid player state: {ex}");
            HeldStreams[slot]?.Dispose();
            HeldStreams[slot] = null;
            wav.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SoundGenerator] Unexpected playback error: {ex}");
            HeldStreams[slot]?.Dispose();
            HeldStreams[slot] = null;
            wav.Dispose();
        }
    }

    /// <summary>Fires a short bright tone for player shots.</summary>
    public static void PlayShootSound() =>
        PlayFromBank("shoot");

    /// <summary>Fires a short medium tone for standard impacts.</summary>
    public static void PlayHitSound() =>
        PlayFromBank("hit");

    /// <summary>Fires a longer low tone for game over.</summary>
    public static void PlayGameOverSound() =>
        PlayFromBank("gameover");

    /// <summary>Distinct short-low cue for losing one life (non-terminal).</summary>
    public static void PlayLifeLostSound() =>
        PlayFromBank("lifelost");

    /// <summary>Rising “power up” chirp for shield pickup.</summary>
    public static void PlayShieldPickupSound()
    {
        PlayFromBank("shield_pickup_low");
        PlayFromBank("shield_pickup_high");
    }

    /// <summary>Bright short impact for shield absorbing a hit.</summary>
    public static void PlayShieldBlockSound() =>
        PlayFromBank("shield_block");

    /// <summary>Short high “zip” when a piercing round passes through a bar.</summary>
    public static void PlayPierceHitSound() =>
        PlayFromBank("pierce");

    private static void WarmupBank()
    {
        int variants = Game.Core.GameRuntimeConfig.Current.AudioVariantCount;
        SoundBank["shoot"] = BuildVariants(1000, 1400, 45, 60, variants);
        SoundBank["hit"] = BuildVariants(600, 900, 65, 100, variants);
        SoundBank["gameover"] = BuildVariants(150, 300, 220, 400, Math.Max(2, variants / 2));
        SoundBank["lifelost"] = BuildVariants(280, 420, 140, 200, Math.Max(2, variants / 2));
        SoundBank["shield_pickup_low"] = BuildVariants(520, 560, 42, 52, Math.Max(2, variants / 2));
        SoundBank["shield_pickup_high"] = BuildVariants(840, 920, 50, 62, Math.Max(2, variants / 2));
        SoundBank["shield_block"] = BuildVariants(1100, 1400, 70, 95, variants);
        SoundBank["pierce"] = BuildVariants(1250, 1650, 38, 58, variants);
    }

    private static byte[][] BuildVariants(int minFreq, int maxFreq, int minMs, int maxMs, int count)
    {
        count = Math.Clamp(count, 1, 64);
        var variants = new byte[count][];
        for (int i = 0; i < count; i++)
        {
            int f = Random.Shared.Next(minFreq, maxFreq + 1);
            int ms = Random.Shared.Next(minMs, maxMs + 1);
            using MemoryStream wav = BuildWavSine(f, ms);
            variants[i] = wav.ToArray();
        }

        return variants;
    }

    private static void PlayFromBank(string key)
    {
        if (!SoundBank.TryGetValue(key, out byte[][]? variants) || variants.Length == 0)
            return;
        byte[] selected = variants[Random.Shared.Next(variants.Length)];
        PlayBytes(selected);
    }

    private static void PlayBytes(byte[] bytes)
    {
        int slot = Interlocked.Increment(ref _nextSlot);
        if (slot < 0) slot = -slot;
        slot %= PoolSize;

        var wav = new MemoryStream(bytes, writable: false);
        SoundPlayer player = Players[slot];
        try
        {
            player.Stop();
            HeldStreams[slot]?.Dispose();
            HeldStreams[slot] = wav;
            wav.Position = 0;
            player.Stream = wav;
            player.Load();
            player.Play();
        }
        catch (InvalidOperationException ex)
        {
            Debug.WriteLine($"[SoundGenerator] Invalid player state: {ex}");
            HeldStreams[slot]?.Dispose();
            HeldStreams[slot] = null;
            wav.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SoundGenerator] Unexpected playback error: {ex}");
            HeldStreams[slot]?.Dispose();
            HeldStreams[slot] = null;
            wav.Dispose();
        }
    }

    /// <summary>
    /// Builds a complete mono PCM WAV stream for the requested sine tone.
    /// </summary>
    private static MemoryStream BuildWavSine(int frequencyHz, int durationMs)
    {
        int sampleCount = SampleRate * durationMs / 1000;
        if (sampleCount < 1) sampleCount = 1;

        int dataBytes = sampleCount * Channels * (BitsPerSample / 8);
        int riffChunkSize = 36 + dataBytes;

        var ms = new MemoryStream(44 + dataBytes);
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            bw.Write(Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(riffChunkSize);
            bw.Write(Encoding.ASCII.GetBytes("WAVE"));

            bw.Write(Encoding.ASCII.GetBytes("fmt "));
            bw.Write(16);
            bw.Write((short)1);
            bw.Write((short)Channels);
            bw.Write(SampleRate);
            bw.Write(SampleRate * Channels * BitsPerSample / 8);
            bw.Write((short)(Channels * BitsPerSample / 8));
            bw.Write((short)BitsPerSample);

            bw.Write(Encoding.ASCII.GetBytes("data"));
            bw.Write(dataBytes);

            for (int i = 0; i < sampleCount; i++)
            {
                double env = CosineEnvelope(i, sampleCount);
                double t = i / (double)SampleRate;
                double s = Math.Sin(2 * Math.PI * frequencyHz * t) * env * MasterGain;
                short q = (short)Math.Clamp(s * short.MaxValue, short.MinValue, short.MaxValue);
                bw.Write(q);
            }
        }

        ms.Position = 0;
        return ms;
    }

    /// <summary>
    /// Smooth attack/release envelope to reduce clicks at waveform start/end.
    /// </summary>
    private static double CosineEnvelope(int i, int n)
    {
        int attack = Math.Max(4, n / 10);
        int release = Math.Max(4, n / 8);
        if (attack + release > n)
        {
            attack = Math.Max(1, n / 2);
            release = n - attack;
        }

        double a = 1.0;
        if (attack > 0 && i < attack)
            a = 0.5 * (1.0 - Math.Cos(Math.PI * i / attack));

        double r = 1.0;
        if (release > 0 && i > n - 1 - release)
        {
            int j = n - 1 - i;
            r = 0.5 * (1.0 - Math.Cos(Math.PI * j / release));
        }

        return a * r;
    }
}
