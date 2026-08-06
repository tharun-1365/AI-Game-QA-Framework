// -----------------------------------------------------------------------------
// BenchGame — SpikeHazard.cs                                     (M5 Slice C)
//
// PURPOSE
//   Static spike: the player touching its trigger ends the run as SpikeDeath.
//   No movement, no animation, no state — a red square with a consequence.
//   Outcome policy lives in GameRun (this class reports, GameRun decides —
//   including the once-only rule).
//
// SETUP: BoxCollider2D with Is Trigger ON, on the same GameObject.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace BenchGame
{
    /// <summary>Touch = SpikeDeath. Requires a trigger collider.</summary>
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class SpikeHazard : MonoBehaviour
    {
        private GameRun run;

        private void Awake()
        {
            run = FindFirstObjectByType<GameRun>();
            if (run == null)
                Debug.LogWarning("[BenchGame] SpikeHazard found no GameRun — spikes inert.");
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (run == null) return;
            if (other.GetComponent<PlayerController2D>() == null) return; // only the player dies here
            run.EndRun(SessionOutcome.SpikeDeath);
        }
    }
}
