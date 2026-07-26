# BENCHMARK — Planted-Bug Ground Truth (FR-1.16)

**Status:** PLACEHOLDER — bugs are planted in a later Module 1 milestone
(bug injection). This file is the authoritative answer key for the entire
project (SRS §13); nothing may be planted that is not recorded here, and
nothing may be recorded here that is not planted.

## Registry (to be completed at planting time)

| ID | Class | Level | Planting method | Ground-truth location | Expected symptom | Detected by (future) | Severity | Status |
|---|---|---|---|---|---|---|---|---|
| BUG-001 | Fall-out-of-world | Level_PlantedBugs_A | TBD | TBD | TBD | M4 OutOfBounds | Critical | not planted |
| BUG-002 | Soft lock | Level_PlantedBugs_A | TBD | TBD | TBD | M4 Stuck/SoftLock | Critical | not planted |
| BUG-003 | Unreachable area | Level_PlantedBugs_A | TBD | TBD | TBD | M4/M3 UnreachableArea | Major | not planted |
| BUG-004 | Missing trigger | Level_PlantedBugs_A | TBD | TBD | TBD | M4 MissingTrigger | Major | not planted |
| BUG-005 | Collider gap | Level_PlantedBugs_A | TBD | TBD | TBD | M4 OutOfBounds | Critical | not planted |
| BUG-006 | Invisible wall | Level_PlantedBugs_A | TBD | TBD | TBD | M4 Stuck + M3 coverage | Major | not planted |

## Calibration rules (SRS §13, binding)

1. Every bug must be reproducible by an ordinary human player (verified: AC-7).
2. Every bug must be invisible to a casual glance — the level must look legitimate.
3. Exactly one bug per taxonomy class; `Level_Baseline` stays 100% clean (AC-9).
4. Plants are scene-local edits only — never shared prefabs/tiles (risk R4).
5. Each site gets an EditorOnly `PB_xxx` marker (runtime must never see the answer key).
