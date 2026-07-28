// -----------------------------------------------------------------------------
// UnityQA Tests — ReplayRecorderTests.cs                (M3 Slice A, PlayMode)
//
// End-to-end: real session, scripted input through the D-008 seam, real
// replay.json on disk. This is the one PlayMode suite that intentionally
// touches the filesystem (the file IS the deliverable), so teardown deletes
// the session folders it creates.
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
    public sealed class ReplayRecorderTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();
        private readonly List<string> sessionFolders = new List<string>();
        private QAConfig config;
        private QARunner runner;
        private ScriptedInputSource script;   // reused from InputPipelineTests (same assembly)
        private ReplayRecorder recorder;

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
            floor.AddComponent<BoxCollider2D>().size = new Vector2(20f, 1f);
            floor.transform.position = new Vector3(0f, -0.5f, 0f);

            var player = Track(new GameObject("TestPlayer"));
            player.transform.position = new Vector3(0f, 1f, 0f);
            var body = player.AddComponent<Rigidbody2D>();
            body.gravityScale = 3f;
            body.freezeRotation = true;
            player.AddComponent<BoxCollider2D>().size = new Vector2(0.9f, 0.9f);
            var check = new GameObject("GroundCheck");
            check.transform.SetParent(player.transform, false);
            check.transform.localPosition = new Vector3(0f, -0.45f, 0f);
            script = player.AddComponent<ScriptedInputSource>();
            var controller = player.AddComponent<PlayerController2D>();
            SetPrivate(controller, "groundCheck", check.transform);
            SetPrivate(controller, "groundLayer", (LayerMask)(1 << ground));

            config = ScriptableObject.CreateInstance<QAConfig>();
            config.consoleEvents = false;
            config.telemetryHz = 2;

            var qa = Track(new GameObject("[QA-ReplayTest]"));
            qa.SetActive(false);
            runner = qa.AddComponent<QARunner>();
            SetPrivate(runner, "config", config);
            qa.AddComponent<BenchGameAdapter>();
            recorder = qa.AddComponent<ReplayRecorder>();
            qa.SetActive(true);

            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (runner != null && runner.IsSessionActive) runner.EndSession();
            foreach (var go in spawned) SafeDestroy(go);
            spawned.Clear();
            SafeDestroy(config);
            foreach (string folder in sessionFolders)
                try { Directory.Delete(folder, true); } catch { }
            sessionFolders.Clear();
            yield return null;
        }

        private string RunSessionFor(float seconds, out string sessionId)
        {
            runner.StartSession();
            sessionId = runner.CurrentSession.SessionId;
            string folder = Path.Combine(QALogger.SessionsRoot, runner.CurrentSession.FolderName);
            sessionFolders.Add(folder);
            return folder;
        }

        [UnityTest]
        public IEnumerator Session_ProducesReplayJson_WithMatchingSessionId()
        {
            string folder = RunSessionFor(0f, out string sessionId);
            script.moveX = 1f;
            yield return new WaitForSeconds(0.5f);
            script.jumpHeld = true;
            yield return new WaitForSeconds(0.2f);
            script.jumpHeld = false;
            yield return new WaitForSeconds(0.2f);
            runner.EndSession();
            yield return null;

            string path = Path.Combine(folder, ReplayFileStore.FileName);
            Assert.IsTrue(File.Exists(path), "replay.json must exist beside session.json");
            Assert.IsTrue(File.Exists(Path.Combine(folder, SessionManifest.FileName)),
                "session.json must exist in the same folder");

            var replay = JsonUtility.FromJson<ReplayRecording>(File.ReadAllText(path));
            Assert.AreEqual(ReplayRecording.CurrentSchemaVersion, replay.schemaVersion);
            Assert.AreEqual(sessionId, replay.sessionId, "replay must reference its session's UUID");
            Assert.Greater(replay.frameCount, 0, "frameCount must be > 0 after ~1 s of play");
            Assert.AreEqual(replay.frameCount, replay.frames.Length);
        }

        [UnityTest]
        public IEnumerator Frames_CaptureScriptedInputs_InOrder()
        {
            string folder = RunSessionFor(0f, out _);
            yield return new WaitForSeconds(0.3f);   // neutral input first
            script.moveX = 1f;                       // then run right
            script.jumpHeld = true;                  // and hold jump
            yield return new WaitForSeconds(0.4f);
            runner.EndSession();
            yield return null;

            var replay = JsonUtility.FromJson<ReplayRecording>(
                File.ReadAllText(Path.Combine(folder, ReplayFileStore.FileName)));

            bool sawNeutral = false, sawRightWithJump = false;
            int lastFrame = -1;
            float lastTime = -1f;
            foreach (var f in replay.frames)
            {
                Assert.AreEqual(lastFrame + 1, f.frameNumber, "frameNumbers must be contiguous");
                Assert.GreaterOrEqual(f.timestamp, lastTime, "timestamps must be non-decreasing");
                lastFrame = f.frameNumber;
                lastTime = f.timestamp;

                if (f.horizontal == 0f && !f.jumpHeld) sawNeutral = true;
                if (f.horizontal == 1f && f.jumpHeld) sawRightWithJump = true;
            }
            Assert.IsTrue(sawNeutral, "early frames must show neutral input");
            Assert.IsTrue(sawRightWithJump, "later frames must show run-right + jump held");
        }

        [UnityTest]
        public IEnumerator NoSession_NoRecording_AndFramesResetPerSession()
        {
            script.moveX = 1f;
            yield return new WaitForSeconds(0.3f);
            Assert.IsFalse(recorder.IsRecording);
            Assert.AreEqual(0, recorder.FrameCount, "no frames may be captured outside a session");

            string folder1 = RunSessionFor(0f, out string sid1);
            yield return new WaitForSeconds(0.3f);
            runner.EndSession();
            yield return null;

            string folder2 = RunSessionFor(0f, out string sid2);
            yield return new WaitForSeconds(0.2f);
            runner.EndSession();
            yield return null;

            var r1 = JsonUtility.FromJson<ReplayRecording>(
                File.ReadAllText(Path.Combine(folder1, ReplayFileStore.FileName)));
            var r2 = JsonUtility.FromJson<ReplayRecording>(
                File.ReadAllText(Path.Combine(folder2, ReplayFileStore.FileName)));

            Assert.AreNotEqual(r1.sessionId, r2.sessionId);
            Assert.AreEqual(sid1, r1.sessionId);
            Assert.AreEqual(sid2, r2.sessionId);
            Assert.Greater(r1.frameCount, r2.frameCount,
                "second, shorter session must not inherit the first session's frames");
        }
    }
}
