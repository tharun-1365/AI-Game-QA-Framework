// -----------------------------------------------------------------------------
// UnityQA — SessionAnalysis.cs                                   (M5 Slice A)
//
// PURPOSE
//   Descriptive analysis of ONE session relative to the dataset it belongs
//   to: for every canonical feature, where does this session sit? All values
//   are FACTS about position in a distribution — never judgments. The word
//   "abnormal" does not exist in this layer; "far from the mean" is a number,
//   not a verdict (verdicts are later M5/M6 slices).
//
// FORMULA CONTRACT (frozen with analysis schema v1)
//   zScore            (value − mean) / std          (0 when std == 0)
//   percentile        mid-rank: (countBelow + 0.5·countEqual) / N × 100
//                     over AVAILABLE values only — deterministic under ties
//   normalized        (value − min) / (max − min)   (0 when max == min)
//   deviationFromMean value − mean
//   mean/std/min/max come from the SAME statistics code that built the
//   dataset (FeatureDatasetBuilder.ComputeStatistics) — one math source.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace UnityQA.Analysis
{
    /// <summary>One feature's descriptive position for one session.</summary>
    [Serializable]
    public sealed class FeatureAnalysisValue
    {
        public string name;
        /// <summary>False when the session's source group was missing — all
        /// numeric fields are then 0 and the row is excluded from percentiles,
        /// rankings and outlier candidacy (honest missingness, not fake zeros).</summary>
        public bool available;
        public float value;
        public float zScore;
        public float percentile;
        public float normalized;
        public float deviationFromMean;
    }

    /// <summary>Descriptive analysis of one session. Part of analysis.json.</summary>
    [Serializable]
    public sealed class SessionAnalysis
    {
        public string sessionId;
        public string folderName;
        /// <summary>One entry per canonical feature, in Selectors order.</summary>
        public List<FeatureAnalysisValue> features;
        /// <summary>How many of this session's features lie at |z| ≥ the
        /// engine's OutlierZThreshold. A count — not a classification.</summary>
        public int farFromMeanCount;
    }
}
