// -----------------------------------------------------------------------------
// BenchGame — ExitDoor.cs                                        (M5 Slice C)
//
// PURPOSE
//   The benchmark objective: the player entering its trigger ends the run as
//   Success. Same report-don't-decide shape as SpikeHazard.
//
// SETUP: BoxCollider2D with Is Trigger ON, on the same GameObject.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace BenchGame
{
    /// <summary>Reach = Success. Requires a trigger collider.</summary>
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class ExitDoor : MonoBehaviour
    {
        private GameRun run;

        private void Awake()
        {
            run = FindFirstObjectByType<GameRun>();
            if (run == null)
                Debug.LogWarning("[BenchGame] ExitDoor found no GameRun — door inert.");
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (run == null) return;
            if (other.GetComponent<PlayerController2D>() == null) return;
            run.EndRun(SessionOutcome.Success);
        }
    }
}
