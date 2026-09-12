// -----------------------------------------------------------------------------
// UnityQA — SoftLockOracle.cs                                    (M6 Slice B)
//
// PURPOSE
//   "Did the player get permanently trapped?" — detects the soft-lock defect
//   class (PB-002) from observable run data alone, with no benchmark
//   coordinates and no knowledge of the level's geometry.
//
// DECISION RULE (general gameplay-state criterion, documented so reports and
// the paper inherit a defensible definition)
//   A run is a SOFT LOCK when ALL of the following hold:
//     (a) NO terminal outcome was recorded  — a soft lock never ends (no
//         RunEnded: Success/SpikeDeath/OutOfBounds/Quit), and the player is
//         still alive (no death);
//     (b) the player KEPT TRYING to move    — meaningful escape activity:
//         enough direction reversals OR jump presses, AND a real amount of
//         travelled path (rules out "idle" / "chose not to move", which show
//         little input and little path);
//     (c) yet stayed CONFINED               — the whole trajectory fits inside
//         a small spatial region (bounding-box extent ≤ threshold), and the
//         travelled path is much longer than that extent (pacing/backtracking
//         inside a trap, not progress across the level);
//     (d) for a SUSTAINED window            — the trajectory spans at least the
//         observation window, so a brief pause cannot trip it.
//
//   This separates the four cases the spec calls out:
//     • legitimate waiting / a deliberate pause → fails (b): little activity;
//     • a player simply not moving              → fails (b): little path;
//     • normal traversal of the level          → fails (c): large extent;
//     • an actual trapped state                → all of (a)-(d) hold → FAIL.
//
//   Terminal-outcome or dead runs are NOT soft locks → PASS (info). Sessions
//   without enough trajectory to judge → SKIP (null), never a false FAIL.
//
// EVIDENCE SOURCE
//   Path length, direction changes and jump presses come from the existing
//   SessionFeatures; spatial extent comes from OracleContext.Trajectory (the
//   factory's bounding-box summary of the same PlayerSample telemetry). No new
//   event, file, or schema field — pure analysis of recorded evidence, so
//   Evaluate stays deterministic and side-effect-free per the framework.
//
// SEVERITY
//   FAIL → CRITICAL: a soft lock makes the level unwinnable and strands the
//   player — the same "defect QA exists to flag" tier as OutOfBounds.
// -----------------------------------------------------------------------------

using System.Globalization;
using UnityEngine;
using UnityQA.Features;

namespace UnityQA.Oracles
{
    /// <summary>Soft-lock detector over recorded movement + input evidence.</summary>
    public sealed class SoftLockOracle : IQualityOracle
    {
        // --- decision thresholds (documented constants, not magic numbers) ---
        /// <summary>Confinement must persist at least this long to count.</summary>
        public const float ObservationWindowSec = 3f;
        /// <summary>Minimum travelled path (u) that proves the player was
        /// actually moving — below this the run is idle, not trapped.</summary>
        public const float MinPathLength = 8f;
        /// <summary>Maximum bounding-box extent (u, larger of X/Y span) that
        /// still counts as "a small region the player could not leave".</summary>
        public const float MaxConfinementExtent = 6f;
        /// <summary>Travelled path must exceed extent by at least this factor —
        /// lots of motion inside a small box = pacing in a trap.</summary>
        public const float MinConfinementRatio = 2f;
        /// <summary>Escape activity: at least this many horizontal reversals…</summary>
        public const int MinDirectionChanges = 3;
        /// <summary>…OR at least this many jump attempts.</summary>
        public const int MinJumpPresses = 3;
        /// <summary>Below this sample count the trajectory is too thin to judge.</summary>
        public const int MinSamples = 10;

        public string Name => "SoftLock";
        public string Description =>
            "The run did not end in a soft lock — an alive, outcome-less run that kept trying to move yet never left a small region.";
        public bool Enabled { get; set; } = true;

        public OracleResult Evaluate(OracleContext context)
        {
            SessionFeatures f = context.Features;
            TrajectorySummary traj = context.Trajectory;

            // (SKIP) Not enough evidence to judge confinement over a window.
            if (f == null || !f.eventsAvailable ||
                traj == null || !traj.available || traj.sampleCount < MinSamples ||
                traj.DurationSec < ObservationWindowSec)
                return null;

            // (a) A recorded terminal outcome, or a death, means the run ENDED —
            // a soft lock by definition has neither. Not trapped → PASS.
            string outcome = context.RunOutcome;
            if (!string.IsNullOrEmpty(outcome) || f.deaths > 0)
            {
                var ended = new OracleResult
                {
                    passed = true,
                    severity = OracleResult.SeverityInfo,
                    reason = "Not a soft lock: the run reached a terminal outcome or the player died."
                };
                ended.evidence.Add("sessionOutcome=" + (string.IsNullOrEmpty(outcome) ? "none" : outcome));
                ended.evidence.Add("deaths=" + f.deaths);
                return ended;
            }

            // (b)(c)(d) alive + outcome-less: is it trapped-but-trying, confined?
            float extent = Mathf.Max(traj.SpanX, traj.SpanY);
            bool activeAttempts = f.directionChanges >= MinDirectionChanges ||
                                  f.inputJumpPresses >= MinJumpPresses;
            bool movedRealPath = f.totalDistance >= MinPathLength;
            bool confined = extent <= MaxConfinementExtent;
            bool ratioHigh = extent > 0.001f
                ? (f.totalDistance / extent) >= MinConfinementRatio
                : movedRealPath; // degenerate zero-extent: rely on path length

            bool softLocked = activeAttempts && movedRealPath && confined && ratioHigh;

            var result = new OracleResult
            {
                passed = !softLocked,
                severity = softLocked ? OracleResult.SeverityCritical : OracleResult.SeverityInfo,
                reason = softLocked
                    ? $"Soft lock: the player stayed alive with no outcome, kept moving " +
                      $"({f.totalDistance:F1}u of travel, {f.directionChanges} direction changes, " +
                      $"{f.inputJumpPresses} jump presses) yet never left a {extent:F1}u region " +
                      $"over {traj.DurationSec:F1}s."
                    : "No soft lock: the run made spatial progress or showed no sustained trapped-but-trying pattern."
            };
            result.evidence.Add("sessionOutcome=none");
            result.evidence.Add("alive=true");
            result.evidence.Add("spanX=" + traj.SpanX.ToString("0.###", CultureInfo.InvariantCulture));
            result.evidence.Add("spanY=" + traj.SpanY.ToString("0.###", CultureInfo.InvariantCulture));
            result.evidence.Add("extent=" + extent.ToString("0.###", CultureInfo.InvariantCulture));
            result.evidence.Add("pathLength=" + f.totalDistance.ToString("0.###", CultureInfo.InvariantCulture));
            result.evidence.Add("directionChanges=" + f.directionChanges);
            result.evidence.Add("jumpPresses=" + f.inputJumpPresses);
            result.evidence.Add("observedSec=" + traj.DurationSec.ToString("0.###", CultureInfo.InvariantCulture));
            result.evidence.Add("confined=" + (confined ? "true" : "false"));
            return result;
        }
    }
}
