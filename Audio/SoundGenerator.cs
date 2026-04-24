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
    private static int _nextSlot;

    static SoundGenerator()
    {
        for (int i = 0; i < PoolSize; i++)
            Players[i] = new SoundPlayer();
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
        PlayTone(Random.Shared.Next(1000, 1401), Random.Shared.Next(45, 61));

    /// <summary>Fires a short medium tone for standard impacts.</summary>
    public static void PlayHitSound() =>
        PlayTone(Random.Shared.Next(600, 901), Random.Shared.Next(65, 101));

    /// <summary>Fires a longer low tone for game over.</summary>
    public static void PlayGameOverSound() =>
        PlayTone(Random.Shared.Next(150, 301), Random.Shared.Next(220, 401));

    /// <summary>Distinct short-low cue for losing one life (non-terminal).</summary>
    public static void PlayLifeLostSound() =>
        PlayTone(Random.Shared.Next(280, 421), Random.Shared.Next(140, 201));

    /// <summary>Rising “power up” chirp for shield pickup.</summary>
    public static void PlayShieldPickupSound()
    {
        PlayTone(520, 45);
        PlayTone(880, 55);
    }

    /// <summary>Bright short impact for shield absorbing a hit.</summary>
    public static void PlayShieldBlockSound() =>
        PlayTone(Random.Shared.Next(1100, 1401), Random.Shared.Next(70, 95));

    /// <summary>Short high “zip” when a piercing round passes through a bar.</summary>
    public static void PlayPierceHitSound() =>
        PlayTone(Random.Shared.Next(1250, 1651), Random.Shared.Next(38, 58));

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
