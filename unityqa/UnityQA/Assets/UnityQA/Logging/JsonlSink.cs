// -----------------------------------------------------------------------------
// UnityQA — JsonlSink.cs                          (M2 design §9, §11 · HOTFIX-1)
//
// PURPOSE
//   The events.jsonl writer: header line first (A1), one envelope line per
//   event, flush policy balancing crash-safety against I/O cost.
//
// HOTFIX-1 (post-Slice-C validation): BUFFERED APPEND, NO HELD HANDLE
//   The original implementation kept a FileStream open for the whole session
//   (FileShare.Read). Windows sharing checks are mutual: a reader using
//   File.ReadAllLines requests ITS OWN share mode of Read, which refuses to
//   coexist with an already-open writer — IOException "sharing violation",
//   regardless of how permissive the writer's share mode is. The bug was
//   latent from Slice B (masked on platforms where sharing is advisory).
//
//   Fix: lines accumulate in an in-memory StringBuilder; every Flush performs
//   one open→append→close operation. Between flushes NO handle is held, so
//   the file is readable by ANY reader at ANY time — which is the actual
//   contract ("readable while the logger has it open"), now satisfied
//   unconditionally. Durability policy is unchanged: what was flushed is on
//   disk; what wasn't is lost on a crash (NFR-1.4, same as before). Cost:
//   one file-open per flush (≤ every 25 events + lifecycle + Close) —
//   microseconds, invisible at our rates.
//
// FLUSH POLICY (unchanged from Slice B — say it in the viva)
//   - Lifecycle events (SessionStarted/SessionEnded) flush IMMEDIATELY.
//   - Otherwise flush every N lines (QAConfig.flushEveryNEvents, default 25).
//   - Header is durable the moment Open returns.
//
// LINE ENDINGS
//   Explicit '\n' (schema §6). The old StreamWriter.WriteLine emitted \r\n on
//   Windows — a silent schema deviation this rewrite also corrects; all
//   existing readers (ReadAllLines, JSONL parsers) accept both.
// -----------------------------------------------------------------------------

using System.IO;
using System.Text;
using UnityQA.Core;

namespace UnityQA.Logging
{
    /// <summary>Append-only events.jsonl writer with header line and flush policy.
    /// Holds no file handle between flushes — the file is always readable.</summary>
    public sealed class JsonlSink : ILogSink
    {
        public const string StreamName = "events";

        // File.WriteAllText/AppendAllText default to UTF-8 WITHOUT BOM (schema §6);
        // made explicit here so the guarantee is visible, not incidental.
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private readonly JsonLineWriter writer = new JsonLineWriter();
        private readonly StringBuilder buffer = new StringBuilder(8 * 1024);
        private readonly int flushEveryN;

        private string path;
        private bool isOpen;
        private int linesSinceFlush;

        /// <summary>Event lines written (excludes the header) — feeds session.json counts.</summary>
        public long LineCount { get; private set; }

        public JsonlSink(int flushEveryN)
        {
            // Guard against a misconfigured 0/negative: flush-every-line is the
            // safe degenerate case, not an exception.
            this.flushEveryN = flushEveryN < 1 ? 1 : flushEveryN;
        }

        public void Open(QASessionInfo session, string sessionFolder)
        {
            path = Path.Combine(sessionFolder, StreamName + ".jsonl");

            // Create/truncate with the header, then CLOSE — durable immediately,
            // and stale debris from any earlier file at this path is gone.
            // (Session folders are UUID-unique, so debris is theoretical anyway.)
            File.WriteAllText(path, writer.HeaderLine(StreamName, session) + "\n", Utf8NoBom);

            buffer.Length = 0;
            LineCount = 0;
            linesSinceFlush = 0;
            isOpen = true;
        }

        public void Write(QAEvent e)
        {
            if (!isOpen) return; // defensive: a late event after Close is dropped, never a throw

            buffer.Append(writer.EventLine(e)).Append('\n');
            LineCount++;
            linesSinceFlush++;

            bool lifecycle = e.Type == QAEventType.SessionStarted || e.Type == QAEventType.SessionEnded;
            if (lifecycle || linesSinceFlush >= flushEveryN)
                Flush();
        }

        public void Flush()
        {
            if (!isOpen || buffer.Length == 0) return;

            // One open→append→close: the only instant a handle exists at all.
            File.AppendAllText(path, buffer.ToString(), Utf8NoBom);
            buffer.Length = 0;
            linesSinceFlush = 0;
        }

        public void Close()
        {
            if (!isOpen) return;
            Flush();
            isOpen = false; // idempotent: second Close is a no-op (crash paths rely on this)
        }
    }
}
