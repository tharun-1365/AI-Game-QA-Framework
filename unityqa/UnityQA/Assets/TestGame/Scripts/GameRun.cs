// -----------------------------------------------------------------------------
// BenchGame — GameRun.cs                                         (M5 Slice C)
//
// PURPOSE
//   The benchmark run controller: owns run state (Running → Ended), enforces
//   "exactly one outcome per run" structurally (EndRun is a no-op once
//   ended), watches the kill boundary, freezes the player at run end, and
//   raises an ordinary C# event any observer may subscribe to. Deterministic
//   throughout: fixed spawn, fixed killY, no randomness, no timers.
//
// FOREIGN-CODE RULE
//   Zero UnityQA knowledge. RunEnded is the kind of event any small game
//   would expose; the QA adapter subscribes to it exactly as it subscribes
//   to the player's public surface.
//
// KEYS (manual play): Escape = quit the run · R = reset after a run ends.
// -----------------------------------------------------------------------------

using System;
using UnityEngine;

namespace BenchGame
{
    /// <summary>Benchmark run lifecycle. One per benchmark scene.</summary>
    public sealed class GameRun : MonoBehaviour
    {
        public enum RunState { Running, Ended }

        [Tooltip("Where the player starts and returns on reset.")]
        [SerializeField] private Transform spawnPoint;

        [Tooltip("Falling below this world Y ends the run as OutOfBounds.")]
        [SerializeField] private float killY = -5f;

        private PlayerController2D player;
        private Rigidbody2D playerBody;

        public RunState State { get; private set; } = RunState.Running;

        /// <summary>Valid only when State == Ended.</summary>
        public SessionOutcome Outcome { get; private set; }

        /// <summary>Raised exactly once per run, at the moment it ends.</summary>
        public event Action<SessionOutcome> RunEnded;

        private void Awake()
        {
            player = FindFirstObjectByType<PlayerController2D>();
            if (player == null)
            {
                Debug.LogError("[BenchGame] GameRun found no PlayerController2D — disabling.");
                enabled = false;
                return;
            }
            playerBody = player.GetComponent<Rigidbody2D>();
        }

        private void FixedUpdate()
        {
            // Kill-boundary watch on the physics clock (deterministic, FR-1.19).
            if (State == RunState.Running && player.transform.position.y < killY)
                EndRun(SessionOutcome.OutOfBounds);
        }

        private void Update()
        {
            if (State == RunState.Running && Input.GetKeyDown(KeyCode.Escape))
                EndRun(SessionOutcome.Quit);
            if (State == RunState.Ended && Input.GetKeyDown(KeyCode.R))
                ResetRun();
        }

        /// <summary>
        /// End the run with an outcome. Structurally once-only: after the
        /// first call the run is Ended and every later call is ignored — a
        /// spike touch during the same step as a fall can never double-record.
        /// </summary>
        public void EndRun(SessionOutcome outcome)
        {
            if (State != RunState.Running) return;
            State = RunState.Ended;
            Outcome = outcome;

            // Freeze: no further input, no further physics — the end state is
            // stable and identical every time (determinism over spectacle).
            player.enabled = false;
            if (playerBody != null)
            {
                playerBody.linearVelocity = Vector2.zero;
                playerBody.simulated = false;
            }

            Debug.Log($"[BenchGame] Run ended — {outcome}");
            RunEnded?.Invoke(outcome);
        }

        /// <summary>Deterministic reset to spawn for the next manual run.</summary>
        public void ResetRun()
        {
            if (spawnPoint != null)
                player.transform.position = spawnPoint.position;
            if (playerBody != null)
            {
                playerBody.simulated = true;
                playerBody.linearVelocity = Vector2.zero;
            }
            player.enabled = true;
            State = RunState.Running;
            Debug.Log("[BenchGame] Run reset.");
        }
    }
}
