// -----------------------------------------------------------------------------
// UnityQA — ConsoleSink.cs                                  (SRS FR-1.10, §4)
//
// PURPOSE
//   Mirrors discrete events to the Unity Console with a [UnityQA] prefix —
//   the developer-feedback sink. This replaces the temporary echo lambda that
//   lived inside QARunner.Awake during Slice A (deleted in this slice, as
//   planned in the walking-skeleton note).
//
// SEVERITY MAPPING
//   AdapterWarning → LogWarning (yellow, filterable); everything else → Log.
//   Dense streams (telemetry/inputs, Slice C) never pass through ILogSinks,
//   so the Console can never be flooded by them — structural, not configured.
// -----------------------------------------------------------------------------

using UnityEngine;
using UnityQA.Core;

namespace UnityQA.Logging
{
    /// <summary>Console mirror for events. Enabled via QAConfig.consoleEvents.</summary>
    public sealed class ConsoleSink : ILogSink
    {
        public void Open(QASessionInfo session, string sessionFolder)
        {
            Debug.Log($"[UnityQA] Session {session.SessionId} started — writing to {sessionFolder}");
        }

        public void Write(QAEvent e)
        {
            if (e.Type == QAEventType.AdapterWarning)
                Debug.LogWarning($"[UnityQA] {e}");
            else
                Debug.Log($"[UnityQA] {e}");
        }

        public void Flush()
        {
            // Console output is unbuffered — nothing to do.
        }

        public void Close()
        {
            // Nothing to release. (Session-end message comes from the event itself.)
        }
    }
}
