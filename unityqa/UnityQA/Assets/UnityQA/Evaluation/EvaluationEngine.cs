// -----------------------------------------------------------------------------
// UnityQA — EvaluationEngine.cs                                  (M6 Slice C)
//
// PURPOSE
//   Score a planted-bug campaign: turn (experiment plan + real oracle verdicts)
//   into an EvaluationReport. Pure, deterministic, engine-free — same shape as
//   AnalysisEngine / FeatureDatasetBuilder: file I/O and orchestration live in
//   the caller; this is inputs → values.
//
// SCORING RULE (the one place ground truth meets detection)
//   For each case, per recorded run (sessionId):
//     firingOracles = the oracles that returned FAIL (passed == false) on that
//                     session, in registration order (SKIPs are absent).
//     PLANTED case (expectedDetectors non-empty):
//       evaluable = at least one EXPECTED detector produced a verdict for the
//                   session (pass OR fail). A run where every expected detector
//                   SKIPPED is NOT evaluable (insufficient evidence), counted
//                   as indeterminate, never as a false "missed".
//       detected  = any EXPECTED detector FAILED.
//     CLEAN case (control): falsePositive = ANY oracle FAILED.
//
//   Detection is decided ONLY by comparing the campaign's expectedDetectors
//   (the answer key) with the oracles that actually fired. The engine never
//   inspects caseId/bugClass to decide a verdict — a bug-ID shortcut is
//   impossible by construction (pinned by test).
//
//   Rates use an evaluable denominator and are marked unavailable (not NaN)
//   when that denominator is zero — an honestly-uncomputable metric, not a
//   fabricated one.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using UnityQA.Oracles;

namespace UnityQA.Evaluation
{
    /// <summary>Deterministic campaign scoring: plan + oracle verdicts → report.</summary>
    public static class EvaluationEngine
    {
        /// <summary>
        /// Evaluate a campaign against oracle verdicts. `oracleResults` is the
        /// output of OracleRunner over the campaign's sessions; `sessionOutcomes`
        /// (optional) maps sessionId → recorded outcome for per-run evidence.
        /// Never throws on missing pieces — a null/empty campaign yields an
        /// empty, valid report.
        /// </summary>
        public static EvaluationReport Evaluate(EvaluationCampaign campaign,
                                                OracleRunResults oracleResults,
                                                IDictionary<string, string> sessionOutcomes = null)
        {
            var report = new EvaluationReport
            {
                schemaVersion = EvaluationReport.CurrentSchemaVersion,
                generatedUtc = DateTime.UtcNow.ToString("o"),
                benchmarkId = campaign != null ? campaign.benchmarkId : null,
                repeats = campaign != null ? campaign.repeats : 0
            };
            if (campaign == null || campaign.cases == null) return report;

            // Group oracle verdicts by session (order preserved = registration order).
            var bySession = new Dictionary<string, List<OracleResult>>();
            if (oracleResults?.results != null)
            {
                foreach (OracleResult r in oracleResults.results)
                {
                    if (r == null || string.IsNullOrEmpty(r.sessionId)) continue;
                    if (!bySession.TryGetValue(r.sessionId, out List<OracleResult> list))
                    {
                        list = new List<OracleResult>();
                        bySession[r.sessionId] = list;
                    }
                    list.Add(r);
                }
            }

            var agg = report.aggregate;

            foreach (EvaluationCaseSpec spec in campaign.cases)
            {
                if (spec == null) continue;
                var cr = new EvaluationCaseResult
                {
                    caseId = spec.caseId,
                    bugClass = spec.bugClass,
                    isClean = spec.isClean,
                    expectedDetectors = spec.expectedDetectors ?? new List<string>(),
                    runs = spec.sessionIds?.Count ?? 0
                };

                int runIndex = 0;
                for (int i = 0; i < cr.runs; i++)
                {
                    string sid = spec.sessionIds[i];
                    List<OracleResult> results = null;
                    bool haveResults = !string.IsNullOrEmpty(sid) && bySession.TryGetValue(sid, out results);

                    var run = new EvaluationRunResult
                    {
                        runIndex = runIndex++,
                        sessionId = sid,
                        sessionResultsMissing = !haveResults,
                        sessionOutcome = LookupOutcome(sessionOutcomes, sid)
                    };

                    if (results != null)
                    {
                        foreach (OracleResult r in results)
                            if (!r.passed) run.firingOracles.Add(r.oracleName);
                    }

                    if (spec.isClean)
                    {
                        run.falsePositive = run.firingOracles.Count > 0;
                        run.evaluable = true; // a clean run always tells us pass/fail on FP
                        if (run.falsePositive) cr.falsePositives++;
                    }
                    else
                    {
                        // Did any EXPECTED detector produce a verdict / fire?
                        bool expectedEvaluated = false;
                        bool expectedFired = false;
                        if (results != null && cr.expectedDetectors != null)
                        {
                            foreach (OracleResult r in results)
                            {
                                if (!cr.expectedDetectors.Contains(r.oracleName)) continue;
                                expectedEvaluated = true;            // present ⇒ evaluated (pass or fail)
                                if (!r.passed) expectedFired = true;
                            }
                        }
                        run.evaluable = expectedEvaluated;
                        run.detected = expectedFired;
                        if (!expectedEvaluated) cr.indeterminate++;
                        else if (expectedFired) cr.detected++;
                        else cr.missed++;
                    }

                    cr.perRun.Add(run);
                }

                cr.evaluableRuns = cr.isClean ? cr.runs : (cr.detected + cr.missed);

                if (cr.isClean)
                {
                    cr.detectionRateAvailable = false;
                    cr.consistent = AllEqualBool(cr.perRun, isClean: true);
                    agg.cleanRuns += cr.runs;
                    agg.falsePositives += cr.falsePositives;
                }
                else
                {
                    if (cr.evaluableRuns > 0)
                    {
                        cr.detectionRate = (float)cr.detected / cr.evaluableRuns;
                        cr.detectionRateAvailable = true;
                    }
                    cr.consistent = AllEqualBool(cr.perRun, isClean: false);
                    agg.plantedCaseCount++;
                    agg.totalBugRuns += cr.runs;
                    agg.totalEvaluableBugRuns += cr.evaluableRuns;
                    agg.totalDetected += cr.detected;
                    agg.totalMissed += cr.missed;
                    agg.totalIndeterminate += cr.indeterminate;
                }

                report.cases.Add(cr);
            }

            if (agg.totalEvaluableBugRuns > 0)
            {
                agg.overallDetectionRate = (float)agg.totalDetected / agg.totalEvaluableBugRuns;
                agg.overallDetectionRateAvailable = true;
            }
            if (agg.cleanRuns > 0)
            {
                agg.falsePositiveRate = (float)agg.falsePositives / agg.cleanRuns;
                agg.falsePositiveRateAvailable = true;
            }

            return report;
        }

        /// <summary>Consistency: every evaluable run agreed. Clean → agree on
        /// falsePositive; planted → agree on detected over evaluable runs only.
        /// A case with fewer than 2 evaluable runs is trivially consistent.</summary>
        private static bool AllEqualBool(List<EvaluationRunResult> runs, bool isClean)
        {
            bool seen = false, first = false;
            foreach (EvaluationRunResult r in runs)
            {
                bool value;
                if (isClean) value = r.falsePositive;
                else { if (!r.evaluable) continue; value = r.detected; }
                if (!seen) { first = value; seen = true; }
                else if (value != first) return false;
            }
            return true;
        }

        private static string LookupOutcome(IDictionary<string, string> outcomes, string sid)
        {
            if (outcomes == null || string.IsNullOrEmpty(sid)) return "";
            return outcomes.TryGetValue(sid, out string o) ? (o ?? "none") : "";
        }
    }
}
