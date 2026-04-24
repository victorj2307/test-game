using System.Drawing;
using System.Drawing.Drawing2D;
using Game.Core;
using Game.Entities;

namespace Game.Rendering;

/// <summary>All GDI+ drawing for the playfield: world, HUD, overlays, debug.</summary>
public sealed class RenderSystem
{
    private static readonly Brush PlayerBrush = new SolidBrush(Color.FromArgb(40, 220, 255));
    private static readonly Brush MuzzlePortBrush = new SolidBrush(Color.FromArgb(255, 230, 120));
    private static readonly Brush BulletBrushStatic = new SolidBrush(Color.FromArgb(255, 250, 200));
    private static readonly Brush PierceBulletCore = new SolidBrush(Color.FromArgb(255, 255, 210, 255));
    private static readonly Brush PierceBulletGlow = new SolidBrush(Color.FromArgb(120, 200, 60, 255));
    private static readonly Brush TextBrush = Brushes.White;
    private static readonly Brush SubtleTextBrush = new SolidBrush(Color.Silver);
    private static readonly Brush GoBrush = new SolidBrush(Color.OrangeRed);
    private static readonly Pen DebugPenPlayer = new(Color.Lime) { Width = 1.5f };
    private static readonly Pen DebugPenEnemy = new(Color.Magenta) { Width = 1f };
    private static readonly Pen DebugPenBullet = new(Color.Cyan) { Width = 1f };

    private static readonly float[] BombShakeOx =
    {
        2.2f, -2.5f, 1.8f, -1.6f, 2.4f, -2f, 0f, 1.5f, -2.2f, 1.2f
    };

    private static readonly float[] BombShakeOy =
    {
        -1.8f, 2.1f, 1.5f, -2.3f, 1.2f, 2f, -2f, 0.8f, -1.4f, 2.2f
    };

    public void Draw(
        Graphics g,
        int clientWidth,
        int playHeight,
        Font uiFont,
        GameState state,
        EntityManager entities,
        Player player,
        bool showDebug,
        int debugFps)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.CompositingQuality = CompositingQuality.HighSpeed;

        long now = Environment.TickCount64;
        float ox = 0, oy = 0;
        if (state.BombHeavyShakeFrames > 0)
        {
            int ph = state.ElapsedFrames % BombShakeOx.Length;
            ox = BombShakeOx[ph];
            oy = BombShakeOy[ph];
        }
        else if (state.ShieldImpactShakeFrames > 0)
        {
            int ph = state.ElapsedFrames % 6;
            ox = (ph % 3 - 1) * 1.6f;
            oy = ph switch { 0 or 3 => 1.2f, 1 or 4 => -1.2f, _ => 0.6f };
        }
        else if (now < state.ShakeUntilTickMs)
        {
            int phase = (int)(now / 20 % 4);
            ox = phase switch { 0 => 1f, 2 => -1f, _ => 0f };
            oy = phase switch { 1 => 1f, 3 => -1f, _ => 0f };
        }
        var graphicsState = g.Save();
        g.TranslateTransform(ox, oy);

        DrawBackground(g, clientWidth, playHeight);

        int dangerY = Math.Max(40, playHeight - player.Height - 28);
        bool barPastDanger = false;
        foreach (var bar in entities.Bars)
        {
            if (bar.Height > 0 && bar.Y + bar.Height >= dangerY) { barPastDanger = true; break; }
        }
        using (var dangerPen = new Pen(barPastDanger ? Color.FromArgb(180, 255, 120, 60) : Color.FromArgb(70, 80, 110, 130), barPastDanger ? 2f : 1f))
        {
            g.DrawLine(dangerPen, 0, dangerY, clientWidth, dangerY);
        }

        using var barOutline = new Pen(Color.FromArgb(110, 0, 0, 0), 1f);
        foreach (var bar in entities.Bars)
        {
            if (bar.Height <= 0) continue;
            var r = bar.GetBounds();
            using (var br = new SolidBrush(GetBarDrawColor(bar)))
                g.FillRectangle(br, r);
            g.DrawRectangle(barOutline, r.X, r.Y, r.Width - 1, r.Height - 1);
        }

        foreach (var p in entities.Particles)
        {
            using var br = new SolidBrush(p.Color);
            g.FillRectangle(br, p.X, p.Y, 2, 2);
        }

        DrawPowerUps(g, entities.PowerUps);

        if (state.HasPendingBombPickup)
            DrawBombFuseIndicator(g, state);

        DrawExplosionRings(g, entities.Explosions);

        foreach (var bullet in entities.Bullets)
        {
            int cx = bullet.X + bullet.Width / 2;
            int bottom = bullet.Y + bullet.Height;
            if (bullet.IsPiercingVisual)
            {
                using var trailP = new Pen(Color.FromArgb(95, 220, 80, 255), 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLine(trailP, cx, bottom + 14, cx, bottom);
                using var trailInner = new Pen(Color.FromArgb(55, 255, 200, 255), 1.1f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLine(trailInner, cx, bottom + 12, cx, bottom + 2);
            }
            else
            {
                using var trailPen = new Pen(Color.FromArgb(55, 180, 230, 255), 1.5f);
                g.DrawLine(trailPen, cx, bottom + 12, cx, bottom);
            }
        }
        foreach (var bullet in entities.Bullets)
        {
            Rectangle r = bullet.GetBounds();
            if (bullet.IsPiercingVisual)
            {
                g.FillRectangle(PierceBulletGlow, r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2);
                g.FillRectangle(PierceBulletCore, r);
            }
            else
            {
                g.FillRectangle(BulletBrushStatic, r);
            }
        }

        g.FillRectangle(PlayerBrush, player.GetBounds());
        Rectangle muzzle = player.GetMuzzlePortRect();
        g.FillRectangle(MuzzlePortBrush, muzzle);
        int muzzleX = player.MuzzleTopCenter.X;
        using (var aim = new Pen(Color.FromArgb(255, 200, 140, 60), 2.25f))
        {
            aim.StartCap = LineCap.Round;
            aim.EndCap = LineCap.Round;
            g.DrawLine(aim, muzzleX, muzzle.Top, muzzleX, muzzle.Top - 7);
        }
        if (state.MuzzleFlashFrames > 0)
        {
            using var flash = new SolidBrush(Color.FromArgb(220, 255, 255, 255));
            g.FillEllipse(flash, muzzleX - 10, muzzle.Top - 14, 20, 16);
        }

        DrawShieldPlayerFx(g, player, state);

        DrawHud(g, uiFont, clientWidth, state);
        DrawNewBestBanner(g, uiFont, clientWidth, state);
        if (state.IsDevMode && state.IsPlaying && !state.IsGameOver)
            DrawDevModeOverlay(g, uiFont, clientWidth, playHeight, state);

        DrawBombScreenFlash(g, clientWidth, playHeight, state);

        if (state.LifeLostFlashFrames > 0)
        {
            int flashAlpha = Math.Min(170, state.LifeLostFlashFrames * 9);
            using var flashOverlay = new SolidBrush(Color.FromArgb(flashAlpha, 210, 30, 40));
            g.FillRectangle(flashOverlay, 0, 0, clientWidth, playHeight);
        }

        if (state.IsGameOver)
        {
            using (var overlay = new SolidBrush(Color.FromArgb(220, 0, 0, 0)))
                g.FillRectangle(overlay, 0, 0, clientWidth, playHeight);
            using var goFont = new Font(uiFont.FontFamily, uiFont.Size + 20, FontStyle.Bold, GraphicsUnit.Point);
            float cy = playHeight * 0.28f;
            DrawCenteredText(g, "GAME OVER", goFont, GoBrush, clientWidth, cy);
            using var statFont = new Font(uiFont.FontFamily, uiFont.Size + 2f, FontStyle.Bold, GraphicsUnit.Point);
            using var statBrush = new SolidBrush(Color.FromArgb(225, 238, 245));
            DrawCenteredText(g, $"Final Score: {state.Score}", statFont, statBrush, clientWidth, cy + 64f);
            DrawCenteredText(g, $"Best Score:  {state.HighScore}", statFont, statBrush, clientWidth, cy + 96f);
            DrawCenteredText(g, $"Max Combo:  x{Math.Max(1, state.MaxCombo)}", statFont, statBrush, clientWidth, cy + 128f);
            const string hint = "Click Start to play again";
            using var hintBr = new SolidBrush(Color.FromArgb(220, 220, 225));
            DrawCenteredText(g, hint, uiFont, hintBr, clientWidth, cy + 168f);
        }
        else if (state.IsLifeLost)
        {
            using (var overlay = new SolidBrush(Color.FromArgb(165, 0, 0, 0)))
                g.FillRectangle(overlay, 0, 0, clientWidth, playHeight);
            using var lifeLostFont = new Font(uiFont.FontFamily, uiFont.Size + 10, FontStyle.Bold, GraphicsUnit.Point);
            const string msg = "LIFE LOST";
            SizeF sz = g.MeasureString(msg, lifeLostFont);
            float cx = (clientWidth - sz.Width) * 0.5f;
            float cy = playHeight * 0.34f;
            g.DrawString(msg, lifeLostFont, TextBrush, cx, cy);

            DrawLifeIcons(g, 0.5f * (clientWidth - (3 * 18 + 2 * 8)), cy + sz.Height + 14, 3, 16, state.Lives);

            const string hint = "Press Enter/Space or click Continue";
            using var hintBr = new SolidBrush(Color.FromArgb(220, 220, 225));
            float hintW = g.MeasureString(hint, uiFont).Width;
            g.DrawString(hint, uiFont, hintBr, (clientWidth - hintW) * 0.5f, cy + sz.Height + 44f);
        }
        else if (state.IsPaused)
        {
            using var overlay = new SolidBrush(Color.FromArgb(170, 0, 0, 0));
            g.FillRectangle(overlay, 0, 0, clientWidth, playHeight);
            using var pausedFont = new Font(uiFont.FontFamily, uiFont.Size + 18f, FontStyle.Bold, GraphicsUnit.Point);
            using var hintFont = new Font(uiFont.FontFamily, uiFont.Size + 1f, FontStyle.Regular, GraphicsUnit.Point);
            using var hintBr = new SolidBrush(Color.FromArgb(220, 220, 225));
            float cy = playHeight * 0.34f;
            DrawCenteredText(g, "PAUSED", pausedFont, TextBrush, clientWidth, cy);
            DrawCenteredText(g, "Press ESC to continue", hintFont, hintBr, clientWidth, cy + 56f);
        }
        else if (!state.IsPlaying)
        {
            using var title = new Font(uiFont.FontFamily, 22, FontStyle.Bold, GraphicsUnit.Point);
            const string t = "READY?";
            SizeF tsz = g.MeasureString(t, title);
            g.DrawString(t, title, TextBrush, (clientWidth - tsz.Width) * 0.5f, playHeight * 0.38f);
            const string s = "Click the Start button";
            g.DrawString(s, uiFont, SubtleTextBrush, (clientWidth - g.MeasureString(s, uiFont).Width) * 0.5f, playHeight * 0.38f + tsz.Height + 8f);
        }

        if (showDebug)
        {
            using var dbgFont = new Font(uiFont.FontFamily, 7.5f, FontStyle.Regular, GraphicsUnit.Point);
            using var dbgBr = new SolidBrush(Color.FromArgb(130, 160, 170, 190));
            g.DrawString(
                $"FPS {debugFps}  dt {state.LastDeltaSeconds * 1000f:0.#}ms  bars {entities.Bars.Count}  bul {entities.Bullets.Count}  ptcl {entities.Particles.Count}  boom {entities.Explosions.Count}  fuse {state.BombFuseFramesLeft}",
                dbgFont,
                dbgBr,
                8,
                playHeight - 28);
            g.DrawRectangle(DebugPenPlayer, player.GetBounds());
            foreach (var bar in entities.Bars) g.DrawRectangle(DebugPenEnemy, bar.GetBounds());
            foreach (var b in entities.Bullets) g.DrawRectangle(DebugPenBullet, b.GetBounds());
            if (state.HasPendingBombPickup)
            {
                using var fuseDbg = new Pen(Color.Lime, 1f);
                float ix = state.BombIndicatorX;
                float iy = state.BombIndicatorY;
                g.DrawLine(fuseDbg, ix - 8f, iy, ix + 8f, iy);
                g.DrawLine(fuseDbg, ix, iy - 8f, ix, iy + 8f);
            }
            using var boomMark = new Pen(Color.FromArgb(220, 255, 255, 80), 1f);
            foreach (var ex in entities.Explosions)
            {
                float ix = ex.X;
                float iy = ex.Y;
                g.DrawLine(boomMark, ix - 6f, iy, ix + 6f, iy);
                g.DrawLine(boomMark, ix, iy - 6f, ix, iy + 6f);
            }
        }

        g.Restore(graphicsState);
    }

    private static void DrawDevModeOverlay(Graphics g, Font uiFont, int clientWidth, int playHeight, GameState state)
    {
        using var font = new Font(uiFont.FontFamily, 7.5f, FontStyle.Bold, GraphicsUnit.Point);
        using var accent = new SolidBrush(Color.FromArgb(255, 120, 255, 140));
        using var dim = new SolidBrush(Color.FromArgb(210, 200, 220, 210));
        // Sit above F1 debug line (drawn at playHeight - 28)
        float y = playHeight - 76f;
        g.DrawString("DEV MODE", font, accent, 8f, y);
        string line1 = $"Bar {state.BarSpeed}→eff {state.GetEffectiveBarSpeed()}  spawn~{state.EffectiveSpawnIntervalFrames}f  cap {state.EffectiveMaxBarsOnScreen}";
        g.DrawString(line1, font, dim, 8f, y + 14f);
        string pu = state.ActivePowerUp is PowerUpType t
            ? $"Power-up: {GetPowerUpHudLabel(t)}"
            : "Power-up: —";
        g.DrawString(pu, font, dim, 8f, y + 28f);
    }

    private static void DrawBombFuseIndicator(Graphics g, GameState state)
    {
        float x = state.BombIndicatorX;
        float y = state.BombIndicatorY;
        float pulse = 0.55f + 0.45f * MathF.Sin(state.ElapsedFrames * 0.48f);
        int alpha = (int)(140 * pulse + 80);
        alpha = Math.Clamp(alpha, 70, 255);
        using var core = new SolidBrush(Color.FromArgb(alpha, 255, 220, 70));
        using var rim = new Pen(Color.FromArgb(255, 255, 100, 30), 2.5f) { LineJoin = LineJoin.Round };
        float r = 8f + 4f * pulse;
        g.FillEllipse(core, x - r, y - r, r * 2f, r * 2f);
        g.DrawEllipse(rim, x - r, y - r, r * 2f, r * 2f);
        float warnR = 16f;
        using var warn = new Pen(Color.FromArgb(200, 255, 60, 40), 1.5f) { DashStyle = DashStyle.Dash };
        g.DrawEllipse(warn, x - warnR, y - warnR, warnR * 2f, warnR * 2f);
    }

    private static void DrawBombScreenFlash(Graphics g, int clientWidth, int playHeight, GameState state)
    {
        int f = state.BombScreenFlashFrames;
        if (f <= 0) return;
        const int maxF = 14;
        float t = Math.Clamp(f / (float)maxF, 0f, 1f);
        int alpha = Math.Clamp((int)(235 * t), 0, 235);
        int warm = (int)(200 + 40 * t);
        using var flashBr = new SolidBrush(Color.FromArgb(alpha, 255, 255, warm));
        g.FillRectangle(flashBr, 0, 0, clientWidth, playHeight);
    }

    /// <summary>Cyan shield ring while active; pickup pulse; block burst ring + flash.</summary>
    private static void DrawShieldPlayerFx(Graphics g, Player player, GameState state)
    {
        bool passive = state.HasShieldActive;
        bool pickup = state.ShieldPickupFlashFrames > 0;
        bool ring = state.ShieldBlockRingFrames > 0;
        bool flash = state.ShieldBlockFlashFrames > 0;
        if (!passive && !pickup && !ring && !flash) return;

        Rectangle pr = player.GetBounds();
        float cx = pr.X + pr.Width * 0.5f;
        float cy = pr.Y + pr.Height * 0.5f;

        if (passive)
        {
            float pulse = 0.88f + 0.12f * MathF.Sin(state.ElapsedFrames * 0.31f);
            float pad = 8f * pulse;
            float w = pr.Width + pad * 2f;
            float h = (pr.Height + pad * 2f) * 1.05f;
            float left = cx - w * 0.5f;
            float top = cy - h * 0.5f;
            for (int k = 4; k >= 1; k--)
            {
                int alpha = 24 + k * 10;
                using var glow = new Pen(Color.FromArgb(alpha, 72, 210, 255), 1.1f + k * 0.42f) { LineJoin = LineJoin.Round };
                g.DrawEllipse(glow, left - k * 0.85f, top - k * 0.55f, w + k * 1.7f, h + k * 1.1f);
            }
            using var edge = new Pen(Color.FromArgb(238, 125, 242, 255), 2.5f) { LineJoin = LineJoin.Round };
            g.DrawEllipse(edge, left, top, w, h);
        }

        if (pickup)
        {
            float intensity = Math.Clamp(state.ShieldPickupFlashFrames / 22f, 0f, 1f);
            int a = (int)(55 + 135 * intensity);
            using var fill = new SolidBrush(Color.FromArgb(Math.Clamp(a, 0, 200), 50, 225, 255));
            float amp = 5f + 16f * intensity;
            g.FillEllipse(fill, pr.X - amp, pr.Y - amp * 0.7f, pr.Width + amp * 2f, pr.Height + amp * 1.4f);
        }

        if (ring)
        {
            const int ringMax = 18;
            float progress = 1f - Math.Clamp(state.ShieldBlockRingFrames / (float)ringMax, 0f, 1f);
            float e = progress * progress * (3f - 2f * progress);
            float radius = 14f + e * 60f;
            int a = (int)(230 * (1f - progress * 0.75f) + 20);
            using var pen = new Pen(Color.FromArgb(Math.Clamp(a, 35, 245), 150, 245, 255), 3.1f) { LineJoin = LineJoin.Round };
            g.DrawEllipse(pen, cx - radius, cy - radius, radius * 2f, radius * 2f);
            float r2 = radius * 0.5f;
            using var inner = new Pen(Color.FromArgb(Math.Clamp(a + 25, 0, 255), 255, 255, 255), 1.7f);
            g.DrawEllipse(inner, cx - r2, cy - r2, r2 * 2f, r2 * 2f);
        }

        if (flash)
        {
            float f = Math.Clamp(state.ShieldBlockFlashFrames / 14f, 0f, 1f);
            int a = (int)(130 * f);
            using var core = new SolidBrush(Color.FromArgb(Math.Clamp(a, 0, 145), 200, 255, 255));
            g.FillEllipse(core, pr.X - 4, pr.Y - 4, pr.Width + 8, pr.Height + 8);
        }
    }

    private static void DrawExplosionRings(Graphics g, IReadOnlyList<ExplosionFx> explosions)
    {
        foreach (ExplosionFx ex in explosions)
        {
            float u = ex.ExpansionT;
            float maxR = ex.MaxWaveRadius;
            float radius = 10f + (maxR - 10f) * u;
            float fade = ex.FramesLeft / (float)ex.MaxFrames;
            int baseA = Math.Clamp((int)(255 * fade), 18, 255);

            int aOut = (int)(baseA * 0.42f);
            using var outer = new Pen(Color.FromArgb(aOut, 255, 55, 30), 4.5f) { LineJoin = LineJoin.Round };
            g.DrawEllipse(outer, ex.X - radius, ex.Y - radius, radius * 2f, radius * 2f);

            float rMid = Math.Max(8f, radius * 0.64f);
            int aMid = (int)(baseA * 0.78f);
            using var mid = new Pen(Color.FromArgb(aMid, 255, 150, 45), 3f);
            g.DrawEllipse(mid, ex.X - rMid, ex.Y - rMid, rMid * 2f, rMid * 2f);

            float rCore = Math.Max(5f, radius * 0.36f);
            using var core = new Pen(Color.FromArgb(Math.Min(255, baseA + 25), 255, 255, 230), 2.4f);
            g.DrawEllipse(core, ex.X - rCore, ex.Y - rCore, rCore * 2f, rCore * 2f);
        }
    }

    private static void DrawHud(Graphics g, Font uiFont, int clientWidth, GameState state)
    {
        DrawLives(g, state);
        DrawScore(g, state, uiFont, clientWidth);
        DrawBestScore(g, state, uiFont, clientWidth);
        DrawPowerUpHud(g, state, uiFont, clientWidth);
        if (state.ComboStreak > 1)
        {
            int m = state.ComboMultiplier;
            using var comboBr = new SolidBrush(Color.FromArgb(255, 255, 165, 70));
            using var comboFont = new Font(uiFont.FontFamily, uiFont.Size + 4f, FontStyle.Bold, GraphicsUnit.Point);
            string comboText = $"COMBO x{m}";
            SizeF comboSize = g.MeasureString(comboText, comboFont);
            float cx = clientWidth - comboSize.Width - 18;
            g.DrawString(comboText, comboFont, comboBr, cx, 92);
        }
    }

    private static void DrawLives(Graphics g, GameState state)
    {
        const float x = 12f;
        const float y = 12f;
        using var labelBr = new SolidBrush(Color.FromArgb(170, 190, 205));
        using var labelFont = new Font("Segoe UI", 8f, FontStyle.Regular, GraphicsUnit.Point);
        g.DrawString("LIVES", labelFont, labelBr, x, y - 1);
        DrawLifeIcons(g, x + 46, y + 2, 3, 12, state.Lives);
    }

    private static void DrawScore(Graphics g, GameState state, Font uiFont, int clientWidth)
    {
        using var labelBr = new SolidBrush(Color.FromArgb(175, 190, 205));
        using var scoreBr = new SolidBrush(Color.FromArgb(235, 245, 255));
        using var labelFont = new Font(uiFont.FontFamily, uiFont.Size - 1f, FontStyle.Regular, GraphicsUnit.Point);
        using var scoreFont = new Font(uiFont.FontFamily, uiFont.Size + 6f, FontStyle.Bold, GraphicsUnit.Point);
        string label = "SCORE";
        string value = state.Score.ToString();
        SizeF valueSize = g.MeasureString(value, scoreFont);
        float boxW = Math.Max(110f, valueSize.Width + 22f);
        float boxH = 48f;
        float boxX = clientWidth - boxW - 12f;
        float boxY = 10f;
        using var boxBr = new SolidBrush(Color.FromArgb(95, 8, 16, 28));
        using var boxPen = new Pen(Color.FromArgb(120, 75, 95, 125), 1f);
        g.FillRectangle(boxBr, boxX, boxY, boxW, boxH);
        g.DrawRectangle(boxPen, boxX, boxY, boxW - 1, boxH - 1);
        g.DrawString(label, labelFont, labelBr, boxX + 10, boxY + 4);
        g.DrawString(value, scoreFont, scoreBr, boxX + 10, boxY + 16);
    }

    private static void DrawBestScore(Graphics g, GameState state, Font uiFont, int clientWidth)
    {
        using var bestBr = new SolidBrush(Color.FromArgb(165, 176, 188));
        using var bestFont = new Font(uiFont.FontFamily, uiFont.Size, FontStyle.Regular, GraphicsUnit.Point);
        string bestText = $"BEST {state.HighScore}";
        SizeF bestSize = g.MeasureString(bestText, bestFont);
        g.DrawString(bestText, bestFont, bestBr, clientWidth - bestSize.Width - 18f, 62f);
    }

    private static void DrawNewBestBanner(Graphics g, Font uiFont, int clientWidth, GameState state)
    {
        if (!state.IsNewBestThisRun || state.NewBestFlashFrames <= 0) return;
        float t = state.NewBestFlashFrames / 150f;
        int alpha = 120 + (int)(120f * Math.Min(1f, t + 0.1f));
        float rise = (1f - t) * 12f;
        float y = 18f - rise;

        using var back = new SolidBrush(Color.FromArgb(Math.Min(220, alpha), 40, 22, 0));
        using var text = new SolidBrush(Color.FromArgb(Math.Min(255, alpha + 20), 255, 210, 85));
        using var font = new Font(uiFont.FontFamily, uiFont.Size + 5f, FontStyle.Bold, GraphicsUnit.Point);
        const string msg = "NEW BEST!";
        SizeF sz = g.MeasureString(msg, font);
        float boxW = sz.Width + 24f;
        float boxH = sz.Height + 10f;
        float x = (clientWidth - boxW) * 0.5f;
        g.FillRectangle(back, x, y, boxW, boxH);
        g.DrawString(msg, font, text, x + 12f, y + 4f);
    }

    private static void DrawPowerUps(Graphics g, List<PowerUp> powerUps)
    {
        foreach (var p in powerUps)
        {
            Rectangle r = p.GetBounds();
            using var br = new SolidBrush(GetPowerUpColor(p.Type));
            g.FillEllipse(br, r);
            using var pen = new Pen(Color.FromArgb(180, 0, 0, 0), 1f);
            g.DrawEllipse(pen, r);
        }
    }

    private static void DrawPowerUpHud(Graphics g, GameState state, Font uiFont, int clientWidth)
    {
        const float blockW = 78f;
        const float blockH = 62f;
        const float iconSize = 36f;
        float blockX = (clientWidth - blockW) * 0.5f;
        float blockY = 8f;
        using var panel = new SolidBrush(Color.FromArgb(92, 10, 18, 28));
        using var panelBorder = new Pen(Color.FromArgb(130, 75, 95, 125), 1f);
        g.FillRectangle(panel, blockX, blockY, blockW, blockH);
        g.DrawRectangle(panelBorder, blockX, blockY, blockW - 1f, blockH - 1f);

        float iconX = blockX + (blockW - iconSize) * 0.5f;
        float iconY = blockY + 6f;
        DrawActivePowerUpCard(g, state, iconX, iconY, iconSize, iconSize, blockX, blockW);
    }

    /// <summary>Large glyph on top, uppercase label below, optional timer bar at bottom of HUD slot.</summary>
    private static void DrawActivePowerUpCard(
        Graphics g,
        GameState state,
        float iconX,
        float iconY,
        float sizeW,
        float sizeH,
        float panelX,
        float panelW)
    {
        if (state.ActivePowerUp is null)
        {
            using var empty = new SolidBrush(Color.FromArgb(55, 85, 96, 112));
            using var emptyBorder = new Pen(Color.FromArgb(100, 95, 108, 128), 1f);
            g.FillEllipse(empty, iconX, iconY, sizeW, sizeH);
            g.DrawEllipse(emptyBorder, iconX, iconY, sizeW - 1f, sizeH - 1f);
            using var dash = new Pen(Color.FromArgb(140, 180, 195, 210), 1.2f) { DashStyle = DashStyle.Dot };
            float cx = iconX + sizeW * 0.5f;
            float cy = iconY + sizeH * 0.5f;
            g.DrawLine(dash, cx - 8f, cy, cx + 8f, cy);
            return;
        }

        var t = state.ActivePowerUp.Value;
        Color c = GetPowerUpColor(t);
        using var halo = new SolidBrush(Color.FromArgb(95, c));
        g.FillEllipse(halo, iconX - 2f, iconY - 2f, sizeW + 4f, sizeH + 4f);
        using var border = new Pen(Color.FromArgb(255, 255, 255, 255), 2f);
        g.DrawEllipse(border, iconX - 1f, iconY - 1f, sizeW + 1f, sizeH + 1f);
        DrawPowerUpGlyph(g, t, c, iconX + 4f, iconY + 4f, sizeW - 8f, sizeH - 8f);

        string label = GetPowerUpHudLabel(t);
        using var labelBr = new SolidBrush(Color.FromArgb(248, 252, 255));
        using var labelFont = new Font("Segoe UI", 7f, FontStyle.Bold, GraphicsUnit.Point);
        SizeF labelSize = g.MeasureString(label, labelFont);
        float labelX = panelX + (panelW - labelSize.Width) * 0.5f;
        float labelY = iconY + sizeH + 2f;
        g.DrawString(label, labelFont, labelBr, labelX, labelY);

        if (state.ActivePowerUpDurationFrames > 0 && state.PowerUpTimerFrames > 0)
        {
            float r = Math.Clamp(state.PowerUpTimerFrames / (float)state.ActivePowerUpDurationFrames, 0f, 1f);
            float barPad = 8f;
            float barW = panelW - barPad * 2f;
            float barY = labelY + labelSize.Height + 2f;
            using var bg = new SolidBrush(Color.FromArgb(110, 30, 40, 55));
            using var fg = new SolidBrush(Color.FromArgb(230, c));
            g.FillRectangle(bg, panelX + barPad, barY, barW, 4f);
            g.FillRectangle(fg, panelX + barPad, barY, barW * r, 4f);
        }
    }

    /// <summary>Distinct simple glyphs so each power-up reads at a glance.</summary>
    private static void DrawPowerUpGlyph(Graphics g, PowerUpType type, Color c, float x, float y, float w, float h)
    {
        float cx = x + w * 0.5f;
        float cy = y + h * 0.5f;
        using var fill = new SolidBrush(c);
        using var outline = new Pen(Color.FromArgb(220, 20, 20, 30), 1.2f);

        switch (type)
        {
            case PowerUpType.RapidFire:
            {
                g.FillEllipse(fill, cx - w * 0.35f, cy - h * 0.35f, w * 0.7f, h * 0.7f);
                using var streak = new Pen(Color.FromArgb(255, 255, 240, 200), 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLine(streak, cx - w * 0.15f, cy, cx + w * 0.42f, cy - h * 0.12f);
                g.DrawLine(streak, cx - w * 0.15f, cy + 2f, cx + w * 0.42f, cy + h * 0.1f);
                break;
            }
            case PowerUpType.MultiShot:
                float r = Math.Min(w, h) * 0.18f;
                g.FillEllipse(fill, cx - w * 0.38f - r, cy - r, r * 2f, r * 2f);
                g.FillEllipse(fill, cx - r, cy - r, r * 2f, r * 2f);
                g.FillEllipse(fill, cx + w * 0.38f - r, cy - r, r * 2f, r * 2f);
                g.DrawEllipse(outline, cx - w * 0.38f - r, cy - r, r * 2f, r * 2f);
                g.DrawEllipse(outline, cx - r, cy - r, r * 2f, r * 2f);
                g.DrawEllipse(outline, cx + w * 0.38f - r, cy - r, r * 2f, r * 2f);
                break;
            case PowerUpType.PiercingShot:
                PointF[] diamond =
                {
                    new(cx, y + 2f),
                    new(x + w - 2f, cy),
                    new(cx, y + h - 2f),
                    new(x + 2f, cy)
                };
                g.FillPolygon(fill, diamond);
                g.DrawPolygon(outline, diamond);
                break;
            case PowerUpType.Shield:
            {
                float bw = w * 0.72f;
                float bh = h * 0.78f;
                float bx = cx - bw * 0.5f;
                float by = cy - bh * 0.42f;
                using var shieldPath = new GraphicsPath();
                shieldPath.AddArc(bx, by, bw, bh * 0.55f, 180f, 180f);
                shieldPath.AddLine(bx + bw, by + bh * 0.28f, bx + bw * 0.5f, y + h - 3f);
                shieldPath.AddLine(bx + bw * 0.5f, y + h - 3f, bx, by + bh * 0.28f);
                shieldPath.CloseFigure();
                g.FillPath(fill, shieldPath);
                g.DrawPath(outline, shieldPath);
                break;
            }
            case PowerUpType.SlowMotion:
            {
                g.FillEllipse(fill, cx - w * 0.38f, cy - h * 0.38f, w * 0.76f, h * 0.76f);
                using var sweep = new Pen(Color.FromArgb(255, 255, 255, 255), 2.2f) { StartCap = LineCap.Round };
                g.DrawArc(sweep, cx - w * 0.32f, cy - h * 0.32f, w * 0.64f, h * 0.64f, 200f, 220f);
                using var tick = new Pen(Color.FromArgb(255, 255, 255, 255), 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLine(tick, cx, cy, cx + w * 0.22f, cy - h * 0.18f);
                break;
            }
            case PowerUpType.BombShot:
            {
                g.FillEllipse(fill, cx - w * 0.32f, cy - h * 0.25f, w * 0.64f, h * 0.55f);
                using var fuse = new Pen(Color.FromArgb(255, 255, 230, 160), 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLine(fuse, cx + w * 0.12f, cy - h * 0.28f, cx + w * 0.28f, cy - h * 0.48f);
                g.DrawEllipse(outline, cx - w * 0.32f, cy - h * 0.25f, w * 0.64f, h * 0.55f);
                break;
            }
            default:
                g.FillEllipse(fill, x, y, w, h);
                g.DrawEllipse(outline, x, y, w - 1f, h - 1f);
                break;
        }
    }

    private static Color GetPowerUpColor(PowerUpType type) => type switch
    {
        PowerUpType.RapidFire => Color.Orange,
        PowerUpType.MultiShot => Color.DeepSkyBlue,
        PowerUpType.PiercingShot => Color.MediumPurple,
        PowerUpType.Shield => Color.FromArgb(255, 70, 210, 255),
        PowerUpType.SlowMotion => Color.Cyan,
        PowerUpType.BombShot => Color.OrangeRed,
        _ => Color.White
    };

    private static string GetPowerUpHudLabel(PowerUpType type) => type switch
    {
        PowerUpType.RapidFire => "RAPID",
        PowerUpType.MultiShot => "MULTI",
        PowerUpType.PiercingShot => "PIERCE",
        PowerUpType.Shield => "SHIELD",
        PowerUpType.SlowMotion => "SLOW",
        PowerUpType.BombShot => "BOMB",
        _ => "?"
    };

    private static void DrawLifeIcons(Graphics g, float startX, float y, int maxLives, int size, int active)
    {
        if (maxLives <= 0) return;
        float x = startX;
        for (int i = 0; i < maxLives; i++)
        {
            DrawHeart(g, x, y, size, i < active);
            x += size + 8;
        }
    }

    private static void DrawHeart(Graphics g, float x, float y, int size, bool active)
    {
        float r = size * 0.35f;
        float topY = y + size * 0.18f;
        using var fill = new SolidBrush(active ? Color.FromArgb(244, 72, 94) : Color.FromArgb(78, 78, 92));
        using var outline = new Pen(Color.FromArgb(170, 0, 0, 0), 1f);

        g.FillEllipse(fill, x, topY, r * 2f, r * 2f);
        g.FillEllipse(fill, x + r * 1.2f, topY, r * 2f, r * 2f);
        PointF[] tri =
        {
            new(x + r * 0.1f, topY + r * 1.35f),
            new(x + r * 3.1f, topY + r * 1.35f),
            new(x + r * 1.6f, y + size)
        };
        g.FillPolygon(fill, tri);

        g.DrawEllipse(outline, x, topY, r * 2f, r * 2f);
        g.DrawEllipse(outline, x + r * 1.2f, topY, r * 2f, r * 2f);
        g.DrawPolygon(outline, tri);
    }

    private static void DrawCenteredText(Graphics g, string text, Font font, Brush brush, int clientWidth, float y)
    {
        SizeF sz = g.MeasureString(text, font);
        g.DrawString(text, font, brush, (clientWidth - sz.Width) * 0.5f, y);
    }

    private static void DrawBackground(Graphics g, int w, int h)
    {
        DrawSynthwaveGradient(g, w, h);
        DrawSynthwaveSun(g, w, h);
        DrawSynthwaveSkyline(g, w, h);
        DrawSynthwaveGrid(g, w, h);
    }

    /// <summary>Vertical multi-stop gradient: deep purple-blue → magenta → warm sunset.</summary>
    private static void DrawSynthwaveGradient(Graphics g, int w, int h)
    {
        var rect = new Rectangle(0, 0, w, Math.Max(1, h));
        using var sky = new LinearGradientBrush(
            rect,
            Color.FromArgb(255, 14, 8, 40),
            Color.FromArgb(255, 255, 195, 75),
            LinearGradientMode.Vertical);
        var blend = new ColorBlend(4)
        {
            Positions = new[] { 0f, 0.34f, 0.64f, 1f },
            Colors = new[]
            {
                Color.FromArgb(255, 16, 10, 46),
                Color.FromArgb(255, 58, 16, 82),
                Color.FromArgb(255, 215, 65, 110),
                Color.FromArgb(255, 255, 188, 72)
            }
        };
        sky.InterpolationColors = blend;
        g.FillRectangle(sky, 0, 0, w, h);
    }

    /// <summary>Large sunset disk with horizontal scanlines (retro CRT striping).</summary>
    private static void DrawSynthwaveSun(Graphics g, int w, int h)
    {
        float sunR = w * 0.44f;
        float cx = w * 0.5f;
        float cy = h + sunR * 0.82f;

        using (var disk = new SolidBrush(Color.FromArgb(230, 255, 205, 70)))
            g.FillEllipse(disk, cx - sunR, cy - sunR, sunR * 2f, sunR * 2f);

        using var rim = new Pen(Color.FromArgb(180, 255, 230, 120), 1.2f);
        g.DrawEllipse(rim, cx - sunR, cy - sunR, sunR * 2f, sunR * 2f);

        using var sunPath = new GraphicsPath();
        sunPath.AddEllipse(cx - sunR, cy - sunR, sunR * 2f, sunR * 2f);

        GraphicsState saved = g.Save();
        try
        {
            g.SetClip(sunPath);
            using var stripe = new Pen(Color.FromArgb(55, 210, 90, 45), 1f);
            float y0 = cy - sunR;
            float y1 = Math.Min(h + 2f, cy + sunR);
            for (float y = y0; y <= y1; y += 5f)
                g.DrawLine(stripe, cx - sunR - 2f, y, cx + sunR + 2f, y);
        }
        finally
        {
            g.Restore(saved);
        }
    }

    /// <summary>Dark building silhouettes along the bottom (simple rects, deterministic layout).</summary>
    private static void DrawSynthwaveSkyline(Graphics g, int w, int h)
    {
        using var sil = new SolidBrush(Color.FromArgb(252, 6, 4, 18));
        float x = -6f;
        int idx = 0;
        while (x < w + 8f)
        {
            int code = idx * 92837111 ^ (w * 1103515245);
            if (code < 0) code = -code;
            float bw = 11f + (code % 26);
            float bh = 26f + ((code >> 5) % 54);
            g.FillRectangle(sil, x, h - bh, bw, bh + 4f);
            x += bw * 0.72f;
            idx++;
        }
    }

    /// <summary>Thin neon-style grid; low alpha so bars and player stay readable.</summary>
    private static void DrawSynthwaveGrid(Graphics g, int w, int h)
    {
        const int step = 40;
        using var vPen = new Pen(Color.FromArgb(36, 190, 95, 255), 0.65f);
        using var hPen = new Pen(Color.FromArgb(32, 255, 70, 210), 0.65f);
        for (int x = 0; x < w; x += step)
            g.DrawLine(vPen, x, 0, x, h);
        for (int y = 0; y < h; y += step)
            g.DrawLine(hPen, 0, y, w, y);
    }

    private static Color GetBarDrawColor(Bar bar)
    {
        if (bar.HitFlashTimer > 0)
            return bar.PierceFlash
                ? Color.FromArgb(255, 248, 140, 255)
                : Color.FromArgb(255, 220, 200);
        float t = bar.HealthRatio;
        if (t > 0.5f) return Lerp3(Color.LimeGreen, Color.Gold, 2f * (1f - t));
        return Lerp3(Color.Gold, Color.Firebrick, 1f - 2f * t);
    }

    private static Color Lerp3(Color from, Color to, float u)
    {
        u = Math.Clamp(u, 0, 1);
        return Color.FromArgb(
            (int)(from.R + (to.R - from.R) * u),
            (int)(from.G + (to.G - from.G) * u),
            (int)(from.B + (to.B - from.B) * u));
    }
}
