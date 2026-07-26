// -----------------------------------------------------------------------------
// UnityQA — QAEventType.cs                                (EVENT-SCHEMA.md §3)
//
// PURPOSE
//   The closed registry of everything UnityQA can observe, as an enum.
//
// WHY AN ENUM, NOT STRINGS
//   A typo'd string event ("ColisionEnter") would flow through the whole
//   pipeline silently and corrupt datasets. A typo'd enum member does not
//   compile. The string form written to disk is derived from the enum name,
//   so code and files can never disagree.
//
// STABILITY CONTRACT (frozen with schema v1)
//   Numeric values are FOREVER. New members are appended with new numbers;
//   nothing is ever renumbered, renamed, or removed — logs written today must
//   parse correctly years from now. M1.3 values are reserved ahead of time so
//   the next milestone cannot be tempted to renumber.
// -----------------------------------------------------------------------------

namespace UnityQA.Core
{
    /// <summary>Discrete observation kinds. Values are frozen — append only.</summary>
    public enum QAEventType
    {
        // --- Session lifecycle (M1.2) ---
        SessionStarted = 0,
        SessionEnded = 1,

        // --- Gameplay observations (M1.2) ---
        JumpExecuted = 10,
        Landed = 11,
        BoundsExited = 12,
        CollisionEnter = 13,
        CollisionExit = 14,

        // --- Reserved for Milestone 3 (BenchGame completion) ---
        PlayerSpawned = 20,
        PlayerDied = 21,
        TokenCollected = 22,
        TriggerFired = 23,
        ExpectedTriggersSummary = 24,

        // --- Framework diagnostics (M1.2) ---
        AdapterWarning = 90,
    }
}
