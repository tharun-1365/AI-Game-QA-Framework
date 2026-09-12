// -----------------------------------------------------------------------------
// UnityQA — QAPaths.cs                                     (M5 Slice D, D-013)
//
// PURPOSE
//   The single authority on WHERE UnityQA artifacts live. Every system that
//   persists or loads a cross-session artifact resolves its root here — one
//   predictable location instead of paths assembled ad hoc per call site.
//
// WHY PROJECT-LOCAL AND NOT persistentDataPath (decision D-013)
//   persistentDataPath buries research artifacts in AppData/LocalLow, which is
//   hostile to every workflow this project actually has: debugging (find the
//   session you just ran), IEEE evidence collection (package the artifacts
//   behind a table), dataset sharing, and diff-based inspection. In the
//   EDITOR — where 100% of current development happens — everything lands in
//   <project>/QAData/ instead, next to Assets/, visible in any file explorer
//   and one click from the repo. In a PLAYER BUILD (no project folder exists)
//   the same tree lives under persistentDataPath/QAData — the layout is
//   identical, only the base moves.
//
// LAYOUT (all under DataRoot)
//   Sessions/   one folder per session — session.json, events.jsonl,
//               replay.json, validation.json, features.json (the per-session
//               artifact set is FROZEN by EVENT-SCHEMA.md; it stays together
//               so a session folder remains self-contained and shareable),
//               plus catalog.json at the root of Sessions/.
//   Datasets/   dataset.json + features.csv (cross-session, M4.B)
//   Analysis/   analysis.json (M5.A)
//   Reports/    oracle-results.json (M5.B) and future M7 reports
//   Exports/    reserved for packaged evidence (zips, paper tables)
//
// COMPATIBILITY
//   LegacySessionsRoot points at the pre-D-013 location so existing data can
//   be migrated (ReplayManager ▸ Migrate Legacy Sessions). Nothing reads the
//   legacy path implicitly — migration is explicit and logged.
// -----------------------------------------------------------------------------

using System.IO;
using UnityEngine;

namespace UnityQA.Core
{
    /// <summary>Canonical artifact locations. See D-013 in docs/MODULES.md.</summary>
    public static class QAPaths
    {
        public const string DataFolderName = "QAData";

        private static string dataRoot; // resolved once; Application.* is main-thread-only

        /// <summary>Editor: &lt;project&gt;/QAData. Player build: persistentDataPath/QAData.</summary>
        public static string DataRoot
        {
            get
            {
                if (dataRoot == null)
                {
                    string basePath = Application.isEditor
                        ? Directory.GetParent(Application.dataPath).FullName
                        : Application.persistentDataPath;
                    dataRoot = Path.Combine(basePath, DataFolderName);
                }
                return dataRoot;
            }
        }

        /// <summary>Session folders + catalog.json (FR-1.9's root, relocated by D-013).</summary>
        public static string SessionsRoot => Path.Combine(DataRoot, "Sessions");

        /// <summary>dataset.json + features.csv (M4.B).</summary>
        public static string DatasetsRoot => Path.Combine(DataRoot, "Datasets");

        /// <summary>analysis.json (M5.A).</summary>
        public static string AnalysisRoot => Path.Combine(DataRoot, "Analysis");

        /// <summary>oracle-results.json (M5.B) and future developer-facing reports (M7).</summary>
        public static string ReportsRoot => Path.Combine(DataRoot, "Reports");

        /// <summary>Reserved for packaged evidence (IEEE tables, shared datasets).</summary>
        public static string ExportsRoot => Path.Combine(DataRoot, "Exports");

        /// <summary>Where sessions lived before D-013 — migration source only.</summary>
        public static string LegacySessionsRoot =>
            Path.Combine(Application.persistentDataPath, "UnityQA", "Sessions");
    }
}
