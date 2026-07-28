// -----------------------------------------------------------------------------
// UnityQA.Adapters — ReplayRecorder.cs                           (M3 Slice A)
//
// PURPOSE
//   Records one ReplayFrame of attempted input per Update while a session is
//   active, and exports the whole recording as pretty replay.json into the
//   session's folder when the session ends. Recording only — no playback, no
//   loading, no comparison (those are M3.B/C by roadmap).
//
// WHY THIS CLASS LIVES IN THE ADAPTERS ASSEMBLY (decision D-011)
//   The Slice A mandate: input comes ONLY through BenchGame's
//   IPlayerInputSource (the D-008 seam) — and that TYPE lives in the
//   BenchGame assembly, which core UnityQA must never reference (NFR-1.3).
//   UnityQA.Adapters is the one sanctioned bridge, so the recorder lives
//   here. The data model (ReplayFrame/ReplayRecording/ReplayFileStore) is
//   game-agnostic and correctly sits in core UnityQA/Replay. If a future
//   game needs replay, the model moves for free; only this recorder is
//   per-game. The PlayerController2D reference below exists solely to reach
//   .InputSource — every input READ goes through the interface; UnityEngine
//   .Input is never touched (dependency inversion held).
//
// PERFORMANCE (recording path)
//   Pre-sized List of structs (initial 4096, grows by doubling — steady state
//   allocates nothing); no LINQ; no strings in Update; a hard frame cap
//   guards a forgotten session from eating memory (warn once, stop capturing,
//   session itself unaffected). The one deliberate allocation is ToArray at
//   export, outside gameplay.
//
// LIFECYCLE
//   Same bus discipline as every recorder: subscribe in Start, arm on
//   SessionStarted, disarm + export on SessionEnded. Export runs during the
//   SessionEnded publish, when QARunner.CurrentSession is still valid — the
//   same window QALogger uses to close its manifest.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using BenchGame;
using UnityEngine;
using UnityQA.Core;
using UnityQA.Logging;
using UnityQA.Replay;

namespace UnityQA.Adapters
{
    /// <summary>
    /// Per-frame input recording → replay.json. Scene setup: on the "[QA]"
    /// GameObject next to QARunner (see docs/QA-SETUP.md step 7).
    /// </summary>
    [RequireComponent(typeof(QARunner))]
    public sealed class ReplayRecorder : MonoBehaviour
    {
        /// <summary>Safety cap: 30 min at 60 fps. A session that long has
        /// bigger problems than a truncated replay; capture stops with one
        /// warning, the session continues untouched.</summary>
        public const int MaxFrames = 108_000;

        private QARunner runner;
        private IPlayerInputSource input;     // the ONLY input access path (D-008 seam)
        private readonly List<ReplayFrame> frames = new List<ReplayFrame>(4096);

        private bool recording;
        private bool capWarned;
        private float recordingStartTime;
        private string recordingStartUtc;

        /// <summary>Frames captured so far (read-only; overlay/tests).</summary>
        public int FrameCount => frames.Count;

        public bool IsRecording => recording;

        private void Awake()
        {
            runner = GetComponent<QARunner>();
        }

        private void Start()
        {
            runner.Bus.Subscribe(OnEvent);
        }

        private void OnEvent(QAEvent e)
        {
            if (e.Type == QAEventType.SessionStarted) BeginRecording();
            else if (e.Type == QAEventType.SessionEnded) EndRecordingAndExport();
        }

        private void BeginRecording()
        {
            // Bind to the player's input source lazily, at session start —
            // the controller's Awake (which creates the source) has certainly
            // run by the time a human presses F9.
            if (input == null)
            {
                var controller = FindFirstObjectByType<PlayerController2D>();
                input = controller != null ? controller.InputSource : null;
            }

            if (input == null)
            {
                Debug.LogWarning("[UnityQA] ReplayRecorder: no player input source found — " +
                                 "replay recording skipped for this session.");
                return;
            }

            frames.Clear();               // capacity is retained — no re-allocation
            capWarned = false;
            recordingStartTime = Time.time;
            recordingStartUtc = System.DateTime.UtcNow.ToString("o");
            recording = true;
        }

        private void Update()
        {
            if (!recording) return;

            if (frames.Count >= MaxFrames)
            {
                if (!capWarned)
                {
                    capWarned = true;
                    Debug.LogWarning($"[UnityQA] ReplayRecorder: frame cap ({MaxFrames}) reached — " +
                                     "capture stopped; session continues.");
                }
                return;
            }

            frames.Add(new ReplayFrame
            {
                frameNumber = frames.Count,
                timestamp = Time.time - recordingStartTime,
                horizontal = input.MoveX,
                jumpPressed = input.JumpDown,
                jumpHeld = input.JumpHeld
            });
        }

        private void EndRecordingAndExport()
        {
            if (!recording) return;
            recording = false;

            // CurrentSession is still valid during the SessionEnded publish
            // (QARunner nulls it only after the publish returns).
            QASessionInfo session = runner.CurrentSession;
            if (session == null) return; // defensive; cannot happen on the normal path

            var recordingDoc = ReplayRecording.Create(session.SessionId, recordingStartUtc, frames);
            string folder = System.IO.Path.Combine(QALogger.SessionsRoot, session.FolderName);
            string path = ReplayFileStore.Save(recordingDoc, folder);

            Debug.Log($"[UnityQA] Replay saved — {recordingDoc.frameCount} frames → {path}");
        }

        private void OnDisable()
        {
            // Play mode ending mid-session: QARunner/QALogger's shutdown
            // choreography fires SessionEnded while we are still subscribed,
            // so the export above runs on the normal path. Here we only
            // detach from the bus.
            if (runner != null && runner.Bus != null)
                runner.Bus.Unsubscribe(OnEvent);
        }
    }
}
