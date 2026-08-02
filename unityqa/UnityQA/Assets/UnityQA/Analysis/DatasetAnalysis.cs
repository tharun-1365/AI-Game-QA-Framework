// -----------------------------------------------------------------------------
// UnityQA — DatasetAnalysis.cs                                   (M5 Slice A)
//
// PURPOSE
//   The dataset-wide descriptive analysis document (analysis.json, Sessions
//   root — a collection-level document like catalog.json and dataset.json).
//   Carries provenance back to the exact dataset it describes, per-session
//   analyses, per-feature rankings, and numeric outlier CANDIDATES.
//
//   Language discipline, enforced at the type level: "candidate", "far from
//   mean", "rank" — descriptive vocabulary only. Nothing in this document
//   claims abnormality, buggyness, or quality. M5's later slices and M6 make
//   claims; this file gives them defensible numbers to make claims WITH.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace UnityQA.Analysis
{
    /// <summary>Sessions ordered by one feature's value (descending; ties by
    /// folder name ascending — fully deterministic). Available rows only.</summary>
    [Serializable]
    public sealed class FeatureRanking
    {
        public string name;
        public List<string> sessionIdsDescending;
    }

    /// <summary>A numeric fact: this feature of this session lies ≥ the z
    /// threshold from the dataset mean. Candidacy is arithmetic, not judgment.</summary>
    [Serializable]
    public sealed class OutlierCandidate
    {
        public string sessionId;
        public string featureName;
        public float value;
        public float zScore;
        public float datasetMean;
    }

    /// <summary>The dataset-wide analysis. Wire format of analysis.json.</summary>
    [Serializable]
    public sealed class DatasetAnalysis
    {
        public const int CurrentSchemaVersion = 1;

        public int schemaVersion;
        /// <summary>The sole non-deterministic field (established rule).</summary>
        public string generatedUtc;

        // --- provenance: exactly which dataset this analysis describes -------
        public string sourceGeneratedUtc;
        public int sourceSessionCount;

        public List<SessionAnalysis> sessions;
        public List<FeatureRanking> rankings;
        public List<OutlierCandidate> outlierCandidates;
    }
}
