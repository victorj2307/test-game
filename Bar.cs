using System.Drawing;

namespace RetroArcade;

/// <summary>
/// Falling vertical bar. Shrink is height loss with fixed top; <see cref="InitialHeight"/> drives health color.
/// Fall speed eases toward a target when global difficulty changes (no instant jumps).
/// </summary>
public sealed class Bar
{
    public int X { get; }
    public int Y { get; private set; }
    public int Width { get; }
    public int Height { get; private set; }
    /// <summary>Rounded current fall speed in px/frame (for debug / consistency).</summary>
    public int Speed => Math.Max(1, (int)MathF.Round(_currentSpeed));

    public BarType Type { get; }
    public int InitialHeight { get; }
    public int HitFlashTimer { get; private set; }

    private readonly float _speedScale;
    private float _currentSpeed;
    private float _targetSpeed;

    /// <summary>How fast <see cref="_currentSpeed"/> approaches <see cref="_targetSpeed"/> each frame (0–1).</summary>
    private const float SpeedLerpFactor = 0.14f;

    public Bar(int x, int y, int width, int height, BarType type, int initialHeight, float speedScale, int globalBarSpeed)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Type = type;
        InitialHeight = initialHeight;
        _speedScale = speedScale;
        SetMoveSpeed(globalBarSpeed);
    }

    /// <summary>Snap current and target to the same pixel speed (e.g. new spawn).</summary>
    public void SetMoveSpeed(int globalSpeed)
    {
        float t = TargetPixelsFromGlobal(globalSpeed);
        _currentSpeed = t;
        _targetSpeed = t;
    }

    /// <summary>Only updates the interpolation target (when global difficulty rises/falls).</summary>
    public void SetTargetMoveSpeed(int globalSpeed) =>
        _targetSpeed = TargetPixelsFromGlobal(globalSpeed);

    private float TargetPixelsFromGlobal(int globalSpeed) =>
        Math.Max(1f, (int)(globalSpeed * _speedScale + 0.5f));

    /// <summary>Call once per frame before <see cref="Move"/>.</summary>
    public void TickSpeedTowardTarget()
    {
        float d = _targetSpeed - _currentSpeed;
        if (MathF.Abs(d) < 0.02f)
            _currentSpeed = _targetSpeed;
        else
            _currentSpeed += d * SpeedLerpFactor;
    }

    public void Move() => Y += Math.Max(1, (int)MathF.Round(_currentSpeed));

    public void TickEffect()
    {
        if (HitFlashTimer > 0) HitFlashTimer--;
    }

    public void RegisterHit()
    {
        HitFlashTimer = 6;
    }

    public void ApplyDamage(int amount)
    {
        Height = Math.Max(0, Height - amount);
    }

    public bool IsDestroyed => Height <= 0;

    public float HealthRatio => InitialHeight > 0 ? (float)Height / InitialHeight : 0f;

    public Rectangle GetBounds() => new(X, Y, Width, Height);
}
