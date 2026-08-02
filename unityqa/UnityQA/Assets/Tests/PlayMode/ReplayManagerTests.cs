// -----------------------------------------------------------------------------
// UnityQA Tests — ReplayManagerTests.cs                 (M3 Slice D, PlayMode)
//
// End-to-end orchestration: record a real session, refresh the catalog,
// play by session ID through the manager. Assertions use CONTAINMENT, never
// exact counts — the real SessionsRoot on a developer machine holds prior
// sessions the test must tolerate.
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
    public sealed class ReplayManagerTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();
        private readonly List<string> sessionFolders = new List<string>();
        private QAConfig config;
        private QARunner runner;
        private ScriptedInputSource script;
        private ReplayManager manager;

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
            floor.AddComponent<BoxCollider2D>().size = new Vector2(40f, 1f);
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
            var controller = playerGo.AddComponent<PlayerController2D>();
            SetPrivate(controller, "groundCheck", check.transform);
            SetPrivate(controller, "groundLayer", (LayerMask)(1 << ground));

            config = ScriptableObject.CreateInstance<QAConfig>();
            config.consoleEvents = false;
            config.telemetryHz = 2;

            var qa = Track(new GameObject("[QA-ManagerTest]"));
            qa.SetActive(false);
            runner = qa.AddComponent<QARunner>();
            SetPrivate(runner, "config", config);
            qa.AddComponent<BenchGameAdapter>();
            qa.AddComponent<QALogger>();
            qa.AddComponent<ReplayRecorder>();
            qa.AddComponent<ReplayPlayer>();
            manager = qa.AddComponent<ReplayManager>();
            qa.SetActive(true);

            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var replayPlayer = manager != null ? manager.GetComponent<ReplayPlayer>() : null;
            if (replayPlayer != null && replayPlayer.IsPlaying) replayPlayer.Stop();
            if (runner != null && runner.IsSessionActive) runner.EndSession();
            foreach (var go in spawned) SafeDestroy(go);
            spawned.Clear();
            SafeDestroy(config);
            foreach (string f in sessionFolders) try { Directory.Delete(f, true); } catch { }
            sessionFolders.Clear();
            yield return null;
        }

        private IEnumerator RecordShortSession()
        {
            runner.StartSession();
            sessionFolders.Add(Path.Combine(QALogger.SessionsRoot, runner.CurrentSession.FolderName));
            script.moveX = 1f;
            yield return new WaitForSeconds(0.4f);
            script.moveX = 0f;
            runner.EndSession();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Catalog_ContainsFreshlyRecordedSession_WithReplayFacts()
        {
            yield return RecordShortSession();
            string recordedId = null;

            manager.RefreshCatalog();
            foreach (ReplayMetadata e in manager.Entries)
                if (e.folderPath == sessionFolders[0]) recordedId = e.sessionId;

            Assert.IsNotNull(recordedId, "the just-recorded session must appear in the catalog");
            ReplayMetadata entry = null;
            foreach (ReplayMetadata e in manager.Entries)
                if (e.sessionId == recordedId) entry = e;

            Assert.IsTrue(entry.hasReplay, "recorded session must show its replay");
            Assert.Greater(entry.replayFrameCount, 0);
            Assert.AreEqual("closed", entry.status);
            Assert.IsTrue(File.Exists(Path.Combine(QALogger.SessionsRoot,
                ReplayCatalog.CatalogFileName)), "catalog.json must be persisted");
        }

        [UnityTest]
        public IEnumerator PlayBySessionId_StartsPlayback_ViaExistingPlayer()
        {
            yield return RecordShortSession();
            manager.RefreshCatalog();

            string id = null;
            foreach (ReplayMetadata e in manager.Entries)
                if (e.folderPath == sessionFolders[0]) id = e.sessionId;
            Assert.IsNotNull(id);

            script.moveX = 0f; // live input neutral — replay must do the driving
            Assert.IsTrue(manager.PlayBySessionId(id), "manager must start playback");

            var replayPlayer = manager.GetComponent<ReplayPlayer>();
            Assert.IsTrue(replayPlayer.IsPlaying);

            float timeout = Time.time + 5f;
            while (replayPlayer.IsPlaying && Time.time < timeout) yield return null;
            Assert.IsFalse(replayPlayer.IsPlaying, "playback must finish cleanly");
        }

        [UnityTest]
        public IEnumerator PlayByUnknownId_FailsGracefully()
        {
            manager.RefreshCatalog();

            LogAssert.Expect(
                LogType.Error,
                new System.Text.RegularExpressions.Regex("No cataloged replay"));

            Assert.IsFalse(manager.PlayBySessionId("no-such-session-id"));

            yield return null;
        }
    }
}
