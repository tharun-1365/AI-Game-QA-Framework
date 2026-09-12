// -----------------------------------------------------------------------------
// UnityQA — MissingTriggerOracle.cs                              (M6 Slice B)
//
// PURPOSE
//   "The player reached the exit — did the exit actually fire?" — detects the
//   missing/broken completion-trigger defect class (PB-004) from gameplay
//   evidence, never from a planted-bug ID or a hardcoded exit coordinate.
//
// THE EVIDENCE PROBLEM (read before the rule)
//   The exit's location is observable ONLY through the TriggerFired event it
//   emits — which is exactly the event a broken exit fails to produce. There
//   is no expected-trigger registry or level-bounds record in the current
//   build (QAEventType.ExpectedTriggersSummary is declared but never emitted;
//   no LevelBounds instrumentation exists), so a SINGLE direct play cannot, on
//   its own evidence, prove "the player reached where the exit is" — see the
//   M6.B build log for this limitation, reported rather than papered over.
//
//   What the framework DOES already record is the replay-regression pair
//   (M3.C/M5.D validation.json): an ORIGINAL run that reached and fired the
//   exit (originalOutcome == "Success"), and a faithful REPLAY of that run on
//   the same route. That pair supplies the missing reference: if the replay
//   reproduced the run (validation verdict PASS — same trajectory, so the
//   player demonstrably reached the exit's position) but produced no
//   completion, the exit is reachable yet silently non-functional.
//
// DECISION RULE
//   • This run itself ended in Success                         → PASS (the
//     completion trigger fired — the direct, evidence-complete case).
//   • Else, no faithful "original reached the exit" reference  → SKIP: cannot
//     establish the player reached the exit region (validation absent, or the
//     original did not itself complete).
//   • Original completed, replay also Success                  → PASS.
//   • Original completed, replay ended in a FAILURE (spike /
//     out-of-bounds / quit)                                    → SKIP: the run
//     failed BEFORE the exit — a hazard, not a trigger defect (HazardOracle's
//     domain; keeps PB-003 out of this oracle).
//   • Original completed, replay faithful (verdict PASS) but
//     produced NO completion and fired NO trigger this run     → FAIL: the
//     exit fired for the original, the replay walked the same route, yet
//     nothing fired and the run never ended — a missing completion trigger.
//
// SEVERITY
//   FAIL → CRITICAL: a completion trigger that never fires makes the level
//   unwinnable — a functional defect on the OutOfBounds tier, not a mere
//   warning. (This is the oracle-result severity scale info/warning/critical;
//   it is coarser than BENCHMARK.md's per-bug Major/Critical column.)
// -----------------------------------------------------------------------------

using UnityQA.Replay;

namespace UnityQA.Oracles
{
    /// <summary>Missing/broken completion-trigger detector (PB-004 class).</summary>
    public sealed class MissingTriggerOracle : IQualityOracle
    {
        public string Name => "MissingTrigger";
        public string Description =>
            "A run that reaches the level's completion trigger actually fires it (the exit is not silently broken).";
        public bool Enabled { get; set; } = true;

        public OracleResult Evaluate(OracleContext context)
        {
            // Direct, evidence-complete case: this run reached the exit and it fired.
            if (context.RunOutcome == "Success")
            {
                var ok = new OracleResult
                {
                    passed = true,
                    severity = OracleResult.SeverityInfo,
                    reason = "The completion trigger fired: the run reached the exit and ended in Success."
                };
                ok.evidence.Add("sessionOutcome=Success");
                ok.evidence.Add("triggerFired=true");
                return ok;
            }

            // Otherwise we need a reference that the player reached the exit
            // region. The only one the current telemetry offers is a faithful
            // replay of an original run that itself reached and fired the exit.
            ReplayValidationResult v = context.Validation;
            if (v == null || v.originalOutcome != "Success")
                return null; // cannot establish the exit was reached → SKIP

            string replay = v.replayOutcome;

            if (replay == "Success")
            {
                var ok = new OracleResult
                {
                    passed = true,
                    severity = OracleResult.SeverityInfo,
                    reason = "The completion trigger fired on the reproduced run (original and replay both reached Success)."
                };
                ok.evidence.Add("originalOutcome=Success");
                ok.evidence.Add("replayOutcome=Success");
                return ok;
            }

            // The reproduced run FAILED before the exit → hazard, not a trigger
            // defect (PB-003 lands here and is correctly left to HazardOracle).
            if (replay == "SpikeDeath" || replay == "OutOfBounds" || replay == "Quit")
                return null;

            // The route must have been reproduced faithfully for "reached the
            // exit" to hold; an unfaithful replay proves nothing about the exit.
            if (v.verdict != ReplayValidationResult.VerdictPass)
                return null; // could not confirm the exit region was reached → SKIP

            // A trigger DID fire this run → ambiguous, do not over-claim.
            int triggersFired = context.Features != null ? context.Features.checkpointsReached : 0;
            if (triggersFired > 0) return null;

            // Original fired the exit; faithful replay reached the same route;
            // no completion, no terminal outcome, no trigger fired → missing trigger.
            var result = new OracleResult
            {
                passed = false,
                severity = OracleResult.SeverityCritical,
                reason = "Missing completion trigger: the original run reached and fired the exit and the replay " +
                         "reproduced that run faithfully, yet no completion event fired and the run never ended — " +
                         "the exit is reachable but silently non-functional."
            };
            result.evidence.Add("originalOutcome=Success");
            result.evidence.Add("replayOutcome=" + (string.IsNullOrEmpty(replay) ? "none" : replay));
            result.evidence.Add("triggerFired=false");
            result.evidence.Add("sessionOutcome=" + (string.IsNullOrEmpty(context.RunOutcome) ? "none" : context.RunOutcome));
            result.evidence.Add("validationVerdict=" + (string.IsNullOrEmpty(v.verdict) ? "none" : v.verdict));
            return result;
        }
    }
}
