// -----------------------------------------------------------------------------
// UnityQA Tests — TelemetryDerivationTests.cs               (M2 Slice C tests)
//
// Pins the movement-state classification and the gutSpec manifest path.
// DeriveState is the labeling function future datasets/ML will inherit —
// its mapping is a contract, so every branch gets pinned here.
// -----------------------------------------------------------------------------

using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityQA.Core;
using UnityQA.Logging;

namespace UnityQA.Tests
{
    public sealed class TelemetryDerivationTests
    {
        // ----------------------------- DeriveState ---------------------------

        [Test]
        public void Grounded_Still_IsIdle() =>
            Assert.AreEqual(QATelemetrySampler.StateIdle,
                QATelemetrySampler.DeriveState(Vector2.zero, grounded: true));

        [Test]
        public void Grounded_Moving_IsRun_BothDirections()
        {
            Assert.AreEqual(QATelemetrySampler.StateRun,
                QATelemetrySampler.DeriveState(new Vector2(6f, 0f), true));
            Assert.AreEqual(QATelemetrySampler.StateRun,
                QATelemetrySampler.DeriveState(new Vector2(-6f, 0f), true));
        }

        [Test]
        public void Airborne_MovingUp_IsRise()
        {
            Assert.AreEqual(QATelemetrySampler.StateRise,
                QATelemetrySampler.DeriveState(new Vector2(0f, 11.4f), false));
        }

        [Test]
        public void Airborne_MovingDown_OrApex_IsFall()
        {
            Assert.AreEqual(QATelemetrySampler.StateFall,
                QATelemetrySampler.DeriveState(new Vector2(0f, -5f), false));
            // Exactly at apex (vy == 0, airborne): classified as fall — pinned
            // so the boundary can never silently flip between builds.
            Assert.AreEqual(QATelemetrySampler.StateFall,
                QATelemetrySampler.DeriveState(new Vector2(3f, 0f), false));
        }

        [Test]
        public void Airborne_WinsOverHorizontal()
        {
            // Running sideways while airborne is still rise/fall, never "run".
            Assert.AreEqual(QATelemetrySampler.StateRise,
                QATelemetrySampler.DeriveState(new Vector2(6f, 2f), false));
        }

        [Test]
        public void JitterBelowEpsilon_ReadsAsIdle()
        {
            Assert.AreEqual(QATelemetrySampler.StateIdle,
                QATelemetrySampler.DeriveState(new Vector2(0.005f, 0f), true));
        }

        // --------------------- session.json gutSpec (Slice C) ----------------

        [Test]
        public void Manifest_WithGutSpec_WritesAdapterValues_AndSource()
        {
            string dir = Directory.CreateDirectory(Path.Combine(
                Path.GetTempPath(), "unityqa-test-" + Guid.NewGuid().ToString("N"))).FullName;
            var config = ScriptableObject.CreateInstance<QAConfig>();
            try
            {
                var session = new QASessionInfo("L", () => 0f, DateTime.UtcNow, "u", "a");
                var gut = new GutSpecData { runSpeed = 6f, jumpHeight = 2.2f, gravityScale = 3f };

                SessionManifest.WriteOpen(session, config, dir, gut);

                var m = JsonUtility.FromJson<SessionManifest.Manifest>(
                    File.ReadAllText(Path.Combine(dir, SessionManifest.FileName)));
                Assert.AreEqual("adapter", m.gutSpecSource);
                Assert.AreEqual(6f, m.gutSpec.runSpeed, 1e-4f);
                Assert.AreEqual(2.2f, m.gutSpec.jumpHeight, 1e-4f);
                Assert.AreEqual(3f, m.gutSpec.gravityScale, 1e-4f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Test]
        public void Manifest_WithoutGutSpec_KeepsFrozenSliceBBehavior()
        {
            string dir = Directory.CreateDirectory(Path.Combine(
                Path.GetTempPath(), "unityqa-test-" + Guid.NewGuid().ToString("N"))).FullName;
            var config = ScriptableObject.CreateInstance<QAConfig>();
            try
            {
                SessionManifest.WriteOpen(
                    new QASessionInfo("L", () => 0f, DateTime.UtcNow, "u", "a"), config, dir);

                var m = JsonUtility.FromJson<SessionManifest.Manifest>(
                    File.ReadAllText(Path.Combine(dir, SessionManifest.FileName)));
                Assert.AreEqual("pending-slice-c", m.gutSpecSource);
                Assert.AreEqual(0f, m.gutSpec.runSpeed);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
