// -----------------------------------------------------------------------------
// UnityQA.Adapters — ReplayPlayer.cs                             (M3 Slice B)
//
// PURPOSE
//   Loads a replay.json and drives the player with it: swaps the controller's
//   input source to a ReplayInputSource, advances exactly one recorded frame
//   per Update, restores the original source when done, and reports
//   completion. Playback only — no comparison, no validation (Slice C).
//
// FRAME ADVANCEMENT & ORDERING (the subtle part)
//   [DefaultExecutionOrder(-50)] runs this Update BEFORE PlayerController2D's
//   (default order 0): each rendered frame the player advances the cursor,
//   THEN the controller reads the fresh values — one recorded frame consumed
//   per controller read, sequentially, index-addressed (O(1), no LINQ, no
//   allocations, no searching). Honest limitation, already flagged in D-011
//   and owned by Slice C: frames are frame-domain, so a machine running at a
//   different frame rate replays the same INPUT SEQUENCE on a different
//   wall-clock — input fidelity is exact per frame, timing fidelity is not
//   guaranteed. That gap is precisely what "Slice C: Deterministic
//   Validation" exists to measure.
//
// SOURCE SWAP DISCIPLINE
//   Play(): remember controller.InputSource → SetInputSource(replaySource).
//   Stop/finish/disable: Clear() the replay source (no phantom held keys) and
//   restore the remembered source. The controller never knows any of this
//   happened — keyboard and replay are interchangeable implementations of
//   the same seam, exactly as mandated.
// -----------------------------------------------------------------------------

using System;
using System.IO;
using BenchGame;
using UnityEngine;
using UnityQA.Logging;
using UnityQA.Replay;

namespace UnityQA.Adapters
{
    /// <summary>
    /// Replay playback driver. Optional dev component — add to "[QA]" (or any
    /// object) when you want playback; see docs/QA-SETUP.md.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class ReplayPlayer : MonoBehaviour
    {
        [Header("Replay source")]
        [Tooltip("Path to a replay.json. Absolute, or relative to the UnityQA Sessions folder " +
                 "(e.g. '20260726-140102_6f1c2e6a/replay.json'). LEAVE EMPTY to auto-use the " +
                 "most recent session's replay — the record-then-replay demo loop.")]
        [SerializeField] private string replayFile = "";

        [Tooltip("Start playback automatically on Play mode start.")]
        [SerializeField] private bool autoPlay = false;

        [Tooltip("Restart from frame 0 when the replay ends instead of stopping.")]
        [SerializeField] private bool loop = false;

        private ReplayRecording recording;
        private readonly ReplayInputSource replaySource = new ReplayInputSource();
        private PlayerController2D controller;
        private IPlayerInputSource originalSource;
        private int index;
        private bool playing;

        public bool IsPlaying => playing;
        public int CurrentFrame => index;
        public int TotalFrames => recording != null ? recording.frameCount : 0;

        /// <summary>Raised once when playback reaches the natural end (not on manual Stop).</summary>
        public event Action PlaybackFinished;

        private void Start()
        {
            if (autoPlay) Play();
        }

        /// <summary>Load (if needed) and start driving the player. Safe to call repeatedly.</summary>
        [ContextMenu("Play Replay")]
        public void Play()
        {
            if (playing) return;

            if (recording == null && !LoadReplay()) return;

            if (controller == null)
            {
                controller = FindFirstObjectByType<PlayerController2D>();
                if (controller == null)
                {
                    Debug.LogError("[UnityQA] ReplayPlayer: no PlayerController2D in scene — cannot play.");
                    return;
                }
            }

            if (recording.frameCount == 0)
            {
                Debug.LogWarning("[UnityQA] ReplayPlayer: replay has 0 frames — nothing to play.");
                return;
            }

            originalSource = controller.InputSource;    // remember whoever was driving
            controller.SetInputSource(replaySource);    // the swap — controller none the wiser
            index = 0;
            playing = true;
            Debug.Log($"[UnityQA] Replay playback started — {recording.frameCount} frames " +
                      $"(session {recording.sessionId}).");
        }

        /// <summary>Stop early and hand control back. No completion event fires.</summary>
        [ContextMenu("Stop Replay")]
        public void Stop() => StopInternal(finished: false);

        /// <summary>Load/reload from the configured (or latest) replay file.</summary>
        public bool LoadReplay()
        {
            recording = ReplayFileStore.Load(ResolvePath());
            return recording != null;
        }

        /// <summary>
        /// Point this player at a specific replay file (M3.C, additive).
        /// Invalidates any cached recording so the next Play()/LoadReplay()
        /// reads the new target. Exists so orchestration code (ReplayValidator)
        /// can drive playback without touching serialized inspector state.
        /// </summary>
        public void SetReplayFile(string path)
        {
            replayFile = path ?? "";
            recording = null;
        }

        private void Update()
        {
            if (!playing) return;

            if (index >= recording.frameCount)
            {
                if (loop)
                {
                    index = 0; // seamless restart; same frame fed below
                }
                else
                {
                    StopInternal(finished: true);
                    return;
                }
            }

            replaySource.SetFrame(in recording.frames[index]); // O(1) sequential access
            index++;
        }

        private void StopInternal(bool finished)
        {
            if (!playing) return;
            playing = false;

            replaySource.Clear(); // never leave a phantom key held
            if (controller != null && originalSource != null)
                controller.SetInputSource(originalSource); // keyboard (or whoever) resumes

            Debug.Log(finished
                ? $"[UnityQA] Replay playback finished — {recording.frameCount} frames consumed."
                : "[UnityQA] Replay playback stopped.");

            if (finished) PlaybackFinished?.Invoke();
        }

        private void OnDisable()
        {
            StopInternal(finished: false); // leaving Play mode mid-replay restores cleanly
        }

        /// <summary>
        /// Resolve the inspector path: absolute → as-is; relative → under the
        /// Sessions root; a folder → its replay.json; EMPTY → newest session
        /// folder containing a replay (folder names are timestamp-sortable by
        /// construction — amendment A2's naming paying off).
        /// </summary>
        private string ResolvePath()
        {
            if (!string.IsNullOrEmpty(replayFile))
            {
                string path = Path.IsPathRooted(replayFile)
                    ? replayFile
                    : Path.Combine(QALogger.SessionsRoot, replayFile);
                if (Directory.Exists(path))
                    path = Path.Combine(path, ReplayFileStore.FileName);
                return path;
            }

            // Auto mode: newest session with a replay. A simple reverse-sorted
            // directory scan — editor-workflow convenience, not a hot path.
            if (!Directory.Exists(QALogger.SessionsRoot)) return "";
            string[] folders = Directory.GetDirectories(QALogger.SessionsRoot);
            Array.Sort(folders);
            for (int i = folders.Length - 1; i >= 0; i--)
            {
                string candidate = Path.Combine(folders[i], ReplayFileStore.FileName);
                if (File.Exists(candidate)) return candidate;
            }
            return "";
        }
    }
}
