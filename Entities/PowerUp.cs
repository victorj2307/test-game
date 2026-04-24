using System.Drawing;

namespace Game.Entities;

public sealed class PowerUp
{
    public int X { get; private set; }
    public int Y { get; private set; }
    public int Size { get; }
    public int FallSpeed { get; }
    public PowerUpType Type { get; }

    public PowerUp(int x, int y, int size, int fallSpeed, PowerUpType type)
    {
        X = x;
        Y = y;
        Size = size;
        FallSpeed = Math.Max(1, fallSpeed);
        Type = type;
    }

    public void Update() => Y += FallSpeed;

    public Rectangle GetBounds() => new(X, Y, Size, Size);
}
