// -----------------------------------------------------------------------------
// UnityQA Tests — ReplayRecordingTests.cs                   (M3 Slice A tests)
//
// Pins the replay wire format: field names, schemaVersion, frameCount
// integrity, pretty printing, and full serialization round-trip through the
// same serializer that writes the file. If a rename ever breaks these, you
// are changing the replay schema — stop and bump properly.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityQA.Replay;

namespace UnityQA.Tests
{
    public sealed class ReplayRecordingTests
    {
        private static List<ReplayFrame> Frames(int n)
        {
            var list = new List<ReplayFrame>(n);
            for (int i = 0; i < n; i++)
            {
                list.Add(new ReplayFrame
                {
                    frameNumber = i,
                    timestamp = i * 0.016f,
                    horizontal = (i % 3) - 1,          // cycles -1, 0, +1
                    jumpPressed = i == 5,
                    jumpHeld = i >= 5 && i <= 8
                });
            }
            return list;
        }

        [Test]
        public void Create_StampsVersionIdentityAndCount()
        {
            var rec = ReplayRecording.Create("uuid-123", "2026-07-26T12:00:00.0000000Z", Frames(10));

            Assert.AreEqual(ReplayRecording.CurrentSchemaVersion, rec.schemaVersion);
            Assert.AreEqual("uuid-123", rec.sessionId);
            Assert.AreEqual("2026-07-26T12:00:00.0000000Z", rec.recordingStartTime);
            Assert.AreEqual(10, rec.frameCount);
            Assert.AreEqual(rec.frameCount, rec.frames.Length, "frameCount must equal frames.Length");
        }

        [Test]
        public void SerializationRoundTrip_PreservesEveryFrameField()
        {
            var rec = ReplayRecording.Create("uuid-rt", "t0", Frames(20));
            var back = JsonUtility.FromJson<ReplayRecording>(JsonUtility.ToJson(rec, true));

            Assert.AreEqual(rec.schemaVersion, back.schemaVersion);
            Assert.AreEqual(rec.sessionId, back.sessionId);
            Assert.AreEqual(rec.frameCount, back.frameCount);
            Assert.AreEqual(rec.frames.Length, back.frames.Length);
            for (int i = 0; i < rec.frames.Length; i++)
            {
                Assert.AreEqual(rec.frames[i].frameNumber, back.frames[i].frameNumber);
                Assert.AreEqual(rec.frames[i].timestamp, back.frames[i].timestamp, 1e-5f);
                Assert.AreEqual(rec.frames[i].horizontal, back.frames[i].horizontal);
                Assert.AreEqual(rec.frames[i].jumpPressed, back.frames[i].jumpPressed);
                Assert.AreEqual(rec.frames[i].jumpHeld, back.frames[i].jumpHeld);
            }
        }

        [Test]
        public void WireFormat_UsesSpecifiedFieldNames_AndIsPretty()
        {
            string json = JsonUtility.ToJson(ReplayRecording.Create("uuid-w", "t0", Frames(2)), true);

            foreach (string field in new[] { "\"schemaVersion\"", "\"sessionId\"",
                                             "\"recordingStartTime\"", "\"frameCount\"", "\"frames\"",
                                             "\"frameNumber\"", "\"timestamp\"", "\"horizontal\"",
                                             "\"jumpPressed\"", "\"jumpHeld\"" })
                StringAssert.Contains(field, json);

            StringAssert.Contains("\n", json, "replay.json must be pretty-printed per spec");
        }

        [Test]
        public void FileStore_WritesAndReturnsPath_FileParsesBack()
        {
            string dir = Directory.CreateDirectory(Path.Combine(
                Path.GetTempPath(), "unityqa-test-" + Guid.NewGuid().ToString("N"))).FullName;
            try
            {
                string path = ReplayFileStore.Save(
                    ReplayRecording.Create("uuid-f", "t0", Frames(3)), dir);

                Assert.AreEqual(Path.Combine(dir, ReplayFileStore.FileName), path);
                var back = JsonUtility.FromJson<ReplayRecording>(File.ReadAllText(path));
                Assert.AreEqual("uuid-f", back.sessionId);
                Assert.AreEqual(3, back.frameCount);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Test]
        public void EmptyRecording_IsValid_WithZeroFrames()
        {
            var rec = ReplayRecording.Create("uuid-e", "t0", new List<ReplayFrame>());
            var back = JsonUtility.FromJson<ReplayRecording>(JsonUtility.ToJson(rec, true));
            Assert.AreEqual(0, back.frameCount);
            Assert.AreEqual(0, back.frames.Length);
        }
    }
}
