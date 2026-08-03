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

                contexts.Add(ctx);
            }
            return contexts;
        }
    }
}
