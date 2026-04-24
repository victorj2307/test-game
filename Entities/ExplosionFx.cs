using System.Drawing;

namespace Game.Entities;

/// <summary>Expanding shockwave ring at bomb detonation; ticks down each frame.</summary>
public sealed class ExplosionFx
{
    public float X { get; }
    public float Y { get; }
    public int FramesLeft { get; private set; }
    public int MaxFrames { get; }
    /// <summary>Outer shockwave reaches this radius in world pixels when AgeT → 1.</summary>
    public float MaxWaveRadius { get; }

    public ExplosionFx(float x, float y, int maxFrames = 48, float maxWaveRadius = 168f)
    {
        X = x;
        Y = y;
        MaxFrames = maxFrames;
        FramesLeft = maxFrames;
        MaxWaveRadius = maxWaveRadius;
    }

    public void Tick() => FramesLeft--;

    public bool IsDead => FramesLeft <= 0;

    /// <summary>0 at start of life, approaches 1 as the effect ends.</summary>
    public float AgeT => 1f - FramesLeft / (float)MaxFrames;

    /// <summary>Smooth expansion (slow start, faster mid).</summary>
    public float ExpansionT
    {
        get
        {
            float t = AgeT;
            return t * t * (3f - 2f * t);
        }
    }
}
