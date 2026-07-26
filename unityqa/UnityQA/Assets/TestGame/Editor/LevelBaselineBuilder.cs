// -----------------------------------------------------------------------------
// BenchGame (Editor) — LevelBaselineBuilder.cs
//
// PURPOSE
//   Deterministic, code-defined construction of Level_Baseline's verification
//   geometry (and, as a safety net, of the entire scene).
//
// WHY IT EXISTS
//   The shipped Level_Baseline.unity contains the camera, player, and an EMPTY
//   tilemap. Tile data is deliberately NOT hand-authored as YAML: painting it
//   here through Unity's own Tilemap API and letting Unity serialize the result
//   guarantees a byte-correct scene on YOUR editor version. It also means the
//   level layout itself is version-controlled as readable code below — a
//   reproducibility property the benchmark methodology likes (SRS §2.4).
//
// HOW IT RUNS
//   1. AUTOMATICALLY: on first project load (InitializeOnLoadMethod), it opens
//      Level_Baseline, and if the tilemap is empty, paints the geometry and
//      saves the scene. You should not need to do anything.
//   2. MANUALLY: menu  BenchGame ▸ Paint Level Geometry (if empty)
//   3. RECOVERY:  menu  BenchGame ▸ Rebuild Level_Baseline From Scratch —
//      deletes and recreates EVERYTHING in the scene from code (camera, player,
//      grid, tilemap, layer setup). Use if the scene ever ends up broken.
//
// This is editor tooling for the GUT. It is not QA-framework code and it never
// references UnityQA.* (dependency rule, SRS §1.1 / NFR-1.3).
// -----------------------------------------------------------------------------

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace BenchGame.EditorTools
{
    public static class LevelBaselineBuilder
    {
        private const string ScenePath  = "Assets/TestGame/Levels/Level_Baseline.unity";
        private const string TilePath   = "Assets/TestGame/Tiles/GroundTile.asset";
        private const string SpritePath = "Assets/TestGame/Tiles/White.png";
        private const string SessionKey = "BenchGame.LevelBaselineBuilder.AutoRanThisSession";

        // ---------------------------------------------------------------------
        // 1. Auto-run on project load (once per editor session)
        // ---------------------------------------------------------------------
        [InitializeOnLoadMethod]
        private static void AutoEnsureOnLoad()
        {
            // delayCall: wait until the editor is fully initialized and assets
            // are imported; InitializeOnLoad itself runs too early to touch scenes.
            EditorApplication.delayCall += () =>
            {
                if (SessionState.GetBool(SessionKey, false)) return;
                SessionState.SetBool(SessionKey, true);
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                PaintIfEmpty();
            };
        }

        // ---------------------------------------------------------------------
        // 2. Paint verification geometry into the existing (empty) tilemap
        // ---------------------------------------------------------------------
        [MenuItem("BenchGame/Paint Level Geometry (if empty)")]
        public static void PaintIfEmpty()
        {
            var scene = EnsureSceneOpen();
            if (!scene.IsValid()) return;

            var tilemap = FindTilemap();
            if (tilemap == null)
            {
                Debug.LogWarning("[BenchGame] Ground_Tilemap not found. Run " +
                                 "'BenchGame ▸ Rebuild Level_Baseline From Scratch'.");
                return;
            }

            tilemap.CompressBounds();
            if (tilemap.GetUsedTilesCount() > 0)
            {
                Debug.Log("[BenchGame] Level geometry already present — nothing to do.");
                return;
            }

            var tile = AssetDatabase.LoadAssetAtPath<TileBase>(TilePath);
            if (tile == null)
            {
                Debug.LogError($"[BenchGame] Tile asset missing at {TilePath}.");
                return;
            }

            PaintGeometry(tilemap, tile);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[BenchGame] Level_Baseline geometry painted and saved. Press Play!");
        }

        // ---------------------------------------------------------------------
        // 3. Full recovery rebuild — recreates the entire scene from code
        // ---------------------------------------------------------------------
        [MenuItem("BenchGame/Rebuild Level_Baseline From Scratch")]
        public static void RebuildFromScratch()
        {
            var scene = EnsureSceneOpen();
            if (!scene.IsValid())
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            foreach (var root in scene.GetRootGameObjects())
                Object.DestroyImmediate(root);

            int groundLayer = EnsureGroundLayer();
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
            var tile   = AssetDatabase.LoadAssetAtPath<TileBase>(TilePath);
            if (sprite == null || tile == null)
            {
                Debug.LogError("[BenchGame] White.png sprite or GroundTile.asset missing — cannot rebuild.");
                return;
            }

            // --- Grid + Tilemap -------------------------------------------------
            var gridGo = new GameObject("Grid");
            gridGo.AddComponent<Grid>();

            var mapGo = new GameObject("Ground_Tilemap");
            mapGo.transform.SetParent(gridGo.transform, false);
            mapGo.layer = groundLayer;
            var tilemap = mapGo.AddComponent<Tilemap>();
            mapGo.AddComponent<TilemapRenderer>();
            var body = mapGo.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Static;
            var tileCollider = mapGo.AddComponent<TilemapCollider2D>();
            tileCollider.compositeOperation = Collider2D.CompositeOperation.Merge;
            mapGo.AddComponent<CompositeCollider2D>();

            PaintGeometry(tilemap, tile);

            // --- Player ---------------------------------------------------------
            var player = new GameObject("Player") { tag = "Player" };
            player.transform.position = new Vector3(2f, 2f, 0f);
            var sr = player.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = new Color(1f, 0.6f, 0.15f, 1f);
            sr.sortingOrder = 1;
            var rb = player.AddComponent<Rigidbody2D>();
            rb.gravityScale = 3f;                                     // GUT-SPEC constant
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            var box = player.AddComponent<BoxCollider2D>();
            box.size = new Vector2(0.9f, 0.9f);                       // GUT-SPEC constant

            var groundCheck = new GameObject("GroundCheck");
            groundCheck.transform.SetParent(player.transform, false);
            groundCheck.transform.localPosition = new Vector3(0f, -0.45f, 0f);

            var controller = player.AddComponent<PlayerController2D>();
            // Private serialized fields are set the editor-sanctioned way:
            var so = new SerializedObject(controller);
            so.FindProperty("runSpeed").floatValue = 6f;              // GUT-SPEC constant
            so.FindProperty("jumpHeight").floatValue = 2.2f;          // GUT-SPEC constant
            so.FindProperty("groundCheck").objectReferenceValue = groundCheck.transform;
            so.FindProperty("groundCheckSize").vector2Value = new Vector2(0.55f, 0.10f);
            so.FindProperty("groundLayer").intValue = 1 << groundLayer;
            so.ApplyModifiedPropertiesWithoutUndo();

            // --- Camera ---------------------------------------------------------
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

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[BenchGame] Level_Baseline fully rebuilt from code and saved. Press Play!");
        }

        // ---------------------------------------------------------------------
        // The level layout — THE verification geometry, as data (GUT-SPEC.md)
        //
        //   x 0        10   13    19-24     29-36      42   45
        //     |wall     [step 2h]  [gap 4w]  [gap 6w]   |3h| |wall
        //   Rulers: 2-step jumpable / 3-wall not; 4-gap clearable / 6-gap not
        //   (gap rulers are 4 and 6, not 4 and 5 — see MODULES.md D-005).
        // ---------------------------------------------------------------------
        private static void PaintGeometry(Tilemap map, TileBase tile)
        {
            void Fill(int x0, int x1, int y0, int y1)
            {
                for (int x = x0; x <= x1; x++)
                    for (int y = y0; y <= y1; y++)
                        map.SetTile(new Vector3Int(x, y, 0), tile);
            }

            Fill(0, 45, 0, 0);        // main floor row (surface at y = 1)

            // Carve the two gaps out of the main floor:
            for (int x = 20; x <= 23; x++) map.SetTile(new Vector3Int(x, 0, 0), null); // gap A: 4 wide
            for (int x = 30; x <= 35; x++) map.SetTile(new Vector3Int(x, 0, 0), null); // gap B: 6 wide

            Fill(0, 0, 1, 4);         // left boundary wall
            Fill(45, 45, 1, 4);       // right boundary wall

            Fill(10, 13, 1, 2);       // 2-tile-high step (top at y=3): JUMPABLE (apex 2.2)

            Fill(20, 23, -2, -2);     // gap A floor, 2 units down: jump-out-able
            Fill(19, 19, -2, -1);     // gap A left wall below floor level
            Fill(24, 24, -2, -1);     // gap A right wall below floor level

            Fill(30, 35, -2, -2);     // gap B floor (6-wide gap: NOT clearable)
            Fill(29, 29, -2, -1);     // gap B left wall
            Fill(36, 36, -2, -1);     // gap B right wall

            Fill(42, 42, 1, 3);       // 3-tile-high wall (top at y=4): NOT jumpable
        }

        // ---------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------
        private static Scene EnsureSceneOpen()
        {
            var active = SceneManager.GetActiveScene();
            if (active.path == ScenePath) return active;
            if (!System.IO.File.Exists(ScenePath))
            {
                Debug.LogWarning($"[BenchGame] Scene not found at {ScenePath}.");
                return default;
            }
            return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        private static Tilemap FindTilemap()
        {
            var go = GameObject.Find("Ground_Tilemap");
            return go != null ? go.GetComponent<Tilemap>() : null;
        }

        /// <summary>
        /// Returns the index of the 'Ground' layer, creating it in the first
        /// empty user slot if the TagManager somehow lacks it.
        /// </summary>
        private static int EnsureGroundLayer()
        {
            int existing = LayerMask.NameToLayer("Ground");
            if (existing != -1) return existing;

            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            var tagManager = new SerializedObject(assets[0]);
            var layers = tagManager.FindProperty("layers");
            for (int i = 6; i < layers.arraySize; i++)
            {
                var slot = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(slot.stringValue))
                {
                    slot.stringValue = "Ground";
                    tagManager.ApplyModifiedPropertiesWithoutUndo();
                    Debug.Log($"[BenchGame] Created 'Ground' layer in slot {i}.");
                    return i;
                }
            }
            Debug.LogError("[BenchGame] No free layer slot for 'Ground'.");
            return 0;
        }
    }
}
