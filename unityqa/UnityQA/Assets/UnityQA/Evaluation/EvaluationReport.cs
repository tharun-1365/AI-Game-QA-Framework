// -----------------------------------------------------------------------------
// UnityQA — EvaluationReport.cs                                  (M6 Slice C)
//
// PURPOSE
//   The OUTPUT of a planted-bug evaluation: the machine-of-record document
//   (evaluation.json) that quantifies how well the oracles detected the
//   planted defects, with every raw per-run result preserved so the IEEE
//   paper's numbers are reproducible rather than re-derived.
//
// RATE REPRESENTATION (JsonUtility-safe)
//   Rates are floats guarded by an *Available bool. A rate whose denominator
//   is zero is NOT written as NaN (JsonUtility would emit invalid JSON) — it
//   is left at 0 with Available=false, i.e. "unavailable/indeterminate",
//   exactly as the spec requires for a metric that cannot be computed honestly.
//
// DETERMINISM
//   `generatedUtc` is the ONLY non-deterministic field (the established rule);
//   evaluation tests ignore it. Case order = campaign order; per-run order =
//   run index; firing-oracle order = oracle registration order.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace UnityQA.Evaluation
{
    /// <summary>Whole-campaign evaluation result. Wire format of evaluation.json.</summary>
    [Serializable]
    public sealed class EvaluationReport
    {
        public const int CurrentSchemaVersion = 1;
        public int schemaVersion = CurrentSchemaVersion;

        /// <summary>Sole non-deterministic field (ISO-8601 UTC).</summary>
        public string generatedUtc;

        public string benchmarkId;
        public int repeats;

        public List<EvaluationCaseResult> cases = new List<EvaluationCaseResult>();
        public EvaluationAggregate aggregate = new EvaluationAggregate();
    }

    /// <summary>Per-case roll-up over its repeated runs.</summary>
    [Serializable]
    public sealed class EvaluationCaseResult
    {
        public string caseId;
        public string bugClass;
        public bool isClean;
        public List<string> expectedDetectors = new List<string>();

        public int runs;
        /// <summary>Runs where at least one expected detector produced a verdict
        /// (pass or fail) — the honest denominator for detectionRate. A run
        /// where every expected detector SKIPPED is not evaluable.</summary>
        public int evaluableRuns;
        /// <summary>Planted: runs an expected detector FAILED on. Clean: 0.</summary>
        public int detected;
        /// <summary>Planted: evaluable runs an expected detector did NOT fire on.</summary>
        public int missed;
        /// <summary>Runs with no expected-detector verdict (insufficient evidence).</summary>
        public int indeterminate;
        /// <summary>Clean: runs where ANY oracle FAILED (a false positive).</summary>
        public int falsePositives;

        public float detectionRate;          // detected / evaluableRuns
        public bool detectionRateAvailable;  // false ⇒ unavailable (no evaluable runs)

        /// <summary>Planted: every evaluable run agreed on detected/not.
        /// Clean: every run agreed on false-positive/not.</summary>
        public bool consistent;

        public List<EvaluationRunResult> perRun = new List<EvaluationRunResult>();
    }

    /// <summary>One recorded run of one case.</summary>
    [Serializable]
    public sealed class EvaluationRunResult
    {
        public int runIndex;
        public string sessionId;
        /// <summary>Recorded session outcome if known ("Success"/…/"none"), else "".</summary>
        public string sessionOutcome;
        /// <summary>Planted: an expected detector FAILED this run.</summary>
        public bool detected;
        /// <summary>Clean: at least one oracle FAILED this run.</summary>
        public bool falsePositive;
        /// <summary>An expected detector produced a verdict (planted only).</summary>
        public bool evaluable;
        /// <summary>Oracle Names that returned FAIL on this session, in order.</summary>
        public List<string> firingOracles = new List<string>();
        /// <summary>True when no oracle result at all was found for the session
        /// (missing/unrecorded) — the run contributes no evidence.</summary>
        public bool sessionResultsMissing;
    }

    /// <summary>Campaign-wide totals.</summary>
    [Serializable]
    public sealed class EvaluationAggregate
    {
        public int plantedCaseCount;
        public int totalBugRuns;
        public int totalEvaluableBugRuns;
        public int totalDetected;
        public int totalMissed;
        public int totalIndeterminate;
        public float overallDetectionRate;
        public bool overallDetectionRateAvailable;

        public int cleanRuns;
        public int falsePositives;
        public float falsePositiveRate;
        public bool falsePositiveRateAvailable;
    }
}
