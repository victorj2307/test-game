using System.Drawing;

namespace Game.Entities;

/// <summary>
/// Falling collectible that grants a temporary or instant gameplay effect on pickup.
/// </summary>
public sealed class PowerUp
{
    public int X { get; private set; }
    public int Y { get; private set; }
    public int Size { get; }
    public int FallSpeed { get; }
    public PowerUpType Type { get; }

    /// <summary>Creates a power-up drop at world position with fixed fall speed.</summary>
    public PowerUp(int x, int y, int size, int fallSpeed, PowerUpType type)
    {
        X = x;
        Y = y;
        Size = size;
        FallSpeed = Math.Max(1, fallSpeed);
        Type = type;
    }

    /// <summary>Moves the power-up downward one frame.</summary>
    public void Update() => Y += FallSpeed;

    /// <summary>Axis-aligned bounds used for pickup intersection.</summary>
    public Rectangle GetBounds() => new(X, Y, Size, Size);
}
