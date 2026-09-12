// -----------------------------------------------------------------------------
// UnityQA Tests — CoreOracleTests.cs                        (M5 Slice D tests)
//
// The three concrete oracles, judged branch by branch against hand-built
// contexts (pure — no files except the outcome-reader pin, which uses the
// real JsonLineWriter so parser and producer can never drift).
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityQA.Adapters;
using UnityQA.Core;
using UnityQA.Logging;
using UnityQA.Oracles;
using UnityQA.Replay;

namespace UnityQA.Tests
{
    public sealed class CoreOracleTests
    {
        private static OracleContext Ctx(string outcome = null, bool hasReplay = false,
                                         ReplayValidationResult validation = null)
        {
            return new OracleContext
            {
                SessionId = "s1",
                RunOutcome = outcome,
                Metadata = new ReplayMetadata { sessionId = "s1", hasReplay = hasReplay },
                Validation = validation
            };
        }

        private static ReplayValidationResult Validation(string verdict, float max = 0.1f, float mean = 0.05f) =>
            new ReplayValidationResult
            {
                verdict = verdict,
                maxDeviation = max,
                meanDeviation = mean,
                thresholdUnits = 0.75f,
                validationSessionId = "v1",
                firstDivergenceTime = verdict == ReplayValidationResult.VerdictFail ? 1.5f : -1f
            };

        // -------------------------------------------- ReplayConsistencyOracle

        [Test]
        public void ReplayConsistency_Pass()
        {
            var r = new ReplayConsistencyOracle().Evaluate(
                Ctx(hasReplay: true, validation: Validation(ReplayValidationResult.VerdictPass)));
            Assert.IsNotNull(r);
            Assert.IsTrue(r.passed);
            Assert.AreEqual(OracleResult.SeverityInfo, r.severity);
            CollectionAssert.Contains(r.evidence, "validationVerdict=PASS");
            CollectionAssert.Contains(r.evidence, "maxDeviation=0.1");
            CollectionAssert.Contains(r.evidence, "sessionId=s1");
        }

        [Test]
        public void ReplayConsistency_Fail_WithDeviationEvidence()
        {
            var r = new ReplayConsistencyOracle().Evaluate(
                Ctx(hasReplay: true, validation: Validation(ReplayValidationResult.VerdictFail, max: 3.2f, mean: 1.1f)));
            Assert.IsFalse(r.passed);
            Assert.AreEqual(OracleResult.SeverityWarning, r.severity,
                "replay infidelity is an apparatus problem, not a gameplay defect");
            StringAssert.Contains("diverged", r.reason);
            CollectionAssert.Contains(r.evidence, "maxDeviation=3.2");
            CollectionAssert.Contains(r.evidence, "meanDeviation=1.1");
        }

        [Test]
        public void ReplayConsistency_Skips_NoReplay_NoValidation_AndInvalid()
        {
            var oracle = new ReplayConsistencyOracle();
            Assert.IsNull(oracle.Evaluate(Ctx(hasReplay: false)), "no replay → skip");
            Assert.IsNull(oracle.Evaluate(Ctx(hasReplay: true, validation: null)), "never validated → skip");
            Assert.IsNull(oracle.Evaluate(Ctx(hasReplay: true,
                validation: Validation(ReplayValidationResult.VerdictInvalid))), "INVALID → skip");
        }

        // ---------------------------------------------------- CompletionOracle

        [Test]
        public void Completion_Success_Passes()
        {
            var r = new CompletionOracle().Evaluate(Ctx("Success"));
            Assert.IsTrue(r.passed);
            CollectionAssert.Contains(r.evidence, "sessionOutcome=Success");
            CollectionAssert.Contains(r.evidence, "doorReached=true");
        }

        [TestCase("SpikeDeath")]
        [TestCase("OutOfBounds")]
        [TestCase("Quit")]
        public void Completion_NonSuccess_Fails(string outcome)
        {
            var r = new CompletionOracle().Evaluate(Ctx(outcome));
            Assert.IsFalse(r.passed);
            Assert.AreEqual(OracleResult.SeverityWarning, r.severity);
            StringAssert.Contains(outcome, r.reason);
            CollectionAssert.Contains(r.evidence, "doorReached=false");
        }

        [Test]
        public void Completion_NoRecordedOutcome_Skips()
        {
            Assert.IsNull(new CompletionOracle().Evaluate(Ctx(outcome: null)));
        }

        // ------------------------------------------------------- HazardOracle

        [Test]
        public void Hazard_Success_PassesWithNoHazard()
        {
            var r = new HazardOracle().Evaluate(Ctx("Success"));
            Assert.IsTrue(r.passed);
            CollectionAssert.Contains(r.evidence, "hazardType=none");
        }

        [Test]
        public void Hazard_SpikeDeath_FailsAsWarning()
        {
            var r = new HazardOracle().Evaluate(Ctx("SpikeDeath"));
            Assert.IsFalse(r.passed);
            Assert.AreEqual(OracleResult.SeverityWarning, r.severity, "designed hazard = warning");
            StringAssert.Contains("Spike Hazard", r.reason);
            CollectionAssert.Contains(r.evidence, "hazardType=spike");
        }

        [Test]
        public void Hazard_OutOfBounds_FailsAsCritical()
        {
            var r = new HazardOracle().Evaluate(Ctx("OutOfBounds"));
            Assert.IsFalse(r.passed);
            Assert.AreEqual(OracleResult.SeverityCritical, r.severity,
                "leaving the playable space is the defect class QA exists for");
            StringAssert.Contains("Out Of Bounds", r.reason);
            CollectionAssert.Contains(r.evidence, "hazardType=outOfBounds");
        }

        [Test]
        public void Hazard_QuitAndMissingAndUnknown_Skip()
        {
            var oracle = new HazardOracle();
            Assert.IsNull(oracle.Evaluate(Ctx("Quit")), "manual quit attributes nothing");
            Assert.IsNull(oracle.Evaluate(Ctx(outcome: null)));
            Assert.IsNull(oracle.Evaluate(Ctx("SomeFutureOutcome")), "unknown names skip, never guess");
        }

        // -------------------------------------------- registry & runner wiring

        [Test]
        public void ReplayManager_RegistersExactlyTheFiveOracles()
        {
            var go = new GameObject("[QA-RegTest]");
            try
            {
                var manager = go.AddComponent<ReplayManager>();
                OracleRegistry reg = manager.OracleRegistry;
                // Exact registry, no placeholders: the M5.D core three plus the
                // two M6.B planted-defect detectors, in registration order.
                Assert.AreEqual(5, reg.Count, "exactly five oracles, no placeholders");
                Assert.AreEqual("ReplayConsistency", reg.Oracles[0].Name);
                Assert.AreEqual("Completion", reg.Oracles[1].Name);
                Assert.AreEqual("Hazard", reg.Oracles[2].Name);
                Assert.AreEqual("SoftLock", reg.Oracles[3].Name);
                Assert.AreEqual("MissingTrigger", reg.Oracles[4].Name);
                Assert.AreEqual(5, reg.EnabledCount);
                Assert.AreSame(reg, manager.OracleRegistry, "registry is created once");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void Runner_ThreeOracles_JudgeMixedSessionsCorrectly()
        {
            var reg = new OracleRegistry();
            reg.Register(new ReplayConsistencyOracle());
            reg.Register(new CompletionOracle());
            reg.Register(new HazardOracle());

            var contexts = new List<OracleContext>
            {
                Ctx("Success", hasReplay: true, validation: Validation(ReplayValidationResult.VerdictPass)),
                Ctx("SpikeDeath"),   // no replay/validation → RCO skips
                Ctx("Quit"),         // Completion fails, Hazard skips
            };
            OracleRunResults run = OracleRunner.Run(reg, contexts);

            // Session 1: 3 passes. Session 2: RCO skip, Completion fail, Hazard fail.
            // Session 3: RCO skip, Completion fail, Hazard skip.
            Assert.AreEqual(3, run.passedCount);
            Assert.AreEqual(3, run.failedCount);
            Assert.AreEqual(3, run.skippedCount);
            Assert.AreEqual(0, run.errorCount);
            Assert.AreEqual(6, run.executedEvaluations);
        }

        // -------------------------------------------------- outcome reader pin

        [Test]
        public void ReadLastRunOutcome_ParsesRealWriterOutput_LastWins()
        {
            string dir = Directory.CreateDirectory(Path.Combine(
                Path.GetTempPath(), "unityqa-test-" + Guid.NewGuid().ToString("N"))).FullName;
            try
            {
                string path = Path.Combine(dir, "events.jsonl");
                var session = new QASessionInfo("L", () => 0f, DateTime.UtcNow, "u", "a");
                var w = new JsonLineWriter();
                using (var f = new StreamWriter(path))
                {
                    f.WriteLine(w.HeaderLine("events", session));
                    f.WriteLine(w.EventLine(new QAEvent(session.SessionId, 0, 1f, 0,
                        QAEventType.RunEnded, null,
                        new Dictionary<string, object> { { "outcome", "SpikeDeath" } })));
                    f.WriteLine(w.EventLine(new QAEvent(session.SessionId, 1, 2f, 0,
                        QAEventType.PlayerSample, new Vector2(1f, 1f), null)));
                    f.WriteLine(w.EventLine(new QAEvent(session.SessionId, 2, 3f, 0,
                        QAEventType.RunEnded, null,
                        new Dictionary<string, object> { { "outcome", "Success" } })));
                }

                Assert.AreEqual("Success", OracleContextFactory.ReadLastRunOutcome(path),
                    "multi-run session: the LAST RunEnded is the session's outcome");
                Assert.IsNull(OracleContextFactory.ReadLastRunOutcome(Path.Combine(dir, "nope.jsonl")));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }
    }
}
