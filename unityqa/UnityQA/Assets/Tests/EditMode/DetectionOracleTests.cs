// -----------------------------------------------------------------------------
// UnityQA Tests — DetectionOracleTests.cs                   (M6 Slice B tests)
//
// The two planted-defect detectors, judged branch by branch against hand-built
// contexts (pure — no files). SoftLockOracle reads SessionFeatures +
// OracleContext.Trajectory; MissingTriggerOracle reads RunOutcome + the
// replay-regression validation.json fields. These are DETECTION tests
// ("the oracle identifies the defect"), the counterpart to M6-A's GROUND-TRUTH
// tests ("the defect exists") — the two are kept distinct on purpose.
// -----------------------------------------------------------------------------

using NUnit.Framework;
using UnityQA.Features;
using UnityQA.Oracles;
using UnityQA.Replay;

namespace UnityQA.Tests
{
    public sealed class DetectionOracleTests
    {
        // ---- builders -------------------------------------------------------

        private static TrajectorySummary Traj(float minX, float maxX, float minY, float maxY,
                                              float durationSec, int samples = 200)
            => new TrajectorySummary
            {
                available = true,
                sampleCount = samples,
                minX = minX, maxX = maxX, minY = minY, maxY = maxY,
                firstT = 0f, lastT = durationSec
            };

        private static SessionFeatures Feat(float totalDistance, int directionChanges,
                                            int jumpPresses, int deaths = 0,
                                            int checkpointsReached = 0, bool eventsAvailable = true)
            => new SessionFeatures
            {
                eventsAvailable = eventsAvailable,
                totalDistance = totalDistance,
                directionChanges = directionChanges,
                inputJumpPresses = jumpPresses,
                deaths = deaths,
                checkpointsReached = checkpointsReached
            };

        private static OracleContext Ctx(string outcome = null, SessionFeatures f = null,
                                         TrajectorySummary traj = null,
                                         ReplayValidationResult validation = null)
            => new OracleContext
            {
                SessionId = "s1",
                RunOutcome = outcome,
                Features = f,
                Trajectory = traj,
                Validation = validation
            };

        private static ReplayValidationResult Val(string original, string replay,
                                                  string verdict = ReplayValidationResult.VerdictPass)
            => new ReplayValidationResult
            {
                originalOutcome = original,
                replayOutcome = replay,
                verdict = verdict,
                validationSessionId = "v1"
            };

        // ===================================================== SoftLockOracle

        [Test]
        public void SoftLock_GenuineTrap_Fails_Critical()
        {
            // Alive, no outcome, lots of travel + reversals, tiny 3u region, 6s.
            var r = new SoftLockOracle().Evaluate(Ctx(
                outcome: null,
                f: Feat(totalDistance: 40f, directionChanges: 20, jumpPresses: 10),
                traj: Traj(7f, 10f, -2f, 1f, durationSec: 6f)));
            Assert.IsNotNull(r);
            Assert.IsFalse(r.passed, "a trapped-but-trying, alive, outcome-less run is a soft lock");
            Assert.AreEqual(OracleResult.SeverityCritical, r.severity);
            StringAssert.Contains("Soft lock", r.reason);
            CollectionAssert.Contains(r.evidence, "sessionOutcome=none");
            CollectionAssert.Contains(r.evidence, "confined=true");
        }

        [Test]
        public void SoftLock_SuccessfulCompletion_DoesNotReportSoftLock()
        {
            // Terminal outcome present → the run ended → not a soft lock.
            var r = new SoftLockOracle().Evaluate(Ctx(
                outcome: "Success",
                f: Feat(totalDistance: 40f, directionChanges: 8, jumpPresses: 4),
                traj: Traj(2f, 34f, 0f, 3f, durationSec: 6f)));
            Assert.IsNotNull(r);
            Assert.IsTrue(r.passed, "a completed run is not a soft lock");
            CollectionAssert.Contains(r.evidence, "sessionOutcome=Success");
        }

        [Test]
        public void SoftLock_IdleOrNotMoving_DoesNotFalselyReport()
        {
            // Alive, no outcome, but almost no travel and no escape activity —
            // a player choosing not to move is NOT trapped.
            var r = new SoftLockOracle().Evaluate(Ctx(
                outcome: null,
                f: Feat(totalDistance: 1.2f, directionChanges: 0, jumpPresses: 0),
                traj: Traj(2f, 2.4f, 0f, 0.3f, durationSec: 6f)));
            Assert.IsNotNull(r);
            Assert.IsTrue(r.passed, "idle / deliberately-still behaviour must not read as a soft lock");
            Assert.AreEqual(OracleResult.SeverityInfo, r.severity);
        }

        [Test]
        public void SoftLock_NormalTraversal_DoesNotFalselyReport()
        {
            // Lots of travel AND large spatial extent → progress, not a trap.
            var r = new SoftLockOracle().Evaluate(Ctx(
                outcome: null,
                f: Feat(totalDistance: 45f, directionChanges: 6, jumpPresses: 5),
                traj: Traj(2f, 34f, 0f, 3f, durationSec: 6f)));
            Assert.IsTrue(r.passed, "a run that spans the level made progress and is not confined");
            CollectionAssert.Contains(r.evidence, "confined=false");
        }

        [Test]
        public void SoftLock_InsufficientEvidence_Skips()
        {
            var o = new SoftLockOracle();
            Assert.IsNull(o.Evaluate(Ctx(f: Feat(40f, 20, 10), traj: null)),
                "no trajectory → skip");
            Assert.IsNull(o.Evaluate(Ctx(f: Feat(40f, 20, 10),
                traj: Traj(7f, 10f, -2f, 1f, durationSec: 1.0f))),
                "trajectory shorter than the observation window → skip");
            Assert.IsNull(o.Evaluate(Ctx(
                f: Feat(40f, 20, 10, eventsAvailable: false),
                traj: Traj(7f, 10f, -2f, 1f, durationSec: 6f))),
                "no events telemetry → skip");
        }

        // ================================================ MissingTriggerOracle

        [Test]
        public void MissingTrigger_ReachesExit_AndCompletes_Passes()
        {
            var r = new MissingTriggerOracle().Evaluate(Ctx(outcome: "Success"));
            Assert.IsNotNull(r);
            Assert.IsTrue(r.passed);
            CollectionAssert.Contains(r.evidence, "triggerFired=true");
        }

        [Test]
        public void MissingTrigger_ReplayReproducesSuccess_Passes()
        {
            var r = new MissingTriggerOracle().Evaluate(Ctx(
                outcome: null,
                f: Feat(10f, 2, 3, checkpointsReached: 1),
                validation: Val("Success", "Success")));
            Assert.IsTrue(r.passed);
            CollectionAssert.Contains(r.evidence, "replayOutcome=Success");
        }

        [Test]
        public void MissingTrigger_ReachesExit_NoCompletion_Fails_Critical()
        {
            // Original reached+fired the exit; faithful replay reproduced the
            // route; no completion, no terminal outcome, no trigger fired.
            var r = new MissingTriggerOracle().Evaluate(Ctx(
                outcome: null,
                f: Feat(30f, 4, 6, checkpointsReached: 0),
                validation: Val("Success", "", ReplayValidationResult.VerdictPass)));
            Assert.IsNotNull(r);
            Assert.IsFalse(r.passed);
            Assert.AreEqual(OracleResult.SeverityCritical, r.severity);
            StringAssert.Contains("Missing completion trigger", r.reason);
            CollectionAssert.Contains(r.evidence, "triggerFired=false");
            CollectionAssert.Contains(r.evidence, "originalOutcome=Success");
        }

        [Test]
        public void MissingTrigger_NeverReachesExitRegion_Skips()
        {
            var o = new MissingTriggerOracle();
            Assert.IsNull(o.Evaluate(Ctx(outcome: null, f: Feat(5f, 1, 0), validation: null)),
                "no replay reference → cannot establish the exit was reached → skip");
            Assert.IsNull(o.Evaluate(Ctx(outcome: null, f: Feat(5f, 1, 0),
                validation: Val("SpikeDeath", ""))),
                "original never completed → no exit reference → skip");
        }

        [Test]
        public void MissingTrigger_InsufficientEvidence_Skips()
        {
            var o = new MissingTriggerOracle();
            // Reproduced run FAILED before the exit (a hazard, not a trigger
            // defect) — this is the PB-003 discrimination: leave it to Hazard.
            Assert.IsNull(o.Evaluate(Ctx(outcome: "SpikeDeath", f: Feat(20f, 3, 4),
                validation: Val("Success", "SpikeDeath", ReplayValidationResult.VerdictFail))),
                "reproduced run died before the exit → skip (hazard, not trigger)");
            // Replay not faithful → can't confirm the exit region was reached.
            Assert.IsNull(o.Evaluate(Ctx(outcome: null, f: Feat(20f, 3, 4, checkpointsReached: 0),
                validation: Val("Success", "", ReplayValidationResult.VerdictInvalid))),
                "unfaithful/insufficient replay → skip");
        }
    }
}
