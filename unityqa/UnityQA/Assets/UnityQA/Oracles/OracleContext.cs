// -----------------------------------------------------------------------------
// UnityQA — OracleContext.cs                                     (M5 Slice B)
//
// PURPOSE
//   Everything an oracle may look at when judging one session — the full
//   evidence file, assembled once by the factory so individual oracles never
//   do file I/O (that is what keeps Evaluate pure and deterministic).
//
//   Fields may be null when the underlying artifact does not exist for a
//   session (no replay, never validated…). Oracles must null-check and
//   return null ("not applicable") rather than failing a session for
//   missing optional evidence — missingness policy belongs to the specific
//   oracle that cares, not to the framework.
//
// FACTORY
//   BuildContexts is the ONE place file I/O happens in this layer: it walks
//   the already-loaded dataset/analysis (row order = context order =
//   deterministic) and attaches the catalog entry and per-folder
//   validation.json. Reuses ReplayCatalog and the existing stores — no new
//   parsers, no second folder walker.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using System.IO;
using UnityQA.Analysis;
using UnityQA.Features;
using UnityQA.Replay;

namespace UnityQA.Oracles
{
    /// <summary>The evidence available to oracles for one session.</summary>
    public sealed class OracleContext
    {
        public string SessionId;
        public string SessionFolder;

        public SessionFeatures Features;          // features.json row (never null)
        public SessionAnalysis Analysis;           // this session's slice of analysis.json
        public FeatureDataset Dataset;             // the whole dataset (shared reference)
        public DatasetAnalysis DatasetAnalysis;    // the whole analysis (shared reference)
        public ReplayMetadata Metadata;            // catalog entry (may be null)
        public ReplayValidationResult Validation;  // validation.json (null if never validated)

        /// <summary>M5.D: the session's recorded benchmark outcome — the
        /// payload of the LAST RunEnded event in events.jsonl ("Success",
        /// "SpikeDeath", "OutOfBounds", "Quit"). Null for sessions recorded
        /// before BenchGame v2 (or with telemetry off) — outcome-consuming
        /// oracles SKIP those rather than judging blind.</summary>
        public string RunOutcome;

        /// <summary>M6.B: a compact spatial summary of the session's movement,
        /// derived by the factory from the SAME PlayerSample telemetry the
        /// trajectory/features already consume (no new event or file field —
        /// this is analysis of existing evidence, computed once at the I/O
        /// point so Evaluate stays pure). Null when the session has no usable
        /// trajectory; SoftLockOracle needs it to tell "confined" (a small
        /// region) from "traversed" (a large one), which the scalar features
        /// alone cannot express. `available` is false for a present-but-empty
        /// trajectory.</summary>
        public TrajectorySummary Trajectory;
    }

    /// <summary>Bounding-box + duration summary of a session's PlayerSample
    /// trajectory (M6.B). Spatial EXTENT distinguishes a soft lock (large path
    /// length inside a tiny region) from ordinary play (path length ≈ extent);
    /// the scalar SessionFeatures carry path length but no extent.</summary>
    public sealed class TrajectorySummary
    {
        public bool available;
        public int sampleCount;
        public float minX, maxX, minY, maxY;
        public float firstT, lastT;

        // M6.B fix: a SECOND bounding box over only the TRAILING window (the
        // last SoftLockOracle.ObservationWindowSec of samples) — the region the
        // player occupies at the END of the run. The whole-session box (above)
        // is inflated by the approach from spawn to wherever the player ends
        // up, so it cannot tell "confined now" from "travelled here". The tail
        // box is the confinement measure; the whole-session box stays for
        // context/evidence. Derived from the SAME PlayerSample telemetry — no
        // new event/file/schema field. When the session is shorter than the
        // window, the tail box equals the whole-session box.
        public int tailSampleCount;
        public float tailMinX, tailMaxX, tailMinY, tailMaxY;

        public float SpanX => maxX - minX;
        public float SpanY => maxY - minY;
        public float TailSpanX => tailMaxX - tailMinX;
        public float TailSpanY => tailMaxY - tailMinY;
        public float DurationSec => lastT - firstT;
    }

    /// <summary>Deterministic context assembly from the existing artifacts.</summary>
    public static class OracleContextFactory
    {
        /// <summary>One context per dataset row, in dataset (chronological) order.</summary>
        public static List<OracleContext> BuildContexts(string sessionsRoot,
                                                        FeatureDataset dataset,
                                                        DatasetAnalysis analysis)
        {
            var contexts = new List<OracleContext>();
            if (dataset?.rows == null) return contexts;

            ReplayCatalog.CatalogDocument catalog = ReplayCatalog.Scan(sessionsRoot);

            foreach (SessionFeatures row in dataset.rows)
            {
                var ctx = new OracleContext
                {
                    SessionId = row.sessionId,
                    Features = row,
                    Dataset = dataset,
                    DatasetAnalysis = analysis
                };

                if (analysis?.sessions != null)
                    ctx.Analysis = analysis.sessions.Find(s => s.sessionId == row.sessionId);

                ctx.Metadata = catalog.entries.Find(e => e.sessionId == row.sessionId);
                ctx.SessionFolder = ctx.Metadata != null
                    ? ctx.Metadata.folderPath
                    : Path.Combine(sessionsRoot, row.sessionFolderName ?? "");

                string validationPath = Path.Combine(ctx.SessionFolder, "validation.json");
                if (File.Exists(validationPath))
                {
                    try
                    {
                        ctx.Validation = UnityEngine.JsonUtility
                            .FromJson<ReplayValidationResult>(File.ReadAllText(validationPath));
                    }
                    catch (System.Exception) { /* damaged file → null, oracle decides */ }
                }

                string eventsPath = Path.Combine(ctx.SessionFolder, "events.jsonl");
                ctx.RunOutcome = ReadLastRunOutcome(eventsPath);
                ctx.Trajectory = SummarizeTrajectory(eventsPath);

                contexts.Add(ctx);
            }
            return contexts;
        }

        /// <summary>
        /// M6.B: bounding-box + duration of the session's PlayerSample
        /// trajectory, read through the SAME SessionTrajectory loader the
        /// feature extractor and validator use (no second parser). Returns
        /// null when there is no events.jsonl to read; an `available == false`
        /// summary when the file has no samples. This is the factory's one
        /// place to do trajectory I/O — oracles receive the finished summary.
        /// </summary>
        private static TrajectorySummary SummarizeTrajectory(string eventsJsonlPath)
        {
            if (string.IsNullOrEmpty(eventsJsonlPath) || !File.Exists(eventsJsonlPath)) return null;

            SessionTrajectory traj = SessionTrajectory.Load(eventsJsonlPath);
            if (traj == null || traj.Samples.Count == 0)
                return new TrajectorySummary { available = false, sampleCount = traj?.Samples.Count ?? 0 };

            var s = new TrajectorySummary
            {
                available = true,
                sampleCount = traj.Samples.Count,
                minX = traj.Samples[0].x,
                maxX = traj.Samples[0].x,
                minY = traj.Samples[0].y,
                maxY = traj.Samples[0].y,
                firstT = traj.Samples[0].t,
                lastT = traj.Samples[traj.Samples.Count - 1].t
            };
            foreach (TrajectorySample p in traj.Samples)
            {
                if (p.x < s.minX) s.minX = p.x;
                if (p.x > s.maxX) s.maxX = p.x;
                if (p.y < s.minY) s.minY = p.y;
                if (p.y > s.maxY) s.maxY = p.y;
            }

            // Trailing-window box: samples in the last ObservationWindowSec. The
            // window length is SoftLockOracle's (the one consumer that needs
            // confinement), documented there. lastT is itself a sample time, so
            // the window is never empty; if the whole run is shorter than the
            // window, every sample qualifies and the tail box == whole box.
            float windowStart = s.lastT - SoftLockOracle.ObservationWindowSec;
            bool tailInit = false;
            foreach (TrajectorySample p in traj.Samples)
            {
                if (p.t < windowStart) continue;
                if (!tailInit)
                {
                    s.tailMinX = s.tailMaxX = p.x;
                    s.tailMinY = s.tailMaxY = p.y;
                    tailInit = true;
                }
                else
                {
                    if (p.x < s.tailMinX) s.tailMinX = p.x;
                    if (p.x > s.tailMaxX) s.tailMaxX = p.x;
                    if (p.y < s.tailMinY) s.tailMinY = p.y;
                    if (p.y > s.tailMaxY) s.tailMaxY = p.y;
                }
                s.tailSampleCount++;
            }
            if (!tailInit) // defensive: window caught no sample → use whole box
            {
                s.tailMinX = s.minX; s.tailMaxX = s.maxX;
                s.tailMinY = s.minY; s.tailMaxY = s.maxY;
                s.tailSampleCount = s.sampleCount;
            }

            return s;
        }

        /// <summary>
        /// M5.D: the outcome of the session's LAST RunEnded event (a session
        /// may span several runs via manual reset — the final state is the
        /// session's outcome; policy documented in EVENT-SCHEMA §5f). Same
        /// anchored parsing of our own writer's fixed format as
        /// SessionTrajectory; null when the file or event is absent.
        /// </summary>
        public static string ReadLastRunOutcome(string eventsJsonlPath)
        {
            if (string.IsNullOrEmpty(eventsJsonlPath) || !File.Exists(eventsJsonlPath)) return null;

            const string marker = "\"type\":\"RunEnded\"";
            const string anchor = "\"outcome\":\"";
            string outcome = null;
            foreach (string line in File.ReadLines(eventsJsonlPath))
            {
                if (!line.Contains(marker)) continue;
                int idx = line.IndexOf(anchor, System.StringComparison.Ordinal);
                if (idx < 0) continue;
                int start = idx + anchor.Length;
                int end = line.IndexOf('"', start);
                if (end > start) outcome = line.Substring(start, end - start); // last one wins
            }
            return outcome;
        }
    }
}
