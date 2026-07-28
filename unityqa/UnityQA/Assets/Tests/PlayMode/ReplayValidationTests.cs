// -----------------------------------------------------------------------------
// UnityQA Tests — ReplayValidationTests.cs              (M3 Slice C, PlayMode)
//
// The full loop, live: record a scripted run (with telemetry ON — the
// validator's raw material), then ReplayValidator re-runs it under recording
// and judges fidelity. Same-machine scripted replay should be reasonably
// faithful; assertions use generous bands because this test MEASURES
// determinism rather than assumes it (the measurement is the feature).
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
    public sealed class ReplayValidationTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();
        private readonly List<string> sessionFolders = new List<string>();
        private QAConfig config;
        private QARunner runner;
        private ScriptedInputSource script;
        private ReplayValidator validator;

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
            floor.AddComponent<BoxCollider2D>().size = new Vector2(80f, 1f);
            floor.transform.position = new Vector3(0f, -0.5f, 0f);

            var playerGo = Track(new GameObject("TestPlayer"));
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
            var controller = playerGo.AddComponent<PlayerController2D>();
            SetPrivate(controller, "groundCheck", check.transform);
            SetPrivate(controller, "groundLayer", (LayerMask)(1 << ground));

            config = ScriptableObject.CreateInstance<QAConfig>();
            config.consoleEvents = false;
            config.telemetryHz = 10;                       // dense enough trajectories
            config.validationDeviationThreshold = 2.5f;    // generous: measuring, not wishing

            var qa = Track(new GameObject("[QA-ValidationTest]"));
            qa.SetActive(false);
            runner = qa.AddComponent<QARunner>();
            SetPrivate(runner, "config", config);
            qa.AddComponent<BenchGameAdapter>();
            qa.AddComponent<QALogger>();                   // validator needs real files
            qa.AddComponent<QATelemetrySampler>();
            qa.AddComponent<ReplayRecorder>();
            qa.AddComponent<ReplayPlayer>();
            validator = qa.AddComponent<ReplayValidator>();
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
            foreach (string f in sessionFolders) try { Directory.Delete(f, true); } catch { }
            sessionFolders.Clear();
            yield return null;
        }

        private IEnumerator RecordOriginalSession()
        {
            runner.StartSession();
            sessionFolders.Add(Path.Combine(QALogger.SessionsRoot, runner.CurrentSession.FolderName));
            script.moveX = 1f;
            yield return new WaitForSeconds(0.6f);
            script.jumpHeld = true;
            yield return new WaitForSeconds(0.1f);
            script.jumpHeld = false;
            yield return new WaitForSeconds(0.5f);
            script.moveX = 0f;
            yield return new WaitForSeconds(0.2f);
            runner.EndSession();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Validation_ProducesResultFile_WithMetricsAndLinkedIds()
        {
            yield return RecordOriginalSession();
            string originalFolder = sessionFolders[0];

            // Neutralize live input; the validator drives everything else.
            script.moveX = 0f;
            script.jumpHeld = false;

            ReplayValidationResult completed = null;
            validator.ValidationCompleted += r => completed = r;
            validator.Validate(originalFolder);
            Assert.IsTrue(validator.IsValidating);

            float timeout = Time.time + 15f;
            while (validator.IsValidating && Time.time < timeout) yield return null;
            Assert.IsNotNull(completed, "validation must complete and raise its event");

            // Track the validation session folder for cleanup too.
            sessionFolders.Add(completed.validationFolder);

            string resultPath = Path.Combine(completed.validationFolder, ReplayValidator.ResultFileName);
            Assert.IsTrue(File.Exists(resultPath), "validation.json must be written");

            var fromDisk = JsonUtility.FromJson<ReplayValidationResult>(File.ReadAllText(resultPath));
            Assert.AreNotEqual(fromDisk.originalSessionId, fromDisk.validationSessionId);
            Assert.Greater(fromDisk.comparedSamples, 5, "a real comparison happened");
            Assert.GreaterOrEqual(fromDisk.maxDeviation, 0f);
            Assert.AreNotEqual(ReplayValidationResult.VerdictInvalid, fromDisk.verdict,
                "both trajectories existed — verdict must be a real judgment");

            // Same machine, scripted input, controlled start: fidelity should be
            // in a sane band. This is a MEASUREMENT bound, not a wish — if it
            // fails, record the number in MODULES.md; that datum is Slice C
            // doing its job.
            Assert.Less(fromDisk.maxDeviation, 2.5f,
                $"replay drifted {fromDisk.maxDeviation:F2}u from the original on the same machine");
        }

        [UnityTest]
        public IEnumerator Validation_RefusesToRun_DuringActiveSession()
        {
            yield return RecordOriginalSession();
            runner.StartSession();
            sessionFolders.Add(Path.Combine(QALogger.SessionsRoot, runner.CurrentSession.FolderName));

            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("End the current QA session"));
            validator.Validate(sessionFolders[0]);
            Assert.IsFalse(validator.IsValidating, "validator must refuse while a session runs");

            runner.EndSession();
            yield return null;
        }
    }
}
