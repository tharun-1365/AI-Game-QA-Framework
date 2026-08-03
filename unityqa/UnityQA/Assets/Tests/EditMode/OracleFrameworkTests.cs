// -----------------------------------------------------------------------------
// UnityQA Tests — OracleFrameworkTests.cs                   (M5 Slice B tests)
//
// The framework is tested with stub oracles (configurable pass/fail/skip/
// throw + call recording) — no gameplay, no real oracles (none exist yet, by
// scope). The ReplayManager integration runs against a TEMP sessions root
// via the parameterized overload, so no developer-machine session data is
// touched or polluted.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityQA.Adapters;
using UnityQA.Features;
using UnityQA.Oracles;

namespace UnityQA.Tests
{
    public sealed class OracleFrameworkTests
    {
        /// <summary>Configurable stub: records evaluation order, can pass,
        /// fail, skip (null) or throw.</summary>
        private sealed class StubOracle : IQualityOracle
        {
            public static readonly List<string> CallLog = new List<string>();

            public string Name { get; }
            public string Description => "stub";
            public bool Enabled { get; set; } = true;

            public Func<OracleContext, OracleResult> Behavior;

            public StubOracle(string name, Func<OracleContext, OracleResult> behavior = null)
            {
                Name = name;
                Behavior = behavior ?? (ctx => new OracleResult { passed = true });
            }

            public OracleResult Evaluate(OracleContext context)
            {
                CallLog.Add($"{Name}:{context.SessionId}");
                return Behavior(context);
            }
        }

        private static OracleContext Ctx(string id) => new OracleContext { SessionId = id };

        private static List<OracleContext> Contexts(params string[] ids)
        {
            var list = new List<OracleContext>();
            foreach (string id in ids) list.Add(Ctx(id));
            return list;
        }

        [SetUp]
        public void SetUp() => StubOracle.CallLog.Clear();

        // -------------------------------------------------------- registration

        [Test]
        public void Registry_PreservesRegistrationOrder()
        {
            var reg = new OracleRegistry();
            reg.Register(new StubOracle("zeta"));
            reg.Register(new StubOracle("alpha"));
            reg.Register(new StubOracle("mid"));

            Assert.AreEqual(3, reg.Count);
            Assert.AreEqual("zeta", reg.Oracles[0].Name, "order is registration, never alphabetical");
            Assert.AreEqual("alpha", reg.Oracles[1].Name);
        }

        [Test]
        public void Registry_RejectsDuplicateNames_AndNulls()
        {
            var reg = new OracleRegistry();
            Assert.IsTrue(reg.Register(new StubOracle("a")));
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("already registered"));
            Assert.IsFalse(reg.Register(new StubOracle("a")));
            Assert.IsFalse(reg.Register(null));
            Assert.AreEqual(1, reg.Count);
        }

        [Test]
        public void Registry_SetEnabled_TogglesByName()
        {
            var reg = new OracleRegistry();
            reg.Register(new StubOracle("a"));
            Assert.IsTrue(reg.SetEnabled("a", false));
            Assert.AreEqual(0, reg.EnabledCount);
            Assert.IsFalse(reg.SetEnabled("ghost", true), "unknown oracle → false, no throw");
        }

        // ----------------------------------------------------------- execution

        [Test]
        public void Runner_SessionMajor_OracleMinor_Order()
        {
            var reg = new OracleRegistry();
            reg.Register(new StubOracle("first"));
            reg.Register(new StubOracle("second"));

            OracleRunner.Run(reg, Contexts("s1", "s2"));

            CollectionAssert.AreEqual(
                new[] { "first:s1", "second:s1", "first:s2", "second:s2" },
                StubOracle.CallLog,
                "order contract: per session, oracles in registration order");
        }

        [Test]
        public void Runner_DisabledOracle_NeverEvaluated()
        {
            var reg = new OracleRegistry();
            reg.Register(new StubOracle("on"));
            var off = new StubOracle("off");
            reg.Register(off);
            reg.SetEnabled("off", false);

            var run = OracleRunner.Run(reg, Contexts("s1"));

            Assert.AreEqual(1, run.executedEvaluations);
            Assert.AreEqual(2, run.oracleCount);
            Assert.AreEqual(1, run.enabledOracleCount);
            CollectionAssert.DoesNotContain(StubOracle.CallLog, "off:s1");
        }

        [Test]
        public void Runner_EmptyRegistry_ProducesValidEmptyRun()
        {
            var run = OracleRunner.Run(new OracleRegistry(), Contexts("s1", "s2"));
            Assert.AreEqual(2, run.sessionCount);
            Assert.AreEqual(0, run.oracleCount);
            Assert.AreEqual(0, run.results.Count);
            Assert.AreEqual(OracleRunResults.CurrentSchemaVersion, run.schemaVersion);
        }

        [Test]
        public void Runner_NullResult_CountsAsSkip_NotPassOrFail()
        {
            var reg = new OracleRegistry();
            reg.Register(new StubOracle("maybe", ctx => ctx.SessionId == "s1"
                ? new OracleResult { passed = true } : null));

            var run = OracleRunner.Run(reg, Contexts("s1", "s2"));

            Assert.AreEqual(1, run.executedEvaluations);
            Assert.AreEqual(1, run.skippedCount);
            Assert.AreEqual(1, run.passedCount);
            Assert.AreEqual(0, run.failedCount);
        }

        [Test]
        public void Runner_ThrowingOracle_IsolatedAndRecorded_OthersRun()
        {
            var reg = new OracleRegistry();
            reg.Register(new StubOracle("broken", _ => throw new InvalidOperationException("boom")));
            reg.Register(new StubOracle("healthy"));

            var run = OracleRunner.Run(reg, Contexts("s1"));

            Assert.AreEqual(1, run.errorCount);
            Assert.AreEqual(1, run.passedCount, "healthy oracle must still run after the broken one");
            OracleResult err = run.results.Find(r => r.oracleName == "broken");
            Assert.IsFalse(err.passed);
            StringAssert.StartsWith("oracle-error:", err.reason);
            Assert.AreEqual(OracleResult.SeverityWarning, err.severity,
                "a broken ORACLE is a warning about the oracle, never a critical game verdict");
        }

        [Test]
        public void Runner_StampsIdentityAndDefaults()
        {
            var reg = new OracleRegistry();
            reg.Register(new StubOracle("plain", _ => new OracleResult { passed = false }));

            var run = OracleRunner.Run(reg, Contexts("s9"));
            OracleResult r = run.results[0];

            Assert.AreEqual("plain", r.oracleName, "runner owns identity stamping");
            Assert.AreEqual("s9", r.sessionId);
            Assert.AreEqual(OracleResult.SeverityInfo, r.severity, "empty severity defaults to info");
            Assert.IsNotEmpty(r.timestampUtc);
            Assert.AreEqual(run.generatedUtc, r.timestampUtc, "one stamp per run");
        }

        [Test]
        public void Runner_IsDeterministic_ExceptTimestamps()
        {
            OracleRegistry Make()
            {
                var reg = new OracleRegistry();
                reg.Register(new StubOracle("a", ctx => new OracleResult
                { passed = ctx.SessionId != "s2", reason = "r-" + ctx.SessionId }));
                return reg;
            }
            var a = OracleRunner.Run(Make(), Contexts("s1", "s2"));
            var b = OracleRunner.Run(Make(), Contexts("s1", "s2"));
            a.generatedUtc = b.generatedUtc = "";
            foreach (var r in a.results) r.timestampUtc = "";
            foreach (var r in b.results) r.timestampUtc = "";
            Assert.AreEqual(JsonUtility.ToJson(a), JsonUtility.ToJson(b));
        }

        // ------------------------------------------------- serialization/store

        [Test]
        public void Results_SerializationRoundTrips_WithEvidence()
        {
            var reg = new OracleRegistry();
            reg.Register(new StubOracle("ev", _ => new OracleResult
            {
                passed = false,
                severity = OracleResult.SeverityCritical,
                reason = "example failure",
                evidence = new List<string> { "maxDeviation=3.2", "file=validation.json" }
            }));
            var run = OracleRunner.Run(reg, Contexts("s1"));

            var back = JsonUtility.FromJson<OracleRunResults>(JsonUtility.ToJson(run, true));
            Assert.AreEqual(1, back.failedCount);
            Assert.AreEqual(2, back.results[0].evidence.Count);
            Assert.AreEqual("maxDeviation=3.2", back.results[0].evidence[0]);
            Assert.AreEqual(OracleResult.SeverityCritical, back.results[0].severity);
        }

        [Test]
        public void Store_RoundTrips_AtRoot()
        {
            string dir = Directory.CreateDirectory(Path.Combine(
                Path.GetTempPath(), "unityqa-test-" + Guid.NewGuid().ToString("N"))).FullName;
            try
            {
                var run = OracleRunner.Run(new OracleRegistry(), Contexts("s1"));
                string path = OracleResultStore.Save(run, dir);
                Assert.AreEqual(Path.Combine(dir, OracleResultStore.FileName), path);
                var back = OracleResultStore.Load(dir);
                Assert.IsNotNull(back);
                Assert.AreEqual(1, back.sessionCount);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        // ------------------------------------------------ manager integration

        [Test]
        public void ReplayManager_RunQualityOracles_EndToEnd_OnTempRoot()
        {
            string root = Directory.CreateDirectory(Path.Combine(
                Path.GetTempPath(), "unityqa-test-" + Guid.NewGuid().ToString("N"))).FullName;
            var go = new GameObject("[QA-OracleTest]");
            try
            {
                // Minimal real session folder so the dataset chain has one row.
                var config = ScriptableObject.CreateInstance<Core.QAConfig>();
                var session = new Core.QASessionInfo("OracleLevel", () => 0f, DateTime.UtcNow, "u", "a");
                string folder = Directory.CreateDirectory(
                    Path.Combine(root, "20260803-100000_orac0000")).FullName;
                Logging.SessionManifest.WriteClosed(session, config, folder, 5f, 10);
                ScriptableObject.DestroyImmediate(config);

                ReplayManager manager = go.AddComponent<ReplayManager>();
                manager.OracleRegistry.Register(new StubOracle("always-pass"));

                OracleRunResults run = manager.RunQualityOracles(root);

                Assert.AreEqual(1, run.sessionCount);
                Assert.AreEqual(1, run.passedCount);
                Assert.IsTrue(File.Exists(Path.Combine(root, OracleResultStore.FileName)));
                // The courtesy chain must have materialized the upstream docs too:
                Assert.IsTrue(File.Exists(Path.Combine(root, FeatureDatasetStore.JsonFileName)));
                Assert.IsTrue(File.Exists(Path.Combine(root, Analysis.AnalysisStore.FileName)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                try { Directory.Delete(root, true); } catch { }
            }
        }
    }
}
