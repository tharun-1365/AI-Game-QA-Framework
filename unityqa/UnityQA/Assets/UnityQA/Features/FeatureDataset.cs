// -----------------------------------------------------------------------------
// UnityQA — FeatureDataset.cs                                    (M4 Slice B)
//
// PURPOSE
//   The cross-session dataset document: every session's feature vector as a
//   row, plus per-feature statistical summaries. Serialized as dataset.json
//   at the Sessions root (beside catalog.json — root-level documents describe
//   the collection; folder-level documents describe one session).
//
// STATISTICS CONTRACT (frozen with schema v1)
//   Per feature: sampleCount, mean, std, min, max — computed ONLY over rows
//   where the feature's source group was available (a session with no replay
//   contributes nothing to directionChanges statistics rather than a fake 0;
//   sampleCount records exactly how many rows the number rests on).
//   std is the POPULATION standard deviation (÷N): the dataset is treated as
//   the complete population of recorded sessions, not a sample from one.
//   Rows are ordered OLDEST-FIRST (ascending folder name) — chronological
//   order is the natural axis of a dataset, unlike the catalog's newest-first
//   browsing order.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace UnityQA.Features
{
    /// <summary>Summary statistics for one feature across the dataset.</summary>
    [Serializable]
    public sealed class FeatureStatistic
    {
        public string name;
        /// <summary>Rows that actually carried this feature (availability-gated).</summary>
        public int sampleCount;
        public float mean;
        /// <summary>Population standard deviation (÷N).</summary>
        public float std;
        public float min;
        public float max;
    }

    /// <summary>The cross-session feature dataset. Wire format of dataset.json.</summary>
    [Serializable]
    public sealed class FeatureDataset
    {
        public const int CurrentSchemaVersion = 1;

        public int schemaVersion;
        /// <summary>The sole non-deterministic field (same rule as SessionFeatures).</summary>
        public string generatedUtc;
        public int sessionCount;
        /// <summary>Session folders the builder could not extract (damaged/missing).</summary>
        public int skippedSessions;

        /// <summary>One row per session, oldest-first.</summary>
        public List<SessionFeatures> rows;

        /// <summary>Per-feature summaries in the canonical selector order.</summary>
        public List<FeatureStatistic> statistics;
    }
}
