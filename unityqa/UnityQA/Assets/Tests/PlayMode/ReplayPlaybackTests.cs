// -----------------------------------------------------------------------------
// UnityQA Tests — ReplayPlaybackTests.cs               (M3 Slice B, PlayMode)
//
// The full record → playback loop in one physics scene: record a scripted
// session, then let ReplayPlayer drive the SAME controller from the file
// while the scripted source stays neutral. If the player moves, the replay —
// and only the replay — moved it. Asserts the swap, the motion, the
// completion event, and the source restore.
// -----------------------------------------------------------------------------

using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BenchGame;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityQA.Adapters;
using UnityQA.Core;
using UnityQA.Logging;
using UnityQA.Replay;

namespace UnityQA.Tests.PlayMode
{
    public sealed class ReplayPlaybackTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();
        private readonly List<string> sessionFolders = new List<string>();
        private QAConfig config;
        private QARunner runner;
        private ScriptedInputSource script;
        private PlayerController2D controller;
        private ReplayPlayer player;

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
            Assert.IsTrue(Application.isPlaying);
            int ground = LayerMask.NameToLayer("Ground");
            Assert.GreaterOrEqual(ground, 0);

            var floor = Track(new GameObject("TestFloor"));
            floor.layer = ground;
            floor.AddComponent<BoxCollider2D>().size = new Vector2(60f, 1f);
            floor.transform.position = new Vector3(0f, -0.5f, 0f);

            var playerGo = Track(new GameObject("TestPlayer"));
            playerGo.transform.position = new Vector3(0f, 1f, 0f);
            var body = playerGo.AddComponent<Rigidbody2D>();
            body.gravityScale = 3f;
            body.freezeRotation = true;
            playerGo.AddComponent<BoxCollider2D>().size = new Vector2(0.9f, 0.9f);
            var check = new GameObject("GroundCheck");
            check.transform.SetParent(playerGo.transform, false);
            check.transform.localPosition = new Vector3(0f, -0.45f, 0f);
            script = playerGo.AddComponent<ScriptedInputSource>();
            controller = playerGo.AddComponent<PlayerController2D>();
            SetPrivate(controller, "groundCheck", check.transform);
            SetPrivate(controller, "groundLayer", (LayerMask)(1 << ground));

            config = ScriptableObject.CreateInstance<QAConfig>();
            config.consoleEvents = false;
            config.telemetryHz = 2;

            var qa = Track(new GameObject("[QA-PlaybackTest]"));
            qa.SetActive(false);
            runner = qa.AddComponent<QARunner>();
            SetPrivate(runner, "config", config);
            qa.AddComponent<BenchGameAdapter>();
            qa.AddComponent<ReplayRecorder>();
            player = qa.AddComponent<ReplayPlayer>(); // autoPlay stays false
            qa.SetActive(true);

            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (player != null && player.IsPlaying) player.Stop();
            if (runner != null && runner.IsSessionActive) runner.EndSession();
            foreach (var go in spawned) SafeDestroy(go);
            spawned.Clear();
            SafeDestroy(config);
            foreach (string f in sessionFolders) try { Directory.Delete(f, true); } catch { }
            sessionFolders.Clear();
            yield return null;
        }

        /// <summary>Record ~0.6 s of scripted run-right (+ a jump) and return the replay path.</summary>
        private IEnumerator RecordSampleReplay()
        {
            runner.StartSession();
            sessionFolders.Add(Path.Combine(QALogger.SessionsRoot, runner.CurrentSession.FolderName));
            script.moveX = 1f;
            yield return new WaitForSeconds(0.3f);
            script.jumpHeld = true;
            yield return new WaitForSeconds(0.1f);
            script.jumpHeld = false;
            yield return new WaitForSeconds(0.2f);
            script.moveX = 0f;
            runner.EndSession();
            yield return null;

            SetPrivate(player, "replayFile",
                Path.Combine(sessionFolders[sessionFolders.Count - 1], ReplayFileStore.FileName));
        }

        [UnityTest]
        public IEnumerator Playback_MovesPlayer_WithoutLiveInput()
        {
            yield return RecordSampleReplay();

            // Reset the player and keep the scripted (live) source NEUTRAL.
            controller.transform.position = new Vector3(0f, 1f, 0f);
            var rb = controller.GetComponent<Rigidbody2D>();
            rb.linearVelocity = Vector2.zero;
            script.moveX = 0f;
            script.jumpHeld = false;
            yield return new WaitForSeconds(0.2f); // settle

            float startX = controller.transform.position.x;
            player.Play();
            Assert.IsTrue(player.IsPlaying, "playback must start");
            Assert.AreSame(controller.InputSource.GetType(), typeof(ReplayInputSource),
                "controller must be driven by the replay source during playback");

            yield return new WaitForSeconds(0.45f);

            float travelled = controller.transform.position.x - startX;
            Assert.Greater(travelled, 0.8f,
                "player must move right under replay control with no live input " +
                $"(travelled {travelled:F2}u)");
        }

        [UnityTest]
        public IEnumerator Playback_FinishesCleanly_AndRestoresOriginalSource()
        {
            yield return RecordSampleReplay();

            var sourceBefore = controller.InputSource;
            bool finished = false;
            player.PlaybackFinished += () => finished = true;

            player.Play();
            float timeout = Time.time + 5f;
            while (player.IsPlaying && Time.time < timeout) yield return null;

            Assert.IsTrue(finished, "PlaybackFinished must fire at the natural end");
            Assert.IsFalse(player.IsPlaying);
            Assert.AreSame(sourceBefore, controller.InputSource,
                "the original input source must be restored after playback");

            // And the player must be at rest input-wise: no phantom keys.
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(0f, controller.MoveInput, "no residual movement command after replay");
        }

        [UnityTest]
        public IEnumerator ManualStop_RestoresImmediately_NoFinishEvent()
        {
            yield return RecordSampleReplay();

            var sourceBefore = controller.InputSource;
            bool finished = false;
            player.PlaybackFinished += () => finished = true;

            player.Play();
            yield return new WaitForSeconds(0.1f);
            player.Stop();

            Assert.IsFalse(player.IsPlaying);
            Assert.IsFalse(finished, "manual Stop is not a completion");
            Assert.AreSame(sourceBefore, controller.InputSource);
        }
    }
}
