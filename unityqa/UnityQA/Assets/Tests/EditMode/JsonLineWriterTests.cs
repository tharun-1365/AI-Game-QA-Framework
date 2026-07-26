// -----------------------------------------------------------------------------
// UnityQA Tests — JsonLineWriterTests.cs                    (M2 test plan §12)
//
// The schema is frozen; these tests pin the exact wire format. Exact-string
// assertions are deliberately brittle: if one fails, you are changing schema
// v1 output and must stop and think (MODULES.md decision + version bump), not
// fix the test. The culture test is the important one — it fails on any code
// path that forgets InvariantCulture, which is the classic silent corrupter
// of numeric logs on non-English machines.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityQA.Core;
using UnityQA.Logging;

namespace UnityQA.Tests
{
    public sealed class JsonLineWriterTests
    {
        private static QASessionInfo Session() =>
            new QASessionInfo("TestLevel", () => 0f,
                              new DateTime(2026, 7, 26, 12, 0, 0, DateTimeKind.Utc),
                              "6000.3.0f1", "0.1");

        [Test]
        public void HeaderLine_MatchesSchemaA1_Exactly()
        {
            var s = Session();
            string line = new JsonLineWriter().HeaderLine("events", s);
            Assert.AreEqual(
                $"{{\"header\":1,\"schemaVersion\":1,\"stream\":\"events\",\"sessionId\":\"{s.SessionId}\"}}",
                line);
        }

        [Test]
        public void EventLine_FullEnvelope_MatchesSchemaExactly()
        {
            var payload = new Dictionary<string, object> { { "other", "Spike (3)" }, { "relVelX", -2.5f } };
            var e = new QAEvent("abc-123", 7, 9.8014f, 588, QAEventType.CollisionEnter,
                                new Vector2(14.2f, 0.85f), payload);

            string line = new JsonLineWriter().EventLine(e);

            Assert.AreEqual(
                "{\"sid\":\"abc-123\",\"seq\":7,\"t\":9.801,\"frame\":588," +
                "\"type\":\"CollisionEnter\",\"pos\":{\"x\":14.2,\"y\":0.85}," +
                "\"payload\":{\"other\":\"Spike (3)\",\"relVelX\":-2.5}}",
                line);
        }

        [Test]
        public void EventLine_NoPosition_OmitsPosField()
        {
            var e = new QAEvent("s", 0, 0f, 0, QAEventType.SessionStarted, null, null);
            string line = new JsonLineWriter().EventLine(e);
            StringAssert.DoesNotContain("\"pos\"", line);
            StringAssert.Contains("\"payload\":{}", line);
        }

        [Test]
        public void Floats_RoundToThreeDecimals_AndTrimTrailingZeros()
        {
            var e = new QAEvent("s", 0, 1.23456f, 0, QAEventType.Landed, new Vector2(6.0f, 0.1000f),
                                new Dictionary<string, object> { { "fallSpeed", 12.0004f } });
            string line = new JsonLineWriter().EventLine(e);
            StringAssert.Contains("\"t\":1.235", line);       // rounded, not truncated
            StringAssert.Contains("\"x\":6,", line);          // 6.0 → "6"
            StringAssert.Contains("\"fallSpeed\":12}", line); // 12.0004 → "12"
        }

        [Test]
        public void Escape_HandlesQuotesBackslashesAndControlChars()
        {
            Assert.AreEqual("plain", JsonLineWriter.Escape("plain"));
            Assert.AreEqual("say \\\"hi\\\"", JsonLineWriter.Escape("say \"hi\""));
            Assert.AreEqual("a\\\\b", JsonLineWriter.Escape("a\\b"));
            Assert.AreEqual("line\\nbreak\\ttab", JsonLineWriter.Escape("line\nbreak\ttab"));
        }

        [Test]
        public void Numbers_UseInvariantCulture_EvenOnCommaDecimalMachines()
        {
            var original = Thread.CurrentThread.CurrentCulture;
            try
            {
                // German culture writes 1.5 as "1,5" by default — which would
                // silently corrupt every numeric field in every log file.
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");

                var e = new QAEvent("s", 0, 1.5f, 0, QAEventType.Landed, new Vector2(2.75f, 0f), null);
                string line = new JsonLineWriter().EventLine(e);

                StringAssert.Contains("\"t\":1.5", line);
                StringAssert.Contains("\"x\":2.75", line);
                StringAssert.DoesNotContain(",5", line);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }

        [Test]
        public void PayloadValueTypes_KeepTheirJsonTypes()
        {
            var payload = new Dictionary<string, object>
            {
                { "s", "text" }, { "i", 42 }, { "l", 9000000000L },
                { "f", 1.5f }, { "b", true }, { "n", null },
            };
            var e = new QAEvent("s", 0, 0f, 0, QAEventType.SessionEnded, null, payload);
            string line = new JsonLineWriter().EventLine(e);

            StringAssert.Contains("\"s\":\"text\"", line);
            StringAssert.Contains("\"i\":42", line);
            StringAssert.Contains("\"l\":9000000000", line);
            StringAssert.Contains("\"f\":1.5", line);
            StringAssert.Contains("\"b\":true", line);
            StringAssert.Contains("\"n\":null", line);
        }
    }
}
