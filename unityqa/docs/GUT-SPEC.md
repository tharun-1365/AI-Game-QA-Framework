# GUT-SPEC — BenchGame Specification (FR-1.19, FR-1.20)

**Status:** PROVISIONAL — constants below are the authored Milestone 1 values.
They become AUTHORITATIVE only after AC-14's in-engine verification (measured
apex height and jump distance within 5% of the derived values), which happens
in the milestone that brings the instrumentation online.

## Movement constants (authored)

| Constant | Value | Where set |
|---|---|---|
| Run speed | 6.0 u/s | PlayerController2D → Run Speed |
| Jump apex height | 2.2 u | PlayerController2D → Jump Height (velocity is derived: v = √(2·g·h)) |
| Gravity scale | 3.0 | Player's Rigidbody2D → Gravity Scale |
| Project gravity | (0, −9.81) u/s² | Edit → Project Settings → Physics 2D (default, unchanged) |
| Fixed timestep | 0.02 s (50 Hz) | Project Settings → Time (default, unchanged — FR-1.19) |
| Tile size | 1.0 u | Grid cell size (default) |
| Player collider | BoxCollider2D 0.9 × 0.9 u | Player |

## Derived kinematics (computed from the above — the numbers Module 4 will use)

| Quantity | Formula | Value |
|---|---|---|
| Effective gravity g | 9.81 × 3.0 | 29.43 u/s² |
| Jump initial velocity v₀ | √(2·g·h) = √(2 × 29.43 × 2.2) | ≈ 11.38 u/s |
| Time to apex | v₀ / g | ≈ 0.387 s |
| Full-jump airtime (flat ground) | 2 · v₀ / g | ≈ 0.773 s |
| **Max jump height** | authored | **2.2 u** (clears a 2-tile wall; cannot clear 3) |
| **Max jump distance** (center travel, flat, full run speed) | airtime × runSpeed ≈ 0.773 × 6.0 | **≈ 4.64 u** |
| **Max clearable gap width** | center travel + takeoff/landing collider overhang (≈ 0.275 + 0.45) | **≈ 5.3 tiles → a 4-tile gap clears, a 6-tile gap does not; 5 is marginal and excluded from verification** (see MODULES.md D-005) |

## Determinism guarantees (FR-1.19)

No randomness anywhere in BenchGame. All gameplay physics in FixedUpdate on the
default fixed timestep. Input sampled in Update, applied in FixedUpdate
(latched jump request). `GetAxisRaw` (unsmoothed) only. Camera easing is
presentation-only and exempt.

## In-engine verification (AC-14) — TO BE FILLED

| Quantity | Spec | Measured | Delta | Pass (≤5%)? |
|---|---|---|---|---|
| Max jump height | 2.2 u | TBD | TBD | TBD |
| Max jump distance | ≈ 4.64 u | TBD | TBD | TBD |

Interim tile-ruler verification (Milestone 1, VC-6/VC-7): 2-tile step jumpable,
3-tile wall not; 4-tile gap clearable at full speed, 6-tile gap not.
The verification geometry is authored in code:
`Assets/TestGame/Editor/LevelBaselineBuilder.cs → PaintGeometry()`.

## Feature list (frozen per SRS §1.1)

Implemented in Milestone 1: player (run + single fixed-height jump), tilemap
geometry, ground collision, follow camera.
Deferred to later Module 1 milestones: spikes, kill zones, checkpoints, tokens,
level exit, death/respawn, GameManager.
Excluded forever (scope tripwire): variable-height jump, coyote time, jump
buffering, dashes, enemies, moving platforms, menus, audio, real art.
