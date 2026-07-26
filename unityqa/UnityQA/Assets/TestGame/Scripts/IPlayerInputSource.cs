// -----------------------------------------------------------------------------
// BenchGame — IPlayerInputSource.cs                     (D-008, executed M2.D)
//
// PURPOSE
//   The input seam approved back at the Milestone 2 design gate: the player
//   controller asks "what is the player commanding?" through this interface
//   instead of reading UnityEngine.Input directly.
//
// WHY A NORMAL GAME WOULD HAVE THIS (the "foreign code" defense, SRS §1.1)
//   Input abstraction is ordinary game architecture — rebinding, gamepad
//   support, attract-mode demos, and replays all need it. Nothing here knows
//   UnityQA exists.
//
// WHAT IT UNLOCKS (without implementing it now)
//   - Slice D: exact capture of attempted input (via the adapter).
//   - Module 2: the AI agent becomes just another IPlayerInputSource.
//   - Future replay: a recorded trace played back as an input source.
//
// CONTRACT
//   Properties are FRAME-DOMAIN: valid when read during Update (the phase the
//   controller reads them in). JumpDown is true only on the frame the button
//   went down — GetButtonDown semantics, preserved exactly.
// -----------------------------------------------------------------------------

namespace BenchGame
{
    /// <summary>What the player is commanding this frame. Read during Update.</summary>
    public interface IPlayerInputSource
    {
        /// <summary>Horizontal command: exactly -1, 0, or +1 (unsmoothed).</summary>
        float MoveX { get; }

        /// <summary>True only on the frame the jump button was pressed.</summary>
        bool JumpDown { get; }

        /// <summary>True while the jump button is held.</summary>
        bool JumpHeld { get; }
    }
}
