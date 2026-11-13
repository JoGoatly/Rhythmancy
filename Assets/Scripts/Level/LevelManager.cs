using System;
using System.Collections;
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
            [Tooltip("AN = Mitte und Größe des Spielfelds werden automatisch vom Boden unter dem Spieler übernommen " +
                     "(Map Center / Map Size werden dann ignoriert). Der Boden braucht dafür einen Collider (hat eine Plane automatisch).")]
            public bool autoDetectGround = true;
            [Tooltip("Mitte des Spielfelds in der Szene (Y = Höhe des Bodens). Nur wenn Auto Detect Ground AUS ist.")]
            public Vector3 mapCenter = Vector3.zero;
            [Tooltip("Gesamtgröße des Spielfelds in X/Z (inklusive Wald am Rand).")]
            public Vector2 mapSize = new Vector2(40f, 40f);
            [Tooltip("In diesem Radius um den Spieler wird nichts platziert (keine Steine/Büsche).")]
            public float clearRadiusAroundPlayer = 4f;
            [Tooltip("Gegner spawnen mindestens so weit vom Spieler entfernt.")]
            public float enemyMinDistanceFromPlayer = 8f;
            [Tooltip("Steine, Büsche und Gegner halten mindestens so viel Abstand zu allem, was in der Szene schon einen Collider hat (Gebäude, Portal, Shop, Interaktions-Zonen, ...).")]
            public float objectClearance = 1.5f;
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
            [Tooltip("Tempo von Spieler und Gegnern, solange sie in einem Busch stehen (0.5 = halb so schnell).")]
            [Range(0.1f, 1f)] public float bushSpeedMultiplier = 0.5f;
            [Tooltip("Wie groß die bremsende Fläche im Verhältnis zur Buschbreite ist (1 = ganze Breite).")]
            [Range(0.2f, 1.2f)] public float bushSlowAreaSize = 0.8f;

            [Header("Farbe")]
            [Tooltip("Zufällige Helligkeit (1 = Originalbild, kleiner = dunkler, größer als 1 = heller, z.B. 0.8 / 1.15).")]
            public Vector2 brightnessRange = new Vector2(0.75f, 1f);

            /// <summary>
            /// Unity setzt bei einem per "+" neu angelegten Listeneintrag alle Zahlen auf 0, statt
            /// die Standardwerte oben zu benutzen (Abstand 0, Anzahl 0, Größe 0 -> es wird nichts
            /// erzeugt). Erkennt das und trägt die Standardwerte nach - eingetragene Bilder bleiben.
            /// </summary>
            public void FillDefaultsIfUninitialized()
            {
                bool uninitialized = treeHeightRange == Vector2.zero && bushHeightRange == Vector2.zero &&
                                     rockHeightRange == Vector2.zero && brightnessRange == Vector2.zero;
                if (!uninitialized) return;

                MapSettings defaults = new MapSettings();
                foreach (System.Reflection.FieldInfo field in typeof(MapSettings).GetFields())
                {
                    if (field.FieldType == typeof(List<Sprite>)) continue; // Bilder behalten
                    field.SetValue(this, field.GetValue(defaults));
                }
            }
        }

        [Serializable]
        public class EnvironmentScene
        {
            [Tooltip("Exakter Szenen-Dateiname (ohne .unity), z.B. SafeZone.")]
            public string sceneName;
            public MapSettings mapSettings = new MapSettings();
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

        [Header("Weitere Szenen mit Wald/Steinen/Büschen, aber ohne Gegner (z.B. Safe Zone)")]
        [SerializeField] private List<EnvironmentScene> environmentScenes = new List<EnvironmentScene>();

        [Header("Umgebung")]
        [Tooltip("Optional, aber für den fertigen Build empfohlen: ein Material mit Shader 'Universal Render Pipeline/Unlit', " +
                 "Alpha Clipping AN und Render Face 'Both'. Leer = wird automatisch erzeugt (funktioniert sicher im Editor).")]
        [SerializeField] private Material propMaterial;

        public IReadOnlyList<Chapter> Chapters => chapters;

        // Bremsende Busch-Flächen im aktuellen Level (Mittelpunkt + Radius) - reine Abstandsprüfung, keine Collider.
        private readonly List<Vector3> _bushPositions = new List<Vector3>();
        private readonly List<float> _bushRadii = new List<float>();
        private float _bushSpeedMultiplier = 1f;

        // Tatsächlich benutztes Spielfeld der aktuellen Szene (aus Map Settings oder automatisch vom Boden erkannt).
        private Vector3 _mapCenter;
        private Vector2 _mapSize;

        public int CurrentWave { get; private set; } = 1;
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

        private void OnValidate()
        {
            foreach (Chapter chapter in chapters)
            {
                chapter.mapSettings?.FillDefaultsIfUninitialized();
                foreach (Level level in chapter.levels)
                {
                    foreach (EnemySpawn spawn in level.enemies)
                    {
                        if (spawn.count == 0 && spawn.enemyPrefab == null) spawn.count = 1;
                    }
                }
            }

            foreach (EnvironmentScene environmentScene in environmentScenes)
            {
                environmentScene.mapSettings?.FillDefaultsIfUninitialized();
            }
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
        public void RestoreContext(int chapterIndex, int levelIndex)
        {
            CurrentChapterIndex = -1; CurrentLevelIndex = -1;
            if (chapterIndex < 0 || chapterIndex >= chapters.Count) return;
            if (levelIndex < 0 || levelIndex >= chapters[chapterIndex].levels.Count) return;
            CurrentChapterIndex = chapterIndex; CurrentLevelIndex = levelIndex;
        }

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
            if (mode == LoadSceneMode.Additive) return;
            CurrentWave = 1;
            _bushPositions.Clear();
            _bushRadii.Clear();
            _bushSpeedMultiplier = 1f;

            StartCoroutine(BuildSceneNextFrame(scene));
        }

        /// <summary>
        /// Wartet einen Frame, damit der Spieler in Start() schon seine gespeicherte Position
        /// geladen hat - sonst würden Wald und Wände um die alte Startposition herum gebaut,
        /// und der Spieler könnte danach mitten im Wald / hinter einer Wand landen.
        /// </summary>
        private IEnumerator BuildSceneNextFrame(Scene scene)
        {
            yield return null;
            if (!scene.isLoaded) yield break;
            BuildScene(scene);
        }

        private void BuildScene(Scene scene)
        {

            // Szenen wie die Safe Zone: nur Umgebung erzeugen, keine Gegner.
            EnvironmentScene environmentScene = environmentScenes.Find(e => e.sceneName == scene.name);
            if (environmentScene != null)
            {
                GameObject environmentRoot = new GameObject("GeneratedEnvironment");
                SceneManager.MoveGameObjectToScene(environmentRoot, scene);
                SetupCameraSorting();

                PlayerController scenePlayer = FindFirstObjectByType<PlayerController>();
                Vector3 scenePlayerPosition = scenePlayer != null ? scenePlayer.transform.position : environmentScene.mapSettings.mapCenter;

                GenerateEnvironment(environmentScene.mapSettings, environmentRoot.transform, scenePlayerPosition, scene.name);
                return;
            }

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

            GenerateEnvironment(chapter.mapSettings, root.transform, playerPosition, chapter.chapterName);
            if (scene.name == "Tutorial Level")
            {
                if (player != null) player.SetAutoWinCheckEnabled(false);
                StartCoroutine(RunTutorialWaves(chapter.levels[CurrentLevelIndex], chapter.mapSettings, root.transform, player));
            }
            else
            {
                SpawnEnemies(chapter.levels[CurrentLevelIndex], chapter.mapSettings, root.transform, playerPosition);
            }
        }

        private IEnumerator RunTutorialWaves(Level level, MapSettings map, Transform root, PlayerController player)
        {
            EnemySpawn template = level.enemies.Find(spawn => spawn.enemyPrefab != null);
            if (template == null)
            {
                Debug.LogError("Tutorial Level: Kein Gegner-Prefab für die Wellen konfiguriert.");
                yield break;
            }

            for (int wave = 1; wave <= 5; wave++)
            {
                if (root == null || player == null) yield break;
                CurrentWave = wave;
                Level waveLevel = new Level();
                waveLevel.enemies.Add(new EnemySpawn { enemyPrefab = template.enemyPrefab, count = wave });
                SpawnEnemies(waveLevel, map, root, player.transform.position);

                // Erst nach dem letzten besiegten Gegner beginnt die nächste Welle.
                do
                {
                    yield return null;
                    if (root == null || player == null) yield break;
                }
                while (Time.timeScale <= 0f || root.GetComponentsInChildren<EnemyController>().Length > 0);

                GameSession.CompleteWave(wave);
            }

            player.WinLevel();
        }

        /// <summary>
        /// Tempo-Faktor an einer Position: in einem Busch kleiner als 1 (bremst), sonst 1.
        /// Wird von PlayerController und EnemyController beim Bewegen abgefragt.
        /// </summary>
        public float GetSpeedMultiplierAt(Vector3 worldPosition)
        {
            for (int i = 0; i < _bushPositions.Count; i++)
            {
                Vector3 difference = worldPosition - _bushPositions[i];
                difference.y = 0f;
                if (difference.sqrMagnitude <= _bushRadii[i] * _bushRadii[i])
                {
                    return _bushSpeedMultiplier;
                }
            }
            return 1f;
        }

        /// <summary>
        /// Legt das Spielfeld für diese Szene fest: entweder automatisch aus dem Boden unter dem
        /// Spieler (größter Collider direkt unter ihm), oder aus Map Center / Map Size.
        /// Muss vor GenerateEnvironment und SpawnEnemies aufgerufen werden.
        /// </summary>
        private void PrepareMapArea(MapSettings map, Vector3 playerPosition, string displayName)
        {
            _mapCenter = map.mapCenter;
            _mapSize = map.mapSize;

            if (!map.autoDetectGround) return;

            RaycastHit[] hits = Physics.RaycastAll(playerPosition + Vector3.up * 5f, Vector3.down, 100f, ~0, QueryTriggerInteraction.Ignore);
            Collider bestGround = null;
            float bestArea = 0f;
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider is CharacterController) continue; // Spieler / Gegner selbst
                Bounds b = hit.collider.bounds;
                float area = b.size.x * b.size.z;
                if (area > bestArea)
                {
                    bestArea = area;
                    bestGround = hit.collider;
                }
            }

            if (bestGround == null)
            {
                Debug.LogWarning($"LevelManager: '{displayName}' - kein Boden mit Collider unter dem Spieler gefunden, benutze Map Center / Map Size.");
                return;
            }

            Bounds ground = bestGround.bounds;
            _mapCenter = new Vector3(ground.center.x, ground.max.y, ground.center.z);
            _mapSize = new Vector2(ground.size.x, ground.size.z);
        }

        private void GenerateEnvironment(MapSettings map, Transform root, Vector3 playerPosition, string displayName)
        {
            // Sicherstellen, dass alle Collider der frisch geladenen Szene für die Abstandsprüfung bekannt sind.
            Physics.SyncTransforms();
            PrepareMapArea(map, playerPosition, displayName);

            if (GameObject.Find("Handdrawn Forest Map") != null) return;

            Quaternion cameraRotation = Camera.main != null ? Camera.main.transform.rotation : Quaternion.identity;

            Transform forestParent = new GameObject("Forest").transform;
            forestParent.SetParent(root, false);
            Transform propsParent = new GameObject("Props").transform;
            propsParent.SetParent(root, false);

            float halfX = _mapSize.x * 0.5f;
            float halfZ = _mapSize.y * 0.5f;

            if (map.treeSprites.Count == 0) Debug.LogWarning($"LevelManager: '{displayName}' hat keine Tree Sprites eingetragen.");
            if (map.rockSprites.Count == 0) Debug.LogWarning($"LevelManager: '{displayName}' hat keine Rock Sprites eingetragen.");
            if (map.bushSprites.Count == 0) Debug.LogWarning($"LevelManager: '{displayName}' hat keine Bush Sprites eingetragen.");

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

                        // Nie direkt auf/neben den Spieler, sonst steckt er fest.
                        Vector3 flatToPlayer = (_mapCenter + new Vector3(x, 0f, z)) - playerPosition;
                        flatToPlayer.y = 0f;
                        if (flatToPlayer.magnitude < map.clearRadiusAroundPlayer + 1f) continue;

                        // Im inneren Drittel des Waldstreifens nach und nach lichter werden.
                        float depthRatio = distanceToEdge / localDepth;
                        if (depthRatio > 0.66f)
                        {
                            float keepChance = Mathf.Lerp(1f, map.forestEdgeDensity, (depthRatio - 0.66f) / 0.34f);
                            if (UnityEngine.Random.value > keepChance) continue;
                        }

                        float offset = map.treeSpacing * 0.5f * map.treeRandomOffset;
                        Vector3 position = _mapCenter + new Vector3(
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
                    GameObject bush = SpawnProp(map.bushSprites, position, height, map, propsParent, cameraRotation, "Bush");

                    SpriteRenderer bushRenderer = bush.GetComponentInChildren<SpriteRenderer>();
                    if (bushRenderer != null)
                    {
                        _bushPositions.Add(position);
                        _bushRadii.Add(bushRenderer.bounds.extents.x * map.bushSlowAreaSize);
                    }
                }
            }

            _bushSpeedMultiplier = map.bushSpeedMultiplier;

            if (map.addOuterWall) CreateOuterWalls(map, root);
            CreateWalkableAreaWalls(map, root, playerPosition);

            Debug.Log($"LevelManager: {forestParent.childCount} Bäume und {propsParent.childCount} Steine/Büsche erzeugt " +
                      $"(Spielfeld-Mitte {_mapCenter}, Größe {_mapSize}, Kamera gefunden: {Camera.main != null}).");
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
                point = _mapCenter + new Vector3(
                    UnityEngine.Random.Range(minX, maxX),
                    0f,
                    UnityEngine.Random.Range(minZ, maxZ));

                Vector3 flatDifference = point - playerPosition;
                flatDifference.y = 0f;
                if (flatDifference.magnitude < minDistanceFromPlayer) continue;

                // Nicht in/an Gebäuden, Portal, Shop, Bäumen usw. (alles mit Collider, auch Trigger-Zonen).
                // Kugel liegt knapp über dem Boden, damit der Boden selbst nicht mitzählt.
                if (map.objectClearance > 0f)
                {
                    float radius = map.objectClearance;
                    Vector3 checkCenter = point + Vector3.up * (radius + 0.1f);
                    if (Physics.CheckSphere(checkCenter, radius, ~0, QueryTriggerInteraction.Collide)) continue;
                }

                return true;
            }

            point = _mapCenter;
            return false;
        }

        /// <summary>
        /// Innenbereich (relativ zu Map Center): um "edgeInset" vom Rand eingerückt und am unteren
        /// Rand (der Seite zur Kamera) zusätzlich um Bottom Boundary Offset verkleinert.
        /// </summary>
        private void GetInteriorBounds(MapSettings map, float edgeInset, out float minX, out float maxX, out float minZ, out float maxZ)
        {
            float innerHalfX = Mathf.Max(0.5f, _mapSize.x * 0.5f - edgeInset);
            float innerHalfZ = Mathf.Max(0.5f, _mapSize.y * 0.5f - edgeInset);
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
        private void CreateWalkableAreaWalls(MapSettings map, Transform root, Vector3 playerPosition)
        {
            const float wallHeight = 10f;
            const float wallThickness = 1f;

            GetInteriorBounds(map, map.forestDepth, out float minX, out float maxX, out float minZ, out float maxZ);

            // Sicherheit: Steht der Spieler außerhalb des begehbaren Bereichs, KEINE Wände bauen - sonst wäre er eingesperrt.
            Vector3 local = playerPosition - _mapCenter;
            if (local.x < minX || local.x > maxX || local.z < minZ || local.z > maxZ)
            {
                Debug.LogWarning("LevelManager: Spieler startet außerhalb des begehbaren Bereichs (zu nah am Rand / im Wald) - " +
                                 "unsichtbare Wände wurden weggelassen. Spieler weiter in die Mitte setzen oder Forest Depth / Bottom Boundary Offset verkleinern.");
                return;
            }

            float width = maxX - minX + wallThickness * 2f;
            float depth = maxZ - minZ;
            float centerX = (minX + maxX) * 0.5f;
            float centerZ = (minZ + maxZ) * 0.5f;

            CreateWall(root, _mapCenter + new Vector3(centerX, wallHeight * 0.5f, minZ - wallThickness * 0.5f), new Vector3(width, wallHeight, wallThickness));
            CreateWall(root, _mapCenter + new Vector3(centerX, wallHeight * 0.5f, maxZ + wallThickness * 0.5f), new Vector3(width, wallHeight, wallThickness));
            CreateWall(root, _mapCenter + new Vector3(minX - wallThickness * 0.5f, wallHeight * 0.5f, centerZ), new Vector3(wallThickness, wallHeight, depth));
            CreateWall(root, _mapCenter + new Vector3(maxX + wallThickness * 0.5f, wallHeight * 0.5f, centerZ), new Vector3(wallThickness, wallHeight, depth));
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
            float halfX = _mapSize.x * 0.5f;
            float halfZ = _mapSize.y * 0.5f;

            CreateWall(root, _mapCenter + new Vector3(0f, wallHeight * 0.5f, halfZ + wallThickness * 0.5f), new Vector3(_mapSize.x + wallThickness * 2f, wallHeight, wallThickness));
            CreateWall(root, _mapCenter + new Vector3(0f, wallHeight * 0.5f, -halfZ - wallThickness * 0.5f), new Vector3(_mapSize.x + wallThickness * 2f, wallHeight, wallThickness));
            CreateWall(root, _mapCenter + new Vector3(halfX + wallThickness * 0.5f, wallHeight * 0.5f, 0f), new Vector3(wallThickness, wallHeight, _mapSize.y));
            CreateWall(root, _mapCenter + new Vector3(-halfX - wallThickness * 0.5f, wallHeight * 0.5f, 0f), new Vector3(wallThickness, wallHeight, _mapSize.y));
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