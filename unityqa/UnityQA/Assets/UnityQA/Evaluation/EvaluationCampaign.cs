// -----------------------------------------------------------------------------
// UnityQA — EvaluationCampaign.cs                                (M6 Slice C)
//
// PURPOSE
//   The INPUT to the planted-bug evaluation: the experiment plan. It names
//   the benchmark, the repeat count, and one CASE per planted bug (plus at
//   least one clean/golden control), and for each case it lists the recorded
//   session IDs (one per repeat) and the oracle(s) expected to detect that
//   bug class per docs/BENCHMARK.md.
//
// GROUND TRUTH vs DETECTION (the discipline this whole layer preserves)
//   `expectedDetectors` and `isClean` are the ANSWER KEY — ground truth the
//   experimenter supplies from BENCHMARK.md. They say WHICH defect a case
//   contains and WHICH oracle ought to catch it. They are used ONLY to score
//   an oracle verdict AFTER the oracle has produced it (EvaluationEngine),
//   never fed into any oracle's decision. Nothing here tells an oracle what to
//   find; a bug is "detected" only because a real oracle returned FAIL on the
//   case's recorded session.
//
// WHY A MANIFEST, NOT AUTO-CLASSIFICATION
//   Deciding "this session is the PB-002 case" from its outcome would let the
//   detection define the ground truth — the exact circularity this benchmark
//   exists to avoid. So case↔session assignment is an explicit, reviewable
//   experiment record the user authors after recording sessions per the
//   BENCHMARK.md protocol (golden runs on Level_Benchmark; one planted run per
//   bug on Level_PlantedBugs_A). Loaded from evaluation-campaign.json.
//
// WIRE FORMAT
//   JsonUtility-serializable (public fields, Lists, [Serializable] nesting) —
//   the same store discipline as every other UnityQA document.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace UnityQA.Evaluation
{
    /// <summary>The experiment plan for one planted-bug evaluation campaign.</summary>
    [Serializable]
    public sealed class EvaluationCampaign
    {
        public const int CurrentSchemaVersion = 1;
        /// <summary>Recommended default repeat count (DESIGN.md M8: N=5 runs
        /// per condition). Advisory only — the campaign's own `repeats` wins.</summary>
        public const int DefaultRepeats = 5;

        public int schemaVersion = CurrentSchemaVersion;

        /// <summary>Human identifier for the benchmark under evaluation, e.g.
        /// "Level_PlantedBugs_A vs Level_Benchmark".</summary>
        public string benchmarkId;

        /// <summary>Intended repeats per case (documented, configurable). The
        /// authoritative per-case run count is each case's sessionIds length;
        /// EvaluationEngine flags any case whose count disagrees with this.</summary>
        public int repeats = DefaultRepeats;

        public List<EvaluationCaseSpec> cases = new List<EvaluationCaseSpec>();
    }

    /// <summary>One evaluation case: a planted bug, or a clean control.</summary>
    [Serializable]
    public sealed class EvaluationCaseSpec
    {
        /// <summary>Stable case id — a benchmark answer-key key ("PB-001".."PB-004")
        /// or a clean-control id ("clean"). Used only for reporting/scoring
        /// alignment, never inside an oracle.</summary>
        public string caseId;

        /// <summary>Human bug-class label (from BENCHMARK.md).</summary>
        public string bugClass;

        /// <summary>True for a negative/control case (a clean golden run that
        /// must NOT be flagged). Clean cases have no expectedDetectors.</summary>
        public bool isClean;

        /// <summary>Oracle Names expected to FAIL on this planted case, per
        /// BENCHMARK.md (e.g. ["Hazard"], ["SoftLock"], ["MissingTrigger"]).
        /// A case is detected in a run iff ANY of these returned FAIL. Empty
        /// for clean cases.</summary>
        public List<string> expectedDetectors = new List<string>();

        /// <summary>Recorded session IDs, one per repeat, that exercise this
        /// case (authored after recording per the BENCHMARK.md protocol).</summary>
        public List<string> sessionIds = new List<string>();
    }
}
