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

**D-007 — Instrumentation before BenchGame completion (approved 26 Jul 2026).**
Milestone 2 = telemetry/logging spine against today's BenchGame; Milestone 3 =
BenchGame completion (hazards, checkpoints, tokens, exit, death/respawn)
plugging into live telemetry. Reverses SRS implementation-order steps 2/3.
Rationale and cost: MILESTONE-2-DESIGN.md §0.

**D-008 — `IPlayerInputSource` seam in BenchGame (approved 26 Jul 2026).**
Controller reads input through an interface (keyboard default) so input
capture is exact and Module 2's agent gets its actuation seam early. "Normal
game" rule defended in MILESTONE-2-DESIGN.md §0/§8; behavior with default
source is identical (regression-checked by the M1 tile rulers, M2-V1).

**D-009 — JsonUtility for session.json, not Newtonsoft (M2 Slice B).**
session.json is a fixed-shape document — exactly JsonUtility's sweet spot —
so the planned Newtonsoft dependency is dropped entirely (NFR-1.8
strengthened: zero external runtime packages). The serializable DTOs in
SessionManifest.cs ARE the schema §2 shape; renaming a DTO field is a schema
change and is treated as such. Newtonsoft returns only if a genuinely dynamic
document ever appears; none is on the roadmap.

**D-010 — Telemetry rides events.jsonl; dense streams deferred (M2 Slice C).**
Directed at Slice C kickoff: all telemetry flows through the existing
Player → adapter → sampler → QARunner → bus → QALogger → JsonlSink pipeline as
PlayerSample events — no separate telemetry.jsonl/inputs.jsonl files, no
bypass of the event system. At ≤50 Hz the event pipeline's cost is trivial,
one file is simpler to consume, and frozen Slice B persistence stays
untouched. The design's dense-stream sections stand as deferred work; the
input-trace recorder (and the D-008 IPlayerInputSource seam it needs) moves to
a later slice. Revisit only if a future module needs rates the event path
can't carry. Registry addition: PlayerSample = 30 (append-only, allowed
within schema v1).

**HOTFIX-1 — JsonlSink sharing violation (found by Slice C validation, latent
since Slice B).** The sink held a session-long FileStream; Windows file-sharing
checks are mutual, so ordinary readers (File.ReadAllLines requests share=Read,
which excludes existing writers) hit IOException regardless of the writer's
own share mode — masked on platforms with advisory sharing. Fix: buffered
append — lines accumulate in memory, each Flush is one open→append→close, no
handle held between flushes, file unconditionally readable at all times.
Public API, flush policy, ordering, and crash semantics unchanged; Slice B
tests unchanged (they were right; the implementation was wrong). Side
correction: explicit '\n' line endings per schema §6 (StreamWriter had been
emitting \r\n on Windows). Lesson recorded for the viva: "worked on my
machine" and "correct" differ precisely by one platform's file semantics.

**D-008 — EXECUTED in M2 Slice D.** The approved input seam is now real:
`IPlayerInputSource` + `KeyboardInputSource` in BenchGame; PlayerController2D
reads commands through the interface (auto-adds the keyboard source — scenes
unchanged) and its two direct Input.* calls are gone. Behavior with the
default source is byte-identical (M1 rulers + frozen suites pin it). The
substitution mechanism is proven immediately: the Slice D PlayMode tests
drive gameplay with a ScriptedInputSource — the exact pattern Module 2's AI
agent and any future replay source will use. UnityQA-side: new
`IPlayerInputObserver` (adapter-implemented, keeps frozen IGameAdapter file
untouched — same pattern as IGutSpecSource), pure `InputSampleGate`
emit-decision, `QAInputRecorder`, and `InputSample = 31` (registry append).
Time-domain note recorded for the viva: input is frame-domain (Update), the
keyframe cadence and `step` payload are fixed-step-domain (FixedUpdate) —
deliberate, documented straddle.

**D-011 — Milestone 3 Slice A: replay recording (placement + roadmap note).**
Roadmap: the project plan now runs M3 Replay → M4 Features → M5 ML → M6 Bug
Detection → M7 Reports (supersedes the original module ordering; recorded
here so the document trail stays honest). ReplayRecorder lives in the
UnityQA.Adapters assembly BECAUSE the Slice A mandate — input only through
BenchGame's IPlayerInputSource — names a BenchGame type, and Adapters is the
one sanctioned bridge (NFR-1.3). The replay data model
(ReplayFrame/ReplayRecording/ReplayFileStore) is game-agnostic and sits in
core UnityQA/Replay. Wire format: pretty replay.json per session folder,
schemaVersion'd like every UnityQA format. Known forward-looking constraint,
flagged now for M3 Slice C: frames are FRAME-domain (recorded per Update, per
spec) while the physics that must replay deterministically is FIXED-STEP
domain — playback fidelity work in Slice C may add a fixed-step field under a
schemaVersion bump. Relationship to Slice D's InputSample: events.jsonl keeps
the sparse behavioral log (analysis-shaped); replay.json is the dense
replay-grade trace (playback-shaped) — different consumers, both documented.

**M3.B — Replay playback (log entry).** ReplayFileStore gains a skeptical
Load (null + one descriptive error on missing/malformed/future-schema files;
frameCount repaired from the array with a warning — the array is ground
truth). ReplayInputSource (plain class, Adapters) implements the D-008 seam
from recorded frames; ReplayPlayer ([DefaultExecutionOrder(-50)]) advances
one frame per Update BEFORE the controller reads — sequential O(1) index
access — swaps the controller's source via the new additive
PlayerController2D.SetInputSource (generic injection, not replay code:
null-rejected, remembered, restored on stop/finish/disable; a finished
replay Clear()s to neutral so no phantom keys survive). Empty replayFile
auto-resolves to the newest session's replay — A2's sortable folder names
paying off. Known limitation, owned by Slice C per D-011: frame-domain
playback replays the exact input SEQUENCE, not exact wall-clock timing,
under differing frame rates.

**M3.B integration incident (closed).** Root cause of the reported compile
failure: the EditMode test asmdef shipped in M2 Slice A referenced only
`UnityQA`, but Slice B's `ReplayLoadTests` consume `UnityQA.Adapters` types —
missing reference, delivered defect, ours. Fixed by adding `UnityQA.Adapters`
to `UnityQA.Tests.EditMode.asmdef` references (fix applied in-repo; mirror
aligned). Lesson paired with the earlier one: assembly references are part of
a slice's contract and belong in its validation checklist.

**M3.C — Deterministic validation (log entry).** Mechanism:
replay-under-recording — ReplayValidator resets the player to the original
run's first sampled position (the slice's ONE deliberate game-state
intervention: controlled initial conditions for the experiment), starts a
fresh QA session, plays the original replay through the existing
ReplayPlayer/seam, ends the session, then compares the two sessions'
PlayerSample trajectories (SessionTrajectory reads them back from
events.jsonl — anchored substring parsing of our own writer's fixed format,
cross-checked by a test that generates input with the real JsonLineWriter).
TrajectoryComparer (pure, engine-free): t0-normalization, linear
interpolation at original timestamps (never charges the replay for sampler
phase), max/mean/RMS deviation, firstDivergenceTime, durationDelta; verdict
PASS/FAIL vs QAConfig.validationDeviationThreshold (new additive field, with
validationTimeoutMargin). Artifact: pretty validation.json in the VALIDATION
session's folder, linking both session UUIDs — a primary input for M7 and
the paper's determinism table. Additive API: ReplayPlayer.SetReplayFile.
Interpretation guidance: near-zero deviation = faithful on this machine;
growth after firstDivergenceTime quantifies the frame-domain limitation
flagged in D-011 — measured now, not suspected. Record real numbers here
after first runs.

**M3.D — Replay infrastructure (log entry).** Fully additive slice — zero
modifications to existing code. `ReplayMetadata` + `ReplayCatalog` (core
UnityQA/Replay): scans the Sessions root into a newest-first index, reads
every document through the DTOs that wrote it, probes replay.json with a
header-only DTO (frames never materialized), cross-links each session to the
NEWEST validation.json citing it as original, surfaces crashed sessions
(manifest status "open") into the index, counts-not-throws on damaged
folders, and persists `catalog.json` at the root (schemaVersion 1).
`ReplayManager` (Adapters): the front door — refresh/browse (edit-mode safe:
pure file I/O), PlayBySessionId / ValidateBySessionId delegating to the
existing ReplayPlayer / ReplayValidator, context menus for newest-replay
workflows; lifecycle discipline from the ReplayValidator hotfix applied from
birth (EnsureRefs + Play-mode guards on every ContextMenu path). The catalog
is M4's enumeration surface (datasets iterate entries) and M7's citation
index (verdicts + evidence folder links). Milestone 3 is complete with this
slice: record → save → load → play → compare → index.

**A1/A2 — Schema amendments at M2 approval.** Per-stream header line carrying
`schemaVersion` + `sessionId`; canonical session ID becomes a UUID; folder
names stay human-sortable. Frozen into EVENT-SCHEMA.md v1.

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
