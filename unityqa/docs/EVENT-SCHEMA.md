# EVENT-SCHEMA — UnityQA Data Formats

**Status: FROZEN — schema v1** (Milestone 2 design approved 26 Jul 2026, incl.
amendments A1/A2). Evolution rule: later versions may ADD fields; existing
fields are never renamed, retyped, or removed; `schemaVersion` (in
`session.json` and in every stream header) is the single source of truth for
readers. Any change requires a MODULES.md decision entry and a version bump.

## 0. Session identity (amendment A2)

- `sessionId` — canonical ID, GUID string (36 chars), minted at session start.
- Session folder — `Sessions/yyyyMMdd-HHmmss_<uuid[0:8]>/` (human-sortable);
  `session.json` binds folder ↔ UUID.

## 1. Stream header line (amendment A1)

Every `.jsonl` file's **first line** is a header, not a record:

```json
{"header":1,"schemaVersion":1,"stream":"events","sessionId":"6f1c2e6a-…"}
```

`stream` ∈ `events` | `telemetry` | `inputs`. Readers: skip or validate line 1.

## 2. `session.json`

Written at session start (`"status":"open"`), rewritten at clean close
(`"status":"closed"` + counts). A file left `"open"` marks a crashed/killed
session — deliberately detectable.

| Field | Type | Notes |
|---|---|---|
| schemaVersion | int | 1 |
| sessionId | string | GUID (A2) |
| folderName | string | as above |
| level | string | active scene name |
| startedUtc | string | ISO-8601 |
| unityVersion / appVersion | string | environment stamp |
| configSnapshot | object | full QAConfig dump |
| gutSpec | object | {runSpeed, jumpHeight, gravityScale} — datasets self-describe |
| status | string | "open" → "closed" |
| durationSec, counts{events,telemetry,inputs} | — | on close only |

## 3. `events.jsonl` (sparse, discrete)

Envelope per line (SRS §14, FR-1.7):

```json
{"sid":"<uuid>","seq":42,"t":9.801,"frame":588,"type":"CollisionEnter",
 "pos":{"x":14.2,"y":0.8},"payload":{…}}
```

`sid` full UUID (sparse stream — affordable); `seq` strictly increasing, no
gaps; `t` session seconds (3 decimals); `pos` present when spatial.

**Type registry** (enum values fixed forever; extended, never reordered):

| Value | Type | Since | Payload |
|---|---|---|---|
| 0 | SessionStarted | M1.2 | {level} |
| 1 | SessionEnded | M1.2 | {durationSec, eventCount} |
| 10 | JumpExecuted | M1.2 | {} |
| 11 | Landed | M1.2 | {fallSpeed} |
| 12 | BoundsExited | M1.2 | {} |
| 13 | CollisionEnter | M1.2 | {other, otherLayer, contactX, contactY, relVelX, relVelY} |
| 14 | CollisionExit | M1.2 | {other, otherLayer} |
| 20 | PlayerSpawned | M1.3 (reserved) | {} |
| 21 | PlayerDied | M1.3 (reserved) | {cause} |
| 22 | TokenCollected | M1.3 (reserved) | {tokenId} |
| 23 | TriggerFired | M1.3 (reserved) | {triggerId} |
| 24 | ExpectedTriggersSummary | M1.3 (reserved) | {expected, fired[], unfired[]} |
| 25 | RunEnded | M5.C | {outcome: "Success"\|"SpikeDeath"\|"OutOfBounds"\|"Quit"} — exactly one per observed benchmark run; death outcomes are preceded by PlayerDied (21, cause "spike"/"outOfBounds") and Success by TriggerFired (23, triggerId "exit.door") — the reserved types finally live, and features.deaths/checkpointsReached light up with ZERO extraction changes |
| 30 | PlayerSample | M2.C | {vx, vy, g, mx, facing, state} — see below |
| 31 | InputSample | M2.D | {step, horizontal, jumpPressed, jumpReleased, jumpHeld, keyframe} — see below |
| 90 | AdapterWarning | M1.2 | {message} |

**PlayerSample (type 30, added M2 Slice C per D-010 — registry extension, no
version bump needed):** periodic gameplay telemetry riding the events stream
at `QAConfig.telemetryHz` (default 10 Hz). Envelope `pos` = player position.
Payload: `vx`/`vy` velocity; `g` grounded 0/1; `mx` move input −1/0/+1;
`facing` −1/+1 (last non-zero move direction, persists through idle);
`state` ∈ `"idle" | "run" | "rise" | "fall"` (airborne classification wins
over horizontal; apex vy=0 counts as `fall`; |v| < 0.01 counts as not moving —
mapping pinned by TelemetryDerivationTests). `JumpExecuted` (10) and `Landed`
(11) are now live: edge-detected per physics step by the adapter (takeoff =
grounded→airborne with vy>0; a walk-off fall is NOT a jump); `Landed` payload
`fallSpeed` = downward speed at impact.

**InputSample (type 31, added M2 Slice D — registry extension):** the player's
ATTEMPTED commands (telemetry records what the game did; this records what the
player tried — an ignored mid-air jump press appears here with no matching
JumpExecuted). Emitted on every input change AND as a full-state keyframe
every `QAConfig.inputKeyframeEverySteps` fixed steps (default 250 = 5 s);
constant input therefore produces only keyframes. First record of every
session is a keyframe. Fixed payload shape: `step` (fixed-step count since
session start — the frame-rate-independent replay/join clock, FR-1.19);
`horizontal` −1/0/+1 attempted; `jumpPressed`/`jumpReleased` (held-state
edges, true only on the record where the transition happened); `jumpHeld`;
`keyframe` (true when emitted by cadence — such records carry full state
regardless of change). No `pos` — input is not spatial; join to PlayerSample
via `step`×`t` for state-action pairs (the future dataset shape). Replay
readiness, not implementation: a reader can reconstruct exact input state at
any step from the nearest prior keyframe + subsequent changes; BenchGame's
D-008 `IPlayerInputSource` seam is where a future TraceInputSource would feed
it back. Event flow: PlayerController2D (seam) → BenchGameAdapter
(IPlayerInputObserver) → QAInputRecorder (InputSampleGate decides emission) →
QARunner → bus → QALogger → JsonlSink.

Example `events.jsonl` fragment (running right, then a held jump, then release):

```json
{"sid":"…","seq":12,"t":3.001,"frame":181,"type":"InputSample","payload":{"step":150,"horizontal":0,"jumpPressed":false,"jumpReleased":false,"jumpHeld":false,"keyframe":true}}
{"sid":"…","seq":14,"t":3.420,"frame":206,"type":"InputSample","payload":{"step":171,"horizontal":1,"jumpPressed":false,"jumpReleased":false,"jumpHeld":false,"keyframe":false}}
{"sid":"…","seq":15,"t":3.900,"frame":235,"type":"InputSample","payload":{"step":195,"horizontal":1,"jumpPressed":true,"jumpReleased":false,"jumpHeld":true,"keyframe":false}}
{"sid":"…","seq":16,"t":3.917,"frame":236,"type":"JumpExecuted","pos":{"x":8.1,"y":1.45},"payload":{}}
{"sid":"…","seq":18,"t":4.140,"frame":249,"type":"InputSample","payload":{"step":207,"horizontal":1,"jumpPressed":false,"jumpReleased":true,"jumpHeld":false,"keyframe":false}}
```

**session.json change (M2.C):** `gutSpecSource` becomes `"adapter"` and
`gutSpec` carries real values {runSpeed, jumpHeight, gravityScale} whenever a
game adapter is present on `[QA]`; the `"pending-slice-c"` zeros path remains
only for adapter-less scenes. D-010 note: the design's separate
telemetry.jsonl/inputs.jsonl dense streams are deferred — at ≤50 Hz telemetry
rides events.jsonl through the standard pipeline; `counts.telemetry/inputs`
stay 0 until (if ever) dense streams are needed. Input-trace recording moved
out of Slice C scope with them.

## 4. `telemetry.jsonl` (dense, periodic — default 10 Hz)

```json
{"step":312,"t":6.240,"frame":410,"x":14.230,"y":1.550,"vx":6.000,"vy":0.000,"g":1,"mx":1}
```

`step` = FixedUpdate count since session start (the frame-rate-independent
clock; primary join key). `g` grounded 0/1; `mx` move input −1/0/1. No per-line
`sid` — the header (§1) carries it (A2 accommodation, documented).

## 5. `inputs.jsonl` (dense, per-fixed-step, delta-encoded)

Delta record — a field is present **iff it changed** this step:

```json
{"step":313,"mx":1}
{"step":340,"j":1}
```

Keyframe — all fields, unconditional, every 250 steps and as first record:

```json
{"step":0,"mx":0,"j":0,"k":1}
```

`j:1` marks the step a jump command was *consumed* by the controller.
Join to telemetry on `step`; reconstruct full input state by replaying deltas
from the nearest keyframe.

## 5b. `replay.json` (M3 Slice A — per-session replay recording)

Pretty-printed JSON (JsonUtility), one per session folder, beside
session.json. Top level: `schemaVersion` (independent of the event-schema
version — replays evolve on their own track), `sessionId` (the session UUID —
must match session.json; the cross-file join for M3.C validation and M7
replay references), `recordingStartTime` (ISO-8601 UTC), `frameCount`
(= frames.length, loader integrity check), `frames[]` with per-frame
`frameNumber` (0-based, contiguous), `timestamp` (seconds since recording
start), `horizontal` (−1/0/+1), `jumpPressed`, `jumpHeld`. Recorded via the
D-008 IPlayerInputSource seam only.

**v2 (HOTFIX-5, M5.D stabilization):** adds `inputDomain` = `"fixedStep"` —
one frame per PHYSICS step, captured immediately before the controller
consumes it; `timestamp` is on the fixed-time clock and `jumpPressed` means
"a jump request is consumed by this step". v1 files (no `inputDomain`) are
legacy render-frame recordings (one frame per Update); they still load, but
players warn that their timing is frame-rate dependent and validation of
them is best-effort. Rationale: input is consumed per physics step, so the
physics step is the only frame-rate-independent replay clock.

### `validation.json` (M3 Slice C; v2 fields in M5.D stabilization)

Written into the VALIDATION session's folder. v1: identity (both session
IDs/folders), comparison setup (`thresholdUnits`, sample counts,
`parseErrors`), deviation metrics (`maxDeviation`, `meanDeviation`,
`rmsDeviation`, `firstDivergenceTime`, durations), `verdict`
(PASS/FAIL/INVALID). **v2 adds the outcome axis:** `originalOutcome`,
`replayOutcome` (each session's last RunEnded outcome, "" if none),
`outcomesCompared` (both present and original ≠ Quit — Escape is not in the
input seam, so quits are not replayable), `outcomeMatch`. A comparable
mismatch downgrades a trajectory PASS to FAIL; missing outcomes never change
the verdict.

## 5c. `features.json` (M4 Slice A — per-session feature vector)

Pretty JSON, one per session folder — the fifth and final member of the
artifact set. Wire format = `SessionFeatures` field names; `schemaVersion` 1;
`extractedUtc` is the sole non-deterministic field (identical inputs →
identical feature values, pinned by test). Inputs: session.json (identity,
duration, status), events.jsonl (trajectory features from PlayerSample incl.
recorded vx/vy/g — an M4.A additive capture in SessionTrajectory — plus
event counts: jumps, landings, collisions, deaths, checkpoints, tokens),
replay.json (frame count, direction changes, input jump presses),
validation.json (verdict passthrough). Every input optional: missing files
zero their group and clear the matching *Available flag. Formula contract
(frozen — changing any definition = schema bump) lives in
SessionFeatures.cs's header: path-length distance, avg = dist/duration,
max from recorded |v|, airtime/idle by interval-end-state attribution
(idle ⇔ grounded ∧ |v| < 0.05), direction changes as opposite-sign command
flips with transparent zeros. The jumpCount (executed) vs inputJumpPresses
(attempted) pair is a deliberate cross-check inherited from the M2.D design.

## 5d. `dataset.json` + `features.csv` (M4 Slice B — cross-session dataset)

Both live at the Sessions ROOT (collection-level documents, beside
catalog.json). `dataset.json`: schemaVersion 1, generatedUtc (sole
non-deterministic field), sessionCount, skippedSessions, `rows[]` (one
SessionFeatures per session, OLDEST-first — chronological is the dataset
axis; the catalog stays newest-first for browsing), and `statistics[]` — per
feature: sampleCount, mean, std (POPULATION, ÷N), min, max, computed only
over rows whose source group was available (no fake zeros; sampleCount says
what each number rests on). The canonical feature list — names, order,
availability gates — is `FeatureDatasetBuilder.Selectors`, shared by
statistics and CSV so they can never drift. `features.csv`: header =
sessionId, folderName, level, sessionStatus, validationVerdict + selector
names; invariant culture, floats "0.####", unavailable feature = EMPTY cell
(NaN in pandas), minimal quoting. This CSV is the M5 ingestion format.

## 5e. `analysis.json` (M5 Slice A — descriptive dataset analysis)

Sessions-root collection document. `schemaVersion` 1; `generatedUtc` sole
non-deterministic field; provenance (`sourceGeneratedUtc`,
`sourceSessionCount`) binds it to the exact dataset analyzed. Per session ×
per canonical feature (Selectors order): `value`, `zScore` ((v−mean)/std, 0
when std=0), `percentile` (mid-rank: (below + 0.5·equal)/N × 100, available
cohort only), `normalized` ((v−min)/(max−min)), `deviationFromMean`,
`available`. Plus per-feature `rankings` (value descending, folder-name
tie-break) and `outlierCandidates` — values at |z| ≥ 2 recorded with their
arithmetic (value, z, mean). Language contract: descriptive vocabulary only —
this document states positions in distributions and never classifies
anything as abnormal, anomalous, or buggy (later M5/M6 slices). Statistics
math is REUSED from FeatureDatasetBuilder — one source of truth.

## 5f. `oracle-results.json` (M5 Slice B — oracle framework run document)

Sessions-root collection document. Run header: schemaVersion 1, generatedUtc
(sole non-deterministic field; all results share this one stamp — oracles
never read the clock), sessionCount, oracleCount, enabledOracleCount, and
the four-way outcome accounting: executedEvaluations (verdicts),
passedCount/failedCount, skippedCount (oracle returned not-applicable),
errorCount (oracle THREW — recorded as severity "warning" with reason prefix
"oracle-error:", isolated, never conflated with a game failing a rule).
M5.D populates the registry with the three core oracles — ReplayConsistency
(warning on FAIL), Completion (warning on FAIL), Hazard (warning on spike,
CRITICAL on out-of-bounds; skips Quit). OracleContext additionally carries
RunOutcome = the LAST RunEnded event's outcome in the session's events.jsonl
(multi-run sessions: final state wins); sessions without one are skipped by
outcome-consuming oracles.
Each result: oracleName, sessionId, passed, severity ("info"|"warning"|
"critical"), reason (human-readable), evidence[] (machine-checkable value
strings — report generation cites these verbatim), timestampUtc. Result
order contract: session-major (chronological), oracle-minor (registration
order) — fully deterministic. M5.B ships the FRAMEWORK only: zero registered
oracles is a valid run; concrete oracles arrive next slice via explicit
registration (no reflection).

## 6. Format rules (all files)

UTF-8, no BOM; one JSON object per `\n` line; invariant-culture numerals;
floats to 3 decimals; append-only; valid up to last complete line after a
crash (NFR-1.4). `events.jsonl` of a cleanly closed session ends with the
`SessionEnded` line.
