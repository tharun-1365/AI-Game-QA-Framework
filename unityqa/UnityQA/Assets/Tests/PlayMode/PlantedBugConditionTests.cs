// -----------------------------------------------------------------------------
// UnityQA Tests — PlantedBugConditionTests.cs           (M6 Slice A, PlayMode)
//
// Proves, under real physics, that each planted-bug CLASS in BENCHMARK.md
// produces exactly the observable evidence the answer key promises — using
// the same code-built-arena methodology as BenchGameRunTests (scenes are not
// loaded; the defect condition is constructed, which is what makes each test
// a statement about the CLASS, pinned independently of scene authoring):
//
//   PB-001 class — ground that looks solid but has no collider drops the
//                  player out of the world → OutOfBounds (+ PlayerDied).
//   PB-002 class — a sealed basin deeper than the jump height traps a LIVE
//                  player: input keeps flowing, no outcome ever occurs, and
//                  scripted escape attempts provably fail.
//   PB-003 class — the same scripted input that reaches Success on clean
//                  geometry ends in SpikeDeath once a spike sits on the path
//                  (the clean→planted regression flip, in one test).
//   PB-004 class — the same scripted input that completes through an enabled
//                  exit trigger produces NO TriggerFired / NO RunEnded when
//                  the trigger collider is disabled, though the player
//                  demonstrably reaches and passes the door.
//
// GROUND-TRUTH scope only (M6-A): these tests prove the bugs are real and
// observable in the existing event vocabulary. No oracle judges anything
// here — detection claims belong to M6-B/C.
// -----------------------------------------------------------------------------

using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BenchGame;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityQA.Adapters;
using UnityQA.Core;

namespace UnityQA.Tests.PlayMode
{
    public sealed class PlantedBugConditionTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();
        private readonly List<QAEvent> captured = new List<QAEvent>();
        private QAConfig config;
        private QARunner runner;
        private ScriptedInputSource script;
        private GameRun run;
        private PlayerController2D controller;

        private static void SetPrivate(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                  .SetValue(target, value);

        private static void SafeDestroy(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }

        private GameObject Track(GameObject go) { spawned.Add(go); return go; }

        private GameObject Box(string name, Vector2 center, Vector2 size, int layer)
        {
            var go = Track(new GameObject(name));
            go.layer = layer;
            go.transform.position = center;
            go.AddComponent<BoxCollider2D>().size = size;
            return go;
        }

        /// <summary>Player + spawn + GameRun + instrumented [QA] — the shared
        /// tail of every arena (geometry differs per test, this never does).</summary>
        private IEnumerator FinishArena(float killY)
        {
            int ground = LayerMask.NameToLayer("Ground");

            var playerGo = Track(new GameObject("Player"));
            playerGo.transform.position = new Vector3(0f, 1f, 0f);
            var body = playerGo.AddComponent<Rigidbody2D>();
            body.gravityScale = 3f;
            body.freezeRotation = true;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            playerGo.AddComponent<BoxCollider2D>().size = new Vector2(0.9f, 0.9f);
            var check = new GameObject("GroundCheck");
            check.transform.SetParent(playerGo.transform, false);
            check.transform.localPosition = new Vector3(0f, -0.45f, 0f);
            script = playerGo.AddComponent<ScriptedInputSource>();
            controller = playerGo.AddComponent<PlayerController2D>();
            SetPrivate(controller, "groundCheck", check.transform);
            SetPrivate(controller, "groundLayer", (LayerMask)(1 << ground));

            var spawn = Track(new GameObject("Spawn"));
            spawn.transform.position = new Vector3(0f, 1f, 0f);
            var runGo = Track(new GameObject("GameRun"));
            run = runGo.AddComponent<GameRun>();
            SetPrivate(run, "spawnPoint", spawn.transform);
            SetPrivate(run, "killY", killY);

            config = ScriptableObject.CreateInstance<QAConfig>();
            config.consoleEvents = false;
            config.telemetryHz = 5;

            var qa = Track(new GameObject("[QA-PlantedBugTest]"));
            qa.SetActive(false);
            runner = qa.AddComponent<QARunner>();
            SetPrivate(runner, "config", config);
            qa.AddComponent<BenchGameAdapter>();
            qa.AddComponent<QATelemetrySampler>();
            qa.SetActive(true);

            captured.Clear();
            yield return null;
            runner.Bus.Subscribe(captured.Add);
        }

        private IEnumerator TearDownArena()
        {
            if (runner != null && runner.IsSessionActive) runner.EndSession();
            foreach (var go in spawned) SafeDestroy(go);
            spawned.Clear();
            SafeDestroy(config);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown() { yield return TearDownArena(); }

        private List<QAEvent> OfType(QAEventType t)
        {
            var list = new List<QAEvent>();
            foreach (var e in captured) if (e.Type == t) list.Add(e);
            return list;
        }

        private IEnumerator RunUntilEnded(float seconds)
        {
            float timeout = Time.time + seconds;
            while (run.State == GameRun.RunState.Running && Time.time < timeout) yield return null;
        }

        // NOTE ON CREATION ORDER: ExitDoor and SpikeHazard resolve GameRun in
        // Awake (and stay inert if none exists yet), so triggers are added
        // AFTER FinishArena — the same reason BenchGameRunTests creates its
        // GameRun before its spike and exit.

        private GameObject AddExit(float x, bool triggerEnabled)
        {
            var exit = Track(new GameObject("Exit"));
            exit.transform.position = new Vector3(x, 1f, 0f);
            var col = exit.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.enabled = triggerEnabled;             // false = PB-004 in one line
            exit.AddComponent<ExitDoor>();
            return exit;
        }

        private GameObject AddSpike(float x)
        {
            var spike = Track(new GameObject("Spike"));
            spike.transform.position = new Vector3(x, 0.5f, 0f);
            spike.transform.localScale = new Vector3(0.8f, 0.8f, 1f);
            spike.AddComponent<BoxCollider2D>().isTrigger = true;
            spike.AddComponent<SpikeHazard>();
            return spike;
        }

        // ------------------------------------------------------- PB-001 class

        [UnityTest]
        public IEnumerator ColliderGap_GroundThatOnlyLooksSolid_ProducesOutOfBounds()
        {
            int ground = LayerMask.NameToLayer("Ground");
            Assert.GreaterOrEqual(ground, 0);

            // Real floor x∈[-2,3]; from x=3 the "floor" is a bare sprite —
            // rendered exactly like ground, physically absent (the PB-001 class).
            Box("Floor_Real", new Vector2(0.5f, -0.5f), new Vector2(5f, 1f), ground);
            var visual = Track(new GameObject("Floor_VisualOnly"));
            visual.transform.position = new Vector3(6f, -0.5f, 0f);
            visual.transform.localScale = new Vector3(6f, 1f, 1f);   // x∈[3,9]
            visual.AddComponent<SpriteRenderer>();                    // no collider — that IS the bug

            yield return FinishArena(killY: -4f);
            runner.StartSession();
            script.moveX = 1f;                        // run right onto the fake floor
            yield return RunUntilEnded(6f);
            runner.EndSession();

            Assert.AreEqual(SessionOutcome.OutOfBounds, run.Outcome,
                "walking onto collider-less ground must end as OutOfBounds");
            Assert.Greater(controller.transform.position.x, 3f,
                "the fall must begin where the ground still LOOKED solid");
            Assert.Less(controller.transform.position.x, 9f,
                "…and within the visually-solid stretch, not past its end");
            Assert.AreEqual(1, OfType(QAEventType.PlayerDied).Count);
            Assert.AreEqual("outOfBounds", OfType(QAEventType.PlayerDied)[0].Payload["cause"]);
            Assert.AreEqual("OutOfBounds", OfType(QAEventType.RunEnded)[0].Payload["outcome"]);
        }

        // ------------------------------------------------------- PB-002 class

        [UnityTest]
        public IEnumerator SoftLockBasin_IsEnterable_AliveAndInescapable_WithNoOutcome()
        {
            int ground = LayerMask.NameToLayer("Ground");
            Assert.GreaterOrEqual(ground, 0);

            // A wide, deep, open-topped basin centred under the player's spawn
            // (FinishArena spawns the Player and GameRun spawnPoint at (0,1)).
            // Floor top y=-3 (x∈[-4.5,4.5]); side walls rise to y=1.5 — far
            // above the player's jump reach from the floor (apex ≈ y=-0.35).
            // killY=-6 sits well below the floor, so the trapped player LIVES.
            //
            // WHY A STRAIGHT DROP, NOT A WALK-IN. PB-002's property is
            // INESCAPABILITY once inside; how the player enters is incidental
            // (in the scene it falls off platform 1's edge). A free vertical
            // drop onto a wide floor is the one entry with NO dependence on run
            // speed, ledge-edge timing, or wall friction — each of which made an
            // earlier walk-in entry flaky under real physics (pinning on a
            // narrow pit's far wall; then a landing-transient ground-check
            // flicker aborting the walk and leaving the player on the ledge).
            // The walls tower far above the player's jump from the floor, so the
            // entry technique cannot influence the escape result measured below.
            Box("Basin_Floor", new Vector2(0f, -3.5f), new Vector2(9f, 1f), ground);     // top y=-3
            Box("Basin_WallL", new Vector2(-4.25f, -1f), new Vector2(0.5f, 5f), ground); // top y=1.5
            Box("Basin_WallR", new Vector2(4.25f, -1f), new Vector2(0.5f, 5f), ground);  // top y=1.5

            yield return FinishArena(killY: -6f);
            runner.StartSession();

            // ENTER: the player spawns above the open basin and falls straight
            // in (no horizontal input) onto the floor.
            float timeout = Time.time + 4f;
            while (controller.transform.position.y > -2f && Time.time < timeout) yield return null;
            Assert.Less(controller.transform.position.y, -2f, "player must fall into and land in the basin");

            // ESCAPE ATTEMPTS: for a fixed number of physics steps, actively run
            // FIRST one way then the other and jump on a cadence. A soft lock
            // means none of it produces meaningful progress — the player never
            // clears the walls and no outcome is ever recorded.
            float peakY = controller.transform.position.y;
            for (int step = 0; step < 300; step++)              // 6 s at 50 Hz
            {
                script.moveX = (step / 30) % 2 == 0 ? 1f : -1f;          // sweep both directions
                if (step % 15 == 0) script.jumpHeld = !script.jumpHeld;  // fresh jump edges → real jumps
                yield return new WaitForFixedUpdate();
                if (controller.transform.position.y > peakY) peakY = controller.transform.position.y;
            }

            Assert.AreEqual(GameRun.RunState.Running, run.State,
                "a soft lock produces NO outcome — the run must still be running");
            Assert.AreEqual(0, OfType(QAEventType.RunEnded).Count, "no terminal event of any kind");
            Assert.IsTrue(controller.enabled, "input is still connected — the player is stuck, not frozen");
            Assert.Greater(controller.transform.position.y, -6f, "alive: above the kill boundary");
            Assert.Less(peakY, 0f,
                "every escape jump must peak below y=0 (apex from the floor is " +
                "-3 + 0.45 + 2.2 = -0.35), nowhere near the wall tops at y=1.5 — " +
                "if this fails the basin is escapable and PB-002 is not a soft lock");
            Assert.That(controller.transform.position.x, Is.InRange(-4.5f, 4.5f),
                "confined: the player never leaves the basin's x-range");

            runner.EndSession();
        }

        // ------------------------------------------------------- PB-003 class

        [UnityTest]
        public IEnumerator SpikeOnGoldenPath_SameInput_FlipsSuccessToSpikeDeath()
        {
            int ground = LayerMask.NameToLayer("Ground");
            Assert.GreaterOrEqual(ground, 0);

            // CLEAN pass: floor, exit at x=6, no hazard — run right → Success.
            Box("Floor", new Vector2(4f, -0.5f), new Vector2(16f, 1f), ground);
            yield return FinishArena(killY: -4f);
            AddExit(6f, triggerEnabled: true);
            runner.StartSession();
            script.moveX = 1f;
            yield return RunUntilEnded(5f);
            Assert.AreEqual(SessionOutcome.Success, run.Outcome, "clean geometry: the path is golden");
            yield return TearDownArena();

            // PLANTED pass: identical arena + one spike ON the path at x=4.
            Box("Floor", new Vector2(4f, -0.5f), new Vector2(16f, 1f), ground);
            yield return FinishArena(killY: -4f);
            AddExit(6f, triggerEnabled: true);
            AddSpike(4f);
            runner.StartSession();
            script.moveX = 1f;                        // the SAME input policy
            yield return RunUntilEnded(5f);
            runner.EndSession();

            Assert.AreEqual(SessionOutcome.SpikeDeath, run.Outcome,
                "identical input, planted spike: Success must flip to SpikeDeath — " +
                "the regression signature M6-C will measure");
            Assert.AreEqual("spike", OfType(QAEventType.PlayerDied)[0].Payload["cause"]);
            Assert.AreEqual(0, OfType(QAEventType.TriggerFired).Count,
                "the exit is never reached once the spike ends the run");
        }

        // ------------------------------------------------------- PB-004 class

        [UnityTest]
        public IEnumerator MissingExitTrigger_SameInput_ReachesTheDoorButNeverCompletes()
        {
            int ground = LayerMask.NameToLayer("Ground");
            Assert.GreaterOrEqual(ground, 0);

            // CONTROL pass: enabled trigger → Success (the input reaches the door).
            Box("Floor", new Vector2(4f, -0.5f), new Vector2(16f, 1f), ground);
            yield return FinishArena(killY: -4f);
            AddExit(6f, triggerEnabled: true);
            runner.StartSession();
            script.moveX = 1f;
            yield return RunUntilEnded(5f);
            Assert.AreEqual(SessionOutcome.Success, run.Outcome, "control: the door works when its trigger is live");
            yield return TearDownArena();

            // PLANTED pass: same arena, trigger collider DISABLED (BUG-004 method).
            Box("Floor", new Vector2(4f, -0.5f), new Vector2(16f, 1f), ground);
            yield return FinishArena(killY: -4f);
            AddExit(6f, triggerEnabled: false);
            runner.StartSession();
            script.moveX = 1f;                        // the SAME input policy
            float timeout = Time.time + 3f;
            while (controller.transform.position.x < 8f && Time.time < timeout) yield return null;

            Assert.Greater(controller.transform.position.x, 6f,
                "the player REACHES and passes the exit location — the door just never fires");
            script.moveX = 0f;
            yield return new WaitForSeconds(0.3f);

            Assert.AreEqual(GameRun.RunState.Running, run.State,
                "no completion: the run never ends on its own");
            Assert.AreEqual(0, OfType(QAEventType.TriggerFired).Count,
                "the missing-trigger signature: TriggerFired is absent from the whole session");
            Assert.AreEqual(0, OfType(QAEventType.RunEnded).Count, "and no terminal outcome exists");
            runner.EndSession();
        }
    }
}
