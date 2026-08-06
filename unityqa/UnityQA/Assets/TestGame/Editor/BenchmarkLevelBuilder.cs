// -----------------------------------------------------------------------------
// BenchGame (Editor) — BenchmarkLevelBuilder.cs                  (M5 Slice C)
//
// PURPOSE
//   Deterministic, code-defined construction of the v2 benchmark scene
//   (Level_Benchmark.unity): spawn platform → one platforming section over
//   void → exit door, with static spikes. Same D-006 philosophy as the
//   baseline builder — the level IS this code, reviewable in a diff — plus
//   one improvement closing the QA-SETUP "pitfall": this builder ALSO
//   assembles the complete instrumented [QA] object, so the scene is
//   playable AND observable the moment it is built.
//
//   Geometry respects GUT-SPEC kinematics: every gap ≤ 3 tiles (max ≈ 5.3),
//   every rise ≤ 2 (max 2.2) — a deterministic success route exists; spikes
//   are avoidable by jumping. Menu: BenchGame ▸ Build Level_Benchmark.
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
    public static class BenchmarkLevelBuilder
    {
        private const string ScenePath = "Assets/TestGame/Levels/Level_Benchmark.unity";
        private const string TilePath = "Assets/TestGame/Tiles/GroundTile.asset";
        private const string SpritePath = "Assets/TestGame/Tiles/White.png";
        private const string ConfigPath = "Assets/UnityQA/Config/DefaultQAConfig.asset";

        [MenuItem("BenchGame/Build Level_Benchmark From Scratch")]
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
            Platform(24, 27, 0);  // gap 3, drop 1              jump
            Platform(31, 34, 0);  // gap 3 → exit platform      jump

            // --- spikes (static, avoidable) ------------------------------------
            MakeSpike(new Vector2(12.5f, 1.5f), sprite, "Spike_A");  // on platform 2's far edge
            MakeSpike(new Vector2(25.5f, 1.5f), sprite, "Spike_B");  // mid platform 4

            // --- spawn, exit, run controller -----------------------------------
            var spawn = new GameObject("SpawnPoint");
            spawn.transform.position = new Vector3(2f, 2f, 0f);

            var exitGo = new GameObject("ExitDoor");
            exitGo.transform.position = new Vector3(33.5f, 2f, 0f);
            var exitSr = exitGo.AddComponent<SpriteRenderer>();
            exitSr.sprite = sprite;
            exitSr.color = new Color(0.2f, 0.85f, 0.3f, 1f);
            exitGo.transform.localScale = new Vector3(0.8f, 2f, 1f);
            exitGo.AddComponent<BoxCollider2D>().isTrigger = true;
            exitGo.AddComponent<ExitDoor>();

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

            // --- fully instrumented [QA] (closes the QA-SETUP pitfall) ---------
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

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[BenchGame] Level_Benchmark built and saved → {ScenePath}. " +
                      "Route: 4 jumps, 2 avoidable spikes, exit door at the end. Press Play, F9 to record.");
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
