// -----------------------------------------------------------------------------
// UnityQA — IQualityOracle.cs                                    (M5 Slice B)
//
// PURPOSE
//   The contract every quality oracle fulfills. An oracle is a DETERMINISTIC
//   rule that examines one session's evidence (OracleContext) and states
//   whether the session satisfies one specific quality expectation. This
//   slice ships the contract and machinery ONLY — concrete oracles
//   (ReplayConsistency, SoftLock, InvalidState, MissingEvent, Performance)
//   are the next slice, by explicit scope.
//
// CONTRACT
//   - Evaluate MUST be deterministic and side-effect-free: same context in,
//     same result out; no file writes, no engine state, no randomness, no
//     wall-clock reads (the runner stamps time).
//   - Return null to mean "not applicable to this session" (e.g. a replay
//     oracle on a session with no replay) — the runner records a skip, which
//     is different from a pass and MUCH different from a fail.
//   - Throwing marks the ORACLE broken, not the game: the runner isolates
//     the exception and keeps running (same philosophy as the M1 event bus).
// -----------------------------------------------------------------------------

namespace UnityQA.Oracles
{
    /// <summary>One deterministic quality rule. Registered via OracleRegistry.</summary>
    public interface IQualityOracle
    {
        /// <summary>Unique, stable identifier — the registry's key and the
        /// name reports will cite. Never localized, never renamed casually.</summary>
        string Name { get; }

        /// <summary>One sentence: what expectation does this oracle check?</summary>
        string Description { get; }

        /// <summary>Disabled oracles stay registered (order preserved) but are
        /// never evaluated. Toggled via OracleRegistry.SetEnabled.</summary>
        bool Enabled { get; set; }

        /// <summary>Judge one session. Null = not applicable (skip).</summary>
        OracleResult Evaluate(OracleContext context);
    }
}
