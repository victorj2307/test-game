# Retro Blaster

**Retro Blaster** is a small, educational **retro-style 2D arcade** game for **Windows**, written in **C#** with **Windows Forms (WinForms)**. The player controls a cannon at the bottom of the screen, shoots upward, and destroys falling vertical “bar” enemies. The project uses **only** the .NET BCL and WinForms—**no game engines** and **no third-party NuGet packages**—so the code stays easy to read, learn from, and modify.

Simulation and rendering are split into a thin **`Game.Core.GameManager`** orchestrator plus **`Game.Core.GameState`**, **`Game.Core.EntityManager`**, and small **`Game.Systems.*`** classes (spawn, collision, difficulty, render) so responsibilities stay clear without a heavy framework.

---

## Table of contents

1. [Tech stack](#tech-stack)
2. [Features](#features)
3. [Game rules & mechanics](#game-rules--mechanics)
4. [Project structure & architecture](#project-structure--architecture)  
   - [Repository layout](#repository-layout)
5. [Architecture & design](#architecture--design)
6. [Implementation overview](#implementation-overview)
7. [Requirements](#requirements)
8. [Build and run](#build-and-run)
9. [Controls](#controls)  
   - [Dev mode hotkeys (Debug builds only)](#dev-mode-hotkeys-debug-builds-only)
10. [UI layout](#ui-layout)
11. [How the game loop works](#how-the-game-loop-works)
12. [Tuning & constants](#tuning--constants)
13. [Design notes & limitations](#design-notes--limitations)

---

## Tech stack

| Layer | Technology | Notes |
|--------|------------|--------|
| **Language** | C# | Nullable reference types enabled (`<Nullable>enable</Nullable>`). |
| **Runtime / SDK** | .NET 8 | Windows-only project (`net8.0-windows`). |
| **UI framework** | Windows Forms | `UseWindowsForms` in the project file; main window is a `Form`. |
| **Graphics** | **GDI+** via `System.Drawing` | `Graphics`, `Pen`, `Brush`, `Rectangle`, `Color`, `Font`, `SmoothingMode`, etc. Gameplay rendering runs from `GameForm.OnPaint` → `RenderSystem.Draw` (not per-entity controls). |
| **Game loop** | **`System.Windows.Forms.Timer`** (~**16 ms**) + **`Stopwatch`** | **`WM_TIMER`** drives **`GameLoopTick`**: one **`GameManager.Update`** per tick and **`Invalidate`** every tick (including **pause**), so **`OnPaint`** keeps running and overlays stay visible. **`Stopwatch`** measures real elapsed time between ticks (passed as **`deltaSeconds`**) for HUD / future tuning — avoids **`Application.Idle`** tight-spin starving **`WM_PAINT`**. |
| **Input** | Keyboard + WinForms controls | `KeyPreview` on the form, `KeyDown` / `KeyUp`, and a `HashSet<Keys>` for held keys. **Space (held)** with a **~100 ms** cooldown in `GameManager` for auto-fire. **ESC** toggles pause/resume (disabled during life-lost and game-over states). **F1** toggles debug draw in all builds. **F2** and dev **1–6** hotkeys are compiled only under **`#if DEBUG`** (`GameForm`, `GameManager.ToggleDevMode` / `TryDevActivatePowerUpDigit`); **Release** builds cannot turn dev mode on from the keyboard, and **`ResetRun`** forces **`IsDevMode = false`** (`#if !DEBUG` in `GameState`). During life-lost pause, **Enter/Space** continues. |
| **Entry point** | `Program.cs` | `[STAThread]`, `ApplicationConfiguration.Initialize()` (high-DPI / WinForms bootstrap in modern .NET), `Application.Run(new GameForm())`. |
| **Build output** | `WinExe` | Assembly name `RetroArcade`, root namespace `Game`. |
| **Solution** | `ArcadeGame.sln` | Optional; includes `ArcadeGame.csproj` for **Visual Studio** and CLI workflows. |

**Not used:** Unity, MonoGame, SDL, Skia, WPF, DirectX wrappers, or any external libraries for rendering, audio, or physics.

---

## Features

### Gameplay

- **Cannon (player)** — Rectangle at the **bottom** of the play area; horizontal movement is **discrete steps** (half bar width), **grid-snapped**, with a **difficulty-scaled per-direction cooldown** so control remains fair as bars speed up.
- **Lives system** — Each run starts with **3 lives**. A bar reaching the floor removes one life instead of ending immediately. The run ends only when lives reach **0**.
- **Bullets** — Fired **straight up**; many bullets can exist at once. Off-screen removal when they leave the **top** of the play area.
- **Enemies (bars)** — **Vertical** rectangles that **fall** from the top. Width comes from **`GameConfig.Bars.Width`** (24 px); height and type vary by **`BarType`** (Normal, Fast, Tank).
- **Natural bar entry** — New bars spawn above the playfield and move downward at their normal fall speed. Spawn Y is computed per column overlap as `min(-height, highestOverlappingBarY - height)`, so new bars always appear above existing bars in that lane.
- **Core mechanic: bars do not vanish on first hit** — Each hit applies **`GameConfig.Scoring.DamagePerHit`** to **height**; the bar’s **top** stays fixed, so the bar **shrinks from the bottom upward**. When **height ≤ 0**, the bar is removed and score/combo logic is applied (see [Game rules](#game-rules--mechanics)).
- **Life loss / game over** — If a bar reaches the floor (and dev mode is off), **`GameState.TryConsumeShield()`** runs first: with **`ShieldCharges`** active, the bar is removed, **no life is lost**, and shield feedback (sound, cyan ring burst, flash, light shake) plays. Otherwise **`GameState.LoseLife()`** is called. If lives remain, the playfield is cleared and **`ResetAfterLifeLost`** runs, then the game enters a paused **`IsLifeLost`** state until the player continues. If lives are depleted, existing game-over flow runs (`ApplyGameOverShakeAndClearMuzzle`). **Dev mode** (`GameState.IsDevMode`): bars that reach the floor are **removed** with a short shake and **do not** cost a life (shield still consumes first if active).
- **Progressive difficulty (tuning table)** — Difficulty is driven by explicit keyframes (time in seconds + target bar speed + target spawn interval + target max bars). This gives precise pacing control and avoids hidden spikes.
- **Parameter mapping (independent)** — At runtime, `DifficultySystem` samples the table by current run time and linearly interpolates between neighboring keyframes, then applies the resulting targets to `BarSpeed`, `SpawnIntervalFrames`, and `DynamicMaxBarsOnScreen`.
- **Non-overlapping spawns** — New bars must pass both checks: no **AABB overlap** and minimum **horizontal spacing padding** from existing bar X positions. Placement computes spawn Y from horizontally overlapping bars (highest Y wins), then runs overlap checks. Uses bounded random retries and a left-to-right scan fallback; if no valid slot exists, spawn is skipped for a short retry window.
- **Concurrent bar cap** — `MaxBarsOnScreen` comes from a smooth difficulty-driven target (`DynamicMaxBarsOnScreen`, clamped), while **`SpawnSystem`** still enforces `EffectiveMaxBarsOnScreen` and dev-mode cap behavior.
- **Damage & collisions** — Bullet vs bar uses **`Rectangle.IntersectsWith`** against each bar’s **visible on-screen bounds** (`top = max(Y, 0)`, `bottom = Y + height`), so there are no invisible hitboxes. Spawn placement still uses full-size bounds to keep newly created bars from overlapping.
- **Immediate power-up activation** — Collected power-ups activate instantly on pickup and replace any current active effect (single-active model).
- **Power-up drop consistency** — Drops use a base random chance (**10%**) plus bad-luck protection: once the no-drop counter reaches **12** destroyed bars, that threshold destroy is forced to drop. **Dev mode** uses **~55%** per destroy and forces at threshold **3** ([`SpawnSystem.TrySpawnPowerUpAt`](Systems/SpawnSystem.cs)).

### User experience & presentation

- **Start gate** — Simulation does not advance until **Start** (or **Enter** as `AcceptButton`). After game over, **Play again** returns and the **idle-driven loop** stays off until a new start.
- **Muzzle / shot line** — Gold muzzle port and warm aim line align with **`Player.GetBulletSpawn`** / **`MuzzleTopCenter`**.
- **HUD** — Visual HUD with **life icons** (top-left), a boxed **SCORE** label+value (top-right), secondary **BEST** beneath it, emphasized **COMBO** when active, and a **centered active power-up card**: large colored glyph on top, short **uppercase** label beneath (**RAPID**, **MULTI**, **PIERCE**, **SHIELD**, **SLOW**, **BOMB**), plus a **timer bar** when the effect is timed.
- **Overlays** — Attract: **READY?** + start hint. Pause: semi-transparent overlay with **PAUSED** and an ESC continue hint. Life lost: semi-transparent overlay with **LIFE LOST**, remaining life icons, continue hint, and dedicated life-lost SFX. Game over: darker/stronger overlay that presents **FINAL RESULTS**, a glowing **GAME OVER** title, and leaderboard panel.
- **Status bar** — Docked bottom **`Panel` + `Label`**; play height = client height minus bar height; **`OnPaint`** clips to the play rectangle.
- **Double-buffered** form to reduce flicker.

### Gameplay & polish

- **Hit feedback** — Bullet impacts now combine a short flash (**`HitFlashTimer`**) with a subtle pulse (**`HitPulseFrames`**) so non-lethal hits feel responsive without overpowering the scene.
- **Fragments & particles** — Impact sparks remain in **`EntityManager.Particles`**. A shared debris system in **`EntityManager.Fragments`** adds rectangle fragments with lifetime, gravity, and fade: bullet hits spawn tiny directional chips (subtle), while bomb kills spawn larger radial shards (dramatic).
- **Bar types** — **Normal**, **Fast** (higher per-type speed scale, shorter height band), **Tank** (lower scale, taller). Target pixel speed derives from global bar speed × type scale; motion eases toward that target (**`Bar`** lerp).
- **Health color** — Green → gold → red from **`HealthRatio`**; flash color overrides while flashing.
- **Combo** — Destroys within **`ComboTimeWindowFrames`** (90) raise streak; points per destroy = **`min(streak, ComboMaxMultiplier)`** (multiplier cap 4).
- **Spawn patterns** — **Cluster** (~15%): 1–2 extra bars with small X offset. **Lane** (~10%): next spawn may reuse the same X column.
- **Special bars** — New bars roll **`IsSpecial`** with a fixed chance (~12% in `SpawnSystem`). Special bars render with a purple-neon tint, subtle time-based pulse, and glow outline for fast recognition.
- **Firing** — Hold **Space**; cooldown starts at **100 ms** and scales down slightly with difficulty (plus Rapid Fire when active), clamped for control.
- **Background** — Vertical dark gradient + low-contrast grid (**`RenderSystem`**).
- **Danger line** — Horizontal guide; pen warms when any bar’s bottom crosses it.
- **Player / shots** — Cyan cannon, gold muzzle, aim line, short **muzzle flash** on fire, faint **cyan bullet trails**. **Piercing Shot** rounds are **wider**, **magenta/purple** core + soft glow, and **magenta dual trails** so they read differently from normal shots.
- **Bars** — Thin dark outline for separation from the grid.
- **Screen shake** — ~1 px for a short time on destroy, life lost, and game over; **bomb detonation** adds a separate **~2 px** pattern for **`BombHeavyShakeFrames`** while the flash fades. **Shield block** uses a small dedicated shake pattern (**`ShieldImpactShakeFrames`**) plus **`ShakeUntilTickMs`** extension from **`TryConsumeShield`** (does not stack with bomb’s heavy shake while bomb frames run).
- **Life-lost flash** — Short red flash fade (`LifeLostFlashFrames`) when a life is consumed.
- **Power-ups** — Destroyed bars can drop collectibles: Rapid Fire, Multi Shot, Piercing Shot, Shield, Slow Motion, Bomb Shot. Effects are applied immediately on collection. The HUD uses a **visual-first** slot: each type has a **distinct color** and **simple drawn symbol** (speed streaks, triple dots, diamond, shield path, clock arc, bomb+fuse) in [`RenderSystem`](Rendering/RenderSystem.cs) (`DrawPowerUpGlyph` / `GetPowerUpHudLabel`), not long technical names. Timed effects show a **progress bar** under the label when `ActivePowerUpDurationFrames` is set. **Bomb Shot** does **not** use bullets: on pickup, **`GameState`** stores a **random point** in the playfield and a **frame fuse** (~12–31 frames ≈ **200–500 ms**); [`RenderSystem.DrawBombFuseIndicator`](Rendering/RenderSystem.cs) shows a pulsing marker; when the fuse hits zero, **`CollisionSystem.ExecuteBombExplosionAt`** runs a **hybrid cull**: active bars are **sorted by danger** (lowest on screen / largest **bottom** Y first, then distance to the blast point), then **`floor(count × 0.72)`** of them are **destroyed** (capped so at least one bar can survive when **count ≥ 2**); remaining bars inside a **splash radius** take extra damage. Culled bars are **`DestroyBarAt`**’d **over several frames** in **distance order** (closest to the blast center first; **`CollisionSystem.TickPendingBombKills`**). **`ExplosionFx`** uses a **smooth-expanding** triple shockwave (**`ExpansionT`** easing, **`MaxWaveRadius`** ~168); **`DrawBombScreenFlash`** adds a brief **white/yellow** full-screen fade (`BombScreenFlashFrames`); see [Dev mode hotkeys](#dev-mode-hotkeys-debug-builds-only) for tester shortcut **3**.
- **Drop trigger coverage** — Drop roll runs on **every destroyed bar** through the collision-destroy path, using the same shared `Random` instance owned by `GameManager` (no per-frame re-creation).
- **Start / Play again** — Large flat button, border, hover highlight (**`GameForm`**).
- **Audio** — Procedural mono **44.1 kHz** sine WAV in memory (**`SoundGenerator`**), **cosine envelope**, **`SoundPlayer`** pool (12 slots) for overlapping **`Play()`**; **`GameAudio`** exposes shoot / hit / life lost / game over / **shield pickup** / **shield block** / **pierce hit**.
- **Leaderboard** — **`highscore.json`** via **`HighScoreStore`** stores **Top 10** entries (`Name`, `Score`, optional `DateUtc`). Names are normalized (`Trim`, case-insensitive match), and each player keeps only a single **best score** entry (no run-to-run accumulation). On game over, qualifying scores prompt for a nickname (default **PLAYER** if blank); the overlay shows a dedicated **FINAL RESULTS** panel (Score, Best Score, Max Combo) above the leaderboard using the same panel style.
- **Debug (F1)** — Hitboxes, FPS (from form), entity counts; smaller, dimmer font.
- **Dev mode (Debug builds only)** — **`GameState.IsDevMode`** is toggled with **F2**; number keys **1–6** (main row or numpad) grant power-ups while a run is active—see the [key table](#dev-mode-hotkeys-debug-builds-only). Compiled only with **`DEBUG`** (`#if DEBUG` in **`GameForm`** / **`GameManager.ToggleDevMode`**; **`TryDevActivatePowerUpDigit`** is empty in Release). Not cleared on **Start** / **Play again** while debugging. While on: easier difficulty (see above), boosted power-up drops, no life loss when a bar touches the floor. **Release** builds: dev flag is cleared on each **`ResetRun`**; gameplay dev branches never activate. Overlay: [`RenderSystem.DrawDevModeOverlay`](Rendering/RenderSystem.cs).

---

## Game rules & mechanics

### Objective

Clear falling **bars** before they reach the **bottom** of the playfield. Each full **destroy** adds **score** (combo-based). Survive as long as possible; a local **Top-10 leaderboard** persists to disk.

### Player

| Rule | Detail |
|--------|--------|
| **Position** | Integer **X**, bottom-aligned **Y** = `playHeight − height` each frame. |
| **Movement** | Step size = **`GameConfig.Bars.Width / 2`** (12 px). **`TryStep(±1)`** with **`ClampAndSnapToGrid`** so X stays on a step grid and inside `[0, clientWidth − width]`. |
| **Input cadence** | Step cooldown starts at **75 ms** and is scaled by difficulty in `GameState.GetPlayerStepCooldownMs(...)` (`base - barSpeedTerm - scoreTerm`, clamped to **34..75 ms**) per direction (`GameState.LastPlayerStepLeftMs` / `RightMs`). |
| **Firing** | **`TryFire`**: 4×10 px bullet, speed **10** px/frame upward; only while `IsPlaying`, not game over, and not paused. |
| **Pause** | **ESC** toggles `IsPaused`; when paused, gameplay update logic is skipped. |
| **Life-lost pause** | While `IsLifeLost` is true, gameplay update logic is paused until continue input/button. |

### Combat & scoring

| Rule | Detail |
|--------|--------|
| **Damage** | **`DamagePerHit`** = **20** per bullet impact; bullet is removed; bar **`ApplyDamage`**. |
| **Hit feedback** | **`RegisterHit`** sets flash + pulse; **`GameAudio.PlayHit`**; impact particles and small directional fragments. **Piercing Shot** uses **`RegisterPierceHit`** (longer magenta flash), **`GameAudio.PlayPierceHit`**, and magenta-tinted hit effects. |
| **Destroy** | When **`Height ≤ 0`**: bar removed, **`GameState.RegisterBarDestroyed(isSpecial)`** runs, then **`DifficultySystem.SyncBarSpeedFromScore`**. Special bars apply a **2x score multiplier** and add slightly stronger destroy feedback. |
| **Combo** | If previous destroy was within **90 frames**, **`_combo`** increments; else reset to **1**. **Points added** = **`ComboMultiplier`** = **`min(_combo, 4)`**. |
| **Leaderboard** | During play, **`HighScore`** updates in memory for HUD. At game over, if the final score qualifies for Top-10, name entry is requested and `HighScoreStore.TryAddScore(...)` saves leaderboard JSON using normalized-name best-score rules (case-insensitive, one row per player). |
| **Kills window** | **`KillsInWindow`** increments on destroy and is still tracked for telemetry/debug pacing visibility. |

### Difficulty (tuning table + interpolation)

Difficulty is defined by `GameConfig.Difficulty.Table` and sampled by `DifficultySystem`:
- `Seconds`
- `BarSpeed`
- `SpawnIntervalFrames`
- `MaxBarsOnScreen`

Each update:
1. Find the two keyframes around current run time (`ElapsedFrames / 60f`).
2. Compute `t` in `[0..1]` across that segment.
3. Linearly interpolate each parameter with `Lerp(a, b, t)`.
4. Apply clamped results to game state (dev mode caps still apply).

After interpolation, values are eased slightly frame-to-frame to keep transitions visually smooth.

Each **`Bar`** still maps global speed × type scale to a target fall speed and lerps internally (`SpeedLerpFactor` in `Bar`).

### Spawning & density

| Rule | Detail |
|--------|--------|
| **Countdown** | **`SpawnCountdown`** decrements each spawn attempt; on success reset to **`EffectiveSpawnIntervalFrames`** (in dev mode, at least **72** frames even if internal **`SpawnIntervalFrames`** fell lower); on hard failure use short retry delay from `GameConfig.Spawn.RetryFramesWhenNoFit`. |
| **Max on screen** | Normal: difficulty-table-driven `DynamicMaxBarsOnScreen` (clamped by config). Dev: capped by **`EffectiveMaxBarsOnScreen`** (`GameConfig.Difficulty.DevMaxBarsOnScreen`). |
| **Lane** | **`NextSpawnLaneX`** may force the next primary spawn X. |
| **Cluster** | Extra bars after a successful primary place, same cap and overlap rules. |

### Lose condition

If **any** bar has **visible bottom** (**`bar.GetBounds().Bottom`**) at or below `playHeight`: if **dev mode** is on, the bar is removed without life loss. Else if **`TryConsumeShield()`** succeeds (one shield charge in **`ShieldCharges`**), the bar is removed and the player keeps the life. Otherwise consume one life. If lives remain, clear entities, reset volatile timers, set **`IsLifeLost`**, play life-lost sound, and wait for continue. If lives reach 0, trigger game over + shake + stop playing + game-over sound; **`GameForm`** stops the **run loop** flag and shows **Play again**.

### Coordinates

WinForms standard: **origin top-left**, **Y** increases downward; bullets move by decreasing **Y**.

---

## Project structure & architecture

### Repository layout

Source files are grouped by responsibility under top-level folders. Build outputs go under `bin/` and `obj/`.

```
Retro Blaster (repo root)
├── ArcadeGame.sln
├── ArcadeGame.csproj
├── Program.cs
├── Core/
│   ├── GameManager.cs
│   ├── GameState.cs
│   ├── EntityManager.cs
│   └── HighScoreStore.cs
├── Entities/
│   ├── Player.cs
│   ├── Bar.cs
│   ├── Bullet.cs
│   ├── Particle.cs
│   ├── BarType.cs
│   ├── PowerUp.cs
│   └── PowerUpType.cs
├── Systems/
│   ├── SpawnSystem.cs
│   ├── CollisionSystem.cs
│   └── DifficultySystem.cs
├── Rendering/
│   └── RenderSystem.cs
├── Audio/
│   ├── GameAudio.cs
│   └── SoundGenerator.cs
├── UI/
│   └── GameForm.cs
└── readme.md
```

At runtime, **`highscore.json`** may appear next to **`RetroArcade.exe`** after leaderboard updates are saved.

Example `highscore.json`:

```json
{
  "Entries": [
    { "Name": "ACE", "Score": 128, "DateUtc": "2026-04-24T19:20:00Z" },
    { "Name": "PLAYER", "Score": 97, "DateUtc": "2026-04-24T18:05:12Z" },
    { "Name": "KAI", "Score": 86, "DateUtc": "2026-04-23T22:11:45Z" }
  ]
}
```

### Source files (reference)

| File | Role |
|------|------|
| `ArcadeGame.csproj` | `net8.0-windows`, `WinExe`, `UseWindowsForms`, nullable, implicit usings. |
| `Program.cs` | `[STAThread]`; starts **`Game.UI.GameForm`**. |
| `UI/GameForm.cs` | **`Forms.Timer`** tick, **`Stopwatch`** delta per tick, keys, **Start / Continue / Play again** button flow, status strip, clip + **`Game.Core.GameManager.Draw`**, FPS sample for debug. |
| `Core/GameManager.cs` | **`DamagePerHit`**; **`Game.Entities.Player`**; composes **`Game.Core.GameState`**, **`Game.Core.EntityManager`**, **`Game.Systems.DifficultySystem`**, **`Game.Systems.CollisionSystem`**, **`Game.Systems.SpawnSystem`**, **`Game.Rendering.RenderSystem`**. Floor-hit handling respects dev mode; **`ToggleDevMode`** / **`TryDevActivatePowerUpDigit`**. |
| `Core/GameState.cs` | Run data; reset flows; combo/lives/high-score/new-best/pause flags; leaderboard visibility flag; difficulty and cadence fields; **`IsDevMode`** and effective spawn/cap helpers for dev tuning. |
| `Core/EntityManager.cs` | **`Bars`**, **`Bullets`**, **`Particles`**, **`Fragments`**; update and culling helpers. |
| `Entities/Fragment.cs` | Shared rectangular debris effect entity (position, velocity, size, gravity, lifetime, fade basis). |
| `Core/HighScoreStore.cs` | JSON load/save for Top-10 leaderboard entries (with legacy migration), plus normalization + best-score-per-name logic. |
| `Core/GameConfig.cs` | Centralized tunable constants grouped by gameplay area (player, bars, spawn, difficulty, power-ups, effects, visuals, persistence). |
| `Systems/SpawnSystem.cs` | **`TrySpawn`**, overlap/spacing validation, overlap-aware spawn Y stacking, types/heights, **`BarWidth`**. |
| `Systems/CollisionSystem.cs` | **`Resolve`**: hits, audio/particles/fragments, destroy hooks, bomb wave queue, and staggered bomb fragmentation. |
| `Systems/DifficultySystem.cs` | Table-driven difficulty progression with time-keyframe interpolation for speed/spawn/cap. |
| `Rendering/RenderSystem.cs` | Full GDI+ draw path (world, HUD, overlays, debug). |
| `Entities/*` | Domain entities: player, bars (off-screen spawn + visible clipped bounds), bullets, particles, bar type enum. |
| `Audio/*` | Procedural SFX (`GameAudio`, `SoundGenerator`). |

**Data flow:** `Timer.Tick` → **`Stopwatch`** elapsed → **`GameManager.Update(..., deltaSeconds)`** → **`Invalidate`** → **`OnPaint`** → **`RenderSystem.Draw`** (via **`GameManager.Draw`**).

---

## Architecture & design

### Goals

- **Readable** — Small types, explicit wiring in **`GameManager`**, no DI container, no ECS.
- **WinForms-first** — Single UI thread; **`Forms.Timer`** drives sim cadence; **`Invalidate`** + **`OnPaint`** drive draw.
- **BCL-only** — Drawing, audio, JSON only from the framework.

### Layering

| Layer | Responsibility |
|--------|----------------|
| **`GameForm`** | Window, keyboard set, **`Timer`** + **`Stopwatch`**, Start/Continue/Play again button flow, status text, play height, clip, invalidate. |
| **`GameManager`** | Orchestration: call order for state, entities, systems; owns **`Player`** and firing cooldowns. |
| **`GameState`** | Authoritative mutable run fields (no systems referenced). |
| **`EntityManager`** | Shared runtime collections (bars, bullets, particles, fragments, power-ups, explosion FX) plus movement/cull helpers. |
| **`SpawnSystem`**, **`CollisionSystem`**, **`DifficultySystem`**, **`RenderSystem`** | Use state + entities (+ **`Random`** where needed); plain constructors. |
| **`GameAudio` / `SoundGenerator` / `HighScoreStore`** | SFX and persistence at the edges. |

Namespace map (matches folders):
- `Game.Core` → orchestration and run state
- `Game.Entities` → player/bars/bullets/particles
- `Game.Systems` → spawn/collision/difficulty rules
- `Game.Rendering` → draw pipeline and HUD/overlays
- `Game.Audio` → procedural SFX
- `Game.UI` → WinForms host form
- `Game` → entry point (`Program.cs`)

```mermaid
flowchart TB
  subgraph Host["GameForm"]
    K["Keys in HashSet"]
    T["Timer.Tick"]
    O["OnPaint"]
  end
  subgraph Orchestrator["GameManager"]
    U["Update"]
    D["Draw"]
  end
  subgraph Systems["Systems"]
    R["RenderSystem"]
    Sp["SpawnSystem"]
    Co["CollisionSystem"]
    Di["DifficultySystem"]
  end
  K -.-> T
  T --> U
  U --> Sp
  U --> Co
  U --> Di
  U -->|after entities| O
  O --> D
  D --> R
```

### Simulation order (`GameManager.Update`)

1. **Guards** — early return if not playing, paused, or playfield too short.
2. **`GameState.AdvanceFrame(deltaSeconds)`** — frame index and transient timer decay.
3. **Life-lost gate** — if `IsLifeLost`, return (normal gameplay simulation is paused).
4. **Player phase** — bottom align, movement steps, and firing cadence.
5. **Entity phase** — update bullets/particles/fragments/power-ups; collect pickups; tick pending bomb fuse.
6. **Bar phase** — retarget active bar speed and move bars.
7. **Floor-hit phase (before bullet collisions)** — bars that reach floor consume shield/life or trigger game over.
8. **Combat phase** — `CollisionSystem.Resolve` then explosions and delayed bomb kills.
9. **Spawn phase** — `SpawnSystem.TrySpawn` when not game over.
10. **Difficulty phase** — `DifficultySystem.Tick` samples table and smooths toward targets.

### Render order (`RenderSystem.Draw`)

Shake transform → background + grid → danger line → bars (fill + outline + hit pulse) → particles + fragments → bullet trails → bullets (cyan default vs **magenta pierce** + glow) → player + muzzle + flash → **shield ring / pickup pulse / block burst (player)** → **state-based UI**: game-over overlay now uses a top **FINAL RESULTS** panel plus leaderboard panel beneath, matching border/background styling → debug (hidden while leaderboard overlay is shown).

### Difficulty design (summary)

| Lever | Role |
|--------|------|
| **Table source of truth** | `GameConfig.Difficulty.Table` defines target pace over elapsed seconds. |
| **Interpolation** | `DifficultySystem` linearly samples between neighboring keyframes. |
| **Smoothing** | Current values blend toward sampled targets (`BarSpeedBlend`, `SpawnIntervalBlend`, `MaxBarsBlend`) to avoid visible jumps. |
| **Dev-mode caps** | Dev mode clamps speed/cap for easier testing (`DevMaxBarSpeed`, `DevMaxBarsOnScreen`, `DevMinSpawnIntervalFrames`). |

### Audio

**`SoundGenerator`** writes RIFF/WAVE PCM into **`MemoryStream`**, **`Load`**, **`Play()`**; streams kept alive per pool slot until replaced. **`GameAudio`** is the gameplay-facing API.

### Persistence

**`HighScoreStore`** is the JSON touchpoint for leaderboard data (`highscore.json`).  
`GameState.ResetRun` reads best score from leaderboard for HUD initialization, and game-over submission writes updates through `TryAddScore(...)` with best-score-per-name entries.

---

## Implementation overview

| Concern | Primary types |
|--------|----------------|
| **Lifecycle / attract / new run** | **`GameManager`** (`EnterAttractMode`, `StartNewGame`, `ResetState`, `Initialize`). |
| **Authoritative run numbers** | **`GameState`** (score, lives, flags including `IsLifeLost`, spawn countdown/interval, `BarSpeed`, combo, active power-up/timer, shake/flash, input timers). |
| **World lists** | **`EntityManager`** (bars, bullets, particles, fragments, power-up drops, explosion FX—**not** the player). |
| **Creating enemies** | **`SpawnSystem`** (random + lane + cluster + overlap + cap). |
| **Hits & destroys** | **`CollisionSystem.Resolve`**: normal bullets stop on first bar; **piercing** bullets use **`Bullet.ConsumePierce`** and a repeat pass. Bullet hits spawn subtle directional fragments; bomb destruction uses staggered radial fragmentation for high-impact breakup. |
| **Power-up activation** | **`GameManager`** + **`GameState`** (pickup activates immediately; one active power-up at a time). **Shield**: one charge (**`ShieldCharges`**), HUD **SHIELD**, cyan ellipse + glow on the player (**`RenderSystem.DrawShieldPlayerFx`**), pickup pulse + **`GameAudio.PlayShieldPickup`**, floor block via **`TryConsumeShield`** (ring burst, flash, **`PlayShieldBlock`**). **Bomb**: fuse + **`ExecuteBombExplosionAt`**, then staggered **`TickPendingBombKills`** + screen flash / heavy shake + **`ExplosionFx`**. |
| **Difficulty table + interpolation** | **`DifficultySystem`**. |
| **Drawing** | **`RenderSystem`** (all GDI+ for the playfield). |

**Construction order inside `GameManager`:** `DifficultySystem` → `SpawnSystem` → `CollisionSystem` (needs difficulty and spawn for post-destroy side effects) → `RenderSystem` (stateless).

---

## Requirements

- **OS:** Windows (WinForms + `net8.0-windows`).
- **SDK:** [.NET 8 SDK](https://dotnet.microsoft.com/download) (or compatible tooling that can build this TFM).
- **IDE (optional):** Visual Studio 2022+ (.NET desktop workload), or VS Code + C# + `dotnet` CLI.

---

## Build and run

### .NET CLI

```bash
dotnet build
dotnet run
```

Release example: `dotnet run -c Release`

Output: `bin/<Configuration>/net8.0-windows/RetroArcade.exe` (see **`AssemblyName`** in the csproj).

### Visual Studio

Open **`ArcadeGame.sln`**, set startup project, F5 or Ctrl+F5.

---

## Controls

| Action | Input |
|--------|--------|
| **Move left** | **Left** or **A** |
| **Move right** | **Right** or **D** |
| **Fire** | **Hold Space** (~100 ms cooldown) |
| **Pause / resume** | **ESC** (disabled during life-lost and game-over screens) |
| **Lives** | Start with **3**; life loss clears only playfield + temporary timers (score/difficulty preserved) |
| **Continue after life loss** | **Enter** or **Space**, or click **Continue** button |
| **Start / play again** | Button or **Enter** (`AcceptButton`) |
| **Debug overlay** | **F1** (hitboxes, FPS, counts) |
| **Dev mode** | **Debug configuration only:** **F2** toggles dev on/off. **1–6** grant power-ups during play when dev is on—see [Dev mode hotkeys](#dev-mode-hotkeys-debug-builds-only). **Release:** no keyboard toggle; **`IsDevMode`** stays false. |
| **Focus** | Form calls **`Focus()`** on start; click client/title if keys stop responding |

### Dev mode hotkeys (Debug builds only)

These keys exist only in **Debug** builds (`#if DEBUG` in **`GameForm`**). **`GameManager.TryDevActivatePowerUpDigit`** maps digits to **`PowerUpType`** ([`GameManager.cs`](Core/GameManager.cs)). Use **main row** **1–6** or **NumPad 1–6**. Requires **dev mode on** (**F2**), **`IsPlaying`**, and not paused / life-lost.

| Key | Grants (`PowerUpType`) | HUD label (after grant) | Notes |
|-----|------------------------|-------------------------|--------|
| **F2** | — | — | Toggles **`IsDevMode`** on/off. |
| **1** | `RapidFire` | **RAPID** | Faster fire for a timed window. |
| **2** | `MultiShot` | **MULTI** | Triple shot for a timed window. |
| **3** | `BombShot` | *(none — bomb is not a timed HUD buff)* | Same as a normal pickup: random fuse point in the playfield, then AoE explosion (no shooting). |
| **4** | `PiercingShot` | **PIERCE** | Timed buff: new bullets get pierce budget (**up to 3 bars** per shot with current **`GetPierceCount()`** = 2), magenta visuals + **`PlayPierceHit`**. |
| **5** | `Shield` | **SHIELD** | One shield charge (no timer bar). |
| **6** | `SlowMotion` | **SLOW** | Slower bar fall for a timed window. |

---

## UI layout

- **Default client size** — `480×640` (client area, excluding window chrome).
- **Play area** — Full width × (client height − status bar, height **30**).
- **HUD (in-play overlay)** — Top-left lives label + heart icons, top-right score box, best score text under it, combo text when active, and a **centered power-up card** (large glyph, uppercase short label, optional timer bar). Empty slot shows a dim placeholder ellipse. Temporary banners like **NEW BEST!** when applicable. **Dev mode** adds a small **DEV MODE** block (lower-left of the play area, above the F1 FPS line) with live tuning readouts.
- **Active power-up card (top-center)** — Implemented in **`RenderSystem.DrawPowerUpHud`** / **`DrawActivePowerUpCard`**. Layout: **~36×36 px** colored glyph inside a white ring, **7 pt bold** label centered under the icon, **4 px** timer strip when the run state exposes a finite duration. **Type → color / symbol / label** (all `System.Drawing` primitives):

  | `PowerUpType` | HUD label | Color (fill) | Glyph idea |
  |---------------|-----------|----------------|------------|
  | `RapidFire` | RAPID | Orange | Filled circle + forward streak lines |
  | `MultiShot` | MULTI | Deep sky blue | Three filled circles in a row |
  | `PiercingShot` | PIERCE | Medium purple | Diamond (rotated square) |
  | `Shield` | SHIELD | Light cyan (`ARGB` ~70,210,255) | Curved “shield” path (`GraphicsPath`) |
  | `SlowMotion` | SLOW | Cyan | Filled circle + clock arc + hand |
  | `BombShot` | BOMB | Orange red | Oval body + fuse line |

- **Start button** — Centered horizontally, above bottom margin in play coords; hidden during a run; flat style with hover.

---

## How the game loop works

1. **`Timer.Tick`** → **`GameForm`** reads **`HashSet<Keys>`** and **`Stopwatch`** elapsed → **`GameManager.Update(w, h, left, right, fire, deltaSeconds)`** → **`Invalidate`** → **`OnPaint`**.
2. **`Update`** early-outs if not playing, paused, or playfield too short.
3. Subsystems run in the order listed under [Simulation order](#architecture--design).
4. **`Invalidate`** → **`OnPaint`** clips to play rect → **`GameManager.Draw`** → **`RenderSystem.Draw`**.
5. All work stays on the **UI thread**.

---

## Tuning & constants

| Area | Where to look |
|------|----------------|
| **Damage per hit** | **`GameConfig.Scoring`** (`DamagePerHit`). |
| **Fire cadence** | **`GameConfig.Player`** base fire cooldown + **`GameState`** scaling helpers. |
| **Player step / cooldown** | **`GameConfig.Player`** base step cooldown; step size derives from **`GameConfig.Bars.Width / 2`**. |
| **Bullet size / speed** | **`GameConfig.Player`** bullet fields. |
| **Player size** | **`GameConfig.Player`** ship dimensions. |
| **Difficulty table** | **`GameConfig.Difficulty.Table`** keyframes sampled by **`DifficultySystem`**. |
| **Spawn timing** | **`DifficultySystem`** maps difficulty to target spawn interval; **`SpawnSystem`** still handles retry behavior, cluster/lane chances, and placement attempts. |
| **Max bars on screen** | **`DifficultySystem`** drives `GameState.DynamicMaxBarsOnScreen`; `SpawnSystem` uses `EffectiveMaxBarsOnScreen` (dev mode still caps). |
| **Dev mode** | **`GameState.IsDevMode`** (forced **off** on **`ResetRun`** in Release via **`#if !DEBUG`**). **`GameManager.ToggleDevMode`** and **`TryDevActivatePowerUpDigit`** bodies are **`#if DEBUG`** only; **`GameForm`** wraps **F2** and digit handling in **`#if DEBUG`**. Overlay **`RenderSystem.DrawDevModeOverlay`**. |
| **Bomb pickup** | Fuse + **`ExecuteBombExplosionAt`**: splash immediately; **kill queue** + **`TickPendingBombKills`** (stagger **3** frames between cull destroys). **`GameState`**: **`TriggerBombImpactFx`**, **`BombScreenFlashFrames`**, **`BombHeavyShakeFrames`**. **`ExplosionFx`**: ~**48** frames, **`MaxWaveRadius`** ~**168**. |
| **Fragment FX** | `CollisionSystem` controls spawn profiles and cap (`MaxActiveFragments`), `EntityManager.UpdateFragments` handles per-frame motion + cleanup, and `RenderSystem.DrawFragments` handles fade rendering. Bullet profile: low-count, short-lived, directional. Bomb profile: higher-count, radial, longer-lived with gravity. |
| **Combo** | **`GameState`** — `ComboTimeWindowFrames`, `ComboMaxMultiplier`. |
| **Bar height / type odds** | **`GameConfig.Bars`** ranges/thresholds/scales, applied by **`SpawnSystem`**. Bars spawn above-screen with overlap-aware stacking (`min(-height, highestOverlappingY - height)`), and `Bar.GetBounds()` returns visible clipped bounds for render + collision. |
| **Window / timer / status** | **`GameConfig.Ui`** defaults consumed by **`GameForm`**. |

### `GameConfig` constant reference

All tunable constants are in [`Core/GameConfig.cs`](Core/GameConfig.cs).

- **`Persistence`** — `LeaderboardMaxEntries` (Top-N size), `HighScoreFileName` (local JSON filename), `DefaultPlayerName` (fallback nickname), `NameInputMaxLength` (nickname max chars).
- **`Scoring`** — `DamagePerHit` (damage per bullet), `ComboMaxMultiplier` (combo cap), `ComboTimeWindowFrames` (combo chain window), `SpecialBarScoreMultiplier` (special bar reward factor), `StartingLives` (initial lives per run).
- **`Player`** — `Width`/`Height` (ship size), `MuzzlePortWidth`/`MuzzlePortYOffset`/`MuzzlePortHeight` (muzzle rect), `FireCooldownMs` (base firing cadence), `StepCooldownMs` (base movement cadence), `MultiShotSpreadDrift` (angled side bullet drift), `MultiShotOffsetX` (side bullet horizontal spacing), `BulletWidth`/`BulletHeight`/`BulletSpeed` (base bullet shape/speed), `PiercingBulletSizeBoost` (pierce bullet size bonus), `MuzzleFlashFrames` (shot flash duration), `MinPlayHeightPadding` (minimum playable vertical margin).
- **`Spawn`** — `InitialSpawnCountdownFrames` (startup grace delay), `RetryFramesWhenNoFit` (retry delay when spawn fails), `PlacementAttempts` (random placement tries), `HorizontalPadding` (lane spacing), `ClusterChance`/`LaneChance` (pattern probabilities), `ClusterExtraMin`/`ClusterExtraMaxExclusive` (extra bars per cluster), `ClusterOffsetMin`/`ClusterOffsetMaxExclusive` (cluster X spread), `FallbackHeight` (fallback bar height), `MinBarHeightClamp`/`MaxBarHeightClamp` (safety clamp), `InitialSpawnIntervalFrames` (initial cadence target).
- **`Bars`** — `Width` (bar width), `MinHeight`/`MaxHeight` (normal height band), `FastMinHeight`/`FastMaxHeightExclusive` and `TankMinHeight`/`TankMaxHeightExclusive` (type-specific bands), `FastSpeedScale`/`TankSpeedScale`/`NormalSpeedScale` (per-type speed multipliers), `SpecialChance` (special bar probability), `RollNormalThreshold`/`RollFastThreshold` (type roll cutoffs), `SpeedLerpFactor` (speed target blending), `SpeedSnapEpsilon` (snap threshold), `HitFlashFrames`/`HitPulseFrames` and `PierceHitFlashFrames`/`PierceHitPulseFrames` (hit feedback timing).
- **`PowerUps`** — `DropChance`/`ForceDropAfterBars` (base drop behavior), `DevDropChance`/`DevForceDropAfterBars` (dev-mode drop behavior), `DropSize`/`DropFallSpeed`/`DropYDivisor` (pickup spawn shape/motion), `OffscreenCullPadding` (cleanup threshold), `RapidFireDurationFrames`/`MultiShotDurationFrames`/`PiercingDurationFrames`/`SlowMotionDurationFrames` (timed effect durations), `ShieldCharges` (shield strength), `ShieldPickupFlashFrames`/`ShieldBlockFlashFrames`/`ShieldBlockRingFrames`/`ShieldImpactShakeFrames`/`ShieldBlockShakeMs` (shield feedback), `PierceCount` (pierce budget), `SlowMotionDivisor` (bar-speed reduction factor).
- **`Difficulty`** — `MinBarSpeed`/`MaxBarSpeed` (speed bounds), `DevMaxBarSpeed` (dev cap), `MinSpawnIntervalFrames`/`MaxSpawnIntervalFrames` (cadence bounds), `MinBarsOnScreen`/`MaxBarsOnScreen` (spawn cap bounds), `DevMaxBarsOnScreen`/`DevMinSpawnIntervalFrames` (dev constraints), `BarSpeedBlend`/`SpawnIntervalBlend`/`MaxBarsBlend` (smoothing factors), `DifficultyTimeScaleFrames` (frames→seconds scale), `InterpolationSpanEpsilon` (Lerp safety epsilon), `Table` (time-keyed difficulty entries of `Seconds`, `BarSpeed`, `SpawnIntervalFrames`, `MaxBarsOnScreen`).
- **`Effects`** — `BombRadius`/`BombSplashDamage`/`BombSoleBarDamage`/`BombSingleTargetBonusDamage` (bomb damage profile), `BombKillFraction` (fraction of bars queued for kill), `BombKillDelayStartFrames`/`BombKillStaggerFrames` (bomb stagger timing), `MaxActiveFragments` (fragment cap), `BombExplosionShakeMs`/`BombScreenFlashFrames`/`BombHeavyShakeFrames` (bomb impact feedback), `LifeLostShakeMs`/`FloorHitDevShakeMs`/`GameOverShakeMs`/`DestroyShakeMs` (shake durations), `SpawnRelaxAfterLifeLostFrames` (post-life-loss spawn relief), `LifeLostFlashFrames`/`NewBestFlashFrames` (overlay timing), `BombIndicatorMargin`/`BombFuseMinFrames`/`BombFuseMaxExclusiveFrames` (bomb indicator placement/fuse range).
- **`Visual`** — `SpecialBarPulseRate`/`SpecialBarPulsePhaseByX` (special bar pulse timing/phase), `SpecialBarNeonMix` (base-to-neon blend), `SpecialBarBrightnessBase`/`SpecialBarBrightnessRange` (brightness pulse envelope), `SpecialOutlinePulseRate` (outline pulse speed), `SpecialOutlineAlphaBase`/`SpecialOutlineAlphaRange`/`SpecialOutlineGreenBase`/`SpecialOutlineGreenRange`/`SpecialOutlineBlueBase`/`SpecialOutlineBlueRange` (outline color animation), `BombGridStep` (background grid spacing).
- **`Ui`** — `WindowWidth`/`WindowHeight` (default client size), `GameTimerIntervalMs` (tick interval), `MinPlayHeight` (min drawable playfield), `MinDeltaSeconds`/`MaxDeltaSeconds` (delta-time clamp), `FpsWindowMs` (FPS averaging window).

Rebuild after edits and smoke-test movement, spawn cap, combo, power-up activation, audio, and high score save.

---

## Learner Audit Notes

### Design practices used in this codebase

- **Single orchestrator** — `GameManager` is the update-order authority and gameplay coordinator.
- **State bag + focused systems** — `GameState` stores mutable run data; `SpawnSystem`, `CollisionSystem`, and `DifficultySystem` apply rules against shared state/entity collections.
- **Centralized tuning** — `GameConfig` is the single tuning source; systems should consume config values rather than introducing local magic numbers.
- **Immediate-mode rendering** — one paint path (`GameForm.OnPaint -> GameManager.Draw -> RenderSystem.Draw`) with deterministic draw order.
- **Thin platform wrappers** — `GameAudio` exposes gameplay-facing sound calls while `SoundGenerator` handles low-level PCM generation/playback.

### Important behavior caveats

- **Frame ordering matters** — floor-hit checks run before bullet collisions in the same frame, so a bar can still cost a life even if a bullet would also intersect it that frame.
- **Mixed time sources** — simulation is largely frame/cooldown driven; `LastDeltaSeconds` is tracked but most movement logic is not delta-time physics.
- **Visible vs full bar bounds** — gameplay collisions use visible clipped bar bounds (`GetBounds`), while spawn overlap checks use full bounds (`GetFullBounds`).
- **Piercing behavior detail** — piercing bullets can process repeated collision passes while remaining intersecting, so “pierce” currently means extra collision iterations, not strictly guaranteed forward travel to a different bar.
- **UI life icon count** — life rendering currently assumes 3 icons in HUD/life-lost overlay; if you change starting lives, update corresponding render layout.
- **Leaderboard highlight nuance** — game-over leaderboard “current run” highlight matches by score value; ties can highlight an existing entry.

### Entry-point and threading assumptions

- This is a WinForms UI-thread game loop (`[STAThread]` + `Application.Run(new GameForm())`).
- Update, draw, input handling, and local persistence all run on the same thread.
- Keep long/blocking work out of the tick path to avoid frame hitches.

### Troubleshooting

- **Dev hotkeys do nothing** — F2 and numeric power-up hotkeys are `DEBUG`-only.
- **Movement feels misaligned with lanes** — confirm `GameConfig.Bars.Width` and player step derivation (`width / 2`) remain consistent.
- **Difficulty feels too abrupt** — adjust `GameConfig.Difficulty.Table` first, then smoothing factors.
- **No leaderboard file appears** — `highscore.json` is written after a qualifying game-over save path is reached.
- **Keys stop responding** — refocus the game window (`Focus()` is called on start, but OS focus can still be lost).
- **Audio cuts under stress** — tune `SoundGenerator` pool/gain/jitter (`PoolSize`, `MasterGain`, pitch jitter), and note one-shot errors are intentionally swallowed.

---

## Design notes & limitations

- **Teaching focus** — Prefer reading **`GameManager`** then one system at a time; no hidden magic from frameworks.
- **Rendering** — One paint path through **`RenderSystem`**; no sprite controls.
- **Threading** — Single-threaded; huge entity counts could stutter.
- **Audio** — Procedural only; no bundled WAV assets.
- **Resize** — Fixed form border in the sample; resize still reclamps the player via **`OnClientResize`**.
- **Persistence** — Only local **`highscore.json`** leaderboard data; no settings file.
- **Growth** — Further splits can stay in the same assembly (partial classes or helpers) without introducing a full engine.

---

## License

No license file is provided in this sample repository. If you distribute or share the code, add a `LICENSE` file and/or copyright notice as appropriate for your use case.

---

## Credits

**Retro Blaster** — A minimal WinForms + GDI+ arcade sample: **`GameForm`** for hosting, **`GameManager`** for orchestration, **`GameState`** + **`EntityManager`** + **systems** for rules and presentation, and **`SoundGenerator`** for lightweight SFX.

For changes, run **`dotnet build`** frequently to catch regressions early.
