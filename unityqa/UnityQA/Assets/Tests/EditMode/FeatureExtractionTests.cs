// -----------------------------------------------------------------------------
// UnityQA Tests — FeatureExtractionTests.cs                 (M4 Slice A tests)
//
// Every input document is generated with the REAL production writers
// (JsonLineWriter, SessionManifest, ReplayFileStore, JsonUtility), so these
// tests pin the producer→extractor pairing, not a hand-rolled imitation of
// it. The synthetic session is built from known motion, so every formula in
// the SessionFeatures contract is checked against hand-computed values.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityQA.Core;
using UnityQA.Features;
using UnityQA.Logging;
using UnityQA.Replay;

namespace UnityQA.Tests
{
    public sealed class FeatureExtractionTests
    {
        private string folder;
        private QAConfig config;
        private QASessionInfo session;

        [SetUp]
        public void SetUp()
        {
            folder = Directory.CreateDirectory(Path.Combine(
                Path.GetTempPath(), "unityqa-test-" + Guid.NewGuid().ToString("N"))).FullName;
            config = ScriptableObject.CreateInstance<QAConfig>();
            session = new QASessionInfo("FeatureLevel", () => 0f,
                new DateTime(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc), "u", "a");
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(folder, true); } catch { }
            UnityEngine.Object.DestroyImmediate(config);
        }

        // --------------------------------------------------------- builders

        private void WriteManifest(float duration = 10f) =>
            SessionManifest.WriteClosed(session, config, folder, duration, eventCount: 99);

        /// <summary>
        /// Known synthetic run, 10 Hz, 21 samples over 2.0 s:
        ///   t 0.0–1.0  run right at 6 u/s   (x: 0 → 6, grounded, vx=6)
        ///   t 1.0–1.5  airborne jump arc    (x advances 3 more, g=0, |v|≈8 peak)
        ///   t 1.5–2.0  idle                 (x fixed at 9, grounded, v=0)
        /// Plus 1 JumpExecuted + 1 Landed event.
        /// Hand-computed ground truth:
        ///   duration 2.0 · distance ≈ 9.0+ (jump arc adds a little y path)
        ///   maxSpeed = 8 · airtime = 0.5 · idle = 0.5 · airFrac = idleFrac = 0.25
        /// </summary>
        private void WriteEvents()
        {
            var w = new JsonLineWriter();
            using var f = new StreamWriter(Path.Combine(folder, "events.jsonl"));
            f.WriteLine(w.HeaderLine("events", session));
            long seq = 0;
            f.WriteLine(w.EventLine(new QAEvent(session.SessionId, seq++, 0f, 0,
                QAEventType.SessionStarted, null, null)));

            void Sample(float t, float x, float y, float vx, float vy, int g)
            {
                var payload = new Dictionary<string, object>
                    { { "vx", vx }, { "vy", vy }, { "g", g }, { "mx", vx > 0 ? 1 : 0 } };
                f.WriteLine(w.EventLine(new QAEvent(session.SessionId, seq++, t, (int)(t * 60),
                    QAEventType.PlayerSample, new Vector2(x, y), payload)));
            }

            for (int i = 0; i <= 10; i++)                    // 0.0–1.0 run right
                Sample(i * 0.1f, i * 0.6f, 1.45f, 6f, 0f, 1);

            f.WriteLine(w.EventLine(new QAEvent(session.SessionId, seq++, 1.0f, 60,
                QAEventType.JumpExecuted, new Vector2(6f, 1.45f), null)));

            for (int i = 1; i <= 5; i++)                     // 1.0–1.5 airborne
            {
                float t = 1.0f + i * 0.1f;
                float y = 1.45f + Mathf.Sin(i / 5f * Mathf.PI) * 1.0f;
                Sample(t, 6f + i * 0.6f, y, 6f, i <= 2 ? 5.29f : -5.29f, 0);
            }

            f.WriteLine(w.EventLine(new QAEvent(session.SessionId, seq++, 1.5f, 90,
                QAEventType.Landed, new Vector2(9f, 1.45f),
                new Dictionary<string, object> { { "fallSpeed", 5.3f } })));

            for (int i = 1; i <= 5; i++)                     // 1.5–2.0 idle
                Sample(1.5f + i * 0.1f, 9f, 1.45f, 0f, 0f, 1);

            f.WriteLine(w.EventLine(new QAEvent(session.SessionId, seq++, 2.0f, 120,
                QAEventType.SessionEnded, null, null)));
        }

        /// <summary>Replay: right 30f → left 20f (via 5 zero frames) → right 10f,
        /// with jumpPressed on exactly 2 frames ⇒ directionChanges = 2.</summary>
        private void WriteReplay()
        {
            var frames = new List<ReplayFrame>();
            void Add(int n, float h, bool press = false)
            {
                for (int i = 0; i < n; i++)
                    frames.Add(new ReplayFrame
                    {
                        frameNumber = frames.Count,
                        timestamp = frames.Count * 0.016f,
                        horizontal = h,
                        jumpPressed = press && i == 0,
                        jumpHeld = press && i < 3
                    });
            }
            Add(30, 1f, press: true);
            Add(5, 0f);
            Add(20, -1f);
            Add(10, 1f, press: true);
            ReplayFileStore.Save(ReplayRecording.Create(session.SessionId, "t0", frames), folder);
        }

        private void WriteValidation(string verdict = "PASS", float maxDev = 0.14f) =>
            File.WriteAllText(Path.Combine(folder, "validation.json"),
                JsonUtility.ToJson(new ReplayValidationResult
                {
                    schemaVersion = 1,
                    originalSessionId = session.SessionId,
                    verdict = verdict,
                    maxDeviation = maxDev
                }, true));

        private SessionFeatures ExtractAll()
        {
            WriteManifest();
            WriteEvents();
            WriteReplay();
            WriteValidation();
            return FeatureExtractor.Extract(folder);
        }

        // ------------------------------------------------------------ tests

        [Test]
        public void Identity_AndSessionFacts_PassThrough()
        {
            var f = ExtractAll();
            Assert.AreEqual(SessionFeatures.CurrentSchemaVersion, f.schemaVersion);
            Assert.AreEqual(session.SessionId, f.sessionId);
            Assert.AreEqual("FeatureLevel", f.level);
            Assert.AreEqual(10f, f.sessionDurationSec, 1e-3f);
            Assert.AreEqual("closed", f.sessionStatus);
        }

        [Test]
        public void TrajectoryFeatures_MatchHandComputedValues()
        {
            var f = ExtractAll();
            Assert.IsTrue(f.eventsAvailable);
            Assert.AreEqual(21, f.trajectorySamples);
            Assert.AreEqual(2.0f, f.trajectoryDurationSec, 1e-3f);
            // Exact hand-computed path length (corrected expectation — the
            // original "≈9 ± 0.6" underestimated the jump arc's contribution;
            // the extractor was right and the test was wrong):
            //   phase 1 (run):  6.000
            //   phase 2 (arc):  0.8399 + 0.7014 + 0.6 + 0.7014 + 0.8399 = 3.6826
            //                   (segments √(0.6² + Δy²), Δy from y = 1.45 + sin(i/5·π))
            //   phase 3 (idle): 0
            //   total:          9.6826   (±0.01 absorbs 3-decimal wire rounding)
            Assert.AreEqual(9.6826f, f.totalDistance, 0.01f, "exact path-length integral of the synthetic arc");
            Assert.GreaterOrEqual(f.totalDistance, 9.0f - 1e-3f, "never less than straight-line x");
            Assert.AreEqual(8f, f.maxSpeed, 0.05f, "peak |v| = √(6²+5.29²) ≈ 8");
            Assert.AreEqual(f.totalDistance / 2.0f, f.averageSpeed, 1e-3f, "avg = dist/duration");
        }

        [Test]
        public void AirtimeAndIdle_AttributedByIntervalEndState()
        {
            var f = ExtractAll();
            Assert.AreEqual(0.5f, f.airtimeSec, 0.02f);
            Assert.AreEqual(0.5f, f.idleTimeSec, 0.02f);
            Assert.AreEqual(0.25f, f.airtimeFraction, 0.02f);
            Assert.AreEqual(0.25f, f.idleFraction, 0.02f);
        }

        [Test]
        public void EventCounts_JumpsLandsAndUnavailableMechanics()
        {
            var f = ExtractAll();
            Assert.AreEqual(1, f.jumpCount);
            Assert.AreEqual(1, f.landedCount);
            Assert.AreEqual(0, f.deaths, "no death mechanic in the GUT yet — honest zero");
            Assert.AreEqual(0, f.checkpointsReached);
            Assert.AreEqual(0, f.tokensCollected);
        }

        [Test]
        public void InputFeatures_DirectionChangesAndJumpPresses()
        {
            var f = ExtractAll();
            Assert.IsTrue(f.replayAvailable);
            Assert.AreEqual(65, f.replayFrameCount);
            Assert.AreEqual(2, f.directionChanges, "+1→−1→+1 with transparent zeros = 2");
            Assert.AreEqual(2, f.inputJumpPresses);
        }

        [Test]
        public void ValidationPassthrough_VerdictAndDeviation()
        {
            var f = ExtractAll();
            Assert.IsTrue(f.validationAvailable);
            Assert.AreEqual("PASS", f.validationVerdict);
            Assert.AreEqual(0.14f, f.validationMaxDeviation, 1e-4f);
        }

        [Test]
        public void MissingReplay_ZeroesInputGroup_OthersUnaffected()
        {
            WriteManifest();
            WriteEvents();
            var f = FeatureExtractor.Extract(folder);
            Assert.IsFalse(f.replayAvailable);
            Assert.AreEqual(0, f.directionChanges);
            Assert.IsTrue(f.eventsAvailable, "trajectory features must survive a missing replay");
            Assert.Greater(f.totalDistance, 8f);
        }

        [Test]
        public void MissingEvents_ZeroesTrajectoryGroup_NeverThrows()
        {
            WriteManifest();
            WriteReplay();
            var f = FeatureExtractor.Extract(folder);
            Assert.IsFalse(f.eventsAvailable);
            Assert.AreEqual(0, f.trajectorySamples);
            Assert.AreEqual(0f, f.totalDistance);
            Assert.IsTrue(f.replayAvailable, "input features must survive missing telemetry");
        }

        [Test]
        public void MissingFolder_ReturnsNull_WithSingleError()
        {
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex("folder not found"));
            Assert.IsNull(FeatureExtractor.Extract(Path.Combine(folder, "nope")));
        }

        [Test]
        public void Extraction_IsDeterministic_ExceptTimestamp()
        {
            WriteManifest(); WriteEvents(); WriteReplay(); WriteValidation();
            var a = FeatureExtractor.Extract(folder);
            var b = FeatureExtractor.Extract(folder);
            a.extractedUtc = b.extractedUtc = ""; // the sole sanctioned difference
            Assert.AreEqual(JsonUtility.ToJson(a), JsonUtility.ToJson(b),
                "identical inputs must yield identical feature values");
        }

        [Test]
        public void Store_RoundTrips_IntoSessionFolder()
        {
            var f = ExtractAll();
            string path = FeatureStore.Save(f, folder);
            Assert.AreEqual(Path.Combine(folder, FeatureStore.FileName), path);
            var back = FeatureStore.Load(folder);
            Assert.IsNotNull(back);
            Assert.AreEqual(f.sessionId, back.sessionId);
            Assert.AreEqual(f.totalDistance, back.totalDistance, 1e-4f);
            Assert.AreEqual(f.directionChanges, back.directionChanges);
        }

        [Test]
        public void TrajectoryReader_CapturesKinematics_Additively()
        {
            WriteEvents();
            var traj = SessionTrajectory.Load(Path.Combine(folder, "events.jsonl"));
            Assert.AreEqual(6f, traj.Samples[5].vx, 1e-3f);
            Assert.AreEqual(1, traj.Samples[5].g);
            Assert.AreEqual(0, traj.Samples[13].g, "airborne samples must read g=0");
        }
    }
}
