// -----------------------------------------------------------------------------
// UnityQA.Adapters — ReplayPlayer.cs                             (M3 Slice B)
//
// PURPOSE
//   Loads a replay.json and drives the player with it: swaps the controller's
//   input source to a ReplayInputSource, advances exactly one recorded frame
//   per PHYSICS STEP, restores the original source when done, and reports
//   completion plus the run's OUTCOME. Playback only — no comparison, no
//   validation (Slice C).
//
// FRAME ADVANCEMENT & ORDERING (HOTFIX-5 — the determinism fix)
//   [DefaultExecutionOrder(-50)] runs this FixedUpdate BEFORE the recorder's
//   (-10) and PlayerController2D's (0): each physics step the player advances
//   the cursor, THEN the controller consumes the fresh values — one recorded
//   frame per physics step, the SAME domain the recorder captured in, so the
//   mapping "recorded frame → physics step" is 1:1 and frame-rate independent
//   (this closes the D-011 limitation that Slice C existed to measure: the
//   old render-domain playback re-applied the input sequence onto different
//   physics steps whenever the frame rate differed from recording time).
//   This component's Update (also -50, before the controller's) clears the
//   replay source's jump down-edge so the controller's Update latch cannot
//   double-consume a recorded press — see ReplayInputSource.ClearJumpEdge.
//
// OUTCOME REPORTING (M5.D stabilization)
//   When the scene has a GameRun, playback observes RunEnded and logs
//   "Replay Outcome: <Success|SpikeDeath|OutOfBounds|Quit>" at playback end
//   ("Unknown" if the run never ended during playback). If the previous run
//   had already ENDED when Play() is called, the run is reset first — feeding
//   input to a frozen player is never a meaningful replay.
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

        // Outcome observation (M5.D stabilization) — null on outcome-less scenes.
        private GameRun gameRun;
        private bool sawRunEnd;
        private SessionOutcome observedOutcome;

        public bool IsPlaying => playing;
        public int CurrentFrame => index;
        public int TotalFrames => recording != null ? recording.frameCount : 0;

        /// <summary>Outcome name of the most recently FINISHED playback:
        /// "Success"/"SpikeDeath"/"OutOfBounds"/"Quit" (schema vocabulary),
        /// "Unknown" when a GameRun existed but never ended during playback,
        /// or "" when the scene has no GameRun / no playback finished yet.</summary>
        public string LastReplayOutcome { get; private set; } = "";

        /// <summary>
        /// Recorded timestamp of the frame most recently pushed into the input
        /// source, or -1 when not playing (HOTFIX-4, read-only — diagnostic
        /// surface for ReplayDivergenceProbe). Playback advances exactly one
        /// frame per physics step (HOTFIX-5) and ignores this value when
        /// deciding what to apply.
        /// </summary>
        public float CurrentFrameTimestamp =>
            playing && recording != null && index > 0 && index <= recording.frameCount
                ? recording.frames[index - 1].timestamp
                : -1f;

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

            if (recording.inputDomain != ReplayRecording.InputDomainFixedStep)
                Debug.LogWarning("[UnityQA] ReplayPlayer: legacy render-frame replay (pre-HOTFIX-5) — " +
                                 "timing is best-effort; re-record for deterministic validation.");

            // Outcome observation + frozen-player guard (M5.D stabilization).
            if (gameRun == null) gameRun = FindFirstObjectByType<GameRun>();
            if (gameRun != null)
            {
                if (gameRun.State == GameRun.RunState.Ended)
                {
                    Debug.Log("[UnityQA] ReplayPlayer: previous run had ended — resetting GameRun first.");
                    gameRun.ResetRun();
                }
                sawRunEnd = false;
                gameRun.RunEnded += OnRunEnded;
            }

            originalSource = controller.InputSource;    // remember whoever was driving
            controller.SetInputSource(replaySource);    // the swap — controller none the wiser
            index = 0;
            playing = true;
            Debug.Log($"[UnityQA] Replay playback started — {recording.frameCount} frames " +
                      $"(session {recording.sessionId}).");
        }

        private void OnRunEnded(SessionOutcome outcome)
        {
            // Record, don't react: playback runs to the end of the input
            // stream regardless (the frozen player consumes the tail frames
            // exactly as the original run did after ITS run ended).
            sawRunEnd = true;
            observedOutcome = outcome;
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

        private void FixedUpdate()
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

        private void Update()
        {
            // All of this frame's physics steps have consumed the current
            // frame's jump edge by now; drop it before the controller's Update
            // latch (order 0, after us) can consume it a second time.
            if (playing) replaySource.ClearJumpEdge();
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

            if (gameRun != null)
            {
                gameRun.RunEnded -= OnRunEnded;
                if (finished)
                {
                    LastReplayOutcome = sawRunEnd ? observedOutcome.ToString() : "Unknown";
                    Debug.Log(sawRunEnd
                        ? $"[UnityQA] Replay Outcome: {LastReplayOutcome}"
                        : "[UnityQA] Replay Outcome: Unknown (run did not end during playback)");
                }
            }

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
