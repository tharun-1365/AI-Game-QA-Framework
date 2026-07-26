// -----------------------------------------------------------------------------
// UnityQA — SessionManifest.cs                    (EVENT-SCHEMA.md §2, D-009)
//
// PURPOSE
//   Writes session.json — the session's self-description. Written twice by
//   design: at open with status:"open", rewritten at clean close with
//   status:"closed" + duration + counts. A file still reading "open" after
//   the fact is the crash marker later modules rely on.
//
// DECISION D-009 (recorded in MODULES.md)
//   The design named Newtonsoft for this file; implementation uses Unity's
//   built-in JsonUtility instead. Rationale: session.json is a FIXED-SHAPE
//   document, which is exactly what JsonUtility handles — and this removes a
//   package dependency from the project entirely (NFR-1.8 strengthened).
//   Newtonsoft returns if and when a truly dynamic document appears (none is
//   on the roadmap). The DTOs below ARE the schema §2 shape; field names are
//   serialized verbatim, so renaming a field here is a schema change — don't.
//
// SLICE NOTE
//   gutSpec ships as zeros in Slice B: reading the real values requires the
//   adapter (Slice C), and UnityQA cannot reference BenchGame directly
//   (NFR-1.3). gutSpecSource says so honestly rather than letting zeros
//   masquerade as measurements.
// -----------------------------------------------------------------------------

using System;
using System.IO;
using UnityEngine;
using UnityQA.Core;

namespace UnityQA.Logging
{
    /// <summary>Builds and writes session.json (schema §2).</summary>
    public static class SessionManifest
    {
        public const string FileName = "session.json";

        // ---- Schema §2 as serializable DTOs (field names = JSON names) ----

        [Serializable]
        public sealed class Manifest
        {
            public int schemaVersion;
            public string sessionId;
            public string folderName;
            public string level;
            public string startedUtc;
            public string unityVersion;
            public string appVersion;
            public ConfigSnapshot configSnapshot;
            public GutSpec gutSpec;
            public string gutSpecSource; // "pending-slice-c" until the adapter fills it
            public string status;        // "open" → "closed"
            public float durationSec;    // valid when closed
            public Counts counts;        // valid when closed
        }

        [Serializable]
        public sealed class ConfigSnapshot
        {
            public string startStopKey;
            public string overlayKey;
            public bool consoleEvents;
            public int telemetryHz;
            public int inputKeyframeEverySteps;
            public int flushEveryNEvents;
            public float denseFlushSeconds;
        }

        [Serializable]
        public sealed class GutSpec
        {
            public float runSpeed;
            public float jumpHeight;
            public float gravityScale;
        }

        [Serializable]
        public sealed class Counts
        {
            public long events;
            public long telemetry; // 0 until Slice C
            public long inputs;    // 0 until Slice C
        }

        // ------------------------------------------------------------- writes

        /// <summary>Write the opening manifest (status:"open").</summary>
        public static void WriteOpen(QASessionInfo session, QAConfig config, string sessionFolder)
            => WriteOpen(session, config, sessionFolder, null);

        /// <summary>Slice C overload: same, with real GUT constants when an
        /// adapter can supply them. The parameterless overload above is kept
        /// verbatim so frozen Slice B callers/tests are untouched.</summary>
        public static void WriteOpen(QASessionInfo session, QAConfig config, string sessionFolder,
                                     GutSpecData? gutSpec)
        {
            var m = Build(session, config, gutSpec);
            m.status = "open";
            WriteFile(sessionFolder, m);
        }

        /// <summary>Rewrite as closed with final numbers. Whole-file rewrite is
        /// deliberate: session.json is tiny and atomic-enough via write-temp+move
        /// would be over-engineering here (Rule 8) — a crash between open and
        /// close is exactly what the "open" status is FOR.</summary>
        public static void WriteClosed(QASessionInfo session, QAConfig config, string sessionFolder,
                                       float durationSec, long eventCount)
            => WriteClosed(session, config, sessionFolder, durationSec, eventCount, null);

        /// <summary>Slice C overload — see WriteOpen note.</summary>
        public static void WriteClosed(QASessionInfo session, QAConfig config, string sessionFolder,
                                       float durationSec, long eventCount, GutSpecData? gutSpec)
        {
            var m = Build(session, config, gutSpec);
            m.status = "closed";
            m.durationSec = durationSec;
            m.counts = new Counts { events = eventCount, telemetry = 0, inputs = 0 };
            WriteFile(sessionFolder, m);
        }

        private static Manifest Build(QASessionInfo session, QAConfig config, GutSpecData? gutSpec = null)
        {
            return new Manifest
            {
                schemaVersion = QASessionInfo.SchemaVersion,
                sessionId = session.SessionId,
                folderName = session.FolderName,
                level = session.Level,
                startedUtc = session.StartedUtc,
                unityVersion = session.UnityVersion,
                appVersion = session.AppVersion,
                configSnapshot = new ConfigSnapshot
                {
                    startStopKey = config.startStopKey.ToString(),
                    overlayKey = config.overlayKey.ToString(),
                    consoleEvents = config.consoleEvents,
                    telemetryHz = config.telemetryHz,
                    inputKeyframeEverySteps = config.inputKeyframeEverySteps,
                    flushEveryNEvents = config.flushEveryNEvents,
                    denseFlushSeconds = config.denseFlushSeconds
                },
                gutSpec = gutSpec.HasValue
                    ? new GutSpec
                    {
                        runSpeed = gutSpec.Value.runSpeed,
                        jumpHeight = gutSpec.Value.jumpHeight,
                        gravityScale = gutSpec.Value.gravityScale
                    }
                    : new GutSpec(),               // zeros only when no adapter supplied them
                gutSpecSource = gutSpec.HasValue ? "adapter" : "pending-slice-c",
                counts = new Counts()
            };
        }

        private static void WriteFile(string sessionFolder, Manifest m)
        {
            File.WriteAllText(Path.Combine(sessionFolder, FileName),
                              JsonUtility.ToJson(m, prettyPrint: true));
        }
    }
}
