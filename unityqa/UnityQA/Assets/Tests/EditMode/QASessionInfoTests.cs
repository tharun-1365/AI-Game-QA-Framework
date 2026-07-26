// -----------------------------------------------------------------------------
// UnityQA Tests — QASessionInfoTests.cs                     (M2 test plan §12)
//
// Session identity (amendment A2) and stamping-counter integrity (NFR-1.5).
// The injected clock (constructor parameter) is what lets these run in
// EditMode with no engine loop — controlled time, deterministic assertions.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityQA.Core;

namespace UnityQA.Tests
{
    public sealed class QASessionInfoTests
    {
        private static QASessionInfo Make(Func<float> clock = null) =>
            new QASessionInfo("TestLevel", clock ?? (() => 0f),
                              new DateTime(2026, 7, 26, 12, 0, 0, DateTimeKind.Utc),
                              "6000.3.0f1", "0.1");

        [Test]
        public void SessionId_IsValidGuid()
        {
            Assert.IsTrue(Guid.TryParse(Make().SessionId, out _));
        }

        [Test]
        public void SessionIds_AreUniqueAcross1000Mints()
        {
            var seen = new HashSet<string>();
            for (int i = 0; i < 1000; i++)
                Assert.IsTrue(seen.Add(Make().SessionId), "duplicate GUID minted");
        }

        [Test]
        public void FolderName_MatchesSpec_TimestampPlusUuidPrefix()
        {
            var s = Make();
            // yyyyMMdd-HHmmss_ + first 8 chars of the UUID (amendment A2)
            StringAssert.IsMatch(@"^20260726-120000_[0-9a-f]{8}$", s.FolderName);
            StringAssert.StartsWith(s.FolderName.Substring(16), s.SessionId.Substring(0, 8));
        }

        [Test]
        public void Seq_IsStrictlyIncreasing_GapFree_FromZero()
        {
            var s = Make();
            for (long expected = 0; expected < 100; expected++)
                Assert.AreEqual(expected, s.NextSeq());
            Assert.AreEqual(100, s.EventCount);
        }

        [Test]
        public void SessionTime_UsesInjectedClock()
        {
            float now = 10f;
            var s = Make(() => now);   // session starts at clock = 10
            now = 12.5f;
            Assert.AreEqual(2.5f, s.SessionTime, 1e-4f);
        }

        [Test]
        public void SchemaVersion_IsFrozenAtOne()
        {
            // If this test surprises you, you are editing a frozen schema:
            // stop, write a MODULES.md decision entry, bump properly (EVENT-SCHEMA.md).
            Assert.AreEqual(1, QASessionInfo.SchemaVersion);
        }
    }
}
