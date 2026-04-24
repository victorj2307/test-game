using System.Drawing;
using Game.Core;

namespace Game.Entities;

/// <summary>Small debris spark; simple linear motion + light gravity, fixed lifetime.</summary>
public sealed class Particle
{
    public float X { get; private set; }
    public float Y { get; private set; }
    public float Vx { get; private set; }
    public float Vy { get; private set; }
    public int Lifetime { get; private set; }
    public Color Color { get; }

    /// <summary>Constructs a short-lived spark particle.</summary>
    public Particle(float x, float y, float vx, float vy, int lifetime, Color color)
    {
        X = x;
        Y = y;
        Vx = vx;
        Vy = vy;
        Lifetime = lifetime;
        Color = color;
    }

    /// <summary>Advances particle motion and decrements lifetime with delta scaling.</summary>
    public void Update(float deltaSeconds)
    {
        if (Lifetime <= 0) return;
        float step = MathF.Max(0.1f, deltaSeconds * GameConfig.Ui.TargetFps);
        int timerStep = Math.Max(1, (int)MathF.Round(step));
        const float gravity = 0.22f;
        Vy += gravity * step;
        X += Vx * step;
        Y += Vy * step;
        Lifetime = Math.Max(0, Lifetime - timerStep);
    }

    /// <summary>True once lifetime reaches zero.</summary>
    public bool IsDead => Lifetime <= 0;
}
