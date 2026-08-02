// -----------------------------------------------------------------------------
// UnityQA — ReplayCatalog.cs                                     (M3 Slice D)
//
// PURPOSE
//   The scanner and index: walks a Sessions root, builds ReplayMetadata for
//   every session folder, cross-links validations to the sessions they
//   validated, and persists the whole index as catalog.json at the root.
//
// DESIGN NOTES
//   - Pure file I/O over documents WE wrote, read back through the same
//     serializable DTOs that wrote them (SessionManifest.Manifest,
//     ReplayValidationResult) — no new parsers, no packages (D-009 lineage).
//   - replay.json is probed with a HEADER-ONLY DTO: JsonUtility fills just
//     the fields the probe declares, so the frames array is never
//     materialized — cataloging 200 sessions never allocates 200 frame
//     arrays.
//   - A folder with a missing/corrupt session.json is COUNTED and skipped,
//     never fatal: a catalog that dies on one damaged folder indexes nothing.
//   - Entries sort newest-first by folder name — the A2 timestamp-sortable
//     naming convention doing load-bearing work again.
//   - Engine-free static class: EditMode-testable, usable from editor tooling
//     and (M4) batch dataset generation alike.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityQA.Logging;

namespace UnityQA.Replay
{
    /// <summary>Scans session folders into ReplayMetadata and persists catalog.json.</summary>
    public static class ReplayCatalog
    {
        public const string CatalogFileName = "catalog.json";

        /// <summary>Wire format of catalog.json.</summary>
        [Serializable]
        public sealed class CatalogDocument
        {
            public const int CurrentSchemaVersion = 1;
            public int schemaVersion;
            public string generatedUtc;
            public int sessionCount;
            /// <summary>Folders present but unreadable (corrupt/missing session.json).</summary>
            public int skippedFolders;
            public List<ReplayMetadata> entries;
        }

        /// <summary>Header-only probe of replay.json — frames stay unparsed.</summary>
        [Serializable]
        private sealed class ReplayHeaderProbe
        {
            public int schemaVersion;
            public string sessionId;
            public string recordingStartTime;
            public int frameCount;
        }

        /// <summary>
        /// Scan a Sessions root into a newest-first metadata list.
        /// Never throws; damaged folders increment skippedFolders.
        /// </summary>
        public static CatalogDocument Scan(string sessionsRoot)
        {
            var doc = new CatalogDocument
            {
                schemaVersion = CatalogDocument.CurrentSchemaVersion,
                generatedUtc = DateTime.UtcNow.ToString("o"),
                entries = new List<ReplayMetadata>()
            };

            if (string.IsNullOrEmpty(sessionsRoot) || !Directory.Exists(sessionsRoot))
                return doc; // an empty catalog is a valid answer to "nothing recorded yet"

            string[] folders = Directory.GetDirectories(sessionsRoot);
            Array.Sort(folders);
            Array.Reverse(folders); // newest first (A2 sortable names)

            // Validations found during the walk, resolved to originals afterwards.
            var validations = new List<ReplayValidationResult>();

            foreach (string folder in folders)
            {
                ReplayMetadata entry = ReadEntry(folder);
                if (entry == null)
                {
                    doc.skippedFolders++;
                    continue;
                }
                doc.entries.Add(entry);

                string validationPath = Path.Combine(folder, "validation.json");
                if (File.Exists(validationPath))
                {
                    ReplayValidationResult v = TryRead<ReplayValidationResult>(validationPath);
                    if (v != null && !string.IsNullOrEmpty(v.originalSessionId))
                        validations.Add(v);
                }
            }

            // Cross-link: newest validation wins per original (walk order is
            // newest-first, so the FIRST hit for an original is the newest).
            foreach (ReplayValidationResult v in validations)
            {
                foreach (ReplayMetadata entry in doc.entries)
                {
                    if (entry.sessionId != v.originalSessionId) continue;
                    if (entry.hasValidation) break; // an earlier (newer) result already claimed it
                    entry.hasValidation = true;
                    entry.validationVerdict = v.verdict;
                    entry.validationMaxDeviation = v.maxDeviation;
                    entry.validationSessionId = v.validationSessionId;
                    break;
                }
            }

            doc.sessionCount = doc.entries.Count;
            return doc;
        }

        /// <summary>Convenience: scan the standard root (QALogger.SessionsRoot).</summary>
        public static CatalogDocument ScanDefault() => Scan(QALogger.SessionsRoot);

        /// <summary>Write catalog.json at the root. Returns the file path.</summary>
        public static string Save(CatalogDocument doc, string sessionsRoot)
        {
            Directory.CreateDirectory(sessionsRoot);
            string path = Path.Combine(sessionsRoot, CatalogFileName);
            File.WriteAllText(path, JsonUtility.ToJson(doc, prettyPrint: true));
            return path;
        }

        /// <summary>Read a previously saved catalog.json; null if absent/corrupt.</summary>
        public static CatalogDocument LoadSaved(string sessionsRoot) =>
            TryRead<CatalogDocument>(Path.Combine(sessionsRoot, CatalogFileName));

        // ------------------------------------------------------------- helpers

        private static ReplayMetadata ReadEntry(string folder)
        {
            var manifest = TryRead<SessionManifest.Manifest>(
                Path.Combine(folder, SessionManifest.FileName));
            if (manifest == null || string.IsNullOrEmpty(manifest.sessionId))
                return null; // not a session folder we can vouch for

            var entry = new ReplayMetadata
            {
                sessionId = manifest.sessionId,
                folderName = Path.GetFileName(folder), // actual dir name is authoritative
                folderPath = folder,
                level = manifest.level,
                startedUtc = manifest.startedUtc,
                status = manifest.status,
                durationSec = manifest.durationSec
            };

            var replay = TryRead<ReplayHeaderProbe>(Path.Combine(folder, ReplayFileStore.FileName));
            if (replay != null && replay.frameCount > 0)
            {
                entry.hasReplay = true;
                entry.replayFrameCount = replay.frameCount;
                entry.replayRecordingStartTime = replay.recordingStartTime;
            }

            return entry;
        }

        /// <summary>Deserialize a JSON file into T; null on any problem, never a throw.</summary>
        private static T TryRead<T>(string path) where T : class
        {
            if (!File.Exists(path)) return null;
            try
            {
                return JsonUtility.FromJson<T>(File.ReadAllText(path));
            }
            catch (Exception)
            {
                return null; // damaged file = skipped file; the scanner reports counts, not exceptions
            }
        }
    }
}
