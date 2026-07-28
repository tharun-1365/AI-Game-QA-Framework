// -----------------------------------------------------------------------------
// UnityQA Tests — ReplayLoadTests.cs                        (M3 Slice B tests)
//
// The loader's skepticism, pinned: happy path, missing file, malformed JSON,
// future schema refusal, frameCount repair — plus the ReplayInputSource
// contract (frame in → properties out, Clear → neutral).
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityQA.Adapters;
using UnityQA.Replay;

namespace UnityQA.Tests
{
    public sealed class ReplayLoadTests
    {
        private string dir;

        [SetUp]
        public void SetUp() =>
            dir = Directory.CreateDirectory(Path.Combine(
                Path.GetTempPath(), "unityqa-test-" + Guid.NewGuid().ToString("N"))).FullName;

        [TearDown]
        public void TearDown() { try { Directory.Delete(dir, true); } catch { } }

        private string WriteReplay(ReplayRecording rec)
        {
            string path = Path.Combine(dir, ReplayFileStore.FileName);
            File.WriteAllText(path, JsonUtility.ToJson(rec, true));
            return path;
        }

        private static ReplayRecording Sample(int frames = 5)
        {
            var list = new List<ReplayFrame>();
            for (int i = 0; i < frames; i++)
                list.Add(new ReplayFrame { frameNumber = i, timestamp = i * 0.016f, horizontal = 1f });
            return ReplayRecording.Create("uuid-load", "t0", list);
        }

        [Test]
        public void Load_HappyPath_RoundTrips()
        {
            string path = WriteReplay(Sample(5));
            var rec = ReplayFileStore.Load(path);
            Assert.IsNotNull(rec);
            Assert.AreEqual("uuid-load", rec.sessionId);
            Assert.AreEqual(5, rec.frameCount);
        }

        [Test]
        public void Load_MissingFile_ReturnsNull_WithError()
        {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("file not found"));
            Assert.IsNull(ReplayFileStore.Load(Path.Combine(dir, "nope.json")));
        }

        [Test]
        public void Load_MalformedJson_ReturnsNull_NeverThrows()
        {
            string path = Path.Combine(dir, ReplayFileStore.FileName);
            File.WriteAllText(path, "{ this is : not json ]");
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("malformed JSON"));
            Assert.IsNull(ReplayFileStore.Load(path));
        }

        [Test]
        public void Load_FutureSchema_IsRefused()
        {
            var rec = Sample(2);
            rec.schemaVersion = ReplayRecording.CurrentSchemaVersion + 1;
            string path = WriteReplay(rec);
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("refusing to load"));
            Assert.IsNull(ReplayFileStore.Load(path));
        }

        [Test]
        public void Load_FrameCountMismatch_TrustsArray_WithWarning()
        {
            var rec = Sample(4);
            rec.frameCount = 99; // simulated truncation/hand-edit
            string path = WriteReplay(rec);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("trusting the array"));
            var loaded = ReplayFileStore.Load(path);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(4, loaded.frameCount, "frames array is ground truth");
        }

        // ----------------------- ReplayInputSource contract -------------------

        [Test]
        public void InputSource_ReflectsFrame_AndClearsToNeutral()
        {
            var src = new ReplayInputSource();
            Assert.AreEqual(0f, src.MoveX);
            Assert.IsFalse(src.JumpHeld);

            src.SetFrame(new ReplayFrame { horizontal = -1f, jumpPressed = true, jumpHeld = true });
            Assert.AreEqual(-1f, src.MoveX);
            Assert.IsTrue(src.JumpDown);
            Assert.IsTrue(src.JumpHeld);

            src.Clear();
            Assert.AreEqual(0f, src.MoveX, "a stopped replay must not keep moving the player");
            Assert.IsFalse(src.JumpDown);
            Assert.IsFalse(src.JumpHeld);
        }
    }
}
