using System.Drawing;

namespace Game.Entities;

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
    public int Speed => Math.Max(1, (int)MathF.Round(_currentSpeed));

    public BarType Type { get; }
    public int InitialHeight { get; }
    public int HitFlashTimer { get; private set; }

    /// <summary>When true, hit flash uses magenta tint (piercing shot).</summary>
    public bool PierceFlash { get; private set; }

    private readonly float _speedScale;
    private float _currentSpeed;
    private float _targetSpeed;

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

    public void SetMoveSpeed(int globalSpeed)
    {
        float t = TargetPixelsFromGlobal(globalSpeed);
        _currentSpeed = t;
        _targetSpeed = t;
    }

    public void SetTargetMoveSpeed(int globalSpeed) =>
        _targetSpeed = TargetPixelsFromGlobal(globalSpeed);

    private float TargetPixelsFromGlobal(int globalSpeed) =>
        Math.Max(1f, (int)(globalSpeed * _speedScale + 0.5f));

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
        if (HitFlashTimer > 0)
        {
            HitFlashTimer--;
            if (HitFlashTimer == 0) PierceFlash = false;
        }
    }

    public void RegisterHit()
    {
        HitFlashTimer = 6;
        PierceFlash = false;
    }

    public void RegisterPierceHit()
    {
        HitFlashTimer = 14;
        PierceFlash = true;
    }

    public void ApplyDamage(int amount)
    {
        Height = Math.Max(0, Height - amount);
    }

    public bool IsDestroyed => Height <= 0;

    public float HealthRatio => InitialHeight > 0 ? (float)Height / InitialHeight : 0f;

    public Rectangle GetBounds() => new(X, Y, Width, Height);
}
