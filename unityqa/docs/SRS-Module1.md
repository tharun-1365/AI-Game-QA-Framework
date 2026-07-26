# Software Requirements Specification — Module 1
## Instrumented QA Test Environment

**Project:** UnityQA — AI-Powered Automated Game QA Framework for Unity
**Document:** SRS-M1 **v1.1** (APPROVED WITH CHANGES — this version incorporates them)
**Author:** Khanna · **Supervisor:** Senior Game AI Engineer (Claude)
**Date:** 19 July 2026 (v1.0: 18 July 2026)
**Locked context:** Unity 6.3 LTS · **custom-built benchmark platformer ("BenchGame") as Game Under Test (GUT)** · heuristics-first · planted-bug benchmark methodology

---

## Change Log

**v1.1 (19 Jul 2026) — one architectural change, approved by Khanna:** the Unity 2D Platformer Microgame is replaced by **BenchGame**, a small 2D platformer we build ourselves, designed from the start as a QA benchmark. Architecture, instrumentation, event bus, logging, adapter pattern, bug-injection methodology, acceptance structure, and module breakdown are unchanged. Consequences of the change, honestly stated:

- **What improves:** (1) The GUT's movement constants (run speed, jump impulse, gravity ⇒ max jump height/distance) are now *authored and documented*, not reverse-engineered — Module 4's unreachable-area math gets exact ground truth. (2) Risk R1 (Microgame/Unity 6.3 incompatibility) disappears entirely. (3) Determinism can be designed in (fixed timestep, zero randomness), which will make Module 5's replay markedly less flaky. (4) No third-party asset licensing questions in the paper.
- **What it costs:** roughly **+1 week** of build time in Module 1 (the project design doc's timeline already named this trade; M1 now spans ~3 weeks). Mitigated by keeping BenchGame deliberately tiny (Section 1.1).
- **What we must now guard against:** a self-built GUT risks being *unconsciously designed for detectability*, and "hooking into foreign code" is no longer demonstrated in M1. Both are addressed: new risk R8 with its mitigations, a new dependency-direction rule (BenchGame code may never reference UnityQA code — it must be written *as if* foreign), and Module 7's foreign-game adaptation becomes the place where generality is demonstrated rather than assumed.

Sections changed: header, 1, 2, 3 (FR-1.1, +FR-1.19/1.20), 4 (NFR-1.3), 5, 6, 7, 8, 9, 15, 16 (R1, R2, +R8), 17 (AC-1, +AC-14), 18, 19 (Q12, +Q15), 20 (D1, +D13). All other content carries over verbatim from v1.0.

---

## 1. Module Objective

Build the **foundation layer** that every later module stands on: a Unity project containing the Game Under Test, a test level with **deliberately planted, documented bugs**, and a **non-invasive instrumentation layer** that observes the game and turns everything that happens into a stream of structured, timestamped, logged events.

At the end of Module 1 there is **no AI agent yet**. You — a human — play the level while the instrumentation watches. That is deliberate: it lets us validate the *observation* machinery in isolation before any autonomous behavior exists. If the instrumentation is wrong, every later module inherits the error; Module 1 exists to make that impossible.

In one sentence: **Module 1 turns a Unity game into a measurable experiment.**

**Explicitly in scope:** project + repo setup, **building BenchGame (the custom GUT — Section 1.1)**, planted-bug test level, event model, event bus, session lifecycle, structured logging (console + JSONL file), debug overlay, game adapter (observation only), expected-trigger markers, level bounds definition, benchmark ground-truth document, **GUT specification document (movement constants)**.

### 1.1 BenchGame — the Game Under Test (new in v1.1)

BenchGame is **scientific apparatus, not a game project**. The project's emphasis remains the QA framework; BenchGame exists only to be tested, and its design is ruled by three principles: *minimal* (only features needed to express the six bug classes), *deterministic* (fixed physics timestep, zero randomness, no frame-rate-dependent forces), and *specified* (every movement constant documented in `docs/GUT-SPEC.md` before tuning ends).

Complete feature list — nothing more will be built: a player with run + single fixed-height jump (Rigidbody2D, ~1 script); tilemap-based level geometry; spike hazards that kill on contact; kill zones under legitimate pits; checkpoint zones (respawn point update); collectible tokens; a level-exit zone; death → respawn logic; a simple follow camera. Estimated total: **7–8 scripts, ~400 lines**, all under `Assets/TestGame/`. Deliberately excluded: variable-height jump, dashes, enemies with AI, moving platforms, menus, audio, art beyond colored placeholder tiles. Every one of these exclusions is a scope-creep tripwire — wanting any of them is the signal to stop (see R8b).

One rule makes the adapter pattern stay honest now that we own the GUT: **BenchGame must be written as if it were foreign code.** Its scripts live in their own assembly (`BenchGame.asmdef`), never reference any `UnityQA.*` type, and expose ordinary public state and C# events a reasonable game would have anyway. The adapter hooks into those exactly as it would a stranger's game. Dependency direction is enforced by asmdef references: `UnityQA` ← references — `BenchGameAdapter` — references → `BenchGame`, and never the reverse.

**Explicitly out of scope (deferred):** any agent control or input injection (Module 2), coverage grids (Module 3), detectors (Module 4), screenshots/snapshots/replay (Module 5), HTML reports (Module 6), metrics experiments (Module 7).

---

## 2. Research Purpose

This module is not just setup — it embodies three methodological positions you will defend in the viva and, later, in the paper:

**2.1 Fault injection as ground truth.** You cannot measure a bug detector without knowing what bugs exist. By *planting* a documented set of bugs (a technique adjacent to fault injection and mutation testing in software-engineering literature), we create a benchmark with known ground truth. Every later claim — "UnityQA detects X% of soft locks" — is only meaningful because Module 1 defines the answer key. The file `docs/BENCHMARK.md` produced here becomes Table 1 of the experiments chapter.

**2.2 Observation without perturbation.** A QA framework that changes the game it tests is measuring itself, not the game. Module 1 therefore enforces a strict rule: instrumentation is **additive and observational** — it attaches new components and listens, but never modifies GUT gameplay logic or physics. The *only* permitted modifications to the GUT are the planted bugs themselves, and each one is documented. This is the 2D-game analog of the "probe effect" problem in software testing, and being able to name it earns marks.

**2.3 Adapter-mediated generality.** All knowledge of *this particular game* is quarantined inside one class (`BenchGameAdapter`) behind one interface (`IGameAdapter`). The claim "UnityQA is a framework, not a BenchGame plugin" is true exactly to the extent this boundary holds. Because we author the GUT ourselves (v1.1), this claim needs *more* discipline, not less: the dependency-direction rule in Section 1.1 keeps the boundary real, and Module 7's adaptation of a genuinely foreign game is where generality gets *demonstrated* rather than asserted.

**2.4 The GUT as designed benchmark (new in v1.1).** Building the GUT ourselves converts it from a convenience into an instrument. Known, documented movement kinematics give the unreachable-area analysis exact rather than estimated ground truth; designed-in determinism removes a major confound from replay and from run-to-run variance in Module 7's experiments; and the level itself can be shaped so each planted bug is cleanly isolated from the others. The methodological threat this creates — experimenter bias, i.e., unconsciously building a game whose bugs are easy for our own detectors — is named openly here, mitigated by the calibration rules of Section 13 (bugs must be human-findable and casual-glance-invisible) and by R8, and disclosed as a limitation in the eventual paper. Naming your own threat to validity before a reviewer does is what makes this a strength.

---

## 3. Functional Requirements

Numbered for traceability; later modules and tests will reference these IDs.

| ID | Requirement | Priority |
|---|---|---|
| FR-1.1 | The system SHALL contain BenchGame, a custom 2D platformer implementing exactly the feature list of Section 1.1 (run, fixed jump, tilemap geometry, spikes, kill zones, checkpoints, tokens, level exit, death/respawn, follow camera) and nothing beyond it. | Must |
| FR-1.2 | The system SHALL provide a test scene `Level_PlantedBugs_A`, derived from the BenchGame baseline level, containing exactly the planted bugs specified in Section 13 and no undocumented modifications. | Must |
| FR-1.3 | The system SHALL provide a baseline scene `Level_Baseline` — the same level with **zero** planted bugs — for false-positive testing by later modules. | Must |
| FR-1.4 | A QA session SHALL be startable and stoppable via an in-game key (default **F9**) and via an inspector button, without recompiling. | Must |
| FR-1.5 | On session start, the system SHALL generate a unique session ID (`yyyyMMdd-HHmmss` + short random suffix) and emit a `SessionStarted` event. | Must |
| FR-1.6 | While a session is active, the system SHALL emit structured events for at minimum: player position samples (at a configurable rate, default 5 Hz), player death, player respawn, token/pickup collection, expected-trigger firings, and session start/end. | Must |
| FR-1.7 | Every event SHALL carry: session ID, monotonically increasing sequence number, session time (seconds, float), frame count, event type, world position (where applicable), and a type-specific payload. | Must |
| FR-1.8 | All events SHALL be routed through a single publish/subscribe event bus; no component may log or consume game observations through any other path. | Must |
| FR-1.9 | The system SHALL write all session events to a JSONL file (one JSON object per line, append-only) in a per-session folder under `Application.persistentDataPath/UnityQA/Sessions/<sessionId>/`. | Must |
| FR-1.10 | The system SHALL mirror events to the Unity Console with a `[UnityQA]` prefix, filterable by minimum severity, toggleable in config. | Must |
| FR-1.11 | The system SHALL display an on-screen debug overlay showing: session state (idle/running), session ID, elapsed time, total event count, and the last 3 events. Toggleable via key (default **F10**). | Must |
| FR-1.12 | All tunable values (sample rate, output paths, console verbosity, overlay defaults) SHALL live in a `QAConfig` ScriptableObject — zero magic numbers in code. | Must |
| FR-1.13 | The system SHALL provide a `QAExpectedTrigger` marker component that, when its trigger zone fires, publishes a `TriggerFired` event carrying the trigger's declared ID; at session end the system SHALL emit a summary event listing expected trigger IDs that never fired. | Must |
| FR-1.14 | The system SHALL provide a `LevelBounds` component defining the rectangular playable region of a level, visible as an editor gizmo, and queryable by later modules. | Must |
| FR-1.15 | Ending a session (manually or by exiting Play mode) SHALL flush and close the JSONL file such that the log is valid and complete (no truncated final line). | Must |
| FR-1.16 | Each planted bug SHALL be documented in `docs/BENCHMARK.md` with ID, class, location, planting method, expected observable symptom, and the future module expected to detect it. | Must |
| FR-1.17 | The system SHOULD emit a `BoundsExited` event when the player leaves `LevelBounds` (raw observation only — *classification* as a bug is Module 4's job). | Should |
| FR-1.18 | The instrumentation SHOULD function identically in the Editor and in a standalone build. | Should |
| FR-1.19 | BenchGame SHALL be deterministic by construction: physics driven from `FixedUpdate` on the default fixed timestep, no use of randomness, no frame-rate-dependent forces or timers affecting gameplay. | Must |
| FR-1.20 | BenchGame's movement constants (run speed, jump impulse, gravity scale) and their derived kinematics (max jump height, max jump distance at full run speed) SHALL be documented in `docs/GUT-SPEC.md`, and the derived values SHALL be empirically verified in-engine to within 5% (see AC-14). | Must |

---

## 4. Non-Functional Requirements

| ID | Requirement | Priority |
|---|---|---|
| NFR-1.1 | **Non-perturbation:** instrumentation must not modify GUT gameplay scripts, physics settings, or timing. Permitted touchpoints: adding components/objects, subscribing to existing events, reading public state. The single documented exception mechanism for planted bugs is defined in Section 13. | Must |
| NFR-1.2 | **Performance:** with a session running, median frame time on the test level must not degrade by more than 5% versus uninstrumented play; steady-state per-frame managed allocations from UnityQA code ≈ 0 bytes outside of event emission moments (no per-frame string concatenation, no LINQ in `Update`). | Must |
| NFR-1.3 | **Isolation, both directions:** all framework code lives under `Assets/UnityQA/` in namespace `UnityQA.*` behind an assembly definition; the UnityQA assembly must compile even if the TestGame folder is deleted (the adapter is the only bridge, via interface). Symmetrically (v1.1): BenchGame code must never reference any `UnityQA.*` type — enforced by asmdef reference lists (Section 1.1). | Must |
| NFR-1.4 | **Crash-safety:** the JSONL log must be readable up to the last completed line even if the Editor crashes mid-session (append + flush policy, no end-of-file footer required for validity). | Must |
| NFR-1.5 | **Determinism of records:** event sequence numbers are strictly increasing with no gaps within a session; identical manual play should produce structurally identical logs (same event types in same causal order; timestamps naturally vary). | Must |
| NFR-1.6 | **Explainability:** every class ≤ ~200 lines, single responsibility, XML doc comment on every public member explaining *why* not just *what* (Development Rule 6). | Must |
| NFR-1.7 | **Portability of output:** the event schema is versioned (`schemaVersion` field in session metadata) so later modules can evolve it without breaking old logs. | Should |
| NFR-1.8 | **Zero external runtime dependencies** beyond Newtonsoft JSON (already justified in the design doc). | Must |

---

## 5. System Architecture

Module 1 realizes the bottom two layers of the project architecture (Core/Harness + Game Under Test) plus the observation half of the Adapter seam.

```
                    (future: Modules 2–6 subscribe here)
                                   ▲
                                   │ QAEvent stream
┌──────────────────────────────────┴───────────────────────────────┐
│                          QAEventBus                              │
│              Publish(QAEvent) / Subscribe / Unsubscribe          │
└───────▲──────────────▲───────────────▲──────────────────▲───────┘
        │              │               │                  │
   publishes      publishes       publishes          subscribes
        │              │               │                  │
┌───────┴──────┐ ┌─────┴────────┐ ┌────┴─────────┐ ┌──────┴───────┐
│  QARunner    │ │ Observation  │ │ QAExpected   │ │  QALogger    │
│  (lifecycle: │ │ Sampler      │ │ Trigger ×N   │ │  ├ Console   │
│  start/stop, │ │ (position @  │ │ (markers in  │ │  │  sink     │
│  session ID, │ │  5 Hz, death,│ │  the level)  │ │  └ JSONL     │
│  end-of-run  │ │  pickups,    │ │              │ │     sink     │
│  summary)    │ │  bounds)     │ │              │ │              │
└──────┬───────┘ └─────┬────────┘ └──────────────┘ └──────────────┘
       │               │ reads game state through
       │               ▼
       │        ┌──────────────────┐        ┌──────────────────┐
 reads │        │  IGameAdapter    │──impl──│ BenchGameAdapter │
       ▼        │  (observation    │        │ (the ONLY class  │
┌──────────────┐│   contract)      │        │  that knows      │
│  QAConfig    │└──────────────────┘        │  BenchGame)      │
│ (Scriptable  │                            └────────┬─────────┘
│  Object)     │                                     │ hooks into
└──────────────┘                            ┌────────▼─────────┐
   ┌──────────────┐  ┌──────────────┐       │  GAME UNDER TEST │
   │ QADebug      │  │ LevelBounds  │       │  BenchGame       │
   │ Overlay (UI, │  │ (gizmo +     │       │  (custom, §1.1)  │
   │ subscribes)  │  │  query API)  │       │  + planted bugs  │
   └──────────────┘  └──────────────┘       └──────────────────┘
```

**Architectural decisions and their justifications** (Development Rule 7):

1. **One event bus, not direct references.** Modules 2–6 will all need the same observations. A pub/sub bus means adding a consumer never touches a producer. Cost: slight indirection. Benefit: the entire future of the project plugs into this one seam. Kept deliberately primitive — a list of subscribers and a `Publish` loop — because Rule 8 says simple beats clever (no reflection, no attribute magic, no third-party messaging library).
2. **Adapter interface from day one, even with one game.** Interfaces with a single implementation are usually over-engineering; here the interface *is* the research claim (Section 2.3). The exception is justified, and you should be able to argue both sides in the viva.
3. **ScriptableObject config, not inspector fields scattered across components.** One asset = one place to see every knob = reproducible experiment settings you can commit to Git per-benchmark-run.
4. **JSONL, not JSON array or database.** Append-only lines are crash-safe (NFR-1.4), stream-friendly, diff-friendly, and trivially parsed by any language — Module 7's Python analysis scripts will read them directly. A database is unjustifiable complexity at this scale (Rule 8).
5. **Session as the unit of everything.** One session = one folder = one log = (later) one report. This one-to-one-to-one mapping is what keeps Modules 5–7 simple.

---

## 6. Folder Structure

Delta to the repository structure already approved in the design document — Module 1 creates the parts marked `←M1`:

```
unityqa/
├── docs/
│   ├── SRS-Module1.md                ←M1  this document
│   ├── BENCHMARK.md                  ←M1  planted-bug ground truth (Section 13)
│   ├── EVENT-SCHEMA.md               ←M1  event model reference (Section 14)
│   └── MODULES.md                    ←M1  entry #1 written at module close
└── UnityQA/                                Unity 6.3 LTS project
    ├── Assets/
    │   ├── UnityQA/
    │   │   ├── Core/                 ←M1
    │   │   │   ├── QARunner.cs
    │   │   │   ├── QAConfig.cs
    │   │   │   ├── QAEvent.cs
    │   │   │   ├── QAEventType.cs
    │   │   │   ├── QAEventBus.cs
    │   │   │   ├── QASessionInfo.cs
    │   │   │   ├── ObservationSampler.cs
    │   │   │   ├── LevelBounds.cs
    │   │   │   └── QAExpectedTrigger.cs
    │   │   ├── Logging/              ←M1
    │   │   │   ├── QALogger.cs
    │   │   │   ├── ConsoleSink.cs
    │   │   │   ├── JsonlSink.cs
    │   │   │   └── ILogSink.cs
    │   │   ├── UI/                   ←M1
    │   │   │   └── QADebugOverlay.cs
    │   │   ├── Adapters/             ←M1
    │   │   │   ├── IGameAdapter.cs
    │   │   │   └── BenchGameAdapter.cs
    │   │   ├── Config/               ←M1
    │   │   │   └── DefaultQAConfig.asset
    │   │   └── UnityQA.asmdef        ←M1
    │   ├── TestGame/                 ←M1  BenchGame — the custom GUT (§1.1)
    │   │   ├── Scripts/                    PlayerController2D, GameManager, Hazard,
    │   │   │                               KillZone, Checkpoint, Token, LevelExit,
    │   │   │                               FollowCamera  (~8 scripts, ~400 lines)
    │   │   ├── BenchGame.asmdef            no reference to UnityQA — ever (NFR-1.3)
    │   │   ├── Tiles/                      placeholder tile palette (colored squares)
    │   │   └── Levels/
    │   │       ├── Level_Baseline.unity
    │   │       └── Level_PlantedBugs_A.unity
    │   └── Tests/
    │       ├── EditMode/             ←M1  UnityQA.Tests.EditMode.asmdef + tests
    │       └── PlayMode/             ←M1  UnityQA.Tests.PlayMode.asmdef + tests
    ├── Packages/manifest.json        ←M1  pinned (Newtonsoft, Input System, URP…)
    └── ProjectSettings/              ←M1  Force Text serialization, Visible Meta Files
```

Note the file count: **~15 framework scripts + ~8 BenchGame scripts, most under 100 lines.** Module 1 is wide and shallow by design. `docs/GUT-SPEC.md` (movement constants, FR-1.20) joins the docs list alongside BENCHMARK.md and EVENT-SCHEMA.md.

---

## 7. Scene Hierarchy

Both test scenes share this structure; `Level_Baseline` simply omits the planted bugs.

```
Level_PlantedBugs_A                        (scene)
├── [QA]                                   ← everything UnityQA-owned lives under one root
│   ├── QARunner                           (QARunner, BenchGameAdapter, ObservationSampler)
│   ├── QALogger                           (QALogger)
│   ├── LevelBounds                        (LevelBounds — sized to the level, gizmo visible)
│   ├── ExpectedTriggers                   ← empty parent for organization
│   │   ├── ET_Checkpoint_Mid              (QAExpectedTrigger id="checkpoint.mid")
│   │   ├── ET_LevelExit                   (QAExpectedTrigger id="level.exit")
│   │   └── ET_TokenCluster_Upper          (QAExpectedTrigger id="tokens.upper")
│   └── QACanvas                           (Canvas + QADebugOverlay, top-right corner)
├── GameManager                            ← BenchGame's own objects (never reference UnityQA)
├── Grid → Tilemap(s)                      ← level geometry; planted-bug edits live here
├── Player                                 ← BenchGame player (PlayerController2D)
├── Main Camera (FollowCamera)             ← simple follow, no Cinemachine (Rule 8)
├── Tokens / Spikes / KillZones /          ← BenchGame gameplay objects, untouched
│   Checkpoints / LevelExit                   except documented plants
└── [PLANTED] BugMarkers                   ← EDITOR-ONLY annotations (Section 13):
    ├── PB_001_FallOutOfWorld                 invisible gizmo markers at each planted
    ├── PB_002_SoftLockPit                    bug site, tagged EditorOnly so they are
    ├── PB_003_UnreachablePlatform            stripped from builds and NEVER visible
    ├── PB_004_MissingTrigger                 to the agent or detectors at runtime —
    ├── PB_005_ColliderGap                    they exist for humans and for Module 7's
    └── PB_006_InvisibleWall                  ground-truth scoring only
```

Two rules encoded here, both viva-worthy: everything UnityQA adds is **corralled under `[QA]` and `[PLANTED]` roots**, making the diff between an instrumented scene and a virgin scene visually obvious; and ground-truth markers are **EditorOnly**, so the framework can never accidentally "cheat" by reading the answer key at runtime.

---

## 8. Class Responsibilities

One line of *responsibility* and one line of *justification* each. (Files map 1:1 to classes; Section 9 covers runtime placement and lifecycle.)

- **`QAEvent`** — immutable data record for one observation (who/what/when/where + payload). *The single currency of the whole framework; immutability means consumers can never corrupt history.*
- **`QAEventType`** — enum of event kinds (Section 14 lists them). *An enum, not strings, so typos are compile errors.*
- **`QAEventBus`** — minimal pub/sub: `Publish`, `Subscribe`, `Unsubscribe`. *The seam every future module plugs into; deliberately ~50 lines.*
- **`QARunner`** — session lifecycle owner: start/stop (key + inspector), session ID minting, wiring components to the bus, emitting `SessionStarted`/`SessionEnded` and the end-of-session unfired-triggers summary. *Exactly one object owns "is a session running?" — ambiguity here would poison every log.*
- **`QAConfig`** — ScriptableObject of all tunables. *Reproducibility: an experiment's settings are an asset, not tribal knowledge.*
- **`QASessionInfo`** — metadata for the current session (ID, level name, start time, config snapshot, schema version); serialized once per session as `session.json`. *Logs without metadata are archaeology.*
- **`ObservationSampler`** — polls the adapter on a timer (position @ 5 Hz, bounds checks) and relays adapter-raised happenings (death, respawn, pickups) as events. *Separates "what to watch" from "how the game exposes it" (adapter) and from "what happens to observations" (bus).*
- **`LevelBounds`** — authored rectangle of legal play space; gizmo; `Contains(point)` query. *Ground truth for "outside the world" must be authored, not guessed.*
- **`QAExpectedTrigger`** — declarative marker: "a correctly functioning level fires this." Publishes `TriggerFired` on activation. *Turns implicit designer intent into explicit, checkable specification — the heart of missing-trigger detection later.*
- **`IGameAdapter`** — observation contract (Section 12). *The generality boundary.*
- **`BenchGameAdapter`** — implements the contract using BenchGame's public state and events (player transform, death/respawn/token events). *The only file allowed to reference BenchGame code — enforced by asmdef reference lists (NFR-1.3), not just convention.*

**BenchGame classes (v1.1 — the GUT itself, all in `BenchGame.asmdef`, none aware of UnityQA):** `PlayerController2D` (run + fixed jump, grounded check, publishes ordinary C# events for died/respawned as any well-written game would); `GameManager` (respawn point tracking, death handling); `Hazard` (kill on contact); `KillZone` (kill volume under legitimate pits); `Checkpoint` (updates respawn point on trigger); `Token` (collectible, raises collected event); `LevelExit` (end-of-level trigger); `FollowCamera` (lerped follow). Design constraint for every one of them: *write it as a normal small game, not as a test rig* — no QA hooks, no special cases; the adapter must earn its observations the same way it would from foreign code.
- **`QALogger`** — bus subscriber that fans events out to sinks; owns sink lifecycle (open on `SessionStarted`, flush/close on `SessionEnded`). *One consumer, many outputs.*
- **`ILogSink` / `ConsoleSink` / `JsonlSink`** — sink contract and its two implementations. *Adding Module 5's evidence writers later = new sinks, zero changes to QALogger.*
- **`QADebugOverlay`** — on-screen HUD (session state, ID, elapsed, event count, last 3 events). *Instant visual feedback is what makes the module demonstrable (Rule 10).*

---

## 9. Script Responsibilities (runtime placement & lifecycle)

| Script | Lives on | Executes | Notes |
|---|---|---|---|
| `QARunner` | `[QA]/QARunner` | `Awake` (wiring), `Update` (F9 poll), `OnApplicationQuit`/`OnDisable` (safety end-session) | The only component with key-input polling |
| `BenchGameAdapter` | same object | `Awake` (find player, hook GUT events), `OnDestroy` (unhook) | Fails loudly at `Awake` if the BenchGame objects it expects are missing |
| `ObservationSampler` | same object | coroutine started/stopped by QARunner | 5 Hz timer via `WaitForSeconds`; no per-frame work |
| `QAConfig` | asset in `Config/` | n/a (data) | Referenced by QARunner; a per-scene override slot exists but default asset is the norm |
| `LevelBounds` | `[QA]/LevelBounds` | `OnDrawGizmos` only | Pure data + gizmo; queried by others |
| `QAExpectedTrigger` | each `ET_*` object | `OnTriggerEnter2D` | Requires a 2D trigger collider on the same object; publishes once per session (latching) |
| `QALogger` | `[QA]/QALogger` | subscribes in `OnEnable`, unsubscribes `OnDisable` | Owns file handles; flush policy per NFR-1.4 |
| `QADebugOverlay` | `[QA]/QACanvas` | subscribes on enable; lightweight `Update` for elapsed-time text | F10 visibility toggle |
| `QAEventBus`, `QAEvent`, `QASessionInfo`, `ILogSink`, sinks, `IGameAdapter` | — | plain C# (not MonoBehaviours) | Testable in EditMode without a scene — this is deliberate and viva-worthy |

Execution-order note: `QARunner`'s wiring must precede any publisher's first publish; we guarantee this by doing all wiring in `Awake` and all publishing no earlier than `Start` — a one-line rule that avoids Script Execution Order settings entirely (Rule 8).

---

## 10. Data Flow

```
QAConfig.asset ──(read once at session start; snapshot into)──► QASessionInfo
                                                                     │
                                                          written as session.json
                                                                     ▼
GUT state ──► BenchGameAdapter ──► ObservationSampler ──► QAEvent ──► QAEventBus
(player pos,     (normalizes to      (samples/relays,      (immutable    │
 deaths,          game-agnostic       stamps seq/time)      record)      │
 pickups)         observations)                                          │
                                                        ┌────────────────┼────────────────┐
                                                        ▼                ▼                ▼
                                                  ConsoleSink       JsonlSink       QADebugOverlay
                                                  (dev feedback)    (events.jsonl)  (live HUD)
                                                                         │
                                                                         ▼
                                        persistentDataPath/UnityQA/Sessions/<sessionId>/
                                        ├── session.json      (metadata, config snapshot)
                                        └── events.jsonl      (the record of everything)
```

Key property: data flows **one direction only** (game → adapter → sampler → bus → sinks). Nothing downstream writes back into the game. When Module 2 adds control, it will be a *separate* channel (agent → virtual input → game), keeping observation and actuation cleanly split — the classic sense/act loop.

---

## 11. Event Flow

Chronological trace of one representative manual session on `Level_PlantedBugs_A` (T = session seconds):

```
T=0.00  F9 pressed → QARunner mints session 20260718-143012-k3f
T=0.00  SessionStarted            {level:"Level_PlantedBugs_A", schemaVersion:1}
T=0.00  session.json written; events.jsonl opened
T=0.05  PlayerSpawned             {pos:(2.0,1.5)}
T=0.20  PlayerPositionSampled     {pos:(2.4,1.5), vel:(2.1,0)}     ← every 0.2 s hereafter
T=3.40  TokenCollected            {tokenId:"Token (7)", pos:(8.5,3.0)}
T=6.00  TriggerFired              {triggerId:"checkpoint.mid"}      ← ET marker latches
T=9.80  PlayerDied                {pos:(14.2,0.8), cause:"hazard"}
T=10.4  PlayerSpawned             {pos:(12.0,2.0)}                  ← respawn observed
T=15.2  BoundsExited              {pos:(22.7,-6.3)}                 ← walked into BUG-001 pit
        (raw observation only — Module 4 will classify this; Module 1 just records)
T=21.0  F9 pressed → QARunner begins shutdown
T=21.0  ExpectedTriggersSummary   {expected:3, fired:["checkpoint.mid"],
                                   unfired:["level.exit","tokens.upper"]}
T=21.0  SessionEnded              {durationSec:21.0, eventCount:117}
        JsonlSink flushes and closes → log valid per NFR-1.4
```

Rules visible in the trace: every event goes through the bus (FR-1.8); sequence numbers (omitted above for readability) increase without gaps (NFR-1.5); the summary event fires *before* `SessionEnded` so the log's last line is always `SessionEnded` — a cheap integrity check Module 7 will exploit.

---

## 12. Public Interfaces Between Scripts

Specification-level signatures (contracts, not code — implementation happens after approval):

**`IGameAdapter`** — the observation contract; everything game-specific hides behind it:

| Member | Type | Meaning |
|---|---|---|
| `PlayerPosition` | `Vector2` property | Player's world position |
| `PlayerVelocity` | `Vector2` property | Player's current velocity |
| `IsPlayerAlive` | `bool` property | False between death and respawn |
| `SpawnPosition` | `Vector2` property | Initial/current spawn point |
| `PlayerDied` | event (cause string) | Raised when the GUT kills the player |
| `PlayerRespawned` | event | Raised on respawn |
| `TokenCollected` | event (id, position) | Raised on pickup collection |
| `Initialize()` / `Teardown()` | methods | Hook/unhook GUT internals; `Initialize` throws descriptively if the GUT is not recognized |

**`QAEventBus`:** `Publish(QAEvent e)` · `Subscribe(Action<QAEvent> handler)` · `Unsubscribe(handler)`. Synchronous, main-thread only (documented constraint — Unity API access in handlers stays legal; Rule 8 says no threading until a measurement demands it).

**`QAEvent`** (shape): `SessionId : string` · `Seq : long` · `SessionTime : float` · `Frame : int` · `Type : QAEventType` · `Position : Vector2?` · `Payload : Dictionary<string, object>` — immutable after construction.

**`QARunner`:** `StartSession()` · `EndSession()` · `IsSessionActive : bool` · `CurrentSession : QASessionInfo`.

**`ILogSink`:** `Open(QASessionInfo session)` · `Write(QAEvent e)` · `Flush()` · `Close()`.

**`LevelBounds`:** `Bounds : Rect` · `Contains(Vector2 point) : bool`.

**`QAExpectedTrigger`:** `TriggerId : string` (inspector-set) · `HasFired : bool` (latching, reset per session).

Stability promise: these signatures are the contract Modules 2–7 build against. Changing them after Module 1 approval requires a documented decision entry in MODULES.md (cheap process, but forces the habit of API discipline).

---

## 13. Bug Injection Design

**Method.** Each bug is planted by a minimal, reversible, documented edit to `Level_PlantedBugs_A` only (never to shared prefabs or GUT scripts — edits to shared assets would leak into `Level_Baseline` and destroy the false-positive control). Each site gets an EditorOnly `PB_xxx` marker (Section 7) recording ground-truth position and radius for Module 7's automated scoring. `docs/BENCHMARK.md` is the authoritative registry; this table is its seed:

| ID | Class | Planting method (level-data edit) | Expected observable symptom | Detected by (future) | Severity |
|---|---|---|---|---|---|
| BUG-001 | Fall-out-of-world | Widen an existing pit; delete the kill/respawn zone beneath it | Player falls past bounds and never dies/respawns | M4 OutOfBounds | Critical |
| BUG-002 | Soft lock | Add a pit with walls higher than max jump; no hazard inside | Player alive, inputs work, but can never leave the pit | M4 Stuck/SoftLock | Critical |
| BUG-003 | Unreachable area | Move one platform (with a token on it) ~1.5 units above max jump reach | Content exists that no input sequence can reach | M4/M3 UnreachableArea | Major |
| BUG-004 | Missing trigger | Disable the trigger collider on the mid-level checkpoint (object present, looks normal) | Checkpoint never fires; `checkpoint.mid` unfired at session end | M4 MissingTrigger (via FR-1.13 data) | Major |
| BUG-005 | Collider gap | Remove the collider from one floor tile (sprite still drawn) | Player falls through visually solid ground | M4 OutOfBounds (+ position/geometry cross-check) | Critical |
| BUG-006 | Invisible wall / navigation failure | Add a transparent BoxCollider2D blocking a corridor | Progress blocked with no visual cause | M4 Stuck + M3 coverage anomaly | Major |

**Calibration rules.** (a) Every bug must be *findable by an ordinary player* — you will personally reproduce each one during acceptance testing (Section 18); if a human can't hit it, the benchmark is unfair to the agent. (b) Every bug must be *invisible to a casual glance* — no floating "BUG HERE" geometry; the level must look legitimate. (c) One bug per class from the project's bug taxonomy, so Module 4 gets exactly one positive example per detector, plus `Level_Baseline` as the all-negative control. (d) Planting is a per-bug toggleable: each plant is isolated so a future `Level_PlantedBugs_B` can remix classes without archaeology.

**What Module 1 does with these bugs: nothing.** It only records raw events near them (e.g., `BoundsExited` at BUG-001). Detection is Module 4. Keeping "observe" and "judge" in different modules is the architecture talking.

---

## 14. Event Logging Design

**Schema (v1).** One JSON object per line in `events.jsonl`:

```
{"sid":"20260718-143012-k3f","seq":42,"t":9.80,"frame":588,
 "type":"PlayerDied","pos":{"x":14.2,"y":0.8},"payload":{"cause":"hazard"}}
```

Field rules: `sid`/`seq`/`t`/`frame`/`type` are mandatory on every event; `pos` present when spatially meaningful; `payload` is a flat string-keyed map, kept shallow deliberately (deep nesting is where log schemas go to die). `session.json` carries `schemaVersion`, level name, config snapshot, Unity version, and app version — everything needed to interpret the log without the project open. Full reference lives in `docs/EVENT-SCHEMA.md`.

**Event types introduced in Module 1** (`QAEventType`): `SessionStarted`, `SessionEnded`, `PlayerSpawned`, `PlayerDied`, `PlayerPositionSampled`, `TokenCollected`, `TriggerFired`, `BoundsExited`, `ExpectedTriggersSummary`, `AdapterWarning` (adapter saw something it couldn't normalize — observability for the observer). The enum is extended, never reordered, in later modules (stable serialized values).

**Sink behavior.** ConsoleSink: severity-filtered, `[UnityQA]`-prefixed, off by default in builds. JsonlSink: opens lazily on `SessionStarted`, writes every event immediately, flushes on write of *lifecycle* events and every N=25 events otherwise (compromise between NFR-1.2 and NFR-1.4 — say this trade-off out loud in the viva), closes on `SessionEnded` and defensively in `OnDisable`.

**Why logging is a Module 1 concern and not Module 5's.** Module 5 (Evidence) adds *rich* evidence — screenshots, state snapshots, replay data. But the raw event record must exist from the first day the agent runs (Module 2), because we debug the agent *with* these logs. Logging is infrastructure; evidence is product.

---

## 15. Future Dependencies (what later modules consume from Module 1)

| Future module | Consumes from Module 1 |
|---|---|
| M2 Exploration Agent | `IGameAdapter` (extended with a control surface — the observation half stays frozen), `QAEventBus` (publishes agent decision events), `QARunner` session lifecycle, `QAConfig` |
| M3 Coverage Mapping | `PlayerPositionSampled` stream, `LevelBounds` (grid extents), the bus |
| M4 Bug Detection | Every event type, `LevelBounds`, `QAExpectedTrigger`/`ExpectedTriggersSummary` (missing-trigger data), `Level_Baseline` (false-positive control), BENCHMARK.md (calibration), **GUT-SPEC.md jump kinematics (exact inputs for unreachable-area analysis — v1.1)** |
| M5 Evidence Collection | `ILogSink` (new sinks slot in), session folder convention, `QAEvent` timeline |
| M6 Report Generator | `session.json` + `events.jsonl` formats (EVENT-SCHEMA.md is its input spec) |
| M7 Evaluation | BENCHMARK.md ground truth + `PB_xxx` marker positions (scoring), session logs (metrics), both levels |

The inverse reading matters more: **nothing in Module 1 references any future module.** Dependency arrows point strictly forward in time. If a later module needs something Module 1 doesn't expose, we extend Module 1's contracts by decision entry — we never reach around them.

---

## 16. Risks

| # | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| R1 | **BenchGame build overrun** (v1.1 — replaces the retired Microgame-compatibility risk): building even a tiny platformer takes a beginner-to-intermediate developer longer than expected, especially player-controller feel (jump tuning is a famous time sink) | Medium | High | Hard feature freeze at the Section 1.1 list; placeholder art only (colored tiles); jump constants chosen by formula from desired jump height, then locked into GUT-SPEC.md — "good enough to test, not good to play" is the bar. Time-box: **1 week**; if exceeded, cut Checkpoint + LevelExit to trigger-only stubs and proceed |
| R2 | BenchGame accidentally written *around* the framework (QA-aware game code, gameplay logic leaking into the adapter, or hooks no normal game would have) — quietly invalidating the adapter claim (§2.3) | Medium | Medium | Dependency direction enforced by asmdef (NFR-1.3); code-review rule: every BenchGame public member must be justifiable as "a normal game would have this anyway"; adapter review asks the reverse question |
| R3 | Over-engineering the bus/logger (beginner-adjacent temptation to add features "we'll need later") | High | Medium | Line budgets (bus ≈ 50 lines, logger ≈ 100); Rule 8; supervisor review before merge |
| R4 | Planted bugs accidentally leak into `Level_Baseline` via shared prefab/tile edits | Medium | High | Planting rule: scene-local edits only (Section 13); acceptance test AT-9 explicitly plays Baseline checking for all six symptoms |
| R5 | Logging overhead breaks NFR-1.2 (string/JSON allocation churn) | Low | Medium | 5 Hz sampling (not per-frame), serialization only at write time, measured with the Profiler during acceptance |
| R6 | Scope creep into Module 2 ("the sampler's done, let me just make the player move…") | High | Low | Hard rule: Module 1 merges with zero control-injection code. The urge is the signal that M1 is done |
| R7 | Editor crash mid-session corrupts logs | Low | Medium | NFR-1.4 flush policy; acceptance test AT-8 kills Play mode mid-session and verifies log validity |
| R8 | **Experimenter bias** (v1.1): (a) a self-built GUT unconsciously designed so our detectors look good; (b) the sibling temptation — polishing BenchGame like a game project instead of an apparatus | Medium | Medium (a) / Low (b) | (a) §2.4 mitigations: calibration rules (human-findable, casual-glance-invisible), disclosure as a stated limitation in the paper, Module 7 foreign-game adaptation as the external check; (b) Section 1.1 exclusion list is a hard tripwire — any "wouldn't it be cool if the player could…" thought gets written to FUTURE-IDEAS.md and dropped |

---

## 17. Acceptance Criteria

Module 1 is **done** when all of the following hold (each maps to requirements; each is demonstrated live to me before we open Module 2):

1. **AC-1** Fresh clone → open in Unity 6.3 LTS → press Play on `Level_PlantedBugs_A` → BenchGame runs with zero console errors, and the full gameplay loop works by hand: run, jump, collect a token, die on a spike, respawn at a checkpoint, reach the exit. (FR-1.1, FR-1.2)
2. **AC-2** F9 starts a session: overlay switches to *running* with a fresh session ID; F9 again ends it cleanly. F10 toggles the overlay. (FR-1.4, FR-1.5, FR-1.11)
3. **AC-3** A 2-minute manual play session produces `session.json` + `events.jsonl` in the correct folder; the JSONL parses line-by-line with zero malformed lines; last line is `SessionEnded`. (FR-1.9, FR-1.15)
4. **AC-4** The log from AC-3 contains every mandatory event type actually exercised: position samples at 5 Hz ±10%, ≥1 death, ≥1 respawn, ≥1 token collection, ≥1 trigger firing. (FR-1.6, FR-1.7)
5. **AC-5** Walking into the BUG-001 pit produces a `BoundsExited` event with a plausible position. (FR-1.17, FR-1.14)
6. **AC-6** Ending a session having deliberately skipped the level exit yields an `ExpectedTriggersSummary` listing `level.exit` as unfired. (FR-1.13)
7. **AC-7** All six planted bugs are *manually reproduced by you*, each matching its documented symptom in BENCHMARK.md, while I watch (screen-share or recording). (FR-1.16, Section 13 calibration rule a)
8. **AC-8** Force-stopping Play mode mid-session leaves a valid, parseable log up to the last completed line. (NFR-1.4)
9. **AC-9** A full manual sweep of `Level_Baseline` shows none of the six symptoms and fires all expected triggers. (FR-1.3, R4)
10. **AC-10** Profiler comparison instrumented-vs-not on the same level: median frame-time delta ≤ 5%. (NFR-1.2)
11. **AC-11** All EditMode and PlayMode tests in Section 18 pass in the Unity Test Runner. (NFR-1.3 et al.)
12. **AC-12** Docs complete: BENCHMARK.md (six bugs), EVENT-SCHEMA.md, MODULES.md entry #1. Repo state: PR from `module/m1-instrumentation` merged to `main`, tagged `v0.1.0`. (FR-1.16)
13. **AC-13** *The viva gate:* you answer the Section 19 questions without notes, to my satisfaction. Understanding is a deliverable (your Rule: "I want to completely understand every module").
14. **AC-14** (v1.1) `docs/GUT-SPEC.md` exists, lists all movement constants, and its *derived* kinematics are verified in-engine: measured max jump height and max jump distance (read from position samples in a session log — the instrumentation validating the game, pleasingly circular) each within 5% of the documented values. (FR-1.19, FR-1.20)

---

## 18. Testing Plan

**Level 1 — EditMode unit tests** (pure C#, no scene, milliseconds to run):
- `EventBus_PublishReachesAllSubscribers`, `EventBus_UnsubscribeStopsDelivery`, `EventBus_SubscriberExceptionDoesNotBreakOthers`
- `QAEvent_SequenceNumbersAreStrictlyIncreasing`
- `SessionId_FormatMatchesSpec_AndIsUniqueAcross1000Mints`
- `JsonlSink_WritesOneValidJsonObjectPerLine` (against a temp file)
- `JsonlSink_ReopenAfterSimulatedCrash_LogParsesToLastCompleteLine`
- `QAEvent_SerializationRoundTrip_PreservesAllFields`
- `LevelBounds_ContainsLogic_EdgesAndOutside`

**Level 2 — PlayMode integration tests** (scripted scene, seconds to run):
- `Session_StartStop_EmitsLifecycleEventsInOrder`
- `Sampler_EmitsPositionAtConfiguredRate` (±10% tolerance over 3 simulated seconds)
- `ExpectedTrigger_FiresOnceAndLatches` (test rig moves a dummy body through it twice)
- `BoundsExit_EmitsWhenBodyLeavesRect`
- `Adapter_InitializeThrowsDescriptivelyOnUnknownScene` (negative test — error quality is a feature)
- (v1.1) BenchGame sanity: `Player_JumpApexMatchesGutSpecWithin5Percent`, `Player_DiesOnHazardAndRespawnsAtCheckpoint`, `Token_RaisesCollectedEventOnce`

**Level 3 — Manual test protocol** (you, with a checklist, ~30 min): the AC-1…AC-10 walkthrough above, executed in order, results recorded in a dated checklist file committed to the repo. Manual testing is legitimate testing when it's *scripted, recorded, and repeatable* — another viva line.

**What we deliberately do not test in M1:** agent behavior (none exists), detector logic (none exists), log *content* semantics beyond schema validity (Module 7's analysis owns that).

---

## 19. Viva Questions You Should Be Able to Answer

Practice these out loud. Bracketed hints point at the section holding the answer.

1. Why does the framework observe the game through an adapter interface instead of reading BenchGame classes directly — *especially* now that we wrote BenchGame ourselves? What breaks if you skip it? [§2.3, §5-D2, §1.1]
2. Why plant bugs deliberately instead of testing on a game with natural bugs? What does this enable that natural bugs cannot? [§2.1]
3. What is the "probe effect," and name three concrete rules in this module that guard against it. [§2.2, NFR-1.1, §7]
4. Why JSONL rather than a single JSON array — give the crash-safety argument and one other. [§5-D4, NFR-1.4]
5. Walk me through one event's life from the player collecting a token to a line existing on disk. [§10, §11]
6. Why is the event bus synchronous and single-threaded, and why is that acceptable here? [§12]
7. Why does `Level_Baseline` exist? What statistical concept does it serve for Module 4? [FR-1.3, §13c — it's the negative control / false-positive measurement]
8. Why are the `PB_xxx` ground-truth markers EditorOnly? What would it mean for the research if the runtime could see them? [§7 — the framework must not read the answer key]
9. Your `BoundsExited` event fires when the player enters the BUG-001 pit — why doesn't Module 1 call that a bug? Where is the observation/judgment line and why draw it there? [§13, §11]
10. Why is position sampled at 5 Hz instead of every frame? What breaks at 60 Hz? What breaks at 0.5 Hz? [NFR-1.2 vs. detector resolution — a genuine trade-off with no perfect answer]
11. Which classes are not MonoBehaviours, and why was that a deliberate choice? [§9 — testability without a scene]
12. Why did the project switch from the Microgame to a custom GUT at SRS review, what did the switch buy, and what did it cost? [Change Log, §2.4 — know both columns of this trade, not just the favorable one]
13. What exactly does Module 2 receive from Module 1, and what stops Module 2 from bypassing the bus? [§15, NFR-1.3]
14. Defend the sentence: "Module 1 contains no AI, and is still the most important module of the project."
15. (v1.1) An examiner says: "You built the game AND the tester — of course your tool finds the bugs you planted. Isn't this circular?" Give the full answer: the threat is real (experimenter bias), name every mitigation, and explain what Module 7's foreign-game test proves that nothing in Module 1 can. [§2.4, R8, §15]

---

## 20. Deliverables

| # | Deliverable | Form |
|---|---|---|
| D1 | Unity 6.3 LTS project containing BenchGame — the custom GUT, complete per Section 1.1, in its own assembly | `UnityQA/` in repo |
| D2 | `Level_PlantedBugs_A` (six documented bugs) + `Level_Baseline` (clean control) | Scenes |
| D3 | Instrumentation layer: Core, Logging, UI, Adapters (~15 scripts, all commented per Rule 6) | `Assets/UnityQA/` |
| D4 | `DefaultQAConfig.asset` with documented defaults | Asset |
| D5 | Sample session folder from a real manual session (`session.json` + `events.jsonl`) | Committed under `reports/samples/` |
| D6 | `docs/BENCHMARK.md` v1 — the ground-truth answer key | Doc |
| D7 | `docs/EVENT-SCHEMA.md` v1 — event model reference (Module 6's input spec) | Doc |
| D8 | EditMode + PlayMode test suites, green in Test Runner | `Assets/Tests/` |
| D9 | Completed manual-test checklist (dated, results recorded) | Doc |
| D10 | `docs/MODULES.md` entry #1: what was built, decisions taken, what you learned | Doc |
| D11 | Merged PR `module/m1-instrumentation` → `main`, tag `v0.1.0` | Repo state |
| D12 | 30–60 s screen recording: session start → play → planted-bug encounter → session end → log file on disk | `docs/img/` (also your first README GIF) |
| D13 | (v1.1) `docs/GUT-SPEC.md` — BenchGame feature list, movement constants, derived kinematics with in-engine verification numbers | Doc |

---

## Approval Status

**v1.0 approved with changes on 19 Jul 2026** (change: custom GUT replaces the Microgame). **This document (v1.1) incorporates that change and is the implementation baseline.**

Implementation order for Module 1, per this SRS: **(1)** project + repo + asmdef scaffold → **(2)** BenchGame, script by script, ending with GUT-SPEC.md and a playable `Level_Baseline` (time-boxed per R1) → **(3)** instrumentation layer, script by script → **(4)** `Level_PlantedBugs_A` planting + BENCHMARK.md → **(5)** tests → **(6)** acceptance run (AC-1…AC-14). Each script is explained before and after it is written, per our rules, and we pause for your understanding at each step.

*Standing offer from v1.0 remains: if your college requires a formal IEEE-830-style SRS document for submission, say so and I'll maintain a .docx export alongside this repo Markdown.*
