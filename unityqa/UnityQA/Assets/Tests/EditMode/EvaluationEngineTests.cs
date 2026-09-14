// -----------------------------------------------------------------------------
// UnityQA Tests — EvaluationEngineTests.cs                  (M6 Slice C tests)
//
// The evaluation aggregation, judged against hand-built campaigns + oracle
// verdicts (pure — no files). These pin the metrics the IEEE paper will quote:
// per-bug and overall detection rate, false-positive rate, indeterminate
// handling, repeat aggregation, consistency, timestamp-independent determinism,
// answer-key-not-bug-ID scoring, and stable serialization.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityQA.Evaluation;
using UnityQA.Oracles;

namespace UnityQA.Tests
{
    public sealed class EvaluationEngineTests
    {
        // ---- builders -------------------------------------------------------

        private static OracleResult Res(string oracle, string session, bool passed)
            => new OracleResult { oracleName = oracle, sessionId = session, passed = passed,
                                  severity = passed ? OracleResult.SeverityInfo : OracleResult.SeverityCritical };

        private static OracleRunResults Run(params OracleResult[] results)
        {
            var r = new OracleRunResults();
            r.results.AddRange(results);
            return r;
        }

        private static EvaluationCaseSpec Case(string id, string bugClass, bool clean,
                                               string[] expected, params string[] sessions)
            => new EvaluationCaseSpec
            {
                caseId = id, bugClass = bugClass, isClean = clean,
                expectedDetectors = new List<string>(expected ?? new string[0]),
                sessionIds = new List<string>(sessions)
            };

        private static EvaluationCampaign Campaign(int repeats, params EvaluationCaseSpec[] cases)
        {
            var c = new EvaluationCampaign { benchmarkId = "bench", repeats = repeats };
            c.cases.AddRange(cases);
            return c;
        }

        private static EvaluationCaseResult Find(EvaluationReport r, string caseId)
            => r.cases.Find(x => x.caseId == caseId);

        // ---- 1. all four bugs detected -> 100% -----------------------------

        [Test]
        public void AllFourBugsDetected_OverallRateIsOne()
        {
            var camp = Campaign(1,
                Case("PB-001", "Collider gap", false, new[] { "Hazard" }, "s1"),
                Case("PB-002", "Soft lock", false, new[] { "SoftLock" }, "s2"),
                Case("PB-003", "Spike on path", false, new[] { "Hazard" }, "s3"),
                Case("PB-004", "Missing trigger", false, new[] { "MissingTrigger" }, "s4"));
            var results = Run(
                Res("Hazard", "s1", passed: false),
                Res("SoftLock", "s2", passed: false),
                Res("Hazard", "s3", passed: false),
                Res("MissingTrigger", "s4", passed: false));

            var report = EvaluationEngine.Evaluate(camp, results);

            Assert.AreEqual(4, report.aggregate.plantedCaseCount);
            Assert.AreEqual(4, report.aggregate.totalDetected);
            Assert.AreEqual(0, report.aggregate.totalMissed);
            Assert.IsTrue(report.aggregate.overallDetectionRateAvailable);
            Assert.AreEqual(1.0f, report.aggregate.overallDetectionRate, 1e-6);
            foreach (var c in report.cases) Assert.AreEqual(1.0f, c.detectionRate, 1e-6);
        }

        // ---- 2. one missed detection ---------------------------------------

        [Test]
        public void OneMissed_PerBugAndOverallRatesCorrect()
        {
            var camp = Campaign(1,
                Case("PB-001", "Collider gap", false, new[] { "Hazard" }, "s1"),
                Case("PB-002", "Soft lock", false, new[] { "SoftLock" }, "s2"));
            // PB-002's SoftLock evaluated but PASSED (missed).
            var results = Run(
                Res("Hazard", "s1", passed: false),
                Res("SoftLock", "s2", passed: true));

            var report = EvaluationEngine.Evaluate(camp, results);

            Assert.AreEqual(1, Find(report, "PB-001").detected);
            Assert.AreEqual(0, Find(report, "PB-002").detected);
            Assert.AreEqual(1, Find(report, "PB-002").missed);
            Assert.AreEqual(0f, Find(report, "PB-002").detectionRate, 1e-6);
            Assert.AreEqual(0.5f, report.aggregate.overallDetectionRate, 1e-6);
        }

        // ---- 3. clean run -> no false positive -----------------------------

        [Test]
        public void CleanRun_NoFailures_NoFalsePositive()
        {
            var camp = Campaign(1, Case("clean", "Baseline", true, null, "sc"));
            // A clean golden run: every oracle passes (or skips → absent).
            var results = Run(
                Res("Completion", "sc", passed: true),
                Res("Hazard", "sc", passed: true));

            var report = EvaluationEngine.Evaluate(camp, results);

            Assert.AreEqual(1, report.aggregate.cleanRuns);
            Assert.AreEqual(0, report.aggregate.falsePositives);
            Assert.IsTrue(report.aggregate.falsePositiveRateAvailable);
            Assert.AreEqual(0f, report.aggregate.falsePositiveRate, 1e-6);
            Assert.IsFalse(Find(report, "clean").perRun[0].falsePositive);
        }

        [Test]
        public void CleanRun_AnyOracleFailure_IsFalsePositive()
        {
            var camp = Campaign(1, Case("clean", "Baseline", true, null, "sc"));
            var results = Run(Res("SoftLock", "sc", passed: false)); // wrongly fires on a clean run

            var report = EvaluationEngine.Evaluate(camp, results);

            Assert.AreEqual(1, report.aggregate.falsePositives);
            Assert.AreEqual(1.0f, report.aggregate.falsePositiveRate, 1e-6);
            CollectionAssert.Contains(Find(report, "clean").perRun[0].firingOracles, "SoftLock");
        }

        // ---- 4. repeated runs aggregate correctly --------------------------

        [Test]
        public void RepeatedRuns_AggregateAndConsistencyCorrect()
        {
            var camp = Campaign(3, Case("PB-002", "Soft lock", false, new[] { "SoftLock" }, "a", "b", "c"));
            var results = Run(
                Res("SoftLock", "a", passed: false),
                Res("SoftLock", "b", passed: false),
                Res("SoftLock", "c", passed: true)); // 2/3 detected → inconsistent

            var report = EvaluationEngine.Evaluate(camp, results);
            var c = Find(report, "PB-002");

            Assert.AreEqual(3, c.runs);
            Assert.AreEqual(3, c.evaluableRuns);
            Assert.AreEqual(2, c.detected);
            Assert.AreEqual(1, c.missed);
            Assert.AreEqual(2f / 3f, c.detectionRate, 1e-6);
            Assert.IsFalse(c.consistent, "2-of-3 detection is not consistent");
        }

        [Test]
        public void IndeterminateRun_NotCountedAsMissed()
        {
            // The expected detector SKIPPED (absent) on run "b" → indeterminate.
            var camp = Campaign(2, Case("PB-004", "Missing trigger", false, new[] { "MissingTrigger" }, "a", "b"));
            var results = Run(Res("MissingTrigger", "a", passed: false)); // nothing for "b"

            var report = EvaluationEngine.Evaluate(camp, results);
            var c = Find(report, "PB-004");

            Assert.AreEqual(1, c.detected);
            Assert.AreEqual(0, c.missed);
            Assert.AreEqual(1, c.indeterminate);
            Assert.AreEqual(1, c.evaluableRuns);
            Assert.AreEqual(1.0f, c.detectionRate, 1e-6, "rate is over EVALUABLE runs, not all runs");
            Assert.IsTrue(c.perRun[1].sessionResultsMissing);
        }

        // ---- 5. zero-run case -> safe, rate unavailable --------------------

        [Test]
        public void ZeroRuns_SafeAndRateUnavailable()
        {
            var camp = Campaign(0,
                Case("PB-001", "Collider gap", false, new[] { "Hazard" } /* no sessions */));
            var report = EvaluationEngine.Evaluate(camp, Run());

            var c = Find(report, "PB-001");
            Assert.AreEqual(0, c.runs);
            Assert.AreEqual(0, c.evaluableRuns);
            Assert.IsFalse(c.detectionRateAvailable, "no runs → rate is unavailable, not 0/0");
            Assert.IsFalse(report.aggregate.overallDetectionRateAvailable);
            Assert.IsFalse(report.aggregate.falsePositiveRateAvailable);
        }

        [Test]
        public void NullCampaign_YieldsEmptyValidReport()
        {
            var report = EvaluationEngine.Evaluate(null, Run());
            Assert.IsNotNull(report);
            Assert.AreEqual(0, report.cases.Count);
            Assert.IsFalse(report.aggregate.overallDetectionRateAvailable);
        }

        // ---- 6. determinism across timestamps / run ids --------------------

        [Test]
        public void Aggregation_IsDeterministic_IgnoringGeneratedUtc()
        {
            var camp = Campaign(2,
                Case("PB-002", "Soft lock", false, new[] { "SoftLock" }, "a", "b"),
                Case("clean", "Baseline", true, null, "sc"));
            var results = Run(
                Res("SoftLock", "a", passed: false),
                Res("SoftLock", "b", passed: false),
                Res("Completion", "sc", passed: true));

            EvaluationReport r1 = EvaluationEngine.Evaluate(camp, results);
            EvaluationReport r2 = EvaluationEngine.Evaluate(camp, results);

            // Null out the sole non-deterministic field, then the serialized
            // documents must be byte-identical.
            r1.generatedUtc = null; r2.generatedUtc = null;
            Assert.AreEqual(JsonUtility.ToJson(r1), JsonUtility.ToJson(r2));
            Assert.AreEqual(EvaluationStore.BuildCsv(r1), EvaluationStore.BuildCsv(r2));
        }

        // ---- 7. ground truth = answer key, not bug-ID shortcut -------------

        [Test]
        public void Detection_UsesExpectedDetectors_NotCaseId()
        {
            // Same caseId "PB-002", but the expected detector did NOT fire and a
            // DIFFERENT oracle did. Scoring must follow the answer key (expected
            // detector), so this is a MISS — not a detection off the bug id.
            var camp = Campaign(1, Case("PB-002", "Soft lock", false, new[] { "SoftLock" }, "s"));
            var results = Run(
                Res("SoftLock", "s", passed: true),        // expected detector: passed
                Res("Hazard", "s", passed: false));        // some other oracle fired

            var report = EvaluationEngine.Evaluate(camp, results);
            var c = Find(report, "PB-002");
            Assert.AreEqual(0, c.detected, "a non-expected oracle firing must not count as detecting this bug");
            Assert.AreEqual(1, c.missed);
        }

        // ---- 8. serialization is valid and stable --------------------------

        [Test]
        public void Report_RoundTrips_ThroughJsonUtility()
        {
            var camp = Campaign(1,
                Case("PB-001", "Collider gap", false, new[] { "Hazard" }, "s1"),
                Case("clean", "Baseline", true, null, "sc"));
            var results = Run(
                Res("Hazard", "s1", passed: false),
                Res("Completion", "sc", passed: true));

            EvaluationReport report = EvaluationEngine.Evaluate(camp, results);
            string json = JsonUtility.ToJson(report, prettyPrint: true);
            EvaluationReport back = JsonUtility.FromJson<EvaluationReport>(json);

            Assert.IsNotNull(back);
            Assert.AreEqual(report.cases.Count, back.cases.Count);
            Assert.AreEqual(report.aggregate.totalDetected, back.aggregate.totalDetected);
            Assert.AreEqual(report.aggregate.overallDetectionRate, back.aggregate.overallDetectionRate, 1e-6);
            StringAssert.Contains("PB-001", EvaluationStore.BuildCsv(report));
            StringAssert.Contains("OVERALL", EvaluationStore.BuildCsv(report));
        }
    }
}
