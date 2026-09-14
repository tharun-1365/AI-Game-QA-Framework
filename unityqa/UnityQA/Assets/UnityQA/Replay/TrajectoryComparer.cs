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
//   1b. (M6.D) Bounded constant-offset alignment: the deterministic fixed-step
//      replay can play back a small CONSTANT phase ahead/behind the original
//      (frame-domain playback, D-011). Search a bounded set of constant time
//      offsets and keep the one that minimises MEAN deviation — MEAN, not MAX,
//      so a genuine localized spatial divergence cannot be balanced away by a
//      mis-alignment (a real divergence raises the mean at every offset). Only
//      one constant shift is allowed (no time-warping); the raw timing
//      discrepancy stays in durationDelta and the applied shift is reported in
//      alignmentOffsetSec.
//   2. For every ORIGINAL sample inside the overlapping time window, linearly
//      interpolate the VALIDATION trajectory at that (offset) time (single
//      forward pointer — O(n+m), no allocation, no LINQ) and take the Euclidean
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
        /// <summary>M6.D bounded constant-offset alignment — the largest phase
        /// offset (seconds, either direction) the search will compensate. The
        /// bound is what keeps the alignment honest: it can NEVER absorb a real
        /// divergence as a huge time-warp. ±0.5 s comfortably covers the observed
        /// frame-domain playback lead (~0.3 s) while staying far below any real
        /// route timing.</summary>
        public const float MaxAlignmentSec = 0.5f;

        /// <summary>Search resolution — the project's physics Fixed Timestep
        /// (0.02 s), the granularity at which the deterministic fixed-step
        /// simulation can actually differ.</summary>
        public const float AlignmentStepSec = 0.02f;

        /// <summary>Mean-deviation tie tolerance: within this, prefer the SMALLEST
        /// |offset| (0 = the pre-M6.D behaviour), so an offset is applied only
        /// when it genuinely improves the fit.</summary>
        private const float AlignmentEpsilon = 1e-4f;

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

            // --- M6.D: bounded constant-offset alignment (minimise MEAN) -------
            // Pick the single constant offset in [-Max, +Max] (step = fixed
            // timestep) that minimises MEAN deviation; ties resolved toward the
            // smallest |offset| so offset 0 (the old behaviour) wins unless a
            // shift strictly improves the whole-trajectory fit.
            int steps = Mathf.RoundToInt(MaxAlignmentSec / AlignmentStepSec);
            float bestOffset = 0f, bestMean = float.MaxValue, bestAbs = float.MaxValue;
            for (int k = -steps; k <= steps; k++)
            {
                float offset = k * AlignmentStepSec;
                MeasureAtOffset(original, validation, origT0, valT0, window, offset, thresholdUnits,
                                out _, out float mean, out _, out _, out int n);
                if (n == 0) continue;
                float absOff = Mathf.Abs(offset);
                if (mean < bestMean - AlignmentEpsilon ||
                    (mean <= bestMean + AlignmentEpsilon && absOff < bestAbs))
                {
                    bestMean = mean;
                    bestOffset = offset;
                    bestAbs = absOff;
                }
            }

            // --- final reported metrics, measured at the chosen offset ---------
            MeasureAtOffset(original, validation, origT0, valT0, window, bestOffset, thresholdUnits,
                            out float max, out float mean2, out float rms, out float firstDiv,
                            out int compared);

            result.comparedSamples = compared;
            if (compared == 0) return result; // verdict stays INVALID

            result.alignmentOffsetSec = bestOffset;
            result.maxDeviation = max;
            result.meanDeviation = mean2;
            result.rmsDeviation = rms;
            result.firstDivergenceTime = firstDiv;
            result.verdict = max <= thresholdUnits
                ? ReplayValidationResult.VerdictPass
                : ReplayValidationResult.VerdictFail;
            return result;
        }

        /// <summary>
        /// Deviation metrics of the original against the validation trajectory
        /// shifted by a CONSTANT <paramref name="offset"/> seconds, over the
        /// shared window. Pure; a fresh forward cursor per call (the query times
        /// t + offset are monotonic for a fixed offset, so the single-pass scan
        /// still holds). Used both to search the offset and to compute the final
        /// reported metrics, so search and result can never disagree.
        /// </summary>
        private static void MeasureAtOffset(List<TrajectorySample> original,
                                            List<TrajectorySample> validation,
                                            float origT0, float valT0, float window,
                                            float offset, float thresholdUnits,
                                            out float max, out float mean, out float rms,
                                            out float firstDivergence, out int compared)
        {
            float sum = 0f, sumSq = 0f;
            max = 0f;
            firstDivergence = -1f;
            compared = 0;
            int cursor = 0; // forward pointer into validation — never rewinds

            for (int i = 0; i < original.Count; i++)
            {
                float t = original[i].t - origT0;
                if (t > window) break;

                Vector2 replayed = EvaluateAt(validation, valT0 + t + offset, ref cursor);
                float dx = original[i].x - replayed.x;
                float dy = original[i].y - replayed.y;
                float deviation = Mathf.Sqrt(dx * dx + dy * dy);

                compared++;
                sum += deviation;
                sumSq += deviation * deviation;
                if (deviation > max) max = deviation;
                if (deviation > thresholdUnits && firstDivergence < 0f) firstDivergence = t;
            }

            mean = compared > 0 ? sum / compared : 0f;
            rms = compared > 0 ? Mathf.Sqrt(sumSq / compared) : 0f;
        }

        /// <summary>
        /// Fold the recorded run outcomes into a comparison result (M5.D
        /// stabilization; pure, EditMode-testable). Outcomes are comparable
        /// only when BOTH sessions recorded one and the original is not a
        /// manual Quit (Escape is not part of the input seam, so a quit can
        /// never be reproduced by playback — not comparing is honest, failing
        /// would be wrong). A comparable MISMATCH downgrades a trajectory
        /// PASS to FAIL: same path within tolerance but a different ending is
        /// not a reproduced run. Missing outcomes never change the verdict —
        /// absence of evidence is not evidence of failure (M5.D oracle rule).
        /// </summary>
        public static void ApplyOutcomes(ReplayValidationResult result,
                                         string originalOutcome, string replayOutcome)
        {
            result.originalOutcome = originalOutcome ?? "";
            result.replayOutcome = replayOutcome ?? "";
            result.outcomesCompared = result.originalOutcome.Length > 0
                                   && result.replayOutcome.Length > 0
                                   && result.originalOutcome != "Quit";
            result.outcomeMatch = result.outcomesCompared
                               && result.originalOutcome == result.replayOutcome;

            if (result.outcomesCompared && !result.outcomeMatch
                && result.verdict == ReplayValidationResult.VerdictPass)
                result.verdict = ReplayValidationResult.VerdictFail;
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
