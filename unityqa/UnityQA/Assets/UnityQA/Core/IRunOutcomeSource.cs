// -----------------------------------------------------------------------------
// UnityQA — IRunOutcomeSource.cs                                 (M5 Slice C)
//
// PURPOSE
//   Game-agnostic view of "the run ended, and this is how" — the third small
//   observer interface in the IGutSpecSource / IPlayerInputObserver pattern:
//   the frozen IGameAdapter file stays untouched; BenchGameAdapter simply
//   implements one more contract, and consumers null-check so scenes without
//   a run controller (Level_Baseline) keep working unchanged.
//
//   RunOutcomeInfo is deliberately neutral: the adapter maps the game's own
//   outcome enum into (name, isDeath, isSuccess, cause) so core UnityQA never
//   hardcodes another game's enum names.
// -----------------------------------------------------------------------------

using System;

namespace UnityQA.Core
{
    /// <summary>Neutral description of a finished run.</summary>
    public struct RunOutcomeInfo
    {
        /// <summary>Outcome name as recorded in logs (e.g. "SpikeDeath").</summary>
        public string outcome;
        /// <summary>True for death-class outcomes → a PlayerDied event is emitted.</summary>
        public bool isDeath;
        /// <summary>Death cause for the PlayerDied payload (e.g. "spike").</summary>
        public string cause;
        /// <summary>True when the objective was reached → a TriggerFired event is emitted.</summary>
        public bool isSuccess;
    }

    /// <summary>Provider of run-end observations (implemented by the game adapter).</summary>
    public interface IRunOutcomeSource
    {
        /// <summary>Raised once when the observed run ends.</summary>
        event Action<RunOutcomeInfo> RunEndedDetected;
    }
}
