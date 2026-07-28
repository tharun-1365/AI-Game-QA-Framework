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
//   PASS    — maxDeviation ≤ threshold across the compared window.
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
        public const int CurrentSchemaVersion = 1;
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

        public string verdict;
    }
}
