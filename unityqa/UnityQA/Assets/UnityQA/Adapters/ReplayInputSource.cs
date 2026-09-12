// -----------------------------------------------------------------------------
// UnityQA.Adapters — ReplayInputSource.cs                        (M3 Slice B)
//
// PURPOSE
//   IPlayerInputSource implementation fed from a recorded ReplayFrame instead
//   of the keyboard. To PlayerController2D it is indistinguishable from
//   KeyboardInputSource — that interchangeability IS the D-008 seam paying
//   out, and the reason the controller needed zero replay code.
//
// DESIGN
//   A plain class, not a MonoBehaviour: it has no lifecycle of its own —
//   ReplayPlayer owns it and pushes one frame into it per Update (SetFrame),
//   and the controller pulls the three properties. Between playbacks, and
//   after Clear(), it reports neutral input (stand still, no jump) so a
//   finished replay can never leave a phantom key held down.
//
//   Lives in UnityQA.Adapters for the same reason as ReplayRecorder (D-011):
//   it implements a BenchGame interface while consuming UnityQA.Replay data —
//   only the bridge assembly sees both sides.
// -----------------------------------------------------------------------------

using BenchGame;
using UnityQA.Replay;

namespace UnityQA.Adapters
{
    /// <summary>Replay-driven input. ReplayPlayer pushes frames; the controller pulls.</summary>
    public sealed class ReplayInputSource : IPlayerInputSource
    {
        private float moveX;
        private bool jumpDown;
        private bool jumpHeld;

        public float MoveX => moveX;
        public bool JumpDown => jumpDown;
        public bool JumpHeld => jumpHeld;

        /// <summary>Make the given recorded frame the current input. O(1), allocation-free.</summary>
        public void SetFrame(in ReplayFrame frame)
        {
            moveX = frame.horizontal;
            jumpDown = frame.jumpPressed;   // recorded down-edge replays as a down-edge
            jumpHeld = frame.jumpHeld;
        }

        /// <summary>
        /// Drop only the jump down-edge, keeping movement and hold state
        /// (HOTFIX-5). With fixed-step playback the frame is pushed in
        /// FixedUpdate and consumed by the controller's FixedUpdate in the
        /// same step; the controller's UPDATE then also reads JumpDown to
        /// latch keyboard presses — if the edge were still asserted there, one
        /// recorded press would latch a second, phantom jump for the next
        /// step. ReplayPlayer clears the edge in its Update (order -50, before
        /// the controller's) so each recorded down-edge is consumed exactly
        /// once, by the step it was recorded on.
        /// </summary>
        public void ClearJumpEdge()
        {
            jumpDown = false;
        }

        /// <summary>Reset to neutral (no movement, no jump). Called on stop/finish.</summary>
        public void Clear()
        {
            moveX = 0f;
            jumpDown = false;
            jumpHeld = false;
        }
    }
}
