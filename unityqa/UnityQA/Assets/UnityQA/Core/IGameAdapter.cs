// -----------------------------------------------------------------------------
// UnityQA — IGameAdapter.cs                              (SRS §12, M2 design §4)
//
// PURPOSE
//   The observation contract: everything UnityQA is allowed to know about the
//   game under test, expressed game-agnostically. Lives in the UnityQA
//   assembly so Core/Recording code can hold a reference WITHOUT referencing
//   BenchGame (NFR-1.3). The concrete BenchGameAdapter lives in the separate
//   UnityQA.Adapters assembly — the only bridge (D-001, executed this slice).
//
// SHAPE NOTES
//   - Properties are POLLED state (the sampler's diet).
//   - C# events are EDGES the adapter detects at fixed-step resolution (jump
//     takeoff, landing) — a 10 Hz poll would miss them; the adapter, which
//     watches every physics step, does not. Detection is pure observation of
//     public game state; the game is never touched (NFR-1.1).
//   - IGutSpecSource is split into its own tiny interface: QALogger needs the
//     GUT constants for session.json and nothing else — no reason to hand it
//     the whole observation surface.
// -----------------------------------------------------------------------------

using System;
using UnityEngine;

namespace UnityQA.Core
{
    /// <summary>The GUT's authored movement constants (GUT-SPEC.md, FR-1.20).</summary>
    [Serializable]
    public struct GutSpecData
    {
        public float runSpeed;
        public float jumpHeight;
        public float gravityScale;
    }

    /// <summary>Provider of GUT constants for session metadata (session.json §2).</summary>
    public interface IGutSpecSource
    {
        /// <returns>False when the game is not present/recognized (values then unusable).</returns>
        bool TryGetGutSpec(out GutSpecData spec);
    }

    /// <summary>
    /// Game-agnostic observation surface. One implementation per game under
    /// test; UnityQA never sees past this interface.
    /// </summary>
    public interface IGameAdapter
    {
        /// <summary>False when the adapter failed to bind (e.g. no Player in scene).</summary>
        bool IsValid { get; }

        Vector2 PlayerPosition { get; }
        Vector2 PlayerVelocity { get; }
        bool IsGrounded { get; }

        /// <summary>Horizontal command the controller consumed this step: -1, 0, +1.</summary>
        float MoveInput { get; }

        /// <summary>Raised on the physics step where a jump takeoff is observed.</summary>
        event Action JumpDetected;

        /// <summary>Raised on the physics step where a landing is observed; the
        /// argument is the downward speed at impact (positive number).</summary>
        event Action<float> LandedDetected;
    }
}
