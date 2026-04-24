# Full Codebase Audit

Date: 2026-04-24  
Project: `test-game` (WinForms / C#)

## Critical Findings

- **Per-frame allocation in core update loop (`Bars.ToList()`)** in `GameManager.Update` creates avoidable GC pressure at gameplay frequency.
- **Direct, non-atomic high-score writes** in `HighScoreStore.TryAddScore` can corrupt leaderboard data on crash/power loss.
- **Catch-all exception swallowing** in persistence/audio paths hides failures and turns recoverable problems into silent data loss or silent feature failure.

```csharp
foreach (var b in _entities.Bars.ToList())
{
    if (b.GetBounds().Bottom >= playHeight)
    {
        if (_state.TryConsumeShield())
        {
            _entities.Bars.Remove(b);
```

```csharp
var normalized = Normalize(entries, maxEntries);
var json = JsonSerializer.Serialize(new LeaderboardData { Entries = normalized }, JsonOptions);
File.WriteAllText(path, json);
return true;
}
catch
{
    return false;
}
```

## High Findings

- **Rendering allocates GDI objects continuously in hot paths** (`RenderSystem.Draw` and helpers): many `new Pen`, `new SolidBrush`, `new Font`, `GraphicsPath`, etc. each frame or per entity.
- **Collision broad-phase is missing** (`CollisionSystem.Resolve` is bullet x bar scan), which can spike frame time as action density increases.
- **Audio synthesis/load on gameplay thread** (`SoundGenerator.PlayTone`) builds WAV buffers and calls `SoundPlayer.Load()` for events during play; burst events can hitch the loop.
- **Global mutable game state is too permissive** (`GameState` has many public setters), increasing invalid state-transition risk.

```csharp
using (var br = new SolidBrush(GetBarDrawColor(bar)))
    g.FillRectangle(br, r);
if (bar.IsSpecial)
{
    using var specialGlow = new Pen(GetSpecialOutlineColor(state.ElapsedFrames), 1.6f);
    g.DrawRectangle(specialGlow, r.X - 1, r.Y - 1, r.Width + 1, r.Height + 1);
}
```

```csharp
for (int bi = _entities.Bullets.Count - 1; bi >= 0; bi--)
{
    Bullet bl = _entities.Bullets[bi];
    bool removeBullet = false;
    while (!removeBullet)
    {
        Rectangle bRect = bl.GetBounds();
        int hitIndex = -1;
        for (int j = 0; j < _entities.Bars.Count; j++)
```

```csharp
public bool IsPlaying { get; set; }
public bool IsGameOver { get; set; }
public bool IsLifeLost { get; set; }
public bool IsPaused { get; set; }
public bool ShowLeaderboard { get; set; }
public bool IsDevMode { get; set; }
public int Score { get; set; }
public int ElapsedFrames { get; set; }
```

## Medium Findings

- **High-score file read is unbounded** (`File.ReadAllText`) in `HighScoreStore.LoadLeaderboard`; oversized local file can stall startup/game-over flow.
- **Persistence path uses `AppContext.BaseDirectory`** (`HighScoreStore`), brittle in protected install locations.
- **Difficulty system updates bar speed targets for every bar every tick** (`DifficultySystem.ApplyTable`) even when effective speed is unchanged.
- **No automated tests detected** (no test attributes/framework usage found in repository search).

```csharp
var json = File.ReadAllText(path);
if (string.IsNullOrWhiteSpace(json)) return Array.Empty<LeaderboardEntry>();

var listData = JsonSerializer.Deserialize<LeaderboardData>(json);
if (listData?.Entries is { Count: > 0 })
```

## Low Findings

- Minor config drift/magic values in rendering (`RenderSystem`) outside centralized config.
- Naming consistency drift (`Retro Blaster` window title vs `RetroArcade` assembly naming).
- Potential stale state (`KillsInWindow` incremented/reset without clear downstream behavior).

## Open Questions / Assumptions

- Game simulation and rendering are effectively tied to the UI thread via WinForms timer.
- Threat model appears local-only (no networking), so security focus is integrity/robustness.
- Findings are static-analysis based; runtime profiling should confirm top hotspots.

## Recommended Remediation Order

1. Remove `Bars.ToList()` from the hot update path.
2. Implement atomic high-score writes (temp + replace + optional backup).
3. Replace catch-all exception swallowing with specific exceptions + diagnostics.
4. Cache render resources and reduce per-frame GDI allocations.
5. Add collision broad-phase (lane/grid bucketization).
6. Move/optimize runtime audio generation to avoid UI thread stalls.
7. Harden `GameState` transitions behind intent methods.
8. Add automated tests for core systems (`GameState`, `CollisionSystem`, `SpawnSystem`, `DifficultySystem`).
