// -----------------------------------------------------------------------------
// BenchGame — FollowCamera.cs                                        (SRS §1.1)
//
// PURPOSE
//   Keep the player on screen with a smoothed follow. That is the entire job.
//
// WHY IT EXISTS (and why not Cinemachine)
//   SRS §7 and Rule 8: a ~20-line script we fully understand beats a large
//   camera framework we would use 2% of. Cinemachine earns its complexity in
//   real games (multiple virtual cameras, blends, confiners); BenchGame needs
//   "follow one target", so it gets exactly that and nothing more.
//
// DETERMINISM NOTE
//   The camera is VISUAL-ONLY: nothing in BenchGame or UnityQA reads camera
//   position for gameplay or detection. Its SmoothDamp easing is therefore
//   allowed to be frame-rate dependent without violating FR-1.19 — determinism
//   is required of the *gameplay simulation*, not of presentation.
//
// WHY LateUpdate
//   Runs after all Update/physics interpolation for the frame, so the camera
//   sees the player's FINAL position. Following from Update causes the classic
//   one-frame-behind jitter.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace BenchGame
{
    /// <summary>
    /// Minimal smoothed follow camera locked to the target's X/Y.
    /// The camera's own Z (e.g. -10) is preserved.
    /// </summary>
    public sealed class FollowCamera : MonoBehaviour
    {
        [Tooltip("What to follow — the Player's transform.")]
        [SerializeField] private Transform target;

        [Tooltip("Approximate seconds to catch up to the target. 0 = rigid lock.")]
        [SerializeField] private float smoothTime = 0.15f;

        [Tooltip("View offset from the target, e.g. (0, 1) to look slightly above the feet.")]
        [SerializeField] private Vector2 offset = new Vector2(0f, 1f);

        private Vector3 dampVelocity; // internal state for SmoothDamp — do not touch

        private void LateUpdate()
        {
            if (target == null) return; // fail quiet: a camera without a target just holds still

            Vector3 goal = new Vector3(
                target.position.x + offset.x,
                target.position.y + offset.y,
                transform.position.z); // never move in Z — orthographic 2D camera

            transform.position = Vector3.SmoothDamp(
                transform.position, goal, ref dampVelocity, smoothTime);
        }
    }
}
