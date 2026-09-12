// -----------------------------------------------------------------------------
// BenchGame (Editor) — PlantedBugLevelBuilder.cs                  (M6 Slice A)
//
// PURPOSE
//   Deterministic, code-defined construction of the planted-bug benchmark
//   scene (Level_PlantedBugs_A.unity): Level_Benchmark's exact geometry and
//   instrumentation, plus FOUR deliberate, documented defects. The level IS
//   this code (D-006); the defects ARE the deviation blocks below, each
//   tagged with its BENCHMARK.md ground-truth ID. docs/BENCHMARK.md is the
//   binding answer key — nothing is planted here that is not recorded there,
//   and nothing is recorded there that is not planted here (FR-1.16).
//
// WHY A SIBLING BUILDER, NOT A PARAMETERIZED SHARED CORE
//   Same reason LevelBaselineBuilder and BenchmarkLevelBuilder already stand
//   separate: under D-006 a level's builder is the level's version-controlled
//   source, and this file diffed against BenchmarkLevelBuilder.cs IS the
//   complete, reviewable list of planted deviations. A shared parameterized
//   core would hide exactly the information this benchmark exists to pin
//   down, and would put the clean benchmark at risk from planted-level edits
//   (BUG rule: Level_Benchmark stays byte-untouched).
//
// THE FOUR PLANTS (calibration rules SRS §13: reachable by an ordinary
// player, invisible to a casual glance, scene-local only, EditorOnly marker
// per site — runtime never sees the answer key)
//   PB-001 collider gap    — platform 4's far tile (cell x27) is painted on a
//                            render-only tilemap: looks identical, has no
//                            collider. Running onto it drops the player into
//                            the void → OutOfBounds.
//   PB-002 soft-lock pit   — the first gap (x7–9) gets a sealed basin: floor
//                            at y=-3, walls flush with the platform edges.
//                            Basin depth 3 u > max jump height 2.2 u
//                            (GUT-SPEC), floor top y=-2 sits above killY=-5:
//                            the player survives, keeps receiving input, and
//                            can never make progress again.
//   PB-003 spike on path   — a third spike (Spike_C) on platform 3's walking
//                            surface, mid-route: a clean-level golden run
//                            crossing that surface meets it → SpikeDeath.
//   PB-004 missing trigger — the ExitDoor is visually intact but its trigger
//                            collider is DISABLED (the SRS §13 BUG-004
//                            planting method): the run can no longer end in
//                            Success.
//
// DETERMINISM
//   Static geometry only, no randomness, no timers — every plant is a
//   once-authored scene fact, so planted runs replay exactly like clean ones
//   (FR-1.19 holds; the defects change WHAT happens, never whether it
//   reproduces). Menu: BenchGame ▸ Build Level_PlantedBugs_A From Scratch.
// -----------------------------------------------------------------------------

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityQA.Adapters;
using UnityQA.Core;
using UnityQA.Logging;

namespace BenchGame.EditorTools
{
    public static class PlantedBugLevelBuilder
    {
        public const string ScenePath = "Assets/TestGame/Levels/Level_PlantedBugs_A.unity";
        private const string TilePath = "Assets/TestGame/Tiles/GroundTile.asset";
        private const string SpritePath = "Assets/TestGame/Tiles/White.png";
        private const string ConfigPath = "Assets/UnityQA/Config/DefaultQAConfig.asset";

        /// <summary>Ground-truth marker names, exactly as they appear in the
        /// scene (SRS §13 rule 5). Tests and BENCHMARK.md cite these.</summary>
        public static readonly string[] MarkerNames =
        {
            "PB_001_ColliderGap",
            "PB_002_SoftLockPit",
            "PB_003_SpikeOnGoldenPath",
            "PB_004_MissingExitTrigger",
        };

        [MenuItem("BenchGame/Build Level_PlantedBugs_A From Scratch")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
            var tile = AssetDatabase.LoadAssetAtPath<TileBase>(TilePath);
            if (sprite == null || tile == null)
            {
                Debug.LogError("[BenchGame] White.png / GroundTile.asset missing — cannot build.");
                return;
            }
            int ground = EnsureLayer("Ground");

            // --- platforms (tilemap over void) --------------------------------
            var gridGo = new GameObject("Grid");
            gridGo.AddComponent<Grid>();
            var mapGo = new GameObject("Ground_Tilemap");
            mapGo.transform.SetParent(gridGo.transform, false);
            mapGo.layer = ground;
            var map = mapGo.AddComponent<Tilemap>();
            mapGo.AddComponent<TilemapRenderer>();
            var mapBody = mapGo.AddComponent<Rigidbody2D>();
            mapBody.bodyType = RigidbodyType2D.Static;
            mapGo.AddComponent<TilemapCollider2D>().compositeOperation = Collider2D.CompositeOperation.Merge;
            mapGo.AddComponent<CompositeCollider2D>();

            void Platform(int x0, int x1, int y)
            {
                for (int x = x0; x <= x1; x++) map.SetTile(new Vector3Int(x, y, 0), tile);
            }
            Platform(0, 6, 0);    // spawn platform            (top y = 1)
            Platform(10, 13, 0);  // gap 3                      route: jump
            Platform(17, 20, 1);  // gap 3, rise 1              jump
            Platform(24, 26, 0);  // DEVIATION (PB-001): clean is Platform(24,27,0);
                                  // cell x27 moves to the render-only map below.
            Platform(31, 34, 0);  // gap 3 → exit platform      jump

            // --- DEVIATION (PB-002): sealed soft-lock basin under gap 1 --------
            // Real collider tiles — the defect is the geometry, not the physics:
            // walls flush with platform 1's right edge (x6) and platform 2's
            // left edge (x10), floor across the gap at y=-3. Wall top (y=1)
            // minus floor top (y=-2) = 3 u > max jump height 2.2 u (GUT-SPEC):
            // enterable by simply walking off platform 1, inescapable forever.
            // Floor top y=-2 stays above killY=-5 — the player does NOT die;
            // that is precisely what distinguishes a soft lock from PB-001.
            for (int y = -1; y >= -3; y--)
            {
                map.SetTile(new Vector3Int(6, y, 0), tile);   // left wall
                map.SetTile(new Vector3Int(10, y, 0), tile);  // right wall
            }
            Platform(7, 9, -3);                               // basin floor

            // --- DEVIATION (PB-001): render-only ground, no collider -----------
            // A second tilemap using the SAME tile — visually indistinguishable
            // from real ground — but with no collider components and NOT on the
            // Ground layer. Cell x27 (clean platform 4's far tile, the natural
            // take-off cell for the last gap) is painted ONLY here: running
            // onto it drops the player through into the void → OutOfBounds.
            // A deliberate jump from x26 still clears to the exit platform
            // (reach ≈ 32.3 u > required 30.55 u), so the rest of the level
            // stays reachable for PB-004.
            var fakeGo = new GameObject("Ground_Tilemap_Visual");
            fakeGo.transform.SetParent(gridGo.transform, false);
            var fakeMap = fakeGo.AddComponent<Tilemap>();
            fakeGo.AddComponent<TilemapRenderer>();
            fakeMap.SetTile(new Vector3Int(27, 0, 0), tile);

            // --- spikes (static; Spike_A/B as in the clean benchmark) ----------
            MakeSpike(new Vector2(12.5f, 1.5f), sprite, "Spike_A");  // on platform 2's far edge
            MakeSpike(new Vector2(25.5f, 1.5f), sprite, "Spike_B");  // mid platform 4

            // --- DEVIATION (PB-003): spike on the golden path ------------------
            // Clean platform 3 (x17–20, walking surface y=2) carries no hazard;
            // this one sits mid-surface, in the stretch every surface-crossing
            // run traverses between landing (~x17–18) and the next take-off
            // (~x19–20). A clean-level golden run replayed here meets it →
            // SpikeDeath where the original was Success. Jumpable (apex 2.2 u
            // clears the 0.8 u spike) so the route beyond stays reachable.
            MakeSpike(new Vector2(18.5f, 2.5f), sprite, "Spike_C");

            // --- spawn, exit, run controller -----------------------------------
            var spawn = new GameObject("SpawnPoint");
            spawn.transform.position = new Vector3(2f, 2f, 0f);

            var exitGo = new GameObject("ExitDoor");
            exitGo.transform.position = new Vector3(33.5f, 2f, 0f);
            var exitSr = exitGo.AddComponent<SpriteRenderer>();
            exitSr.sprite = sprite;
            exitSr.color = new Color(0.2f, 0.85f, 0.3f, 1f);
            exitGo.transform.localScale = new Vector3(0.8f, 2f, 1f);
            var exitCol = exitGo.AddComponent<BoxCollider2D>();
            exitCol.isTrigger = true;
            exitGo.AddComponent<ExitDoor>();

            // --- DEVIATION (PB-004): the exit trigger never fires ---------------
            // The SRS §13 BUG-004 planting method verbatim: component present,
            // door looks normal, trigger collider DISABLED. OnTriggerEnter2D
            // can never fire, so the run can never end in Success — the session
            // simply never completes.
            exitCol.enabled = false;

            // --- player ---------------------------------------------------------
            var player = new GameObject("Player") { tag = "Player" };
            player.transform.position = spawn.transform.position;
            var sr = player.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = new Color(1f, 0.6f, 0.15f, 1f);
            sr.sortingOrder = 1;
            var rb = player.AddComponent<Rigidbody2D>();
            rb.gravityScale = 3f;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            player.AddComponent<BoxCollider2D>().size = new Vector2(0.9f, 0.9f);
            var check = new GameObject("GroundCheck");
            check.transform.SetParent(player.transform, false);
            check.transform.localPosition = new Vector3(0f, -0.45f, 0f);
            var controller = player.AddComponent<PlayerController2D>();
            var so = new SerializedObject(controller);
            so.FindProperty("runSpeed").floatValue = 6f;
            so.FindProperty("jumpHeight").floatValue = 2.2f;
            so.FindProperty("groundCheck").objectReferenceValue = check.transform;
            so.FindProperty("groundCheckSize").vector2Value = new Vector2(0.55f, 0.10f);
            so.FindProperty("groundLayer").intValue = 1 << ground;
            so.ApplyModifiedPropertiesWithoutUndo();

            var runGo = new GameObject("GameRun");
            var run = runGo.AddComponent<GameRun>();
            var runSo = new SerializedObject(run);
            runSo.FindProperty("spawnPoint").objectReferenceValue = spawn.transform;
            runSo.FindProperty("killY").floatValue = -5f;
            runSo.ApplyModifiedPropertiesWithoutUndo();

            // --- camera ---------------------------------------------------------
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.transform.position = new Vector3(2f, 3f, -10f);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 6f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.14f, 0.15f, 0.19f, 1f);
            camGo.AddComponent<AudioListener>();
            var follow = camGo.AddComponent<FollowCamera>();
            var camSo = new SerializedObject(follow);
            camSo.FindProperty("target").objectReferenceValue = player.transform;
            camSo.FindProperty("smoothTime").floatValue = 0.15f;
            camSo.FindProperty("offset").vector2Value = new Vector2(0f, 1f);
            camSo.ApplyModifiedPropertiesWithoutUndo();

            // --- fully instrumented [QA] (same stack as the clean benchmark) ---
            QAConfig config = AssetDatabase.LoadAssetAtPath<QAConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<QAConfig>();
                System.IO.Directory.CreateDirectory("Assets/UnityQA/Config");
                AssetDatabase.CreateAsset(config, ConfigPath);
                Debug.Log("[BenchGame] Created DefaultQAConfig.asset.");
            }
            var qa = new GameObject("[QA]");
            var runner = qa.AddComponent<QARunner>();
            var runnerSo = new SerializedObject(runner);
            runnerSo.FindProperty("config").objectReferenceValue = config;
            runnerSo.ApplyModifiedPropertiesWithoutUndo();
            qa.AddComponent<QALogger>();
            qa.AddComponent<BenchGameAdapter>();
            qa.AddComponent<QATelemetrySampler>();
            qa.AddComponent<QAInputRecorder>();
            qa.AddComponent<ReplayRecorder>();
            qa.AddComponent<ReplayPlayer>();
            qa.AddComponent<ReplayValidator>();
            qa.AddComponent<ReplayManager>();

            // --- ground-truth markers (SRS §13 rule 5) --------------------------
            // Empty, EditorOnly-tagged transforms at each defect site: stripped
            // from player builds by Unity, invisible in the Game view (no
            // renderer), never read by any runtime system — the answer key
            // exists for humans and for the EditMode ground-truth tests only.
            var markers = new GameObject("PlantedBugMarkers") { tag = "EditorOnly" };
            Marker(markers, MarkerNames[0], new Vector3(27.5f, 0.5f, 0f)); // fake tile cell
            Marker(markers, MarkerNames[1], new Vector3(8.5f, -1.5f, 0f)); // basin interior
            Marker(markers, MarkerNames[2], new Vector3(18.5f, 2.5f, 0f)); // Spike_C
            Marker(markers, MarkerNames[3], new Vector3(33.5f, 2f, 0f));   // dead door

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[BenchGame] Level_PlantedBugs_A built and saved → {ScenePath}. " +
                      "Planted: PB-001 collider gap (x27), PB-002 soft-lock pit (gap 1), " +
                      "PB-003 spike on golden path (x18.5), PB-004 dead exit trigger. " +
                      "Answer key: docs/BENCHMARK.md. Press Play, F9 to record.");
        }

        private static void Marker(GameObject parent, string name, Vector3 pos)
        {
            var m = new GameObject(name) { tag = "EditorOnly" };
            m.transform.SetParent(parent.transform, false);
            m.transform.position = pos;
        }

        private static void MakeSpike(Vector2 pos, Sprite sprite, string name)
        {
            var spike = new GameObject(name);
            spike.transform.position = pos;
            spike.transform.localScale = new Vector3(0.8f, 0.8f, 1f);
            var sr = spike.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = new Color(0.9f, 0.15f, 0.15f, 1f);
            spike.AddComponent<BoxCollider2D>().isTrigger = true;
            spike.AddComponent<SpikeHazard>();
        }

        private static int EnsureLayer(string name)
        {
            int existing = LayerMask.NameToLayer(name);
            if (existing != -1) return existing;
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            var tagManager = new SerializedObject(assets[0]);
            var layers = tagManager.FindProperty("layers");
            for (int i = 6; i < layers.arraySize; i++)
            {
                var slot = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(slot.stringValue))
                {
                    slot.stringValue = name;
                    tagManager.ApplyModifiedPropertiesWithoutUndo();
                    return i;
                }
            }
            return 0;
        }
    }
}
