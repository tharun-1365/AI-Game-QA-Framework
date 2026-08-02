// -----------------------------------------------------------------------------
// UnityQA — ReplayMetadata.cs                                    (M3 Slice D)
//
// PURPOSE
//   One catalog entry: everything worth knowing about a recorded session
//   WITHOUT loading its frame data — identity, when, how long, whether a
//   replay exists, and the latest validation verdict against it. This is the
//   unit Milestone 4's dataset generation will iterate and Milestone 7's
//   reports will cite; Slice D exists so those milestones inherit an index
//   instead of a folder-walking problem.
//
//   [Serializable]; field names are the catalog.json wire format (same
//   discipline as every other UnityQA document).
// -----------------------------------------------------------------------------

using System;

namespace UnityQA.Replay
{
    /// <summary>Catalog entry for one session folder. Wire format of catalog.json entries.</summary>
    [Serializable]
    public sealed class ReplayMetadata
    {
        // --- identity (from the folder + session.json) -----------------------
        public string sessionId;
        /// <summary>Actual directory name (timestamp-sortable, A2 convention).
        /// Authoritative for ordering and paths — session.json's own copy is
        /// informational only.</summary>
        public string folderName;
        /// <summary>Absolute path of the session folder on THIS machine.
        /// catalog.json lives in persistentDataPath, never in the repo, so
        /// machine-specific paths are fine here.</summary>
        public string folderPath;

        // --- session facts (session.json) ------------------------------------
        public string level;
        public string startedUtc;
        /// <summary>"closed" for clean sessions; "open" marks a crashed one —
        /// the crash-forensics signal, surfaced into the catalog.</summary>
        public string status;
        public float durationSec;

        // --- replay facts (replay.json header probe) -------------------------
        public bool hasReplay;
        public int replayFrameCount;
        public string replayRecordingStartTime;

        // --- validation cross-link (newest validation.json citing this session
        //     as its ORIGINAL) --------------------------------------------------
        public bool hasValidation;
        public string validationVerdict;
        public float validationMaxDeviation;
        /// <summary>The validation SESSION that produced the verdict (its folder
        /// holds validation.json) — the M7 "click through to evidence" link.</summary>
        public string validationSessionId;
    }
}
