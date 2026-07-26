// -----------------------------------------------------------------------------
// UnityQA Tests — SessionManifestTests.cs                   (M2 test plan §12)
//
// session.json open → closed lifecycle and schema §2 shape, round-tripped
// through the same serializer that writes it (JsonUtility — decision D-009).
// QAConfig is a ScriptableObject, so tests create it via CreateInstance,
// never `new` (Unity rule worth knowing: engine objects have engine lifetimes).
// -----------------------------------------------------------------------------

using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityQA.Core;
using UnityQA.Logging;

namespace UnityQA.Tests
{
    public sealed class SessionManifestTests
    {
        private string dir;
        private QAConfig config;

        [SetUp]
        public void SetUp()
        {
            dir = Directory.CreateDirectory(
                Path.Combine(Path.GetTempPath(), "unityqa-test-" + Guid.NewGuid().ToString("N"))).FullName;
            config = ScriptableObject.CreateInstance<QAConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(dir, true); } catch { }
            UnityEngine.Object.DestroyImmediate(config);
        }

        private static QASessionInfo Session() =>
            new QASessionInfo("TestLevel", () => 0f, DateTime.UtcNow, "6000.3.0f1", "0.1");

        private SessionManifest.Manifest ReadBack() =>
            JsonUtility.FromJson<SessionManifest.Manifest>(
                File.ReadAllText(Path.Combine(dir, SessionManifest.FileName)));

        [Test]
        public void WriteOpen_ProducesSchema2Shape_WithStatusOpen()
        {
            var s = Session();
            SessionManifest.WriteOpen(s, config, dir);

            var m = ReadBack();
            Assert.AreEqual(1, m.schemaVersion);
            Assert.AreEqual(s.SessionId, m.sessionId);
            Assert.AreEqual(s.FolderName, m.folderName);
            Assert.AreEqual("TestLevel", m.level);
            Assert.AreEqual("open", m.status);
            Assert.AreEqual("F9", m.configSnapshot.startStopKey);
            Assert.AreEqual(10, m.configSnapshot.telemetryHz);
            Assert.AreEqual("pending-slice-c", m.gutSpecSource);
        }

        [Test]
        public void WriteClosed_OverwritesWithFinalNumbers()
        {
            var s = Session();
            SessionManifest.WriteOpen(s, config, dir);
            SessionManifest.WriteClosed(s, config, dir, durationSec: 21.5f, eventCount: 117);

            var m = ReadBack();
            Assert.AreEqual("closed", m.status);
            Assert.AreEqual(21.5f, m.durationSec, 1e-4f);
            Assert.AreEqual(117, m.counts.events);
            Assert.AreEqual(0, m.counts.telemetry); // honest zero until Slice C
            Assert.AreEqual(s.SessionId, m.sessionId, "identity must survive the rewrite");
        }

        [Test]
        public void CrashScenario_FileNeverRewritten_KeepsStatusOpen()
        {
            SessionManifest.WriteOpen(Session(), config, dir);
            // ... process dies here: no WriteClosed ever happens ...
            Assert.AreEqual("open", ReadBack().status,
                "an un-closed manifest is the crash marker later modules depend on");
        }
    }
}
