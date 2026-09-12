// -----------------------------------------------------------------------------
// UnityQA — HazardOracle.cs                                      (M5 Slice D)
//
// PURPOSE
//   "If the run failed, WHY?" — hazard attribution from the recorded outcome.
//
// SEVERITY RATIONALE (documented so reports inherit a defensible scale)
//   SpikeDeath   → FAIL, WARNING : defeated by a DESIGNED hazard — expected
//                                  gameplay, reportable, not alarming.
//   OutOfBounds  → FAIL, CRITICAL: the player left the playable space — the
//                                  classic falls-out-of-world defect class,
//                                  exactly what a QA framework exists to flag.
//   Quit         → SKIP          : a manually ended run attributes nothing.
//   no outcome   → SKIP          : nothing recorded, nothing to attribute.
// -----------------------------------------------------------------------------

namespace UnityQA.Oracles
{
    /// <summary>Failure-cause attribution oracle over the recorded SessionOutcome.</summary>
    public sealed class HazardOracle : IQualityOracle
    {
        public string Name => "Hazard";
        public string Description => "Attributes a failed run to its hazard: spike contact or out-of-bounds fall.";
        public bool Enabled { get; set; } = true;

        public OracleResult Evaluate(OracleContext context)
        {
            string outcome = context.RunOutcome;
            if (string.IsNullOrEmpty(outcome) || outcome == "Quit") return null; // nothing to attribute

            OracleResult result;
            switch (outcome)
            {
                case "Success":
                    result = new OracleResult
                    {
                        passed = true,
                        severity = OracleResult.SeverityInfo,
                        reason = "The run ended without touching any hazard."
                    };
                    result.evidence.Add("hazardType=none");
                    break;

                case "SpikeDeath":
                    result = new OracleResult
                    {
                        passed = false,
                        severity = OracleResult.SeverityWarning,
                        reason = "Spike Hazard: the run ended on spike contact."
                    };
                    result.evidence.Add("hazardType=spike");
                    break;

                case "OutOfBounds":
                    result = new OracleResult
                    {
                        passed = false,
                        severity = OracleResult.SeverityCritical,
                        reason = "Out Of Bounds: the player left the playable space."
                    };
                    result.evidence.Add("hazardType=outOfBounds");
                    break;

                default:
                    // Unknown future outcome name: skip rather than guess —
                    // the framework's not-applicable path exists for exactly this.
                    return null;
            }

            result.evidence.Add("sessionOutcome=" + outcome);
            return result;
        }
    }
}
