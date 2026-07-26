// -----------------------------------------------------------------------------
// UnityQA Tests — InputPipelineTests.cs                 (M2 Slice D, PlayMode)
//
// Drives the REAL pipeline with a scripted input source: because of the D-008
// seam, the test attaches its own IPlayerInputSource to the player BEFORE
// PlayerController2D (whose Awake adopts any existing source instead of
// adding the keyboard). The test then "plays" by setting fields — no keyboard
// needed — and asserts on the bus, same contract boundary as the telemetry
// PlayMode tests. This substitution is exactly the mechanism Module 2's AI
// agent will use; these tests are its first proof.
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
    /// <summary>Test-controlled input source; fields set by the test body.</summary>
    public sealed class ScriptedInputSource : MonoBehaviour, IPlayerInputSource
    {
        public float moveX;
        public bool jumpHeld;
        private bool prevHeld;
        private int downFrame = -1;

        // JumpDown derived from held transitions, valid for exactly one frame —
        // matches GetButtonDown semantics so the controller behaves normally.
        private void Update()
        {
            if (jumpHeld && !prevHeld) downFrame = Time.frameCount;
            prevHeld = jumpHeld;
        }

        public float MoveX => moveX;
        public bool JumpDown => Time.frameCount == downFrame;
        public bool JumpHeld => jumpHeld;
    }

    public sealed class InputPipelineTests
    {
        private const int KeyframeSteps = 10; // 0.2 s at 50 Hz — fast cadence for testing

        private readonly List<GameObject> spawned = new List<GameObject>();
        private readonly List<QAEvent> captured = new List<QAEvent>();
        private QAConfig config;
        private QARunner runner;
        private ScriptedInputSource script;

        private static void SetPrivate(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                  .SetValue(target, value);

        private static void SafeDestroy(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }

        private GameObject Track(GameObject go) { spawned.Add(go); return go; }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Assert.IsTrue(Application.isPlaying,
                "PlayMode tests running outside Play mode — check the asmdef includePlatforms.");
            captured.Clear();

            int ground = LayerMask.NameToLayer("Ground");
            Assert.GreaterOrEqual(ground, 0, "project must define the 'Ground' layer");

            var floor = Track(new GameObject("TestFloor"));
            floor.layer = ground;
            floor.AddComponent<BoxCollider2D>().size = new Vector2(20f, 1f);
            floor.transform.position = new Vector3(0f, -0.5f, 0f);

            var player = Track(new GameObject("TestPlayer"));
            player.transform.position = new Vector3(0f, 1f, 0f); // near the floor: grounded fast
            var body = player.AddComponent<Rigidbody2D>();
            body.gravityScale = 3f;
            body.freezeRotation = true;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            player.AddComponent<BoxCollider2D>().size = new Vector2(0.9f, 0.9f);
            var check = new GameObject("GroundCheck");
            check.transform.SetParent(player.transform, false);
            check.transform.localPosition = new Vector3(0f, -0.45f, 0f);

            script = player.AddComponent<ScriptedInputSource>();   // BEFORE the controller: seam adopts it
            var controller = player.AddComponent<PlayerController2D>();
            SetPrivate(controller, "groundCheck", check.transform);
            SetPrivate(controller, "groundLayer", (LayerMask)(1 << ground));

            config = ScriptableObject.CreateInstance<QAConfig>();
            config.consoleEvents = false;
            config.telemetryHz = 2;                                // minimal telemetry noise
            config.inputKeyframeEverySteps = KeyframeSteps;

            var qa = Track(new GameObject("[QA-InputTest]"));
            qa.SetActive(false);
            runner = qa.AddComponent<QARunner>();
            SetPrivate(runner, "config", config);
            qa.AddComponent<BenchGameAdapter>();
            qa.AddComponent<QAInputRecorder>();
            qa.SetActive(true);

            yield return null;                                     // Start() subscriptions
            runner.Bus.Subscribe(captured.Add);
            yield return new WaitForSeconds(0.3f);                 // settle: land + ground
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

        private List<QAEvent> Inputs()
        {
            var list = new List<QAEvent>();
            foreach (var e in captured)
                if (e.Type == QAEventType.InputSample) list.Add(e);
            return list;
        }

        private static bool Flag(QAEvent e, string key) =>
            e.Payload.TryGetValue(key, out object v) && v is bool b && b;

        [UnityTest]
        public IEnumerator SessionStart_EmitsInitialKeyframe_First()
        {
            runner.StartSession();
            yield return null; yield return null;                  // recorder's next Update
            runner.EndSession();

            var inputs = Inputs();
            Assert.GreaterOrEqual(inputs.Count, 1, "session must open with an input keyframe");
            Assert.IsTrue(Flag(inputs[0], "keyframe"), "first input record must be a keyframe");
            Assert.IsFalse(Flag(inputs[0], "jumpPressed"), "no edges on the initial keyframe");
        }

        [UnityTest]
        public IEnumerator Changes_AreDetected_WithJumpEdges()
        {
            runner.StartSession();
            yield return null; yield return null;                  // initial keyframe out

            script.moveX = 1f;                                     // start running right
            yield return null; yield return null;
            script.jumpHeld = true;                                // press jump
            yield return null; yield return null;
            script.jumpHeld = false;                               // release jump
            yield return null; yield return null;
            runner.EndSession();

            var inputs = Inputs();
            bool sawMove = false, sawPress = false, sawRelease = false;
            foreach (var e in inputs)
            {
                if (e.Payload["horizontal"] is int h && h == 1 && !Flag(e, "jumpPressed")) sawMove = true;
                if (Flag(e, "jumpPressed") && Flag(e, "jumpHeld")) sawPress = true;
                if (Flag(e, "jumpReleased") && !Flag(e, "jumpHeld")) sawRelease = true;
            }
            Assert.IsTrue(sawMove, "horizontal change must produce a sample");
            Assert.IsTrue(sawPress, "jump press edge must be flagged");
            Assert.IsTrue(sawRelease, "jump release edge must be flagged");
        }

        [UnityTest]
        public IEnumerator ConstantInput_ProducesOnlyKeyframes_NoSpam()
        {
            runner.StartSession();
            yield return null; yield return null;
            int afterInitial = Inputs().Count;

            yield return new WaitForSeconds(1f);                   // input never changes
            runner.EndSession();

            var inputs = Inputs();
            // 1 s at 50 Hz / 10-step cadence → ~5 keyframes. Every post-initial
            // record MUST be a keyframe; count stays in a generous band.
            for (int i = afterInitial; i < inputs.Count; i++)
                Assert.IsTrue(Flag(inputs[i], "keyframe"),
                    "constant input may only produce keyframes — change-spam detected");
            int keyframes = inputs.Count - afterInitial;
            Assert.That(keyframes, Is.InRange(3, 8),
                "keyframe cadence must follow QAConfig.inputKeyframeEverySteps");
        }

        [UnityTest]
        public IEnumerator NoInputEvents_OutsideSessions()
        {
            script.moveX = 1f;
            script.jumpHeld = true;
            yield return new WaitForSeconds(0.4f);                 // no session active
            Assert.AreEqual(0, captured.Count, "nothing may be emitted outside a session");
        }
    }
}
