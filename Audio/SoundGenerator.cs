using System.Media;
using System.Text;
using System.Diagnostics;

namespace Game.Audio;

/// <summary>
/// Builds short PCM sine tones in memory and plays them with <see cref="SoundPlayer"/>.
/// A small pool of players avoids blocking the UI thread and lets one-shots overlap.
/// Clip banks are built at type initialization; lazy <see cref="PlayTone"/> banks use <c>ClipBankLock</c> so dictionary
/// writes and bank reads stay consistent if playback is ever invoked off the UI thread.
/// </summary>
public static class SoundGenerator
{
    private const int SampleRate = 44100;
    private const int BitsPerSample = 16;
    private const int Channels = 1;
    private const int PoolSize = 12;
    private const double MasterGain = 0.35;

    /// <summary>One reusable player+stream pair bound to a clip instance.</summary>
    private sealed class CachedPlaybackSlot
    {
        public required SoundPlayer Player { get; init; }
        public required MemoryStream Stream { get; init; }
    }

    /// <summary>Round-robin playback slots for one logical sound key.</summary>
    private sealed class CachedClipBank
    {
        public required CachedPlaybackSlot[] Slots { get; init; }
        public int NextIndex;
    }

    private static readonly Dictionary<string, byte[][]> SoundBank = new();
    private static readonly Dictionary<string, CachedClipBank> ClipBanks = new(StringComparer.Ordinal);
    private static readonly object ClipBankLock = new();

    static SoundGenerator()
    {
        WarmupBank();
        BuildClipBanks();
    }

    /// <summary>Ensures static warmup executes before gameplay.</summary>
    public static void Warmup()
    {
        // Access forces static constructor completion and preload side effects.
        _ = ClipBanks.Count;
    }

    /// <summary>
    /// Generates and plays a short sine-wave tone with slight randomized pitch jitter.
    /// </summary>
    public static void PlayTone(int frequencyHz, int durationMs)
    {
        int jitter = Random.Shared.Next(-95, 96);
        int f = Math.Clamp(frequencyHz + jitter, 40, 16000);
        int ms = Math.Max(1, durationMs);
        string key = $"tone:{f}:{ms}";
        lock (ClipBankLock)
        {
            if (!ClipBanks.ContainsKey(key))
                BuildDynamicToneBank(key, f, ms);
        }
        PlayFromBank(key);
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

    /// <summary>Builds raw PCM variant arrays for each gameplay SFX key.</summary>
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

    /// <summary>Converts raw PCM variants into preloaded replayable banks.</summary>
    private static void BuildClipBanks()
    {
        lock (ClipBankLock)
        {
        foreach (var (key, variants) in SoundBank)
            ClipBanks[key] = BuildCachedClipBank(variants);
        }
    }

    /// <summary>Creates and preloads round-robin players for one clip variant set.</summary>
    private static CachedClipBank BuildCachedClipBank(byte[][] variants)
    {
        if (variants.Length == 0)
            return new CachedClipBank { Slots = [] };

        int slotCount = Math.Max(2, Math.Min(PoolSize, variants.Length * 2));
        var slots = new CachedPlaybackSlot[slotCount];
        for (int i = 0; i < slotCount; i++)
        {
            byte[] sample = variants[i % variants.Length];
            var stream = new MemoryStream(sample, writable: false);
            var player = new SoundPlayer(stream);
            try
            {
                player.Load();
            }
            catch (InvalidOperationException ex)
            {
                Debug.WriteLine($"[SoundGenerator] Invalid player state during preload: {ex}");
            }
            catch (TimeoutException ex)
            {
                Debug.WriteLine($"[SoundGenerator] Timeout preloading clip: {ex}");
            }

            slots[i] = new CachedPlaybackSlot
            {
                Player = player,
                Stream = stream
            };
        }

        return new CachedClipBank { Slots = slots };
    }

    /// <summary>Lazy-builds a one-key tone bank for ad-hoc <see cref="PlayTone"/> calls.</summary>
    private static void BuildDynamicToneBank(string key, int frequencyHz, int durationMs)
    {
        try
        {
            using MemoryStream wav = BuildWavSine(frequencyHz, durationMs);
            ClipBanks[key] = BuildCachedClipBank([wav.ToArray()]);
        }
        catch (OutOfMemoryException ex)
        {
            Debug.WriteLine($"[SoundGenerator] Out of memory building tone bank: {ex}");
        }
        catch (ArgumentOutOfRangeException ex)
        {
            Debug.WriteLine($"[SoundGenerator] Invalid tone arguments: {ex}");
        }
    }

    /// <summary>Builds randomized PCM variants to reduce repetitive timbre.</summary>
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

    /// <summary>Plays one preloaded clip bank slot without reloading audio metadata.</summary>
    private static void PlayFromBank(string key)
    {
        CachedClipBank? bank;
        lock (ClipBankLock)
        {
            if (!ClipBanks.TryGetValue(key, out bank) || bank.Slots.Length == 0)
                return;
        }
        // Round-robin keeps rapid one-shots from cutting each other off immediately.
        int slotIndex = Interlocked.Increment(ref bank.NextIndex);
        if (slotIndex < 0) slotIndex = -slotIndex;
        CachedPlaybackSlot slot = bank.Slots[slotIndex % bank.Slots.Length];
        try
        {
            slot.Player.Stop();
            slot.Stream.Position = 0;
            slot.Player.Play();
        }
        catch (InvalidOperationException ex)
        {
            Debug.WriteLine($"[SoundGenerator] Invalid player state during playback: {ex}");
        }
        catch (TimeoutException ex)
        {
            Debug.WriteLine($"[SoundGenerator] Playback timeout: {ex}");
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
