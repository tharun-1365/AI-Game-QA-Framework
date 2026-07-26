# MODULES — Build Log & Decision Register

Per-milestone log of what was built, why, and what was learned. This file is a
formal deliverable (SRS D10) and the raw material for the viva presentation.

---

## Decision Register

Decisions that refine or deviate from an approved SRS. Small process, big habit
(SRS §12 stability promise).

**D-001 — Adapters get their own assembly (refines SRS §6).**
SRS §6 places `Adapters/` inside `Assets/UnityQA/` under `UnityQA.asmdef`, but
NFR-1.3 requires (a) UnityQA to compile without TestGame and (b) BenchGame to
never reference UnityQA. If the adapter lived inside the UnityQA assembly, that
assembly would need a reference to BenchGame — violating (a). Resolution: when
the adapter is implemented, `Adapters/` receives its own
`UnityQA.Adapters.asmdef` referencing both `UnityQA` and `BenchGame`; the
dependency picture becomes `UnityQA ← UnityQA.Adapters → BenchGame`, exactly
matching the SRS §5 architecture diagram. Decided at Milestone 1 scaffold time
so the asmdef layout never needs retrofitting. *(Status: to implement in the
instrumentation milestone.)*

**D-002 — BenchGame uses the legacy Input Manager API (`Input.GetAxisRaw`).**
Rationale (Rule 8): a two-axis, one-button game gains nothing from the Input
System package's action maps, and the legacy API keeps PlayerController2D
self-contained and beginner-readable. Consequence: Project Settings → Player →
Active Input Handling must be **Both** (Unity 6 templates default to the new
Input System only, which makes legacy calls throw). Module 2's virtual-input
seam will wrap input behind an interface anyway, at which point this choice
becomes invisible to the framework.

**D-003 — `AssemblyAnchor.cs` placeholder in the UnityQA assembly.**
An asmdef with zero scripts triggers a persistent import warning. A single
empty internal type keeps the assembly clean. It contains no logic and is
deleted when the first real Core script lands. Not framework code.

**D-004 — Built-in render pipeline, not URP.**
BenchGame's art is colored squares; URP offers it nothing (no 2D lights planned
in any module) while adding a package dependency, pipeline assets, and quality
settings to a project we generate as raw text assets. Rule 8 decides it:
built-in pipeline, `Sprites/Default` material everywhere. Revisit only if a
future module genuinely needs URP features (none does on the current roadmap).

**D-005 — Gap rulers corrected to 4 (clearable) and 6 (unclearable).**
The original plan said 4/5. Implementation math caught the error: max jump
*center travel* is ≈ 4.64 u, but a gap is cleared edge-to-edge, and the 0.9-wide
collider grants ≈ 0.275 u of takeoff overhang (ground-check box half-width)
plus ≈ 0.45 u of landing overhang — effective clearable gap ≈ 5.3 tiles. A
5-tile gap therefore *clears* and would have been a broken ruler. Verification
uses 4 (comfortably clears) and 6 (robustly fails); 5 is documented as marginal
and excluded. Recorded because it is a spec bug caught before it reached the
benchmark — exactly what this register is for. GUT-SPEC.md updated.

**D-006 — Scene ships with an empty tilemap; geometry is painted by editor code.**
Unity scene YAML for tilemap tile data is version-sensitive and cannot be
validated outside Unity. Instead of hand-authoring fragile tile YAML,
`LevelBaselineBuilder` (editor-only assembly) paints the verification geometry
through Unity's own Tilemap API on first project load and saves the scene with
Unity's own serializer — guaranteed byte-correct for the local editor version.
Side benefits: the level layout is version-controlled as readable code
(`PaintGeometry()`), and a full from-scratch scene rebuild exists as a recovery
menu item. The painted scene, once saved, is a perfectly normal hand-editable
scene — later milestones plant bugs in the editor as the SRS specifies.

---

## Milestone Log

### M1.1 — Project Scaffold + BenchGame Foundation (2026-07-19)

**Built:** complete ready-to-open Unity 6.3 LTS project — repo structure per
SRS §6; `UnityQA.asmdef` + `BenchGame.asmdef` (zero cross-references, NFR-1.3)
+ `BenchGame.Editor.asmdef` (editor tooling, references BenchGame only); doc
set (BENCHMARK, EVENT-SCHEMA, GUT-SPEC, MODULES); `PlayerController2D` (run +
derived-velocity fixed jump, latched input, FixedUpdate physics);
`FollowCamera` (SmoothDamp, LateUpdate); `White.png` sprite + `GroundTile`
asset + `Player.prefab`; `Level_Baseline.unity` (camera, player, grid/tilemap
with composite collider, Ground layer 6); `LevelBaselineBuilder` editor
bootstrap (auto-paints verification geometry on first load; full rebuild menu
as recovery); ProjectSettings (Active Input Handling = Both, Force Text,
Ground layer, defaults verified) and Packages manifest.

**Key decisions:** D-001…D-006 above; jump velocity derived from authored apex
height so GUT-SPEC is exact by construction.

**What I learned:** *(Khanna fills this in after completing the milestone —
minimum three sentences, in your own words. This section is the viva gold.)*

**Verification result:** *(record the VC checklist outcome from the Milestone 1
guide here, with date)*
