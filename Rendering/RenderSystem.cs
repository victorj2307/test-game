using System.Drawing;
using System.Drawing.Drawing2D;
using Game.Core;
using Game.Entities;

namespace Game.Rendering;

/// <summary>
/// All GDI+ drawing for the playfield: world, HUD, overlays, debug.
/// Hot paths reuse <see cref="Pen"/>, <see cref="SolidBrush"/>, and <see cref="Font"/> via static LRU-bounded caches
/// (evicted entries are disposed). The synthwave backdrop is cached to a <see cref="Bitmap"/> and rebuilt only on resize.
/// Call <see cref="DisposeSharedResources"/> on application shutdown
/// (see <see cref="Game.Core.GameManager.DisposeResources"/>) to release remaining cached handles and the backdrop bitmap.
/// </summary>
public sealed class RenderSystem
{
    private static readonly Brush MuzzlePortBrush = new SolidBrush(Color.FromArgb(255, 220, 190, 90));
    // Readable ground turret: hull + barrel fills, one silhouette outline (no nested neon boxes / silhouette glow).
    private static readonly SolidBrush CannonHullBrush = new(Color.FromArgb(255, 16, 38, 62));
    private static readonly SolidBrush CannonHullLipBrush = new(Color.FromArgb(255, 6, 14, 32));
    private static readonly SolidBrush CannonBarrelBrush = new(Color.FromArgb(255, 28, 72, 98));
    private static readonly SolidBrush CannonPanelBrush = new(Color.FromArgb(255, 255, 215, 70));
    private static readonly SolidBrush CannonPanelFlashBrush = new(Color.FromArgb(255, 255, 255, 220));
    private static readonly Pen CannonOutlinePen = new(Color.FromArgb(220, 40, 210, 220), 1.25f) { LineJoin = LineJoin.Miter };
    private static readonly SolidBrush BulletOuterGlowBrush = new(Color.FromArgb(70, 255, 230, 60));
    private static readonly SolidBrush BulletCoreBrush = new(Color.FromArgb(255, 255, 252, 120));
    private static readonly SolidBrush BulletTrailNearBrush = new(Color.FromArgb(50, 255, 240, 140));
    private static readonly SolidBrush BulletTrailFarBrush = new(Color.FromArgb(30, 180, 230, 255));
    private static readonly SolidBrush BulletTrailPierceNearBrush = new(Color.FromArgb(55, 240, 120, 255));
    private static readonly SolidBrush BulletTrailPierceFarBrush = new(Color.FromArgb(35, 200, 80, 255));
    private static readonly SolidBrush PierceBulletOuterGlowBrush = new(Color.FromArgb(95, 200, 50, 255));
    private static readonly Brush PierceBulletCore = new SolidBrush(Color.FromArgb(255, 255, 210, 255));
    private static readonly Brush PierceBulletGlow = new SolidBrush(Color.FromArgb(140, 210, 70, 255));
    private static readonly Brush TextBrush = Brushes.White;
    private static readonly Brush SubtleTextBrush = new SolidBrush(Color.FromArgb(235, 236, 245));
    private static readonly Brush AttractHintBrush = new SolidBrush(Color.FromArgb(245, 248, 255));
    private static readonly Brush GoBrush = new SolidBrush(Color.OrangeRed);
    private static readonly Pen DebugPenPlayer = new(Color.Lime) { Width = 1.5f };
    private static readonly Pen DebugPenEnemy = new(Color.Magenta) { Width = 1f };
    private static readonly Pen DebugPenBullet = new(Color.Cyan) { Width = 1f };
    private static readonly Font Ui8RegularFont = new("Segoe UI", 8f, FontStyle.Regular, GraphicsUnit.Point);
    private static readonly Font Ui7BoldFont = new("Segoe UI", 7f, FontStyle.Bold, GraphicsUnit.Point);

    private readonly SolidBrush _barBrush = new(Color.White);
    private readonly Pen _specialGlowPen = new(Color.White, 1.6f);
    private readonly SolidBrush _particleBrush = new(Color.White);
    private static readonly SolidBrush PowerUpBrush = new(Color.White);
    private static readonly SolidBrush FragmentBrush = new(Color.White);
    private static readonly Pen BulletAimPen = new(Color.FromArgb(255, 210, 160, 90), 2.25f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
    // Mutable pens for animated FX — avoid per-frame alpha cache keys / GDI create+dispose churn.
    private static readonly Pen ExplosionOuterPen = new(Color.FromArgb(100, 255, 55, 30), 5.5f) { LineJoin = LineJoin.Round };
    private static readonly Pen ExplosionMidPen = new(Color.FromArgb(180, 255, 150, 45), 4f);
    private static readonly Pen ExplosionCorePen = new(Color.FromArgb(220, 255, 255, 230), 3f);
    private static readonly SolidBrush ExplosionCoreFillBrush = new(Color.FromArgb(160, 255, 200, 80));
    private static readonly SolidBrush ImpactFlashBrush = new(Color.FromArgb(230, 255, 255, 220));
    private static readonly Pen ShieldGlowPen = new(Color.FromArgb(40, 110, 90, 220), 1.5f) { LineJoin = LineJoin.Round };
    private static readonly Pen ShieldEdgePen = new(Color.FromArgb(210, 190, 170, 255), 1.9f) { LineJoin = LineJoin.Round };
    private static readonly Pen ShieldBlockOuterPen = new(Color.FromArgb(200, 170, 140, 255), 3.1f) { LineJoin = LineJoin.Round };
    private static readonly Pen ShieldBlockInnerPen = new(Color.FromArgb(220, 230, 220, 255), 1.7f);
    private static readonly Pen BombFuseRimPen = new(Color.FromArgb(255, 255, 100, 30), 2.5f) { LineJoin = LineJoin.Round };
    private static readonly Pen BombFuseWarnPen = new(Color.FromArgb(200, 255, 60, 40), 1.5f) { DashStyle = DashStyle.Dash };
    private static readonly SolidBrush ScorePopupBrush = new(Color.FromArgb(255, 255, 230, 120));
    // GDI resource caches reduce repeated allocations in hot draw paths.
    private static readonly Dictionary<long, Font> FontCache = new();
    private static readonly LinkedList<long> FontLru = new();
    private static readonly Dictionary<long, LinkedListNode<long>> FontNodes = new();
    private static readonly Dictionary<int, SolidBrush> BrushCache = new();
    private static readonly LinkedList<int> BrushLru = new();
    private static readonly Dictionary<int, LinkedListNode<int>> BrushNodes = new();
    private static readonly Dictionary<long, Pen> PenCache = new();
    private static readonly LinkedList<long> PenLru = new();
    private static readonly Dictionary<long, LinkedListNode<long>> PenNodes = new();
    private const int MaxFontCacheEntries = 96;
    private const int MaxBrushCacheEntries = 192;
    private const int MaxPenCacheEntries = 256;

    // HUD text caches — rebuild only when values change (avoids ToString/MeasureString every paint).
    private static int _hudScoreCached = int.MinValue;
    private static string _hudScoreText = "0";
    private static float _hudScoreWidth;
    private static int _hudBestCached = int.MinValue;
    private static string _hudBestText = "BEST 0";
    private static float _hudBestWidth;
    private static int _hudComboMultCached = int.MinValue;
    private static bool _hudComboHotCached;
    private static string _hudComboText = "COMBO x1";
    private static float _hudComboWidth;
    // Synthwave backdrop is size-dependent; rebuild only when the playfield dimensions change.
    private static Bitmap? CachedBackgroundBitmap;
    private static int CachedBgWidth = -1;
    private static int CachedBgHeight = -1;
    /// <summary>Shared color stops reused whenever the cached sky brush is rebuilt into the backdrop bitmap.</summary>
    private static readonly ColorBlend SkyGradientBlend = new(4)
    {
        Positions = [0f, 0.34f, 0.64f, 1f],
        Colors =
        [
            Color.FromArgb(255, 16, 10, 46),
            Color.FromArgb(255, 58, 16, 82),
            Color.FromArgb(255, 215, 65, 110),
            Color.FromArgb(255, 255, 188, 72)
        ]
    };

    private static readonly float[] BombShakeOx =
    {
        2.2f, -2.5f, 1.8f, -1.6f, 2.4f, -2f, 0f, 1.5f, -2.2f, 1.2f
    };

    private static readonly float[] BombShakeOy =
    {
        -1.8f, 2.1f, 1.5f, -2.3f, 1.2f, 2f, -2f, 0.8f, -1.4f, 2.2f
    };

    /// <summary>
    /// Draws a full gameplay frame including world entities, effects, HUD, overlays, and debug visuals.
    /// </summary>
    public void Draw(
        Graphics g,
        int clientWidth,
        int playHeight,
        Font uiFont,
        GameState state,
        EntityManager entities,
        Player player,
        IReadOnlyList<HighScoreStore.LeaderboardEntry> leaderboard,
        bool showDebug,
        int debugFps,
        string? leaderboardHighlightName = null)
    {
        // World / FX: no AA — GDI+ AntiAlias dominates frame cost when particles/fragments are dense.
        g.SmoothingMode = SmoothingMode.None;
        g.CompositingQuality = CompositingQuality.HighSpeed;
        g.PixelOffsetMode = PixelOffsetMode.HighSpeed;

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
            // ~2–2.5 px destroy shake (bomb heavy path stays stronger above).
            ox = phase switch { 0 => 2.2f, 2 => -2.2f, _ => 0f };
            oy = phase switch { 1 => 2f, 3 => -2f, _ => 0f };
        }
        var graphicsState = g.Save();
        g.TranslateTransform(ox, oy);

        DrawBackground(g, clientWidth, playHeight);

        int dangerY = Math.Max(40, playHeight - player.Height - 28);
        bool barPastDanger = false;
        foreach (var bar in entities.Bars)
        {
            if (bar.GetBounds().Bottom >= dangerY) { barPastDanger = true; break; }
        }
        Color dangerColor = barPastDanger ? Color.FromArgb(180, 255, 120, 60) : Color.FromArgb(70, 80, 110, 130);
        _specialGlowPen.Color = dangerColor;
        _specialGlowPen.Width = barPastDanger ? 2f : 1f;
        g.DrawLine(_specialGlowPen, 0, dangerY, clientWidth, dangerY);

        var barOutline = GetCachedPen(Color.FromArgb(110, 0, 0, 0));
        foreach (var bar in entities.Bars)
        {
            if (bar.Height <= 0) continue;
            var r = bar.GetBounds();
            if (r.Height <= 0) continue;
            if (bar.HitPulseFrames > 0)
            {
                int pulsePad = bar.PierceFlash ? 2 : 1;
                r = Rectangle.Inflate(r, pulsePad, pulsePad);
            }
            _barBrush.Color = GetBarDrawColor(bar);
            g.FillRectangle(_barBrush, r);
            if (bar.IsSpecial)
            {
                _specialGlowPen.Color = GetSpecialOutlineColor(state.ElapsedFrames);
                _specialGlowPen.Width = 1.6f;
                g.DrawRectangle(_specialGlowPen, r.X - 1, r.Y - 1, r.Width + 1, r.Height + 1);
            }
            g.DrawRectangle(barOutline, r.X, r.Y, r.Width - 1, r.Height - 1);
        }

        foreach (var p in entities.Particles)
        {
            _particleBrush.Color = p.Color;
            int s = p.Size;
            g.FillRectangle(_particleBrush, p.X - s * 0.5f, p.Y - s * 0.5f, s, s);
        }
        DrawFragments(g, entities.Fragments);

        DrawPowerUps(g, entities.PowerUps);

        if (state.HasPendingBombPickup)
            DrawBombFuseIndicator(g, state);

        // Brief AA only for curved explosion/shield rings; particles stay None.
        g.SmoothingMode = SmoothingMode.AntiAlias;
        DrawExplosionRings(g, entities.Explosions);
        g.SmoothingMode = SmoothingMode.None;

        DrawImpactFlash(g, state);
        DrawScorePopups(g, uiFont, entities.ScorePopups);

        foreach (var bullet in entities.Bullets)
            DrawBulletTrails(g, bullet);
        foreach (var bullet in entities.Bullets)
            DrawBulletBody(g, bullet);

        if (!state.IsFinalDeathAnimating)
        {
            // Shield under the cannon: status FX stays ambient; turret remains the focal object.
            g.SmoothingMode = SmoothingMode.AntiAlias;
            DrawShieldPlayerFx(g, player, state, clientWidth, playHeight);

            int recoilDy = state.MuzzleFlashFrames > 0 ? (state.MuzzleFlashFrames >= 2 ? 2 : 1) : 0;
            GraphicsState cannonState = g.Save();
            // Negative Y: brief kick upward (weapon silhouette jumps toward muzzle).
            g.TranslateTransform(0, -recoilDy);
            DrawPlayerCannon(g, player, state);
            g.Restore(cannonState);
        }

        // HUD / overlays: AntiAlias for readable text and curved power-up glyphs.
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (state.ShowLeaderboard)
        {
            g.FillRectangle(GetCachedBrush(Color.FromArgb(220, 0, 0, 0)), 0, 0, clientWidth, playHeight);
            float statsBottom = DrawFinalResultsPanel(g, uiFont, state, clientWidth, playHeight);
            float gameOverBottom = DrawGameOverTitle(g, uiFont, clientWidth, statsBottom + 10f);
            float leaderboardBottom = DrawLeaderboard(
                g,
                uiFont,
                leaderboard,
                clientWidth,
                playHeight,
                state.Score,
                gameOverBottom + 14f,
                leaderboardHighlightName);
            DrawOverlayActionHint(g, uiFont, clientWidth, playHeight, leaderboardBottom, "Enter: Play again · Menu: title");
        }
        else if (state.IsBrowsingLeaderboard)
        {
            DrawAttractHud(g, uiFont, clientWidth, state);
            g.FillRectangle(GetCachedBrush(Color.FromArgb(200, 0, 0, 0)), 0, 0, clientWidth, playHeight);
            float boardBottom = DrawLeaderboard(
                g,
                uiFont,
                leaderboard,
                clientWidth,
                playHeight,
                currentScore: 0,
                minTopY: Math.Max(28f, playHeight * 0.08f),
                highlightName: null);
            DrawOverlayActionHint(g, uiFont, clientWidth, playHeight, boardBottom, "Esc or click Back");
        }
        else
        {
            bool attract = !state.IsPlaying && !state.IsGameOver;
            if (attract)
                DrawAttractHud(g, uiFont, clientWidth, state);
            else
                DrawHud(g, uiFont, clientWidth, state);

            DrawNewBestBanner(g, uiFont, clientWidth, state);
            if (state.IsDevMode && state.IsPlaying && !state.IsGameOver && !state.IsFinalDeathAnimating)
                DrawDevModeOverlay(g, uiFont, clientWidth, playHeight, state);

            DrawBombScreenFlash(g, clientWidth, playHeight, state);

            if (state.FinalDeathFlashFrames > 0)
            {
                int flashAlpha = Math.Min(200, 28 + state.FinalDeathFlashFrames * 18);
                var fdFlash = GetCachedBrush(Color.FromArgb(flashAlpha, 255, 252, 235));
                g.FillRectangle(fdFlash, 0, 0, clientWidth, playHeight);
            }

            if (state.LifeLostFlashFrames > 0)
            {
                int flashAlpha = Math.Min(170, state.LifeLostFlashFrames * 9);
                var flashOverlay = GetCachedBrush(Color.FromArgb(flashAlpha, 210, 30, 40));
                g.FillRectangle(flashOverlay, 0, 0, clientWidth, playHeight);
            }

            if (state.IsGameOver)
            {
                // Fallback if ShowLeaderboard is false; keep copy in sync with the live path.
                g.FillRectangle(GetCachedBrush(Color.FromArgb(220, 0, 0, 0)), 0, 0, clientWidth, playHeight);
                var goFont = GetCachedFont(uiFont, uiFont.Size + 20f, FontStyle.Bold);
                float cy = playHeight * 0.28f;
                DrawCenteredText(g, "GAME OVER", goFont, GoBrush, clientWidth, cy);
                var statFont = GetCachedFont(uiFont, uiFont.Size + 2f, FontStyle.Bold);
                var statBrush = GetCachedBrush(Color.FromArgb(225, 238, 245));
                DrawCenteredText(g, $"Final Score: {state.Score}", statFont, statBrush, clientWidth, cy + 64f);
                DrawCenteredText(g, $"Best Score:  {state.HighScore}", statFont, statBrush, clientWidth, cy + 96f);
                DrawCenteredText(g, $"Max Combo:  x{Math.Max(1, state.MaxCombo)}", statFont, statBrush, clientWidth, cy + 128f);
                const string hint = "Enter: Play again · Menu: title screen";
                var hintBr = GetCachedBrush(Color.FromArgb(230, 230, 238));
                DrawCenteredText(g, hint, uiFont, hintBr, clientWidth, cy + 176f);
            }
            else if (state.IsLifeLost)
            {
                g.FillRectangle(GetCachedBrush(Color.FromArgb(165, 0, 0, 0)), 0, 0, clientWidth, playHeight);
                var lifeLostFont = GetCachedFont(uiFont, uiFont.Size + 10f, FontStyle.Bold);
                const string msg = "LIFE LOST";
                SizeF sz = g.MeasureString(msg, lifeLostFont);
                float cx = (clientWidth - sz.Width) * 0.5f;
                float cy = playHeight * 0.34f;
                g.DrawString(msg, lifeLostFont, TextBrush, cx, cy);

                DrawLifeIcons(g, 0.5f * (clientWidth - (state.MaxLives * 18 + Math.Max(0, state.MaxLives - 1) * 8)), cy + sz.Height + 14, state.MaxLives, 16, state.Lives);

                const string hint = "Press Enter/Space or click Continue";
                var hintBr = GetCachedBrush(Color.FromArgb(230, 230, 238));
                float hintW = g.MeasureString(hint, uiFont).Width;
                g.DrawString(hint, uiFont, hintBr, (clientWidth - hintW) * 0.5f, cy + sz.Height + 44f);
            }
            else if (state.IsPaused)
            {
                var overlay = GetCachedBrush(Color.FromArgb(170, 0, 0, 0));
                g.FillRectangle(overlay, 0, 0, clientWidth, playHeight);
                var pausedFont = GetCachedFont(uiFont, uiFont.Size + 18f, FontStyle.Bold);
                var hintFont = GetCachedFont(uiFont, uiFont.Size + 1f, FontStyle.Regular);
                var hintBr = GetCachedBrush(Color.FromArgb(230, 230, 238));
                float cy = playHeight * 0.34f;
                DrawCenteredText(g, "PAUSED", pausedFont, TextBrush, clientWidth, cy);
                DrawCenteredText(g, "Press ESC to continue", hintFont, hintBr, clientWidth, cy + 56f);
            }
            else if (attract)
            {
                DrawAttractOverlay(g, uiFont, clientWidth, playHeight);
            }
        }
        if (showDebug && !state.ShowLeaderboard && !state.IsBrowsingLeaderboard)
        {
            var dbgFont = GetCachedFont(uiFont, 7.5f, FontStyle.Regular);
            var dbgBr = GetCachedBrush(Color.FromArgb(130, 160, 170, 190));
            g.DrawString(
                $"FPS {debugFps}  dt {state.LastDeltaSeconds * 1000f:0.#}ms  bars {entities.Bars.Count}  bul {entities.Bullets.Count}  ptcl {entities.Particles.Count}  frag {entities.Fragments.Count}  boom {entities.Explosions.Count}  fuse {state.BombFuseFramesLeft}",
                dbgFont,
                dbgBr,
                8,
                playHeight - 28);
            g.DrawRectangle(DebugPenPlayer, player.GetBounds());
            foreach (var bar in entities.Bars) g.DrawRectangle(DebugPenEnemy, bar.GetBounds());
            foreach (var b in entities.Bullets) g.DrawRectangle(DebugPenBullet, b.GetBounds());
            if (state.HasPendingBombPickup)
            {
                var fuseDbg = GetCachedPen(Color.Lime);
                float ix = state.BombIndicatorX;
                float iy = state.BombIndicatorY;
                g.DrawLine(fuseDbg, ix - 8f, iy, ix + 8f, iy);
                g.DrawLine(fuseDbg, ix, iy - 8f, ix, iy + 8f);
            }
            var boomMark = GetCachedPen(Color.FromArgb(220, 255, 255, 80));
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
        var font = GetCachedFont(uiFont, 7.5f, FontStyle.Bold);
        var accent = GetCachedBrush(Color.FromArgb(255, 120, 255, 140));
        var dim = GetCachedBrush(Color.FromArgb(210, 200, 220, 210));
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
        var core = GetCachedBrush(Color.FromArgb(alpha, 255, 220, 70));
        float r = 8f + 4f * pulse;
        g.FillEllipse(core, x - r, y - r, r * 2f, r * 2f);
        g.DrawEllipse(BombFuseRimPen, x - r, y - r, r * 2f, r * 2f);
        float warnR = 16f;
        g.DrawEllipse(BombFuseWarnPen, x - warnR, y - warnR, warnR * 2f, warnR * 2f);
    }

    private static void DrawBombScreenFlash(Graphics g, int clientWidth, int playHeight, GameState state)
    {
        int f = state.BombScreenFlashFrames;
        if (f <= 0) return;
        const int maxF = 14;
        float t = Math.Clamp(f / (float)maxF, 0f, 1f);
        int alpha = Math.Clamp((int)(235 * t), 0, 235);
        int warm = (int)(200 + 40 * t);
        var flashBr = GetCachedBrush(Color.FromArgb(alpha, 255, 255, warm));
        g.FillRectangle(flashBr, 0, 0, clientWidth, playHeight);
    }

    private static void DrawBulletTrails(Graphics g, Bullet bullet)
    {
        Rectangle r = bullet.GetBounds();
        int cx = r.X + r.Width / 2;
        int bottom = r.Y + r.Height;
        if (bullet.IsPiercingVisual)
        {
            int tw = Math.Max(r.Width + 3, 7);
            int x = cx - tw / 2;
            g.FillRectangle(BulletTrailPierceNearBrush, x, bottom + 3, tw, 3);
            g.FillRectangle(BulletTrailPierceFarBrush, x + 1, bottom + 8, tw - 2, 4);
        }
        else
        {
            int tw = Math.Max(r.Width + 4, 8);
            int x = cx - tw / 2;
            g.FillRectangle(BulletTrailNearBrush, x, bottom + 2, tw, 3);
            g.FillRectangle(BulletTrailFarBrush, x + 1, bottom + 7, tw - 2, 3);
        }
    }

    private static void DrawBulletBody(Graphics g, Bullet bullet)
    {
        Rectangle r = bullet.GetBounds();
        if (bullet.IsPiercingVisual)
        {
            Rectangle outer = Rectangle.Inflate(r, 3, 3);
            g.FillRectangle(PierceBulletOuterGlowBrush, outer);
            Rectangle mid = Rectangle.Inflate(r, 1, 1);
            g.FillRectangle(PierceBulletGlow, mid);
            g.FillRectangle(PierceBulletCore, r);
            int capH = Math.Max(4, r.Width / 2 + 2);
            g.FillEllipse(PierceBulletCore, r.X - 1, r.Y - 2, r.Width + 2, capH + 2);
        }
        else
        {
            Rectangle outer = Rectangle.Inflate(r, 2, 2);
            g.FillRectangle(BulletOuterGlowBrush, outer);
            g.FillRectangle(BulletCoreBrush, r);
            int capH = Math.Max(4, r.Width / 2 + 2);
            g.FillEllipse(BulletCoreBrush, r.X - 1, r.Y - 2, r.Width + 2, capH + 2);
        }
    }

    /// <summary>
    /// Readable ground turret inside the player AABB: wide chassis, thicker centered barrel, gold muzzle.
    /// One T-silhouette outline (no nested neon boxes). Panel light + aim line; shoot flash uses <see cref="GameState.MuzzleFlashFrames"/>.
    /// </summary>
    private static void DrawPlayerCannon(Graphics g, Player player, GameState state)
    {
        Rectangle bounds = player.GetBounds();
        int pw = bounds.Width;
        int ph = bounds.Height;
        int px = bounds.X;
        int py = bounds.Y;

        // Chassis: bottom ~⅔ of the hitbox, almost full width.
        int hullH = (ph * 2) / 3;
        if (hullH < 8) hullH = 8;
        if (hullH > ph - 4) hullH = ph - 4;
        int hullW = pw - 2;
        int hullX = px + (pw - hullW) / 2;
        int hullY = py + ph - hullH;

        // Barrel: thicker (~10–12 px), centered, from hitbox top down into the hull.
        int barrelW = Math.Clamp(pw / 4, 10, 12);
        int barrelX = px + (pw - barrelW) / 2;
        int barrelY = py;
        int barrelH = hullY - py + (hullH / 2);
        if (barrelH < 6) barrelH = 6;

        g.FillRectangle(CannonHullBrush, hullX, hullY, hullW, hullH);
        // Darker under-lip so the chassis reads as grounded.
        int lipH = Math.Min(2, hullH);
        g.FillRectangle(CannonHullLipBrush, hullX, hullY + hullH - lipH, hullW, lipH);
        g.FillRectangle(CannonBarrelBrush, barrelX, barrelY, barrelW, barrelH);

        // Small panel light on the hull (idle gold; brighter while firing).
        const int panelW = 5;
        const int panelH = 3;
        int panelX = hullX + (hullW - panelW) / 2;
        int panelY = hullY + Math.Max(2, (hullH - panelH) / 2);
        if (state.MuzzleFlashFrames > 0)
            g.FillRectangle(CannonPanelFlashBrush, panelX, panelY, panelW, panelH);
        else
            g.FillRectangle(CannonPanelBrush, panelX, panelY, panelW, panelH);

        DrawCannonSilhouetteOutline(g, CannonOutlinePen, hullX, hullY, hullW, hullH, barrelX, barrelY, barrelW);

        if (state.MuzzleFlashFrames > 0)
        {
            Pen pulsePen = GetCachedPen(Color.FromArgb(255, 220, 255, 255), 2f, join: LineJoin.Miter);
            DrawCannonSilhouetteOutline(g, pulsePen, hullX, hullY, hullW, hullH, barrelX, barrelY, barrelW);
        }

        Rectangle muzzle = player.GetMuzzlePortRect();
        g.FillRectangle(MuzzlePortBrush, muzzle);
        int muzzleX = player.MuzzleTopCenter.X;
        g.DrawLine(BulletAimPen, muzzleX, muzzle.Top, muzzleX, muzzle.Top - 7);

        if (state.MuzzleFlashFrames > 0)
        {
            SolidBrush flash = GetCachedBrush(Color.FromArgb(235, 255, 255, 255));
            g.FillRectangle(flash, muzzleX - 8, muzzle.Top - 12, 16, 12);
        }
    }

    /// <summary>Outer T outline for hull+barrel (allocation-free line segments).</summary>
    private static void DrawCannonSilhouetteOutline(
        Graphics g,
        Pen pen,
        int hullX,
        int hullY,
        int hullW,
        int hullH,
        int barrelX,
        int barrelY,
        int barrelW)
    {
        int hullRight = hullX + hullW - 1;
        int hullBottom = hullY + hullH - 1;
        int barrelRight = barrelX + barrelW - 1;
        int barrelTop = barrelY;

        // Top of barrel, then down to hull deck, out to hull sides, around the chassis, back up the barrel.
        g.DrawLine(pen, barrelX, barrelTop, barrelRight, barrelTop);
        g.DrawLine(pen, barrelRight, barrelTop, barrelRight, hullY);
        g.DrawLine(pen, barrelRight, hullY, hullRight, hullY);
        g.DrawLine(pen, hullRight, hullY, hullRight, hullBottom);
        g.DrawLine(pen, hullRight, hullBottom, hullX, hullBottom);
        g.DrawLine(pen, hullX, hullBottom, hullX, hullY);
        g.DrawLine(pen, hullX, hullY, barrelX, hullY);
        g.DrawLine(pen, barrelX, hullY, barrelX, barrelTop);
    }

    /// <summary>
    /// Full-width violet-lavender floor band while shield is active; pickup wash; floor-centered block ripple.
    /// Drawn behind the cannon (world-anchored). Soft rect + thin top edge (no dome arc). Quiet idle; louder on events.
    /// </summary>
    private static void DrawShieldPlayerFx(Graphics g, Player player, GameState state, int clientWidth, int playHeight)
    {
        bool passive = state.HasShieldActive;
        bool pickup = state.ShieldPickupFlashFrames > 0;
        bool ring = state.ShieldBlockRingFrames > 0;
        bool flash = state.ShieldBlockFlashFrames > 0;
        if (!passive && !pickup && !ring && !flash) return;

        // Band sits below the danger line (~playHeight - player.Height - 28).
        const float BandExtraPx = 18f;
        float pulse = 0.95f + 0.05f * MathF.Sin(state.ElapsedFrames * 0.28f);
        float bandH = (player.Height + BandExtraPx) * pulse;
        float bandTop = playHeight - bandH;
        if (bandTop < 0f) bandTop = 0f;

        if (passive)
        {
            int bandAlpha = (int)(22 + 8 * pulse);
            SolidBrush bandFill = GetCachedBrush(Color.FromArgb(Math.Clamp(bandAlpha, 18, 36), 95, 70, 190));
            g.FillRectangle(bandFill, 0, bandTop, clientWidth, playHeight - bandTop);

            // One faint glow line above the edge — not a stack that fights the danger line.
            ShieldGlowPen.Color = Color.FromArgb(28, 110, 90, 220);
            ShieldGlowPen.Width = 1.2f;
            g.DrawLine(ShieldGlowPen, 0, bandTop - 1.5f, clientWidth, bandTop - 1.5f);

            ShieldEdgePen.Color = Color.FromArgb(210, 190, 170, 255);
            ShieldEdgePen.Width = 1.6f + 0.2f * pulse;
            g.DrawLine(ShieldEdgePen, 0, bandTop, clientWidth, bandTop);
        }

        if (pickup)
        {
            float intensity = Math.Clamp(state.ShieldPickupFlashFrames / 22f, 0f, 1f);
            int a = (int)(45 + 100 * intensity);
            SolidBrush wash = GetCachedBrush(Color.FromArgb(Math.Clamp(a, 0, 180), 130, 100, 230));
            float amp = 4f + 10f * intensity;
            g.FillRectangle(wash, 0, bandTop - amp, clientWidth, playHeight - (bandTop - amp));
        }

        if (ring)
        {
            const int ringMax = 18;
            float progress = 1f - Math.Clamp(state.ShieldBlockRingFrames / (float)ringMax, 0f, 1f);
            float e = progress * progress * (3f - 2f * progress);
            float radius = 18f + e * 90f;
            float cx = state.ShieldBlockImpactX;
            float cy = playHeight;
            int a = (int)(220 * (1f - progress * 0.75f) + 20);
            a = Math.Clamp(a, 35, 245);
            float rx = radius * 1.55f;
            float ry = radius * 0.42f;
            ShieldBlockOuterPen.Color = Color.FromArgb(a, 170, 140, 255);
            g.DrawEllipse(ShieldBlockOuterPen, cx - rx, cy - ry, rx * 2f, ry * 2f);
            float rx2 = rx * 0.5f;
            float ry2 = ry * 0.5f;
            ShieldBlockInnerPen.Color = Color.FromArgb(Math.Clamp(a + 20, 0, 255), 230, 220, 255);
            g.DrawEllipse(ShieldBlockInnerPen, cx - rx2, cy - ry2, rx2 * 2f, ry2 * 2f);
        }

        if (flash)
        {
            float f = Math.Clamp(state.ShieldBlockFlashFrames / 14f, 0f, 1f);
            int a = (int)(100 * f);
            SolidBrush core = GetCachedBrush(Color.FromArgb(Math.Clamp(a, 0, 130), 160, 130, 240));
            g.FillRectangle(core, 0, bandTop, clientWidth, playHeight - bandTop);
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

            // Filled core for punch; strokes for expanding rings.
            float fillR = Math.Max(6f, radius * 0.28f);
            int fillA = Math.Clamp((int)(baseA * 0.55f), 40, 200);
            ExplosionCoreFillBrush.Color = Color.FromArgb(fillA, 255, 210, 90);
            g.FillEllipse(ExplosionCoreFillBrush, ex.X - fillR, ex.Y - fillR, fillR * 2f, fillR * 2f);

            ExplosionOuterPen.Color = Color.FromArgb((int)(baseA * 0.42f), 255, 55, 30);
            g.DrawEllipse(ExplosionOuterPen, ex.X - radius, ex.Y - radius, radius * 2f, radius * 2f);

            float rMid = Math.Max(8f, radius * 0.64f);
            ExplosionMidPen.Color = Color.FromArgb((int)(baseA * 0.78f), 255, 150, 45);
            g.DrawEllipse(ExplosionMidPen, ex.X - rMid, ex.Y - rMid, rMid * 2f, rMid * 2f);

            float rCore = Math.Max(5f, radius * 0.36f);
            ExplosionCorePen.Color = Color.FromArgb(Math.Min(255, baseA + 25), 255, 255, 230);
            g.DrawEllipse(ExplosionCorePen, ex.X - rCore, ex.Y - rCore, rCore * 2f, rCore * 2f);
        }
    }

    private static void DrawImpactFlash(Graphics g, GameState state)
    {
        if (state.ImpactFlashFrames <= 0) return;
        float t = state.ImpactFlashFrames / (float)GameConfig.Effects.ImpactFlashFrames;
        int alpha = Math.Clamp((int)(80 + 175 * t), 80, 255);
        ImpactFlashBrush.Color = Color.FromArgb(alpha, 255, 250, 210);
        float cx = state.ImpactFlashX;
        float cy = state.ImpactFlashY;
        float arm = 5f + 4f * t;
        g.FillRectangle(ImpactFlashBrush, cx - arm, cy - 1.5f, arm * 2f, 3f);
        g.FillRectangle(ImpactFlashBrush, cx - 1.5f, cy - arm, 3f, arm * 2f);
        float core = 3f + 2f * t;
        g.FillRectangle(ImpactFlashBrush, cx - core * 0.5f, cy - core * 0.5f, core, core);
    }

    private static void DrawAttractOverlay(Graphics g, Font uiFont, int clientWidth, int playHeight)
    {
        var title = GetCachedFont(uiFont, 22f, FontStyle.Bold);
        const string t = "READY?";
        SizeF tsz = g.MeasureString(t, title);
        // Keep title clear of the taller Start + Scores stack at the bottom.
        float titleY = playHeight * 0.30f;
        g.DrawString(t, title, TextBrush, (clientWidth - tsz.Width) * 0.5f, titleY);

        var startFont = GetCachedFont(uiFont, uiFont.Size + 1.5f, FontStyle.Bold);
        const string startHint = "Enter or click Start · Scores for leaderboard";
        SizeF startSz = g.MeasureString(startHint, startFont);
        float startY = titleY + tsz.Height + 10f;
        g.DrawString(startHint, startFont, AttractHintBrush, (clientWidth - startSz.Width) * 0.5f, startY);

        var controlsFont = GetCachedFont(uiFont, uiFont.Size, FontStyle.Regular);
        const string controls = "Move: arrows / A D   ·   Fire: Space   ·   Pause: ESC";
        SizeF ctrlSz = g.MeasureString(controls, controlsFont);
        g.DrawString(controls, controlsFont, AttractHintBrush, (clientWidth - ctrlSz.Width) * 0.5f, startY + startSz.Height + 12f);
    }

    /// <summary>
    /// Places an action hint in the band above the WinForms button stack so it cannot collide with buttons.
    /// </summary>
    private static void DrawOverlayActionHint(
        Graphics g,
        Font uiFont,
        int clientWidth,
        int playHeight,
        float contentBottom,
        string hint)
    {
        const float buttonStackHeight = 114f;
        float hintMaxY = playHeight - buttonStackHeight - 22f;
        float hintY = Math.Min(contentBottom + 10f, hintMaxY);
        hintY = Math.Max(contentBottom + 4f, hintY);
        var hintBr = GetCachedBrush(Color.FromArgb(230, 230, 238));
        DrawCenteredText(g, hint, uiFont, hintBr, clientWidth, hintY);
    }

    /// <summary>Minimal attract HUD: BEST only so READY stays the focus.</summary>
    private static void DrawAttractHud(Graphics g, Font uiFont, int clientWidth, GameState state)
    {
        var bestBr = GetCachedBrush(Color.FromArgb(140, 176, 188));
        var bestFont = GetCachedFont(uiFont, uiFont.Size, FontStyle.Regular);
        string bestText = $"BEST {state.HighScore}";
        SizeF bestSize = g.MeasureString(bestText, bestFont);
        g.DrawString(bestText, bestFont, bestBr, clientWidth - bestSize.Width - 18f, 14f);
    }

    private static void DrawHud(Graphics g, Font uiFont, int clientWidth, GameState state)
    {
        if (!state.IsLifeLost)
            DrawLives(g, state);
        DrawScore(g, state, uiFont, clientWidth);
        DrawBestScore(g, state, uiFont, clientWidth);
        DrawPowerUpHud(g, state, uiFont, clientWidth);
        DrawComboHud(g, uiFont, clientWidth, state);
    }

    private static void DrawComboHud(Graphics g, Font uiFont, int clientWidth, GameState state)
    {
        int m = state.ComboMultiplier;
        bool hot = state.ComboStreak > 1;
        var comboBr = GetCachedBrush(hot
            ? Color.FromArgb(255, 255, 165, 70)
            : Color.FromArgb(120, 176, 188, 200));
        float sizeBoost = hot ? 4f : 0f;
        FontStyle style = hot ? FontStyle.Bold : FontStyle.Regular;
        var comboFont = GetCachedFont(uiFont, uiFont.Size + sizeBoost, style);
        if (m != _hudComboMultCached || hot != _hudComboHotCached)
        {
            _hudComboMultCached = m;
            _hudComboHotCached = hot;
            _hudComboText = $"COMBO x{m}";
            _hudComboWidth = g.MeasureString(_hudComboText, comboFont).Width;
        }
        float cx = clientWidth - _hudComboWidth - 18;
        g.DrawString(_hudComboText, comboFont, comboBr, cx, 92);
    }

    private static void DrawScorePopups(Graphics g, Font uiFont, IReadOnlyList<ScorePopup> popups)
    {
        if (popups.Count == 0) return;
        var font = GetCachedFont(uiFont, uiFont.Size + 4f, FontStyle.Bold);
        float charW = font.Size * 0.74f;
        for (int i = 0; i < popups.Count; i++)
        {
            ScorePopup p = popups[i];
            float t = p.LifeT;
            int alpha = (int)Math.Clamp(90 + t * 165f, 90, 255);
            ScorePopupBrush.Color = Color.FromArgb(alpha, 255, 235, 130);
            float textW = p.Text.Length * charW;
            float textH = font.Size * 1.4f;
            g.DrawString(p.Text, font, ScorePopupBrush, p.X - textW * 0.5f, p.Y - textH * 0.5f);
        }
    }

    private static void DrawLives(Graphics g, GameState state)
    {
        const float x = 12f;
        const float y = 12f;
        var labelBr = GetCachedBrush(Color.FromArgb(170, 190, 205));
        var labelFont = Ui8RegularFont;
        g.DrawString("LIVES", labelFont, labelBr, x, y - 1);
        DrawLifeIcons(g, x + 46, y + 2, state.MaxLives, 12, state.Lives);

        // Shield charge pip when protection remains but the HUD card shows another buff.
        bool showShieldPip = state.HasShieldActive && state.ActivePowerUp is not PowerUpType.Shield;
        if (!showShieldPip) return;

        float pipX = x + 46 + state.MaxLives * (12 + 8) + 4;
        float pipY = y + 1;
        const float pip = 14f;
        Color c = GetPowerUpColor(PowerUpType.Shield);
        var halo = GetCachedBrush(Color.FromArgb(90, c));
        g.FillEllipse(halo, pipX - 1f, pipY - 1f, pip + 2f, pip + 2f);
        DrawPowerUpGlyph(g, PowerUpType.Shield, c, pipX + 2f, pipY + 2f, pip - 4f, pip - 4f);
    }

    private static void DrawScore(Graphics g, GameState state, Font uiFont, int clientWidth)
    {
        var labelBr = GetCachedBrush(Color.FromArgb(175, 190, 205));
        var scoreBr = GetCachedBrush(Color.FromArgb(235, 245, 255));
        var labelFont = GetCachedFont(uiFont, uiFont.Size - 1f, FontStyle.Regular);
        var scoreFont = GetCachedFont(uiFont, uiFont.Size + 6f, FontStyle.Bold);
        const string label = "SCORE";
        if (state.Score != _hudScoreCached)
        {
            _hudScoreCached = state.Score;
            _hudScoreText = state.Score.ToString();
            _hudScoreWidth = g.MeasureString(_hudScoreText, scoreFont).Width;
        }
        float boxW = Math.Max(110f, _hudScoreWidth + 22f);
        float boxH = 48f;
        float boxX = clientWidth - boxW - 12f;
        float boxY = 10f;
        var boxBr = GetCachedBrush(Color.FromArgb(95, 8, 16, 28));
        var boxPen = GetCachedPen(Color.FromArgb(120, 75, 95, 125));
        g.FillRectangle(boxBr, boxX, boxY, boxW, boxH);
        g.DrawRectangle(boxPen, boxX, boxY, boxW - 1, boxH - 1);
        g.DrawString(label, labelFont, labelBr, boxX + 10, boxY + 4);
        g.DrawString(_hudScoreText, scoreFont, scoreBr, boxX + 10, boxY + 16);
    }

    private static void DrawBestScore(Graphics g, GameState state, Font uiFont, int clientWidth)
    {
        var bestBr = GetCachedBrush(Color.FromArgb(165, 176, 188));
        var bestFont = GetCachedFont(uiFont, uiFont.Size, FontStyle.Regular);
        if (state.HighScore != _hudBestCached)
        {
            _hudBestCached = state.HighScore;
            _hudBestText = $"BEST {state.HighScore}";
            _hudBestWidth = g.MeasureString(_hudBestText, bestFont).Width;
        }
        g.DrawString(_hudBestText, bestFont, bestBr, clientWidth - _hudBestWidth - 18f, 62f);
    }

    private static void DrawNewBestBanner(Graphics g, Font uiFont, int clientWidth, GameState state)
    {
        if (!state.IsNewBestThisRun || state.NewBestFlashFrames <= 0) return;
        float t = state.NewBestFlashFrames / 150f;
        int alpha = 120 + (int)(120f * Math.Min(1f, t + 0.1f));
        float rise = (1f - t) * 10f;
        // Sit under the centered power-up card (y=8, h=62) so the two never stack.
        float y = 76f - rise;

        var back = GetCachedBrush(Color.FromArgb(Math.Min(220, alpha), 40, 22, 0));
        var text = GetCachedBrush(Color.FromArgb(Math.Min(255, alpha + 20), 255, 210, 85));
        var font = GetCachedFont(uiFont, uiFont.Size + 5f, FontStyle.Bold);
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
            Color c = GetPowerUpColor(p.Type);
            PowerUpBrush.Color = c;
            g.FillEllipse(PowerUpBrush, r);
            var pen = GetCachedPen(Color.FromArgb(200, 255, 255, 255), 1.2f);
            g.DrawEllipse(pen, r);
            float pad = Math.Max(2f, r.Width * 0.18f);
            DrawPowerUpGlyph(
                g,
                p.Type,
                Color.FromArgb(255, 255, 255, 255),
                r.X + pad,
                r.Y + pad,
                r.Width - pad * 2f,
                r.Height - pad * 2f);
        }
    }

    private static void DrawPowerUpHud(Graphics g, GameState state, Font uiFont, int clientWidth)
    {
        const float blockW = 78f;
        const float blockH = 62f;
        const float iconSize = 36f;
        float blockX = (clientWidth - blockW) * 0.5f;
        float blockY = 8f;
        var panel = GetCachedBrush(Color.FromArgb(92, 10, 18, 28));
        var panelBorder = GetCachedPen(Color.FromArgb(130, 75, 95, 125));
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
            var empty = GetCachedBrush(Color.FromArgb(55, 85, 96, 112));
            var emptyBorder = GetCachedPen(Color.FromArgb(100, 95, 108, 128));
            g.FillEllipse(empty, iconX, iconY, sizeW, sizeH);
            g.DrawEllipse(emptyBorder, iconX, iconY, sizeW - 1f, sizeH - 1f);
            var dash = GetCachedPen(Color.FromArgb(140, 180, 195, 210), 1.2f, dash: DashStyle.Dot);
            float cx = iconX + sizeW * 0.5f;
            float cy = iconY + sizeH * 0.5f;
            g.DrawLine(dash, cx - 8f, cy, cx + 8f, cy);
            return;
        }

        var t = state.ActivePowerUp.Value;
        Color c = GetPowerUpColor(t);
        var halo = GetCachedBrush(Color.FromArgb(95, c));
        g.FillEllipse(halo, iconX - 2f, iconY - 2f, sizeW + 4f, sizeH + 4f);
        var border = GetCachedPen(Color.FromArgb(255, 255, 255, 255), 2f);
        g.DrawEllipse(border, iconX - 1f, iconY - 1f, sizeW + 1f, sizeH + 1f);
        DrawPowerUpGlyph(g, t, c, iconX + 4f, iconY + 4f, sizeW - 8f, sizeH - 8f);

        string label = GetPowerUpHudLabel(t);
        var labelBr = GetCachedBrush(Color.FromArgb(248, 252, 255));
        var labelFont = Ui7BoldFont;
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
            var bg = GetCachedBrush(Color.FromArgb(110, 30, 40, 55));
            var fg = GetCachedBrush(Color.FromArgb(230, c));
            g.FillRectangle(bg, panelX + barPad, barY, barW, 4f);
            g.FillRectangle(fg, panelX + barPad, barY, barW * r, 4f);
        }
    }

    /// <summary>Distinct simple glyphs so each power-up reads at a glance.</summary>
    private static void DrawPowerUpGlyph(Graphics g, PowerUpType type, Color c, float x, float y, float w, float h)
    {
        float cx = x + w * 0.5f;
        float cy = y + h * 0.5f;
        var fill = GetCachedBrush(c);
        var outline = GetCachedPen(Color.FromArgb(220, 20, 20, 30), 1.2f);

        switch (type)
        {
            case PowerUpType.RapidFire:
            {
                g.FillEllipse(fill, cx - w * 0.35f, cy - h * 0.35f, w * 0.7f, h * 0.7f);
                var streak = GetCachedPen(Color.FromArgb(255, 255, 240, 200), 2f, startCap: LineCap.Round, endCap: LineCap.Round);
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
                PointF[] shield =
                [
                    new(bx + bw * 0.08f, by + bh * 0.2f),
                    new(bx + bw * 0.32f, by + bh * 0.02f),
                    new(bx + bw * 0.68f, by + bh * 0.02f),
                    new(bx + bw * 0.92f, by + bh * 0.2f),
                    new(bx + bw * 0.5f, y + h - 3f)
                ];
                g.FillPolygon(fill, shield);
                g.DrawPolygon(outline, shield);
                break;
            }
            case PowerUpType.SlowMotion:
            {
                g.FillEllipse(fill, cx - w * 0.38f, cy - h * 0.38f, w * 0.76f, h * 0.76f);
                var sweep = GetCachedPen(Color.FromArgb(255, 255, 255, 255), 2.2f, startCap: LineCap.Round);
                g.DrawArc(sweep, cx - w * 0.32f, cy - h * 0.32f, w * 0.64f, h * 0.64f, 200f, 220f);
                var tick = GetCachedPen(Color.FromArgb(255, 255, 255, 255), 1.8f, startCap: LineCap.Round, endCap: LineCap.Round);
                g.DrawLine(tick, cx, cy, cx + w * 0.22f, cy - h * 0.18f);
                break;
            }
            case PowerUpType.BombShot:
            {
                g.FillEllipse(fill, cx - w * 0.32f, cy - h * 0.25f, w * 0.64f, h * 0.55f);
                var fuse = GetCachedPen(Color.FromArgb(255, 255, 230, 160), 2f, startCap: LineCap.Round, endCap: LineCap.Round);
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
        PowerUpType.Shield => Color.FromArgb(255, 175, 150, 245),
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
        var fill = GetCachedBrush(active ? Color.FromArgb(244, 72, 94) : Color.FromArgb(78, 78, 92));
        var outline = GetCachedPen(Color.FromArgb(170, 0, 0, 0));

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

    private static float DrawFinalResultsPanel(
        Graphics g,
        Font uiFont,
        GameState state,
        int clientWidth,
        int playHeight)
    {
        float panelW = Math.Min(380f, clientWidth - 44f);
        float panelH = 128f;
        float panelX = (clientWidth - panelW) * 0.5f;
        float panelY = Math.Max(24f, playHeight * 0.08f);
        DrawPanelFrame(g, panelX, panelY, panelW, panelH);

        var titleFont = GetCachedFont(uiFont, uiFont.Size + 3f, FontStyle.Bold);
        var titleBr = GetCachedBrush(Color.FromArgb(245, 230, 245, 255));
        DrawCenteredText(g, "FINAL RESULTS", titleFont, titleBr, clientWidth, panelY + 10f);
        var separatorPen = GetCachedPen(Color.FromArgb(170, 95, 205, 255));
        g.DrawLine(separatorPen, panelX + 14f, panelY + 38f, panelX + panelW - 14f, panelY + 38f);

        float labelX = panelX + 18f;
        float valueRightX = panelX + panelW - 18f;
        float rowY = panelY + 48f;
        float rowStep = 24f;
        DrawStatRow(g, uiFont, "Score", state.Score.ToString(), labelX, valueRightX, rowY, state.IsNewBestThisRun);
        DrawStatRow(g, uiFont, "Best Score", state.HighScore.ToString(), labelX, valueRightX, rowY + rowStep, false);
        DrawStatRow(g, uiFont, "Max Combo", $"x{Math.Max(1, state.MaxCombo)}", labelX, valueRightX, rowY + rowStep * 2f, false);
        return panelY + panelH;
    }

    private static float DrawGameOverTitle(Graphics g, Font uiFont, int clientWidth, float startY)
    {
        var titleFont = GetCachedFont(uiFont, uiFont.Size + 16f, FontStyle.Bold);
        const string title = "GAME OVER";
        SizeF titleSize = g.MeasureString(title, titleFont);
        float y = startY;
        float x = (clientWidth - titleSize.Width) * 0.5f;

        var glowBr = GetCachedBrush(Color.FromArgb(80, 255, 110, 70));
        g.DrawString(title, titleFont, glowBr, x - 2f, y);
        g.DrawString(title, titleFont, glowBr, x + 2f, y);
        g.DrawString(title, titleFont, glowBr, x, y - 2f);
        g.DrawString(title, titleFont, glowBr, x, y + 2f);

        var coreBr = GetCachedBrush(Color.FromArgb(255, 255, 135, 80));
        g.DrawString(title, titleFont, coreBr, x, y);

        float lineY = y + titleSize.Height * 0.55f;
        float margin = 18f;
        float gap = 14f;
        float leftStart = margin;
        float leftEnd = x - gap;
        float rightStart = x + titleSize.Width + gap;
        float rightEnd = clientWidth - margin;
        var linePen = GetCachedPen(Color.FromArgb(140, 255, 120, 80), 1.4f);
        if (leftEnd - leftStart > 10f) g.DrawLine(linePen, leftStart, lineY, leftEnd, lineY);
        if (rightEnd - rightStart > 10f) g.DrawLine(linePen, rightStart, lineY, rightEnd, lineY);

        return y + titleSize.Height + 8f;
    }

    private static float DrawLeaderboard(
        Graphics g,
        Font uiFont,
        IReadOnlyList<HighScoreStore.LeaderboardEntry> leaderboard,
        int clientWidth,
        int playHeight,
        int currentScore,
        float minTopY,
        string? highlightName = null)
    {
        // Reserve room for primary+secondary WinForms buttons plus a short on-canvas hint band.
        const float reservedBottom = 148f;
        float maxBottomY = playHeight - reservedBottom;
        float panelW = Math.Min(380f, clientWidth - 44f);
        float rowH = 22f;
        float availableHeight = Math.Max(96f, maxBottomY - minTopY);
        int maxRowsByHeight = Math.Max(1, (int)MathF.Floor((availableHeight - 86f) / rowH));
        int rows = Math.Min(Math.Min(leaderboard.Count, 10), maxRowsByHeight);
        float panelH = 70f + Math.Max(1, rows) * rowH + 16f;

        // Prefer anchoring under content above; never slide upward past minTopY into titles.
        float panelY = minTopY;
        if (panelY + panelH > maxBottomY)
        {
            // Shrink rows until the panel fits in the remaining band.
            float fitH = Math.Max(96f, maxBottomY - minTopY);
            maxRowsByHeight = Math.Max(1, (int)MathF.Floor((fitH - 86f) / rowH));
            rows = Math.Min(Math.Min(leaderboard.Count, 10), maxRowsByHeight);
            panelH = 70f + Math.Max(1, rows) * rowH + 16f;
            panelY = minTopY;
            if (panelY + panelH > maxBottomY)
                panelY = Math.Max(minTopY, maxBottomY - panelH);
        }

        float panelX = (clientWidth - panelW) * 0.5f;
        DrawPanelFrame(g, panelX, panelY, panelW, panelH);

        var titleFont = GetCachedFont(uiFont, uiFont.Size + 4f, FontStyle.Bold);
        var titleBr = GetCachedBrush(Color.FromArgb(245, 230, 245, 255));
        DrawCenteredText(g, "LEADERBOARD", titleFont, titleBr, clientWidth, panelY + 12f);
        var separatorPen = GetCachedPen(Color.FromArgb(170, 95, 205, 255));
        g.DrawLine(separatorPen, panelX + 14f, panelY + 42f, panelX + panelW - 14f, panelY + 42f);

        float rankNameX = panelX + 18f;
        float scoreRightX = panelX + panelW - 18f;
        float rowsY = panelY + 50f;

        var headerFont = GetCachedFont(uiFont, uiFont.Size - 0.5f, FontStyle.Bold);
        var headerBr = GetCachedBrush(Color.FromArgb(205, 200, 220, 238));
        g.DrawString("RANK  NAME", headerFont, headerBr, rankNameX, rowsY);
        string scoreHdr = "SCORE";
        float scoreHdrW = g.MeasureString(scoreHdr, headerFont).Width;
        g.DrawString(scoreHdr, headerFont, headerBr, scoreRightX - scoreHdrW, rowsY);

        if (leaderboard.Count == 0)
        {
            var emptyBr = GetCachedBrush(Color.FromArgb(225, 225, 232, 242));
            DrawCenteredText(g, "No scores yet", uiFont, emptyBr, clientWidth, rowsY + 26f);
            return panelY + panelH;
        }

        bool highlightedCurrent = false;
        float y = rowsY + 22f;
        for (int i = 0; i < rows; i++)
        {
            var entry = leaderboard[i];
            bool scoreMatch = entry.Score == currentScore && currentScore > 0;
            bool nameMatch = !string.IsNullOrWhiteSpace(highlightName)
                && string.Equals(entry.Name, highlightName, StringComparison.OrdinalIgnoreCase);
            bool isCurrent = !highlightedCurrent && scoreMatch && (nameMatch || highlightName is null);
            if (isCurrent) highlightedCurrent = true;
            bool isTop = i == 0;

            if (isCurrent)
            {
                var rowHighlight = GetCachedBrush(Color.FromArgb(80, 70, 190, 255));
                g.FillRectangle(rowHighlight, panelX + 10f, y - 1f, panelW - 20f, rowH - 2f);
            }

            string rankAndName = $"{i + 1,2}. {TrimName(entry.Name, 14)}";
            string scoreText = entry.Score.ToString();

            var rowFont = GetCachedFont(uiFont, uiFont.Size, isCurrent || isTop ? FontStyle.Bold : FontStyle.Regular);
            var rowBrush = GetCachedBrush(
                isCurrent
                    ? Color.FromArgb(255, 175, 245, 255)
                    : isTop
                        ? Color.FromArgb(255, 255, 225, 120)
                        : Color.FromArgb(235, 235, 242, 250));

            g.DrawString(rankAndName, rowFont, rowBrush, rankNameX, y);
            float scoreWidth = g.MeasureString(scoreText, rowFont).Width;
            g.DrawString(scoreText, rowFont, rowBrush, scoreRightX - scoreWidth, y);
            y += rowH;
        }

        return panelY + panelH;
    }

    private static void DrawPanelFrame(Graphics g, float x, float y, float width, float height)
    {
        var panelBr = GetCachedBrush(Color.FromArgb(170, 8, 16, 34));
        g.FillRectangle(panelBr, x, y, width, height);
        var glowPen = GetCachedPen(Color.FromArgb(90, 100, 230, 255), 5f);
        g.DrawRectangle(glowPen, x - 1f, y - 1f, width + 2f, height + 2f);
        var borderPen = GetCachedPen(Color.FromArgb(220, 120, 245, 255), 1.6f);
        g.DrawRectangle(borderPen, x, y, width, height);
    }

    private static void DrawStatRow(
        Graphics g,
        Font uiFont,
        string label,
        string value,
        float labelX,
        float valueRightX,
        float y,
        bool isHighlight)
    {
        var rowFont = GetCachedFont(uiFont, uiFont.Size + 0.5f, isHighlight ? FontStyle.Bold : FontStyle.Regular);
        var labelBr = GetCachedBrush(Color.FromArgb(215, 210, 224, 240));
        var valueBr = GetCachedBrush(isHighlight ? Color.FromArgb(255, 255, 225, 120) : Color.FromArgb(240, 235, 242, 250));
        g.DrawString(label, rowFont, labelBr, labelX, y);
        float valueWidth = g.MeasureString(value, rowFont).Width;
        g.DrawString(value, rowFont, valueBr, valueRightX - valueWidth, y);
        if (isHighlight)
        {
            const string newBest = "NEW BEST";
            var tagFont = GetCachedFont(uiFont, uiFont.Size - 1f, FontStyle.Bold);
            var tagBr = GetCachedBrush(Color.FromArgb(255, 255, 210, 95));
            g.DrawString(newBest, tagFont, tagBr, labelX + 170f, y + 1f);
        }
    }

    private static string TrimName(string name, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(name)) return "PLAYER";
        string trimmed = name.Trim();
        if (trimmed.Length <= maxChars) return trimmed;
        return trimmed[..Math.Max(1, maxChars - 3)] + "...";
    }

    private static void DrawFragments(Graphics g, IReadOnlyList<Fragment> fragments)
    {
        foreach (var f in fragments)
        {
            int minA = f.IsHighlight ? 115 : 18;
            int alpha = Math.Clamp((int)(f.BaseColor.A * f.LifeT), minA, 255);
            FragmentBrush.Color = Color.FromArgb(alpha, f.BaseColor.R, f.BaseColor.G, f.BaseColor.B);
            g.FillRectangle(FragmentBrush, f.X, f.Y, f.Width, f.Height);
        }
    }

    /// <summary>Draws the cached synthwave backdrop (rebuilt only when playfield size changes).</summary>
    private static void DrawBackground(Graphics g, int w, int h)
    {
        EnsureBackgroundCache(w, h);
        g.DrawImageUnscaled(CachedBackgroundBitmap!, 0, 0);
    }

    private static void EnsureBackgroundCache(int w, int h)
    {
        int ww = Math.Max(1, w);
        int hh = Math.Max(1, h);
        if (CachedBackgroundBitmap is not null && CachedBgWidth == ww && CachedBgHeight == hh)
            return;

        CachedBackgroundBitmap?.Dispose();
        CachedBackgroundBitmap = new Bitmap(ww, hh);
        CachedBgWidth = ww;
        CachedBgHeight = hh;

        using Graphics bg = Graphics.FromImage(CachedBackgroundBitmap);
        bg.SmoothingMode = SmoothingMode.AntiAlias;
        bg.CompositingQuality = CompositingQuality.HighSpeed;
        bg.PixelOffsetMode = PixelOffsetMode.HighSpeed;
        DrawSynthwaveGradient(bg, ww, hh);
        DrawSynthwaveSun(bg, ww, hh);
        DrawSynthwaveSkyline(bg, ww, hh);
        DrawSynthwaveGrid(bg, ww, hh);
    }

    /// <summary>Vertical multi-stop gradient: deep purple-blue → magenta → warm sunset.</summary>
    private static void DrawSynthwaveGradient(Graphics g, int w, int h)
    {
        using var sky = new LinearGradientBrush(
            new Rectangle(0, 0, w, h),
            Color.FromArgb(255, 14, 8, 40),
            Color.FromArgb(255, 255, 195, 75),
            LinearGradientMode.Vertical)
        {
            InterpolationColors = SkyGradientBlend
        };
        g.FillRectangle(sky, 0, 0, w, h);
    }

    /// <summary>Large sunset disk with horizontal scanlines (retro CRT striping).</summary>
    private static void DrawSynthwaveSun(Graphics g, int w, int h)
    {
        float sunR = w * 0.44f;
        float cx = w * 0.5f;
        float cy = h + sunR * 0.82f;

        g.FillEllipse(GetCachedBrush(Color.FromArgb(230, 255, 205, 70)), cx - sunR, cy - sunR, sunR * 2f, sunR * 2f);

        var rim = GetCachedPen(Color.FromArgb(180, 255, 230, 120), 1.2f);
        g.DrawEllipse(rim, cx - sunR, cy - sunR, sunR * 2f, sunR * 2f);

        // Draw scanlines using circle intersection math to avoid temporary clip paths.
        float y0 = cy - sunR;
        float y1 = Math.Min(h + 2f, cy + sunR);
        var stripe = GetCachedPen(Color.FromArgb(55, 210, 90, 45));
        for (float y = y0; y <= y1; y += 5f)
        {
            float dy = y - cy;
            float inside = sunR * sunR - dy * dy;
            if (inside <= 0f) continue;
            float half = MathF.Sqrt(inside);
            g.DrawLine(stripe, cx - half, y, cx + half, y);
        }
    }

    /// <summary>Dark building silhouettes along the bottom (simple rects, deterministic layout).</summary>
    private static void DrawSynthwaveSkyline(Graphics g, int w, int h)
    {
        var sil = GetCachedBrush(Color.FromArgb(252, 6, 4, 18));
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
        const int step = GameConfig.Visual.BombGridStep;
        var vPen = GetCachedPen(Color.FromArgb(36, 190, 95, 255), 0.65f);
        var hPen = GetCachedPen(Color.FromArgb(32, 255, 70, 210), 0.65f);
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
        Color baseColor = t > 0.5f
            ? Lerp3(Color.LimeGreen, Color.Gold, 2f * (1f - t))
            : Lerp3(Color.Gold, Color.Firebrick, 1f - 2f * t);
        if (!bar.IsSpecial) return baseColor;

        float pulse = 0.5f + 0.5f * MathF.Sin(Environment.TickCount64 * GameConfig.Visual.SpecialBarPulseRate + bar.X * GameConfig.Visual.SpecialBarPulsePhaseByX);
        Color neonBase = Lerp3(Color.FromArgb(255, 140, 70, 255), Color.FromArgb(255, 215, 120, 255), 1f - t);
        Color mixed = Lerp3(baseColor, neonBase, GameConfig.Visual.SpecialBarNeonMix);
        float brightness = GameConfig.Visual.SpecialBarBrightnessBase + GameConfig.Visual.SpecialBarBrightnessRange * pulse;
        return Lerp3(mixed, Color.FromArgb(255, 245, 215, 255), brightness);
    }

    private static Color GetSpecialOutlineColor(int elapsedFrames)
    {
        float pulse = 0.5f + 0.5f * MathF.Sin(elapsedFrames * GameConfig.Visual.SpecialOutlinePulseRate);
        int alpha = GameConfig.Visual.SpecialOutlineAlphaBase + (int)(GameConfig.Visual.SpecialOutlineAlphaRange * pulse);
        int green = GameConfig.Visual.SpecialOutlineGreenBase + (int)(GameConfig.Visual.SpecialOutlineGreenRange * pulse);
        int blue = GameConfig.Visual.SpecialOutlineBlueBase + (int)(GameConfig.Visual.SpecialOutlineBlueRange * pulse);
        return Color.FromArgb(alpha, 210, green, blue);
    }

    private static Color Lerp3(Color from, Color to, float u)
    {
        u = Math.Clamp(u, 0, 1);
        return Color.FromArgb(
            (int)(from.R + (to.R - from.R) * u),
            (int)(from.G + (to.G - from.G) * u),
            (int)(from.B + (to.B - from.B) * u));
    }

    /// <summary>Returns a shared font instance keyed by family hash / size / style (no string alloc).</summary>
    private static Font GetCachedFont(Font baseFont, float size, FontStyle style)
    {
        // size quantized to 0.01pt; family hash in high bits.
        int sizeQ = (int)MathF.Round(size * 100f);
        long key = ((long)(uint)baseFont.FontFamily.GetHashCode() << 32)
                   | ((long)(uint)sizeQ << 8)
                   | (byte)style;
        if (FontCache.TryGetValue(key, out Font? cached))
        {
            TouchKey(FontLru, FontNodes, key);
            return cached;
        }

        EnsureCacheCapacity(FontCache, FontLru, FontNodes, MaxFontCacheEntries, static disposable => disposable.Dispose());

        var created = new Font(baseFont.FontFamily, size, style, GraphicsUnit.Point);
        FontCache[key] = created;
        TouchKey(FontLru, FontNodes, key);
        return created;
    }

    /// <summary>Returns a shared solid brush keyed by ARGB color.</summary>
    private static SolidBrush GetCachedBrush(Color color)
    {
        int key = color.ToArgb();
        if (BrushCache.TryGetValue(key, out SolidBrush? cached))
        {
            TouchKey(BrushLru, BrushNodes, key);
            return cached;
        }
        EnsureCacheCapacity(BrushCache, BrushLru, BrushNodes, MaxBrushCacheEntries, static disposable => disposable.Dispose());
        var created = new SolidBrush(color);
        BrushCache[key] = created;
        TouchKey(BrushLru, BrushNodes, key);
        return created;
    }

    /// <summary>Returns a shared pen keyed by packed color/width/style (no string alloc).</summary>
    private static Pen GetCachedPen(
        Color color,
        float width = 1f,
        DashStyle dash = DashStyle.Solid,
        LineCap startCap = LineCap.Flat,
        LineCap endCap = LineCap.Flat,
        LineJoin join = LineJoin.Miter)
    {
        // Width quantized to 0.001px in 16 bits (~65.5 max encoded).
        int widthQ = Math.Clamp((int)MathF.Round(width * 1000f), 0, 65535);
        ulong keyBits = ((ulong)(uint)color.ToArgb() << 32)
                        | ((ulong)(uint)widthQ << 16)
                        | ((ulong)(byte)dash << 12)
                        | ((ulong)(byte)startCap << 8)
                        | ((ulong)(byte)endCap << 4)
                        | ((ulong)(byte)join & 0xFul);
        long key = unchecked((long)keyBits);
        if (PenCache.TryGetValue(key, out Pen? cached))
        {
            TouchKey(PenLru, PenNodes, key);
            return cached;
        }

        EnsureCacheCapacity(PenCache, PenLru, PenNodes, MaxPenCacheEntries, static disposable => disposable.Dispose());

        var created = new Pen(color, width)
        {
            DashStyle = dash,
            StartCap = startCap,
            EndCap = endCap,
            LineJoin = join
        };
        PenCache[key] = created;
        TouchKey(PenLru, PenNodes, key);
        return created;
    }

    /// <summary>
    /// Disposes and clears all static pen/brush/font caches and the backdrop <see cref="Bitmap"/>.
    /// Mutable FX pens are process-lifetime (not disposed) so a second draw after cleanup cannot use freed GDI handles.
    /// Safe to call more than once.
    /// </summary>
    public static void DisposeSharedResources()
    {
        DisposeCache(FontCache, FontLru, FontNodes, static disposable => disposable.Dispose());
        DisposeCache(BrushCache, BrushLru, BrushNodes, static disposable => disposable.Dispose());
        DisposeCache(PenCache, PenLru, PenNodes, static disposable => disposable.Dispose());
        CachedBackgroundBitmap?.Dispose();
        CachedBackgroundBitmap = null;
        CachedBgWidth = -1;
        CachedBgHeight = -1;
        _hudScoreCached = int.MinValue;
        _hudBestCached = int.MinValue;
        _hudComboMultCached = int.MinValue;
    }

    private static void TouchKey<TKey>(
        LinkedList<TKey> lru,
        Dictionary<TKey, LinkedListNode<TKey>> nodes,
        TKey key) where TKey : notnull
    {
        if (nodes.TryGetValue(key, out LinkedListNode<TKey>? existing))
        {
            lru.Remove(existing);
            lru.AddLast(existing);
            return;
        }

        LinkedListNode<TKey> node = lru.AddLast(key);
        nodes[key] = node;
    }

    private static void EnsureCacheCapacity<TKey, TValue>(
        Dictionary<TKey, TValue> cache,
        LinkedList<TKey> lru,
        Dictionary<TKey, LinkedListNode<TKey>> nodes,
        int maxEntries,
        Action<TValue> disposer) where TKey : notnull where TValue : class
    {
        while (cache.Count >= maxEntries && lru.First is not null)
        {
            TKey keyToEvict = lru.First.Value;
            lru.RemoveFirst();
            nodes.Remove(keyToEvict);
            if (!cache.Remove(keyToEvict, out TValue? evicted)) continue;
            disposer(evicted);
        }
    }

    private static void DisposeCache<TKey, TValue>(
        Dictionary<TKey, TValue> cache,
        LinkedList<TKey> lru,
        Dictionary<TKey, LinkedListNode<TKey>> nodes,
        Action<TValue> disposer) where TKey : notnull where TValue : class
    {
        foreach (TValue value in cache.Values)
            disposer(value);
        cache.Clear();
        lru.Clear();
        nodes.Clear();
    }
}
