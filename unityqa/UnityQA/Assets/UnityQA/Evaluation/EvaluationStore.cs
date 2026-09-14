// -----------------------------------------------------------------------------
// UnityQA — EvaluationStore.cs                                   (M6 Slice C)
//
// PURPOSE
//   Storage conventions for the evaluation layer, same one-store-per-document
//   pattern as OracleResultStore / FeatureDatasetStore:
//     evaluation-campaign.json — the INPUT experiment plan (user-authored)
//     evaluation.json          — the machine-of-record result
//     evaluation.csv           — a flat per-case summary for spreadsheets/paper
//   All three live under QAData/Reports (QAPaths.ReportsRoot).
//
// CSV RULES (deterministic by construction)
//   Fixed column order; invariant culture; rates rendered "0.####" or empty
//   when unavailable (never a fake 0 or "NaN"); a trailing OVERALL row and a
//   CLEAN row summarise the aggregate. Strings quoted only if they contain a
//   comma/quote. BuildCsv is public and pure so a test can pin the exact text.
// -----------------------------------------------------------------------------

using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace UnityQA.Evaluation
{
    /// <summary>Save/Load the campaign plan and the evaluation report.</summary>
    public static class EvaluationStore
    {
        public const string CampaignFileName = "evaluation-campaign.json";
        public const string JsonFileName = "evaluation.json";
        public const string CsvFileName = "evaluation.csv";
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ---- campaign (input) ----------------------------------------------

        public static EvaluationCampaign LoadCampaign(string reportsRoot)
        {
            string path = Path.Combine(reportsRoot, CampaignFileName);
            if (!File.Exists(path)) return null;
            try { return JsonUtility.FromJson<EvaluationCampaign>(File.ReadAllText(path)); }
            catch (Exception) { return null; }
        }

        public static string SaveCampaign(EvaluationCampaign campaign, string reportsRoot)
        {
            Directory.CreateDirectory(reportsRoot);
            string path = Path.Combine(reportsRoot, CampaignFileName);
            File.WriteAllText(path, JsonUtility.ToJson(campaign, prettyPrint: true));
            return path;
        }

        // ---- report (output) -----------------------------------------------

        public static string SaveJson(EvaluationReport report, string reportsRoot)
        {
            Directory.CreateDirectory(reportsRoot);
            string path = Path.Combine(reportsRoot, JsonFileName);
            File.WriteAllText(path, JsonUtility.ToJson(report, prettyPrint: true));
            return path;
        }

        public static EvaluationReport LoadJson(string reportsRoot)
        {
            string path = Path.Combine(reportsRoot, JsonFileName);
            if (!File.Exists(path)) return null;
            try { return JsonUtility.FromJson<EvaluationReport>(File.ReadAllText(path)); }
            catch (Exception) { return null; }
        }

        public static string SaveCsv(EvaluationReport report, string reportsRoot)
        {
            Directory.CreateDirectory(reportsRoot);
            string path = Path.Combine(reportsRoot, CsvFileName);
            File.WriteAllText(path, BuildCsv(report), new UTF8Encoding(false));
            return path;
        }

        /// <summary>CSV text for a report — public and pure so tests pin the format.</summary>
        public static string BuildCsv(EvaluationReport report)
        {
            var sb = new StringBuilder();
            sb.Append("caseId,bugClass,isClean,expectedDetectors,runs,evaluableRuns,")
              .Append("detected,missed,indeterminate,falsePositives,detectionRate,consistent\n");

            if (report?.cases != null)
            {
                foreach (EvaluationCaseResult c in report.cases)
                {
                    sb.Append(Csv(c.caseId)).Append(',')
                      .Append(Csv(c.bugClass)).Append(',')
                      .Append(c.isClean ? "true" : "false").Append(',')
                      .Append(Csv(string.Join(" ", c.expectedDetectors ?? new System.Collections.Generic.List<string>()))).Append(',')
                      .Append(c.runs.ToString(Inv)).Append(',')
                      .Append(c.evaluableRuns.ToString(Inv)).Append(',')
                      .Append(c.detected.ToString(Inv)).Append(',')
                      .Append(c.missed.ToString(Inv)).Append(',')
                      .Append(c.indeterminate.ToString(Inv)).Append(',')
                      .Append(c.falsePositives.ToString(Inv)).Append(',')
                      .Append(c.detectionRateAvailable ? c.detectionRate.ToString("0.####", Inv) : "").Append(',')
                      .Append(c.consistent ? "true" : "false").Append('\n');
                }
            }

            EvaluationAggregate a = report?.aggregate;
            if (a != null)
            {
                sb.Append("OVERALL,,,,")
                  .Append(a.totalBugRuns.ToString(Inv)).Append(',')
                  .Append(a.totalEvaluableBugRuns.ToString(Inv)).Append(',')
                  .Append(a.totalDetected.ToString(Inv)).Append(',')
                  .Append(a.totalMissed.ToString(Inv)).Append(',')
                  .Append(a.totalIndeterminate.ToString(Inv)).Append(",,")
                  .Append(a.overallDetectionRateAvailable ? a.overallDetectionRate.ToString("0.####", Inv) : "").Append(",\n");

                sb.Append("CLEAN,,,,")
                  .Append(a.cleanRuns.ToString(Inv)).Append(',')
                  .Append(a.cleanRuns.ToString(Inv)).Append(",,,,")
                  .Append(a.falsePositives.ToString(Inv)).Append(',')
                  .Append(a.falsePositiveRateAvailable ? a.falsePositiveRate.ToString("0.####", Inv) : "").Append(",\n");
            }

            return sb.ToString();
        }

        private static string Csv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOf(',') < 0 && s.IndexOf('"') < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}
