// -----------------------------------------------------------------------------
// UnityQA — FeatureDatasetBuilder.cs                             (M4 Slice B)
//
// PURPOSE
//   Turns the whole Sessions root into one FeatureDataset: enumerate sessions
//   via the EXISTING ReplayCatalog (no second folder-walker), obtain each
//   session's SessionFeatures (cached features.json via FeatureStore when
//   present, FeatureExtractor when not — and the freshly extracted vector is
//   persisted, so builds are incremental by default), then compute the
//   availability-gated statistics.
//
// THE SELECTOR TABLE (the slice's one important data structure)
//   A single canonical, ORDERED list of (name, value-accessor, availability)
//   triples defines "what is a numeric feature" exactly once. Statistics and
//   the CSV exporter both consume this table — column order, statistic order
//   and feature naming can never drift apart because they are the same list.
//   Adding a feature in a future slice = one entry here + a schema note.
//
// DETERMINISM
//   Same folders in, same dataset out (generatedUtc excepted): rows sorted
//   ascending by folder name, statistics in table order, all arithmetic in
//   plain float accumulation over ordered rows. Pinned by test.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityQA.Replay;

namespace UnityQA.Features
{
    /// <summary>One canonical numeric feature: name + accessor + availability gate.</summary>
    public sealed class FeatureSelector
    {
        public readonly string Name;
        public readonly Func<SessionFeatures, float> Get;
        public readonly Func<SessionFeatures, bool> Available;

        public FeatureSelector(string name, Func<SessionFeatures, float> get,
                               Func<SessionFeatures, bool> available)
        {
            Name = name;
            Get = get;
            Available = available;
        }
    }

    /// <summary>Catalog-driven dataset construction + statistics.</summary>
    public static class FeatureDatasetBuilder
    {
        private static readonly Func<SessionFeatures, bool> Always = _ => true;
        private static readonly Func<SessionFeatures, bool> Events = f => f.eventsAvailable;
        private static readonly Func<SessionFeatures, bool> Replay = f => f.replayAvailable;
        private static readonly Func<SessionFeatures, bool> Validation = f => f.validationAvailable;

        /// <summary>
        /// The canonical feature list — the single source of truth for
        /// statistics AND csv columns. Order is part of the schema contract.
        /// </summary>
        public static readonly IReadOnlyList<FeatureSelector> Selectors = new[]
        {
            new FeatureSelector("sessionDurationSec",    f => f.sessionDurationSec,        Always),
            new FeatureSelector("trajectoryDurationSec", f => f.trajectoryDurationSec,     Events),
            new FeatureSelector("trajectorySamples",     f => f.trajectorySamples,         Events),
            new FeatureSelector("totalDistance",         f => f.totalDistance,             Events),
            new FeatureSelector("averageSpeed",          f => f.averageSpeed,              Events),
            new FeatureSelector("maxSpeed",              f => f.maxSpeed,                  Events),
            new FeatureSelector("airtimeSec",            f => f.airtimeSec,                Events),
            new FeatureSelector("airtimeFraction",       f => f.airtimeFraction,           Events),
            new FeatureSelector("idleTimeSec",           f => f.idleTimeSec,               Events),
            new FeatureSelector("idleFraction",          f => f.idleFraction,              Events),
            new FeatureSelector("jumpCount",             f => f.jumpCount,                 Events),
            new FeatureSelector("landedCount",           f => f.landedCount,               Events),
            new FeatureSelector("collisionCount",        f => f.collisionCount,            Events),
            new FeatureSelector("deaths",                f => f.deaths,                    Events),
            new FeatureSelector("checkpointsReached",    f => f.checkpointsReached,        Events),
            new FeatureSelector("tokensCollected",       f => f.tokensCollected,           Events),
            new FeatureSelector("replayFrameCount",      f => f.replayFrameCount,          Replay),
            new FeatureSelector("directionChanges",      f => f.directionChanges,          Replay),
            new FeatureSelector("inputJumpPresses",      f => f.inputJumpPresses,          Replay),
            new FeatureSelector("validationMaxDeviation", f => f.validationMaxDeviation,   Validation),
        };

        /// <summary>
        /// Build the dataset for a Sessions root. Cached features.json is
        /// reused unless <paramref name="forceReextract"/> (then every session
        /// is re-extracted and its features.json rewritten). Never throws.
        /// </summary>
        public static FeatureDataset Build(string sessionsRoot, bool forceReextract = false)
        {
            var dataset = new FeatureDataset
            {
                schemaVersion = FeatureDataset.CurrentSchemaVersion,
                generatedUtc = DateTime.UtcNow.ToString("o"),
                rows = new List<SessionFeatures>(),
                statistics = new List<FeatureStatistic>()
            };

            ReplayCatalog.CatalogDocument catalog = ReplayCatalog.Scan(sessionsRoot);
            foreach (ReplayMetadata entry in catalog.entries)
            {
                SessionFeatures features = forceReextract ? null : FeatureStore.Load(entry.folderPath);
                if (features == null)
                {
                    features = FeatureExtractor.Extract(entry.folderPath);
                    if (features == null)
                    {
                        dataset.skippedSessions++;
                        continue;
                    }
                    FeatureStore.Save(features, entry.folderPath); // incremental builds stay cheap
                }
                dataset.rows.Add(features);
            }

            // Chronological order for the dataset (catalog was newest-first).
            dataset.rows.Sort((a, b) =>
                string.CompareOrdinal(a.sessionFolderName, b.sessionFolderName));
            dataset.sessionCount = dataset.rows.Count;

            ComputeStatistics(dataset);
            return dataset;
        }

        /// <summary>Availability-gated per-feature statistics (contract in FeatureDataset.cs).</summary>
        public static void ComputeStatistics(FeatureDataset dataset)
        {
            dataset.statistics.Clear();
            foreach (FeatureSelector selector in Selectors)
            {
                var stat = new FeatureStatistic { name = selector.Name };
                float sum = 0f, sumSq = 0f;
                float min = float.MaxValue, max = float.MinValue;

                foreach (SessionFeatures row in dataset.rows)
                {
                    if (!selector.Available(row)) continue;
                    float v = selector.Get(row);
                    stat.sampleCount++;
                    sum += v;
                    sumSq += v * v;
                    if (v < min) min = v;
                    if (v > max) max = v;
                }

                if (stat.sampleCount > 0)
                {
                    stat.mean = sum / stat.sampleCount;
                    float variance = Mathf.Max(0f, sumSq / stat.sampleCount - stat.mean * stat.mean);
                    stat.std = Mathf.Sqrt(variance); // population std (÷N), per contract
                    stat.min = min;
                    stat.max = max;
                }
                dataset.statistics.Add(stat);
            }
        }
    }
}
