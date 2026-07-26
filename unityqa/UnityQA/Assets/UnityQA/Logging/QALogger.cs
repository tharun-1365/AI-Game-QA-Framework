// -----------------------------------------------------------------------------
// UnityQA — QALogger.cs                                    (SRS §8, M2 design §4)
//
// PURPOSE
//   The one bus subscriber that owns persistence: creates the session folder,
//   writes session.json open/closed, and fans every event out to its sinks
//   (JsonlSink always; ConsoleSink when configured). Sits on the same "[QA]"
//   GameObject as QARunner.
//
// LIFECYCLE CHOREOGRAPHY (the subtle 20% — read this before the viva)
//   Subscribe happens in Start, not OnEnable: QARunner creates the Bus in its
//   Awake, and within one GameObject Unity orders Awakes before Starts but
//   makes no promise about OnEnable vs. a sibling's Awake. Start is the first
//   moment the Bus is guaranteed to exist — and sessions can only begin from
//   Update (F9) anyway, so nothing can be missed.
//
//   Shutdown ordering: when Play mode ends mid-session, QARunner.OnDisable and
//   QALogger.OnDisable race — Unity does not define sibling OnDisable order.
//   If ours runs FIRST, we end the session ourselves (EndSession is public and
//   idempotent) while still subscribed, so SessionEnded flows through the
//   normal path; QARunner's own OnDisable then finds the session already gone.
//   If ours runs SECOND, the normal path already completed. Either order ends
//   with a closed manifest — the property we actually care about.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityQA.Core;

namespace UnityQA.Logging
{
    /// <summary>
    /// Persistence owner. Scene setup: add to the "[QA]" GameObject next to
    /// QARunner (auto-discovered — no inspector wiring needed).
    /// </summary>
    [RequireComponent(typeof(QARunner))]
    public sealed class QALogger : MonoBehaviour
    {
        private QARunner runner;
        private readonly List<ILogSink> sinks = new List<ILogSink>();
        private JsonlSink eventsSink;      // kept separately for LineCount
        private string sessionFolder;
        private bool streamsOpen;

        /// <summary>Root of all session folders (FR-1.9): persistentDataPath/UnityQA/Sessions.</summary>
        public static string SessionsRoot =>
            Path.Combine(Application.persistentDataPath, "UnityQA", "Sessions");

        private IGutSpecSource gutSource; // optional sibling (Slice C): fills session.json gutSpec

        private void Awake()
        {
            runner = GetComponent<QARunner>();
            gutSource = GetComponent<IGutSpecSource>(); // null is fine → "pending-slice-c" path
        }

        /// <summary>Real GUT constants when an adapter sibling can provide them; else null.</summary>
        private GutSpecData? CurrentGutSpec =>
            gutSource != null && gutSource.TryGetGutSpec(out GutSpecData g) ? g : (GutSpecData?)null;

        private void Start()
        {
            runner.Bus.Subscribe(OnEvent); // Bus guaranteed non-null by now (see header)
        }

        private void OnEvent(QAEvent e)
        {
            if (e.Type == QAEventType.SessionStarted)
                OpenStreams(runner.CurrentSession);

            if (!streamsOpen) return; // never throw from a bus handler

            for (int i = 0; i < sinks.Count; i++)
                sinks[i].Write(e);

            if (e.Type == QAEventType.SessionEnded)
                CloseStreams(e);
        }

        private void OpenStreams(QASessionInfo session)
        {
            if (streamsOpen || session == null) return;

            sessionFolder = Path.Combine(SessionsRoot, session.FolderName);
            Directory.CreateDirectory(sessionFolder);

            SessionManifest.WriteOpen(session, runner.Config, sessionFolder, CurrentGutSpec);

            sinks.Clear();
            eventsSink = new JsonlSink(runner.Config.flushEveryNEvents);
            eventsSink.Open(session, sessionFolder);
            sinks.Add(eventsSink);

            if (runner.Config.consoleEvents)
            {
                var console = new ConsoleSink();
                console.Open(session, sessionFolder);
                sinks.Add(console);
            }

            streamsOpen = true;
        }

        /// <summary>Clean close: SessionEnded has been written to every sink.</summary>
        private void CloseStreams(QAEvent endEvent)
        {
            if (!streamsOpen) return;
            streamsOpen = false;

            long eventCount = eventsSink.LineCount;

            foreach (var sink in sinks)
                sink.Close();

            // Duration comes from the event payload — CurrentSession may be
            // nulled by QARunner immediately after this publish returns.
            float duration = endEvent.Payload.TryGetValue("durationSec", out object d) && d is float f
                ? f : 0f;

            SessionManifest.WriteClosed(runner.CurrentSession, runner.Config, sessionFolder,
                                        duration, eventCount, CurrentGutSpec);

            Debug.Log($"[UnityQA] Session closed — {eventCount} events → {sessionFolder}");
            sinks.Clear();
            eventsSink = null;
        }

        private void OnDisable()
        {
            // Shutdown race handling — see header choreography note.
            if (runner != null && runner.IsSessionActive)
                runner.EndSession(); // flows through OnEvent → CloseStreams while still subscribed

            if (streamsOpen)
            {
                // Abnormal path (e.g. runner destroyed first): flush and release
                // file handles but DON'T mark the manifest closed — an "open"
                // manifest is the truthful crash marker (schema §2).
                streamsOpen = false;
                foreach (var sink in sinks) sink.Close();
                sinks.Clear();
                eventsSink = null;
                Debug.LogWarning("[UnityQA] Streams released without clean close — manifest stays \"open\".");
            }

            if (runner != null && runner.Bus != null)
                runner.Bus.Unsubscribe(OnEvent);
        }

        /// <summary>Right-click the component header → jump straight to the output.</summary>
        [ContextMenu("Open Sessions Folder")]
        private void OpenSessionsFolder()
        {
            Directory.CreateDirectory(SessionsRoot);
            Application.OpenURL("file://" + SessionsRoot.Replace('\\', '/'));
        }
    }
}
