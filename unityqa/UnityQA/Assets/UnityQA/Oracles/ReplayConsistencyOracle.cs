// -----------------------------------------------------------------------------
// UnityQA — ReplayConsistencyOracle.cs                           (M5 Slice D)
//
// PURPOSE
//   "Does the replay faithfully reproduce the recorded gameplay?" — judged
//   entirely from the EXISTING M3.C validation result; this oracle runs no
//   playback itself (Evaluate stays pure, per the framework contract).
//
// VERDICT LOGIC (per slice spec)
//   no replay, never validated, or verdict INVALID → SKIP (null): you cannot
//   testify about a comparison that never meaningfully happened.
//   validation PASS → PASS.  validation FAIL → FAIL, severity WARNING —
//   replay infidelity undermines reproducibility of the TEST APPARATUS; it
//   is not itself a gameplay defect (that distinction matters in reports).
// -----------------------------------------------------------------------------

using System.Globalization;
using UnityQA.Replay;

namespace UnityQA.Oracles
{
    /// <summary>Replay fidelity oracle over the M3.C validation result.</summary>
    public sealed class ReplayConsistencyOracle : IQualityOracle
    {
        public string Name => "ReplayConsistency";
        public string Description => "Replay playback reproduces the recorded gameplay within the configured deviation threshold.";
        public bool Enabled { get; set; } = true;

        public OracleResult Evaluate(OracleContext context)
        {
            if (context.Metadata == null || !context.Metadata.hasReplay) return null; // no replay → skip
            ReplayValidationResult v = context.Validation;
            if (v == null || v.verdict == ReplayValidationResult.VerdictInvalid) return null; // never (meaningfully) validated

            bool passed = v.verdict == ReplayValidationResult.VerdictPass;
            var result = new OracleResult
            {
                passed = passed,
                severity = passed ? OracleResult.SeverityInfo : OracleResult.SeverityWarning,
                reason = passed
                    ? $"Replay reproduced the session within threshold (max deviation {v.maxDeviation:F3}u ≤ {v.thresholdUnits:F2}u)."
                    : $"Replay diverged from the session (max deviation {v.maxDeviation:F3}u > threshold {v.thresholdUnits:F2}u" +
                      (v.firstDivergenceTime >= 0f ? $", first at t={v.firstDivergenceTime:F2}s)." : ").")
            };
            result.evidence.Add("sessionId=" + context.SessionId);
            result.evidence.Add("validationVerdict=" + v.verdict);
            result.evidence.Add("meanDeviation=" + v.meanDeviation.ToString("0.###", CultureInfo.InvariantCulture));
            result.evidence.Add("maxDeviation=" + v.maxDeviation.ToString("0.###", CultureInfo.InvariantCulture));
            result.evidence.Add("validationSessionId=" + v.validationSessionId);
            return result;
        }
    }
}
