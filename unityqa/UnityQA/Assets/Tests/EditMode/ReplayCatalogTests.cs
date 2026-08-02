// -----------------------------------------------------------------------------
// UnityQA Tests — ReplayCatalogTests.cs                     (M3 Slice D tests)
//
// The catalog is exercised against session folders built with the REAL
// writers (SessionManifest, ReplayFileStore, JsonUtility for validation) —
// the same producer/consumer pairing discipline as the trajectory tests.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityQA.Core;
using UnityQA.Logging;
using UnityQA.Replay;

namespace UnityQA.Tests
{
    public sealed class ReplayCatalogTests
    {
        private string root;
        private QAConfig config;

        [SetUp]
        public void SetUp()
        {
            root = Directory.CreateDirectory(Path.Combine(
                Path.GetTempPath(), "unityqa-test-" + Guid.NewGuid().ToString("N"))).FullName;
            config = ScriptableObject.CreateInstance<QAConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(root, true); } catch { }
            UnityEngine.Object.DestroyImmediate(config);
        }

        /// <summary>Create a realistic session folder with controlled name/content.</summary>
        private QASessionInfo MakeSession(string folderName, bool withReplay,
                                          int frames = 10, bool closed = true)
        {
            string folder = Directory.CreateDirectory(Path.Combine(root, folderName)).FullName;
            var session = new QASessionInfo("CatalogLevel", () => 0f, DateTime.UtcNow, "u", "a");

            SessionManifest.WriteOpen(session, config, folder);
            if (closed)
                SessionManifest.WriteClosed(session, config, folder, durationSec: 12.5f, eventCount: 40);

            if (withReplay)
            {
                var list = new List<ReplayFrame>();
                for (int i = 0; i < frames; i++)
                    list.Add(new ReplayFrame { frameNumber = i, timestamp = i * 0.016f, horizontal = 1f });
                ReplayFileStore.Save(ReplayRecording.Create(session.SessionId, "t0", list), folder);
            }
            return session;
        }

        private void WriteValidation(string folderName, string originalId, string validationId,
                                     string verdict, float maxDev)
        {
            string folder = Path.Combine(root, folderName);
            var v = new ReplayValidationResult
            {
                schemaVersion = 1,
                originalSessionId = originalId,
                validationSessionId = validationId,
                verdict = verdict,
                maxDeviation = maxDev
            };
            File.WriteAllText(Path.Combine(folder, "validation.json"), JsonUtility.ToJson(v, true));
        }

        [Test]
        public void Scan_IndexesSessions_NewestFirst_WithReplayFacts()
        {
            var older = MakeSession("20260728-100000_aaaa1111", withReplay: true, frames: 30);
            var newer = MakeSession("20260728-110000_bbbb2222", withReplay: false);

            var doc = ReplayCatalog.Scan(root);

            Assert.AreEqual(2, doc.sessionCount);
            Assert.AreEqual(0, doc.skippedFolders);
            Assert.AreEqual("20260728-110000_bbbb2222", doc.entries[0].folderName, "newest first");
            Assert.AreEqual(newer.SessionId, doc.entries[0].sessionId);
            Assert.IsFalse(doc.entries[0].hasReplay);
            Assert.IsTrue(doc.entries[1].hasReplay);
            Assert.AreEqual(30, doc.entries[1].replayFrameCount);
            Assert.AreEqual("closed", doc.entries[0].status);
            Assert.AreEqual(12.5f, doc.entries[0].durationSec, 1e-3f);
            Assert.AreEqual(older.SessionId, doc.entries[1].sessionId);
        }

        [Test]
        public void Scan_CrossLinksNewestValidation_ToItsOriginal()
        {
            var original = MakeSession("20260728-100000_orig0000", withReplay: true);
            var val1 = MakeSession("20260728-110000_vald1111", withReplay: false);
            var val2 = MakeSession("20260728-120000_vald2222", withReplay: false);
            WriteValidation("20260728-110000_vald1111", original.SessionId, val1.SessionId, "FAIL", 3.2f);
            WriteValidation("20260728-120000_vald2222", original.SessionId, val2.SessionId, "PASS", 0.14f);

            var doc = ReplayCatalog.Scan(root);

            ReplayMetadata entry = doc.entries.Find(e => e.sessionId == original.SessionId);
            Assert.IsTrue(entry.hasValidation);
            Assert.AreEqual("PASS", entry.validationVerdict, "the NEWEST validation must win");
            Assert.AreEqual(0.14f, entry.validationMaxDeviation, 1e-3f);
            Assert.AreEqual(val2.SessionId, entry.validationSessionId);

            ReplayMetadata valEntry = doc.entries.Find(e => e.sessionId == val1.SessionId);
            Assert.IsFalse(valEntry.hasValidation, "validation sessions are not themselves validated");
        }

        [Test]
        public void Scan_SkipsDamagedFolders_CountsThem_NeverThrows()
        {
            MakeSession("20260728-100000_good0000", withReplay: true);
            string bad = Directory.CreateDirectory(Path.Combine(root, "20260728-110000_bad00000")).FullName;
            File.WriteAllText(Path.Combine(bad, SessionManifest.FileName), "{ not valid json ]");
            Directory.CreateDirectory(Path.Combine(root, "not-a-session-folder"));

            var doc = ReplayCatalog.Scan(root);

            Assert.AreEqual(1, doc.sessionCount, "only the intact session is indexed");
            Assert.AreEqual(2, doc.skippedFolders, "damaged + foreign folders are counted, not fatal");
        }

        [Test]
        public void Scan_CrashedSession_SurfacesOpenStatus()
        {
            MakeSession("20260728-100000_crash000", withReplay: true, closed: false);
            var doc = ReplayCatalog.Scan(root);
            Assert.AreEqual("open", doc.entries[0].status, "crash marker must reach the catalog");
        }

        [Test]
        public void SaveAndLoad_CatalogFile_RoundTrips()
        {
            MakeSession("20260728-100000_rt000000", withReplay: true, frames: 7);
            var doc = ReplayCatalog.Scan(root);

            string path = ReplayCatalog.Save(doc, root);
            Assert.IsTrue(File.Exists(path));

            var back = ReplayCatalog.LoadSaved(root);
            Assert.IsNotNull(back);
            Assert.AreEqual(doc.sessionCount, back.sessionCount);
            Assert.AreEqual(doc.entries[0].sessionId, back.entries[0].sessionId);
            Assert.AreEqual(7, back.entries[0].replayFrameCount);
        }

        [Test]
        public void Scan_EmptyOrMissingRoot_ReturnsEmptyCatalog()
        {
            var doc = ReplayCatalog.Scan(Path.Combine(root, "does-not-exist"));
            Assert.AreEqual(0, doc.sessionCount);
            Assert.IsNotNull(doc.entries);
        }
    }
}
