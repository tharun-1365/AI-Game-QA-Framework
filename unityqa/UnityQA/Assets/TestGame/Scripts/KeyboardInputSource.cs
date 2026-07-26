// -----------------------------------------------------------------------------
// BenchGame — KeyboardInputSource.cs                    (D-008, executed M2.D)
//
// PURPOSE
//   The default IPlayerInputSource: byte-for-byte the same legacy Input calls
//   PlayerController2D made directly before this slice (D-002 API choice),
//   now behind the seam. With this source attached, gameplay is IDENTICAL to
//   pre-seam behavior — regression-pinned by the M1 tile rulers.
//
//   PlayerController2D auto-adds this component when no other input source is
//   present, so existing scenes and prefabs need no editing.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace BenchGame
{
    /// <summary>Default keyboard input via the legacy Input Manager (D-002).</summary>
    public sealed class KeyboardInputSource : MonoBehaviour, IPlayerInputSource
    {
        public float MoveX => Input.GetAxisRaw("Horizontal");
        public bool JumpDown => Input.GetButtonDown("Jump");
        public bool JumpHeld => Input.GetButton("Jump");
    }
}
