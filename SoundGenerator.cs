using System.Media;
using System.Text;

namespace RetroArcade;

/// <summary>
/// Builds short PCM sine tones in memory and plays them with <see cref="SoundPlayer"/> (async <see cref="SoundPlayer.Play"/>).
/// A small pool of players avoids blocking the UI thread and lets one-shots overlap.
/// </summary>
public static class SoundGenerator
{
    private const int SampleRate = 44100;
    private const int BitsPerSample = 16;
    private const int Channels = 1;
    private const int PoolSize = 12;

    /// <summary>Amplitude scale (0..1) before clipping guard; keep below 1 to reduce harshness.</summary>
    private const double MasterGain = 0.35;

    private static readonly SoundPlayer[] Players = new SoundPlayer[PoolSize];
    /// <summary>Holds each stream alive while SoundPlayer reads it asynchronously.</summary>
    private static readonly MemoryStream?[] HeldStreams = new MemoryStream[PoolSize];
    private static int _nextSlot;

    static SoundGenerator()
    {
        for (int i = 0; i < PoolSize; i++)
            Players[i] = new SoundPlayer();
    }

    /// <summary>
    /// Plays a mono 16-bit PCM sine at 44.1 kHz. Frequency is jittered by ±50–100 Hz for variety.
    /// Uses <see cref="SoundPlayer.Play"/> (non-blocking). Overlap uses round-robin pool slots.
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
        catch
        {
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
        catch
        {
            HeldStreams[slot]?.Dispose();
            HeldStreams[slot] = null;
            wav.Dispose();
        }
    }

    public static void PlayShootSound() =>
        PlayTone(Random.Shared.Next(1000, 1401), Random.Shared.Next(45, 61));

    public static void PlayHitSound() =>
        PlayTone(Random.Shared.Next(600, 901), Random.Shared.Next(65, 101));

    public static void PlayGameOverSound() =>
        PlayTone(Random.Shared.Next(150, 301), Random.Shared.Next(220, 401));

    /// <summary>
    /// Writes a valid in-memory WAV: RIFF → WAVE → fmt (PCM) → data (16-bit mono samples).
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
            // --- RIFF container ---
            bw.Write(Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(riffChunkSize);
            bw.Write(Encoding.ASCII.GetBytes("WAVE"));

            // --- fmt chunk (PCM) ---
            bw.Write(Encoding.ASCII.GetBytes("fmt "));
            bw.Write(16);
            bw.Write((short)1);
            bw.Write((short)Channels);
            bw.Write(SampleRate);
            bw.Write(SampleRate * Channels * BitsPerSample / 8);
            bw.Write((short)(Channels * BitsPerSample / 8));
            bw.Write((short)BitsPerSample);

            // --- data chunk (raw PCM) ---
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

    /// <summary>Raised-cosine attack × release so very short tones still avoid clicks.</summary>
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

