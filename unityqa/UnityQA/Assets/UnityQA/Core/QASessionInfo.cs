// -----------------------------------------------------------------------------
// UnityQA — QASessionInfo.cs                          (EVENT-SCHEMA.md §0, §2)
//
// PURPOSE
//   Identity, metadata, and stamping state for exactly one QA session:
//   who am I (UUID + folder name), where/when did I start, and the two
//   counters every record needs (sequence number, session clock).
//
// WHY STAMPING LIVES HERE
//   Every event needs sid/seq/t/frame. If each publisher stamped its own,
//   sequence gaps and clock skew would be one refactor away. Centralizing the
//   counters in the session object (used only via QARunner.Emit) makes
//   NFR-1.5 — strictly increasing, gap-free seq — true by construction.
//
// AMENDMENT A2 (approved 26 Jul 2026)
//   SessionId is a GUID — the canonical machine identifier, referenced by
//   every output stream. FolderName stays human-sortable
//   (yyyyMMdd-HHmmss_<uuid[0:8]>); session.json binds the two.
//
// Plain C# class (no MonoBehaviour) — unit-testable without a scene.
// -----------------------------------------------------------------------------

using System;

namespace UnityQA.Core
{
    /// <summary>Identity + metadata + stamping counters for one session.</summary>
    public sealed class QASessionInfo
    {
        /// <summary>Frozen with EVENT-SCHEMA.md v1 (amendment A1).</summary>
        public const int SchemaVersion = 1;

        /// <summary>Canonical session identifier — GUID string (amendment A2).</summary>
        public readonly string SessionId;

        /// <summary>Human-sortable folder name: yyyyMMdd-HHmmss_&lt;uuid[0:8]&gt;.</summary>
        public readonly string FolderName;

        /// <summary>Scene name under test.</summary>
        public readonly string Level;

        /// <summary>ISO-8601 UTC start timestamp.</summary>
        public readonly string StartedUtc;

        public readonly string UnityVersion;
        public readonly string AppVersion;

        private long nextSeq;
        private readonly float startTime;
        private readonly Func<float> clock;

        /// <param name="level">Active scene name.</param>
        /// <param name="clock">
        /// Time source returning seconds (production: () => Time.time).
        /// Injected so EditMode tests can control time without an engine loop.
        /// </param>
        /// <param name="utcNow">Start instant (production: DateTime.UtcNow).</param>
        public QASessionInfo(string level, Func<float> clock, DateTime utcNow,
                             string unityVersion, string appVersion)
        {
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            SessionId = Guid.NewGuid().ToString();
            FolderName = $"{utcNow:yyyyMMdd-HHmmss}_{SessionId.Substring(0, 8)}";
            Level = level;
            StartedUtc = utcNow.ToString("o");
            UnityVersion = unityVersion;
            AppVersion = appVersion;
            startTime = clock();
            nextSeq = 0;
        }

        /// <summary>Seconds since session start.</summary>
        public float SessionTime => clock() - startTime;

        /// <summary>Hands out 0, 1, 2, … — one caller (QARunner.Emit) only.</summary>
        public long NextSeq() => nextSeq++;

        /// <summary>Events stamped so far (= next seq value).</summary>
        public long EventCount => nextSeq;
    }
}
