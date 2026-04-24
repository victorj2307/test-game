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

    /// <summary>Creates a timed explosion ring effect at a world position.</summary>
    public ExplosionFx(float x, float y, int maxFrames = 48, float maxWaveRadius = 168f)
    {
        X = x;
        Y = y;
        MaxFrames = maxFrames;
        FramesLeft = maxFrames;
        MaxWaveRadius = maxWaveRadius;
    }

    /// <summary>Advances the effect by one frame.</summary>
    public void Tick() => FramesLeft--;

    /// <summary>True when no frames remain.</summary>
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
