using System.Drawing;

namespace RetroArcade;

/// <summary>Small debris spark; simple linear motion + light gravity, fixed lifetime.</summary>
public sealed class Particle
{
    public float X { get; private set; }
    public float Y { get; private set; }
    public float Vx { get; private set; }
    public float Vy { get; private set; }
    public int Lifetime { get; private set; }
    public Color Color { get; }

    public Particle(float x, float y, float vx, float vy, int lifetime, Color color)
    {
        X = x;
        Y = y;
        Vx = vx;
        Vy = vy;
        Lifetime = lifetime;
        Color = color;
    }

    public void Update()
    {
        if (Lifetime <= 0) return;
        // Light gravity: pixels / frame^2
        const float gravity = 0.22f;
        Vy += gravity;
        X += Vx;
        Y += Vy;
        Lifetime--;
    }

    public bool IsDead => Lifetime <= 0;
}
