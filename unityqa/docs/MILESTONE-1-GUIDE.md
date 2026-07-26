# Module 1 — Milestone 1 Implementation Guide
## Project Scaffold + BenchGame Foundation

> **ADDENDUM (supersedes parts of this guide):** the project now ships as a
> complete, ready-to-open Unity project — Part C's manual assembly is **no
> longer required** (keep it as reading material: it explains what every
> generated piece is). Open the project per README Quick Start; geometry
> auto-paints on first load (Decision D-006). One correction to Part D:
> per Decision D-005, the gap rulers are **4 tiles (clears) and 6 tiles
> (fails)** — not 4/5 — because the player's collider width extends the
> effective jump range (see GUT-SPEC.md). Read VC-7 accordingly.

**Scope (per approval):** folder structure, assembly definitions, doc placeholders, and BenchGame's foundation only — player, camera, tilemap, ground collision, left/right movement, single fixed-height jump, follow camera. Nothing else. Everything on the DO-NOT-IMPLEMENT list is absent from this milestone by design; if you find any of it in the files, that's a defect — tell me.

**What you received:** the repo scaffold as real files (zip). Two C# scripts exist: `PlayerController2D.cs` and `FollowCamera.cs`. Both are reproduced in full in this guide with their explanations. Scenes, sprites, tile palettes, and project settings cannot be shipped as files — Unity generates them — so Part C walks you through assembling them by hand. That hand-assembly *is* the learning content of this milestone.

---

## Part A — The scaffold, and why it looks the way it does

### A1. Folder structure (SRS §6, Milestone-1 subset)

```
unityqa/                          ← repo root — git init happens here
├── README.md                     ← stub; grows per milestone
├── .gitignore                    ← Unity-specific; Library/ is never committed
├── .gitattributes                ← Git LFS rules; installed BEFORE first binary
├── docs/
│   ├── DESIGN.md                 ← approved project design
│   ├── SRS-Module1.md            ← approved SRS v1.1 (implementation baseline)
│   ├── MILESTONE-1-GUIDE.md      ← this file
│   ├── BENCHMARK.md              ← placeholder (bugs planted in a later milestone)
│   ├── EVENT-SCHEMA.md           ← placeholder (frozen when events are implemented)
│   ├── GUT-SPEC.md               ← PROVISIONAL movement constants — already filled in
│   ├── MODULES.md                ← build log + Decision Register (D-001..D-003 recorded)
│   └── img/
├── reports/samples/              ← empty until instrumentation exists
└── UnityQA/                      ← the Unity project (created by Unity Hub, Part C)
    └── Assets/
        ├── UnityQA/
        │   ├── UnityQA.asmdef        ← framework assembly (empty but real)
        │   └── Core/AssemblyAnchor.cs ← placeholder only — see D-003
        ├── TestGame/
        │   ├── BenchGame.asmdef      ← GUT assembly — references: NONE
        │   ├── Scripts/
        │   │   ├── PlayerController2D.cs
        │   │   └── FollowCamera.cs
        │   ├── Tiles/                ← palette + sprites created in-editor (C4)
        │   └── Levels/               ← Level_Baseline.unity created in-editor (C6)
        └── Tests/EditMode/, Tests/PlayMode/   ← empty; asmdefs arrive with first tests
```

### A2. The two assembly definitions — the architectural heart of this milestone

An **asmdef** makes Unity compile a folder into its own .NET assembly with an *explicit* reference list. Without asmdefs, all scripts land in one big assembly and "BenchGame must never reference UnityQA" (NFR-1.3) would be a promise; with asmdefs, it's a **compile error**. That's the difference between discipline and architecture.

Both asmdefs have an **empty `references` list** — neither assembly can see the other at all:

- `BenchGame.asmdef` — references: none. BenchGame is written "as if foreign" (SRS §1.1). If any BenchGame script tries `using UnityQA.Anything`, the compiler refuses. The rule enforces itself from today onward.
- `UnityQA.asmdef` — references: none. The framework must compile even if `TestGame/` is deleted (NFR-1.3).

**Where the adapter goes, then?** — recorded as **Decision D-001** in MODULES.md: `Adapters/` will get its *own* `UnityQA.Adapters.asmdef` referencing both, in the instrumentation milestone. The SRS §6 tree drew Adapters inside UnityQA/; the asmdef reality refines that without changing the architecture diagram (§5), which already showed the adapter as the bridge. This is exactly the kind of small, documented refinement the Decision Register exists for.

**`AssemblyAnchor.cs`** (Decision D-003): an asmdef containing zero scripts produces a persistent import warning. The anchor is one empty internal type that silences it. It contains no logic — it is scaffolding, not framework code — and is deleted the moment the first real Core script lands.

### A3. Doc placeholders

`BENCHMARK.md` (bug registry skeleton — six BUG IDs, all "not planted"), `EVENT-SCHEMA.md` (envelope + type list from SRS §14, marked unfrozen), `GUT-SPEC.md` (**not** a placeholder — the authored constants and derived kinematics are already filled in; only the AC-14 verification table is TBD), and `MODULES.md` (Decision Register D-001–D-003 + your M1.1 log entry with a "What I learned" section **you** must write — it's a deliverable).

---

## Part B — The scripts

### B1. `PlayerController2D.cs`

**1. Purpose.** Horizontal run at constant speed + single fixed-height jump + grounded check. The complete movement model of BenchGame.

**2. Why it exists.** BenchGame is apparatus (SRS §2.4). Module 4 will one day compute "which platforms are reachable?" from pure math — that only works if the movement model is simple enough to *have* clean math. So: velocity is **set**, not force-accumulated (instant, analyzable direction changes); jump **height** is the authored value and jump **velocity is derived** from it (v = √(2·g·h)), which means GUT-SPEC.md's headline number (apex = 2.2 u) is exact *by construction* rather than by tuning. This one design choice is why the unreachable-area detector will get exact ground truth.

**3. How it works — the three ideas you must be able to explain in a viva:**

- **Update samples, FixedUpdate applies.** `Update` runs once per rendered frame (machine-dependent); `FixedUpdate` runs once per physics step (0.02 s, machine-independent, FR-1.19). Input is *read* in Update and *applied* in FixedUpdate, so gameplay never depends on frame rate.
- **The latch.** A key press can land between two physics steps. `jumpRequested = true` in Update "latches" it; FixedUpdate consumes it and clears it *every step* — so a press is never lost, but also never stored until landing (a hidden jump buffer would violate §1.1 minimalism).
- **OverlapBox grounding.** A small box at the feet is tested against the `Ground` layer each physics step. A box (not a ray) tolerates tile seams and platform edges; making it slightly narrower than the player's collider stops wall-touches from counting as "grounded" (which would allow wall-jumping — a feature we did not order).

**4. Full code** — as shipped in `Assets/TestGame/Scripts/PlayerController2D.cs`:

```csharp
using UnityEngine;

namespace BenchGame
{
    /// <summary>
    /// BenchGame player movement: constant-speed run + single fixed-height jump.
    /// Movement constants are documented in docs/GUT-SPEC.md (FR-1.20); the
    /// inspector values here and that document must always agree.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class PlayerController2D : MonoBehaviour
    {
        [Header("Movement — GUT-SPEC.md is the authoritative record (FR-1.20)")]
        [Tooltip("Horizontal run speed in world units per second.")]
        [SerializeField] private float runSpeed = 6f;

        [Tooltip("Apex height of a jump in world units. Jump velocity is DERIVED " +
                 "from this via v = sqrt(2·g·h), so this value is exact by construction.")]
        [SerializeField] private float jumpHeight = 2.2f;

        [Header("Ground check")]
        [Tooltip("Empty child positioned at the player's feet.")]
        [SerializeField] private Transform groundCheck;

        [Tooltip("Size of the box tested for ground contact. Slightly narrower than " +
                 "the player collider so wall-touches don't count as 'grounded'.")]
        [SerializeField] private Vector2 groundCheckSize = new Vector2(0.55f, 0.10f);

        [Tooltip("Layers that count as ground. Set to the 'Ground' layer only.")]
        [SerializeField] private LayerMask groundLayer;

        private Rigidbody2D body;

        // Input state: written in Update, read in FixedUpdate. Single-threaded,
        // so no locking is needed — Unity calls both on the main thread.
        private float moveInput;      // -1, 0, or +1 (GetAxisRaw is unsmoothed — deterministic)
        private bool jumpRequested;   // latched on key-down, consumed by the next physics step

        private bool isGrounded;

        /// <summary>Current world-space velocity (read-only observation surface).</summary>
        public Vector2 Velocity => body.linearVelocity;

        /// <summary>True while the ground-check box overlaps the Ground layer.</summary>
        public bool IsGrounded => isGrounded;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
        }

        private void Update()
        {
            // GetAxisRaw, not GetAxis: raw returns exactly -1/0/+1 with no input
            // smoothing. Smoothing is feel-polish for real games; for a benchmark
            // it is a hidden, frame-rate-coupled state variable — so we refuse it.
            moveInput = Input.GetAxisRaw("Horizontal");

            // Latch, don't act: acting here would couple jumping to frame rate.
            if (Input.GetButtonDown("Jump"))
            {
                jumpRequested = true;
            }
        }

        private void FixedUpdate()
        {
            // Ground test: a small box at the feet against the Ground layer.
            // OverlapBox (not raycast) tolerates standing on tile seams and edges.
            isGrounded = Physics2D.OverlapBox(
                groundCheck.position, groundCheckSize, 0f, groundLayer) != null;

            Vector2 velocity = body.linearVelocity;

            // Horizontal: velocity is SET, not force-added. Constant speed with
            // instant direction change — trivially analyzable kinematics, which
            // is the whole point of BenchGame (SRS §2.4).
            velocity.x = moveInput * runSpeed;

            // Jump: only from the ground (single jump, SRS §1.1 feature list).
            if (jumpRequested && isGrounded)
            {
                // v = sqrt(2·g·h) — solve projectile apex for initial velocity.
                // g must include this body's gravityScale multiplier.
                float g = Mathf.Abs(Physics2D.gravity.y) * body.gravityScale;
                velocity.y = Mathf.Sqrt(2f * g * jumpHeight);
            }

            // Consume the latch every step: an airborne press should NOT be
            // stored and fired on landing (that would be a hidden jump buffer —
            // excluded by SRS §1.1's minimalism rule).
            jumpRequested = false;

            body.linearVelocity = velocity;
        }

        // Editor-only visualization of the ground-check box; compiled out of builds.
        private void OnDrawGizmosSelected()
        {
            if (groundCheck == null) return;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(groundCheck.position, groundCheckSize);
        }
    }
}
```

*(Unity 6 note: `Rigidbody2D.velocity` is deprecated; the current API is `linearVelocity`, which this script uses.)*

**5. Inspector setup** (on the Player object, Part C5): Run Speed `6` · Jump Height `2.2` · Ground Check → drag the `GroundCheck` child · Ground Check Size `(0.55, 0.10)` · Ground Layer → tick **Ground only**. If Ground Layer is left at Nothing, the player can never jump — that's the first thing to check when "jump doesn't work".

**6. Scene setup.** Lives on the `Player` GameObject; requires a Rigidbody2D (auto-added by `RequireComponent`) and expects a `GroundCheck` child at the feet. Full assembly in C5.

### B2. `FollowCamera.cs`

**1. Purpose.** Keep the player on screen with smoothed follow. Nothing else.

**2. Why it exists (and why not Cinemachine).** Rule 8: ~20 lines we fully understand beat a camera framework we'd use 2% of. Cinemachine earns its complexity with virtual cameras, blends, and confiners; BenchGame needs "follow one target."

**3. How it works.** `LateUpdate` (runs after all movement for the frame, so the camera sees the player's final position — following from `Update` gives one-frame-behind jitter) + `Vector3.SmoothDamp` (critically damped spring — eases smoothly, never overshoots) + Z preserved (an orthographic 2D camera must stay at its own Z, e.g. −10). Determinism note for the viva: the camera is *presentation-only* — nothing in gameplay or (future) detection reads its position — so its frame-rate-dependent easing does not violate FR-1.19, which governs the gameplay simulation only.

**4. Full code** — as shipped in `Assets/TestGame/Scripts/FollowCamera.cs`:

```csharp
using UnityEngine;

namespace BenchGame
{
    /// <summary>
    /// Minimal smoothed follow camera locked to the target's X/Y.
    /// The camera's own Z (e.g. -10) is preserved.
    /// </summary>
    public sealed class FollowCamera : MonoBehaviour
    {
        [Tooltip("What to follow — the Player's transform.")]
        [SerializeField] private Transform target;

        [Tooltip("Approximate seconds to catch up to the target. 0 = rigid lock.")]
        [SerializeField] private float smoothTime = 0.15f;

        [Tooltip("View offset from the target, e.g. (0, 1) to look slightly above the feet.")]
        [SerializeField] private Vector2 offset = new Vector2(0f, 1f);

        private Vector3 dampVelocity; // internal state for SmoothDamp — do not touch

        private void LateUpdate()
        {
            if (target == null) return; // fail quiet: a camera without a target just holds still

            Vector3 goal = new Vector3(
                target.position.x + offset.x,
                target.position.y + offset.y,
                transform.position.z); // never move in Z — orthographic 2D camera

            transform.position = Vector3.SmoothDamp(
                transform.position, goal, ref dampVelocity, smoothTime);
        }
    }
}
```

**5. Inspector setup** (on Main Camera): Target → drag `Player` · Smooth Time `0.15` · Offset `(0, 1)`.

**6. Scene setup.** Added to the existing Main Camera. Keep the camera at Z = −10, Projection Orthographic, Size ≈ 6.

---

## Part C — Assembling the project in Unity, step by step

### C1. Create the Unity project (one-time dance to merge Hub output with the scaffold)

1. Unzip the scaffold so you have a `unityqa/` folder.
2. Temporarily rename `unityqa/UnityQA` → `unityqa/_incoming` (Unity Hub refuses to create a project into a non-empty folder).
3. Unity Hub → **New project** → editor **6000.3.x LTS** → template **Universal 2D** → Project name `UnityQA` → Location: your `unityqa/` folder → Create.
4. Once the editor opens, **close it**. Merge the scaffold in: copy everything inside `_incoming/Assets/` into `unityqa/UnityQA/Assets/` (you'll end up with `Assets/UnityQA/`, `Assets/TestGame/`, `Assets/Tests/`). Delete `_incoming`.
5. Reopen the project. Unity imports the scripts and asmdefs; the Console must show **zero errors**. You will see the two assemblies compile separately — that's the asmdefs working.

### C2. Project settings (5 minutes, do them now, in this order)

1. **Edit → Project Settings → Player → Other Settings → Active Input Handling → Both.** (Unity 6 templates default to the new Input System only; BenchGame uses the legacy API per Decision D-002 — without this, `Input.GetAxisRaw` throws.) Unity restarts.
2. **Edit → Project Settings → Version Control:** Mode = Visible Meta Files (usually default). **Editor → Asset Serialization → Force Text** (usually default — verify, don't assume). These make scenes diffable text, which our whole Git story depends on.
3. **Verify, change nothing:** Time → Fixed Timestep `0.02`; Physics 2D → Gravity Y `−9.81`. These are FR-1.19/GUT-SPEC constants — we *rely* on defaults rather than customizing (fewer things to document, fewer things to break).
4. **Layers:** Project Settings → Tags and Layers → add a layer named exactly `Ground` in the first empty User Layer slot.

### C3. Create the placeholder sprite

Project window → `Assets/TestGame/Tiles/` → right-click → **Create → 2D → Sprites → Square**. Name it `SquareSprite`. This 1×1-unit white square is the entire art budget of BenchGame (risk R1: placeholder art only). White tints freely, so tiles and player can be distinct colors from one sprite.

### C4. Build the tilemap (the level's ground)

1. Hierarchy → right-click → **2D Object → Tilemap → Rectangular**. This creates `Grid` with a child `Tilemap`. Rename the child `Ground_Tilemap`.
2. **Window → 2D → Tile Palette** → Create New Palette, name `BenchPalette`, save into `Assets/TestGame/Tiles/`. Drag `SquareSprite` into the palette window → it creates a Tile asset (save as `GroundTile` in the same folder). Select the tile in the palette, and in its inspector tint it a solid color (e.g. dark green) so ground reads as ground.
3. Paint with the brush tool: a flat floor about **40 tiles wide** at y = 0. Then add this *verification geometry* (it exists to test GUT-SPEC numbers, and doubles as level structure later): a **2-tile-high step** (jumpable — apex is 2.2), a **3-tile-high wall** (must NOT be jumpable), a **4-tile-wide gap** with floor 2 tiles below (clearable at full run speed — max distance ≈ 4.64), and a **5-tile-wide gap** the same way (must NOT be clearable; put floor beneath so falling in isn't fatal — there are no kill zones yet, so give it a 2-tile stair to climb back out).
4. Make the tilemap solid: select `Ground_Tilemap` → Add Component → **Tilemap Collider 2D** → set its **Composite Operation = Merge** → Add Component → **Composite Collider 2D** (this auto-adds a Rigidbody2D — set its **Body Type = Static**). *Why the composite:* per-tile box colliders create seams; a moving box collider can snag on them ("ghost collisions"). Merging into one composite outline eliminates the seams. This is the single most common 2D platformer construction bug, now permanently avoided.
5. Set the `Ground_Tilemap` GameObject's **Layer = Ground** (top-right of inspector).

### C5. Build the Player

1. Hierarchy → Create Empty → name `Player`, position `(2, 2, 0)` (above the floor).
2. Add **Sprite Renderer** → Sprite = `SquareSprite`, Color = something loud (orange). Scale stays (1,1,1).
3. Add **Rigidbody 2D**: Body Type `Dynamic` · **Gravity Scale `3`** (GUT-SPEC constant — snappy platformer gravity instead of floaty default) · Collision Detection **Continuous** (a fast-falling box must not tunnel through a 1-tile-thin floor — discrete detection can skip past it in one step) · Interpolate **Interpolate** (physics runs at 50 Hz, rendering faster; interpolation smooths the visual without touching the simulation — FR-1.19 intact) · Constraints → **Freeze Rotation Z** (our player is a box, not a tumbling die).
4. Add **Box Collider 2D**: Size `(0.9, 0.9)` (GUT-SPEC). Slightly smaller than the sprite so the player doesn't visually overlap walls it touches, and fits 1-tile openings without pixel-perfect alignment.
5. Create empty child of Player → name `GroundCheck` → local position `(0, -0.45)` (at the collider's bottom edge).
6. Add **PlayerController2D** (it auto-required the Rigidbody2D) and fill the inspector per B1.5. Player's own layer stays `Default` — if you put the Player on the Ground layer, the ground check detects the player's *own* collider and grounding is permanently true (infinite air-jumps: a bug we'd rather not plant by accident).

### C6. Camera + save

Main Camera: confirm Projection Orthographic, Size `6`, position Z `−10`; set a neutral background color (Solid Color). Add **FollowCamera** per B2.5. Then **File → Save As** → `Assets/TestGame/Levels/Level_Baseline.unity`. Delete/ignore the template's default sample scene.

### C7. Git (repo goes live at the end of the milestone, per the SRS Git workflow)

```
cd unityqa
git init
git lfs install
git add .
git commit -m "feat(m1): milestone 1 — project scaffold + BenchGame foundation

Repo structure per SRS-M1 v1.1 §6. UnityQA + BenchGame asmdefs with zero
cross-references (NFR-1.3). Doc placeholders + provisional GUT-SPEC.
PlayerController2D (run + derived-velocity fixed jump), FollowCamera.
Level_Baseline with kinematics-verification geometry. Decisions D-001..D-003."
git branch -M main
```

Then create the GitHub repo (`unityqa`, public), `git remote add origin …`, `git push -u origin main`. Subsequent Module 1 milestones happen on `module/m1-instrumentation` and merge back by PR at module end (tag `v0.1.0`), per the SRS.

---

## Part D — Verification checklist (do all of these; record results in MODULES.md)

| # | Check | Pass looks like |
|---|---|---|
| VC-1 | Open project, Console clear | Zero errors, zero warnings from our files (the AssemblyAnchor prevents the empty-assembly warning) |
| VC-2 | Assembly isolation | Select each `.asmdef` in the Project window → References list is empty in both |
| VC-3 | Play → A/D or ←/→ | Player runs left/right at constant speed; instant direction change; no sliding after release |
| VC-4 | Space on ground | One jump; apex visibly ~just over 2 tiles |
| VC-5 | Space in mid-air | Nothing happens (no double jump, no stored jump on landing) |
| VC-6 | **The 2/3 ruler** | Player clears the 2-tile step; cannot clear the 3-tile wall no matter what |
| VC-7 | **The 4/5 ruler** | At full run speed, clears the 4-tile gap; falls into the 5-tile gap every time |
| VC-8 | Determinism smoke test | Standing jump on flat ground, five times: apex reaches the same tile line every time, no variation you can see |
| VC-9 | Camera | Follows smoothly, no jitter while running + jumping, never moves in Z |
| VC-10 | Ground-check gizmo | Select Player in Play mode: yellow box sits at the feet; walk off a ledge → IsGrounded flips (watch jump availability) |
| VC-11 | Seam test | Run the full 40-tile floor both directions: no snags/hops at tile boundaries (composite collider doing its job) |
| VC-12 | Repo state | `git log` shows the milestone commit; `.gitignore` working (no `Library/` in `git status`); pushed to GitHub |

VC-6 and VC-7 are the important ones: they are **GUT-SPEC.md's derived kinematics being checked against reality** with tiles as the measuring stick. If either fails, an inspector value doesn't match the spec (usual suspects: Gravity Scale ≠ 3, Jump Height ≠ 2.2, or Run Speed ≠ 6). Fix the value — never "tune until it feels right"; the spec is the truth (SRS §2.4).

Finally: write the **"What I learned"** entry in `docs/MODULES.md` (minimum three sentences, your own words) — it's deliverable D10's first installment, and the viva presentation is built out of these.

---

## Part E — What Milestone 1 did NOT do (scope honesty)

No QA framework code (the UnityQA assembly contains only the anchor), no event bus, no logging, no sessions, no instrumentation, no overlay, no hazards/checkpoints/tokens/exit, no death (you *can't die* in this build — falling in a gap just means walking back out), no bug injection, no AI, no detection. `Level_PlantedBugs_A` does not exist yet. All of that is later milestones, in the SRS implementation order.

**Next (pending your approval): Milestone 2 — BenchGame completion:** Hazard, KillZone, Checkpoint, Token, LevelExit, GameManager (death/respawn), the C# events a normal game would expose, GUT-SPEC verification against real play, and `Level_Baseline` finalized. After that, instrumentation begins.
