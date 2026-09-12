// -----------------------------------------------------------------------------
// UnityQA Tests — PlantedBugLevelTests.cs                (M6 Slice A, EditMode)
//
// Ground-truth pins for the planted-bug benchmark. The suite builds
// Level_PlantedBugs_A with the real PlantedBugLevelBuilder (once, in
// OneTimeSetUp — the exact code path the menu item runs) and then asserts,
// against the LIVE scene, that every defect BENCHMARK.md documents actually
// exists as documented and that the clean benchmark was not touched:
//
//   PB-001 — cell x27 is render-only (visual map has it, collider map does
//            not; the visual map has no collider and is off the Ground layer)
//   PB-002 — the basin under gap 1 is sealed and DEEPER than the player's
//            authored jump height (inescapability by construction), with its
//            floor above the kill boundary (soft lock, not death)
//   PB-003 — a third SpikeHazard sits on platform 3's walking surface
//   PB-004 — the ExitDoor's trigger collider exists but is disabled
//
// These are GROUND-TRUTH tests ("the bug exists"), not detection tests
// ("the framework found the bug") — detection belongs to M6-B/C and must
// never be claimed here. Scene-side note: building swaps the open scene
// (same as clicking the menu item); the Test Runner restores your scene
// setup after the run, and teardown leaves an empty scene behind.
// -----------------------------------------------------------------------------

using System.IO;
using BenchGame;
using BenchGame.EditorTools;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace UnityQA.Tests
{
    public sealed class PlantedBugLevelTests
    {
        private const string CleanScenePath = "Assets/TestGame/Levels/Level_Benchmark.unity";

        private byte[] cleanBytesBefore;

        [OneTimeSetUp]
        public void BuildPlantedLevel()
        {
            cleanBytesBefore = File.Exists(CleanScenePath) ? File.ReadAllBytes(CleanScenePath) : null;
            PlantedBugLevelBuilder.Build(); // the menu item's exact code path
        }

        [OneTimeTearDown]
        public void LeaveEmptyScene()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        // ------------------------------------------------------------ helpers

        private static Tilemap FindMap(string name)
        {
            var go = GameObject.Find(name);
            Assert.IsNotNull(go, $"'{name}' missing from the built scene");
            var tilemap = go.GetComponent<Tilemap>();
            Assert.IsNotNull(tilemap, $"'{name}' has no Tilemap");
            return tilemap;
        }

        // ------------------------------------------------------- construction

        [Test]
        public void SceneAsset_IsSaved_AtCanonicalPath()
        {
            Assert.IsTrue(File.Exists(PlantedBugLevelBuilder.ScenePath),
                "builder must save Level_PlantedBugs_A.unity where BENCHMARK.md says it lives");
        }

        [Test]
        public void CleanBenchmark_IsByteUntouched_ByBuildingThePlantedLevel()
        {
            // The planted level is a SEPARATE scene; building it must never
            // rewrite the clean baseline (BENCHMARK.md calibration rule 3's
            // spirit: the clean level stays 100% clean).
            if (cleanBytesBefore == null)
                Assert.Ignore("Level_Benchmark.unity not present in this checkout — nothing to protect.");
            Assert.AreEqual(cleanBytesBefore, File.ReadAllBytes(CleanScenePath),
                "building Level_PlantedBugs_A modified Level_Benchmark.unity");
        }

        [Test]
        public void Markers_ExistForEveryPlantedBug_EditorOnly_AndInert()
        {
            foreach (string name in PlantedBugLevelBuilder.MarkerNames)
            {
                var marker = GameObject.Find(name);
                Assert.IsNotNull(marker, $"ground-truth marker '{name}' missing");
                Assert.AreEqual("EditorOnly", marker.tag,
                    $"'{name}' must be EditorOnly — stripped from builds, runtime never sees the answer key");
                // Inert: a bare transform. No renderer (not gameplay-visible),
                // no collider (no physics), no behaviours (no runtime signal).
                Assert.AreEqual(0, marker.GetComponents<Component>().Length - 1, // Transform only
                    $"'{name}' must carry no components beyond its Transform");
            }
        }

        // ------------------------------------------------------------- PB-001

        [Test]
        public void PB001_CellX27_IsRenderOnly_NoColliderNoGroundLayer()
        {
            Tilemap real = FindMap("Ground_Tilemap");
            Tilemap fake = FindMap("Ground_Tilemap_Visual");

            // The defect: the collider map stops at x26; x27 exists only visually.
            Assert.IsTrue(real.HasTile(new Vector3Int(26, 0, 0)), "platform 4 must end at x26 in the collider map");
            Assert.IsFalse(real.HasTile(new Vector3Int(27, 0, 0)), "x27 in the collider map would un-plant PB-001");
            Assert.IsTrue(fake.HasTile(new Vector3Int(27, 0, 0)), "x27 must LOOK solid (visual map)");

            // The visual map must be physically inert.
            Assert.IsNull(fake.GetComponent<TilemapCollider2D>(), "visual map must have no collider");
            Assert.IsNull(fake.GetComponent<Rigidbody2D>(), "visual map must have no rigidbody");
            Assert.AreNotEqual(LayerMask.NameToLayer("Ground"), fake.gameObject.layer,
                "visual map on the Ground layer could fool a layer-based query");
        }

        // ------------------------------------------------------------- PB-002

        [Test]
        public void PB002_Basin_IsSealed_AndDeeperThanJumpHeight_AboveKillY()
        {
            Tilemap real = FindMap("Ground_Tilemap");

            for (int x = 7; x <= 9; x++)
                Assert.IsTrue(real.HasTile(new Vector3Int(x, -3, 0)), $"basin floor missing at x={x}");
            for (int y = -1; y >= -3; y--)
            {
                Assert.IsTrue(real.HasTile(new Vector3Int(6, y, 0)), $"left wall missing at y={y}");
                Assert.IsTrue(real.HasTile(new Vector3Int(10, y, 0)), $"right wall missing at y={y}");
            }

            // Inescapability by construction: wall top (y=1, platform surface)
            // minus basin floor top (y=-2) must exceed the AUTHORED jump apex —
            // read from the scene's controller, not hardcoded, so a future
            // kinematics change re-judges the plant automatically.
            var controller = Object.FindFirstObjectByType<PlayerController2D>();
            Assert.IsNotNull(controller);
            const float wallTopY = 1f, basinFloorTopY = -2f;
            Assert.Greater(wallTopY - basinFloorTopY, controller.JumpHeight,
                "basin depth must exceed max jump height or PB-002 is escapable");

            // Soft lock, not death: the basin floor must sit ABOVE the kill
            // boundary, or the 'stuck' state would end as OutOfBounds (PB-001's
            // signature, not PB-002's).
            var run = Object.FindFirstObjectByType<GameRun>();
            Assert.IsNotNull(run);
            var killY = new UnityEditor.SerializedObject(run).FindProperty("killY").floatValue;
            Assert.Greater(basinFloorTopY, killY, "a basin below killY is a death pit, not a soft lock");
        }

        // ------------------------------------------------------------- PB-003

        [Test]
        public void PB003_ThirdSpike_SitsOnPlatform3WalkingSurface()
        {
            var spikes = Object.FindObjectsByType<SpikeHazard>(FindObjectsSortMode.None);
            Assert.AreEqual(3, spikes.Length,
                "planted level = clean level's two spikes + exactly one planted spike");

            var planted = System.Array.Find(spikes, s => s.gameObject.name == "Spike_C");
            Assert.IsNotNull(planted, "planted spike 'Spike_C' missing");
            Assert.AreEqual(18.5f, planted.transform.position.x, 0.001f);
            Assert.AreEqual(2.5f, planted.transform.position.y, 0.001f,
                "Spike_C must rest on platform 3's walking surface (top y=2)");
            Assert.IsTrue(planted.GetComponent<BoxCollider2D>().isTrigger,
                "spikes report through triggers (SpikeHazard contract)");
        }

        // ------------------------------------------------------------- PB-004

        [Test]
        public void PB004_ExitDoor_LooksNormal_ButTriggerColliderIsDisabled()
        {
            var door = Object.FindFirstObjectByType<ExitDoor>();
            Assert.IsNotNull(door, "the door must EXIST — PB-004 is a dead trigger, not a missing object");
            Assert.IsNotNull(door.GetComponent<SpriteRenderer>(), "the door must still LOOK normal");

            var col = door.GetComponent<BoxCollider2D>();
            Assert.IsNotNull(col, "collider present (object looks intact in the inspector)");
            Assert.IsTrue(col.isTrigger, "still configured as a trigger — only 'enabled' is the plant");
            Assert.IsFalse(col.enabled,
                "PB-004 IS this line: a disabled trigger collider (SRS §13 BUG-004 planting method)");
        }
    }
}
