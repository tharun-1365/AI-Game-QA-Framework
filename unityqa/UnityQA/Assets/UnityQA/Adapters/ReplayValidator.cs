// -----------------------------------------------------------------------------
// UnityQA.Adapters — ReplayValidator.cs                          (M3 Slice C)
//
// PURPOSE
//   Orchestrates one deterministic-validation run, end to end, reusing every
//   existing system and inventing none:
//
//     original session folder (events.jsonl + replay.json)
//       → reset player to the original starting state
//       → START a fresh QA session   (QARunner — existing)
//       → PLAY the original replay   (ReplayPlayer — existing)
//       → telemetry re-records it    (QATelemetrySampler → QALogger — existing)
//       → END the session
//       → load both trajectories     (SessionTrajectory — Slice C)
//       → compare                    (TrajectoryComparer — Slice C)
//       → write validation.json into the VALIDATION session's folder
//
//   The replay-under-recording trick is the heart of Slice C: playback drives
//   the game through the same input seam a human would, so the validation
//   session's telemetry is produced by the SAME pipeline as the original's —
//   the comparison is symmetric by construction.
//
// THE ONE DELIBERATE GAME-STATE INTERVENTION
//   Before playback the validator teleports the player to the original run's
//   first sampled position and zeroes velocity. Deterministic replay is an
//   EXPERIMENT, and experiments get controlled initial conditions; this is
//   test-harness actuation (like the future agent), not instrumentation, and
//   it is the only place Slice C touches game state.
// -----------------------------------------------------------------------------

using System;
using System.Collections;
using System.IO;
using BenchGame;
using UnityEngine;
using UnityQA.Core;
using UnityQA.Logging;
using UnityQA.Replay;

namespace UnityQA.Adapters
{
    /// <summary>
    /// Replay-fidelity validation driver. Scene setup: on "[QA]" alongside
    /// QARunner, QALogger, QATelemetrySampler, BenchGameAdapter, ReplayPlayer
    /// (all required — see docs/QA-SETUP.md step 9).
    /// </summary>
    [RequireComponent(typeof(QARunner))]
    [RequireComponent(typeof(ReplayPlayer))]
    public sealed class ReplayValidator : MonoBehaviour
    {
        [Tooltip("Original session folder to validate: absolute path, a folder name under " +
                 "UnityQA/Sessions, or EMPTY = newest session that has a replay.json.")]
        [SerializeField] private string originalSessionFolder = "";

        private QARunner runner;
        private ReplayPlayer player;
        private Coroutine running;

        /// <summary>Result of the most recent validation; null until one completes.</summary>
        public ReplayValidationResult LastResult { get; private set; }

        public bool IsValidating => running != null;

        /// <summary>Raised when a validation run completes (PASS, FAIL or INVALID).</summary>
        public event Action<ReplayValidationResult> ValidationCompleted;

        public const string ResultFileName = "validation.json";

        private void Awake()
        {
            EnsureRefs();
        }

        /// <summary>
        /// Lazy reference acquisition. [ContextMenu] methods can be invoked in
        /// EDIT MODE, where Awake has never run — so no method in this class
        /// may assume lifecycle-initialized fields. Idempotent and cheap
        /// (GetComponent only when a field is still null).
        /// </summary>
        private void EnsureRefs()
        {
            if (runner == null) runner = GetComponent<QARunner>();
            if (player == null) player = GetComponent<ReplayPlayer>();
        }

        /// <summary>Validate the folder set in the inspector (or the newest session).</summary>
        [ContextMenu("Validate Replay")]
        public void Validate()
        {
            // Validation needs a LIVE game: coroutines, physics, playback, and
            // the session pipeline all require Play mode. In Edit Mode we say
            // so instead of failing somewhere deeper.
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[UnityQA] Replay validation requires Play mode — enter Play, " +
                                 "then use Validate Replay.");
                return;
            }

            EnsureRefs();
            if (runner == null || player == null)
            {
                Debug.LogError("[UnityQA] ReplayValidator needs QARunner and ReplayPlayer on the " +
                               "same GameObject — validation aborted.");
                return;
            }

            if (IsValidating)
            {
                Debug.LogWarning("[UnityQA] Validation already running — ignored.");
                return;
            }
            if (runner.IsSessionActive)
            {
                Debug.LogWarning("[UnityQA] End the current QA session before validating — ignored.");
                return;
            }

            string folder = ResolveOriginalFolder();
            if (folder == null) return; // ResolveOriginalFolder already logged why

            running = StartCoroutine(RunValidation(folder));
        }

        /// <summary>Programmatic entry point with an explicit folder (tests, batch use).</summary>
        public void Validate(string sessionFolder)
        {
            originalSessionFolder = sessionFolder;
            Validate();
        }

        private IEnumerator RunValidation(string originalFolder)
        {
            // ---- 1. Load the original run's artifacts --------------------------
            SessionTrajectory original = SessionTrajectory.Load(
                Path.Combine(originalFolder, "events.jsonl"));
            if (original == null || original.Samples.Count < 2)
            {
                Debug.LogError("[UnityQA] Validation aborted — original session has no usable " +
                               "trajectory (was telemetry running when it was recorded?).");
                running = null;
                yield break;
            }

            string replayPath = Path.Combine(originalFolder, ReplayFileStore.FileName);
            player.SetReplayFile(replayPath);
            if (!player.LoadReplay())
            {
                running = null;
                yield break; // ReplayFileStore.Load already logged the reason
            }

            // ---- 2. Controlled initial conditions ------------------------------
            var controller = FindFirstObjectByType<PlayerController2D>();
            if (controller == null)
            {
                Debug.LogError("[UnityQA] Validation aborted — no PlayerController2D in scene.");
                running = null;
                yield break;
            }

            // HOTFIX-3a (M5.C determinism regression): if the scene has a
            // GameRun and its previous run ENDED, the player is still frozen
            // (controller disabled, body unsimulated). Replaying into that
            // state is a guaranteed divergence — restore Running first so the
            // replayed run starts from the same live state the original did.
            var gameRun = FindFirstObjectByType<GameRun>();
            if (gameRun != null && gameRun.State == GameRun.RunState.Ended)
            {
                Debug.Log("[UnityQA] Validation: previous run had ended — resetting GameRun first.");
                gameRun.ResetRun();
            }

            // HOTFIX-3b (root cause of the benchmark divergence): the replay's
            // input frame 0 corresponds to SESSION START (t = 0), but the
            // first telemetry sample is captured one sampler interval LATER
            // (t ≈ 1/telemetryHz) — by which time a moving player is already
            // ~0.6u past the true starting point. Teleporting to sample[0]
            // therefore started every validation run spatially AHEAD of the
            // original: a constant offset that stayed under the 0.75u
            // threshold on flat Level_Baseline (latent since M3.C) but turns
            // into a binary path change at Level_Benchmark's precision jumps.
            // Fix: first-order backward extrapolation to t = 0 using the
            // sample's own recorded velocity (available since M4.A) —
            // stationary starts are unchanged (v = 0 → same point).
            TrajectorySample s0 = original.Samples[0];
            Vector2 startPos = new Vector2(s0.x - s0.vx * s0.t, s0.y - s0.vy * s0.t);
            controller.transform.position = new Vector3(startPos.x, startPos.y, 0f);
            var body = controller.GetComponent<Rigidbody2D>();
            if (body != null) body.linearVelocity = Vector2.zero;

            // HOTFIX-4a (remaining half of "controlled initial conditions"):
            // position and velocity were being reset, but the CONTROLLER'S
            // INPUT LATCH was not. moveInput and jumpRequested are written
            // only in Update; GameRun.EndRun disables the controller, so both
            // freeze at the dead player's last command and survive ResetRun
            // and the teleport. FixedUpdate runs before Update, so the first
            // physics step after the teleport applied that stale command —
            // uncommanded horizontal motion, and a phantom jump from the
            // spawn point whenever the previous run ended with a press
            // latched. Clearing the latch makes the replay start from rest in
            // the input domain too.
            controller.ResetInputState();

            yield return new WaitForFixedUpdate(); // let physics settle the teleport

            // ---- 3. Replay under recording -------------------------------------
            // HOTFIX-4b (input-stream phase alignment): playback is now ARMED
            // BEFORE the session starts, and the extra frame of slack is gone.
            //
            // The old order was StartSession → yield null → Play, which meant
            // recorded frame 0 was applied on session-update #2 while the
            // ORIGINAL run consumed its frame 0 on session-update #0 — the
            // whole input stream ran ~2 rendered frames late against the
            // validation session's own telemetry clock, so every jump took off
            // late by the same amount. The dropped `yield return null` was
            // guarding against the sampler emitting inside the SessionStarted
            // publish, which SampleLoop already prevents structurally by
            // yielding its interval before the first EmitSample.
            //
            // Arming first has a second effect worth naming: SessionStarted
            // now reaches ReplayRecorder while the controller is already on
            // the ReplayInputSource, so the validation session's own
            // replay.json records the replayed input instead of the idle
            // keyboard — validation sessions become re-playable like any
            // other, and the catalog stops indexing empty replays.
            bool finished = false;
            Action onFinish = () => finished = true;
            player.PlaybackFinished += onFinish;
            player.Play();

            runner.StartSession();
            string validationSessionId = runner.CurrentSession.SessionId;
            string validationFolder = Path.Combine(QALogger.SessionsRoot,
                                                   runner.CurrentSession.FolderName);

            float originalDuration = original.Samples[original.Samples.Count - 1].t
                                   - original.Samples[0].t;
            float timeout = Time.time + originalDuration + runner.Config.validationTimeoutMargin;
            while (!finished && player.IsPlaying && Time.time < timeout)
                yield return null;
            player.PlaybackFinished -= onFinish;

            if (!finished)
            {
                player.Stop();
                Debug.LogWarning("[UnityQA] Validation playback timed out — result will reflect " +
                                 "the truncated run.");
            }

            runner.EndSession();
            yield return null;      // let QALogger flush and close the manifest

            // ---- 4. Compare and persist ----------------------------------------
            SessionTrajectory replayed = SessionTrajectory.Load(
                Path.Combine(validationFolder, "events.jsonl"));
            if (replayed == null)
            {
                running = null;
                yield break;
            }

            ReplayValidationResult result = TrajectoryComparer.Compare(
                original.Samples, replayed.Samples, runner.Config.validationDeviationThreshold);
            result.originalSessionId = original.SessionId;
            result.validationSessionId = validationSessionId;
            result.originalFolder = originalFolder;
            result.validationFolder = validationFolder;
            result.parseErrors = original.ParseErrors + replayed.ParseErrors;

            // M5.D stabilization: the second axis of fidelity — did the
            // replayed run END the same way? Outcomes come from each session's
            // own events.jsonl (last RunEnded wins — the same reader the
            // oracles use, so validator and oracles can never disagree).
            TrajectoryComparer.ApplyOutcomes(result,
                Oracles.OracleContextFactory.ReadLastRunOutcome(
                    Path.Combine(originalFolder, "events.jsonl")),
                Oracles.OracleContextFactory.ReadLastRunOutcome(
                    Path.Combine(validationFolder, "events.jsonl")));

            File.WriteAllText(Path.Combine(validationFolder, ResultFileName),
                              JsonUtility.ToJson(result, prettyPrint: true));

            LastResult = result;
            string outcomeBlock = result.outcomesCompared
                ? $"\n  Original Outcome: {result.originalOutcome}" +
                  $"\n  Replay Outcome:   {result.replayOutcome}" +
                  $"\n  Outcome Match:    {(result.outcomeMatch ? "YES" : "NO")}"
                : "\n  Outcomes: not compared (missing on one side, or original was a manual Quit)";
            Debug.Log($"[UnityQA] Replay validation {result.verdict}{outcomeBlock}" +
                      $"\n  Max Error:  {result.maxDeviation:F3}u (threshold {result.thresholdUnits:F2}u)" +
                      $"\n  Mean Error: {result.meanDeviation:F3}u   RMS: {result.rmsDeviation:F3}u" +
                      $"\n  First Divergence: " +
                      (result.firstDivergenceTime >= 0f
                          ? $"t={result.firstDivergenceTime:F2}s"
                          : "none (no threshold crossing)") +
                      $"\n  Samples: {result.comparedSamples}   Duration Δ: {result.durationDelta:F2}s" +
                      $"\n  → {Path.Combine(validationFolder, ResultFileName)}");

            running = null;
            ValidationCompleted?.Invoke(result);
        }

        /// <summary>Inspector path → absolute folder; empty → newest session with a replay.</summary>
        private string ResolveOriginalFolder()
        {
            if (!string.IsNullOrEmpty(originalSessionFolder))
            {
                string folder = Path.IsPathRooted(originalSessionFolder)
                    ? originalSessionFolder
                    : Path.Combine(QALogger.SessionsRoot, originalSessionFolder);
                if (Directory.Exists(folder)) return folder;
                Debug.LogError($"[UnityQA] Validation aborted — folder not found: '{folder}'");
                return null;
            }

            if (Directory.Exists(QALogger.SessionsRoot))
            {
                string[] folders = Directory.GetDirectories(QALogger.SessionsRoot);
                Array.Sort(folders);
                for (int i = folders.Length - 1; i >= 0; i--)
                    if (File.Exists(Path.Combine(folders[i], ReplayFileStore.FileName)))
                        return folders[i];
            }
            Debug.LogError("[UnityQA] Validation aborted — no session with a replay.json found.");
            return null;
        }

        private void OnDisable()
        {
            if (running != null)
            {
                StopCoroutine(running);
                running = null;
                if (player != null && player.IsPlaying) player.Stop();
                if (runner != null && runner.IsSessionActive) runner.EndSession();
            }
        }
    }
}
