// -----------------------------------------------------------------------------
// BenchGame — PlayerController2D.cs                                  (SRS §1.1)
//
// PURPOSE
//   Minimal, deterministic 2D platformer movement: horizontal run at a constant
//   speed and a single fixed-height jump. Nothing else — by specification.
//
// WHY IT EXISTS
//   BenchGame is scientific apparatus (SRS §2.4). This controller's job is to
//   have EXACTLY known kinematics so Module 4 can compute reachability from
//   first principles. That is why jump *height* is the authored value and jump
//   *velocity* is derived from it (v = sqrt(2·g·h)) — the number written in
//   GUT-SPEC.md is then exact by construction, not by tuning.
//
// DETERMINISM RULES HONORED (FR-1.19)
//   - All physics happens in FixedUpdate on the fixed timestep.
//   - Input is SAMPLED in Update but APPLIED in FixedUpdate: Update runs once
//     per rendered frame (frame-rate dependent), FixedUpdate once per physics
//     step (frame-rate independent). Applying forces in Update would make the
//     player's motion depend on the machine's frame rate — a determinism bug.
//   - The jump request is LATCHED (a bool set in Update, consumed in
//     FixedUpdate) so a key press that lands between two physics steps is
//     never lost.
//   - No randomness, no frame-rate-dependent timers.
//
// FOREIGN-CODE RULE (SRS §1.1)
//   This class knows nothing about UnityQA. Velocity/IsGrounded are exposed as
//   ordinary public read-only properties — the kind any reasonable game would
//   have — which is all a future adapter is allowed to rely on.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace BenchGame
{
    /// <summary>
    /// BenchGame player movement: constant-speed run + single fixed-height jump.
    /// Movement constants are documented in docs/GUT-SPEC.md (FR-1.20); the
    /// inspector values here and that document must always agree.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class PlayerController2D : MonoBehaviour
    {
        [Header("Movement — GUT-SPEC.md is the authoritative record (FR-1.20)")]
        [Tooltip("Horizontal run speed in world units per second.")]
        [SerializeField] private float runSpeed = 6f;

        [Tooltip("Apex height of a jump in world units. Jump velocity is DERIVED " +
                 "from this via v = sqrt(2·g·h), so this value is exact by construction.")]
        [SerializeField] private float jumpHeight = 2.2f;

        [Header("Ground check")]
        [Tooltip("Empty child positioned at the player's feet.")]
        [SerializeField] private Transform groundCheck;

        [Tooltip("Size of the box tested for ground contact. Slightly narrower than " +
                 "the player collider so wall-touches don't count as 'grounded'.")]
        [SerializeField] private Vector2 groundCheckSize = new Vector2(0.55f, 0.10f);

        [Tooltip("Layers that count as ground. Set to the 'Ground' layer only.")]
        [SerializeField] private LayerMask groundLayer;

        private Rigidbody2D body;

        // Input state: written in Update, read in FixedUpdate. Single-threaded,
        // so no locking is needed — Unity calls both on the main thread.
        private float moveInput;      // -1, 0, or +1 (GetAxisRaw is unsmoothed — deterministic)
        private bool jumpRequested;   // latched on key-down, consumed by the next physics step

        private bool isGrounded;

        /// <summary>Current world-space velocity (read-only observation surface).</summary>
        public Vector2 Velocity => body.linearVelocity;

        /// <summary>True while the ground-check box overlaps the Ground layer.</summary>
        public bool IsGrounded => isGrounded;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
        }

        private void Update()
        {
            // GetAxisRaw, not GetAxis: raw returns exactly -1/0/+1 with no input
            // smoothing. Smoothing is feel-polish for real games; for a benchmark
            // it is a hidden, frame-rate-coupled state variable — so we refuse it.
            moveInput = Input.GetAxisRaw("Horizontal");

            // Latch, don't act: acting here would couple jumping to frame rate.
            if (Input.GetButtonDown("Jump"))
            {
                jumpRequested = true;
            }
        }

        private void FixedUpdate()
        {
            // Ground test: a small box at the feet against the Ground layer.
            // OverlapBox (not raycast) tolerates standing on tile seams and edges.
            isGrounded = Physics2D.OverlapBox(
                groundCheck.position, groundCheckSize, 0f, groundLayer) != null;

            Vector2 velocity = body.linearVelocity;

            // Horizontal: velocity is SET, not force-added. Constant speed with
            // instant direction change — trivially analyzable kinematics, which
            // is the whole point of BenchGame (SRS §2.4).
            velocity.x = moveInput * runSpeed;

            // Jump: only from the ground (single jump, SRS §1.1 feature list).
            if (jumpRequested && isGrounded)
            {
                // v = sqrt(2·g·h) — solve projectile apex for initial velocity.
                // g must include this body's gravityScale multiplier.
                float g = Mathf.Abs(Physics2D.gravity.y) * body.gravityScale;
                velocity.y = Mathf.Sqrt(2f * g * jumpHeight);
            }

            // Consume the latch every step: an airborne press should NOT be
            // stored and fired on landing (that would be a hidden jump buffer —
            // excluded by SRS §1.1's minimalism rule).
            jumpRequested = false;

            body.linearVelocity = velocity;
        }

        // Editor-only visualization of the ground-check box; compiled out of builds.
        private void OnDrawGizmosSelected()
        {
            if (groundCheck == null) return;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(groundCheck.position, groundCheckSize);
        }
    }
}
