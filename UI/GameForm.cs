using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Game.Core;

namespace Game.UI;

/// <summary>
/// Host window: timer-driven sim, Stopwatch per-tick delta, keyboard input, GDI+ in OnPaint.
/// On game over, the timer and run loop stop before the leaderboard name modal. On close, shared render caches are disposed via <see cref="GameManager.DisposeResources"/>.
/// </summary>
public sealed class GameForm : Form
{
    private const int PrimaryButtonWidth = 220;
    private const int PrimaryButtonHeight = 50;
    private const int SecondaryButtonWidth = 200;
    private const int SecondaryButtonHeight = 36;
    private const int ButtonGap = 10;
    private const int ButtonBottomMargin = 18;
    private const int GameStatusBarHeight = 30;

    /// <summary>~60 Hz; WM_TIMER coalesces slightly — <see cref="_frameWatch"/> supplies actual delta to <c>Update</c>.</summary>
    private const int GameTimerIntervalMs = GameConfig.Ui.GameTimerIntervalMs;

    private readonly System.Windows.Forms.Timer _gameTimer = new() { Interval = GameTimerIntervalMs };
    private readonly Stopwatch _frameWatch = new();
    private readonly GameManager _game = new();
    private readonly Button _btnStart = new();
    private readonly Button _btnScores = new();
    private readonly Panel _statusBar = new();
    private readonly Label _statusLine = new();
    private readonly HashSet<Keys> _keysDown = new();
    private long _lastFpsTimeMs;
    private int _framesThisSecond;
    private bool _runGameLoop;
    private bool _gameOverEntryHandled;
    /// <summary>
    /// Intended secondary-button visibility. Do not use <see cref="Control.Visible"/> for layout:
    /// while the form is not yet shown, WinForms reports child Visible as false and would leave Scores at (0,0).
    /// </summary>
    private bool _secondaryButtonDesired;

    public GameForm()
    {
        Text = GameConfig.Ui.WindowTitle;
        ClientSize = new Size(GameConfig.Ui.WindowWidth, GameConfig.Ui.WindowHeight);
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

        StylePrimaryButton(_btnStart);
        _btnStart.Text = "Start";
        _btnStart.AccessibleName = "Start game";
        _btnStart.Size = new Size(PrimaryButtonWidth, PrimaryButtonHeight);
        _btnStart.Font = new Font("Segoe UI", 11.5f, FontStyle.Bold, GraphicsUnit.Point);
        _btnStart.Click += OnStartOrContinueClick;

        StyleSecondaryButton(_btnScores);
        _btnScores.Text = "Scores";
        _btnScores.AccessibleName = "View leaderboard";
        _btnScores.Size = new Size(SecondaryButtonWidth, SecondaryButtonHeight);
        _btnScores.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold, GraphicsUnit.Point);
        _btnScores.Click += OnScoresClick;

        AcceptButton = _btnStart;
        Controls.Add(_btnScores);
        Controls.Add(_btnStart);
        UpdateActionButtonsLayout();
        _game.EnterAttractMode(ClientSize.Width, GetPlayHeight());
        ShowAttractButtons();
        _lastFpsTimeMs = Environment.TickCount64;
        UpdateStatusLine();
        _gameTimer.Tick += GameLoopTick;
        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
        ClientSizeChanged += OnClientOrSize;
        Shown += (_, _) =>
        {
            UpdateActionButtonsLayout();
            Invalidate();
            if (_btnStart.Visible)
                _btnStart.Focus();
        };
        FormClosed += OnFormClosed;
    }

    private static void StylePrimaryButton(Button btn)
    {
        btn.UseVisualStyleBackColor = false;
        btn.Cursor = Cursors.Hand;
        btn.TabStop = true;
        btn.FlatStyle = FlatStyle.Flat;
        btn.FlatAppearance.BorderSize = 2;
        btn.FlatAppearance.BorderColor = Color.FromArgb(90, 230, 255);
        btn.BackColor = Color.FromArgb(12, 120, 150);
        btn.ForeColor = Color.FromArgb(250, 252, 255);
        btn.MouseEnter += (_, _) =>
        {
            btn.BackColor = Color.FromArgb(20, 155, 190);
            btn.FlatAppearance.BorderColor = Color.FromArgb(160, 245, 255);
        };
        btn.MouseLeave += (_, _) =>
        {
            btn.BackColor = Color.FromArgb(12, 120, 150);
            btn.FlatAppearance.BorderColor = Color.FromArgb(90, 230, 255);
        };
    }

    private static void StyleSecondaryButton(Button btn)
    {
        btn.UseVisualStyleBackColor = false;
        btn.Cursor = Cursors.Hand;
        btn.TabStop = true;
        btn.FlatStyle = FlatStyle.Flat;
        btn.FlatAppearance.BorderSize = 1;
        btn.FlatAppearance.BorderColor = Color.FromArgb(70, 90, 120);
        btn.BackColor = Color.FromArgb(28, 36, 54);
        btn.ForeColor = Color.FromArgb(200, 210, 225);
        btn.MouseEnter += (_, _) =>
        {
            btn.BackColor = Color.FromArgb(42, 56, 82);
            btn.FlatAppearance.BorderColor = Color.FromArgb(110, 160, 220);
        };
        btn.MouseLeave += (_, _) =>
        {
            btn.BackColor = Color.FromArgb(28, 36, 54);
            btn.FlatAppearance.BorderColor = Color.FromArgb(70, 90, 120);
        };
    }

    /// <summary>Stops the frame timer, unsubscribes, and releases static GDI caches owned by <see cref="Rendering.RenderSystem"/>.</summary>
    private void OnFormClosed(object? sender, FormClosedEventArgs e)
    {
        _gameTimer.Stop();
        _game.DisposeResources();
        _gameTimer.Tick -= GameLoopTick;
        _gameTimer.Dispose();
    }

    /// <summary>Returns drawable gameplay height excluding the bottom status bar.</summary>
    private int GetPlayHeight() => Math.Max(GameConfig.Ui.MinPlayHeight, ClientSize.Height - _statusBar.Height);

    private void OnClientOrSize(object? sender, EventArgs e)
    {
        _game.OnClientResize(ClientSize.Width, GetPlayHeight());
        UpdateActionButtonsLayout();
    }

    /// <summary>Updates status/help line text according to current game state.</summary>
    private void UpdateStatusLine()
    {
        if (_game.IsBrowsingLeaderboard)
            _statusLine.Text = "Leaderboard. Esc or click Back to return.";
        else if (_game.IsGameOver)
#if DEBUG
            _statusLine.Text = "Enter: Play again · Menu: title screen · F1 debug · F2: dev.";
#else
            _statusLine.Text = "Enter: Play again · Menu: title screen · F1: debug overlay.";
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
            _statusLine.Text = "Enter or Start · Scores for leaderboard · Move: arrows/A D · Space · ESC · F1/F2.";
#else
            _statusLine.Text = "Enter or Start · Scores for leaderboard · Move: arrows or A/D · Space fire · ESC pause · F1.";
#endif
    }

    /// <summary>Shows Continue after life lost (hides secondary button).</summary>
    private void ShowContinueButton()
    {
        SetSecondaryButtonVisible(false);
        _btnStart.Text = "Continue";
        _btnStart.AccessibleName = "Continue after life lost";
        _btnStart.Visible = true;
        AcceptButton = _btnStart;
        UpdateActionButtonsLayout();
        UpdateStatusLine();
        _btnStart.Focus();
    }

    /// <summary>Game-over chrome: Play again (primary, top) then Menu (secondary).</summary>
    private void ShowGameOverButtons()
    {
        _btnStart.Text = "Play again";
        _btnStart.AccessibleName = "Play again";
        _btnStart.Visible = true;
        _btnScores.Text = "Menu";
        _btnScores.AccessibleName = "Return to title screen";
        SetSecondaryButtonVisible(true);
        AcceptButton = _btnStart;
        UpdateActionButtonsLayout();
        UpdateStatusLine();
        _btnStart.Focus();
    }

    /// <summary>Attract chrome: Start (primary, top) then Scores.</summary>
    private void ShowAttractButtons()
    {
        _btnStart.Text = "Start";
        _btnStart.AccessibleName = "Start game";
        _btnStart.Visible = true;
        _btnScores.Text = "Scores";
        _btnScores.AccessibleName = "View leaderboard";
        SetSecondaryButtonVisible(true);
        AcceptButton = _btnStart;
        UpdateActionButtonsLayout();
        UpdateStatusLine();
        _btnStart.Focus();
    }

    /// <summary>Browse chrome: Back only (Enter/click closes board).</summary>
    private void ShowBrowseBackButton()
    {
        SetSecondaryButtonVisible(false);
        _btnStart.Text = "Back";
        _btnStart.AccessibleName = "Close leaderboard";
        _btnStart.Visible = true;
        AcceptButton = _btnStart;
        UpdateActionButtonsLayout();
        UpdateStatusLine();
        _btnStart.Focus();
    }

    private void SetSecondaryButtonVisible(bool visible)
    {
        _secondaryButtonDesired = visible;
        _btnScores.Visible = visible;
    }

    /// <summary>
    /// Stacks primary above secondary near the bottom of the play area.
    /// Attract: Start then Scores. Game over: Play again then Menu. Single-button modes pin primary at the bottom.
    /// </summary>
    private void UpdateActionButtonsLayout()
    {
        int ph = GetPlayHeight();
        int width = Math.Max(GameConfig.Ui.WindowWidth, ClientSize.Width);
        if (_secondaryButtonDesired)
        {
            int secondaryY = Math.Max(0, ph - _btnScores.Height - ButtonBottomMargin);
            int primaryY = Math.Max(0, secondaryY - ButtonGap - _btnStart.Height);
            _btnStart.Location = new Point((width - _btnStart.Width) / 2, primaryY);
            _btnScores.Location = new Point((width - _btnScores.Width) / 2, secondaryY);
        }
        else
        {
            int primaryY = Math.Max(0, ph - _btnStart.Height - ButtonBottomMargin);
            _btnStart.Location = new Point((width - _btnStart.Width) / 2, primaryY);
        }

        _btnStart.BringToFront();
    }

    /// <summary>Starts a new run and enables simulation loop/timer.</summary>
    private void StartGame()
    {
        _game.CloseLeaderboardBrowse();
        _btnStart.Visible = false;
        SetSecondaryButtonVisible(false);
        _gameOverEntryHandled = false;
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

    /// <summary>Continues gameplay after life-lost pause.</summary>
    private void ContinueLife()
    {
        _game.ContinueAfterLifeLost(ClientSize.Width, GetPlayHeight());
        _btnStart.Visible = false;
        SetSecondaryButtonVisible(false);
        UpdateStatusLine();
        Focus();
        Invalidate();
    }

    private void OpenLeaderboardBrowse()
    {
        if (!_game.TryOpenLeaderboardBrowse())
            return;
        ShowBrowseBackButton();
        Invalidate();
    }

    private void CloseLeaderboardBrowse()
    {
        if (!_game.IsBrowsingLeaderboard)
            return;
        _game.CloseLeaderboardBrowse();
        ShowAttractButtons();
        Invalidate();
    }

    private void ReturnToTitleScreen()
    {
        _runGameLoop = false;
        _gameTimer.Stop();
        _gameOverEntryHandled = false;
        _game.EnterAttractMode(ClientSize.Width, GetPlayHeight());
        ShowAttractButtons();
        Invalidate();
    }

    /// <summary>Starts a game, continues after life loss, or closes leaderboard browse.</summary>
    private void OnStartOrContinueClick(object? sender, EventArgs e)
    {
        if (_game.IsBrowsingLeaderboard)
        {
            CloseLeaderboardBrowse();
            return;
        }
        if (_game.IsLifeLost) ContinueLife();
        else StartGame();
    }

    private void OnScoresClick(object? sender, EventArgs e)
    {
        if (_game.IsGameOver)
        {
            ReturnToTitleScreen();
            return;
        }
        if (_game.IsAttractMode && !_game.IsBrowsingLeaderboard)
            OpenLeaderboardBrowse();
    }

    /// <summary>Handles gameplay and debug hotkeys while tracking held-key state.</summary>
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
            if (_game.IsBrowsingLeaderboard)
            {
                CloseLeaderboardBrowse();
                e.Handled = true;
                return;
            }
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

    /// <summary>
    /// Main timer callback: computes dt, updates simulation, and schedules repaint.
    /// When <see cref="GameManager.IsGameOver"/> becomes true, stops the timer before <see cref="HandleLeaderboardOnGameOver"/> (modal dialog).
    /// </summary>
    private void GameLoopTick(object? sender, EventArgs e)
    {
        if (!_runGameLoop) return;

        float dt = (float)_frameWatch.Elapsed.TotalSeconds;
        _frameWatch.Restart();
        if (dt < GameConfig.Ui.MinDeltaSeconds) dt = GameConfig.Ui.MinDeltaSeconds;
        if (dt > GameConfig.Ui.MaxDeltaSeconds) dt = GameConfig.Ui.MaxDeltaSeconds;

        long now = Environment.TickCount64;
        _framesThisSecond++;
        if (now - _lastFpsTimeMs >= GameConfig.Ui.FpsWindowMs)
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
            ShowContinueButton();
        if (_game.IsGameOver)
        {
            if (_runGameLoop)
            {
                _gameTimer.Stop();
                _runGameLoop = false;
            }
            if (!_gameOverEntryHandled)
            {
                _gameOverEntryHandled = true;
                HandleLeaderboardOnGameOver();
            }
            ShowGameOverButtons();
        }

        Invalidate();
    }

    /// <summary>Handles score submission prompt when game-over score qualifies for leaderboard.</summary>
    private void HandleLeaderboardOnGameOver()
    {
        if (!_game.ScoreQualifiesForLeaderboard()) return;
        string? entered = PromptForName();
        string playerName = string.IsNullOrWhiteSpace(entered) ? GameConfig.Persistence.DefaultPlayerName : entered.Trim();
        _game.SubmitLeaderboardScore(playerName);
    }

    /// <summary>Shows modal player-name prompt for leaderboard entry.</summary>
    private string? PromptForName()
    {
        using var dialog = new Form
        {
            Text = $"New High Score — {_game.Score}",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(340, 168)
        };
        var lbl = new Label
        {
            AutoSize = false,
            Size = new Size(312, 36),
            Text = $"Score {_game.Score} made the leaderboard!\nEnter your name (Esc cancels → PLAYER):",
            Location = new Point(14, 12)
        };
        var input = new TextBox
        {
            Location = new Point(15, 56),
            Width = 308,
            MaxLength = GameConfig.Persistence.NameInputMaxLength
        };
        var ok = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Width = 88,
            Height = 30,
            Location = new Point(134, 108)
        };
        var cancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Width = 88,
            Height = 30,
            Location = new Point(234, 108)
        };
        dialog.Controls.Add(lbl);
        dialog.Controls.Add(input);
        dialog.Controls.Add(ok);
        dialog.Controls.Add(cancel);
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        dialog.ActiveControl = input;
        return dialog.ShowDialog(this) == DialogResult.OK ? input.Text : null;
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
