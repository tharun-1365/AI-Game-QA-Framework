// -----------------------------------------------------------------------------
// UnityQA — ReplayFrame.cs                                       (M3 Slice A)
//
// PURPOSE
//   One recorded gameplay frame of attempted input — the atom of a replay.
//
// DESIGN
//   A [Serializable] STRUCT: recorded ~60×/second into a pre-sized List, so
//   value semantics keep recording allocation-free (a class here would mean
//   one heap object per frame). Fields are exactly the Slice A specification;
//   field NAMES are the JSON wire format (JsonUtility serializes them
//   verbatim), so renaming any field is a replay-schema change and must go
//   through a schemaVersion bump (same discipline as EVENT-SCHEMA.md).
//
//   Frame-domain by specification: input is read in Update, so frames are
//   editor-frame-indexed. The determinism implications for playback are a
//   Slice C concern, flagged in MODULES.md — recording faithfully captures
//   what the player attempted per rendered frame, which is Slice A's whole
//   contract.
// -----------------------------------------------------------------------------

using System;

namespace UnityQA.Replay
{
    /// <summary>One frame of attempted input. Field names = replay.json format.</summary>
    [Serializable]
    public struct ReplayFrame
    {
        /// <summary>0-based frame index since recording start.</summary>
        public int frameNumber;

        /// <summary>Seconds since recording start (frame-domain clock).</summary>
        public float timestamp;

        /// <summary>Attempted horizontal command: exactly -1, 0, or +1.</summary>
        public float horizontal;

        /// <summary>Jump button went down on this frame (GetButtonDown semantics).</summary>
        public bool jumpPressed;

        /// <summary>Jump button held during this frame.</summary>
        public bool jumpHeld;
    }
}
