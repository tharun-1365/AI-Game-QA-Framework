// -----------------------------------------------------------------------------
// UnityQA Tests — TelemetryPipelineTests.cs             (M2 Slice C, PlayMode)
//
// Live-pipeline integration: a code-built rig (floor + real PlayerController2D
// + full [QA] stack) runs actual physics while assertions listen on the bus.
// Persistence is deliberately NOT exercised here — QALogger is frozen Slice B
// and file I/O in tests would leave session-folder litter; the bus is the
// contract boundary Slice C feeds, so the bus is what we assert on.
//
// Private serialized fields (QARunner.config, controller's groundCheck/layer)
// are set via reflection — the editor-sanctioned SerializedObject route is
// editor-main-thread-only and clunkier inside [UnityTest]; reflection in
// TESTS (never in production code) is the accepted trade.
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
    public sealed class TelemetryPipelineTests
    {
        private const int TelemetryHz = 10;

        private readonly List<GameObject> spawned = new List<GameObject>();
        private QAConfig config;
        private QARunner runner;
        private readonly List<QAEvent> captured = new List<QAEvent>();

        private static void SetPrivate(object target, string field, object value) =>
            target.GetType()
                  .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                  .SetValue(target, value);

        private GameObject Track(GameObject go) { spawned.Add(go); return go; }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // If this assembly is ever misclassified as edit-mode again (see
            // asmdef: includePlatforms MUST stay empty = any platform), fail
            // here with a message instead of an opaque NullReference later.
            Assert.IsTrue(Application.isPlaying,
                "PlayMode tests are running outside Play mode — check that " +
                "UnityQA.Tests.PlayMode.asmdef has an EMPTY includePlatforms list.");

            captured.Clear();

            int ground = LayerMask.NameToLayer("Ground");
            Assert.GreaterOrEqual(ground, 0, "project must define the 'Ground' layer");

            // --- floor: 20 units wide at y = 0 ------------------------------
            var floor = Track(new GameObject("TestFloor"));
            floor.layer = ground;
            var floorCol = floor.AddComponent<BoxCollider2D>();
            floorCol.size = new Vector2(20f, 1f);
            floor.transform.position = new Vector3(0f, -0.5f, 0f); // top surface at y = 0

            // --- player: real controller, spawned 3 units up ----------------
            var player = Track(new GameObject("TestPlayer"));
            player.transform.position = new Vector3(0f, 3f, 0f);
            var body = player.AddComponent<Rigidbody2D>();
            body.gravityScale = 3f;
            body.freezeRotation = true;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            var col = player.AddComponent<BoxCollider2D>();
            col.size = new Vector2(0.9f, 0.9f);
            var check = new GameObject("GroundCheck");
            check.transform.SetParent(player.transform, false);
            check.transform.localPosition = new Vector3(0f, -0.45f, 0f);
            var controller = player.AddComponent<PlayerController2D>();
            SetPrivate(controller, "groundCheck", check.transform);
            SetPrivate(controller, "groundLayer", (LayerMask)(1 << ground));

            // --- [QA] stack -------------------------------------------------
            config = ScriptableObject.CreateInstance<QAConfig>();
            config.telemetryHz = TelemetryHz;
            config.consoleEvents = false; // keep the test log quiet

            var qa = Track(new GameObject("[QA-Test]"));
            qa.SetActive(false);                       // configure before Awake runs
            runner = qa.AddComponent<QARunner>();
            SetPrivate(runner, "config", config);
            qa.AddComponent<BenchGameAdapter>();
            qa.AddComponent<QATelemetrySampler>();
            qa.SetActive(true);                        // Awake fires here, fully wired

            yield return null;                         // let Start() subscriptions run
            runner.Bus.Subscribe(captured.Add);
        }

        /// <summary>
        /// Play mode → Destroy (deferred, engine-managed); edit mode →
        /// DestroyImmediate (Destroy is illegal there). Guards cleanup even if
        /// this assembly is ever misclassified again — the failure would then
        /// be one clear assert, not a cascade of teardown errors.
        /// </summary>
        private static void SafeDestroy(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
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

        private int Count(QAEventType t)
        {
            int n = 0;
            foreach (var e in captured) if (e.Type == t) n++;
            return n;
        }

        [UnityTest]
        public IEnumerator Samples_ArriveAtConfiguredRate()
        {
            runner.StartSession();
            yield return new WaitForSeconds(2f);
            runner.EndSession();

            int samples = Count(QAEventType.PlayerSample);
            // 2 s at 10 Hz → ~20; wide ±30% band tolerates editor frame hitches.
            Assert.GreaterOrEqual(samples, 14, "sampler too slow vs QAConfig.telemetryHz");
            Assert.LessOrEqual(samples, 27, "sampler too fast vs QAConfig.telemetryHz");
        }

        [UnityTest]
        public IEnumerator Sample_PayloadCarriesFullSchema()
        {
            runner.StartSession();
            yield return new WaitForSeconds(0.5f);
            runner.EndSession();

            QAEvent sample = null;
            foreach (var e in captured)
                if (e.Type == QAEventType.PlayerSample) { sample = e; break; }

            Assert.IsNotNull(sample, "no PlayerSample captured");
            Assert.IsTrue(sample.Pos.HasValue, "sample must carry position in the envelope");
            foreach (string key in new[] { "vx", "vy", "g", "mx", "facing", "state" })
                Assert.IsTrue(sample.Payload.ContainsKey(key), $"payload missing '{key}'");
        }

        [UnityTest]
        public IEnumerator SpawnDrop_ProducesLanded_WithImpactSpeed()
        {
            runner.StartSession();
            yield return new WaitForSeconds(1.5f);     // 3-unit drop lands well within this
            runner.EndSession();

            Assert.AreEqual(1, Count(QAEventType.Landed), "exactly one landing expected");
            foreach (var e in captured)
                if (e.Type == QAEventType.Landed)
                {
                    Assert.IsTrue(e.Payload.TryGetValue("fallSpeed", out object v) && v is float f && f > 1f,
                        "landing must report a positive impact speed");
                }
            Assert.AreEqual(0, Count(QAEventType.JumpExecuted),
                "a fall is not a jump — no takeoff may be detected from a spawn drop");
        }

        [UnityTest]
        public IEnumerator NoSampling_OutsideSessions()
        {
            yield return new WaitForSeconds(0.5f);     // no session started
            Assert.AreEqual(0, captured.Count, "nothing may be emitted outside a session");
        }
    }
}
