using Game.Core;

namespace Game.Entities;

/// <summary>Short-lived floating "+N" score popup at a destroy site.</summary>
public sealed class ScorePopup
{
    public float X { get; private set; }
    public float Y { get; private set; }
    public int Points { get; }
    /// <summary>Prebuilt label so draw avoids per-frame string alloc.</summary>
    public string Text { get; }
    public int Lifetime { get; private set; }
    public int MaxLifetime { get; }

    public ScorePopup(float x, float y, int points, int lifetime)
    {
        X = x;
        Y = y;
        Points = points;
        Text = $"+{points}";
        Lifetime = lifetime;
        MaxLifetime = lifetime;
    }

    public void Update(float deltaSeconds)
    {
        if (Lifetime <= 0) return;
        float step = MathF.Max(0.1f, deltaSeconds * GameConfig.Ui.TargetFps);
        int timerStep = Math.Max(1, (int)MathF.Round(step));
        Y -= GameConfig.Effects.ScorePopupRiseSpeed * step;
        Lifetime = Math.Max(0, Lifetime - timerStep);
    }

    public bool IsDead => Lifetime <= 0;

    public float LifeT => MaxLifetime <= 0 ? 0f : Lifetime / (float)MaxLifetime;
}
