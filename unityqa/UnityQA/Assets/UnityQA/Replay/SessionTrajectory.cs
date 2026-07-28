// -----------------------------------------------------------------------------
// UnityQA — SessionTrajectory.cs                                 (M3 Slice C)
//
// PURPOSE
//   Reads a session's movement trajectory back OUT of its events.jsonl — the
//   PlayerSample lines the telemetry sampler wrote in M2. Slice C validates
//   replays by comparing trajectories, and the canonical trajectory record
//   already exists on disk for every session ever run — so we read it rather
//   than inventing a second recording system (no-duplicate-systems rule).
//
// PARSING APPROACH (and why it is safe without a JSON library)
//   We parse ONLY lines we ourselves wrote: JsonLineWriter emits a fixed key
//   order, invariant culture, one object per line. Extraction is therefore
//   substring-anchored ("\"t\":", "\"x\":", "\"y\":") — note the leading
//   quote in each anchor, which is what makes payload keys like "vx"/"vy"
//   unmatchable. Any line that fails to parse is COUNTED and skipped, never
//   thrown on: a trajectory reader that crashes on a truncated crash-log
//   would be broken in exactly the situation it exists for.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace UnityQA.Replay
{
    /// <summary>One trajectory point: session time + player position.</summary>
    public struct TrajectorySample
    {
        public float t;
        public float x;
        public float y;
    }

    /// <summary>A session's movement trajectory, loaded from its events.jsonl.</summary>
    public sealed class SessionTrajectory
    {
        /// <summary>Session UUID from the stream's header line (A1/A2); null if absent.</summary>
        public string SessionId { get; private set; }

        public readonly List<TrajectorySample> Samples = new List<TrajectorySample>();

        /// <summary>Lines that looked like samples but failed extraction — a
        /// nonzero value means the file is damaged or the schema drifted.</summary>
        public int ParseErrors { get; private set; }

        private const string SampleMarker = "\"type\":\"PlayerSample\"";
        private const string HeaderMarker = "\"header\":1";

        /// <summary>Load a trajectory. Returns null (one error log) if the file
        /// is missing; an EMPTY trajectory is returned as an object so callers
        /// can distinguish "no file" from "file with no samples".</summary>
        public static SessionTrajectory Load(string eventsJsonlPath)
        {
            if (string.IsNullOrEmpty(eventsJsonlPath) || !File.Exists(eventsJsonlPath))
            {
                Debug.LogError($"[UnityQA] Trajectory load failed — file not found: '{eventsJsonlPath}'");
                return null;
            }

            var trajectory = new SessionTrajectory();
            foreach (string line in File.ReadLines(eventsJsonlPath))
            {
                if (line.Contains(HeaderMarker))
                {
                    trajectory.SessionId = ExtractString(line, "\"sessionId\":\"");
                    continue;
                }

                if (!line.Contains(SampleMarker)) continue;

                // Envelope "t" and the pos block's "x"/"y". Quoted anchors make
                // payload's vx/vy/mx unmatchable (their preceding char is a letter).
                if (TryExtractFloat(line, "\"t\":", out float t) &&
                    TryExtractFloat(line, "\"x\":", out float x) &&
                    TryExtractFloat(line, "\"y\":", out float y))
                {
                    trajectory.Samples.Add(new TrajectorySample { t = t, x = x, y = y });
                }
                else
                {
                    trajectory.ParseErrors++;
                }
            }
            return trajectory;
        }

        private static bool TryExtractFloat(string line, string anchor, out float value)
        {
            value = 0f;
            int idx = line.IndexOf(anchor, System.StringComparison.Ordinal);
            if (idx < 0) return false;
            int start = idx + anchor.Length;
            int end = start;
            while (end < line.Length && (char.IsDigit(line[end]) || line[end] == '-' ||
                                         line[end] == '+' || line[end] == '.' ||
                                         line[end] == 'e' || line[end] == 'E'))
                end++;
            return end > start &&
                   float.TryParse(line.Substring(start, end - start),
                                  NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static string ExtractString(string line, string anchor)
        {
            int idx = line.IndexOf(anchor, System.StringComparison.Ordinal);
            if (idx < 0) return null;
            int start = idx + anchor.Length;
            int end = line.IndexOf('"', start);
            return end > start ? line.Substring(start, end - start) : null;
        }
    }
}
