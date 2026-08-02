// -----------------------------------------------------------------------------
// UnityQA — AnalysisEngine.cs                                    (M5 Slice A)
//
// PURPOSE
//   FeatureDataset in → DatasetAnalysis out. Pure, static, deterministic,
//   engine-free computation; no file I/O (AnalysisStore owns that), no scene,
//   no judgments. The canonical feature list and the statistics math are both
//   REUSED from M4 (FeatureDatasetBuilder.Selectors / .ComputeStatistics) —
//   this layer adds positional description, never a second source of truth.
//
// DESCRIPTIVE-ONLY BOUNDARY (Slice A discipline)
//   OutlierZThreshold marks values whose |z| ≥ 2 as numeric CANDIDATES —
//   a reproducible arithmetic fact chosen for its textbook convention
//   (~95% of a normal distribution lies within 2σ). Nothing here decides
//   what a candidate MEANS. Classification, anomaly labels, and any use of
//   the word "bug" are later slices' work, by design.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityQA.Features;

namespace UnityQA.Analysis
{
    /// <summary>Descriptive analytics over a FeatureDataset.</summary>
    public static class AnalysisEngine
    {
        /// <summary>|z| at or beyond which a value is recorded as a numeric
        /// outlier candidate (2σ — textbook convention, stated not judged).</summary>
        public const float OutlierZThreshold = 2f;

        /// <summary>Analyze a dataset. Never throws; an empty dataset yields a
        /// valid, empty analysis (a fine answer to "nothing recorded yet").</summary>
        public static DatasetAnalysis Analyze(FeatureDataset dataset)
        {
            var analysis = new DatasetAnalysis
            {
                schemaVersion = DatasetAnalysis.CurrentSchemaVersion,
                generatedUtc = System.DateTime.UtcNow.ToString("o"),
                sourceGeneratedUtc = dataset?.generatedUtc ?? "",
                sourceSessionCount = dataset?.sessionCount ?? 0,
                sessions = new List<SessionAnalysis>(),
                rankings = new List<FeatureRanking>(),
                outlierCandidates = new List<OutlierCandidate>()
            };
            if (dataset?.rows == null || dataset.rows.Count == 0) return analysis;

            // One math source: recompute statistics with M4's own code, so the
            // analysis can never disagree with the dataset pipeline (and a
            // hand-assembled dataset without a statistics block still works).
            var stats = new FeatureDataset { rows = dataset.rows, statistics = new List<FeatureStatistic>() };
            FeatureDatasetBuilder.ComputeStatistics(stats);

            foreach (SessionFeatures row in dataset.rows)
            {
                analysis.sessions.Add(new SessionAnalysis
                {
                    sessionId = row.sessionId,
                    folderName = row.sessionFolderName,
                    features = new List<FeatureAnalysisValue>()
                });
            }

            for (int s = 0; s < FeatureDatasetBuilder.Selectors.Count; s++)
            {
                FeatureSelector selector = FeatureDatasetBuilder.Selectors[s];
                FeatureStatistic stat = stats.statistics[s]; // ComputeStatistics preserves order

                // Gather available values once (percentiles need the cohort).
                var values = new List<float>();
                foreach (SessionFeatures row in dataset.rows)
                    if (selector.Available(row)) values.Add(selector.Get(row));

                // Ranking: available sessions, value descending, folder tie-break.
                var ranked = new List<SessionFeatures>();
                foreach (SessionFeatures row in dataset.rows)
                    if (selector.Available(row)) ranked.Add(row);
                ranked.Sort((a, b) =>
                {
                    int byValue = selector.Get(b).CompareTo(selector.Get(a));
                    return byValue != 0
                        ? byValue
                        : string.CompareOrdinal(a.sessionFolderName, b.sessionFolderName);
                });
                var ranking = new FeatureRanking
                { name = selector.Name, sessionIdsDescending = new List<string>() };
                foreach (SessionFeatures row in ranked)
                    ranking.sessionIdsDescending.Add(row.sessionId);
                analysis.rankings.Add(ranking);

                // Per-session positional description.
                for (int r = 0; r < dataset.rows.Count; r++)
                {
                    SessionFeatures row = dataset.rows[r];
                    var fav = new FeatureAnalysisValue { name = selector.Name };
                    analysis.sessions[r].features.Add(fav);

                    if (!selector.Available(row)) continue; // stays available=false, zeros

                    float v = selector.Get(row);
                    fav.available = true;
                    fav.value = v;
                    fav.deviationFromMean = v - stat.mean;
                    fav.zScore = stat.std > 0f ? (v - stat.mean) / stat.std : 0f;
                    fav.normalized = stat.max > stat.min ? (v - stat.min) / (stat.max - stat.min) : 0f;
                    fav.percentile = Percentile(values, v);

                    if (fav.zScore >= OutlierZThreshold || fav.zScore <= -OutlierZThreshold)
                    {
                        analysis.sessions[r].farFromMeanCount++;
                        analysis.outlierCandidates.Add(new OutlierCandidate
                        {
                            sessionId = row.sessionId,
                            featureName = selector.Name,
                            value = v,
                            zScore = fav.zScore,
                            datasetMean = stat.mean
                        });
                    }
                }
            }
            return analysis;
        }

        /// <summary>Mid-rank percentile (contract in SessionAnalysis.cs):
        /// (below + 0.5·equal) / N × 100 — deterministic under ties; a lone
        /// value sits at the 50th percentile of its own cohort.</summary>
        public static float Percentile(List<float> cohort, float value)
        {
            if (cohort == null || cohort.Count == 0) return 0f;
            int below = 0, equal = 0;
            for (int i = 0; i < cohort.Count; i++)
            {
                if (cohort[i] < value) below++;
                else if (cohort[i] == value) equal++;
            }
            return (below + 0.5f * equal) / cohort.Count * 100f;
        }
    }
}
