# UnityQA — Scene & Asset Setup (canonical, as of M2 Slice B)

The one page that answers "what must exist for QA mode to work?". Later slices
append here; per-slice notes link here instead of repeating steps.

## One-time assets

1. **Config asset:** Project window → `Assets/UnityQA/Config/` (create the
   folder if absent) → right-click → **Create ▸ UnityQA ▸ Config** → keep the
   default name `DefaultQAConfig`. One asset serves every scene; per-scene
   overrides are possible but not the norm.

## Per-scene setup (any scene you want to instrument)

2. Hierarchy → right-click empty space → **Create Empty** (must be a ROOT
   object) → rename to `[QA]`. The bracket name is convention for "everything
   UnityQA owns lives under one visually obvious root" (SRS §7) — no code
   looks the name up.
3. With `[QA]` selected: **Add Component → QALogger**. Its
   `[RequireComponent]` auto-adds **QARunner** in the same click. (Adding
   QARunner first and QALogger second is equally fine.)
4. Drag `DefaultQAConfig` into QARunner's **Config** slot. QALogger needs no
   wiring — it finds its sibling QARunner in `Awake`.
5. *(Slice C)* Still on `[QA]`: **Add Component → BenchGameAdapter**, then
   **Add Component → QATelemetrySampler**. No wiring — the adapter finds the
   Player in the scene; the sampler finds its siblings. Component order tip:
   keep QALogger above the sampler in the inspector (add it first) so
   persistence subscribes to the bus before the sampler does.
6. *(Slice D)* Still on `[QA]`: **Add Component → QAInputRecorder** (below the
   adapter, same ordering logic). The Player needs nothing added — the
   controller auto-attaches its KeyboardInputSource on first Play.
7. *(M3 Slice A)* Still on `[QA]`: **Add Component → ReplayRecorder**. No
   wiring — it binds to the player's input source at session start. Every
   session now also writes `replay.json` into its session folder.
8. *(M3 Slice B, optional dev tool)* **Add Component → ReplayPlayer** to
   `[QA]` when you want playback. Inspector: *Replay File* (leave EMPTY to
   auto-play the most recent session's replay; or paste a session folder
   name), *Auto Play*, *Loop*. Demo loop: F9 → play a bit → F9 → tick Auto
   Play → restart Play mode → hands off the keyboard — the character re-runs
   your session. Right-click the component header for Play/Stop menu items.
9. *(M3 Slice C, optional dev tool)* **Add Component → ReplayValidator** on
   `[QA]` (requires ReplayPlayer — auto-added if absent). Leave *Original
   Session Folder* empty to validate the newest session with a replay.
   Workflow: record a session (F9…F9) → right-click ReplayValidator →
   **Validate Replay** → watch the Console verdict → `validation.json`
   appears in the new validation session's folder. Requires QALogger and
   QATelemetrySampler on `[QA]` (steps 3/5) — validation compares telemetry,
   so telemetry must be recording.
10. *(M3 Slice D, optional dev tool)* **Add Component → ReplayManager** on
    `[QA]` (requires ReplayPlayer — auto-added if absent). Right-click the
    component header for: **Refresh Catalog** (works even in Edit Mode —
    logs an indexed summary of every session and writes `catalog.json`),
    **Play Newest Replay**, **Validate Newest Replay** (needs ReplayValidator,
    step 9). This is the front door to the whole replay system.
11. Save the scene.

## Expected `Level_Baseline` hierarchy after setup

```
Level_Baseline
├── Main Camera        (Camera, AudioListener, FollowCamera)
├── Player             (SpriteRenderer, Rigidbody2D, BoxCollider2D,
│   └── GroundCheck     PlayerController2D)
├── Grid
│   └── Ground_Tilemap (Tilemap, TilemapRenderer, TilemapCollider2D,
│                       Rigidbody2D static, CompositeCollider2D — layer Ground)
└── [QA]               (QARunner + QALogger)          ← this doc's subject
```

## Verify (60 seconds)

Play → **F9** → Console shows `Session <uuid> started — writing to <path>`;
jump once; **F9** → right-click QALogger's component header → **Open Sessions
Folder** → newest folder contains `session.json` (status `"closed"`) and
`events.jsonl` (header line first, `SessionEnded` last).

## Known pitfalls

- **`[QA]` missing after a scene rebuild:** the recovery tool
  *BenchGame ▸ Rebuild Level_Baseline From Scratch* recreates only the GUT
  objects (Camera/Player/Grid) — it predates the framework and deletes `[QA]`
  without restoring it. After any rebuild, redo steps 2–4. (Registered as a
  tooling gap; the rebuild tool learns to preserve/recreate `[QA]` in the next
  slice that touches editor tooling.)
- **No Config assigned:** QARunner logs one explanatory error and disables
  itself — intended behavior (validation gate A-6), not a crash.
- **F9 does nothing:** check the key wasn't rebound in `DefaultQAConfig`
  (startStopKey) and that the Game view has focus.
