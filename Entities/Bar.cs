using System.Drawing;
using Game.Core;

namespace Game.Entities;

/// <summary>
/// Falling vertical bar. Shrink is height loss with fixed top; <see cref="InitialHeight"/> drives health color.
/// Fall speed eases toward a target when global difficulty changes (no instant jumps).
/// </summary>
public sealed class Bar
{
    public int X { get; }
    private float _y;
    public int Y => (int)MathF.Round(_y);
    public int Width { get; }
    public int Height { get; private set; }
    public int Speed => Math.Max(1, (int)MathF.Round(_currentSpeed));

    public BarType Type { get; }
    public bool IsSpecial { get; }
    public int InitialHeight { get; }
    public int HitFlashTimer { get; private set; }
    public int HitPulseFrames { get; private set; }

    /// <summary>When true, hit flash uses magenta tint (piercing shot).</summary>
    public bool PierceFlash { get; private set; }

    private readonly float _speedScale;
    private float _currentSpeed;
    private float _targetSpeed;

    public Bar(int x, int y, int width, int height, BarType type, bool isSpecial, int initialHeight, float speedScale, int globalBarSpeed, float slowMotionSpeedScale = 1f)
    {
        X = x;
        _y = y;
        Width = width;
        Height = height;
        Type = type;
        IsSpecial = isSpecial;
        InitialHeight = initialHeight;
        _speedScale = speedScale;
        SetMoveSpeed(globalBarSpeed, slowMotionSpeedScale);
    }

    public void SetMoveSpeed(int globalSpeed, float slowMotionSpeedScale = 1f)
    {
        float t = TargetPixelsFromGlobal(globalSpeed, slowMotionSpeedScale);
        _currentSpeed = t;
        _targetSpeed = t;
    }

    public void SetTargetMoveSpeed(int globalSpeed, float slowMotionSpeedScale = 1f) =>
        _targetSpeed = TargetPixelsFromGlobal(globalSpeed, slowMotionSpeedScale);

    private float TargetPixelsFromGlobal(int globalSpeed, float slowMotionSpeedScale)
    {
        if (slowMotionSpeedScale < 0.999f)
        {
            float raw = globalSpeed * _speedScale * slowMotionSpeedScale;
            return Math.Max(GameConfig.PowerUps.SlowMotionMinPixelsPerFrame, raw);
        }

        return Math.Max(1f, (int)(globalSpeed * _speedScale + 0.5f));
    }

    public void TickSpeedTowardTarget(float deltaSeconds, float lerpFactor = -1f)
    {
        float step = MathF.Max(0.1f, deltaSeconds * GameConfig.Ui.TargetFps);
        float d = _targetSpeed - _currentSpeed;
        float k = lerpFactor >= 0f ? lerpFactor : GameConfig.Bars.SpeedLerpFactor;
        if (MathF.Abs(d) < GameConfig.Bars.SpeedSnapEpsilon)
            _currentSpeed = _targetSpeed;
        else
            _currentSpeed += d * k * step;
    }

    public void Move(float deltaSeconds)
    {
        float step = MathF.Max(0.1f, deltaSeconds * GameConfig.Ui.TargetFps);
        _y += _currentSpeed * step;
    }

    public void TickEffect(float deltaSeconds)
    {
        int timerStep = Math.Max(1, (int)MathF.Round(MathF.Max(0.1f, deltaSeconds * GameConfig.Ui.TargetFps)));
        if (HitFlashTimer > 0)
        {
            HitFlashTimer = Math.Max(0, HitFlashTimer - timerStep);
            if (HitFlashTimer == 0) PierceFlash = false;
        }
        if (HitPulseFrames > 0) HitPulseFrames = Math.Max(0, HitPulseFrames - timerStep);
    }

    public void RegisterHit()
    {
        HitFlashTimer = GameConfig.Bars.HitFlashFrames;
        HitPulseFrames = GameConfig.Bars.HitPulseFrames;
        PierceFlash = false;
    }

    public void RegisterPierceHit()
    {
        HitFlashTimer = GameConfig.Bars.PierceHitFlashFrames;
        HitPulseFrames = GameConfig.Bars.PierceHitPulseFrames;
        PierceFlash = true;
    }

    public void ApplyDamage(int amount)
    {
        Height = Math.Max(0, Height - amount);
    }

    public bool IsDestroyed => Height <= 0;

    public float HealthRatio => InitialHeight > 0 ? (float)Height / InitialHeight : 0f;

    public Rectangle GetBounds()
    {
        int visibleTop = Math.Max(Y, 0);
        int visibleBottom = Y + Height;
        int visibleHeight = visibleBottom - visibleTop;
        if (visibleHeight <= 0) return new Rectangle(X, visibleTop, Width, 0);
        return new Rectangle(X, visibleTop, Width, visibleHeight);
    }
    public Rectangle GetFullBounds() => new(X, Y, Width, Height);
}
