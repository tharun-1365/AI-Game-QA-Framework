// -----------------------------------------------------------------------------
// UnityQA.Adapters — ReplayManager.cs                            (M3 Slice D)
//
// PURPOSE
//   The single front door to the whole replay system: refresh/browse the
//   catalog, play any replay by session ID, validate any replay by session
//   ID — each operation DELEGATED to the existing component that already
//   owns it (ReplayPlayer, ReplayValidator). This class orchestrates; it
//   implements nothing twice.
//
// LIFECYCLE DISCIPLINE (lesson from the ReplayValidator hotfix, now standard)
//   Every [ContextMenu] entry point works without assuming Awake ran:
//   EnsureRefs() lazily acquires siblings, and operations that need a live
//   game guard on Application.isPlaying with an instructive message.
//   Catalog refresh is deliberately EDIT-MODE SAFE — it is pure file I/O,
//   so you can browse your sessions without pressing Play.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityQA.Logging;
using UnityQA.Replay;

namespace UnityQA.Adapters
{
    /// <summary>
    /// Replay-system front door. Scene setup: on "[QA]" (requires ReplayPlayer;
    /// ReplayValidator optional but needed for validation delegation).
    /// </summary>
    [RequireComponent(typeof(ReplayPlayer))]
    public sealed class ReplayManager : MonoBehaviour
    {
        private ReplayPlayer player;
        private ReplayValidator validator; // optional sibling

        private ReplayCatalog.CatalogDocument catalog;

        /// <summary>Newest-first entries from the last refresh (empty until one runs).</summary>
        public IReadOnlyList<ReplayMetadata> Entries =>
            catalog != null ? (IReadOnlyList<ReplayMetadata>)catalog.entries
                            : Array.Empty<ReplayMetadata>();

        /// <summary>Raised after every catalog refresh.</summary>
        public event Action<IReadOnlyList<ReplayMetadata>> CatalogRefreshed;

        private void Awake()
        {
            EnsureRefs();
        }

        private void EnsureRefs()
        {
            if (player == null) player = GetComponent<ReplayPlayer>();
            if (validator == null) validator = GetComponent<ReplayValidator>();
        }

        // ------------------------------------------------------------ catalog

        /// <summary>Rescan the Sessions root, persist catalog.json, log a summary.
        /// Edit-mode safe (pure file I/O).</summary>
        [ContextMenu("Refresh Catalog (logs summary)")]
        public void RefreshCatalog()
        {
            catalog = ReplayCatalog.ScanDefault();
            string path = ReplayCatalog.Save(catalog, QALogger.SessionsRoot);

            Debug.Log($"[UnityQA] Catalog: {catalog.sessionCount} session(s), " +
                      $"{CountWithReplay()} with replays, {catalog.skippedFolders} skipped → {path}");
            foreach (ReplayMetadata e in catalog.entries)
            {
                Debug.Log($"[UnityQA]   {e.folderName} | {e.level} | {e.durationSec:F1}s | " +
                          (e.hasReplay ? $"replay {e.replayFrameCount}f" : "no replay") +
                          (e.hasValidation
                              ? $" | validated {e.validationVerdict} (max {e.validationMaxDeviation:F3}u)"
                              : "") +
                          (e.status == "open" ? " | ⚠ CRASHED (manifest open)" : ""));
            }

            CatalogRefreshed?.Invoke(Entries);
        }

        // ------------------------------------------------------------ playback

        /// <summary>Play the replay of a cataloged session. False if unavailable.</summary>
        public bool PlayBySessionId(string sessionId)
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[UnityQA] Replay playback requires Play mode.");
                return false;
            }
            EnsureRefs();

            ReplayMetadata entry = FindEntry(sessionId);
            if (entry == null || !entry.hasReplay)
            {
                Debug.LogError($"[UnityQA] No cataloged replay for session '{sessionId}' — " +
                               "refresh the catalog or check the ID.");
                return false;
            }

            player.SetReplayFile(Path.Combine(entry.folderPath, ReplayFileStore.FileName));
            player.Play();
            return player.IsPlaying;
        }

        [ContextMenu("Play Newest Replay")]
        public void PlayNewest()
        {
            ReplayMetadata entry = NewestWithReplay();
            if (entry != null) PlayBySessionId(entry.sessionId);
            else Debug.LogWarning("[UnityQA] No session with a replay found.");
        }

        // ---------------------------------------------------------- validation

        /// <summary>Validate a cataloged session's replay (delegates to ReplayValidator).</summary>
        public bool ValidateBySessionId(string sessionId)
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[UnityQA] Replay validation requires Play mode.");
                return false;
            }
            EnsureRefs();
            if (validator == null)
            {
                Debug.LogError("[UnityQA] No ReplayValidator on this GameObject — add one to validate.");
                return false;
            }

            ReplayMetadata entry = FindEntry(sessionId);
            if (entry == null || !entry.hasReplay)
            {
                Debug.LogError($"[UnityQA] No cataloged replay for session '{sessionId}'.");
                return false;
            }

            validator.Validate(entry.folderPath);
            return validator.IsValidating;
        }

        [ContextMenu("Validate Newest Replay")]
        public void ValidateNewest()
        {
            ReplayMetadata entry = NewestWithReplay();
            if (entry != null) ValidateBySessionId(entry.sessionId);
            else Debug.LogWarning("[UnityQA] No session with a replay found.");
        }

        // ------------------------------------------------------ features (M4.A)

        /// <summary>Extract and persist features.json for a cataloged session.
        /// Edit-mode safe (pure file I/O, like the catalog). Returns the
        /// features, or null on failure.</summary>
        public Features.SessionFeatures ExtractFeaturesBySessionId(string sessionId)
        {
            ReplayMetadata entry = FindEntry(sessionId);
            if (entry == null)
            {
                Debug.LogError($"[UnityQA] No cataloged session '{sessionId}' — refresh the catalog?");
                return null;
            }

            Features.SessionFeatures f = Features.FeatureExtractor.Extract(entry.folderPath);
            if (f == null) return null;

            string path = Features.FeatureStore.Save(f, entry.folderPath);
            Debug.Log($"[UnityQA] Features extracted — dist {f.totalDistance:F1}u, " +
                      $"avg {f.averageSpeed:F2}u/s, max {f.maxSpeed:F2}u/s, jumps {f.jumpCount}, " +
                      $"air {f.airtimeFraction:P0}, idle {f.idleFraction:P0}, " +
                      $"dirChanges {f.directionChanges} → {path}");
            return f;
        }

        /// <summary>M4.B: build the cross-session dataset (dataset.json +
        /// features.csv at the Sessions root). Edit-mode safe (pure file I/O).
        /// Returns the dataset, or null only on catastrophic failure.</summary>
        public Features.FeatureDataset BuildFeatureDataset(bool forceReextract = false)
        {
            Features.FeatureDataset ds =
                Features.FeatureDatasetBuilder.Build(QALogger.SessionsRoot, forceReextract);
            string jsonPath = Features.FeatureDatasetStore.SaveJson(ds, QALogger.SessionsRoot);
            Features.FeatureDatasetStore.SaveCsv(ds, QALogger.SessionsRoot);

            Debug.Log($"[UnityQA] Dataset built — {ds.sessionCount} rows, " +
                      $"{ds.skippedSessions} skipped, {ds.statistics.Count} feature statistics " +
                      $"→ {jsonPath} (+ {Features.FeatureDatasetStore.CsvFileName})");
            return ds;
        }

        [ContextMenu("Build Feature Dataset (All Sessions)")]
        public void BuildFeatureDatasetMenu() => BuildFeatureDataset(false);

        /// <summary>M5.A: descriptive analysis of the dataset → analysis.json.
        /// Loads dataset.json (building it first if absent — orchestration
        /// courtesy, logged); edit-mode safe (pure file I/O). Facts only —
        /// no classification happens anywhere below this call.</summary>
        [ContextMenu("Analyze Dataset")]
        public void AnalyzeDataset()
        {
            Features.FeatureDataset dataset =
                Features.FeatureDatasetStore.LoadJson(QALogger.SessionsRoot);
            if (dataset == null)
            {
                Debug.Log("[UnityQA] No dataset.json found — building the dataset first.");
                dataset = BuildFeatureDataset(false);
                if (dataset == null) return;
            }

            Analysis.DatasetAnalysis analysis = Analysis.AnalysisEngine.Analyze(dataset);
            string path = Analysis.AnalysisStore.Save(analysis, QALogger.SessionsRoot);

            Debug.Log($"[UnityQA] Analysis complete — {analysis.sessions.Count} session(s), " +
                      $"{analysis.rankings.Count} feature rankings, " +
                      $"{analysis.outlierCandidates.Count} numeric outlier candidate(s) " +
                      $"(|z| ≥ {Analysis.AnalysisEngine.OutlierZThreshold}) → {path}");
        }

        [ContextMenu("Extract Features (Newest Session)")]
        public void ExtractFeaturesNewest()
        {
            if (catalog == null || catalog.entries.Count == 0) RefreshCatalog();
            if (catalog.entries.Count > 0) ExtractFeaturesBySessionId(catalog.entries[0].sessionId);
            else Debug.LogWarning("[UnityQA] No sessions cataloged.");
        }

        // ------------------------------------------------------------- helpers

        private ReplayMetadata FindEntry(string sessionId)
        {
            if (catalog == null || catalog.entries.Count == 0) RefreshCatalog();
            foreach (ReplayMetadata e in catalog.entries)
                if (e.sessionId == sessionId) return e;
            return null;
        }

        private ReplayMetadata NewestWithReplay()
        {
            if (catalog == null || catalog.entries.Count == 0) RefreshCatalog();
            foreach (ReplayMetadata e in catalog.entries) // newest-first order
                if (e.hasReplay) return e;
            return null;
        }

        private int CountWithReplay()
        {
            int n = 0;
            foreach (ReplayMetadata e in catalog.entries) if (e.hasReplay) n++;
            return n;
        }
    }
}
