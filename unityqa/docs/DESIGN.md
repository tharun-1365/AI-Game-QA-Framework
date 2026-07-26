# UnityQA — AI-Powered Automated Game QA Framework
## Project Design Document v1.0

**Author:** Khanna &nbsp;|&nbsp; **Supervisor role:** Senior Game AI Engineer (Claude) &nbsp;|&nbsp; **Date:** 17 July 2026
**Status:** DRAFT — awaiting approval before any code is written

---

## 0. Supervisor's Reality Check (read this first)

You gave me four constraints: **2–3 months**, **beginner in Unity and C#**, **2D platformer**, **heuristics first, ML later**. A good supervisor tells you the truth about what that means, so here it is.

The full wishlist (autonomous exploration, soft-lock detection, unreachable areas, missing triggers, navigation problems, recording, replay, bug reports, dashboard) is roughly a 6–9 month project for someone already fluent in Unity. In 2–3 months as a beginner, we can build a *complete, honest, demonstrable* version of it if we make three strategic cuts:

1. **We do not build a game.** We adopt Unity's official **2D Platformer Microgame** as the "game under test" and deliberately plant bugs into it. Building QA tooling is the project; building a game is not. This also gives the project scientific structure: a *known set of planted bugs* becomes your ground-truth benchmark, and "how many planted bugs did UnityQA find?" becomes your evaluation metric — which is exactly what makes this publishable later.

2. **The HTML bug report *is* the dashboard (v1).** A separate live web dashboard is a stretch goal (Module 9), not core scope. A single self-contained HTML report with screenshots, a coverage heatmap, and a bug table is professional, demoable, and achievable.

3. **Replay is "evidence replay," not frame-perfect replay.** Deterministic input replay in Unity is a known hard problem (physics is not bit-deterministic across runs). Core scope is: full event timeline + automatic screenshots + agent state snapshots + *best-effort* input replay. This is what most industry tools actually ship, and it is defensible in a viva.

Everything else in your wishlist survives. The IEEE goal is realistic as a **conference/student-track paper written after the core build**, using the planted-bug benchmark as the experiments section. I'll flag paper-relevant framing throughout with the tag **[paper]**.

---

## 1. Overall Architecture

UnityQA is a **layered framework that sits inside a Unity project and tests the game from the outside-in**, the way a human QA tester would: it looks at the world, presses buttons, notices when something is wrong, and writes it down.

```
┌─────────────────────────────────────────────────────────────┐
│                    REPORTING LAYER                           │
│   BugReport model → JSON store → HTML report generator      │
│   (coverage heatmap, screenshots, bug table, run summary)   │
└──────────────────────────▲──────────────────────────────────┘
                           │ bug records + evidence
┌──────────────────────────┴──────────────────────────────────┐
│                    RECORDING LAYER                           │
│   Event timeline (JSONL) · screenshot capture ·             │
│   state snapshots · best-effort input replay                │
└──────────────────────────▲──────────────────────────────────┘
                           │ events
┌──────────────────────────┴──────────────────────────────────┐
│                    DETECTION LAYER                           │
│   Detector framework (plugin-style) with detectors:         │
│   OutOfBounds · StuckSoftLock · UnreachableArea ·           │
│   MissingTrigger · (extensible)                             │
└──────────────────────────▲──────────────────────────────────┘
                           │ observations
┌──────────────────────────┴──────────────────────────────────┐
│                    AGENT LAYER (the "AI")                    │
│   Perception (coverage grid, raycasts) →                    │
│   Exploration policy (frontier-based FSM) →                 │
│   Action primitives (walk, jump, jump-across) →             │
│   Virtual input injection                                   │
└──────────────────────────▲──────────────────────────────────┘
                           │ reads state / injects input
┌──────────────────────────┴──────────────────────────────────┐
│                 GAME UNDER TEST (GUT)                        │
│   Unity 2D Platformer Microgame + planted-bug test levels   │
│   + thin instrumentation (trigger registry, level bounds)   │
└─────────────────────────────────────────────────────────────┘
                           ▲
┌──────────────────────────┴──────────────────────────────────┐
│                    CORE / HARNESS                            │
│   QARunner (session lifecycle) · Config · Logger ·          │
│   Debug overlay · batch-mode entry point                    │
└─────────────────────────────────────────────────────────────┘
```

**Key design principles** (these are your viva answers when asked "why is it built this way?"):

- **Separation of tester and testee.** All UnityQA code lives in its own assembly (`UnityQA.*` namespaces) and touches the game only through a small, explicit interface (`IGameAdapter`). In theory the framework could be dropped into a different 2D platformer by writing a new adapter. You will be asked "is this general or game-specific?" — this is the answer.
- **Detectors are plugins.** Every detector implements one interface (`IBugDetector`) and is registered with the framework. Adding a detector never requires touching the agent or the reporter. This is the Open/Closed Principle in action — say that in the viva.
- **Everything observable is an event.** Agent actions, detector firings, game state changes all flow through one `EventBus` as timestamped events. Recording, detection, and reporting all subscribe to the same stream. One concept, three consumers.
- **The AI is explainable.** Frontier-based exploration over a coverage grid can be drawn on screen with gizmos, explained in two sentences, and defended without hand-waving. This beats a black-box RL agent for a viva every single time.

---

## 2. Folder Structure

```
unityqa/                            ← Git repository root
├── README.md                       ← portfolio front page (badges, GIFs, architecture diagram)
├── LICENSE                         ← MIT
├── .gitignore                      ← Unity-specific (from github/gitignore)
├── .gitattributes                  ← Git LFS rules for binaries
├── docs/
│   ├── DESIGN.md                   ← this document, versioned
│   ├── MODULES.md                  ← per-module log: what/why/learned (viva gold)
│   ├── BUG-TAXONOMY.md             ← definitions of every bug class we detect
│   ├── BENCHMARK.md                ← the planted-bug list = ground truth  [paper]
│   └── img/                        ← diagrams, GIFs for README
├── reports/                        ← sample generated reports (small ones, committed)
└── UnityQA/                        ← the Unity project (Unity 6.3 LTS)
    ├── Assets/
    │   ├── UnityQA/                ← THE FRAMEWORK (all our code)
    │   │   ├── Core/               ← QARunner, Config, EventBus, Logger, DebugOverlay
    │   │   ├── Agent/              ← perception, action primitives, input injection
    │   │   ├── Exploration/        ← coverage grid, frontier policy, FSM
    │   │   ├── Detection/          ← IBugDetector + all detectors
    │   │   ├── Recording/          ← timeline writer, screenshotter, snapshots, replayer
    │   │   ├── Reporting/          ← BugReport model, JSON store, HTML generator
    │   │   ├── Adapters/           ← IGameAdapter + MicrogameAdapter
    │   │   └── UnityQA.asmdef      ← assembly definition (isolates framework code)
    │   ├── TestGame/               ← the Microgame + our planted-bug test levels
    │   │   └── Levels/             ← Level_Baseline, Level_PlantedBugs_A, _B ...
    │   └── Tests/
    │       ├── EditMode/           ← pure-C# unit tests (grid math, report model…)
    │       └── PlayMode/           ← in-engine tests (agent walks, detector fires…)
    ├── Packages/manifest.json
    └── ProjectSettings/
```

Why this shape: the `asmdef` file makes UnityQA a real, separately-compiled library (professional practice, faster compiles, clean dependency story). `docs/MODULES.md` is your secret weapon — after every module you write half a page on what you built and what you learned, and by viva day your presentation writes itself.

---

## 3. Technology Stack

| Concern | Choice | Why |
|---|---|---|
| Engine | **Unity 6.3 LTS** (6000.3.x) | Current long-term-support release; safest for a 3-month project; what recruiters expect on a CV in 2026 |
| Render pipeline | URP 2D (comes with the Microgame) | Default, zero extra work |
| Language | C# (as shipped with Unity 6.3) | Only option that matters here |
| Game under test | **Unity 2D Platformer Microgame** | Official, free, tiny, well-made; we modify copies of its levels to plant bugs |
| Input injection | Direct control of the player controller via an input-abstraction seam | Simpler and more reliable for a beginner than synthesizing OS-level/InputSystem events; documented honestly as a design decision |
| JSON | Newtonsoft Json.NET (`com.unity.nuget.newtonsoft-json`) | `JsonUtility` is too limited (no dictionaries); Newtonsoft is the de-facto standard |
| Testing | Unity Test Framework (EditMode + PlayMode) | Built in; gives the project engineering credibility |
| Reports | Self-contained HTML generated from a C# template (inline CSS/JS, base64 screenshots) | One file → email-able, demo-able, no server |
| VCS | Git + GitHub, **Git LFS** for binaries | Non-negotiable for Unity |
| CI (stretch) | GitHub Actions running EditMode tests | Nice badge for the README; not core scope |
| ML (stretch, M9) | Unity ML-Agents | Only if we're ahead of schedule; PPO explorer vs. frontier explorer comparison **[paper]** |

Deliberately **excluded** from core scope: computer-vision-based bug detection, OS-level input synthesis, cloud anything, multiplayer, 3D.

---

## 4. AI Techniques (and how to talk about them)

The word "AI" in this project means **autonomous decision-making under uncertainty**, not necessarily machine learning. This is a legitimate and defensible position — most shipped game bots are exactly this. Techniques, in order of appearance:

1. **Finite State Machine (FSM)** — the agent's brain: `SelectingTarget → NavigatingToTarget → Stuck? → Recovering → …` Classic game-AI technique, easy to draw on a whiteboard.
2. **Occupancy / coverage grid mapping** — the level is discretized into cells (~0.5 units); the agent marks cells as *visited*, *seen-but-unvisited*, or *unknown*. Borrowed from robotics (SLAM literature) — say so, it sounds great and it's true.
3. **Frontier-based exploration** (Yamauchi, 1997 — cite it **[paper]**) — the agent always moves toward the nearest boundary between explored and unexplored space. This single idea gives you "autonomous exploration" with about 200 lines of code.
4. **Kinematic jump-arc sampling for reachability** — from the player's jump velocity and gravity, sample the parabola to decide which cells are *theoretically reachable*. The set difference `theoreticallyReachable − actuallyReached` is the **unreachable-area detector**. This is the most novel-feeling part of the core project **[paper]**.
5. **Statistical anomaly heuristics for detection** — e.g. soft-lock = "position variance below ε for T seconds while the agent is still issuing movement commands." Simple, tunable, explainable.
6. **Coverage-guided testing** — framing borrowed from fuzzing literature: the agent maximizes coverage because bugs live in unexplored corners **[paper — this is your related-work bridge to software-engineering venues]**.
7. *(Stretch, M9)* **Reinforcement learning (PPO via ML-Agents)** — a curiosity-rewarded explorer, evaluated head-to-head against the frontier explorer on the planted-bug benchmark. If it happens, it's the paper's headline experiment; if not, the paper still stands on 1–6.

---

## 5. Git Workflow

- **Repo:** GitHub, public from day one (portfolio pressure is good pressure). Name suggestion: `unityqa`.
- **Branches:** `main` is always demoable. Each module gets a branch: `module/m3-exploration`. Merge to `main` via a Pull Request **to yourself** — write a real PR description (what/why/how tested). Recruiters read PRs; examiners love them.
- **Tags:** after every module merge, tag it: `v0.3-exploration`. Your project history becomes a timeline of working milestones.
- **Commits:** conventional style — `feat(agent): add jump-across primitive`, `fix(detector): debounce stuck detection`, `docs(m2): module log`.
- **Unity specifics:** the standard Unity `.gitignore` (never commit `Library/`), **Git LFS from the very first commit** (retro-fitting LFS is painful), and *Force Text* serialization + *Visible Meta Files* in project settings (I'll walk you through this in M0).
- **Cadence:** commit at least at every "it works" moment. Small commits are a beginner's best friend — they make every mistake reversible.

---

## 6. Module Breakdown, Roadmap & Timeline

Twelve weeks, ten modules (M0–M9), one built-in buffer week. Dependency chain:

```
M0 ─► M1 ─► M2 ─► M3 ─► M4 ─► M5 ─► M7 ─► M8 ─► (M9 stretch)
                   │                 ▲
                   └───► M6 ────────┘
```

M6 (Recording) depends only on M3's event stream, so if a detector module runs long, M6 can start in parallel — that's our schedule slack. M5 and M6 feed M7 (reports need both bugs and evidence).

---

### M0 — Foundations & Toolchain (Weeks 1–2)

The only module with no framework code. Its job is to turn "beginner in both" into "dangerous enough to proceed."

- **Objective:** working toolchain, working game, working repo, working C# fundamentals.
- **Tasks:** install Unity Hub + Unity 6.3 LTS; import and *play* the 2D Platformer Microgame; complete a focused C# crash course (see learning list); set up the Git repo with LFS and correct Unity settings; make the player double-jump by editing one script (rite of passage).
- **What you'll SEE:** the Microgame running in Play mode; your first commits on GitHub; the player double-jumping because *you* changed the code.
- **Learn before/during:** C# — classes, interfaces, properties, events/delegates, generics (just `List<T>`/`Dictionary<K,V>`), coroutines. Unity — editor layout, GameObjects & components, MonoBehaviour lifecycle (`Awake/Start/Update/FixedUpdate`), prefabs, scenes, the Console. Git — clone/branch/commit/push/PR, what LFS is.
- **Risks:** *Tutorial hell* (mitigation: 2-week hard cap, learn the rest on the job); Unity version confusion (mitigation: 6.3 LTS only, decided); underpowered laptop (mitigation: the Microgame is tiny; we find out in week 1, not week 8).
- **Success criteria:** you can explain the MonoBehaviour lifecycle unprompted; the Microgame runs; repo exists with LFS and correct `.gitignore`; the double-jump edit is committed via a PR.
- **Deliverables:** repo scaffold, `docs/MODULES.md` entry #0.

### M1 — Test Bed & Harness Bootstrap (Weeks 2–3)

- **Objective:** a controlled arena with **known, planted bugs**, plus the skeleton the whole framework hangs on.
- **Tasks:** duplicate a Microgame level into `Level_PlantedBugs_A`; plant 5–6 bugs *deliberately* — a pit with no death trigger (fall-out-of-world), a room you can enter but not exit (soft lock), a platform just out of jump reach (unreachable), a checkpoint trigger that was never wired up (missing trigger), a collider gap in the floor. Document each in `docs/BENCHMARK.md` with an ID (`BUG-001`…) **[paper — this file becomes Table 1 of your experiments]**. Build `QARunner` (starts/stops a QA session), `QAConfig` (ScriptableObject), `EventBus`, `QALogger`, and a minimal on-screen debug overlay.
- **What you'll SEE:** a "QA Mode" you can toggle; an overlay showing session time and event count; log lines streaming as you *manually* walk the level and fall into your own planted pit.
- **Learn before:** ScriptableObjects; Unity UI basics (one Canvas, one text label); C# events vs. Unity events; singletons and why we'll use exactly one, carefully.
- **Risks:** over-engineering the EventBus (mitigation: it's a `List<Action<QAEvent>>` with a `Publish` method — 40 lines, no more); planting bugs that are too easy/too hard to find (mitigation: we'll calibrate together before you build them).
- **Success criteria:** all planted bugs verified *by you manually*, documented in BENCHMARK.md; events visibly flowing in the overlay; EditMode test for the EventBus passes.
- **Deliverables:** `Level_PlantedBugs_A`, BENCHMARK.md v1, Core/ skeleton, tag `v0.1`.

### M2 — Agent Body: Programmatic Control (Weeks 3–4)

- **Objective:** the bot can play the game — walk, jump, and chain moves — with no human touching the keyboard.
- **Tasks:** create the input-abstraction seam (`IVirtualInput`) so the player controller reads from our agent instead of the keyboard when QA mode is on; implement **action primitives**: `WalkTo(x)`, `Jump()`, `JumpAcross(gap)`, `WaitUntilGrounded()` as coroutine-based actions with success/failure/timeout results.
- **What you'll SEE:** the money moment of the early project — you press Play, sit back, and the character walks and jumps *by itself*. Record this GIF; it goes in the README.
- **Learn before:** how the Microgame's player controller reads input (we read its code together — first real code-reading exercise); Rigidbody2D and 2D physics basics; coroutines in anger; `Time.deltaTime` vs `FixedUpdate`.
- **Risks:** **the highest-risk module for a beginner** — hooking into someone else's controller code is fiddly (mitigation: the Microgame controller is small and readable, and this is where I earn my keep as supervisor); primitives that work on flat ground but fail on slopes/edges (mitigation: primitives return failure results instead of hanging — failure-tolerance is built into the design).
- **Success criteria:** a scripted sequence (walk right → jump gap → walk → jump onto platform) completes hands-free on the baseline level, 5/5 runs; every primitive has a timeout; PlayMode test proves `WalkTo` works.
- **Deliverables:** Agent/ assembly, demo GIF, tag `v0.2`.

### M3 — Exploration Engine (Weeks 5–6)

- **Objective:** the agent explores a level it has never seen, autonomously, and we can *watch it think*.
- **Tasks:** coverage grid over the level bounds (visited / seen / unknown per cell); frontier detection (visited cells adjacent to unknown); target selection (nearest reachable frontier); the FSM that loops *select → navigate via M2 primitives → mark coverage → repeat*; recovery behavior when navigation fails; gizmo visualization of the whole thing; coverage-percentage metric.
- **What you'll SEE:** the flagship demo — the Scene view painting green (visited) and grey (frontier) cells in real time as the agent sweeps the level. This GIF is your README header and your viva opener.
- **Learn before:** 2D arrays and grid↔world math; queues and BFS (frontier finding *is* BFS); Unity gizmos (`OnDrawGizmos`); reading a paper — I'll give you Yamauchi (1997), 4 pages, your first academic read **[paper]**.
- **Risks:** agent oscillates between two frontiers (mitigation: commit-to-target hysteresis); can't reach a frontier and retries forever (mitigation: 3-strikes blacklist per target); tuning eats the schedule (mitigation: time-boxed — at 80% coverage on the baseline level we ship and move on. 80% is a *result*, not a failure).
- **Success criteria:** ≥80% of reachable cells covered on the baseline level with zero human input, 3/3 runs; live gizmo view works; coverage % printed at session end.
- **Deliverables:** Exploration/ assembly, flagship GIF, tag `v0.3`. **Milestone: this is officially an "AI that explores game levels."**

### M4 — Detection Framework + Detector Pack 1 (Week 7)

- **Objective:** the framework notices things going wrong, through a plugin architecture.
- **Tasks:** `IBugDetector` interface (`OnTick`, `OnEvent`, emits `BugReport`); detector registry in QARunner; `BugReport` model (ID, type, severity, position, timestamp, description, evidence refs); **OutOfBoundsDetector** (player below/outside level bounds → real fall-through bug vs. registered death zone); **StuckDetector** (movement commanded, position variance < ε for T seconds → soft lock; must NOT fire during legitimate idling — this debounce discussion is a great viva topic).
- **What you'll SEE:** the agent falls into your planted pit from M1 and a red **BUG DETECTED: OutOfBounds (BUG-001)** flashes on the overlay, live. First closed loop: plant → explore → detect.
- **Learn before:** interfaces & polymorphism in practice; a first taste of precision/recall thinking — false positives are the enemy of trust in QA tools **[paper — this becomes your evaluation vocabulary]**.
- **Risks:** StuckDetector false positives (mitigation: tune on the baseline level where *zero* detections is the target — that's your false-positive test); detectors tangled into agent code (mitigation: detectors may only consume events and observations, never call the agent — enforced by the asmdef dependency direction).
- **Success criteria:** both planted bugs from these classes detected on Level_A; **zero false positives** on the baseline level across 3 full exploration runs; adding a dummy detector requires no changes outside Detection/.
- **Deliverables:** Detection/ framework + 2 detectors, tag `v0.4`.

### M5 — Detector Pack 2: Reachability & Triggers (Week 8)

- **Objective:** the two "smart" detectors that make examiners sit up.
- **Tasks:** **UnreachableAreaDetector** — static analysis pass: from level geometry + jump-arc sampling (Section 4.4), compute *theoretically reachable* cells; after exploration, diff against *actually reached*; flag clusters (your planted too-high platform gets caught here). **MissingTriggerDetector** — `QAExpectedTrigger` marker component on things that *should* fire (checkpoints, doors, pickups); detector cross-references expected vs. actually-fired at session end (your planted unwired checkpoint gets caught here). Render the coverage heatmap texture (feeds M7).
- **What you'll SEE:** a heatmap of the level — green = reached, red = theoretically reachable but never reached, with your planted unreachable platform glowing red; plus a trigger checklist with one damning ✗.
- **Learn before:** projectile-motion math (12th-grade physics, back for revenge); reading collider/tilemap geometry from code; set operations on grids.
- **Risks:** **the most algorithmically ambitious module.** Jump-arc reachability can rabbit-hole (mitigation: sampled approximation from a coarse set of launch cells, not exact analysis — approximate with honest error discussion beats exact-but-never-finished, and the discussion itself is viva/paper material); tilemap geometry extraction fiddlier than expected (mitigation: fallback = hand-authored walkable-region annotation on test levels, documented as a limitation).
- **Success criteria:** planted unreachable platform flagged; planted missing trigger flagged; heatmap renders; false-positive rate on baseline level documented (target < 3 flagged clusters).
- **Deliverables:** 2 smart detectors + heatmap, BUG-TAXONOMY.md complete, tag `v0.5`.

### M6 — Recording & Evidence (Week 9, parallelizable after M3)

- **Objective:** every bug comes with proof.
- **Tasks:** `TimelineRecorder` — every event to a JSONL file per session (append-only, crash-safe); `Screenshotter` — automatic capture on every bug detection + periodic captures, saved beside the timeline; `StateSnapshot` — player position/velocity/FSM state ring buffer, so each bug report carries the *last 10 seconds of context*; **best-effort input replay** — re-inject the recorded primitive sequence, honestly documented: "reproduces the path; physics divergence possible" (the honest limitation section every good paper has **[paper]**).
- **What you'll SEE:** a session folder appearing on disk — timeline, screenshots, snapshots; open the JSONL and scroll through the agent's entire life story; watch a replay retrace the agent's route to a bug.
- **Learn before:** file I/O in C# (`StreamWriter`, paths, `Application.persistentDataPath`); `ScreenCapture` API; JSONL as a format and why append-only matters; ring buffers.
- **Risks:** screenshot capture stalling the frame (mitigation: capture on bug events only + low periodic rate, not every frame); replay divergence disappointing you (mitigation: expectations set *now*, in this document — it's "evidence replay," and the divergence discussion is a feature of your writeup, not a bug in your project).
- **Success criteria:** a full exploration session produces a complete, well-formed evidence folder; every BugReport references ≥1 screenshot + a snapshot window; replay retraces the route on the baseline level.
- **Deliverables:** Recording/ assembly, sample session folder committed, tag `v0.6`.

### M7 — Bug Report Generator (Week 10)

- **Objective:** output a report a real studio would not be embarrassed by.
- **Tasks:** JSON session store (machine-readable — the "API" for any future dashboard); HTML generator from a C# template → **one self-contained file**: run summary (level, duration, coverage %, bug count by severity), coverage heatmap image, bug table (ID, type, severity, position, time, description), and per-bug detail cards with embedded screenshots + "how to reproduce" (position + FSM context + timeline slice); benchmark scoring — the report **scores itself against BENCHMARK.md**: *planted bugs found: 5/6, detection rate 83%, false positives: 1* **[paper — this table IS your results section]**.
- **What you'll SEE:** double-click a file, and a professional QA report opens in your browser — the artifact you'll show at the viva, in interviews, and to your parents.
- **Learn before:** string templating in C#; just enough HTML/CSS (I'll provide the skeleton — this project does not require you to become a front-end developer); base64 image embedding.
- **Risks:** front-end perfectionism (mitigation: hard 1-week box; clean beats beautiful); giant HTML from too many screenshots (mitigation: JPEG-compress, cap per-bug images).
- **Success criteria:** one command/menu-click produces the HTML from a session folder; opens correctly in a browser with zero external dependencies; benchmark score table correct against hand-checked ground truth.
- **Deliverables:** Reporting/ assembly, a committed sample report, tag `v0.7`.

### M8 — Integration, Batch Runs & Hardening (Week 11)

- **Objective:** turn modules into a *system*; generate the data for your final writeup.
- **Tasks:** one-command pipeline (menu item / CLI batch mode: load level → explore → detect → record → report, unattended); build `Level_PlantedBugs_B` — a *fresh* level with fresh planted bugs the detectors were never tuned on (**your held-out test set [paper]** — this is the difference between "it works on my level" and *evidence*); run the full benchmark N=5 times per level, collect detection rates; fix what the runs expose; README overhaul with GIFs, architecture diagram, results table.
- **What you'll SEE:** UnityQA running unattended through both levels and producing reports while you literally make tea. Also: your GitHub repo suddenly looking like a real project.
- **Learn before:** Unity batch mode & command-line args; basic experiment hygiene — repeated runs, variance, held-out sets **[paper]**.
- **Risks:** integration surfacing cross-module bugs (mitigation: this week exists precisely to absorb them — it *is* the buffer); Level_B revealing over-fit detectors (mitigation: that's not a risk, that's a *finding* — it goes in the report).
- **Success criteria:** full unattended pipeline works on both levels; results table (detection rate, false positives, coverage %, run time per level) complete; README done; demo video (~2 min) recorded.
- **Deliverables:** `v1.0` tag. **This is the viva-ready, portfolio-ready state.** Everything after is bonus.

### M9 — Stretch Goals (Week 12+ / post-submission, pick at most one)

- **Option A — Multi-run dashboard:** a static HTML dashboard aggregating all session JSONs — bug trends across runs, coverage over time. Cheapest, most demo-friendly.
- **Option B — ML-Agents RL explorer:** curiosity-rewarded PPO explorer; head-to-head vs. frontier explorer on the benchmark (coverage, bugs found, wall-clock) **[paper headline experiment if it happens]**. Most expensive, highest risk, highest reward.
- **Option C — Second-game generalization:** write a new `IGameAdapter` for a different free 2D platformer; report what transferred and what broke **[paper — generalization section]**.
- **Advice:** decide *after* M8, based on remaining time and energy. A is the safe pick; B only if you're ≥1 week ahead of schedule, which, let's be honest, you won't be. (Nobody ever is. That's not a you-problem.)

---

## 7. Timeline at a Glance

| Weeks | Module | Milestone |
|---|---|---|
| 1–2 | M0 Foundations | Toolchain + C#/Unity basics + repo |
| 2–3 | M1 Test bed & harness | Planted-bug level + event skeleton |
| 3–4 | M2 Agent body | Bot plays hands-free |
| 5–6 | M3 Exploration | **Autonomous exploration demo (flagship)** |
| 7 | M4 Detectors 1 | First live bug detection |
| 8 | M5 Detectors 2 | Reachability heatmap + trigger audit |
| 9 | M6 Recording | Evidence folders + replay |
| 10 | M7 Reports | Professional HTML report |
| 11 | M8 Integration | **v1.0 — viva-ready** |
| 12+ | M9 Stretch / buffer | Dashboard *or* ML *or* breathing room |

Built-in honesty: if any module slips a week (M2 or M5 are the likely culprits), M9 is the crumple zone and v1.0 still lands inside 12 weeks. If *two* modules slip, we cut MissingTriggerDetector from M5 and best-effort replay from M6 — pre-agreed now so we never have to make a panicked scope decision later.

---

## 8. Project-Level Risks (beyond per-module ones)

1. **The beginner cliff (biggest risk).** Weeks 1–4 will feel slow and occasionally demoralizing; the payoff curve is exponential, not linear. Mitigation: M0 is honest about being a learning module; M2's "it moves by itself!" moment is deliberately scheduled early as a morale checkpoint.
2. **Scope creep.** Every week you'll think of a cool new detector. Mitigation: a `FUTURE-IDEAS.md` file — every idea goes there instead of into the code. After v1.0, raid it.
3. **Losing the narrative.** A framework with no story is just files. Mitigation: `MODULES.md` after every module, GIFs at every milestone — the viva presentation assembles itself from these.
4. **Publication expectations.** A first-tier IEEE journal is not realistic on this timeline; an IEEE student/regional conference or a games-focused venue (e.g., IEEE CoG-style) with the benchmark results *is* a credible target after v1.0. We frame the paper after M8, from BENCHMARK.md + the results table.

---

## 9. What Approval Means

If you approve this design (or approve it with changes), Module 0 begins: I give you the exact install checklist, the C# crash-course plan tuned to this project (not generic tutorials), and the repo setup walkthrough. We write zero framework code until you've earned the fundamentals — exactly as agreed.

Questions I'd genuinely like your input on before we start:

1. Comfortable with the three strategic cuts in Section 0?
2. Happy with the Microgame as the game under test, or did you want to build even a tiny level from scratch for the experience?
3. Does the 12-week table fit your actual academic calendar (exam weeks, submission deadlines)? Tell me the real dates and I'll re-fit the plan around them.
