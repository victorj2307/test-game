namespace RetroArcade;

/// <summary>
/// Straight upward shot. Top-left (X, Y) with Y growing downward in WinForms; many may be active.
/// </summary>
public sealed class Bullet
{
    public int X { get; private set; }
    public int Y { get; private set; }
    public int Width { get; }
    public int Height { get; }
    public int Speed { get; }

    public bool IsActive { get; private set; } = true;

    public Bullet(int x, int y, int width, int height, int speed)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Speed = speed;
    }

    public void Deactivate() => IsActive = false;

    public void Update()
    {
        if (!IsActive) return;
        Y -= Speed; // up = decrease Y
    }

    public Rectangle GetBounds() => new(X, Y, Width, Height);
}
