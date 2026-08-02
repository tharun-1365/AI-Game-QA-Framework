// -----------------------------------------------------------------------------
// UnityQA — SessionFeatures.cs                                   (M4 Slice A)
//
// PURPOSE
//   The feature vector of one session — the reusable, engine-independent
//   summary that Milestone 5's ML and Milestone 7's reports will consume.
//   Serialized as pretty features.json in the session folder (wire format =
//   field names; schemaVersion'd like every UnityQA document).
//
// FORMULA CONTRACT (frozen with schema v1 — these definitions ARE the
// dataset semantics; changing any of them is a schema change):
//   totalDistance        Σ ‖pos[i] − pos[i−1]‖ over trajectory samples (path length)
//   trajectoryDurationSec  t_last − t_first of the trajectory
//   averageSpeed         totalDistance / trajectoryDurationSec (0 if degenerate)
//   maxSpeed             max ‖(vx,vy)‖ over samples (recorded velocity, not derived)
//   airtimeSec           Σ dt attributed to samples with g == 0 (dt = t[i]−t[i−1])
//   idleTimeSec          Σ dt where g == 1 and sample speed < 0.05 u/s
//   *Fraction            corresponding time / trajectoryDurationSec
//   directionChanges     sign flips between consecutive NON-ZERO horizontal
//                        commands in replay frames (+1…−1 counts once,
//                        zeros between them are transparent)
//   jumpCount            JumpExecuted events (what the game DID)
//   inputJumpPresses     jumpPressed frames in the replay (what the player TRIED)
//                        — the pair is a deliberate cross-check: presses with
//                        no execution are ignored mid-air attempts
//   deaths / checkpointsReached / tokensCollected
//                        PlayerDied / TriggerFired / TokenCollected event counts
//                        (0 until the game under test gains those mechanics)
// -----------------------------------------------------------------------------

using System;

namespace UnityQA.Features
{
    /// <summary>One session's extracted feature vector. Wire format of features.json.</summary>
    [Serializable]
    public sealed class SessionFeatures
    {
        public const int CurrentSchemaVersion = 1;

        public int schemaVersion;

        // --- identity --------------------------------------------------------
        public string sessionId;
        public string sessionFolderName;
        public string level;
        /// <summary>Extraction timestamp. The ONLY non-deterministic field:
        /// identical inputs yield identical feature VALUES (tested).</summary>
        public string extractedUtc;

        // --- session facts (session.json) ------------------------------------
        public float sessionDurationSec;
        public string sessionStatus;

        // --- trajectory features (events.jsonl → PlayerSample) ---------------
        public bool eventsAvailable;
        public int trajectorySamples;
        public int parseErrors;
        public float trajectoryDurationSec;
        public float totalDistance;
        public float averageSpeed;
        public float maxSpeed;
        public float airtimeSec;
        public float airtimeFraction;
        public float idleTimeSec;
        public float idleFraction;

        // --- event counts (events.jsonl) --------------------------------------
        public int jumpCount;
        public int landedCount;
        public int collisionCount;
        public int deaths;
        public int checkpointsReached;
        public int tokensCollected;

        // --- input features (replay.json) -------------------------------------
        public bool replayAvailable;
        public int replayFrameCount;
        public int directionChanges;
        public int inputJumpPresses;

        // --- validation passthrough (validation.json) --------------------------
        public bool validationAvailable;
        public string validationVerdict;
        public float validationMaxDeviation;
    }
}
