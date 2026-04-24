using System.Drawing;

namespace Game.Entities;

/// <summary>
/// Cannon with discrete horizontal steps (half bar width), snapped to a grid for alignment with bars.
/// </summary>
public sealed class Player
{
    private int _x;
    public int Y { get; private set; }

    public int Width { get; }
    public int Height { get; }

    public int MuzzlePortWidth { get; } = 8;

    public Player(int x, int y, int width, int height)
    {
        _x = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public int X => _x;

    public Point MuzzleTopCenter => new(X + Width / 2, Y);

    public void GetBulletSpawn(int bulletW, int bulletH, out int spawnX, out int spawnY)
    {
        spawnX = X + (Width - bulletW) / 2;
        spawnY = Y - bulletH;
    }

    public Rectangle GetMuzzlePortRect()
    {
        int w = MuzzlePortWidth;
        w = w > Width ? Width - 1 : w;
        if (w < 1) w = 1;
        return new Rectangle(X + (Width - w) / 2, Y - 2, w, 2);
    }

    public void ClampAndSnapToGrid(int clientWidth, int stepPixels)
    {
        if (stepPixels < 1) stepPixels = 1;
        int maxX = Math.Max(0, clientWidth - Width);
        _x = Math.Clamp(_x, 0, maxX);
        int gridMax = maxX / stepPixels * stepPixels;
        _x = Math.Min(_x / stepPixels * stepPixels, gridMax);
    }

    public void TryStep(int direction, int clientWidth, int stepPixels)
    {
        if (direction is not (-1) and not 1) return;
        if (stepPixels < 1) stepPixels = 1;
        int maxX = Math.Max(0, clientWidth - Width);
        _x = Math.Clamp(_x + direction * stepPixels, 0, maxX);
        ClampAndSnapToGrid(clientWidth, stepPixels);
    }

    public void SetBottom(int playHeight) => Y = playHeight - Height;

    public Rectangle GetBounds() => new(X, Y, Width, Height);
}
