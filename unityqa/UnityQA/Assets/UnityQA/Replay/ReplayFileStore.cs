// -----------------------------------------------------------------------------
// UnityQA — ReplayFileStore.cs                                   (M3 Slice A)
//
// PURPOSE
//   Owns the replay file convention: WHERE a replay lives (inside its
//   session's folder, beside session.json and events.jsonl — one session =
//   one folder = one complete record, the SRS invariant) and HOW it is
//   written (pretty JSON via JsonUtility — D-009 unchanged, zero packages).
//
// WHY A SEPARATE CLASS (extension preparation, M3 roadmap)
//   Path convention and format knowledge stay in one place: Slice A shipped
//   Save; Slice B added Load exactly here, as planned. ReplayRecorder never
//   learns about loading; ReplayPlayer never learns about saving.
//
// LOAD PHILOSOPHY (M3.B)
//   A loader must be a skeptic: files can be truncated, hand-edited, or from
//   a future build. Load returns null (with ONE descriptive error) rather
//   than throwing — a broken replay must never crash a QA run — and repairs
//   what is safely repairable (frameCount disagreement → trust the actual
//   array, with a warning).
// -----------------------------------------------------------------------------

using System;
using System.IO;
using UnityEngine;

namespace UnityQA.Replay
{
    /// <summary>Save/Load replay.json inside a session folder.</summary>
    public static class ReplayFileStore
    {
        public const string FileName = "replay.json";

        /// <returns>Absolute path of the written file.</returns>
        public static string Save(ReplayRecording recording, string sessionFolder)
        {
            Directory.CreateDirectory(sessionFolder); // defensive; normally exists
            string path = Path.Combine(sessionFolder, FileName);
            File.WriteAllText(path, JsonUtility.ToJson(recording, prettyPrint: true));
            return path;
        }

        /// <summary>
        /// Load and validate a replay. Returns null on any unrecoverable
        /// problem (missing file, malformed JSON, future schema) — callers
        /// must null-check and skip playback; nothing throws.
        /// </summary>
        public static ReplayRecording Load(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Debug.LogError($"[UnityQA] Replay load failed — file not found: '{path}'");
                return null;
            }

            ReplayRecording recording;
            try
            {
                recording = JsonUtility.FromJson<ReplayRecording>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UnityQA] Replay load failed — malformed JSON at '{path}': {ex.Message}");
                return null;
            }

            if (recording == null)
            {
                Debug.LogError($"[UnityQA] Replay load failed — empty document at '{path}'");
                return null;
            }

            if (recording.schemaVersion > ReplayRecording.CurrentSchemaVersion)
            {
                // Refuse, don't guess: a future format may mean fields this
                // build cannot interpret; playing it back would be silently wrong.
                Debug.LogError($"[UnityQA] Replay at '{path}' has schemaVersion " +
                               $"{recording.schemaVersion} > supported {ReplayRecording.CurrentSchemaVersion} — refusing to load.");
                return null;
            }

            recording.frames ??= Array.Empty<ReplayFrame>();

            if (recording.frameCount != recording.frames.Length)
            {
                // The integrity field disagreeing with reality = likely truncation
                // or hand-editing. The frames array is the ground truth.
                Debug.LogWarning($"[UnityQA] Replay at '{path}': frameCount {recording.frameCount} " +
                                 $"≠ frames.Length {recording.frames.Length} — trusting the array.");
                recording.frameCount = recording.frames.Length;
            }

            return recording;
        }
    }
}
