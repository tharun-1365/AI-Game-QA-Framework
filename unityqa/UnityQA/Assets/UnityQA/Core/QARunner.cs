// -----------------------------------------------------------------------------
// UnityQA — QARunner.cs                                    (SRS §8, FR-1.4/1.5)
//
// PURPOSE
//   The single owner of "is a QA session running?". Mints session identity,
//   owns the bus, stamps and emits every event, and guarantees clean shutdown.
//
// WHY EXACTLY ONE OWNER
//   If two components could disagree about session state, every log would be
//   suspect. All lifecycle authority concentrates here; everything else only
//   observes (subscribes) or reports (calls Emit).
//
// EMIT AS THE ONLY DOOR
//   Publishers never construct QAEvents directly — they call Emit(type, pos,
//   payload) and QARunner stamps sid/seq/t/frame from the session object.
//   One door in = sequence integrity (NFR-1.5) is structural, not disciplined.
//
// HISTORY NOTE
//   Slice A shipped a temporary inline console echo here (walking-skeleton
//   practice); Slice B removed it when QALogger/ConsoleSink took over the
//   subscriber role, exactly as the design's §15 plan scheduled.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;

namespace UnityQA.Core
{
    /// <summary>
    /// Session lifecycle owner. Scene setup: one GameObject "[QA]" with this
    /// component and a QAConfig assigned. F9 (configurable) toggles sessions;
    /// the inspector context menu offers the same for mouse-driven demos.
    /// </summary>
    public sealed class QARunner : MonoBehaviour
    {
        [Tooltip("The framework's settings asset. Required — QARunner disables itself without one.")]
        [SerializeField] private QAConfig config;

        /// <summary>The event seam. Never null after Awake.</summary>
        public QAEventBus Bus { get; private set; }

        /// <summary>Metadata + stamping state of the running session; null when idle.</summary>
        public QASessionInfo CurrentSession { get; private set; }

        public bool IsSessionActive => CurrentSession != null;

        /// <summary>Config accessor for other UnityQA components (read-only use).</summary>
        public QAConfig Config => config;

        // Reused payload dictionary for lifecycle emissions (QAEvent copies it;
        // reuse here avoids trivially avoidable garbage).
        private readonly Dictionary<string, object> scratch = new Dictionary<string, object>();

        private void Awake()
        {
            Bus = new QAEventBus();

            if (config == null)
            {
                Debug.LogError("[UnityQA] QARunner has no QAConfig assigned — disabling. " +
                               "Create one via Assets ▸ Create ▸ UnityQA ▸ Config and assign it.");
                enabled = false;
                return;
            }

            // Slice A's temporary console echo lived here; Slice B replaced it
            // with the real sink path (QALogger → ConsoleSink), as planned.
        }

        private void Update()
        {
            if (Input.GetKeyDown(config.startStopKey))
            {
                if (IsSessionActive) EndSession();
                else StartSession();
            }
        }

        /// <summary>Begin a session: mint identity, announce on the bus (FR-1.5).</summary>
        public void StartSession()
        {
            if (IsSessionActive)
            {
                Debug.LogWarning("[UnityQA] StartSession ignored — session already active.");
                return;
            }

            CurrentSession = new QASessionInfo(
                level: gameObject.scene.name,
                clock: () => Time.time,
                utcNow: System.DateTime.UtcNow,
                unityVersion: Application.unityVersion,
                appVersion: Application.version);

            scratch.Clear();
            scratch["level"] = CurrentSession.Level;
            Emit(QAEventType.SessionStarted, null, scratch);
        }

        /// <summary>
        /// End cleanly: SessionEnded is guaranteed to be the session's last
        /// event — a structural integrity check later modules rely on (§11 SRS).
        /// </summary>
        public void EndSession()
        {
            if (!IsSessionActive) return;

            scratch.Clear();
            scratch["durationSec"] = CurrentSession.SessionTime;
            scratch["eventCount"] = CurrentSession.EventCount + 1; // incl. this event
            Emit(QAEventType.SessionEnded, null, scratch);

            CurrentSession = null;
        }

        /// <summary>
        /// The ONLY way events enter the system. Stamps identity/sequence/time
        /// from the current session and publishes. No session → warn and drop
        /// (never throw: a stray late reporter must not crash gameplay).
        /// </summary>
        public void Emit(QAEventType type, Vector2? pos, IDictionary<string, object> payload)
        {
            if (!IsSessionActive)
            {
                Debug.LogWarning($"[UnityQA] Emit({type}) with no active session — dropped.");
                return;
            }

            // QAEvent's constructor makes its own defensive copy of the payload,
            // so callers (including our reused scratch dictionary) pass theirs as-is.
            var e = new QAEvent(
                sid: CurrentSession.SessionId,
                seq: CurrentSession.NextSeq(),
                t: CurrentSession.SessionTime,
                frame: Time.frameCount,
                type: type,
                pos: pos,
                payload: payload);

            Bus.Publish(e);
        }

        /// <summary>Safety net: leaving Play mode mid-session still closes cleanly (SRS §9).</summary>
        private void OnDisable()
        {
            if (IsSessionActive) EndSession();
        }

        // Inspector conveniences (FR-1.4's "via inspector button").
        [ContextMenu("Start Session")] private void CtxStart() => StartSession();
        [ContextMenu("End Session")] private void CtxEnd() => EndSession();
    }
}
