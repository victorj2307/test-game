using System.Drawing;

namespace Game.Entities;

/// <summary>Rectangular debris fragment used by bomb and bullet-hit effects.</summary>
public sealed class Fragment
{
    public float X { get; private set; }
    public float Y { get; private set; }
    public float Vx { get; private set; }
    public float Vy { get; private set; }
    public float Gravity { get; }
    public float Width { get; }
    public float Height { get; }
    public int Lifetime { get; private set; }
    public int MaxLifetime { get; }
    public Color BaseColor { get; }

    /// <summary>Creates one rectangular debris fragment with gravity and finite lifetime.</summary>
    public Fragment(
        float x,
        float y,
        float vx,
        float vy,
        float width,
        float height,
        int lifetime,
        float gravity,
        Color baseColor)
    {
        X = x;
        Y = y;
        Vx = vx;
        Vy = vy;
        Width = width;
        Height = height;
        Lifetime = lifetime;
        MaxLifetime = Math.Max(1, lifetime);
        Gravity = gravity;
        BaseColor = baseColor;
    }

    /// <summary>Advances fragment physics by one frame.</summary>
    public void Update()
    {
        if (Lifetime <= 0) return;
        Vy += Gravity;
        X += Vx;
        Y += Vy;
        Lifetime--;
    }

    /// <summary>True when lifetime is depleted.</summary>
    public bool IsDead => Lifetime <= 0;
    /// <summary>Normalized remaining lifetime in [0..1], used for fade rendering.</summary>
    public float LifeT => Lifetime / (float)MaxLifetime;
}
