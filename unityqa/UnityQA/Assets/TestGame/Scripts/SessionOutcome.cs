// -----------------------------------------------------------------------------
// BenchGame — SessionOutcome.cs                                  (M5 Slice C)
//
// PURPOSE
//   The four deterministic ways a benchmark run can end. A GAME concept
//   (BenchGame owns how runs end), observed by UnityQA through the adapter —
//   the foreign-code rule holds: nothing here knows QA exists.
//
//   Values are frozen (serialized into logs as names): append-only, never
//   renumber or rename.
// -----------------------------------------------------------------------------

namespace BenchGame
{
    /// <summary>How a benchmark run ended. Exactly one per run.</summary>
    public enum SessionOutcome
    {
        /// <summary>The player reached the Exit Door.</summary>
        Success = 0,

        /// <summary>The player touched a spike hazard.</summary>
        SpikeDeath = 1,

        /// <summary>The player fell below the world kill boundary.</summary>
        OutOfBounds = 2,

        /// <summary>The run was ended manually (Escape).</summary>
        Quit = 3,
    }
}
