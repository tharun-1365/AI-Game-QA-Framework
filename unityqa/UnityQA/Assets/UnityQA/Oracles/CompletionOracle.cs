// -----------------------------------------------------------------------------
// UnityQA — CompletionOracle.cs                                  (M5 Slice D)
//
// PURPOSE
//   "Was the benchmark level completed?" — judged from the session's recorded
//   RunOutcome (BenchGame v2). Severity WARNING on failure: not finishing is
//   a quality signal worth reporting, not automatically a defect (the
//   attribution of WHY is HazardOracle's job — one question per oracle).
//
//   Sessions with no recorded outcome (pre-v2, or telemetry off) → SKIP:
//   absence of evidence is not evidence of failure.
// -----------------------------------------------------------------------------

namespace UnityQA.Oracles
{
    /// <summary>Benchmark completion oracle over the recorded SessionOutcome.</summary>
    public sealed class CompletionOracle : IQualityOracle
    {
        public string Name => "Completion";
        public string Description => "The benchmark run reached the Exit Door (recorded outcome Success).";
        public bool Enabled { get; set; } = true;

        public OracleResult Evaluate(OracleContext context)
        {
            string outcome = context.RunOutcome;
            if (string.IsNullOrEmpty(outcome)) return null; // no recorded outcome → skip

            bool passed = outcome == "Success";
            var result = new OracleResult
            {
                passed = passed,
                severity = passed ? OracleResult.SeverityInfo : OracleResult.SeverityWarning,
                reason = passed
                    ? "The run reached the Exit Door."
                    : $"The run did not reach the Exit Door (outcome: {outcome})."
            };
            result.evidence.Add("sessionOutcome=" + outcome);
            result.evidence.Add("doorReached=" + (passed ? "true" : "false"));
            return result;
        }
    }
}
