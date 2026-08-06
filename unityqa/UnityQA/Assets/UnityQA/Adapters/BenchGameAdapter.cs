// -----------------------------------------------------------------------------
// UnityQA.Adapters — BenchGameAdapter.cs                (SRS §8, D-001 executed)
//
// PURPOSE
//   The ONE class in the entire framework allowed to know BenchGame exists.
//   Implements IGameAdapter (observation) and IGutSpecSource (session
//   metadata) by reading PlayerController2D's public, read-only surface.
//   This assembly (UnityQA.Adapters) references both UnityQA and BenchGame;
//   neither of those references the other — the SRS §5 dependency picture,
//   now enforced by asmdef instead of promised (NFR-1.3).
//
// EDGE DETECTION (why FixedUpdate)
//   Jump takeoffs and landings are single-physics-step facts; the sampler's
//   10 Hz poll would miss or mistime them. The adapter watches every
//   FixedUpdate and raises C# events on the transitions:
//     takeoff : grounded → airborne with upward velocity
//     landing : airborne → grounded (impact speed = last airborne |vy|)
//   Falling off a ledge is grounded → airborne with vy ≤ 0 — deliberately NOT
//   a JumpDetected (a fall is not a jump; detectors will care about the
//   difference).
//
// FAIL-LOUD CONTRACT (SRS §12)
//   If the scene has no PlayerController2D, Awake logs one descriptive error
//   and the adapter reports IsValid = false forever. Observers skip; nothing
//   throws; the game is never affected by its observer being confused.
// -----------------------------------------------------------------------------

using System;
using BenchGame;
using UnityEngine;
using UnityQA.Core;

namespace UnityQA.Adapters
{
    /// <summary>
    /// IGameAdapter for BenchGame. Scene setup: on the "[QA]" GameObject,
    /// alongside QARunner / QALogger / QATelemetrySampler.
    /// </summary>
    public sealed class BenchGameAdapter : MonoBehaviour, IGameAdapter, IGutSpecSource, IPlayerInputObserver, IRunOutcomeSource
    {
        private PlayerController2D controller;
        private Rigidbody2D controllerBody;
        private GameRun gameRun; // optional (M5.C): null in scenes without a run controller

        private bool prevGrounded;
        private float lastAirborneVy;

        public bool IsValid => controller != null;

        public Vector2 PlayerPosition => controller.transform.position;
        public Vector2 PlayerVelocity => controller.Velocity;
        public bool IsGrounded => controller.IsGrounded;
        public float MoveInput => controller.MoveInput;

        public event Action JumpDetected;
        public event Action<float> LandedDetected;

        private void Awake()
        {
            // FindFirstObjectByType: one player per level is a BenchGame
            // invariant (GUT-SPEC feature list) — no need for tags or lookups.
            controller = FindFirstObjectByType<PlayerController2D>();
            if (controller == null)
            {
                Debug.LogError("[UnityQA] BenchGameAdapter: no PlayerController2D found in scene '" +
                               gameObject.scene.name + "'. Telemetry disabled. Is this a BenchGame level?");
                enabled = false;
                return;
            }

            controllerBody = controller.GetComponent<Rigidbody2D>();
            prevGrounded = controller.IsGrounded;

            // M5.C: observe the run controller when the scene has one.
            // Level_Baseline has none — everything else works unchanged.
            gameRun = FindFirstObjectByType<GameRun>();
            if (gameRun != null)
                gameRun.RunEnded += OnGameRunEnded;
        }

        /// <summary>IRunOutcomeSource (M5.C): the game's outcome enum mapped to
        /// the neutral shape core UnityQA consumes — the adapter is the only
        /// place BenchGame.SessionOutcome names are known.</summary>
        public event Action<RunOutcomeInfo> RunEndedDetected;

        private void OnGameRunEnded(SessionOutcome outcome)
        {
            RunEndedDetected?.Invoke(new RunOutcomeInfo
            {
                outcome = outcome.ToString(),
                isDeath = outcome == SessionOutcome.SpikeDeath || outcome == SessionOutcome.OutOfBounds,
                cause = outcome == SessionOutcome.SpikeDeath ? "spike"
                      : outcome == SessionOutcome.OutOfBounds ? "outOfBounds" : "",
                isSuccess = outcome == SessionOutcome.Success
            });
        }

        private void OnDestroy()
        {
            if (gameRun != null)
                gameRun.RunEnded -= OnGameRunEnded;
        }

        private void FixedUpdate()
        {
            if (!IsValid) return;

            bool grounded = controller.IsGrounded;
            float vy = controller.Velocity.y;

            if (prevGrounded && !grounded && vy > 0f)
            {
                // Left the ground moving up = jump takeoff.
                JumpDetected?.Invoke();
            }
            else if (!prevGrounded && grounded)
            {
                // Touched down; impact speed is the last airborne downward speed.
                LandedDetected?.Invoke(Mathf.Max(0f, -lastAirborneVy));
            }

            if (!grounded) lastAirborneVy = vy;
            prevGrounded = grounded;
        }

        /// <summary>
        /// IPlayerInputObserver (M2 Slice D): the player's ATTEMPTED commands,
        /// read through the controller's D-008 input seam. Frame-domain — the
        /// recorder calls this from Update, matching the seam's contract.
        /// </summary>
        public bool TryGetInputState(out PlayerInputState state)
        {
            if (!IsValid || controller.InputSource == null)
            {
                state = default;
                return false;
            }

            IPlayerInputSource src = controller.InputSource;
            state = new PlayerInputState
            {
                horizontal = (int)src.MoveX, // seam contract: exactly -1, 0, +1
                jumpHeld = src.JumpHeld
            };
            return true;
        }

        public bool TryGetGutSpec(out GutSpecData spec)
        {
            if (!IsValid)
            {
                spec = default;
                return false;
            }

            spec = new GutSpecData
            {
                runSpeed = controller.RunSpeed,
                jumpHeight = controller.JumpHeight,
                gravityScale = controllerBody != null ? controllerBody.gravityScale : 0f
            };
            return true;
        }
    }
}
