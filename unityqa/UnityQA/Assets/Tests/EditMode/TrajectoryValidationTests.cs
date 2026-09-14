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

        // ----------------------- constant-offset alignment (M6.D) -------------

        [Test]
        public void Alignment_IdenticalTrajectory_ZeroOffset_Pass()
        {
            var a = Line(0f, 20, 0.1f, i => new Vector2(i * 0.6f, 1.5f));
            var b = Line(0f, 20, 0.1f, i => new Vector2(i * 0.6f, 1.5f));

            var r = TrajectoryComparer.Compare(a, b, 0.75f);
            Assert.AreEqual(ReplayValidationResult.VerdictPass, r.verdict);
            Assert.AreEqual(0f, r.alignmentOffsetSec, 1e-4f, "no offset needed for identical input");
            Assert.AreEqual(0f, r.maxDeviation, 1e-4f);
        }

        [Test]
        public void Alignment_ConstantPhaseLead_PassAfterAlignment()
        {
            // Same spatial path; the replay runs a constant 0.30 s AHEAD (a
            // frame-domain playback phase lead). Both share the spawn-idle prefix
            // real runs have, so the window edges compare idle-to-idle.
            Func<float, Vector2> path = tt =>
                new Vector2(tt <= 0.6f ? 2f : 2f + 6f * (tt - 0.6f), 1.5f);
            var a = Line(0f, 40, 0.1f, i => path(i * 0.1f));
            var b = Line(0f, 40, 0.1f, i => path(i * 0.1f + 0.30f)); // 0.30 s ahead

            var r = TrajectoryComparer.Compare(a, b, 0.75f);
            Assert.AreEqual(ReplayValidationResult.VerdictPass, r.verdict,
                "a constant temporal phase lead must not read as spatial divergence");
            Assert.AreEqual(-0.30f, r.alignmentOffsetSec, 0.04f, "recovers the ~0.30 s lead");
            Assert.Less(r.maxDeviation, 0.1f, "aligned deviation collapses to ~0");
        }

        [Test]
        public void Alignment_SpatialDisplacement_FailsEvenAfterAlignment()
        {
            // A constant 1.0 u VERTICAL displacement is spatial, not temporal —
            // no time-shift can remove it, so the run must still FAIL.
            var a = Line(0f, 25, 0.1f, i => new Vector2(i * 0.6f, 1.5f));
            var b = Line(0f, 25, 0.1f, i => new Vector2(i * 0.6f, 2.5f));

            var r = TrajectoryComparer.Compare(a, b, 0.75f);
            Assert.AreEqual(ReplayValidationResult.VerdictFail, r.verdict);
            Assert.AreEqual(0f, r.alignmentOffsetSec, 1e-4f, "shifting time cannot help a vertical offset");
            Assert.AreEqual(1.0f, r.maxDeviation, 1e-3f);
        }

        [Test]
        public void Alignment_DifferentRoute_SameTiming_Fails()
        {
            // Same x-progression and timing, but the replay takes a different
            // route: a 2 u vertical bump mid-run. No constant shift removes a
            // localized spatial excursion without misaligning the rest, so the
            // min-MEAN search keeps offset 0 and the run FAILS.
            var a = Line(0f, 30, 0.1f, i => new Vector2(i * 0.6f, 1.5f));
            var b = Line(0f, 30, 0.1f, i =>
            {
                float t = i * 0.1f;
                float y = 1.5f + (t >= 1.0f && t <= 1.5f ? 2.0f : 0f);
                return new Vector2(i * 0.6f, y);
            });

            var r = TrajectoryComparer.Compare(a, b, 0.75f);
            Assert.AreEqual(ReplayValidationResult.VerdictFail, r.verdict);
            Assert.AreEqual(0f, r.alignmentOffsetSec, 1e-4f, "min-MEAN keeps the offset at 0 for a real divergence");
            Assert.Greater(r.maxDeviation, 1.5f);
        }

        [Test]
        public void Alignment_PB004Signature_RawLargeAlignedSmall_Pass()
        {
            // The PB-004 evidence in miniature: the replay follows the same route
            // (idle -> run -> jump) but a constant ~0.30 s ahead, plus a small
            // residual fidelity error. RAW deviation is large (~3 u, during the
            // jump); after de-phasing it collapses under threshold -> PASS, so
            // MissingTriggerOracle can see verdict == PASS.
            var a = Line(0f, 40, 0.1f, i => Golden(i * 0.1f));
            var b = Line(0f, 40, 0.1f, i =>
            {
                Vector2 g = Golden(i * 0.1f + 0.30f);   // 0.30 s ahead
                return new Vector2(g.x, g.y + 0.30f);   // + constant residual
            });

            // raw (index-aligned == offset-0) deviation is large.
            float rawMax = 0f;
            for (int i = 0; i < a.Count; i++)
            {
                float dx = a[i].x - b[i].x, dy = a[i].y - b[i].y;
                rawMax = Mathf.Max(rawMax, Mathf.Sqrt(dx * dx + dy * dy));
            }
            Assert.Greater(rawMax, 2.5f, "raw phase-offset deviation is large (~3 u)");

            var r = TrajectoryComparer.Compare(a, b, 0.75f);
            Assert.AreEqual(ReplayValidationResult.VerdictPass, r.verdict,
                "a spatially faithful replay with a constant phase lead must PASS");
            Assert.AreEqual(-0.30f, r.alignmentOffsetSec, 0.04f, "recovers the ~0.30 s constant lead");
            Assert.Less(r.maxDeviation, 0.5f, "aligned deviation is small (~0.3 u)");
            Assert.Greater(r.maxDeviation, 0.1f, "the constant residual is NOT hidden");
            Assert.AreEqual(0f, r.durationDelta, 1e-3f, "raw timing discrepancy preserved separately");
        }

        /// <summary>PB-004-shaped reference path: 0.5 s spawn idle, run right at
        /// 6 u/s, one jump (parabolic apex 2.2 u) at t in [1.2, 1.8].</summary>
        private static Vector2 Golden(float tt)
        {
            float x = tt <= 0.5f ? 2f : 2f + 6f * (tt - 0.5f);
            float y = 1.5f;
            if (tt >= 1.2f && tt <= 1.8f)
            {
                float u = (tt - 1.2f) / 0.6f;
                y = 1.5f + 2.2f * 4f * u * (1f - u);
            }
            return new Vector2(x, y);
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

        // ----------------------- outcome comparison (M5.D stabilization) ------

        private static ReplayValidationResult PassResult() => new ReplayValidationResult
        { verdict = ReplayValidationResult.VerdictPass };

        [Test]
        public void Outcomes_MatchingSuccess_KeepsPass()
        {
            var r = PassResult();
            TrajectoryComparer.ApplyOutcomes(r, "Success", "Success");
            Assert.IsTrue(r.outcomesCompared);
            Assert.IsTrue(r.outcomeMatch);
            Assert.AreEqual(ReplayValidationResult.VerdictPass, r.verdict);
        }

        [Test]
        public void Outcomes_Mismatch_DowngradesPassToFail()
        {
            var r = PassResult();
            TrajectoryComparer.ApplyOutcomes(r, "Success", "OutOfBounds");
            Assert.IsTrue(r.outcomesCompared);
            Assert.IsFalse(r.outcomeMatch);
            Assert.AreEqual(ReplayValidationResult.VerdictFail, r.verdict,
                "same path within tolerance but a different ending is not a reproduced run");
        }

        [Test]
        public void Outcomes_MatchingFailureOutcome_IsAMatch()
        {
            var r = PassResult();
            TrajectoryComparer.ApplyOutcomes(r, "SpikeDeath", "SpikeDeath");
            Assert.IsTrue(r.outcomeMatch, "a faithfully reproduced spike death is a MATCH");
            Assert.AreEqual(ReplayValidationResult.VerdictPass, r.verdict);
        }

        [Test]
        public void Outcomes_MissingEitherSide_NeverChangesVerdict()
        {
            var r1 = PassResult();
            TrajectoryComparer.ApplyOutcomes(r1, null, "Success");
            Assert.IsFalse(r1.outcomesCompared);
            Assert.IsFalse(r1.outcomeMatch);
            Assert.AreEqual(ReplayValidationResult.VerdictPass, r1.verdict);
            Assert.AreEqual("", r1.originalOutcome, "null normalizes to empty for the wire format");

            var r2 = PassResult();
            TrajectoryComparer.ApplyOutcomes(r2, "Success", "");
            Assert.IsFalse(r2.outcomesCompared);
            Assert.AreEqual(ReplayValidationResult.VerdictPass, r2.verdict);
        }

        [Test]
        public void Outcomes_QuitOriginal_IsNotComparable()
        {
            var r = PassResult();
            TrajectoryComparer.ApplyOutcomes(r, "Quit", "OutOfBounds");
            Assert.IsFalse(r.outcomesCompared,
                "Escape is not part of the input seam — a quit is not replayable");
            Assert.AreEqual(ReplayValidationResult.VerdictPass, r.verdict);
        }

        [Test]
        public void Outcomes_TrajectoryFail_StaysFail_EvenOnMatch()
        {
            var r = new ReplayValidationResult { verdict = ReplayValidationResult.VerdictFail };
            TrajectoryComparer.ApplyOutcomes(r, "Success", "Success");
            Assert.IsTrue(r.outcomeMatch);
            Assert.AreEqual(ReplayValidationResult.VerdictFail, r.verdict,
                "matching outcomes never excuse a trajectory divergence");
        }

        [Test]
        public void Outcomes_SerializationRoundTrips()
        {
            var r = PassResult();
            TrajectoryComparer.ApplyOutcomes(r, "Success", "SpikeDeath");
            var back = JsonUtility.FromJson<ReplayValidationResult>(JsonUtility.ToJson(r, true));
            Assert.AreEqual("Success", back.originalOutcome);
            Assert.AreEqual("SpikeDeath", back.replayOutcome);
            Assert.IsTrue(back.outcomesCompared);
            Assert.IsFalse(back.outcomeMatch);
            Assert.AreEqual(ReplayValidationResult.VerdictFail, back.verdict);
        }
    }
}
