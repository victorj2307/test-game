using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Game.Core;

namespace Game.UI;

/// <summary>Host window: timer-driven sim, Stopwatch per-tick delta, keyboard input, GDI+ in OnPaint.</summary>
public sealed class GameForm : Form
{
    private const int StartButtonWidth = 200;
    private const int StartButtonHeight = 44;
    private const int StartButtonBottomMargin = 18;
    private const int GameStatusBarHeight = 30;

    /// <summary>~60 Hz; WM_TIMER coalesces slightly — <see cref="_frameWatch"/> supplies actual delta to <c>Update</c>.</summary>
    private const int GameTimerIntervalMs = 16;

    private readonly System.Windows.Forms.Timer _gameTimer = new() { Interval = GameTimerIntervalMs };
    private readonly Stopwatch _frameWatch = new();
    private readonly GameManager _game = new();
    private readonly Button _btnStart = new();
    private readonly Panel _statusBar = new();
    private readonly Label _statusLine = new();
    private readonly HashSet<Keys> _keysDown = new();
    private long _lastFpsTimeMs;
    private int _framesThisSecond;
    private bool _runGameLoop;

    public GameForm()
    {
        Text = "Retro Blaster (WinForms)";
        ClientSize = new Size(480, 640);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        KeyPreview = true;
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(8, 12, 20);

        _statusBar.Height = GameStatusBarHeight;
        _statusBar.Dock = DockStyle.Bottom;
        _statusBar.BackColor = Color.FromArgb(16, 20, 32);

        _statusLine.AutoSize = false;
        _statusLine.Dock = DockStyle.Fill;
        _statusLine.TextAlign = ContentAlignment.MiddleLeft;
        _statusLine.Padding = new Padding(10, 4, 10, 4);
        _statusLine.ForeColor = Color.FromArgb(200, 205, 220);
        _statusLine.BackColor = Color.Transparent;
        _statusLine.Font = new Font("Segoe UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point);
        _statusLine.AutoEllipsis = true;
        _statusLine.UseMnemonic = false;
        _statusBar.Controls.Add(_statusLine);
        Controls.Add(_statusBar);

        _btnStart.Text = "Start";
        _btnStart.Size = new Size(StartButtonWidth, StartButtonHeight);
        _btnStart.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold, GraphicsUnit.Point);
        _btnStart.UseVisualStyleBackColor = false;
        _btnStart.Click += OnStartOrContinueClick;
        _btnStart.Cursor = Cursors.Hand;
        _btnStart.TabStop = true;
        _btnStart.FlatStyle = FlatStyle.Flat;
        _btnStart.FlatAppearance.BorderSize = 1;
        _btnStart.FlatAppearance.BorderColor = Color.FromArgb(90, 110, 140);
        _btnStart.BackColor = Color.FromArgb(36, 48, 72);
        _btnStart.ForeColor = Color.FromArgb(245, 248, 255);
        _btnStart.MouseEnter += (_, _) =>
        {
            _btnStart.BackColor = Color.FromArgb(52, 72, 108);
            _btnStart.FlatAppearance.BorderColor = Color.FromArgb(120, 170, 230);
        };
        _btnStart.MouseLeave += (_, _) =>
        {
            _btnStart.BackColor = Color.FromArgb(36, 48, 72);
            _btnStart.FlatAppearance.BorderColor = Color.FromArgb(90, 110, 140);
        };
        AcceptButton = _btnStart;
        Controls.Add(_btnStart);
        UpdateStartButtonLayout();
        _game.EnterAttractMode(ClientSize.Width, GetPlayHeight());
        _lastFpsTimeMs = Environment.TickCount64;
        UpdateStatusLine();
        _gameTimer.Tick += GameLoopTick;
        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
        ClientSizeChanged += OnClientOrSize;
        Shown += (_, _) => Invalidate();
        FormClosed += OnFormClosed;
    }

    private void OnFormClosed(object? sender, FormClosedEventArgs e)
    {
        _gameTimer.Stop();
        _gameTimer.Tick -= GameLoopTick;
        _gameTimer.Dispose();
    }

    private int GetPlayHeight() => Math.Max(32, ClientSize.Height - _statusBar.Height);

    private void OnClientOrSize(object? sender, EventArgs e)
    {
        _game.OnClientResize(ClientSize.Width, GetPlayHeight());
        UpdateStartButtonLayout();
    }

    private void UpdateStatusLine()
    {
        if (_game.IsGameOver)
#if DEBUG
            _statusLine.Text = "Click Start. F1: debug · F2: dev mode (easier test).";
#else
            _statusLine.Text = "Click Start. F1: debug overlay (hitboxes, FPS, counts).";
#endif
        else if (_game.IsLifeLost)
            _statusLine.Text = "Life lost. Press Enter/Space or click Continue.";
        else if (_game.IsPaused)
            _statusLine.Text = "Paused. Press ESC to continue.";
        else if (_game.IsPlaying)
#if DEBUG
            _statusLine.Text = _game.IsDevMode
                ? "DEV: F2 off · 1–6 power-ups · F1 debug · arrows/Space/ESC"
                : "Move: arrows or A/D · Space fire · ESC pause · F1 debug · F2 dev mode.";
#else
            _statusLine.Text = "Move: arrows or A/D · Space fire · ESC pause · F1 debug.";
#endif
        else
#if DEBUG
            _statusLine.Text = "F1: debug overlay · F2: dev mode (before/during play).";
#else
            _statusLine.Text = "F1: debug overlay (hitboxes, FPS, counts).";
#endif
    }

    private void UpdateStartButtonLayout()
    {
        int ph = GetPlayHeight();
        _btnStart.Location = new Point(
            (ClientSize.Width - _btnStart.Width) / 2,
            Math.Max(0, ph - _btnStart.Height - StartButtonBottomMargin));
    }

    private void StartGame()
    {
        _btnStart.Visible = false;
        _game.StartNewGame(ClientSize.Width, GetPlayHeight());
        _runGameLoop = true;
        _frameWatch.Restart();
        _framesThisSecond = 0;
        _lastFpsTimeMs = Environment.TickCount64;
        _gameTimer.Start();
        UpdateStatusLine();
        Focus();
        Invalidate();
    }

    private void ContinueLife()
    {
        _game.ContinueAfterLifeLost(ClientSize.Width, GetPlayHeight());
        _btnStart.Visible = false;
        UpdateStatusLine();
        Focus();
        Invalidate();
    }

    private void OnStartOrContinueClick(object? sender, EventArgs e)
    {
        if (_game.IsLifeLost) ContinueLife();
        else StartGame();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        bool wasDown = !_keysDown.Add(e.KeyCode);
        if (e.KeyCode is Keys.F1)
        {
            _game.ToggleDebug();
            e.Handled = true;
            return;
        }
#if DEBUG
        if (e.KeyCode is Keys.F2)
        {
            _game.ToggleDevMode();
            UpdateStatusLine();
            e.Handled = true;
            return;
        }
        int? devDigit = e.KeyCode switch
        {
            Keys.D1 or Keys.NumPad1 => 1,
            Keys.D2 or Keys.NumPad2 => 2,
            Keys.D3 or Keys.NumPad3 => 3,
            Keys.D4 or Keys.NumPad4 => 4,
            Keys.D5 or Keys.NumPad5 => 5,
            Keys.D6 or Keys.NumPad6 => 6,
            _ => null
        };
        if (devDigit is int d && _game.IsDevMode && _game.IsPlaying && !_game.IsPaused && !_game.IsLifeLost)
        {
            _game.TryDevActivatePowerUpDigit(d);
            e.Handled = true;
            return;
        }
#endif
        if (e.KeyCode is Keys.Escape && !wasDown)
        {
            _game.TogglePause();
            _frameWatch.Restart();
            UpdateStatusLine();
            e.Handled = true;
            return;
        }
        if (_game.IsLifeLost && (e.KeyCode is Keys.Enter or Keys.Space))
        {
            ContinueLife();
            e.Handled = true;
            return;
        }
    }

    private void OnKeyUp(object? sender, KeyEventArgs e) => _keysDown.Remove(e.KeyCode);

    private void GameLoopTick(object? sender, EventArgs e)
    {
        if (!_runGameLoop) return;

        float dt = (float)_frameWatch.Elapsed.TotalSeconds;
        _frameWatch.Restart();
        if (dt < 1f / 500f) dt = 1f / 500f;
        if (dt > 0.25f) dt = 0.25f;

        long now = Environment.TickCount64;
        _framesThisSecond++;
        if (now - _lastFpsTimeMs >= 1000)
        {
            _game.DebugFps = _framesThisSecond;
            _framesThisSecond = 0;
            _lastFpsTimeMs = now;
        }

        int w = ClientSize.Width;
        int h = GetPlayHeight();
        bool wantFire = _keysDown.Contains(Keys.Space);
        _game.Update(
            w,
            h,
            _keysDown.Contains(Keys.Left) || _keysDown.Contains(Keys.A),
            _keysDown.Contains(Keys.Right) || _keysDown.Contains(Keys.D),
            wantFire,
            dt);
        if (_game.IsLifeLost)
        {
            _btnStart.Visible = true;
            _btnStart.Text = "Continue";
            UpdateStartButtonLayout();
            UpdateStatusLine();
        }
        if (_game.IsGameOver)
        {
            _gameTimer.Stop();
            _runGameLoop = false;
            _btnStart.Visible = true;
            _btnStart.Text = "Play again";
            UpdateStartButtonLayout();
            UpdateStatusLine();
        }

        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        int w = ClientSize.Width;
        int h = GetPlayHeight();
        if (h <= 0) return;
        var playRect = new Rectangle(0, 0, w, h);
        e.Graphics.SetClip(playRect);
        e.Graphics.Clear(BackColor);
        _game.Draw(e.Graphics, w, h, Font);
        e.Graphics.ResetClip();
    }
}
