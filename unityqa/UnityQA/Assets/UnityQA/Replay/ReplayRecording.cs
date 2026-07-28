// -----------------------------------------------------------------------------
// UnityQA — ReplayRecording.cs                                   (M3 Slice A)
//
// PURPOSE
//   The complete, self-describing replay document: identity (which session),
//   versioning (schemaVersion — same evolution discipline as every other
//   UnityQA file format), and the frame array.
//
// EXTENSION CONTRACT (for future slices — design note, not implementation)
//   M3.B's ReplayLoader will deserialize exactly this type and hand it to a
//   ReplayPlayer that implements BenchGame's IPlayerInputSource — the D-008
//   seam is the already-proven injection point (the PlayMode ScriptedInputSource
//   tests demonstrate the mechanism today). M3.C validation will compare a
//   replayed session's telemetry to the original's via the shared sessionId.
//   Fields may be ADDED under a schemaVersion bump (e.g. a fixed-step index
//   if Slice C's determinism work needs one); never renamed or removed.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace UnityQA.Replay
{
    /// <summary>A saved replay. Field names = replay.json format.</summary>
    [Serializable]
    public sealed class ReplayRecording
    {
        /// <summary>Bump on any wire-format change; readers key off this.</summary>
        public const int CurrentSchemaVersion = 1;

        public int schemaVersion;

        /// <summary>UUID of the session this replay was recorded in — must
        /// match the sibling session.json (validation gate R-5).</summary>
        public string sessionId;

        /// <summary>ISO-8601 UTC instant recording began.</summary>
        public string recordingStartTime;

        /// <summary>Redundant with frames.Length by design: a cheap integrity
        /// check for loaders (mismatch ⇒ truncated/hand-edited file).</summary>
        public int frameCount;

        public ReplayFrame[] frames;

        /// <summary>
        /// Assemble a recording from captured frames. The single ToArray here
        /// is the export path's one deliberate allocation — recording itself
        /// stays allocation-free.
        /// </summary>
        public static ReplayRecording Create(string sessionId, string recordingStartTime,
                                             List<ReplayFrame> capturedFrames)
        {
            return new ReplayRecording
            {
                schemaVersion = CurrentSchemaVersion,
                sessionId = sessionId,
                recordingStartTime = recordingStartTime,
                frameCount = capturedFrames.Count,
                frames = capturedFrames.ToArray()
            };
        }
    }
}
