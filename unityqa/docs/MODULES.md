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

**HOTFIX-2 — session.json missing in ReplayRecorderTests (surfaced during
M3.D validation; defect dates to M3.A).** Not a Slice D regression: the
failing path (QARunner → QALogger → SessionManifest) is untouched since
Slice C (verifiable by diff). Root cause: the M3.A PlayMode test rig asserts
the two-file session-folder contract (session.json + replay.json) but
instantiates only ONE of the two producers — QALogger, the sole writer of
session.json, was never added to the [QA-ReplayTest] GameObject. Fix
(production-side, test untouched): ReplayRecorder now declares
[RequireComponent(typeof(QALogger))], encoding the real invariant — a replay
belongs inside a fully-formed session folder — so any scene or rig that adds
a recorder automatically gets the manifest system; a no-op wherever QALogger
already exists. Side effect: ReplayPlaybackTests' rig also gains a QALogger
and now writes real session files (its folders were already tracked and
deleted in teardown). Lesson: a test that asserts a multi-component contract
must instantiate every producer of that contract — rig completeness is part
of test correctness.

**M4.A — Feature extraction (log entry).** New `UnityQA.Features` namespace
(Features/ folder, core assembly, no new asmdef): `SessionFeatures` (frozen
formula contract in its header), `FeatureExtractor` (static, deterministic,
file-based, never throws; every input optional with *Available flags — crash
folders are first-class), `FeatureStore` (features.json, ReplayFileStore
pattern). Two additive modifications, each necessary: `SessionTrajectory`
now also captures vx/vy/g from PlayerSample payloads (positional features
need recorded kinematics; defaults keep old logs and existing tests valid) —
and `ReplayManager` gains ExtractFeaturesBySessionId + a context menu (the
catalog is the natural enumeration surface; edit-mode safe like the rest of
the file I/O). Input set is the brief's three files PLUS events.jsonl —
distance/speed/airtime are positional facts and the session's positional
record is its telemetry; replay.json holds inputs, not positions. Boundary
honored: extraction only — no anomaly detection, scoring, clustering, or
classification anywhere in the slice. 12 new EditMode tests against
hand-computed ground truth generated with the real production writers.

**M4.B — Feature dataset generation (log entry).** `FeatureDataset` (+
availability-gated `FeatureStatistic`s), `FeatureDatasetBuilder`
(catalog-driven — reuses ReplayCatalog, no second folder walker;
cached-features.json-or-extract with persistence, so builds are incremental;
`forceReextract` for full rebuilds), `FeatureDatasetStore` (dataset.json +
features.csv at the Sessions root). Key structure: the public
`Selectors` table — ONE ordered list of (name, accessor, availability)
defining "what is a numeric feature"; statistics and CSV both consume it, so
column order, stat order and naming cannot drift. Statistics contract:
population std (÷N), computed only over rows carrying the feature (a
replay-less session contributes nothing to replay stats — sampleCount is
explicit). ReplayManager modification (additive, justified): the established
front door gains BuildFeatureDataset + context menu, edit-mode safe. 10 new
EditMode tests incl. hand-computed statistics, cache-vs-force semantics,
determinism, exact CSV header pinning, empty-cell missingness, and a de-DE
culture pin for the CSV. Slice boundary held: aggregation and description
only — no thresholds, no anomaly flags, no scoring (M5/M6 territory).

**M5.A — Descriptive analysis layer (log entry).** New `UnityQA.Analysis`
namespace (core assembly): SessionAnalysis/DatasetAnalysis models,
AnalysisEngine (pure FeatureDataset→DatasetAnalysis; z-scores, mid-rank
percentiles, min-max normalization, deviations, deterministic rankings,
numeric outlier candidates at |z| ≥ 2 — a stated textbook convention, not a
judgment), AnalysisStore (analysis.json at the Sessions root). Reuse
discipline: canonical feature list and statistics math come from
FeatureDatasetBuilder (Selectors/ComputeStatistics) — the analysis layer
cannot drift from the dataset layer because they share one implementation.
Language discipline enforced at type level: candidate/far-from-mean/rank
vocabulary only; "abnormal"/"bug" do not appear — those words belong to
later M5/M6 slices, and the paper's method section can cite exactly this
boundary. ReplayManager gains the single mandated "Analyze Dataset" context
menu (loads dataset.json, builds it first if absent, saves analysis.json;
edit-mode safe). 14 new EditMode tests, all hand-computed ground truth;
engine tests need no files (pure function) — only the store test touches disk.

**D-012 — Scope refinement: deterministic QA framework; agents/ML = future
work (directed at M5.B kickoff).** Autonomous gameplay agents, RL/ML/LLM
integration and AI decision-making are formally moved to future work. The
system's contribution is the deterministic pipeline: Replay → Feature
Extraction → Dataset → Statistical Analysis → Rule-Based Quality Oracles →
Reports. Recorded because the paper must describe the actual implemented
system — this decision is what makes every claim in it checkable.

**M5.B — Oracle framework (log entry).** New `UnityQA.Oracles` namespace:
IQualityOracle (Name/Description/Enabled/Evaluate; null = not-applicable;
Evaluate must be pure — the runner stamps time), OracleContext + factory
(the ONE file-I/O point: contexts assembled from dataset/analysis/catalog/
validation in chronological order; oracles never touch disk),
OracleResult/OracleRunResults (severity vocabulary info|warning|critical;
evidence[] as machine-checkable strings), OracleRegistry (explicit ordered
registration, duplicate names rejected, no reflection/DI per spec),
OracleRunner (session-major/oracle-minor deterministic order; exception
isolation in the event-bus tradition — a throwing oracle is an ORACLE error,
counted separately, never a game verdict), OracleResultStore
(oracle-results.json). ReplayManager gains the single mandated "Run Quality
Oracles" menu with a root-parameterized overload (tests run on temp roots;
courtesy chain builds dataset/analysis if absent). Zero-oracle runs are
valid and green — the framework is proven before any rule exists. 13 new
EditMode tests via stub oracles incl. order, isolation, four-way accounting,
determinism, and a full manager end-to-end on a temp root.

**M5.C — BenchGame v2: the benchmark level (log entry).** Game side (foreign
-code rule intact): SessionOutcome enum (Success/SpikeDeath/OutOfBounds/
Quit), GameRun (Running→Ended lifecycle; once-only outcome STRUCTURALLY —
EndRun no-ops after the first; killY watch on the physics clock; freeze on
end; Escape=quit, R=reset), SpikeHazard/ExitDoor (report-don't-decide
triggers), BenchmarkLevelBuilder (Level_Benchmark from code: spawn → four
≤3-tile gaps within GUT-SPEC kinematics → exit; two avoidable static spikes;
builds the FULL instrumented [QA] object — closing the QA-SETUP rebuild
pitfall — creating DefaultQAConfig if absent). QA side, all additive:
IRunOutcomeSource (third small observer interface in the IGutSpecSource
pattern — frozen IGameAdapter untouched; neutral RunOutcomeInfo so core
never learns game enum names), adapter maps and relays, sampler emits — per
run end, fixed order: PlayerDied(21) for deaths / TriggerFired(23,
"exit.door") for success / always RunEnded(25, new append). Deliberate
payoff: reserved event types go live and features.deaths /
checkpointsReached start counting with zero feature-extraction changes.
BenchGame.Editor.asmdef gains UnityQA/UnityQA.Adapters refs (builder
assembles the QA stack — asmdef refs are part of the slice contract,
HOTFIX lesson applied proactively). 5 new PlayMode tests: success/spike/
oob outcomes with event chains, once-only under repeated EndRun, freeze-on
-death, reset-to-spawn.

**M5.D — Core quality oracles (log entry).** The first three concrete rules,
each answering ONE question: ReplayConsistency (over the existing M3.C
validation result — no playback in Evaluate; FAIL is severity WARNING
because replay infidelity is an apparatus problem, not a gameplay defect),
Completion (recorded outcome == Success; FAIL warning — attribution is not
its job), Hazard (failure attribution: SpikeDeath → warning, designed
hazard; OutOfBounds → CRITICAL, the falls-out-of-world defect class QA
exists for; Quit and unknown outcome names → skip, never guess). Framework
extension, additive: OracleContext gains RunOutcome, read by the factory as
the LAST RunEnded event in events.jsonl (multi-run sessions: final state
wins — policy documented; anchored parsing of our own writer, pinned by
test against real JsonLineWriter output). Sessions with no recorded outcome
SKIP outcome-oracles — absence of evidence is not evidence of failure.
ReplayManager's registry now self-populates with exactly these three on
first access (three explicit lines, reviewable in a diff; no placeholders).
14 new EditMode tests covering every branch of every oracle, registration,
and a mixed-session runner scenario with full four-way accounting.

**HOTFIX-3 — Validation start-state control (M5.D stabilization).**
(a) A GameRun left in Ended state (controller disabled, body unsimulated)
guaranteed divergence — the validator now resets the run before playback.
(b) Teleporting to telemetry sample[0] started every validation run one
sampler interval AHEAD of the true t = 0 position (the first sample is
captured 1/telemetryHz after session start); fixed by first-order backward
extrapolation using the sample's own recorded velocity (M4.A fields) —
stationary starts unchanged.

**HOTFIX-4 — Input-latch and phase-alignment fixes (M5.D stabilization).**
The controller's input latch (moveInput/jumpRequested, written only in
Update) survived EndRun's component-disable and fired the dead player's
last command on the first physics step after any reset — ResetInputState()
(new, additive, read-only-surface JumpRequested alongside) is now called by
GameRun.ResetRun and the validator. Playback is armed BEFORE the validation
session starts, removing a ~2-rendered-frame input lag against the
validation session's telemetry clock — with the side effect that validation
sessions record the REPLAYED input into their own replay.json (re-playable
like any session; the catalog stops indexing empty replays).

**HOTFIX-5 — Replay determinism root cause: render-domain replay
(M5.D stabilization).** Symptom: a fresh Level_Benchmark Success run
replayed cleanly but validation FAILED (max 15.65u, first divergence
t≈1.2s — the first jump). Root cause: M3.A recorded one frame per RENDERED
frame and M3.B played one frame per rendered frame, but the controller
consumes input once per PHYSICS step — and the number of rendered frames
between two steps is a function of the machine's momentary frame rate
(editor: several hundred fps against 50 Hz physics). The recorded input
sequence therefore landed on DIFFERENT physics steps at playback than at
recording — every jump shifted by whole steps, which Level_Benchmark's
precision gaps convert into binary path changes (the D-011 "honest
limitation", now fatal instead of latent). Fix, end to end in the step
domain: PlayerController2D re-samples the seam in FixedUpdate immediately
before consuming (frame-rate independent by construction; keyboard
unchanged); ReplayRecorder captures per FixedUpdate at order −10 — after
ReplayPlayer (−50), before the controller (0) — recording exactly what the
step consumes (latch OR down-now, via the HOTFIX-4 JumpRequested surface);
ReplayPlayer advances one frame per FixedUpdate and clears the replay
source's jump edge in Update so the controller's Update latch cannot
double-consume a recorded press. replay.json bumps to schemaVersion 2 with
`inputDomain: "fixedStep"`; v1 files still load with a legacy-timing
warning. The mapping "recorded frame → physics step" is now 1:1 on any
frame rate — determinism is structural, not tolerance-tuned.

**M5.D stabilization — replay outcome reporting & comparison.** Playback
now observes GameRun and reports "Replay Outcome: <Success|SpikeDeath|
OutOfBounds|Quit>" ("Unknown" if the run never ended during playback), and
resets an already-Ended run before playing (feeding input to a frozen
player is never a meaningful replay). Validation gains the discrete second
axis: validation.json v2 records originalOutcome/replayOutcome (each
session's last RunEnded, read by the SAME reader the oracles use) plus
outcomesCompared/outcomeMatch; a comparable mismatch downgrades a
trajectory PASS to FAIL, missing outcomes never change the verdict, and a
manual Quit original is not comparable (Escape is not in the input seam).
The console verdict is now a human-readable block (outcomes, max/mean/RMS,
first divergence, samples, duration delta); the artifact stays
machine-readable. 7 new EditMode tests pin ApplyOutcomes branch by branch.

**D-013 — Project-local QAData storage (M5.D stabilization).**
persistentDataPath buried research artifacts in AppData/LocalLow — hostile
to debugging, IEEE evidence collection, and dataset sharing. New QAPaths
(Core) is the single path authority: in the EDITOR everything lives under
`<project>/QAData/` — `Sessions/` (session folders, self-contained per the
frozen schema: session.json, events.jsonl, replay.json, validation.json,
features.json, plus catalog.json at the root), `Datasets/` (dataset.json +
features.csv), `Analysis/` (analysis.json), `Reports/` (oracle-results.json,
and the M7 output that has since landed there: evaluation-campaign.json,
evaluation.json/.csv, unityqa-report.html), `Exports/` (reserved for packaged
evidence); in a player
build the identical tree sits under persistentDataPath/QAData. Per-session
replay/validation artifacts deliberately STAY inside their session folder —
the frozen schema and every reader (catalog, features, oracles) depend on a
self-contained session folder. QALogger.SessionsRoot now delegates to
QAPaths (every existing consumer relocates through the one accessor it
already used); ReplayManager's default menu paths route dataset/analysis/
report artifacts to their subfolders while the root-parameterized test
overload keeps the everything-under-one-root convention. One-time
"Migrate Legacy Sessions" menu copies AppData sessions across (copy, not
move; regenerable cross-session artifacts are rebuilt, not migrated).
QAData/ is gitignored. 4 new EditMode tests pin the layout.

**M6.A — Planted-bug regression benchmark (log entry).** The answer key goes
live: `Level_PlantedBugs_A` = Level_Benchmark's exact geometry and [QA] stack
plus four documented defects, built entirely by the new
`PlantedBugLevelBuilder` (sibling of BenchmarkLevelBuilder, NOT a shared
parameterized core — same reasoning that already keeps the baseline and
benchmark builders separate: under D-006 the builder IS the level's source,
so this file diffed against the clean builder is the complete reviewable
list of plants). The plants — PB-001 collider gap (platform 4's take-off
cell x27 moves to a render-only tilemap: looks solid, isn't), PB-002 soft
lock (sealed basin under gap 1: depth 3 u > jump 2.2 u, floor above killY —
alive, stuck, no outcome forever), PB-003 hazard on the golden path (third
spike mid-platform-3: a clean golden run replayed here flips Success →
SpikeDeath), PB-004 missing trigger (ExitDoor visually intact, trigger
collider disabled — the SRS §13 BUG-004 method verbatim). Every plant is
static geometry — determinism (FR-1.19) untouched; every plant is reachable
by ordinary play AND bypassable (kinematics-checked: the PB-001 bypass jump
reaches the exit platform with ≈1.8 u margin), so one session can target any
chosen bug. Ground truth is machine-checkable: EditorOnly `PB_00x` markers
at each site (SRS §13 rule 5 — stripped from builds, no runtime reads), and
BENCHMARK.md is rewritten as the ACTIVE answer key with a hard rule the
paper depends on: ground truth ("bug exists", status *planted*) is never
conflated with detection ("framework found it", column *pending* until an
M6-C evaluation run). Strictly ground-truth slice: no oracle added, no
schema change, no replay change. Tests: EditMode `PlantedBugLevelTests`
builds the real scene via the real builder and pins every plant (plus: the
clean benchmark's bytes are untouched by building the planted level);
PlayMode `PlantedBugConditionTests` proves each CLASS observable in the
existing event vocabulary — collider gap → OutOfBounds, basin → alive +
inescapable (300 fixed-step escape attempts peak below the rim) + zero
RunEnded, spike-on-path → same scripted input flips Success → SpikeDeath,
dead trigger → door reached and passed with zero TriggerFired/RunEnded.
Contract note (M3.B lesson): `UnityQA.Tests.EditMode.asmdef` gains
`BenchGame` + `BenchGame.Editor` references — the ground-truth tests consume
the builder and BenchGame types.

**M6.B — Detection oracles: SoftLock + MissingTrigger (log entry).** The two
detectors the M6 plan named, both additive `IQualityOracle` implementations
(no framework redesign; registered as two more explicit lines in
`ReplayManager.OracleRegistry` — order-preserving, reviewable in a diff).
Ground truth (M6-A: the defect exists) and detection (M6-B: an oracle found
it) stay separate — nothing here edits the planted geometry, and BENCHMARK.md's
*Detected* column stays `pending` until an M6-C run scores it.

`SoftLockOracle` (PB-002 class) works from a general gameplay-state rule, no
benchmark coordinates: a run is a soft lock when it recorded NO terminal
outcome and the player is alive, YET kept trying to move (enough direction
reversals or jump presses AND a real travelled-path length) while staying
CONFINED — the whole trajectory fits a small bounding-box extent and the path
is much longer than that extent (pacing inside a trap), sustained across an
observation window. That combination separates the four spec cases: idle /
"chose not to move" fails the activity test, normal traversal fails the
confinement test, a terminated or dead run is not a soft lock (PASS), and too
little trajectory → SKIP. FAIL is CRITICAL (unwinnable, player stranded).
Evidence: path length / direction changes / jump presses from SessionFeatures,
spatial EXTENT from a new `OracleContext.Trajectory` bounding-box summary the
factory derives from the SAME PlayerSample telemetry (no new event/file/schema
field — analysis of existing evidence at the one I/O point, so Evaluate stays
pure). Extent is the one thing the scalar features could not express and the
soft lock genuinely needs.

`MissingTriggerOracle` (PB-004 class) attributes from gameplay evidence, never
from a planted-bug ID. Honest evidence note recorded for the viva: the exit's
location is observable ONLY through the TriggerFired event a broken exit fails
to emit, and no expected-trigger / level-bounds instrumentation exists
(`ExpectedTriggersSummary` is declared but never emitted), so a single direct
play cannot self-prove "the player reached the exit." The reference that CAN
is the replay-regression pair already in validation.json: an ORIGINAL run that
reached and fired the exit (originalOutcome Success) plus a FAITHFUL replay
(verdict PASS) of that route. Rule: this run ended Success → PASS; no such
reference → SKIP; original+replay both Success → PASS; replay ended in a
FAILURE (spike/out-of-bounds/quit) → SKIP (a hazard, not a trigger defect —
this is the PB-003 discrimination, left to HazardOracle); original completed +
faithful replay reproduced the route + no completion + no trigger fired →
FAIL CRITICAL (exit reachable but silently non-functional). Direct-play
(non-replay) missing-trigger detection is deferred behind the missing
expected-exit observable, reported not invented.

Tests: `Tests/EditMode/DetectionOracleTests.cs` — 12 EditMode cases over
hand-built contexts (pure, no files) covering FAIL / PASS / idle / traversal /
SKIP for SoftLock and PASS / FAIL / SKIP (incl. PB-003 discrimination) for
MissingTrigger. Existing suites untouched; no replay/event/QAData schema
change; M6-A geometry and M6-C both untouched.

**M6.C — Planted-bug evaluation campaign (log entry).** The quantitative
layer that turns the M6-A ground truth + M6-B detectors into reproducible
numbers, built as the same pure-engine + store + thin-ReplayManager pattern
as every collection artifact — no framework redesign, no oracle-logic change.
New `Assets/UnityQA/Evaluation/`: `EvaluationCampaign` (the experiment plan —
one case per PB plus ≥1 clean control, each listing the recorded session IDs
per repeat and the answer-key detector[s] it expects), `EvaluationReport`
(the evaluation.json wire format, raw per-run results preserved for the
paper), `EvaluationEngine` (pure scoring), `EvaluationStore`
(evaluation-campaign.json in / evaluation.json + evaluation.csv out, under
QAData/Reports). `ReplayManager.RunPlantedBugEvaluation` (menu "Run
Planted-Bug Evaluation") orchestrates: it runs the SAME dataset→analysis→
contexts→OracleRunner chain RunQualityOracles uses, then hands the real
verdicts to EvaluationEngine.

Ground truth vs detection stays strictly separated: a case is "detected" in
a run ONLY because a real oracle returned FAIL on that case's recorded
session; the campaign's `expectedDetectors`/`isClean` (the BENCHMARK.md
answer key) are consulted solely to SCORE those verdicts afterward, never
fed into an oracle — pinned by a test that a non-expected oracle firing is
NOT a detection. Case↔session assignment is an explicit user-authored
manifest (not auto-classified from outcomes — that would let detection
define the ground truth). Answer-key detector mapping: PB-001→Hazard
(OutOfBounds/critical), PB-002→SoftLock, PB-003→Hazard (SpikeDeath), PB-004
→MissingTrigger.

Metrics: per bug — runs, evaluableRuns, detected, missed, indeterminate,
detectionRate, consistency; overall — totals + overall detection rate; clean
control — cleanRuns, falsePositives, false-positive rate. Honesty rules:
detectionRate denominator is EVALUABLE runs (a run where every expected
detector SKIPPED is indeterminate, never a false "missed"); a rate with a
zero denominator is marked `*Available=false` ("unavailable"), never written
as NaN (JsonUtility would emit invalid JSON). Determinism: `generatedUtc` is
the only non-deterministic field (tests null it and compare bytes); case
order = campaign order, run order = index, firing-oracle order = registration
order. Default repeats = 5 (DESIGN.md M8), carried in the campaign and
configurable there. 11 EditMode tests (EvaluationEngineTests) cover all-four-
detected, one-missed, clean-no-FP, clean-FP, repeat aggregation + consistency,
indeterminate, zero-run/null-campaign safety, timestamp-independent
determinism, answer-key-not-bug-ID scoring, and serialization/CSV. BENCHMARK.md
gains an M6-C procedure section and keeps the *Detected* column `pending` —
no measured numbers are recorded until a real Unity campaign runs.

**M6.C — EXECUTED in M8 (evidence hardening).** The campaign above has now
been recorded and scored against real sessions. The code default stays
`DefaultRepeats = 5`; the authored campaign sets `repeats: 3` and each
planted case lists three genuinely recorded sessions (clean control keeps
its 5, exceeding the target). Measured result, from
`QAData/Reports/evaluation.json`: 4 planted cases, **12/12 evaluable
bug-runs detected (100%)**, 0 missed, 0 indeterminate, **0/5 clean false
positives (0%)**; every planted case reports `consistent` over 3 evaluable
runs, so per-case consistency is now demonstrated agreement rather than the
single-run tautology it was at N=1. PB-004's three runs are three
independent original→validation pairs, all three `validation.json` records
`verdict: PASS` against the unchanged 0.75u threshold. Evidence corpus: 40
recorded sessions (`dataset.json`, `analysis.json`). BENCHMARK.md gains a
*Campaign status* section; its *Detected* column still stays `pending` — the
ground-truth/detection separation is unchanged, and no oracle, evaluation,
schema, threshold or benchmark geometry was modified to obtain these numbers.

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
