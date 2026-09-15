// -----------------------------------------------------------------------------
// UnityQA — ReportStore.cs                                       (M7 Reporting)
//
// PURPOSE
//   Load the persisted M6 evidence an HTML QA report renders, and save the
//   finished report — nothing else. Presentation-layer I/O only: this store
//   NEVER runs an oracle or scores a case (EvaluationEngine owns detection).
//   It reuses the existing per-document stores rather than parsing anything
//   new, so the report can never disagree with the machine-of-record.
//
//   `report` (evaluation.json) is the one required input; everything else is
//   optional context and is left null/empty when its artifact is absent, so a
//   report still renders from a bare evaluation result.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityQA.Evaluation;
using UnityQA.Oracles;
using UnityQA.Replay;

namespace UnityQA.Reporting
{
    /// <summary>Read-only bundle of persisted M6 evidence for one report.
    /// Only <see cref="report"/> is required; the rest may be null/empty.</summary>
    public sealed class ReportInputs
    {
        public EvaluationReport report;                 // evaluation.json (required)
        public EvaluationCampaign campaign;             // evaluation-campaign.json (may be null)
        public OracleRunResults oracleResults;          // oracle-results.json (may be null)

        /// <summary>sessionId → that session's validation.json, for the evaluated
        /// sessions that have one. Empty when none were validated.</summary>
        public Dictionary<string, ReplayValidationResult> validations
            = new Dictionary<string, ReplayValidationResult>();

        // --- data-source provenance (report §Data source) -------------------
        public string evaluationJsonPath;
        public string campaignJsonPath;
        public string oracleResultsPath;
        public string sessionsRoot;
    }

    /// <summary>Load persisted evaluation/campaign/session evidence for the HTML
    /// report; save the finished report. Load-only, reusing the sibling stores.</summary>
    public static class ReportStore
    {
        public const string ReportFileName = "unityqa-report.html";
        public const string ValidationFileName = "validation.json";

        /// <summary>Assemble the report inputs from <paramref name="reportsRoot"/>
        /// (evaluation.json + campaign) and <paramref name="sessionsRoot"/>
        /// (oracle-results.json + per-session validation.json). Returns null when
        /// no evaluation.json is present or readable — the caller reports that
        /// rather than emitting a misleading empty report.</summary>
        public static ReportInputs Load(string reportsRoot, string sessionsRoot)
        {
            EvaluationReport report = EvaluationStore.LoadJson(reportsRoot);
            if (report == null) return null; // no result to present

            var inputs = new ReportInputs
            {
                report = report,
                campaign = EvaluationStore.LoadCampaign(reportsRoot),
                oracleResults = OracleResultStore.Load(sessionsRoot),
                evaluationJsonPath = Path.Combine(reportsRoot, EvaluationStore.JsonFileName),
                campaignJsonPath = Path.Combine(reportsRoot, EvaluationStore.CampaignFileName),
                oracleResultsPath = Path.Combine(sessionsRoot, OracleResultStore.FileName),
                sessionsRoot = sessionsRoot
            };

            // Attach each evaluated session's validation.json where one exists.
            // Reuses the same catalog scan OracleContextFactory uses to map a
            // sessionId to its self-contained folder — no second folder walker.
            ReplayCatalog.CatalogDocument catalog = ReplayCatalog.Scan(sessionsRoot);
            if (report.cases != null)
            {
                foreach (EvaluationCaseResult c in report.cases)
                {
                    if (c?.perRun == null) continue;
                    foreach (EvaluationRunResult run in c.perRun)
                    {
                        string sid = run?.sessionId;
                        if (string.IsNullOrEmpty(sid) || inputs.validations.ContainsKey(sid)) continue;

                        ReplayMetadata entry = catalog.entries.Find(e => e.sessionId == sid);
                        if (entry == null || string.IsNullOrEmpty(entry.folderPath)) continue;

                        string vp = Path.Combine(entry.folderPath, ValidationFileName);
                        if (!File.Exists(vp)) continue;
                        try
                        {
                            ReplayValidationResult v =
                                JsonUtility.FromJson<ReplayValidationResult>(File.ReadAllText(vp));
                            if (v != null) inputs.validations[sid] = v;
                        }
                        catch (Exception) { /* damaged validation.json → skip; report stays valid */ }
                    }
                }
            }
            return inputs;
        }

        /// <summary>Write the report HTML under <paramref name="reportsRoot"/> as
        /// unityqa-report.html (UTF-8, no BOM). Returns the absolute path.</summary>
        public static string SaveHtml(string html, string reportsRoot)
        {
            Directory.CreateDirectory(reportsRoot);
            string path = Path.Combine(reportsRoot, ReportFileName);
            File.WriteAllText(path, html ?? string.Empty, new System.Text.UTF8Encoding(false));
            return path;
        }
    }
}
