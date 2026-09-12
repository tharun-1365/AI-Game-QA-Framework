# BENCHMARK — Planted-Bug Ground Truth (FR-1.16)

**Status: ACTIVE — M6 Slice A.** This file is the authoritative answer key
for the planted-bug regression benchmark (SRS §13): nothing may be planted
that is not recorded here, and nothing may be recorded here that is not
planted. The planted level is `Level_PlantedBugs_A.unity`, built entirely by
`Assets/TestGame/Editor/PlantedBugLevelBuilder.cs` (menu **BenchGame ▸ Build
Level_PlantedBugs_A From Scratch**) — the builder source is the
version-controlled definition of every plant. `Level_Benchmark` remains the
100% clean baseline and is never modified.

**Ground truth vs. detection — read this first.** Every entry below is a
GROUND-TRUTH statement: *the bug exists, here, planted this way* (proven by
the M6.A EditMode/PlayMode tests). No entry claims the framework has
DETECTED anything. Detection claims require M6-B (the oracles that judge
these classes) and M6-C (the evaluation run that scores detections against
this answer key); the **Detected** column stays `pending` until an actual
M6-C run fills it with evidence.

## Registry

| ID | Class | Ground-truth location (marker) | Planting method | Expected symptom | Expected evidence (existing schema) | Expected detector | Severity | Status | Detected |
|---|---|---|---|---|---|---|---|---|---|
| PB-001 | Collider gap / fall-out-of-world | Platform 4 far tile, cell x27 (`PB_001_ColliderGap`) | Cell x27 painted on a render-only tilemap (`Ground_Tilemap_Visual`, no collider, off the Ground layer); collider tilemap ends at x26 | Player runs onto visually solid ground and falls out of the world | `RunEnded {outcome:"OutOfBounds"}` + `PlayerDied {cause:"outOfBounds"}`; replay of a clean golden run diverges at the fall | HazardOracle (exists, M5-D) via M6-C evaluation | Critical | planted (M6.A) | pending |
| PB-002 | Soft lock | Sealed basin under gap 1, x7–9 (`PB_002_SoftLockPit`) | Basin dug into the first gap: floor y=−3, walls flush with platform tops; depth 3 u > max jump 2.2 u; floor above killY=−5 | Player enters by falling, survives, and can never progress or escape | NO terminal `RunEnded` ever; telemetry position variance → 0 inside x≈7–10 while `InputSample`s continue | SoftLockOracle (M6-B, planned) | Critical | planted (M6.A) | pending |
| PB-003 | Hazard on golden path (regression) | Platform 3 walking surface, x18.5 (`PB_003_SpikeOnGoldenPath`) | Third spike `Spike_C` at (18.5, 2.5) — clean platform 3 carries no hazard | A clean-level golden run replayed here dies mid-route | Original `Success` vs replay `SpikeDeath`: validation.json v2 `outcomeMatch:false`; `PlayerDied {cause:"spike"}` | Outcome comparison (exists, M5-D) + HazardOracle, via M6-C | Major | planted (M6.A) | pending |
| PB-004 | Missing trigger | ExitDoor at (33.5, 2) (`PB_004_MissingExitTrigger`) | Door intact and visually normal; its trigger `BoxCollider2D` is **disabled** (the SRS §13 BUG-004 method) | Player reaches and overlaps the exit; nothing fires; the run never completes | Zero `TriggerFired` and zero terminal `RunEnded` in a session that reaches the door's position | MissingTriggerOracle (M6-B, planned); CompletionOracle skips (no outcome) | Major | planted (M6.A) | pending |

Marker convention (SRS §13 rule 5): each site has an empty, `EditorOnly`-tagged
transform named `PB_00x_…` under `PlantedBugMarkers` — stripped from player
builds, invisible in the Game view, never read by any runtime system. The
answer key exists for humans and for the EditMode ground-truth tests only.

## Manual reproduction (controls: A/D or ←/→ move, Space jump; F9 start/stop
session; Escape ends a run as Quit)

**PB-001 — fall through the far end of platform 4.**
1. F9. Follow the normal route: jump gap 1, cross platform 2 (jump Spike_A),
   jump up to platform 3, jump over the mid-platform spike (Spike_C), jump
   down to platform 4.
2. Jump over Spike_B and land on the far end of platform 4.
3. Keep running right onto the last tile. It looks solid; you fall through.
4. Observe `Run ended — OutOfBounds` in the Console. F9.

**PB-002 — walk into the pit.**
1. F9. From spawn, walk right off the edge of the spawn platform WITHOUT
   jumping.
2. You land alive on the basin floor. Try to escape: run both directions,
   jump repeatedly — every jump peaks below the rim (depth 3 u vs apex 2.2 u).
3. No outcome ever occurs; the session keeps running. F9 to end it (or
   Escape to record a Quit).

**PB-003 — run into the mid-route spike.**
1. F9. Jump gap 1, cross platform 2 (jump Spike_A), jump up to platform 3.
2. Run right along platform 3's surface without jumping.
3. Observe `Run ended — SpikeDeath` at x≈18.5. F9.

**PB-004 — the door that never fires.**
1. F9. Complete the full route (jump Spike_C on platform 3; on platform 4
   jump over Spike_B, land on the far end, and jump IMMEDIATELY at full
   speed to clear both the fake tile and the last gap — the early take-off
   reaches the exit platform).
2. Walk into the green door. Nothing happens; the run stays Running.
3. F9 to end the session (no Success, no TriggerFired anywhere in it).

## Golden-run protocol (binding for M6-C)

Golden runs are recorded on the CLEAN `Level_Benchmark` and replayed against
the planted level. Two protocol requirements make the answer key's
regression guarantees hold: (1) golden runs traverse platform surfaces on
foot between jumps (PB-003's kill window is the grounded crossing of
x≈18.1–18.9 on platform 3); (2) golden runs take off for the final gap from
platform 4's far edge (cell x27 — the natural take-off tile), which is
exactly the cell PB-001 removes from the collider map. A run ending in Quit
is not comparable (Escape is outside the input seam — M5-D rule).

## Original Module-1 taxonomy (historical; superseded by the M6 registry)

The v1.0 plan targeted six classes via an autonomous explorer. The explorer
was scrapped (D-011 roadmap); the M6 benchmark evaluates the replay-
regression pipeline instead, and each planted bug maps back as follows:
BUG-001/BUG-005 (fall-out-of-world / collider gap) → PB-001 · BUG-002 (soft
lock) → PB-002 · BUG-004 (missing trigger) → PB-004 · hazard regression
(new class, replay-specific) → PB-003. BUG-003 (unreachable area) and
BUG-006 (invisible wall) are **deferred**: both need coverage-style evidence
(where the player COULD go) that the current session record does not carry;
they return if/when a coverage layer exists, and are disclosed as out of
scope in the paper until then.

## Calibration rules (SRS §13, binding — unchanged)

1. Every bug must be reproducible by an ordinary human player (verified: AC-7).
2. Every bug must be invisible to a casual glance — the level must look legitimate.
3. `Level_Baseline` and `Level_Benchmark` stay 100% clean.
4. Plants are scene-local edits only — never shared prefabs/tiles (risk R4).
5. Each site gets an EditorOnly `PB_xxx` marker (runtime must never see the answer key).
