// -----------------------------------------------------------------------------
// UnityQA Tests — TrajectoryValidationTests.cs              (M3 Slice C tests)
//
// Pins the Slice C mathematics and parsing. The reader test generates its
// input with the REAL JsonLineWriter — so if the event wire format and the
// trajectory parser ever drift apart, this suite fails before any replay
// validation silently reads garbage.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityQA.Core;
using UnityQA.Logging;
using UnityQA.Replay;

namespace UnityQA.Tests
{
    public sealed class TrajectoryValidationTests
    {
        private static List<TrajectorySample> Line(float t0, int n, float dt,
                                                   Func<int, Vector2> pos)
        {
            var list = new List<TrajectorySample>(n);
            for (int i = 0; i < n; i++)
            {
                Vector2 p = pos(i);
                list.Add(new TrajectorySample { t = t0 + i * dt, x = p.x, y = p.y });
            }
            return list;
        }

        // ------------------------------ comparer ------------------------------

        [Test]
        public void IdenticalTrajectories_PassWithZeroDeviation()
        {
            var a = Line(0f, 20, 0.1f, i => new Vector2(i * 0.6f, 1.5f));
            var b = Line(0f, 20, 0.1f, i => new Vector2(i * 0.6f, 1.5f));

            var r = TrajectoryComparer.Compare(a, b, 0.75f);
            Assert.AreEqual(ReplayValidationResult.VerdictPass, r.verdict);
            Assert.AreEqual(0f, r.maxDeviation, 1e-4f);
            Assert.AreEqual(-1f, r.firstDivergenceTime);
            Assert.AreEqual(20, r.comparedSamples);
        }

        [Test]
        public void TimeShiftedStart_IsNormalizedAway()
        {
            // Same motion, but the validation session's first sample came 0.4 s
            // later on its own clock — normalization must hide that entirely.
            var a = Line(0.1f, 20, 0.1f, i => new Vector2(i * 0.6f, 1.5f));
            var b = Line(0.5f, 20, 0.1f, i => new Vector2(i * 0.6f, 1.5f));

            var r = TrajectoryComparer.Compare(a, b, 0.75f);
            Assert.AreEqual(ReplayValidationResult.VerdictPass, r.verdict);
            Assert.Less(r.maxDeviation, 1e-3f);
        }

        [Test]
        public void ConstantOffset_ReportsThatOffset_AndFails()
        {
            var a = Line(0f, 20, 0.1f, i => new Vector2(i * 0.6f, 1.5f));
            var b = Line(0f, 20, 0.1f, i => new Vector2(i * 0.6f, 1.5f + 2f)); // 2u above

            var r = TrajectoryComparer.Compare(a, b, 0.75f);
            Assert.AreEqual(ReplayValidationResult.VerdictFail, r.verdict);
            Assert.AreEqual(2f, r.maxDeviation, 1e-3f);
            Assert.AreEqual(2f, r.meanDeviation, 1e-3f);
            Assert.AreEqual(0f, r.firstDivergenceTime, 1e-3f, "diverged from the very start");
        }

        [Test]
        public void LateSpike_SetsFirstDivergenceTime()
        {
            var a = Line(0f, 30, 0.1f, i => new Vector2(i * 0.6f, 1.5f));
            var b = Line(0f, 30, 0.1f, i =>
                new Vector2(i * 0.6f + (i >= 20 ? 3f : 0f), 1.5f)); // veers off at t=2.0

            var r = TrajectoryComparer.Compare(a, b, 0.75f);
            Assert.AreEqual(ReplayValidationResult.VerdictFail, r.verdict);
            Assert.AreEqual(2.0f, r.firstDivergenceTime, 0.15f);
            Assert.AreEqual(3f, r.maxDeviation, 0.2f);
        }

        [Test]
        public void DifferentSampleRates_CompareViaInterpolation()
        {
            // Same straight-line motion sampled at 10 Hz vs 7 Hz: interpolation
            // must see (almost) no deviation despite zero timestamp overlap.
            var a = Line(0f, 21, 0.1f, i => new Vector2(i * 0.6f, 1.5f));          // 2.0 s @10 Hz
            var b = Line(0f, 15, 1f / 7f, i => new Vector2(i * (0.6f / 0.7f), 1.5f)); // 2.0 s @7 Hz

            var r = TrajectoryComparer.Compare(a, b, 0.75f);
            Assert.AreEqual(ReplayValidationResult.VerdictPass, r.verdict);
            Assert.Less(r.maxDeviation, 0.05f);
        }

        [Test]
        public void ShorterValidationRun_LimitsWindow_AndReportsDurationDelta()
        {
            var a = Line(0f, 40, 0.1f, i => new Vector2(i * 0.6f, 1.5f)); // 3.9 s
            var b = Line(0f, 20, 0.1f, i => new Vector2(i * 0.6f, 1.5f)); // 1.9 s

            var r = TrajectoryComparer.Compare(a, b, 0.75f);
            Assert.AreEqual(ReplayValidationResult.VerdictPass, r.verdict, "identical inside window");
            Assert.AreEqual(2.0f, r.durationDelta, 1e-3f);
            Assert.LessOrEqual(r.comparedSamples, 20, "comparison must stop at the shared window");
        }

        [Test]
        public void TooFewSamples_IsInvalid_NeverThrows()
        {
            var one = Line(0f, 1, 0.1f, i => Vector2.zero);
            var many = Line(0f, 10, 0.1f, i => Vector2.zero);
            Assert.AreEqual(ReplayValidationResult.VerdictInvalid,
                TrajectoryComparer.Compare(one, many, 0.75f).verdict);
            Assert.AreEqual(ReplayValidationResult.VerdictInvalid,
                TrajectoryComparer.Compare(null, many, 0.75f).verdict);
        }

        // ------------------------------ reader --------------------------------

        [Test]
        public void Reader_ParsesRealWriterOutput_AndIgnoresOtherLines()
        {
            string dir = Directory.CreateDirectory(Path.Combine(
                Path.GetTempPath(), "unityqa-test-" + Guid.NewGuid().ToString("N"))).FullName;
            try
            {
                var session = new QASessionInfo("TrajLevel", () => 0f, DateTime.UtcNow, "u", "a");
                var w = new JsonLineWriter();
                string path = Path.Combine(dir, "events.jsonl");
                using (var f = new StreamWriter(path))
                {
                    f.WriteLine(w.HeaderLine("events", session));
                    // Non-sample noise the reader must skip:
                    f.WriteLine(w.EventLine(new QAEvent(session.SessionId, 0, 0f, 0,
                        QAEventType.SessionStarted, null, null)));
                    // Real samples — payload deliberately contains vx/vy to prove
                    // the quoted anchors cannot mismatch:
                    for (int i = 0; i < 5; i++)
                    {
                        var payload = new Dictionary<string, object>
                            { { "vx", 6f }, { "vy", -1.5f }, { "mx", 1 } };
                        f.WriteLine(w.EventLine(new QAEvent(session.SessionId, i + 1,
                            0.5f + i * 0.1f, i, QAEventType.PlayerSample,
                            new Vector2(2f + i * 0.6f, 1.45f), payload)));
                    }
                    f.WriteLine(w.EventLine(new QAEvent(session.SessionId, 6, 1.1f, 9,
                        QAEventType.SessionEnded, null, null)));
                }

                var traj = SessionTrajectory.Load(path);
                Assert.IsNotNull(traj);
                Assert.AreEqual(session.SessionId, traj.SessionId, "header sessionId must be read");
                Assert.AreEqual(5, traj.Samples.Count);
                Assert.AreEqual(0, traj.ParseErrors);
                Assert.AreEqual(0.5f, traj.Samples[0].t, 1e-3f);
                Assert.AreEqual(2.0f, traj.Samples[0].x, 1e-3f);
                Assert.AreEqual(1.45f, traj.Samples[0].y, 1e-3f);
                Assert.AreEqual(4.4f, traj.Samples[4].x, 1e-3f, "vx payload must not pollute x");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Test]
        public void Result_SerializationRoundTrips()
        {
            var r = new ReplayValidationResult
            {
                schemaVersion = 1, originalSessionId = "o", validationSessionId = "v",
                thresholdUnits = 0.75f, comparedSamples = 42, maxDeviation = 0.12f,
                firstDivergenceTime = -1f, verdict = ReplayValidationResult.VerdictPass
            };
            var back = JsonUtility.FromJson<ReplayValidationResult>(JsonUtility.ToJson(r, true));
            Assert.AreEqual("o", back.originalSessionId);
            Assert.AreEqual(0.12f, back.maxDeviation, 1e-5f);
            Assert.AreEqual(ReplayValidationResult.VerdictPass, back.verdict);
        }
    }
}
