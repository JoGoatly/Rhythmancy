using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using RhythmWitchClone.Core;
using RhythmWitchClone.Gameplay;

namespace RhythmWitchClone.Levels
{
    /// <summary>
    /// Persistenter Manager (Singleton, DontDestroyOnLoad) für Kapitel und Level.
    ///
    /// Aufbau:
    /// - Pro KAPITEL eine Szene (z.B. Kapitel 1 = Gras-Szene, Kapitel 2 = Wüsten-Szene). Alle Level
    ///   eines Kapitels benutzen dieselbe Szene.
    /// - Pro LEVEL: Name + Liste an Gegnern (Prefab + Anzahl). Beim Laden des Levels werden die
    ///   Gegner zufällig im Spielfeld verteilt.
    /// - Pro KAPITEL "Map Settings": Bäume, Steine, Büsche (2D-Bilder), die bei jedem Level-Start
    ///   zufällig platziert werden - mit zufälliger Größe, Helligkeit und Spiegelung.
    ///   Bäume bilden einen dichten Wald am Rand des Spielfelds (mit Hitbox = natürliche Absperrung),
    ///   Steine und Büsche liegen ohne Hitbox im Inneren verteilt.
    ///
    /// Freischalt-Regeln:
    /// - Kapitel 1 ist immer frei, jedes weitere erst, wenn ALLE Level des vorherigen geschafft sind.
    /// - Level 1 eines Kapitels ist frei, sobald das Kapitel frei ist; jedes weitere erst, wenn das
    ///   vorherige geschafft wurde.
    ///
    /// Platzierung: auf ein GameObject in der allerersten Szene (neben dem AudioManager).
    /// </summary>
    public class LevelManager : MonoBehaviour
    {
        public static LevelManager Instance { get; private set; }

        [Serializable]
        public class EnemySpawn
        {
            [Tooltip("Gegner-Prefab (mit EnemyController).")]
            public GameObject enemyPrefab;
            [Tooltip("Wie viele Gegner dieses Typs im Level spawnen.")]
            public int count = 1;
        }

        [Serializable]
        public class Level
        {
            public string levelName;
            [Tooltip("Gegner, die beim Start dieses Levels zufällig im Spielfeld verteilt werden.")]
            public List<EnemySpawn> enemies = new List<EnemySpawn>();
        }

        [Serializable]
        public class MapSettings
        {
            [Header("Spielfeld")]
            [Tooltip("Mitte des Spielfelds in der Szene (Y = Höhe des Bodens).")]
            public Vector3 mapCenter = Vector3.zero;
            [Tooltip("Gesamtgröße des Spielfelds in X/Z (inklusive Wald am Rand).")]
            public Vector2 mapSize = new Vector2(40f, 40f);
            [Tooltip("In diesem Radius um den Spieler wird nichts platziert (keine Steine/Büsche).")]
            public float clearRadiusAroundPlayer = 4f;
            [Tooltip("Gegner spawnen mindestens so weit vom Spieler entfernt.")]
            public float enemyMinDistanceFromPlayer = 8f;
            [Tooltip("Unsichtbare Wand ganz außen um das Spielfeld, falls doch eine Lücke im Wald ist.")]
            public bool addOuterWall = true;
            [Tooltip("Am unteren Rand (zur Kamera hin) verdecken die Bäume den Spieler, weil ihre Bilder nach oben ragen. " +
                     "Um so viele Einheiten wird die untere Grenze des begehbaren Bereichs nach oben (weg von der Kamera) verschoben - " +
                     "dort steht eine unsichtbare Wand. Etwa so groß wie die Baumhöhe wählen.")]
            public float bottomBoundaryOffset = 3f;

            [Header("Wald am Rand (mit Hitbox)")]
            public List<Sprite> treeSprites = new List<Sprite>();
            [Tooltip("Wie breit der Waldstreifen am Rand ist.")]
            public float forestDepth = 6f;
            [Tooltip("Abstand zwischen den Bäumen - kleiner = dichterer Wald. Ca. halbe Baumbreite ist ein guter Startwert.")]
            public float treeSpacing = 1.5f;
            [Tooltip("Wie stark die Bäume zufällig vom Raster abweichen (0 = exaktes Raster, 1 = sehr unregelmäßig).")]
            [Range(0f, 1f)] public float treeRandomOffset = 0.8f;
            [Tooltip("Wie stark die Waldkante wellig in die Lichtung hineinragt (0 = gerade Linie, 1 = stark gewellt).")]
            [Range(0f, 1f)] public float forestEdgeWaviness = 0.6f;
            [Tooltip("Wie lang die Wellen der Waldkante sind - kleiner = längere, sanftere Wellen.")]
            public float forestEdgeNoiseScale = 0.12f;
            [Tooltip("Dichte der Bäume an der inneren Waldkante (1 = genauso dicht wie tief im Wald, kleiner = lichter, einzelne Bäume am Rand).")]
            [Range(0f, 1f)] public float forestEdgeDensity = 0.4f;
            [Tooltip("Zufällige Höhe in Welt-Einheiten (min / max) - unabhängig davon, wie groß das Bild in Pixeln ist. Zum Vergleich: der Spieler ist ca. 1-2 Einheiten hoch.")]
            public Vector2 treeHeightRange = new Vector2(3f, 6f);
            [Tooltip("Radius der Baum-Hitbox in Welt-Einheiten (nur der Stamm).")]
            public float treeTrunkRadius = 0.4f;
            [Tooltip("Höhe der Baum-Hitbox.")]
            public float treeColliderHeight = 3f;

            [Header("Steine (ohne Hitbox)")]
            public List<Sprite> rockSprites = new List<Sprite>();
            public int rockCount = 15;
            [Tooltip("Zufällige Höhe in Welt-Einheiten (min / max).")]
            public Vector2 rockHeightRange = new Vector2(0.3f, 1f);

            [Header("Büsche (ohne Hitbox)")]
            public List<Sprite> bushSprites = new List<Sprite>();
            public int bushCount = 25;
            [Tooltip("Zufällige Höhe in Welt-Einheiten (min / max).")]
            public Vector2 bushHeightRange = new Vector2(0.5f, 1.6f);

            [Header("Farbe")]
            [Tooltip("Zufällige Helligkeit (1 = Originalbild, kleiner = dunkler, größer als 1 = heller, z.B. 0.8 / 1.15).")]
            public Vector2 brightnessRange = new Vector2(0.75f, 1f);
        }

        [Serializable]
        public class Chapter
        {
            public string chapterName;
            [Tooltip("Exakter Szenen-Dateiname (ohne .unity) - gilt für ALLE Level dieses Kapitels.")]
            public string sceneName;
            public List<Level> levels = new List<Level>();
            public MapSettings mapSettings = new MapSettings();
        }

        [Header("Kapitel (Reihenfolge = Spielreihenfolge)")]
        [SerializeField] private List<Chapter> chapters = new List<Chapter>();

        [Header("Umgebung")]
        [Tooltip("Optional, aber für den fertigen Build empfohlen: ein Material mit Shader 'Universal Render Pipeline/Unlit', " +
                 "Alpha Clipping AN und Render Face 'Both'. Leer = wird automatisch erzeugt (funktioniert sicher im Editor).")]
        [SerializeField] private Material propMaterial;

        public IReadOnlyList<Chapter> Chapters => chapters;

        public int CurrentChapterIndex { get; private set; } = -1;
        public int CurrentLevelIndex { get; private set; } = -1;

        // Eigenes Material pro Bild-Textur + Helligkeitsstufe (siehe GetPropMaterial).
        private readonly Dictionary<(Texture, int), Material> _propMaterials = new Dictionary<(Texture, int), Material>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.Log($"LevelManager: Zweiter LevelManager in Szene '{gameObject.scene.name}' wurde entfernt - es gelten die Einstellungen " +
                          $"des LevelManagers aus Szene '{Instance.gameObject.scene.name}' (bzw. dem zuerst geladenen). Am besten beide als dasselbe Prefab benutzen.");
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            if (Instance == this) SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        // ------------------------------------------------------------------
        // Freischaltung / Fortschritt
        // ------------------------------------------------------------------

        public bool IsChapterUnlocked(int chapterIndex)
        {
            if (chapterIndex == 0) return true;
            if (chapterIndex < 0 || chapterIndex >= chapters.Count) return false;

            return IsChapterCompleted(chapterIndex - 1);
        }

        public bool IsChapterCompleted(int chapterIndex)
        {
            if (chapterIndex < 0 || chapterIndex >= chapters.Count) return false;

            Chapter chapter = chapters[chapterIndex];
            if (chapter.levels.Count == 0) return false;

            for (int i = 0; i < chapter.levels.Count; i++)
            {
                if (!IsLevelCompleted(chapterIndex, i)) return false;
            }
            return true;
        }

        public bool IsLevelUnlocked(int chapterIndex, int levelIndex)
        {
            if (!IsChapterUnlocked(chapterIndex)) return false;
            if (levelIndex == 0) return true;

            return IsLevelCompleted(chapterIndex, levelIndex - 1);
        }

        public bool IsLevelCompleted(int chapterIndex, int levelIndex)
        {
            return GameSession.IsLevelCompleted(GetLevelKey(chapterIndex, levelIndex));
        }

        public void MarkLevelCompleted(int chapterIndex, int levelIndex)
        {
            GameSession.MarkLevelCompleted(GetLevelKey(chapterIndex, levelIndex));
        }

        public void MarkCurrentLevelCompleted()
        {
            if (CurrentChapterIndex < 0 || CurrentLevelIndex < 0) return;
            MarkLevelCompleted(CurrentChapterIndex, CurrentLevelIndex);
        }

        private string GetLevelKey(int chapterIndex, int levelIndex) => $"level_completed_{chapterIndex}_{levelIndex}";

        /// <summary>
        /// Lädt die Kapitel-Szene und merkt sich das Level - Umgebung und Gegner werden nach dem
        /// Laden automatisch in OnSceneLoaded erzeugt.
        /// </summary>
        public void LoadLevel(int chapterIndex, int levelIndex)
        {
            if (chapterIndex < 0 || chapterIndex >= chapters.Count) return;

            Chapter chapter = chapters[chapterIndex];
            if (levelIndex < 0 || levelIndex >= chapter.levels.Count) return;

            CurrentChapterIndex = chapterIndex;
            CurrentLevelIndex = levelIndex;

            SceneManager.LoadScene(chapter.sceneName);
        }

        // ------------------------------------------------------------------
        // Level-Aufbau nach dem Laden
        // ------------------------------------------------------------------

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Kein Level über die Level-Auswahl gestartet (z.B. Kapitel-Szene direkt im Editor
            // gestartet)? Dann zum Testen automatisch Level 1 des passenden Kapitels nehmen.
            if (CurrentChapterIndex < 0 || CurrentChapterIndex >= chapters.Count ||
                chapters[CurrentChapterIndex].sceneName != scene.name)
            {
                int matchingChapter = chapters.FindIndex(c => c.sceneName == scene.name);
                if (matchingChapter < 0) return; // z.B. Safe Zone / Hauptmenü / Tutorial

                CurrentChapterIndex = matchingChapter;
                CurrentLevelIndex = 0;
                Debug.Log($"LevelManager: Szene '{scene.name}' ohne Level-Auswahl gestartet - benutze Level 1 von '{chapters[matchingChapter].chapterName}'.");
            }

            Chapter chapter = chapters[CurrentChapterIndex];
            if (CurrentLevelIndex < 0 || CurrentLevelIndex >= chapter.levels.Count)
            {
                Debug.LogWarning($"LevelManager: Kapitel '{chapter.chapterName}' hat kein Level mit Index {CurrentLevelIndex} - nichts erzeugt.");
                return;
            }

            GameObject root = new GameObject("GeneratedLevel");
            SceneManager.MoveGameObjectToScene(root, scene);

            SetupCameraSorting();

            PlayerController player = FindFirstObjectByType<PlayerController>();
            Vector3 playerPosition = player != null ? player.transform.position : chapter.mapSettings.mapCenter;

            GenerateEnvironment(chapter.mapSettings, root.transform, playerPosition);
            SpawnEnemies(chapter.levels[CurrentLevelIndex], chapter.mapSettings, root.transform, playerPosition);
        }

        private void GenerateEnvironment(MapSettings map, Transform root, Vector3 playerPosition)
        {
            Quaternion cameraRotation = Camera.main != null ? Camera.main.transform.rotation : Quaternion.identity;

            Transform forestParent = new GameObject("Forest").transform;
            forestParent.SetParent(root, false);
            Transform propsParent = new GameObject("Props").transform;
            propsParent.SetParent(root, false);

            float halfX = map.mapSize.x * 0.5f;
            float halfZ = map.mapSize.y * 0.5f;

            Chapter currentChapter = chapters[CurrentChapterIndex];
            if (map.treeSprites.Count == 0) Debug.LogWarning($"LevelManager: Kapitel '{currentChapter.chapterName}' hat keine Tree Sprites eingetragen.");
            if (map.rockSprites.Count == 0) Debug.LogWarning($"LevelManager: Kapitel '{currentChapter.chapterName}' hat keine Rock Sprites eingetragen.");
            if (map.bushSprites.Count == 0) Debug.LogWarning($"LevelManager: Kapitel '{currentChapter.chapterName}' hat keine Bush Sprites eingetragen.");

            // Wald: unregelmäßiges Raster, nur im Randstreifen. Die innere Waldkante ist per Rauschen
            // gewellt und wird zur Lichtung hin lichter, damit sie natürlich statt wie eine Mauer aussieht.
            if (map.treeSprites.Count > 0 && map.treeSpacing > 0.1f)
            {
                float noiseOffsetX = UnityEngine.Random.Range(0f, 1000f);
                float noiseOffsetZ = UnityEngine.Random.Range(0f, 1000f);

                for (float x = -halfX; x <= halfX; x += map.treeSpacing)
                {
                    for (float z = -halfZ; z <= halfZ; z += map.treeSpacing)
                    {
                        float distanceToEdge = Mathf.Min(halfX - Mathf.Abs(x), halfZ - Mathf.Abs(z));

                        float noise = Mathf.PerlinNoise(noiseOffsetX + x * map.forestEdgeNoiseScale, noiseOffsetZ + z * map.forestEdgeNoiseScale);
                        float localDepth = map.forestDepth * (1f + map.forestEdgeWaviness * noise);
                        if (distanceToEdge > localDepth) continue;

                        // Im inneren Drittel des Waldstreifens nach und nach lichter werden.
                        float depthRatio = distanceToEdge / localDepth;
                        if (depthRatio > 0.66f)
                        {
                            float keepChance = Mathf.Lerp(1f, map.forestEdgeDensity, (depthRatio - 0.66f) / 0.34f);
                            if (UnityEngine.Random.value > keepChance) continue;
                        }

                        float offset = map.treeSpacing * 0.5f * map.treeRandomOffset;
                        Vector3 position = map.mapCenter + new Vector3(
                            x + UnityEngine.Random.Range(-offset, offset),
                            0f,
                            z + UnityEngine.Random.Range(-offset, offset));

                        float height = UnityEngine.Random.Range(map.treeHeightRange.x, map.treeHeightRange.y);
                        GameObject tree = SpawnProp(map.treeSprites, position, height, map, forestParent, cameraRotation, "Tree");

                        CapsuleCollider collider = tree.AddComponent<CapsuleCollider>();
                        collider.radius = map.treeTrunkRadius;
                        collider.height = Mathf.Max(map.treeColliderHeight, collider.radius * 2f);
                        collider.center = new Vector3(0f, collider.height * 0.5f, 0f);
                        tree.isStatic = true;
                    }
                }
            }

            // Steine und Büsche im Inneren (ohne Hitbox).
            for (int i = 0; i < map.rockCount && map.rockSprites.Count > 0; i++)
            {
                if (TryGetInteriorPoint(map, playerPosition, map.clearRadiusAroundPlayer, out Vector3 position))
                {
                    float height = UnityEngine.Random.Range(map.rockHeightRange.x, map.rockHeightRange.y);
                    SpawnProp(map.rockSprites, position, height, map, propsParent, cameraRotation, "Rock");
                }
            }

            for (int i = 0; i < map.bushCount && map.bushSprites.Count > 0; i++)
            {
                if (TryGetInteriorPoint(map, playerPosition, map.clearRadiusAroundPlayer, out Vector3 position))
                {
                    float height = UnityEngine.Random.Range(map.bushHeightRange.x, map.bushHeightRange.y);
                    SpawnProp(map.bushSprites, position, height, map, propsParent, cameraRotation, "Bush");
                }
            }

            if (map.addOuterWall) CreateOuterWalls(map, root);
            CreateWalkableAreaWalls(map, root);

            Debug.Log($"LevelManager: {forestParent.childCount} Bäume und {propsParent.childCount} Steine/Büsche erzeugt " +
                      $"(Spielfeld-Mitte {map.mapCenter}, Größe {map.mapSize}, Kamera gefunden: {Camera.main != null}).");
        }

        /// <summary>
        /// Erzeugt ein Objekt aus einem zufälligen Bild der Liste. Root bleibt ungedreht (für die
        /// Hitbox), das Bild ist ein Kind, das zur Kamera ausgerichtet ist und mit der Unterkante
        /// auf dem Boden steht. Die Größe wird als Höhe in Welt-Einheiten angegeben - die
        /// Pixelgröße des Bildes spielt keine Rolle.
        /// </summary>
        private GameObject SpawnProp(List<Sprite> sprites, Vector3 position, float height, MapSettings map,
                                     Transform parent, Quaternion cameraRotation, string objectName)
        {
            Sprite sprite = sprites[UnityEngine.Random.Range(0, sprites.Count)];

            GameObject prop = new GameObject(objectName);
            prop.transform.SetParent(parent, false);
            prop.transform.position = position;

            if (sprite == null) return prop;

            float scale = sprite.bounds.size.y > 0f ? height / sprite.bounds.size.y : 1f;

            // Bild liegt parallel zum Bildschirm (genau wie der Spieler). Dadurch hat jedes Bild
            // überall dieselbe Tiefe = die Tiefe seines Fußpunkts - Verdeckung passt immer, und
            // hohe Bäume ragen nicht in die Kamera hinein (das hat vorher die geraden Schnittkanten
            // erzeugt, die sich beim Laufen verschoben haben).
            GameObject visual = new GameObject("Visual");
            visual.transform.SetParent(prop.transform, false);
            visual.transform.rotation = cameraRotation;
            visual.transform.localScale = Vector3.one * scale;
            visual.transform.position = position + cameraRotation * Vector3.up * (-sprite.bounds.min.y * scale);

            SpriteRenderer spriteRenderer = visual.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = sprite;
            spriteRenderer.flipX = UnityEngine.Random.value < 0.5f;

            float brightness = UnityEngine.Random.Range(map.brightnessRange.x, map.brightnessRange.y);
            spriteRenderer.sharedMaterial = GetPropMaterial(sprite.texture, brightness);

            return prop;
        }

        private void SpawnEnemies(Level level, MapSettings map, Transform root, Vector3 playerPosition)
        {
            Transform enemiesParent = new GameObject("Enemies").transform;
            enemiesParent.SetParent(root, false);

            foreach (EnemySpawn spawn in level.enemies)
            {
                if (spawn.enemyPrefab == null) continue;

                for (int i = 0; i < spawn.count; i++)
                {
                    if (!TryGetInteriorPoint(map, playerPosition, map.enemyMinDistanceFromPlayer, out Vector3 position))
                    {
                        // Notfalls ohne Mindestabstand platzieren, damit die Anzahl trotzdem stimmt.
                        TryGetInteriorPoint(map, playerPosition, 0f, out position);
                    }

                    position.y += spawn.enemyPrefab.transform.position.y;
                    Instantiate(spawn.enemyPrefab, position, spawn.enemyPrefab.transform.rotation, enemiesParent);
                }
            }
        }

        /// <summary>
        /// Zufälliger Punkt im Inneren des Spielfelds (also nicht im Wald), mit Mindestabstand zum Spieler.
        /// </summary>
        private bool TryGetInteriorPoint(MapSettings map, Vector3 playerPosition, float minDistanceFromPlayer, out Vector3 point)
        {
            // Steine/Büsche/Gegner nur dort, wo garantiert kein Wald mehr ist (auch nicht die Wellen der Waldkante).
            float spawnInset = map.forestDepth * (1f + map.forestEdgeWaviness) + 1f;
            GetInteriorBounds(map, spawnInset, out float minX, out float maxX, out float minZ, out float maxZ);

            for (int attempt = 0; attempt < 30; attempt++)
            {
                point = map.mapCenter + new Vector3(
                    UnityEngine.Random.Range(minX, maxX),
                    0f,
                    UnityEngine.Random.Range(minZ, maxZ));

                Vector3 flatDifference = point - playerPosition;
                flatDifference.y = 0f;
                if (flatDifference.magnitude >= minDistanceFromPlayer) return true;
            }

            point = map.mapCenter;
            return false;
        }

        /// <summary>
        /// Innenbereich (relativ zu Map Center): um "edgeInset" vom Rand eingerückt und am unteren
        /// Rand (der Seite zur Kamera) zusätzlich um Bottom Boundary Offset verkleinert.
        /// </summary>
        private void GetInteriorBounds(MapSettings map, float edgeInset, out float minX, out float maxX, out float minZ, out float maxZ)
        {
            float innerHalfX = Mathf.Max(0.5f, map.mapSize.x * 0.5f - edgeInset);
            float innerHalfZ = Mathf.Max(0.5f, map.mapSize.y * 0.5f - edgeInset);
            minX = -innerHalfX; maxX = innerHalfX;
            minZ = -innerHalfZ; maxZ = innerHalfZ;

            Vector3 forward = GetFlatCameraForward();
            float offset = Mathf.Max(0f, map.bottomBoundaryOffset);

            // Unterer Rand = die Seite, von der aus die Kamera schaut.
            if (Mathf.Abs(forward.z) >= Mathf.Abs(forward.x))
            {
                if (forward.z >= 0f) minZ = Mathf.Min(minZ + offset, maxZ - 1f);
                else maxZ = Mathf.Max(maxZ - offset, minZ + 1f);
            }
            else
            {
                if (forward.x >= 0f) minX = Mathf.Min(minX + offset, maxX - 1f);
                else maxX = Mathf.Max(maxX - offset, minX + 1f);
            }
        }

        /// <summary>
        /// Unsichtbare Wände rund um den begehbaren Bereich: an den Seiten und oben an der
        /// Waldkante (so läuft man nicht in den dichten Wald hinein), unten zusätzlich um
        /// Bottom Boundary Offset nach innen versetzt (damit man nicht hinter den vorderen Bäumen verschwindet).
        /// </summary>
        private void CreateWalkableAreaWalls(MapSettings map, Transform root)
        {
            const float wallHeight = 10f;
            const float wallThickness = 1f;

            GetInteriorBounds(map, map.forestDepth, out float minX, out float maxX, out float minZ, out float maxZ);
            float width = maxX - minX + wallThickness * 2f;
            float depth = maxZ - minZ;
            float centerX = (minX + maxX) * 0.5f;
            float centerZ = (minZ + maxZ) * 0.5f;

            CreateWall(root, map.mapCenter + new Vector3(centerX, wallHeight * 0.5f, minZ - wallThickness * 0.5f), new Vector3(width, wallHeight, wallThickness));
            CreateWall(root, map.mapCenter + new Vector3(centerX, wallHeight * 0.5f, maxZ + wallThickness * 0.5f), new Vector3(width, wallHeight, wallThickness));
            CreateWall(root, map.mapCenter + new Vector3(minX - wallThickness * 0.5f, wallHeight * 0.5f, centerZ), new Vector3(wallThickness, wallHeight, depth));
            CreateWall(root, map.mapCenter + new Vector3(maxX + wallThickness * 0.5f, wallHeight * 0.5f, centerZ), new Vector3(wallThickness, wallHeight, depth));
        }

        private Vector3 GetFlatCameraForward()
        {
            if (Camera.main == null) return Vector3.forward;

            Vector3 forward = Camera.main.transform.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        }

        /// <summary>
        /// Sortiert durchsichtige Objekte (Bäume, Spieler, Gegner, ...) nach ihrer Tiefe in
        /// Blickrichtung statt nach dem Abstand zur Kamera. Ohne das springt die Reihenfolge
        /// beim Laufen ständig um (z.B. Stein plötzlich vor statt hinter dem Baum).
        /// </summary>
        private void SetupCameraSorting()
        {
            if (Camera.main == null) return;

            Camera.main.transparencySortMode = TransparencySortMode.CustomAxis;
            Camera.main.transparencySortAxis = GetFlatCameraForward();

            // Sichtweite großzügig, damit weiter entfernte Bäume nie abgeschnitten werden.
            Camera.main.farClipPlane = Mathf.Max(Camera.main.farClipPlane, 500f);
        }

        private void CreateOuterWalls(MapSettings map, Transform root)
        {
            const float wallHeight = 10f;
            const float wallThickness = 1f;
            float halfX = map.mapSize.x * 0.5f;
            float halfZ = map.mapSize.y * 0.5f;

            CreateWall(root, map.mapCenter + new Vector3(0f, wallHeight * 0.5f, halfZ + wallThickness * 0.5f), new Vector3(map.mapSize.x + wallThickness * 2f, wallHeight, wallThickness));
            CreateWall(root, map.mapCenter + new Vector3(0f, wallHeight * 0.5f, -halfZ - wallThickness * 0.5f), new Vector3(map.mapSize.x + wallThickness * 2f, wallHeight, wallThickness));
            CreateWall(root, map.mapCenter + new Vector3(halfX + wallThickness * 0.5f, wallHeight * 0.5f, 0f), new Vector3(wallThickness, wallHeight, map.mapSize.y));
            CreateWall(root, map.mapCenter + new Vector3(-halfX - wallThickness * 0.5f, wallHeight * 0.5f, 0f), new Vector3(wallThickness, wallHeight, map.mapSize.y));
        }

        private void CreateWall(Transform root, Vector3 center, Vector3 size)
        {
            GameObject wall = new GameObject("OuterWall");
            wall.transform.SetParent(root, false);
            wall.transform.position = center;
            BoxCollider box = wall.AddComponent<BoxCollider>();
            box.size = size;
            wall.isStatic = true;
        }

        /// <summary>
        /// Material für Bäume/Steine/Büsche: undurchsichtig mit "Alpha Clipping" - durchsichtige
        /// Pixel werden komplett verworfen, sichtbare Pixel schreiben in den Tiefenpuffer. Dadurch
        /// verdecken sich die Objekte pixelgenau richtig, unabhängig von Zeichenreihenfolge oder
        /// Kameraposition - einmal platziert, muss nichts mehr neu berechnet werden. Der Spieler
        /// (durchsichtig, wird danach gezeichnet) wird automatisch korrekt davor oder dahinter
        /// gezeichnet. Pro Textur + Helligkeitsstufe ein eigenes Material (gecacht).
        /// </summary>
        private Material GetPropMaterial(Texture texture, float brightness)
        {
            int brightnessStep = Mathf.RoundToInt(brightness * 20f); // 5%-Stufen
            var key = (texture, brightnessStep);
            if (_propMaterials.TryGetValue(key, out Material cached) && cached != null) return cached;

            Material material;
            if (propMaterial != null)
            {
                material = new Material(propMaterial);
            }
            else
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) return null;

                material = new Material(shader);
                material.SetFloat("_Surface", 0f);      // Opaque
                material.SetFloat("_AlphaClip", 1f);
                material.SetFloat("_Cull", 0f);         // beidseitig
                material.EnableKeyword("_ALPHATEST_ON");
                material.SetOverrideTag("RenderType", "TransparentCutout");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            }

            material.SetFloat("_Cutoff", 0.5f);
            material.SetTexture("_BaseMap", texture);
            material.mainTexture = texture;
            float b = brightnessStep / 20f;
            material.SetColor("_BaseColor", new Color(b, b, b, 1f));
            material.name = $"Prop_{(texture != null ? texture.name : "none")}_{brightnessStep}";

            _propMaterials[key] = material;
            return material;
        }
    }
}