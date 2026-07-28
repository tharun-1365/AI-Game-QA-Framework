// -----------------------------------------------------------------------------
// UnityQA — TrajectoryComparer.cs                                (M3 Slice C)
//
// PURPOSE
//   The pure mathematics of Slice C: given two trajectories (original run,
//   replayed run) and a tolerance, produce deviation metrics and a verdict.
//   Static and engine-free — EditMode tests pin every branch, and the
//   IEEE-paper numbers come from exactly this code path.
//
// METHOD
//   1. Time-normalize both trajectories to start at t = 0 (each session's
//      first sample defines its zero — removes session-start jitter).
//   2. For every ORIGINAL sample inside the overlapping time window, linearly
//      interpolate the VALIDATION trajectory at that time (single forward
//      pointer — O(n+m), no allocation, no LINQ) and take the Euclidean
//      position deviation.
//   3. Aggregate max / mean / RMS; record the first threshold crossing.
//   Interpolation (not nearest-sample) matters: the two runs' samplers tick
//   on independent coroutine clocks, so timestamps never line up exactly —
//   comparing nearest samples would charge the replay for sampling phase,
//   not for actual divergence.
//
// INTERPRETATION (documented for the viva/paper)
//   Deviations near zero → replay is faithful on this machine. Growing
//   deviation after firstDivergenceTime → the frame-domain playback
//   limitation flagged in D-011, now MEASURED instead of suspected. That
//   measurement — not a perfect PASS — is Slice C's scientific output.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;

namespace UnityQA.Replay
{
    /// <summary>Pure trajectory comparison → ReplayValidationResult metrics.</summary>
    public static class TrajectoryComparer
    {
        /// <summary>
        /// Compare two trajectories. Identity fields (session IDs, folders,
        /// parseErrors) are the caller's to fill — this method owns only the
        /// mathematics.
        /// </summary>
        public static ReplayValidationResult Compare(List<TrajectorySample> original,
                                                     List<TrajectorySample> validation,
                                                     float thresholdUnits)
        {
            var result = new ReplayValidationResult
            {
                schemaVersion = ReplayValidationResult.CurrentSchemaVersion,
                thresholdUnits = thresholdUnits,
                originalSamples = original?.Count ?? 0,
                validationSamples = validation?.Count ?? 0,
                firstDivergenceTime = -1f,
                verdict = ReplayValidationResult.VerdictInvalid
            };

            // Fewer than 2 points on either side: no trajectory to speak of.
            if (result.originalSamples < 2 || result.validationSamples < 2)
                return result;

            float origT0 = original[0].t;
            float valT0 = validation[0].t;
            result.originalDuration = original[original.Count - 1].t - origT0;
            result.validationDuration = validation[validation.Count - 1].t - valT0;
            result.durationDelta = Mathf.Abs(result.originalDuration - result.validationDuration);

            // Compare over the window both trajectories cover.
            float window = Mathf.Min(result.originalDuration, result.validationDuration);

            float sum = 0f, sumSq = 0f, max = 0f;
            int compared = 0;
            int cursor = 0; // forward pointer into validation — never rewinds

            for (int i = 0; i < original.Count; i++)
            {
                float t = original[i].t - origT0;
                if (t > window) break;

                Vector2 replayed = EvaluateAt(validation, valT0 + t, ref cursor);
                float dx = original[i].x - replayed.x;
                float dy = original[i].y - replayed.y;
                float deviation = Mathf.Sqrt(dx * dx + dy * dy);

                compared++;
                sum += deviation;
                sumSq += deviation * deviation;
                if (deviation > max) max = deviation;
                if (deviation > thresholdUnits && result.firstDivergenceTime < 0f)
                    result.firstDivergenceTime = t;
            }

            result.comparedSamples = compared;
            if (compared == 0) return result; // verdict stays INVALID

            result.maxDeviation = max;
            result.meanDeviation = sum / compared;
            result.rmsDeviation = Mathf.Sqrt(sumSq / compared);
            result.verdict = max <= thresholdUnits
                ? ReplayValidationResult.VerdictPass
                : ReplayValidationResult.VerdictFail;
            return result;
        }

        /// <summary>
        /// Linear interpolation of a trajectory at time t, advancing a caller
        /// -owned cursor (callers iterate in increasing t, so the scan is a
        /// single forward pass overall). Times outside the range clamp to the
        /// end samples.
        /// </summary>
        private static Vector2 EvaluateAt(List<TrajectorySample> samples, float t, ref int cursor)
        {
            while (cursor < samples.Count - 2 && samples[cursor + 1].t < t)
                cursor++;

            TrajectorySample a = samples[cursor];
            TrajectorySample b = samples[Mathf.Min(cursor + 1, samples.Count - 1)];

            if (t <= a.t || b.t <= a.t) return new Vector2(a.x, a.y);
            if (t >= b.t) return new Vector2(b.x, b.y);

            float f = (t - a.t) / (b.t - a.t);
            return new Vector2(a.x + (b.x - a.x) * f, a.y + (b.y - a.y) * f);
        }
    }
}
