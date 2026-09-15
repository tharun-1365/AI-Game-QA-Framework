// -----------------------------------------------------------------------------
// UnityQA Tests — ReportingTests.cs                             (M7 Reporting)
//
// Pins the M7 presentation layer: the HTML report is a faithful, deterministic
// render of the persisted M6 evidence, it preserves the M6-D validation values
// verbatim, it escapes every inserted string, and a missing result fails loudly
// instead of producing a misleading page. The generator is pure (inputs +
// injected timestamp), so these tests build evidence in memory — no file I/O,
// no oracle, no re-scoring.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityQA.Evaluation;
using UnityQA.Replay;
using UnityQA.Reporting;

namespace UnityQA.Tests
{
    public sealed class ReportingTests
    {
        private const string Ts = "2026-01-01T00:00:00.0000000Z"; // fixed report timestamp

        private static EvaluationRunResult Run(string sid, string outcome, bool detected, string[] firing)
            => new EvaluationRunResult
            {
                runIndex = 0, sessionId = sid, sessionOutcome = outcome,
                detected = detected, falsePositive = false, evaluable = true,
                firingOracles = new List<string>(firing ?? new string[0]),
                sessionResultsMissing = false
            };

        private static EvaluationCaseResult Planted(string id, string cls, string det, string sid, string[] firing)
            => new EvaluationCaseResult
            {
                caseId = id, bugClass = cls, isClean = false,
                expectedDetectors = new List<string> { det },
                runs = 1, evaluableRuns = 1, detected = 1, missed = 0, indeterminate = 0,
                falsePositives = 0, detectionRate = 1f, detectionRateAvailable = true, consistent = true,
                perRun = new List<EvaluationRunResult> { Run(sid, "none", true, firing) }
            };

        /// <summary>An in-memory mirror of the current 4/4 + 0/5 benchmark result,
        /// with a schema-v3 validation.json attached to the PB-004 session.</summary>
        private static ReportInputs MakeInputs()
        {
            var report = new EvaluationReport
            {
                schemaVersion = 1,
                generatedUtc = "2026-09-14T09:12:52.9Z",
                benchmarkId = "Level_PlantedBugs_A vs Level_Benchmark",
                repeats = 5
            };
            report.cases.Add(Planted("PB-001", "Collider gap / fall-out-of-world", "Hazard", "sid-001", new[] { "Completion", "Hazard" }));
            report.cases.Add(Planted("PB-002", "Soft lock", "SoftLock", "sid-002", new[] { "SoftLock" }));
            report.cases.Add(Planted("PB-003", "Hazard on golden path (regression)", "Hazard", "sid-003", new[] { "Completion", "Hazard" }));
            report.cases.Add(Planted("PB-004", "Missing trigger", "MissingTrigger", "3db5bad8-val", new[] { "MissingTrigger" }));

            report.cases.Add(new EvaluationCaseResult
            {
                caseId = "clean", bugClass = "Baseline", isClean = true,
                expectedDetectors = new List<string>(),
                runs = 5, evaluableRuns = 5, detected = 0, missed = 0, indeterminate = 0,
                falsePositives = 0, detectionRate = 0f, detectionRateAvailable = false, consistent = true,
                perRun = new List<EvaluationRunResult>()
            });

            EvaluationAggregate a = report.aggregate;
            a.plantedCaseCount = 4; a.totalBugRuns = 4; a.totalEvaluableBugRuns = 4;
            a.totalDetected = 4; a.totalMissed = 0; a.totalIndeterminate = 0;
            a.overallDetectionRate = 1f; a.overallDetectionRateAvailable = true;
            a.cleanRuns = 5; a.falsePositives = 0; a.falsePositiveRate = 0f; a.falsePositiveRateAvailable = true;

            var inputs = new ReportInputs
            {
                report = report,
                evaluationJsonPath = "Q:/QAData/Reports/evaluation.json",
                campaignJsonPath = "Q:/QAData/Reports/evaluation-campaign.json",
                oracleResultsPath = "Q:/QAData/Sessions/oracle-results.json",
                sessionsRoot = "Q:/QAData/Sessions"
            };
            inputs.validations["3db5bad8-val"] = new ReplayValidationResult
            {
                schemaVersion = 3,
                originalSessionId = "orig-golden",
                validationSessionId = "3db5bad8-val",
                thresholdUnits = 0.75f,
                originalOutcome = "Success",
                replayOutcome = "",
                verdict = ReplayValidationResult.VerdictPass,
                maxDeviation = 0.346f, meanDeviation = 0.055f, rmsDeviation = 0.076f,
                firstDivergenceTime = -1f, durationDelta = 0.309f, alignmentOffsetSec = -0.30f
            };
            return inputs;
        }

        // ----------------------------------------------------------- tests

        [Test]
        public void Generate_ValidData_ProducesHtml()
        {
            string html = HtmlReportGenerator.Generate(MakeInputs(), Ts);
            Assert.IsNotNull(html);
            Assert.IsNotEmpty(html);
            StringAssert.Contains("<!DOCTYPE html>", html);
            StringAssert.Contains("</html>", html);
            StringAssert.Contains("<style>", html);              // inline CSS only
            StringAssert.DoesNotContain("<script", html);        // no JS at all
            StringAssert.DoesNotContain("http://", html);        // no network
            StringAssert.DoesNotContain("https://", html);
        }

        [Test]
        public void Report_ContainsHeadlineFacts()
        {
            string html = HtmlReportGenerator.Generate(MakeInputs(), Ts);
            StringAssert.Contains("Level_PlantedBugs_A vs Level_Benchmark", html); // benchmark id
            StringAssert.Contains("PB-001", html);
            StringAssert.Contains("PB-002", html);
            StringAssert.Contains("PB-003", html);
            StringAssert.Contains("PB-004", html);
            StringAssert.Contains("Clean control", html);
            StringAssert.Contains("100%", html);  // overall detection rate
            StringAssert.Contains("0%", html);     // false-positive rate
        }

        [Test]
        public void Report_PB004_MissingTrigger_DetectedConsistent()
        {
            string html = HtmlReportGenerator.Generate(MakeInputs(), Ts);
            // First occurrence of PB-004 is its per-case row (§3 precedes §6).
            int i = html.IndexOf("PB-004", StringComparison.Ordinal);
            Assert.Greater(i, 0);
            int rowStart = html.LastIndexOf("<tr", i, StringComparison.Ordinal);
            int rowEnd = html.IndexOf("</tr>", i, StringComparison.Ordinal);
            Assert.Greater(rowStart, 0);
            Assert.Greater(rowEnd, rowStart);
            string row = html.Substring(rowStart, rowEnd - rowStart);

            StringAssert.Contains("MissingTrigger", row);            // expected detector shown
            StringAssert.Contains("class=\"pass\"", row);            // detected (pass state)
            StringAssert.Contains("badge ok\">yes", row);            // consistency = true
        }

        [Test]
        public void Report_PreservesM6DValidationInfo()
        {
            string html = HtmlReportGenerator.Generate(MakeInputs(), Ts);
            // field names are named explicitly (not renamed)
            StringAssert.Contains("alignmentOffsetSec", html);
            StringAssert.Contains("durationDelta", html);
            // and the verbatim values are shown (not recomputed)
            StringAssert.Contains("0.346", html);   // maxDeviation
            StringAssert.Contains("0.309", html);   // raw durationDelta preserved
            StringAssert.Contains("-0.3", html);    // alignmentOffsetSec
            StringAssert.Contains("PASS", html);    // verdict
        }

        [Test]
        public void Report_EscapesInsertedStrings()
        {
            ReportInputs inputs = MakeInputs();
            inputs.report.benchmarkId = "Bench<script>alert(1)</script>&\"'end";
            string html = HtmlReportGenerator.Generate(inputs, Ts);

            StringAssert.DoesNotContain("<script>alert(1)", html); // raw injection gone
            StringAssert.Contains("&lt;script&gt;", html);
            StringAssert.Contains("&amp;", html);
            StringAssert.Contains("&quot;", html);
            StringAssert.Contains("&#39;", html);
        }

        [Test]
        public void Generate_NullOrMissingReport_ThrowsClearly()
        {
            var e1 = Assert.Throws<ArgumentException>(() => HtmlReportGenerator.Generate(null, Ts));
            StringAssert.Contains("EvaluationReport", e1.Message);

            var e2 = Assert.Throws<ArgumentException>(
                () => HtmlReportGenerator.Generate(new ReportInputs { report = null }, Ts));
            StringAssert.Contains("EvaluationReport", e2.Message);
        }

        [Test]
        public void Generate_IsDeterministic_ForFixedTimestamp()
        {
            string h1 = HtmlReportGenerator.Generate(MakeInputs(), Ts);
            string h2 = HtmlReportGenerator.Generate(MakeInputs(), Ts);
            Assert.AreEqual(h1, h2, "identical inputs + timestamp must be byte-identical");

            const string other = "9999-12-31T23:59:59Z";
            string h3 = HtmlReportGenerator.Generate(MakeInputs(), other);
            Assert.AreNotEqual(h1, h3, "the report timestamp must be reflected");
            Assert.AreEqual(h1, h3.Replace(other, Ts),
                "the injected timestamp is the ONLY non-deterministic content");
        }

        [Test]
        public void ReportStore_Load_MissingEvaluation_ReturnsNull()
        {
            string reportsRoot = Path.Combine(Path.GetTempPath(), "unityqa-report-test-" + Guid.NewGuid().ToString("N"));
            string sessionsRoot = Path.Combine(reportsRoot, "Sessions");
            // Neither directory exists → no evaluation.json → null, not a throw.
            Assert.IsNull(ReportStore.Load(reportsRoot, sessionsRoot));
        }
    }
}
