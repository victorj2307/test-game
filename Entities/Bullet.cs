using System.Drawing;
using Game.Core;

namespace Game.Entities;

/// <summary>
/// Straight upward shot. Top-left (X, Y) with Y growing downward in WinForms; many may be active.
/// </summary>
public sealed class Bullet
{
    private float _x;
    private float _y;
    public int X => (int)MathF.Round(_x);
    public int Y => (int)MathF.Round(_y);
    public int Width { get; }
    public int Height { get; }
    public int Speed { get; }
    public float DriftX { get; }
    public int RemainingPierces { get; private set; }

    /// <summary>True if spawned during Piercing Shot (wider purple shot, distinct SFX).</summary>
    public bool IsPiercingVisual { get; }

    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Creates a bullet with optional lateral drift and optional pierce budget.
    /// </summary>
    public Bullet(int x, int y, int width, int height, int speed, float driftX = 0f, int pierceCount = 0)
    {
        _x = x;
        _y = y;
        Width = width;
        Height = height;
        Speed = speed;
        DriftX = driftX;
        RemainingPierces = Math.Max(0, pierceCount);
        IsPiercingVisual = pierceCount > 0;
    }

    /// <summary>Marks the bullet inactive so update/collision can skip it.</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>Advances the bullet one simulation step using a 60 FPS baseline.</summary>
    public void Update(float deltaSeconds)
    {
        if (!IsActive) return;
        float step = MathF.Max(0.1f, deltaSeconds * GameConfig.Ui.TargetFps);
        _x += DriftX * step;
        _y -= Speed * step;
    }

    /// <summary>
    /// Consumes one pierce charge.
    /// Returns true when the bullet should continue through the current target.
    /// </summary>
    public bool ConsumePierce()
    {
        if (RemainingPierces <= 0) return false;
        RemainingPierces--;
        return true;
    }

    /// <summary>Axis-aligned bounds used for collision checks.</summary>
    public Rectangle GetBounds() => new(X, Y, Width, Height);
}
