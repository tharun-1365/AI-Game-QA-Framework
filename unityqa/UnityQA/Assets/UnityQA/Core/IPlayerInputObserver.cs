// -----------------------------------------------------------------------------
// UnityQA — IPlayerInputObserver.cs                              (M2 Slice D)
//
// PURPOSE
//   Game-agnostic view of "what is the player attempting?" — the input-side
//   sibling of IGameAdapter's state observations. A separate small interface
//   (like IGutSpecSource) so the frozen Slice C IGameAdapter file stays
//   untouched; BenchGameAdapter simply implements one more contract.
//
//   Telemetry (Slice C) records what the game DID; this records what the
//   player TRIED. The difference is QA gold: a jump attempted mid-air appears
//   here with no matching JumpExecuted — invisible to pure state telemetry.
//
// InputSampleGate
//   The emit-decision logic (changed? keyframe due?) is a pure static class
//   so EditMode tests pin it without a scene, and QAInputRecorder stays a
//   thin shell around tested logic.
// -----------------------------------------------------------------------------

namespace UnityQA.Core
{
    /// <summary>Snapshot of attempted input, frame-domain. Equality by value.</summary>
    public struct PlayerInputState
    {
        /// <summary>Attempted horizontal command: -1, 0, or +1.</summary>
        public int horizontal;

        /// <summary>Jump button currently held.</summary>
        public bool jumpHeld;

        public bool Equals(in PlayerInputState other) =>
            horizontal == other.horizontal && jumpHeld == other.jumpHeld;
    }

    /// <summary>Provider of attempted-input state (implemented by the game adapter).</summary>
    public interface IPlayerInputObserver
    {
        /// <returns>False when input cannot be observed (no player/source bound).</returns>
        bool TryGetInputState(out PlayerInputState state);
    }

    /// <summary>
    /// Pure emit-decision: emit iff the input changed OR a keyframe is due.
    /// Keyframes exist so a log tail (or a future replay seek) can rebuild
    /// full input state without scanning to the session start.
    /// </summary>
    public static class InputSampleGate
    {
        public static bool ShouldEmit(in PlayerInputState previous, in PlayerInputState current,
                                      int stepsSinceKeyframe, int keyframeEverySteps,
                                      out bool isKeyframe)
        {
            // Guard a misconfigured 0/negative exactly like JsonlSink does:
            // degenerate to keyframe-every-step, never an exception.
            if (keyframeEverySteps < 1) keyframeEverySteps = 1;

            isKeyframe = stepsSinceKeyframe >= keyframeEverySteps;
            return isKeyframe || !current.Equals(previous);
        }
    }
}
