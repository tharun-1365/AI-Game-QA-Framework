// -----------------------------------------------------------------------------
// UnityQA Tests — AnalysisEngineTests.cs                    (M5 Slice A tests)
//
// The engine is pure (FeatureDataset object in, DatasetAnalysis out), so most
// tests need no files at all — hand-built rows with hand-computed statistics.
// Only the store round-trip touches disk.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityQA.Analysis;
using UnityQA.Features;

namespace UnityQA.Tests
{
    public sealed class AnalysisEngineTests
    {
        /// <summary>Row with a known totalDistance (events group available).</summary>
        private static SessionFeatures Row(string id, float distance,
                                           bool events = true, bool replay = true)
        {
            return new SessionFeatures
            {
                sessionId = id,
                sessionFolderName = "20260803-" + id,
                eventsAvailable = events,
                replayAvailable = replay,
                totalDistance = distance,
                replayFrameCount = replay ? 100 : 0
            };
        }

        private static FeatureDataset Dataset(params SessionFeatures[] rows)
        {
            var ds = new FeatureDataset
            {
                schemaVersion = 1,
                generatedUtc = "2026-08-03T10:00:00Z",
                sessionCount = rows.Length,
                rows = new List<SessionFeatures>(rows),
                statistics = new List<FeatureStatistic>()
            };
            return ds;
        }

        private static FeatureAnalysisValue Feature(DatasetAnalysis a, string sessionId, string name)
        {
            SessionAnalysis s = a.sessions.Find(x => x.sessionId == sessionId);
            return s.features.Find(f => f.name == name);
        }

        // ------------------------------------------------------------- z-score

        [Test]
        public void ZScores_MatchHandComputedPopulationValues()
        {
            // 2, 4, 6 → mean 4, population std = √((4+0+4)/3) = 1.63299.
            var a = AnalysisEngine.Analyze(Dataset(Row("a", 2f), Row("b", 4f), Row("c", 6f)));

            Assert.AreEqual(-1.2247f, Feature(a, "a", "totalDistance").zScore, 1e-3f);
            Assert.AreEqual(0f, Feature(a, "b", "totalDistance").zScore, 1e-4f);
            Assert.AreEqual(1.2247f, Feature(a, "c", "totalDistance").zScore, 1e-3f);
            Assert.AreEqual(-2f, Feature(a, "a", "totalDistance").deviationFromMean, 1e-4f);
        }

        [Test]
        public void ZeroVariance_YieldsZeroZScores_NotNaN()
        {
            var a = AnalysisEngine.Analyze(Dataset(Row("a", 5f), Row("b", 5f)));
            Assert.AreEqual(0f, Feature(a, "a", "totalDistance").zScore);
            Assert.IsFalse(float.IsNaN(Feature(a, "a", "totalDistance").normalized));
        }

        // ---------------------------------------------------------- percentile

        [Test]
        public void Percentiles_MidRank_OrderedCorrectly()
        {
            // 2, 4, 6 → (0+0.5)/3, (1+0.5)/3, (2+0.5)/3 = 16.67, 50, 83.33.
            var a = AnalysisEngine.Analyze(Dataset(Row("a", 2f), Row("b", 4f), Row("c", 6f)));
            Assert.AreEqual(16.667f, Feature(a, "a", "totalDistance").percentile, 0.01f);
            Assert.AreEqual(50f, Feature(a, "b", "totalDistance").percentile, 0.01f);
            Assert.AreEqual(83.333f, Feature(a, "c", "totalDistance").percentile, 0.01f);
        }

        [Test]
        public void Percentiles_TiesShareMidRank()
        {
            // 5, 5, 10 → ties: (0 + 0.5·2)/3 = 33.33 each; top: (2 + 0.5)/3 = 83.33.
            var a = AnalysisEngine.Analyze(Dataset(Row("a", 5f), Row("b", 5f), Row("c", 10f)));
            Assert.AreEqual(33.333f, Feature(a, "a", "totalDistance").percentile, 0.01f);
            Assert.AreEqual(33.333f, Feature(a, "b", "totalDistance").percentile, 0.01f);
            Assert.AreEqual(83.333f, Feature(a, "c", "totalDistance").percentile, 0.01f);
        }

        [Test]
        public void Percentile_HelperContract_EmptyAndSingle()
        {
            Assert.AreEqual(0f, AnalysisEngine.Percentile(new List<float>(), 1f));
            Assert.AreEqual(50f, AnalysisEngine.Percentile(new List<float> { 7f }, 7f),
                "a lone value sits at its cohort's 50th percentile");
        }

        // ------------------------------------------------------- normalization

        [Test]
        public void Normalization_MinMaxToUnitInterval()
        {
            var a = AnalysisEngine.Analyze(Dataset(Row("a", 2f), Row("b", 4f), Row("c", 6f)));
            Assert.AreEqual(0f, Feature(a, "a", "totalDistance").normalized, 1e-4f);
            Assert.AreEqual(0.5f, Feature(a, "b", "totalDistance").normalized, 1e-4f);
            Assert.AreEqual(1f, Feature(a, "c", "totalDistance").normalized, 1e-4f);
        }

        // ------------------------------------------------------------ rankings

        [Test]
        public void Rankings_DescendingWithDeterministicTieBreak()
        {
            var a = AnalysisEngine.Analyze(Dataset(Row("a", 5f), Row("b", 9f), Row("c", 5f)));
            FeatureRanking r = a.rankings.Find(x => x.name == "totalDistance");
            Assert.AreEqual("b", r.sessionIdsDescending[0]);
            // a and c tie at 5; folder names "20260803-a" < "20260803-c" → a first.
            Assert.AreEqual("a", r.sessionIdsDescending[1]);
            Assert.AreEqual("c", r.sessionIdsDescending[2]);
        }

        // ------------------------------------------------- outlier candidates

        [Test]
        public void OutlierCandidates_ListedByArithmetic_NotJudgment()
        {
            // Nine at 10, one at 30: mean 12, std 6 → z(30) = 3.0 ≥ threshold.
            var rows = new List<SessionFeatures>();
            for (int i = 0; i < 9; i++) rows.Add(Row("s" + i, 10f));
            rows.Add(Row("far", 30f));

            var a = AnalysisEngine.Analyze(Dataset(rows.ToArray()));
            OutlierCandidate c = a.outlierCandidates.Find(x => x.featureName == "totalDistance");

            Assert.IsNotNull(c, "the distant value must be listed as a candidate");
            Assert.AreEqual("far", c.sessionId);
            Assert.AreEqual(3f, c.zScore, 0.01f);
            Assert.AreEqual(12f, c.datasetMean, 0.01f);
            Assert.AreEqual(1, a.sessions.Find(s => s.sessionId == "far").farFromMeanCount);
        }

        [Test]
        public void UniformDataset_HasNoOutlierCandidates()
        {
            var a = AnalysisEngine.Analyze(Dataset(Row("a", 5f), Row("b", 5f), Row("c", 5f)));
            Assert.AreEqual(0, a.outlierCandidates.Count);
        }

        // ------------------------------------------------ availability & edges

        [Test]
        public void MissingGroup_ExcludedFromCohort_FlaggedUnavailable()
        {
            var a = AnalysisEngine.Analyze(Dataset(
                Row("a", 4f, replay: true), Row("b", 8f, replay: false)));

            FeatureAnalysisValue frames = Feature(a, "b", "replayFrameCount");
            Assert.IsFalse(frames.available);
            Assert.AreEqual(0f, frames.zScore);

            FeatureRanking r = a.rankings.Find(x => x.name == "replayFrameCount");
            Assert.AreEqual(1, r.sessionIdsDescending.Count, "replay-less session not ranked");
            // And the available session's cohort of one sits at percentile 50:
            Assert.AreEqual(50f, Feature(a, "a", "replayFrameCount").percentile, 0.01f);
        }

        [Test]
        public void EmptyDataset_YieldsValidEmptyAnalysis_NeverThrows()
        {
            var a = AnalysisEngine.Analyze(Dataset());
            Assert.AreEqual(0, a.sessions.Count);
            Assert.AreEqual(0, a.outlierCandidates.Count);
            Assert.AreEqual(0, a.sourceSessionCount);

            Assert.DoesNotThrow(() => AnalysisEngine.Analyze(null));
        }

        [Test]
        public void Provenance_CopiesSourceIdentity()
        {
            var a = AnalysisEngine.Analyze(Dataset(Row("a", 1f), Row("b", 2f)));
            Assert.AreEqual("2026-08-03T10:00:00Z", a.sourceGeneratedUtc);
            Assert.AreEqual(2, a.sourceSessionCount);
        }

        [Test]
        public void Analysis_IsDeterministic_ExceptTimestamp()
        {
            var ds = Dataset(Row("a", 2f), Row("b", 4f), Row("c", 6f, replay: false));
            var a = AnalysisEngine.Analyze(ds);
            var b = AnalysisEngine.Analyze(ds);
            a.generatedUtc = b.generatedUtc = "";
            Assert.AreEqual(JsonUtility.ToJson(a), JsonUtility.ToJson(b));
        }

        // --------------------------------------------------------------- store

        [Test]
        public void Store_RoundTrips_AtRoot()
        {
            string dir = Directory.CreateDirectory(Path.Combine(
                Path.GetTempPath(), "unityqa-test-" + Guid.NewGuid().ToString("N"))).FullName;
            try
            {
                var a = AnalysisEngine.Analyze(Dataset(Row("a", 2f), Row("b", 6f)));
                string path = AnalysisStore.Save(a, dir);
                Assert.AreEqual(Path.Combine(dir, AnalysisStore.FileName), path);

                DatasetAnalysis back = AnalysisStore.Load(dir);
                Assert.IsNotNull(back);
                Assert.AreEqual(a.sessions.Count, back.sessions.Count);
                Assert.AreEqual(a.rankings.Count, back.rankings.Count);
                Assert.AreEqual(
                    Feature(a, "a", "totalDistance").zScore,
                    back.sessions.Find(s => s.sessionId == "a")
                        .features.Find(f => f.name == "totalDistance").zScore, 1e-5f);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }
    }
}
