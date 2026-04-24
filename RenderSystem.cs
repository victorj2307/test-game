using System.Drawing;
using System.Drawing.Drawing2D;

namespace RetroArcade;

/// <summary>All GDI+ drawing for the playfield: world, HUD, overlays, debug.</summary>
public sealed class RenderSystem
{
    private static readonly Brush PlayerBrush = new SolidBrush(Color.FromArgb(40, 220, 255));
    private static readonly Brush MuzzlePortBrush = new SolidBrush(Color.FromArgb(255, 230, 120));
    private static readonly Brush BulletBrushStatic = new SolidBrush(Color.FromArgb(255, 250, 200));
    private static readonly Brush TextBrush = Brushes.White;
    private static readonly Brush SubtleTextBrush = new SolidBrush(Color.Silver);
    private static readonly Brush GoBrush = new SolidBrush(Color.OrangeRed);
    private static readonly Pen DebugPenPlayer = new(Color.Lime) { Width = 1.5f };
    private static readonly Pen DebugPenEnemy = new(Color.Magenta) { Width = 1f };
    private static readonly Pen DebugPenBullet = new(Color.Cyan) { Width = 1f };

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
        if (now < state.ShakeUntilTickMs)
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

        using (var trailPen = new Pen(Color.FromArgb(55, 180, 230, 255), 1.5f))
        {
            foreach (var bullet in entities.Bullets)
            {
                int cx = bullet.X + bullet.Width / 2;
                int bottom = bullet.Y + bullet.Height;
                g.DrawLine(trailPen, cx, bottom + 12, cx, bottom);
            }
        }
        foreach (var bullet in entities.Bullets) g.FillRectangle(BulletBrushStatic, bullet.GetBounds());

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

        DrawHudStacked(g, uiFont, playHeight, state);

        if (state.IsGameOver)
        {
            using (var overlay = new SolidBrush(Color.FromArgb(175, 0, 0, 0)))
                g.FillRectangle(overlay, 0, 0, clientWidth, playHeight);
            using var goFont = new Font(uiFont.FontFamily, uiFont.Size + 14, FontStyle.Bold, GraphicsUnit.Point);
            const string msg = "GAME OVER";
            SizeF sz = g.MeasureString(msg, goFont);
            float cx = (clientWidth - sz.Width) * 0.5f;
            float cy = playHeight * 0.38f;
            g.DrawString(msg, goFont, GoBrush, cx, cy);
            const string hint = "Click Start to play again";
            using var hintBr = new SolidBrush(Color.FromArgb(220, 220, 225));
            float hintW = g.MeasureString(hint, uiFont).Width;
            g.DrawString(hint, uiFont, hintBr, (clientWidth - hintW) * 0.5f, cy + sz.Height + 10f);
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
            g.DrawString($"FPS {debugFps}  bars {entities.Bars.Count}  bul {entities.Bullets.Count}  ptcl {entities.Particles.Count}", dbgFont, dbgBr, 8, playHeight - 28);
            g.DrawRectangle(DebugPenPlayer, player.GetBounds());
            foreach (var bar in entities.Bars) g.DrawRectangle(DebugPenEnemy, bar.GetBounds());
            foreach (var b in entities.Bullets) g.DrawRectangle(DebugPenBullet, b.GetBounds());
        }

        g.Restore(graphicsState);
    }

    private static void DrawHudStacked(Graphics g, Font uiFont, int playHeight, GameState state)
    {
        const string gameName = "Retro Blaster";
        g.DrawString(gameName, uiFont, SubtleTextBrush, 8, 6);
        using var scoreFont = new Font(uiFont.FontFamily, uiFont.Size + 2f, FontStyle.Bold, GraphicsUnit.Point);
        g.DrawString($"SCORE  {state.Score}", scoreFont, TextBrush, 8, 24);
        using var bestBr = new SolidBrush(Color.FromArgb(170, 175, 185));
        using var bestFont = new Font(uiFont.FontFamily, uiFont.Size, FontStyle.Regular, GraphicsUnit.Point);
        g.DrawString($"BEST  {state.HighScore}", bestFont, bestBr, 8, 48);
        if (state.ComboStreak > 1)
        {
            int m = state.ComboMultiplier;
            using var co = new SolidBrush(Color.FromArgb(255, 255, 160, 70));
            using var coFont = new Font(uiFont.FontFamily, uiFont.Size + 1f, FontStyle.Bold, GraphicsUnit.Point);
            g.DrawString($"COMBO  x{m}", coFont, co, 8, 70);
        }
    }

    private static void DrawBackground(Graphics g, int w, int h)
    {
        using var br = new LinearGradientBrush(
            new Rectangle(0, 0, w, h),
            Color.FromArgb(255, 5, 7, 18),
            Color.FromArgb(255, 18, 22, 42),
            LinearGradientMode.Vertical);
        g.FillRectangle(br, 0, 0, w, h);
        using var grid = new Pen(Color.FromArgb(28, 26, 34, 56), 0.8f);
        for (int x = 0; x < w; x += 32) g.DrawLine(grid, x, 0, x, h);
        for (int y = 0; y < h; y += 32) g.DrawLine(grid, 0, y, w, y);
    }

    private static Color GetBarDrawColor(Bar bar)
    {
        if (bar.HitFlashTimer > 0) return Color.FromArgb(255, 220, 200);
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
