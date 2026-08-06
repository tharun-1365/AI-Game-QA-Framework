// -----------------------------------------------------------------------------
// UnityQA Tests — BenchGameRunTests.cs                  (M5 Slice C, PlayMode)
//
// Benchmark run lifecycle under real physics: success, spike death,
// out-of-bounds, once-only outcome recording, session termination (player
// frozen), reset, and the QA-side observation chain (PlayerDied /
// TriggerFired / RunEnded events on the bus, in order).
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
    public sealed class BenchGameRunTests
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

        /// <param name="withSpike">Spike at x=4 on the floor.</param>
        /// <param name="withExit">Exit door at x=6.</param>
        /// <param name="floorWidth">Floor from x=-2 to x=floorRight; beyond is void.</param>
        private IEnumerator BuildArena(bool withSpike, bool withExit, float floorRight)
        {
            int ground = LayerMask.NameToLayer("Ground");
            Assert.GreaterOrEqual(ground, 0);

            var floor = Track(new GameObject("Floor"));
            floor.layer = ground;
            float width = floorRight - (-2f);
            var col = floor.AddComponent<BoxCollider2D>();
            col.size = new Vector2(width, 1f);
            floor.transform.position = new Vector3(-2f + width / 2f, -0.5f, 0f);

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
            SetPrivate(run, "killY", -4f);

            if (withSpike)
            {
                var spike = Track(new GameObject("Spike"));
                spike.transform.position = new Vector3(4f, 1f, 0f);
                spike.AddComponent<BoxCollider2D>().isTrigger = true;
                spike.AddComponent<SpikeHazard>();
            }
            if (withExit)
            {
                var exit = Track(new GameObject("Exit"));
                exit.transform.position = new Vector3(6f, 1f, 0f);
                exit.AddComponent<BoxCollider2D>().isTrigger = true;
                exit.AddComponent<ExitDoor>();
            }

            config = ScriptableObject.CreateInstance<QAConfig>();
            config.consoleEvents = false;
            config.telemetryHz = 5;

            var qa = Track(new GameObject("[QA-RunTest]"));
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

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (runner != null && runner.IsSessionActive) runner.EndSession();
            foreach (var go in spawned) SafeDestroy(go);
            spawned.Clear();
            SafeDestroy(config);
            yield return null;
        }

        private List<QAEvent> OfType(QAEventType t)
        {
            var list = new List<QAEvent>();
            foreach (var e in captured) if (e.Type == t) list.Add(e);
            return list;
        }

        [UnityTest]
        public IEnumerator ReachingExit_EndsRun_Success_WithEventChain()
        {
            yield return BuildArena(withSpike: false, withExit: true, floorRight: 12f);
            runner.StartSession();
            script.moveX = 1f;                       // run right toward the exit
            float timeout = Time.time + 5f;
            while (run.State == GameRun.RunState.Running && Time.time < timeout) yield return null;
            runner.EndSession();

            Assert.AreEqual(GameRun.RunState.Ended, run.State);
            Assert.AreEqual(SessionOutcome.Success, run.Outcome);

            var ended = OfType(QAEventType.RunEnded);
            Assert.AreEqual(1, ended.Count, "exactly one RunEnded event");
            Assert.AreEqual("Success", ended[0].Payload["outcome"]);
            var trig = OfType(QAEventType.TriggerFired);
            Assert.AreEqual(1, trig.Count, "success also fires the exit trigger event");
            Assert.AreEqual("exit.door", trig[0].Payload["triggerId"]);
            Assert.AreEqual(0, OfType(QAEventType.PlayerDied).Count);
        }

        [UnityTest]
        public IEnumerator TouchingSpike_EndsRun_SpikeDeath_PlayerFrozen()
        {
            yield return BuildArena(withSpike: true, withExit: true, floorRight: 12f);
            runner.StartSession();
            script.moveX = 1f;                       // runs into the spike at x=4
            float timeout = Time.time + 5f;
            while (run.State == GameRun.RunState.Running && Time.time < timeout) yield return null;

            Assert.AreEqual(SessionOutcome.SpikeDeath, run.Outcome);
            Assert.IsFalse(controller.enabled, "session termination: input is disconnected");

            float xAtDeath = controller.transform.position.x;
            script.moveX = 1f;                       // input continues but must do nothing
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(xAtDeath, controller.transform.position.x, 0.01f,
                "a frozen player must not keep moving");
            runner.EndSession();

            var died = OfType(QAEventType.PlayerDied);
            Assert.AreEqual(1, died.Count);
            Assert.AreEqual("spike", died[0].Payload["cause"]);
            Assert.AreEqual("SpikeDeath", OfType(QAEventType.RunEnded)[0].Payload["outcome"]);
        }

        [UnityTest]
        public IEnumerator FallingOffWorld_EndsRun_OutOfBounds()
        {
            yield return BuildArena(withSpike: false, withExit: false, floorRight: 3f); // short floor → void
            runner.StartSession();
            script.moveX = 1f;                       // runs off the edge and falls
            float timeout = Time.time + 6f;
            while (run.State == GameRun.RunState.Running && Time.time < timeout) yield return null;
            runner.EndSession();

            Assert.AreEqual(SessionOutcome.OutOfBounds, run.Outcome);
            var died = OfType(QAEventType.PlayerDied);
            Assert.AreEqual(1, died.Count);
            Assert.AreEqual("outOfBounds", died[0].Payload["cause"]);
            Assert.AreEqual("OutOfBounds", OfType(QAEventType.RunEnded)[0].Payload["outcome"]);
        }

        [UnityTest]
        public IEnumerator Outcome_IsRecordedExactlyOnce_EvenWithLateEndCalls()
        {
            yield return BuildArena(withSpike: false, withExit: false, floorRight: 12f);
            runner.StartSession();
            run.EndRun(SessionOutcome.Quit);         // public path (Escape key uses it)
            run.EndRun(SessionOutcome.SpikeDeath);   // must be ignored — run already ended
            run.EndRun(SessionOutcome.Success);      // ignored too
            yield return null;
            runner.EndSession();

            Assert.AreEqual(SessionOutcome.Quit, run.Outcome, "first outcome wins, forever");
            Assert.AreEqual(1, OfType(QAEventType.RunEnded).Count);
            Assert.AreEqual("Quit", OfType(QAEventType.RunEnded)[0].Payload["outcome"]);
        }

        [UnityTest]
        public IEnumerator Reset_RestoresRunningState_AtSpawn()
        {
            yield return BuildArena(withSpike: false, withExit: false, floorRight: 12f);
            run.EndRun(SessionOutcome.Quit);
            Assert.AreEqual(GameRun.RunState.Ended, run.State);

            controller.transform.position = new Vector3(8f, 1f, 0f); // wander the corpse
            run.ResetRun();
            yield return new WaitForFixedUpdate();

            Assert.AreEqual(GameRun.RunState.Running, run.State);
            Assert.IsTrue(controller.enabled, "input reconnected after reset");
            Assert.AreEqual(0f, controller.transform.position.x, 0.1f, "back at spawn");
        }
    }
}
