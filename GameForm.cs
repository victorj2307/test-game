using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RetroArcade;

/// <summary>Host window: System.Windows.Forms.Timer for the game loop, keyboard input, and GDI+ rendering in OnPaint.</summary>
public sealed class GameForm : Form
{
    private const int StartButtonWidth = 200;
    private const int StartButtonHeight = 44;
    private const int StartButtonBottomMargin = 18;
    private const int GameStatusBarHeight = 30;

    private readonly System.Windows.Forms.Timer _gameTimer = new() { Interval = 16 };
    private readonly GameManager _game = new();
    private readonly Button _btnStart = new();
    private readonly Panel _statusBar = new();
    private readonly Label _statusLine = new();
    private readonly HashSet<Keys> _keysDown = new();
    private long _lastFpsTimeMs;
    private int _framesThisSecond;

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
        _btnStart.Click += (_, _) => StartGame();
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
        _gameTimer.Tick += (_, _) => GameLoopTick();
        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
        ClientSizeChanged += OnClientOrSize;
        Shown += (_, _) => Invalidate();
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
            _statusLine.Text = "Click Start. F1: debug overlay (hitboxes, FPS, counts).";
        else if (_game.IsPlaying)
            _statusLine.Text = "Move: arrows or A/D (grid steps, ~75ms repeat) · Hold Space: fire · F1: debug. Combo builds on quick clears.";
        else
            _statusLine.Text = "Procedural bleeps (no files). F1: debug overlay.";
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
        _gameTimer.Start();
        _framesThisSecond = 0;
        _lastFpsTimeMs = Environment.TickCount64;
        UpdateStatusLine();
        Focus();
        Invalidate();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode is Keys.F1)
        {
            _game.ToggleDebug();
            e.Handled = true;
            return;
        }
        _keysDown.Add(e.KeyCode);
    }

    private void OnKeyUp(object? sender, KeyEventArgs e) => _keysDown.Remove(e.KeyCode);

    private void GameLoopTick()
    {
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
            wantFire);
        if (_game.IsGameOver)
        {
            _gameTimer.Stop();
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
        e.Graphics.Clip = new Region(new Rectangle(0, 0, w, h));
        e.Graphics.Clear(BackColor);
        _game.Draw(e.Graphics, w, h, Font);
        e.Graphics.ResetClip();
    }
}
