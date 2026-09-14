// -----------------------------------------------------------------------------
// UnityQA — ReplayValidationResult.cs                            (M3 Slice C)
//
// PURPOSE
//   The validation verdict document: how faithfully did a replayed session
//   reproduce the original? Serialized as pretty validation.json into the
//   VALIDATION session's folder (the re-run owns the result; the original
//   stays immutable). Field names = wire format; schemaVersion'd like every
//   UnityQA file — and this file is a primary input to M7's reports and a
//   results table in the IEEE paper (deviation metrics ARE the experiment).
//
// VERDICTS
//   PASS    — maxDeviation ≤ threshold across the compared window (measured
//             after the M6.D bounded constant-offset alignment).
//   FAIL    — deviation exceeded threshold (firstDivergenceTime says when).
//   INVALID — not enough data to judge (either trajectory < 2 samples).
// -----------------------------------------------------------------------------

using System;

namespace UnityQA.Replay
{
    /// <summary>Outcome of one replay-fidelity comparison. Wire format of validation.json.</summary>
    [Serializable]
    public sealed class ReplayValidationResult
    {
        /// <summary>v2 (M5.D stabilization): adds the run-outcome comparison
        /// fields below. v3 (M6.D): adds <see cref="alignmentOffsetSec"/>.
        /// Additive only — older readers are unaffected.</summary>
        public const int CurrentSchemaVersion = 3;
        public const string VerdictPass = "PASS";
        public const string VerdictFail = "FAIL";
        public const string VerdictInvalid = "INVALID";

        public int schemaVersion;

        // --- identity: which two runs were compared -------------------------
        public string originalSessionId;
        public string validationSessionId;
        public string originalFolder;
        public string validationFolder;

        // --- comparison setup ------------------------------------------------
        /// <summary>Max tolerated position deviation, world units (from QAConfig).</summary>
        public float thresholdUnits;
        public int originalSamples;
        public int validationSamples;
        /// <summary>Original samples that fell inside the compared time window.</summary>
        public int comparedSamples;
        public int parseErrors;

        // --- deviation metrics (world units / seconds) ----------------------
        public float maxDeviation;
        public float meanDeviation;
        public float rmsDeviation;
        /// <summary>Session time of the first sample exceeding the threshold; -1 = never.</summary>
        public float firstDivergenceTime;
        public float originalDuration;
        public float validationDuration;
        public float durationDelta;

        // --- constant-offset alignment (v3, M6.D) ---------------------------
        /// <summary>The CONSTANT time offset (seconds) applied to the validation
        /// trajectory before scoring, chosen within a bounded search to
        /// compensate a small replay playback phase lead/lag (frame-domain
        /// playback, D-011). Negative = the replay ran AHEAD of the original.
        /// This records only the applied alignment — the RAW timing discrepancy
        /// is preserved separately in <see cref="durationDelta"/> and is never
        /// hidden. The deviation metrics above are measured at this offset;
        /// 0 means no offset improved the fit (the pre-M6.D behaviour).</summary>
        public float alignmentOffsetSec;

        // --- run-outcome comparison (v2, M5.D stabilization) -----------------
        // A trajectory can stay under threshold while the RUN still ends
        // differently (or vice versa on marginal geometry) — the recorded
        // benchmark outcome is the second, discrete axis of fidelity.
        /// <summary>Recorded outcome of the original session ("" if none recorded).</summary>
        public string originalOutcome;
        /// <summary>Recorded outcome of the validation session ("" if none recorded).</summary>
        public string replayOutcome;
        /// <summary>True when both outcomes were recorded and comparable
        /// (a manual Quit is not — Escape is not part of the input seam).</summary>
        public bool outcomesCompared;
        /// <summary>True when outcomesCompared and the outcomes are equal.</summary>
        public bool outcomeMatch;

        public string verdict;
    }
}
