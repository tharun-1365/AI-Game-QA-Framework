// -----------------------------------------------------------------------------
// UnityQA — OracleResult.cs                                      (M5 Slice B)
//
// PURPOSE
//   One oracle's verdict on one session, and the run-level document that
//   collects them (oracle-results.json). This structure is what M-final's
//   report generation will consume, so it carries everything a report needs:
//   who judged (oracleName), what was judged (sessionId), the verdict
//   (passed + severity), WHY (reason — human-readable, evidence — machine
//   -checkable value strings), and when (stamped by the RUNNER, not the
//   oracle, so Evaluate stays pure).
//
// SEVERITY VOCABULARY (string consts, not an enum — JSON stays readable and
// the set can grow without renumbering): "info" | "warning" | "critical".
// Severity describes how much a FAILURE matters; passing results use "info".
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace UnityQA.Oracles
{
    /// <summary>One oracle's verdict on one session. Part of oracle-results.json.</summary>
    [Serializable]
    public sealed class OracleResult
    {
        public const string SeverityInfo = "info";
        public const string SeverityWarning = "warning";
        public const string SeverityCritical = "critical";

        public string oracleName;
        public string sessionId;
        public bool passed;
        public string severity;
        /// <summary>Human-readable explanation of the verdict.</summary>
        public string reason;
        /// <summary>Machine-checkable supporting facts ("maxDeviation=3.20",
        /// "file=validation.json") — report generation cites these verbatim.</summary>
        public List<string> evidence = new List<string>();
        /// <summary>Stamped by OracleRunner (oracles never read the clock).</summary>
        public string timestampUtc;
    }

    /// <summary>The whole run. Wire format of oracle-results.json.</summary>
    [Serializable]
    public sealed class OracleRunResults
    {
        public const int CurrentSchemaVersion = 1;

        public int schemaVersion;
        /// <summary>Sole non-deterministic field (established rule).</summary>
        public string generatedUtc;

        public int sessionCount;
        public int oracleCount;
        public int enabledOracleCount;

        /// <summary>Evaluations that produced a verdict (excludes skips).</summary>
        public int executedEvaluations;
        public int passedCount;
        public int failedCount;
        /// <summary>Oracle returned null — not applicable to that session.</summary>
        public int skippedCount;
        /// <summary>Oracle threw — the ORACLE is broken, recorded and isolated.</summary>
        public int errorCount;

        public List<OracleResult> results = new List<OracleResult>();
    }
}
