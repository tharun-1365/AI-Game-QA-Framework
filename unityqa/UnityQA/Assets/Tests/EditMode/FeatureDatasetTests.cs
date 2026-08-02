// -----------------------------------------------------------------------------
// UnityQA Tests — FeatureDatasetTests.cs                    (M4 Slice B tests)
//
// Dataset construction over synthetic session folders built with the real
// production writers; statistics checked against hand-computed values;
// CSV format pinned exactly (header, ordering, invariant culture, honest
// empty cells for unavailable features).
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityQA.Core;
using UnityQA.Features;
using UnityQA.Logging;
using UnityQA.Replay;

namespace UnityQA.Tests
{
    public sealed class FeatureDatasetTests
    {
        private string root;
        private QAConfig config;

        [SetUp]
        public void SetUp()
        {
            root = Directory.CreateDirectory(Path.Combine(
                Path.GetTempPath(), "unityqa-test-" + Guid.NewGuid().ToString("N"))).FullName;
            config = ScriptableObject.CreateInstance<QAConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(root, true); } catch { }
            UnityEngine.Object.DestroyImmediate(config);
        }

        /// <summary>
        /// Session with a straight-line run: `samples` PlayerSamples at 10 Hz,
        /// constant speed → totalDistance = (samples−1) × speed × 0.1.
        /// Optionally with a replay (directionChanges = 0) and telemetry.
        /// </summary>
        private QASessionInfo MakeSession(string folderName, float speed, int samples,
                                          bool withEvents = true, bool withReplay = true)
        {
            string folder = Directory.CreateDirectory(Path.Combine(root, folderName)).FullName;
            var session = new QASessionInfo("DatasetLevel", () => 0f, DateTime.UtcNow, "u", "a");
            SessionManifest.WriteClosed(session, config, folder, 10f, 50);

            if (withEvents)
            {
                var w = new JsonLineWriter();
                using var f = new StreamWriter(Path.Combine(folder, "events.jsonl"));
                f.WriteLine(w.HeaderLine("events", session));
                for (int i = 0; i < samples; i++)
                {
                    var payload = new Dictionary<string, object>
                        { { "vx", speed }, { "vy", 0f }, { "g", 1 } };
                    f.WriteLine(w.EventLine(new QAEvent(session.SessionId, i, i * 0.1f, i,
                        QAEventType.PlayerSample, new Vector2(i * speed * 0.1f, 1.45f), payload)));
                }
            }

            if (withReplay)
            {
                var frames = new List<ReplayFrame>();
                for (int i = 0; i < 50; i++)
                    frames.Add(new ReplayFrame { frameNumber = i, timestamp = i * 0.016f, horizontal = 1f });
                ReplayFileStore.Save(ReplayRecording.Create(session.SessionId, "t0", frames), folder);
            }
            return session;
        }

        [Test]
        public void Build_OneRowPerSession_OldestFirst()
        {
            MakeSession("20260802-110000_bbbb0000", speed: 2f, samples: 11);
            MakeSession("20260802-100000_aaaa0000", speed: 6f, samples: 11);

            var ds = FeatureDatasetBuilder.Build(root);

            Assert.AreEqual(2, ds.sessionCount);
            Assert.AreEqual(0, ds.skippedSessions);
            Assert.AreEqual("20260802-100000_aaaa0000", ds.rows[0].sessionFolderName,
                "dataset rows must be chronological (ascending), unlike the catalog");
            Assert.AreEqual(FeatureDataset.CurrentSchemaVersion, ds.schemaVersion);
        }

        [Test]
        public void Build_PersistsExtractedFeatures_AndReusesCacheNextTime()
        {
            MakeSession("20260802-100000_cache000", speed: 4f, samples: 11);
            FeatureDatasetBuilder.Build(root);

            string featuresPath = Path.Combine(root, "20260802-100000_cache000", FeatureStore.FileName);
            Assert.IsTrue(File.Exists(featuresPath), "build must persist freshly extracted features");

            // Poison the cache with a recognizable value; a non-forced rebuild
            // must trust it (cache reuse), a forced rebuild must overwrite it.
            var cached = FeatureStore.Load(Path.Combine(root, "20260802-100000_cache000"));
            cached.totalDistance = 12345f;
            FeatureStore.Save(cached, Path.Combine(root, "20260802-100000_cache000"));

            var cachedBuild = FeatureDatasetBuilder.Build(root, forceReextract: false);
            Assert.AreEqual(12345f, cachedBuild.rows[0].totalDistance, 1e-3f, "cache must be reused");

            var forcedBuild = FeatureDatasetBuilder.Build(root, forceReextract: true);
            Assert.AreEqual(4f, forcedBuild.rows[0].totalDistance, 0.01f, "forced rebuild re-extracts");
        }

        [Test]
        public void Statistics_MatchHandComputedValues()
        {
            // Two sessions, 11 samples each: distances 1s×2u/s = 2.0 and 1s×6u/s = 6.0.
            // mean 4, population std = √(((2−4)²+(6−4)²)/2) = 2, min 2, max 6.
            MakeSession("20260802-100000_stat0001", speed: 2f, samples: 11);
            MakeSession("20260802-110000_stat0002", speed: 6f, samples: 11);

            var ds = FeatureDatasetBuilder.Build(root);
            FeatureStatistic dist = ds.statistics.Find(s => s.name == "totalDistance");

            Assert.AreEqual(2, dist.sampleCount);
            Assert.AreEqual(4f, dist.mean, 0.02f);
            Assert.AreEqual(2f, dist.std, 0.02f);
            Assert.AreEqual(2f, dist.min, 0.02f);
            Assert.AreEqual(6f, dist.max, 0.02f);
        }

        [Test]
        public void Statistics_AvailabilityGated_NotPollutedByMissingGroups()
        {
            MakeSession("20260802-100000_full0000", speed: 6f, samples: 11, withReplay: true);
            MakeSession("20260802-110000_norep000", speed: 6f, samples: 11, withReplay: false);

            var ds = FeatureDatasetBuilder.Build(root);
            FeatureStatistic frames = ds.statistics.Find(s => s.name == "replayFrameCount");

            Assert.AreEqual(1, frames.sampleCount,
                "the replay-less session must not contribute a fake 0 to replay statistics");
            Assert.AreEqual(50f, frames.mean, 1e-3f);
        }

        [Test]
        public void Statistics_OrderMatchesSelectorTable()
        {
            MakeSession("20260802-100000_ord00000", speed: 2f, samples: 5);
            var ds = FeatureDatasetBuilder.Build(root);
            Assert.AreEqual(FeatureDatasetBuilder.Selectors.Count, ds.statistics.Count);
            for (int i = 0; i < ds.statistics.Count; i++)
                Assert.AreEqual(FeatureDatasetBuilder.Selectors[i].Name, ds.statistics[i].name);
        }

        [Test]
        public void Build_IsDeterministic_ExceptTimestamp()
        {
            MakeSession("20260802-100000_det00001", speed: 3f, samples: 11);
            MakeSession("20260802-110000_det00002", speed: 5f, samples: 11);

            var a = FeatureDatasetBuilder.Build(root);
            var b = FeatureDatasetBuilder.Build(root);
            a.generatedUtc = b.generatedUtc = "";
            foreach (var row in a.rows) row.extractedUtc = "";
            foreach (var row in b.rows) row.extractedUtc = "";
            Assert.AreEqual(JsonUtility.ToJson(a), JsonUtility.ToJson(b));
        }

        [Test]
        public void JsonStore_RoundTrips_AtRoot()
        {
            MakeSession("20260802-100000_json0000", speed: 2f, samples: 5);
            var ds = FeatureDatasetBuilder.Build(root);

            string path = FeatureDatasetStore.SaveJson(ds, root);
            Assert.AreEqual(Path.Combine(root, FeatureDatasetStore.JsonFileName), path);

            var back = FeatureDatasetStore.LoadJson(root);
            Assert.AreEqual(ds.sessionCount, back.sessionCount);
            Assert.AreEqual(ds.statistics.Count, back.statistics.Count);
            Assert.AreEqual(ds.rows[0].sessionId, back.rows[0].sessionId);
        }

        [Test]
        public void Csv_HeaderIsIdentityColumnsPlusSelectorTable()
        {
            MakeSession("20260802-100000_csvh0000", speed: 2f, samples: 5);
            var ds = FeatureDatasetBuilder.Build(root);

            string[] lines = FeatureDatasetStore.BuildCsv(ds).Split('\n');
            string expected = "sessionId,folderName,level,sessionStatus,validationVerdict";
            foreach (FeatureSelector s in FeatureDatasetBuilder.Selectors)
                expected += "," + s.Name;
            Assert.AreEqual(expected, lines[0]);
            Assert.AreEqual(ds.sessionCount + 2, lines.Length, "header + rows + trailing newline");
        }

        [Test]
        public void Csv_UnavailableFeature_IsEmptyCell_NotZero()
        {
            MakeSession("20260802-100000_csvm0000", speed: 6f, samples: 11, withReplay: false);
            var ds = FeatureDatasetBuilder.Build(root);

            string row = FeatureDatasetStore.BuildCsv(ds).Split('\n')[1];
            string[] cells = row.Split(',');
            int replayFrameCol = 5 + IndexOfSelector("replayFrameCount");

            Assert.AreEqual("", cells[replayFrameCol],
                "missing replay must read as an empty cell (NaN downstream), not 0");
            Assert.AreNotEqual("", cells[5 + IndexOfSelector("totalDistance")],
                "available features must still be populated");
        }

        [Test]
        public void Csv_UsesInvariantCulture_OnCommaDecimalMachines()
        {
            var original = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture =
                    new System.Globalization.CultureInfo("de-DE");
                MakeSession("20260802-100000_csvc0000", speed: 2.5f, samples: 11);
                var ds = FeatureDatasetBuilder.Build(root);
                string csv = FeatureDatasetStore.BuildCsv(ds);
                StringAssert.Contains("2.5", csv, "decimal POINT required");
                // A comma-decimal would also corrupt the column structure —
                // the header/row column-count agreement is the deeper check:
                Assert.AreEqual(csv.Split('\n')[0].Split(',').Length,
                                csv.Split('\n')[1].Split(',').Length);
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = original;
            }
        }

        private static int IndexOfSelector(string name)
        {
            for (int i = 0; i < FeatureDatasetBuilder.Selectors.Count; i++)
                if (FeatureDatasetBuilder.Selectors[i].Name == name) return i;
            throw new InvalidOperationException("unknown selector " + name);
        }
    }
}
