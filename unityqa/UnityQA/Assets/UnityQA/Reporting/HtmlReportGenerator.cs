// -----------------------------------------------------------------------------
// UnityQA — HtmlReportGenerator.cs                               (M7 Reporting)
//
// PURPOSE
//   Pure transform: persisted M6 evidence (ReportInputs) → one self-contained
//   HTML QA report string. PRESENTATION ONLY — it reads the stored verdicts and
//   never re-scores a case or re-runs an oracle (that is EvaluationEngine's and
//   the oracles' job). It does not recompute or reinterpret any deviation /
//   alignment value; it echoes what M6/M6-D already wrote.
//
// DETERMINISM
//   Generate is a pure function of (inputs, reportGeneratedUtc). The injected
//   timestamp is the ONLY non-deterministic input — isolated as a parameter so
//   the caller supplies the clock and tests can pin the rest byte-for-byte.
//
// SELF-CONTAINED
//   No external CSS/JS/fonts/CDN/network — inline <style> only, system-font
//   stack, works opened straight from the filesystem. Every inserted string is
//   HTML-escaped so a session id / path / detector name can never break the
//   page; numbers use the invariant culture. No raw JSON is embedded.
//
// M6-D TRACEABILITY (do not rename or reinterpret)
//   durationDelta     = raw run-duration difference.
//   alignmentOffsetSec= bounded constant phase offset applied by M6-D.
//   max/mean/RMS dev   = values produced AFTER the approved alignment.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityQA.Evaluation;
using UnityQA.Oracles;
using UnityQA.Replay;

namespace UnityQA.Reporting
{
    /// <summary>Deterministic evidence→HTML rendering for the M7 QA report.</summary>
    public static class HtmlReportGenerator
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Render the report. Throws <see cref="ArgumentException"/> when
        /// there is no EvaluationReport to present — a missing/unreadable result
        /// fails loudly here rather than yielding a misleading empty report.</summary>
        public static string Generate(ReportInputs inputs, string reportGeneratedUtc)
        {
            if (inputs == null || inputs.report == null)
                throw new ArgumentException(
                    "HtmlReportGenerator: no EvaluationReport to render (evaluation.json missing or " +
                    "unreadable) — run the planted-bug evaluation before generating a report.");

            EvaluationReport r = inputs.report;
            EvaluationAggregate agg = r.aggregate ?? new EvaluationAggregate();
            var sb = new StringBuilder(16 * 1024);

            sb.Append("<!DOCTYPE html>\n<html lang=\"en\"><head><meta charset=\"utf-8\">")
              .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
              .Append("<title>UnityQA QA Report — ").Append(Esc(r.benchmarkId)).Append("</title>")
              .Append("<style>").Append(Css()).Append("</style></head><body><main>");

            HeaderSection(sb, r, reportGeneratedUtc);
            SummarySection(sb, agg);
            PerCaseSection(sb, r);
            CleanControlSection(sb, r, agg);
            ValidationSection(sb, r, inputs);
            OracleEvidenceSection(sb, r, inputs);
            MethodologySection(sb);
            DataSourceSection(sb, inputs);

            sb.Append("<footer>UnityQA M7 reporting layer — presentation only. Detection is performed ")
              .Append("by the M6 oracle/evaluation pipeline; this report renders the persisted result.</footer>");
            sb.Append("</main></body></html>");
            return sb.ToString();
        }

        // ---------------------------------------------------------- §1 header
        private static void HeaderSection(StringBuilder sb, EvaluationReport r, string reportGeneratedUtc)
        {
            sb.Append("<header><h1>UnityQA &mdash; Planted-Bug QA Report</h1><table class=\"kv\">");
            Kv(sb, "Benchmark", r.benchmarkId);
            Kv(sb, "Evaluation generated (UTC)", r.generatedUtc);
            Kv(sb, "Report generated (UTC)", reportGeneratedUtc);
            Kv(sb, "Evaluation schema version", r.schemaVersion.ToString(Inv));
            Kv(sb, "Repeats (configured)", r.repeats.ToString(Inv));
            sb.Append("</table></header>");
        }

        // ------------------------------------------------- §2 executive summary
        private static void SummarySection(StringBuilder sb, EvaluationAggregate a)
        {
            sb.Append("<section><h2>Executive summary</h2><table class=\"kv\">");
            Kv(sb, "Planted cases", a.plantedCaseCount.ToString(Inv));
            Kv(sb, "Bug-runs (total)", a.totalBugRuns.ToString(Inv));
            Kv(sb, "Bug-runs evaluable", a.totalEvaluableBugRuns.ToString(Inv));
            Kv(sb, "Detected", a.totalDetected.ToString(Inv));
            Kv(sb, "Missed", a.totalMissed.ToString(Inv));
            Kv(sb, "Indeterminate", a.totalIndeterminate.ToString(Inv));
            KvRaw(sb, "Overall detection rate",
                RatePct(a.overallDetectionRate, a.overallDetectionRateAvailable) +
                "  (" + a.totalDetected.ToString(Inv) + "/" + a.totalEvaluableBugRuns.ToString(Inv) + ")");
            Kv(sb, "Clean runs", a.cleanRuns.ToString(Inv));
            Kv(sb, "False positives", a.falsePositives.ToString(Inv));
            KvRaw(sb, "False-positive rate",
                RatePct(a.falsePositiveRate, a.falsePositiveRateAvailable) +
                "  (" + a.falsePositives.ToString(Inv) + "/" + a.cleanRuns.ToString(Inv) + ")");
            sb.Append("</table></section>");
        }

        // -------------------------------------------------- §3 per-case results
        private static void PerCaseSection(StringBuilder sb, EvaluationReport r)
        {
            sb.Append("<section><h2>Per-case results (planted bugs)</h2>");
            sb.Append("<table class=\"grid\"><thead><tr>")
              .Append("<th>Case</th><th>Bug class</th><th>Expected detector</th>")
              .Append("<th>Runs</th><th>Evaluable</th><th>Detected</th><th>Missed</th>")
              .Append("<th>Indet.</th><th>Detection rate</th><th>Consistent</th>")
              .Append("<th>Firing oracle(s)</th><th>Session(s)</th></tr></thead><tbody>");

            if (r.cases != null)
            {
                foreach (EvaluationCaseResult c in r.cases)
                {
                    if (c == null || c.isClean) continue;
                    bool ok = c.detected > 0 && c.missed == 0 && c.indeterminate == 0;
                    sb.Append("<tr class=\"").Append(ok ? "pass" : "warn").Append("\">");
                    sb.Append("<td>").Append(StatusDot(ok)).Append(Esc(c.caseId)).Append("</td>");
                    sb.Append("<td>").Append(Esc(c.bugClass)).Append("</td>");
                    sb.Append("<td>").Append(Esc(Join(c.expectedDetectors))).Append("</td>");
                    sb.Append("<td>").Append(c.runs.ToString(Inv)).Append("</td>");
                    sb.Append("<td>").Append(c.evaluableRuns.ToString(Inv)).Append("</td>");
                    sb.Append("<td>").Append(c.detected.ToString(Inv)).Append("</td>");
                    sb.Append("<td>").Append(c.missed.ToString(Inv)).Append("</td>");
                    sb.Append("<td>").Append(c.indeterminate.ToString(Inv)).Append("</td>");
                    sb.Append("<td>").Append(Esc(RatePct(c.detectionRate, c.detectionRateAvailable))).Append("</td>");
                    sb.Append("<td>").Append(YesNo(c.consistent)).Append("</td>");
                    sb.Append("<td>").Append(Esc(Join(FiringOracles(c)))).Append("</td>");
                    sb.Append("<td class=\"mono\">").Append(Esc(Join(SessionIds(c)))).Append("</td>");
                    sb.Append("</tr>");
                }
            }
            sb.Append("</tbody></table></section>");
        }

        // ------------------------------------------------------ §4 clean control
        private static void CleanControlSection(StringBuilder sb, EvaluationReport r, EvaluationAggregate a)
        {
            EvaluationCaseResult clean = null;
            if (r.cases != null)
                foreach (EvaluationCaseResult c in r.cases)
                    if (c != null && c.isClean) { clean = c; break; }

            bool passed = a.falsePositives == 0;
            sb.Append("<section><h2>Clean control</h2><table class=\"kv\">");
            Kv(sb, "Clean sessions", (clean != null ? clean.runs : a.cleanRuns).ToString(Inv));
            Kv(sb, "False positives", a.falsePositives.ToString(Inv));
            KvRaw(sb, "False-positive rate",
                RatePct(a.falsePositiveRate, a.falsePositiveRateAvailable));
            KvRaw(sb, "Clean control",
                "<span class=\"badge " + (passed ? "ok" : "bad") + "\">" +
                (passed ? "PASSED (no false positives)" : "FAILED (false positive raised)") + "</span>");
            sb.Append("</table></section>");
        }

        // ------------------------------------------ §5 replay validation evidence
        private static void ValidationSection(StringBuilder sb, EvaluationReport r, ReportInputs inputs)
        {
            sb.Append("<section><h2>Replay validation evidence</h2>");
            if (inputs.validations == null || inputs.validations.Count == 0)
            {
                sb.Append("<p class=\"muted\">No <code>validation.json</code> was found for the evaluated ")
                  .Append("sessions (only the replay-regression cases produce one).</p></section>");
                return;
            }

            // Emit in case/run order for determinism (dictionary order is not stable).
            var seen = new HashSet<string>();
            if (r.cases != null)
            {
                foreach (EvaluationCaseResult c in r.cases)
                {
                    if (c?.perRun == null) continue;
                    foreach (EvaluationRunResult run in c.perRun)
                    {
                        string sid = run?.sessionId;
                        if (string.IsNullOrEmpty(sid) || !inputs.validations.TryGetValue(sid, out ReplayValidationResult v))
                            continue;
                        if (!seen.Add(sid)) continue;
                        ValidationBlock(sb, c.caseId, sid, v);
                    }
                }
            }
            sb.Append("</section>");
        }

        private static void ValidationBlock(StringBuilder sb, string caseId, string sid, ReplayValidationResult v)
        {
            sb.Append("<h3>").Append(Esc(caseId)).Append(" &mdash; replay validation</h3>");
            sb.Append("<table class=\"kv\">");
            KvRaw(sb, "Validation session", "<span class=\"mono\">" + Esc(sid) + "</span>");
            KvRaw(sb, "Verdict",
                "<span class=\"badge " + (v.verdict == ReplayValidationResult.VerdictPass ? "ok" : "bad") + "\">" +
                Esc(v.verdict) + "</span>");
            Kv(sb, "Original outcome", Blank(v.originalOutcome, "(none)"));
            Kv(sb, "Replay outcome", Blank(v.replayOutcome, "(none — run did not terminate)"));
            Kv(sb, "Max deviation (u)", Num(v.maxDeviation) + "  (threshold " + Num(v.thresholdUnits) + "u)");
            Kv(sb, "Mean deviation (u)", Num(v.meanDeviation));
            Kv(sb, "RMS deviation (u)", Num(v.rmsDeviation));
            Kv(sb, "First divergence (s)", v.firstDivergenceTime < 0f ? "none" : Num(v.firstDivergenceTime));
            Kv(sb, "Duration Δ (s, raw)", Num(v.durationDelta));
            if (v.schemaVersion >= 3)
                Kv(sb, "Alignment offset (s, M6-D)", Num(v.alignmentOffsetSec));
            Kv(sb, "Validation schema version", v.schemaVersion.ToString(Inv));
            sb.Append("</table>");
            sb.Append("<p class=\"note\">Raw run-duration difference is <code>durationDelta</code>; ")
              .Append("<code>alignmentOffsetSec</code> is the bounded constant phase offset applied by the ")
              .Append("M6-D trajectory comparison; the deviation values are those produced after that ")
              .Append("alignment, against the unchanged 0.75u threshold.</p>");
        }

        // -------------------------------------------------------- §6 oracle evidence
        private static void OracleEvidenceSection(StringBuilder sb, EvaluationReport r, ReportInputs inputs)
        {
            sb.Append("<section><h2>Oracle evidence</h2>");
            sb.Append("<p class=\"muted\">Firing oracles (verdict FAIL) recorded by the evaluation for each ")
              .Append("evaluated run. Verdicts are read from the persisted result &mdash; not recomputed.</p>");
            sb.Append("<table class=\"grid\"><thead><tr><th>Case</th><th>Session</th>")
              .Append("<th>Recorded outcome</th><th>Firing oracle(s)</th><th>Detail</th></tr></thead><tbody>");

            if (r.cases != null)
            {
                foreach (EvaluationCaseResult c in r.cases)
                {
                    if (c?.perRun == null) continue;
                    foreach (EvaluationRunResult run in c.perRun)
                    {
                        if (run == null) continue;
                        sb.Append("<tr><td>").Append(Esc(c.caseId)).Append("</td>");
                        sb.Append("<td class=\"mono\">").Append(Esc(run.sessionId)).Append("</td>");
                        sb.Append("<td>").Append(Blank(run.sessionOutcome, "(none)")).Append("</td>");
                        sb.Append("<td>").Append(Esc(Join(run.firingOracles))).Append("</td>");
                        sb.Append("<td>").Append(OracleDetail(run, inputs.oracleResults)).Append("</td>");
                        sb.Append("</tr>");
                    }
                }
            }
            sb.Append("</tbody></table></section>");
        }

        /// <summary>Optional enrichment: the stored reason for each firing oracle,
        /// taken verbatim from oracle-results.json when it is present and matches.
        /// Never recomputed; absent when the file is not available.</summary>
        private static string OracleDetail(EvaluationRunResult run, OracleRunResults oracleResults)
        {
            if (run.sessionResultsMissing) return "<span class=\"muted\">session results missing</span>";
            if (oracleResults?.results == null || run.firingOracles == null || run.firingOracles.Count == 0)
                return "";
            var parts = new List<string>();
            foreach (string name in run.firingOracles)
            {
                foreach (OracleResult or in oracleResults.results)
                {
                    if (or == null || or.sessionId != run.sessionId || or.oracleName != name) continue;
                    parts.Add(Esc(name) + ": " + Esc(or.reason) +
                              " <span class=\"muted\">[" + Esc(or.severity) + "]</span>");
                    break;
                }
            }
            return string.Join("<br>", parts.ToArray());
        }

        // ------------------------------------------------ §7 methodology / trace
        private static void MethodologySection(StringBuilder sb)
        {
            sb.Append("<section><h2>Methodology &amp; traceability</h2><ul>")
              .Append("<li>This report is generated from the persisted M6 evaluation evidence; it is a ")
              .Append("presentation layer and performs no detection.</li>")
              .Append("<li>Detection is performed by the existing oracle/evaluation pipeline: the oracles ")
              .Append("produce verdicts, and EvaluationEngine scores them against the campaign answer key. ")
              .Append("Ground truth is never fed into an oracle.</li>")
              .Append("<li>A planted case is &ldquo;detected&rdquo; only because an expected detector returned ")
              .Append("FAIL on that case's recorded session; the report echoes that stored verdict.</li>")
              .Append("<li>Replay-fidelity values are read verbatim from validation.json; the raw ")
              .Append("<code>durationDelta</code> is shown alongside the M6-D <code>alignmentOffsetSec</code> ")
              .Append("and the post-alignment deviation, against the unchanged 0.75u threshold.</li>")
              .Append("</ul></section>");
        }

        // ------------------------------------------------------ §8 data source
        private static void DataSourceSection(StringBuilder sb, ReportInputs inputs)
        {
            sb.Append("<section><h2>Data source</h2><table class=\"kv\">");
            KvRaw(sb, "Evaluation result", "<span class=\"mono\">" + Esc(inputs.evaluationJsonPath) + "</span>");
            KvRaw(sb, "Campaign plan", "<span class=\"mono\">" + Esc(inputs.campaignJsonPath) + "</span>" +
                (inputs.campaign == null ? " <span class=\"muted\">(not loaded)</span>" : ""));
            KvRaw(sb, "Oracle results", "<span class=\"mono\">" + Esc(inputs.oracleResultsPath) + "</span>" +
                (inputs.oracleResults == null ? " <span class=\"muted\">(not present)</span>" : ""));
            KvRaw(sb, "Sessions root", "<span class=\"mono\">" + Esc(inputs.sessionsRoot) + "</span>");
            Kv(sb, "Validation.json loaded", (inputs.validations != null ? inputs.validations.Count : 0).ToString(Inv));
            sb.Append("</table></section>");
        }

        // ------------------------------------------------------------- helpers

        private static List<string> FiringOracles(EvaluationCaseResult c)
        {
            var list = new List<string>();
            if (c?.perRun == null) return list;
            foreach (EvaluationRunResult run in c.perRun)
                if (run?.firingOracles != null)
                    foreach (string o in run.firingOracles)
                        if (!list.Contains(o)) list.Add(o);
            return list;
        }

        private static List<string> SessionIds(EvaluationCaseResult c)
        {
            var list = new List<string>();
            if (c?.perRun == null) return list;
            foreach (EvaluationRunResult run in c.perRun)
                if (!string.IsNullOrEmpty(run?.sessionId) && !list.Contains(run.sessionId))
                    list.Add(run.sessionId);
            return list;
        }

        private static string RatePct(float rate, bool available)
            => available ? (rate * 100f).ToString("0.#", Inv) + "%" : "n/a";

        private static string Num(float v) => v.ToString("0.###", Inv);

        private static string YesNo(bool b)
            => b ? "<span class=\"badge ok\">yes</span>" : "<span class=\"badge bad\">no</span>";

        private static string StatusDot(bool ok)
            => "<span class=\"dot " + (ok ? "ok" : "bad") + "\"></span>";

        private static string Blank(string s, string ifEmpty) => string.IsNullOrEmpty(s) ? Esc(ifEmpty) : Esc(s);

        private static string Join(List<string> items)
        {
            if (items == null || items.Count == 0) return "—"; // em dash
            return string.Join(", ", items.ToArray());
        }

        private static void Kv(StringBuilder sb, string k, string v)
            => sb.Append("<tr><th>").Append(Esc(k)).Append("</th><td>").Append(Esc(v)).Append("</td></tr>");

        /// <summary>Key/value row whose value is trusted, pre-escaped HTML.</summary>
        private static void KvRaw(StringBuilder sb, string k, string vHtml)
            => sb.Append("<tr><th>").Append(Esc(k)).Append("</th><td>").Append(vHtml).Append("</td></tr>");

        /// <summary>HTML-escape every inserted string so ids/paths/names cannot
        /// break the page. Null → empty.</summary>
        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (char ch in s)
            {
                switch (ch)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    case '\'': sb.Append("&#39;"); break;
                    default: sb.Append(ch); break;
                }
            }
            return sb.ToString();
        }

        private static string Css()
        {
            // Inline only. System-font stack — no external/webfont. Kept compact.
            return
                "*{box-sizing:border-box}" +
                "body{margin:0;background:#f4f5f7;color:#1c2430;" +
                "font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;line-height:1.5}" +
                "main{max-width:1024px;margin:0 auto;padding:24px}" +
                "header{border-bottom:3px solid #2f6f4f;margin-bottom:24px;padding-bottom:8px}" +
                "h1{font-size:24px;margin:0 0 12px}h2{font-size:18px;margin:28px 0 10px;color:#2f6f4f}" +
                "h3{font-size:15px;margin:18px 0 6px}" +
                "section{background:#fff;border:1px solid #dfe3e8;border-radius:8px;padding:16px 18px;margin:14px 0}" +
                "table{border-collapse:collapse;width:100%;font-size:13px}" +
                "table.kv th{text-align:left;width:260px;color:#5a6572;font-weight:600;vertical-align:top;padding:3px 8px}" +
                "table.kv td{padding:3px 8px}" +
                "table.grid th,table.grid td{border:1px solid #e2e6ea;padding:6px 8px;text-align:left;vertical-align:top}" +
                "table.grid thead th{background:#eef2f0;color:#33404d}" +
                "tr.pass td{background:#f3fbf6}tr.warn td{background:#fff7ed}" +
                ".mono{font-family:ui-monospace,SFMono-Regular,Consolas,Menlo,monospace;font-size:12px;word-break:break-all}" +
                ".badge{display:inline-block;padding:1px 8px;border-radius:10px;font-size:12px;font-weight:600}" +
                ".badge.ok{background:#dff3e6;color:#1c6b3f}.badge.bad{background:#fbe3e3;color:#a12222}" +
                ".dot{display:inline-block;width:9px;height:9px;border-radius:50%;margin-right:6px;vertical-align:baseline}" +
                ".dot.ok{background:#2f9e5f}.dot.bad{background:#c0392b}" +
                ".muted{color:#7a828c}.note{color:#5a6572;font-size:12px;margin:6px 0 2px}" +
                "code{background:#eef1f4;padding:1px 4px;border-radius:4px;font-size:12px}" +
                "ul{margin:6px 0 0;padding-left:20px}li{margin:3px 0;font-size:13px}" +
                "footer{color:#7a828c;font-size:12px;margin:24px 0 8px;text-align:center}";
        }
    }
}
