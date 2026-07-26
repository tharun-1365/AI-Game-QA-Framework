// -----------------------------------------------------------------------------
// UnityQA Tests — JsonlSinkTests.cs                         (M2 test plan §12)
//
// File-level behavior of the events sink: header-first, append order, flush
// policy consequences, crash tolerance (readable without Close), and safe
// double-Close. Uses a real temp directory — file I/O IS the unit under test.
// -----------------------------------------------------------------------------

using System;
using System.IO;
using NUnit.Framework;
using UnityQA.Core;
using UnityQA.Logging;

namespace UnityQA.Tests
{
    public sealed class JsonlSinkTests
    {
        private string dir;

        [SetUp]
        public void SetUp() =>
            dir = Directory.CreateDirectory(
                Path.Combine(Path.GetTempPath(), "unityqa-test-" + Guid.NewGuid().ToString("N")))
                .FullName;

        [TearDown]
        public void TearDown() { try { Directory.Delete(dir, true); } catch { /* temp cleanup best-effort */ } }

        private static QASessionInfo Session() =>
            new QASessionInfo("TestLevel", () => 0f, DateTime.UtcNow, "6000.3.0f1", "0.1");

        private static QAEvent Event(long seq, QAEventType type = QAEventType.Landed) =>
            new QAEvent("sid", seq, seq * 0.1f, (int)seq, type, null, null);

        private string[] Lines() => File.ReadAllLines(Path.Combine(dir, "events.jsonl"));

        [Test]
        public void Open_WritesHeaderAsFirstLine_Immediately()
        {
            var sink = new JsonlSink(flushEveryN: 25);
            sink.Open(Session(), dir);

            // No Close, no event writes: the header alone must already be durable.
            var lines = Lines();
            Assert.AreEqual(1, lines.Length);
            StringAssert.StartsWith("{\"header\":1,", lines[0]);
            sink.Close();
        }

        [Test]
        public void Write_AppendsInOrder_AndCountsExcludeHeader()
        {
            var sink = new JsonlSink(1); // flush every line for immediate visibility
            sink.Open(Session(), dir);
            for (long i = 0; i < 5; i++) sink.Write(Event(i));

            Assert.AreEqual(5, sink.LineCount);
            var lines = Lines();
            Assert.AreEqual(6, lines.Length); // header + 5
            for (int i = 1; i < lines.Length; i++)
                StringAssert.Contains($"\"seq\":{i - 1},", lines[i]);
            sink.Close();
        }

        [Test]
        public void LifecycleEvents_FlushImmediately_DespiteLargeFlushN()
        {
            var sink = new JsonlSink(flushEveryN: 1000);
            sink.Open(Session(), dir);
            sink.Write(Event(0, QAEventType.SessionStarted));

            // Read WITHOUT closing: if the lifecycle flush works, the line is on disk now.
            Assert.AreEqual(2, Lines().Length, "SessionStarted must be durable immediately");
            sink.Close();
        }

        [Test]
        public void CrashSimulation_FileReadableWithoutClose_EveryLineComplete()
        {
            var sink = new JsonlSink(1);
            sink.Open(Session(), dir);
            for (long i = 0; i < 20; i++) sink.Write(Event(i));
            // Deliberately NO Close — simulating a killed process after flushes.

            var lines = Lines();
            Assert.GreaterOrEqual(lines.Length, 2);
            foreach (var line in lines)
            {
                StringAssert.StartsWith("{", line);
                StringAssert.EndsWith("}", line); // no torn lines among flushed content (NFR-1.4)
            }
            sink.Close(); // and cleanup must still be safe afterwards
        }

        [Test]
        public void Close_IsIdempotent_AndLateWritesAreDroppedNotThrown()
        {
            var sink = new JsonlSink(1);
            sink.Open(Session(), dir);
            sink.Write(Event(0));
            sink.Close();

            Assert.DoesNotThrow(() => sink.Close());          // defensive double-close
            Assert.DoesNotThrow(() => sink.Write(Event(1)));  // late event: dropped silently
            Assert.AreEqual(2, Lines().Length);               // header + the one real event
        }
    }
}
