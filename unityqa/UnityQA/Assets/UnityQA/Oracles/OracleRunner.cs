// -----------------------------------------------------------------------------
// UnityQA — OracleRunner.cs                                      (M5 Slice B)
//
// PURPOSE
//   Deterministic execution of every enabled oracle over every session
//   context, collecting OracleResults into one OracleRunResults document.
//
// EXECUTION ORDER (part of the contract, tested)
//   Session-major: for each context (dataset/chronological order), each
//   enabled oracle in registration order. Result list order therefore never
//   depends on timing, hashing, or discovery — same inputs, same document.
//
// FAILURE ISOLATION (same philosophy as the M1 event bus)
//   An oracle that throws is recorded as an ORACLE error (severity warning,
//   reason prefixed "oracle-error:") and counted in errorCount — it neither
//   crashes the run nor silently disappears, and it is never conflated with
//   a session failing a quality rule.
//
//   Zero registered oracles is a fully valid run: the framework must be
//   green-path testable before any concrete oracle exists (next slice).
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace UnityQA.Oracles
{
    /// <summary>Executes registered oracles over session contexts.</summary>
    public static class OracleRunner
    {
        public static OracleRunResults Run(OracleRegistry registry, List<OracleContext> contexts)
        {
            var run = new OracleRunResults
            {
                schemaVersion = OracleRunResults.CurrentSchemaVersion,
                generatedUtc = DateTime.UtcNow.ToString("o"),
                sessionCount = contexts?.Count ?? 0,
                oracleCount = registry?.Count ?? 0,
                enabledOracleCount = registry?.EnabledCount ?? 0
            };
            if (registry == null || contexts == null) return run;

            string stamp = run.generatedUtc; // one stamp per run: results share it

            foreach (OracleContext context in contexts)          // session-major…
            {
                foreach (IQualityOracle oracle in registry.Oracles)   // …oracle-minor
                {
                    if (!oracle.Enabled) continue;

                    OracleResult result;
                    try
                    {
                        result = oracle.Evaluate(context);
                    }
                    catch (Exception ex)
                    {
                        run.errorCount++;
                        result = new OracleResult
                        {
                            oracleName = oracle.Name,
                            sessionId = context.SessionId,
                            passed = false,
                            severity = OracleResult.SeverityWarning,
                            reason = "oracle-error: " + ex.Message
                        };
                        result.timestampUtc = stamp;
                        run.results.Add(result);
                        continue;
                    }

                    if (result == null) { run.skippedCount++; continue; } // not applicable

                    // The runner owns bookkeeping the oracle shouldn't:
                    result.oracleName = oracle.Name;
                    result.sessionId = context.SessionId;
                    result.timestampUtc = stamp;
                    if (string.IsNullOrEmpty(result.severity))
                        result.severity = OracleResult.SeverityInfo;

                    run.executedEvaluations++;
                    if (result.passed) run.passedCount++; else run.failedCount++;
                    run.results.Add(result);
                }
            }
            return run;
        }
    }
}
