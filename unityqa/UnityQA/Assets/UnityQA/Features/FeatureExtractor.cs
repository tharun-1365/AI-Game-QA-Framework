// -----------------------------------------------------------------------------
// UnityQA — FeatureExtractor.cs                                  (M4 Slice A)
//
// PURPOSE
//   Transforms one recorded session folder into a SessionFeatures vector.
//   Pure file → value computation: static, no scene, no coroutines, no engine
//   state — runnable in Edit Mode, PlayMode, or (later) batch over the whole
//   catalog. DETERMINISTIC: identical inputs produce identical feature values
//   (extractedUtc is the sole timestamp field, excluded from that claim and
//   from the determinism test).
//
// INPUTS (one addition to the brief, justified)
//   session.json, replay.json, validation.json — as specified — PLUS
//   events.jsonl: distance, speed, airtime and idle time are POSITIONAL
//   facts, and the session's positional record is its PlayerSample telemetry.
//   replay.json holds inputs, not positions; without events.jsonl those
//   features cannot exist. Every input is OPTIONAL: a missing file zeroes its
//   feature group and flags availability — extraction never throws over an
//   incomplete folder (crash-session folders are first-class citizens here).
//
// BOUNDARY (Slice A discipline)
//   Extraction ONLY. No anomaly detection, no scoring, no clustering, no
//   classification, no reporting. The moment a number becomes a judgment,
//   it belongs to M5/M6/M7 — not this file.
// -----------------------------------------------------------------------------

using System;
using System.IO;
using UnityEngine;
using UnityQA.Logging;
using UnityQA.Replay;

namespace UnityQA.Features
{
    /// <summary>Deterministic session-folder → feature-vector extraction.</summary>
    public static class FeatureExtractor
    {
        /// <summary>Grounded samples slower than this count as idle (u/s).</summary>
        public const float IdleSpeedEpsilon = 0.05f;

        /// <summary>
        /// Extract features from a session folder. Never throws; missing
        /// inputs zero their group and clear the matching *Available flag.
        /// Returns null only if the folder itself is missing.
        /// </summary>
        public static SessionFeatures Extract(string sessionFolder)
        {
            if (string.IsNullOrEmpty(sessionFolder) || !Directory.Exists(sessionFolder))
            {
                Debug.LogError($"[UnityQA] Feature extraction failed — folder not found: '{sessionFolder}'");
                return null;
            }

            var f = new SessionFeatures
            {
                schemaVersion = SessionFeatures.CurrentSchemaVersion,
                sessionFolderName = Path.GetFileName(sessionFolder),
                extractedUtc = DateTime.UtcNow.ToString("o")
            };

            ReadManifest(sessionFolder, f);
            ReadTrajectoryAndEvents(sessionFolder, f);
            ReadReplay(sessionFolder, f);
            ReadValidation(sessionFolder, f);
            return f;
        }

        // ------------------------------------------------------- session.json

        private static void ReadManifest(string folder, SessionFeatures f)
        {
            string path = Path.Combine(folder, SessionManifest.FileName);
            if (!File.Exists(path)) return;
            try
            {
                var m = JsonUtility.FromJson<SessionManifest.Manifest>(File.ReadAllText(path));
                if (m == null) return;
                f.sessionId = m.sessionId;
                f.level = m.level;
                f.sessionDurationSec = m.durationSec;
                f.sessionStatus = m.status;
            }
            catch (Exception) { /* damaged manifest: identity stays empty, features still extract */ }
        }

        // ------------------------------------- events.jsonl (trajectory + counts)

        private static void ReadTrajectoryAndEvents(string folder, SessionFeatures f)
        {
            string path = Path.Combine(folder, "events.jsonl");
            if (!File.Exists(path)) return;
            f.eventsAvailable = true;

            // Event counts: one pass of anchored marker counting — the same
            // producer/consumer pairing as SessionTrajectory (we count only
            // lines our own writer emitted).
            foreach (string line in File.ReadLines(path))
            {
                if (line.Contains("\"type\":\"JumpExecuted\"")) f.jumpCount++;
                else if (line.Contains("\"type\":\"Landed\"")) f.landedCount++;
                else if (line.Contains("\"type\":\"CollisionEnter\"")) f.collisionCount++;
                else if (line.Contains("\"type\":\"PlayerDied\"")) f.deaths++;
                else if (line.Contains("\"type\":\"TriggerFired\"")) f.checkpointsReached++;
                else if (line.Contains("\"type\":\"TokenCollected\"")) f.tokensCollected++;
            }

            // Trajectory features via the existing reader (M4.A extension gave
            // it vx/vy/g capture).
            SessionTrajectory trajectory = SessionTrajectory.Load(path);
            if (trajectory == null) return;
            f.parseErrors = trajectory.ParseErrors;
            f.trajectorySamples = trajectory.Samples.Count;
            if (trajectory.Samples.Count < 2) return;

            var samples = trajectory.Samples;
            f.trajectoryDurationSec = samples[samples.Count - 1].t - samples[0].t;

            float distance = 0f, maxSpeed = 0f, airtime = 0f, idle = 0f;
            for (int i = 0; i < samples.Count; i++)
            {
                TrajectorySample s = samples[i];
                float speed = Mathf.Sqrt(s.vx * s.vx + s.vy * s.vy);
                if (speed > maxSpeed) maxSpeed = speed;

                if (i == 0) continue;
                TrajectorySample prev = samples[i - 1];
                float dx = s.x - prev.x, dy = s.y - prev.y;
                distance += Mathf.Sqrt(dx * dx + dy * dy);

                // Interval attribution: dt belongs to the state observed at its
                // END sample (formula contract in SessionFeatures header).
                float dt = s.t - prev.t;
                if (dt <= 0f) continue;
                if (s.g == 0) airtime += dt;
                else if (speed < IdleSpeedEpsilon) idle += dt;
            }

            f.totalDistance = distance;
            f.maxSpeed = maxSpeed;
            f.airtimeSec = airtime;
            f.idleTimeSec = idle;
            if (f.trajectoryDurationSec > 0f)
            {
                f.averageSpeed = distance / f.trajectoryDurationSec;
                f.airtimeFraction = airtime / f.trajectoryDurationSec;
                f.idleFraction = idle / f.trajectoryDurationSec;
            }
        }

        // -------------------------------------------------------- replay.json

        private static void ReadReplay(string folder, SessionFeatures f)
        {
            string path = Path.Combine(folder, ReplayFileStore.FileName);
            if (!File.Exists(path)) return; // optional input: no error spam for old folders

            ReplayRecording replay = ReplayFileStore.Load(path);
            if (replay == null) return;

            f.replayAvailable = true;
            f.replayFrameCount = replay.frameCount;

            int lastSign = 0;
            for (int i = 0; i < replay.frames.Length; i++)
            {
                ReplayFrame frame = replay.frames[i];
                if (frame.jumpPressed) f.inputJumpPresses++;

                // Direction changes: zeros are transparent; only a genuine
                // opposite-sign command counts (formula contract).
                int sign = frame.horizontal > 0.5f ? 1 : (frame.horizontal < -0.5f ? -1 : 0);
                if (sign != 0)
                {
                    if (lastSign != 0 && sign != lastSign) f.directionChanges++;
                    lastSign = sign;
                }
            }
        }

        // ---------------------------------------------------- validation.json

        private static void ReadValidation(string folder, SessionFeatures f)
        {
            string path = Path.Combine(folder, "validation.json");
            if (!File.Exists(path)) return;
            try
            {
                var v = JsonUtility.FromJson<ReplayValidationResult>(File.ReadAllText(path));
                if (v == null || string.IsNullOrEmpty(v.verdict)) return;
                f.validationAvailable = true;
                f.validationVerdict = v.verdict;
                f.validationMaxDeviation = v.maxDeviation;
            }
            catch (Exception) { /* damaged validation file: passthrough group stays empty */ }
        }
    }
}
