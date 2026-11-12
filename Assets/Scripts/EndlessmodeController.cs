using RhythmWitchClone.Audio;
using RhythmWitchClone.Core;
using RhythmWitchClone.Gameplay;
using RhythmWitchClone.UI.Pause;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RhythmWitchClone.Levels
{
    /// <summary>
    /// Endlos-Modus (z.B. für den Tag der offenen Tür):
    /// - Endlose Wellen. Jede Welle hat mehr Gegner, Gegner haben mehr Leben, und nach und nach
    ///   kommen mehr Tasten auf der Beat-Lane dazu.
    /// - Punkte pro Kill (spätere Wellen geben mehr) + Bonus für jede geschaffte Welle.
    /// - Beim Tod: Highscore-Bildschirm mit Namenseingabe und scrollbarer Bestenliste.
    ///   Die Bestenliste wird auf diesem PC gespeichert (PlayerPrefs) und bleibt nach Neustart erhalten.
    ///
    /// Platzierung: auf ein leeres GameObject in der Endlos-Szene.
    /// </summary>
    [DefaultExecutionOrder(100)] // Start nach PlayerController (der lädt sonst die Speicher-Position)
    public class EndlessModeController : MonoBehaviour
    {
        [System.Serializable]
        public class EndlessEnemy
        {
            public GameObject enemyPrefab;
            [Tooltip("Ab welcher Welle dieser Gegnertyp vorkommen kann.")]
            public int fromWave = 1;
            [Tooltip("Wie häufig dieser Typ im Vergleich zu den anderen gewählt wird (2 = doppelt so oft wie 1).")]
            public float weight = 1f;
        }

        [System.Serializable]
        private class LeaderboardEntry
        {
            public string name;
            public int score;
            public int wave;
        }

        [System.Serializable]
        private class LeaderboardData
        {
            public List<LeaderboardEntry> entries = new List<LeaderboardEntry>();
        }

        private const string LeaderboardKey = "EndlessLeaderboard";

        [Header("Referenzen")]
        [SerializeField] private PlayerController player;
        [SerializeField] private BeatLaneController beatLane;

        [Header("Gegner")]
        [SerializeField] private List<EndlessEnemy> enemies = new List<EndlessEnemy>();
        [Tooltip("Gegner erscheinen nie näher als das am Spieler. Sie tauchen zufällig im Wald am Rand der Map auf " +
                 "(dafür muss die Szene im LevelManager unter 'Environment Scenes' eingetragen sein) und laufen durch " +
                 "Bäume und unsichtbare Wände hindurch auf den Spieler zu.")]
        [SerializeField] private float minSpawnDistanceToPlayer = 8f;

        [Header("Wellen")]
        [SerializeField] private int firstWaveEnemyCount = 3;
        [Tooltip("So viele Gegner kommen pro Welle dazu.")]
        [SerializeField] private int extraEnemiesPerWave = 2;
        [SerializeField] private int maxEnemiesPerWave = 40;
        [Tooltip("Gegner-Leben steigt pro Welle um diesen Anteil (0.15 = +15% pro Welle).")]
        [SerializeField] private float healthIncreasePerWave = 0.15f;
        [Tooltip("Abstand zwischen zwei Gegner-Spawns innerhalb einer Welle (Sekunden).")]
        [SerializeField] private float spawnInterval = 0.6f;
        [Tooltip("Pause zwischen zwei Wellen (Sekunden).")]
        [SerializeField] private float timeBetweenWaves = 3f;

        [Header("Tasten auf der Beat-Lane")]
        [Tooltip("Mit so vielen Tasten startet Welle 1 (die ersten N aus der Lane-Keys-Liste der Beat-Lane).")]
        [SerializeField] private int startKeyCount = 2;
        [Tooltip("Alle wie viele Wellen eine weitere Taste dazukommt.")]
        [SerializeField] private int wavesPerExtraKey = 2;

        [Header("Tempo der Beat-Lane")]
        [Tooltip("So viel schneller laufen die Noten pro Welle (0.06 = +6 % pro Welle: Welle 5 = +24 %, Welle 10 = +54 %).")]
        [SerializeField] private float laneSpeedIncreasePerWave = 0.06f;
        [Tooltip("Höchstes Tempo (2 = doppelt so schnell wie am Anfang) - damit es nicht unspielbar wird.")]
        [SerializeField] private float maxLaneSpeedMultiplier = 2.2f;
        [Tooltip("Nur wenn die Beat-Lane mit der Musik synchronisiert ist: Song wird mit jeder Welle genauso viel " +
                 "schneller abgespielt (dadurch bleiben Noten und Musik synchron). AUS = Song bleibt normal, " +
                 "die Noten werden dann aber auch nicht schneller.")]
        [SerializeField] private bool speedUpMusicWithLane = true;

        [Header("Punkte")]
        [SerializeField] private int pointsPerKill = 10;
        [Tooltip("Zusätzliche Punkte pro Kill für jede Welle nach der ersten (Welle 5 = 10 + 4*5 = 30 pro Kill).")]
        [SerializeField] private int extraKillPointsPerWave = 5;
        [Tooltip("Bonus für eine geschaffte Welle, wird mit der Wellennummer multipliziert.")]
        [SerializeField] private int waveClearBonus = 50;

        [Header("Upgrade-Auswahl nach jeder Welle (wird komplett per Script gebaut)")]
        [Tooltip("AN = nach jeder geschafften Welle pausiert das Spiel und man kann eine von 3 Verbesserungen kaufen oder überspringen.")]
        [SerializeField] private bool upgradeShopEnabled = true;
        [Tooltip("Für die Karten-Upgrades. Leer = wird automatisch in der Szene gesucht.")]
        [SerializeField] private CardAbilityController cardAbilities;
        [Tooltip("Optional: Schriftart für das Upgrade-Menü. Leer = TextMeshPro-Standardschrift.")]
        [SerializeField] private TMP_FontAsset shopFont;
        [Tooltip("Um so viele Münzen wird ein Upgrade pro Kauf teurer.")]
        [SerializeField] private int upgradeCostIncreasePerPurchase = 2;
        [Tooltip("Multipliziert alle Upgrade-Preise (z.B. 2 = alles doppelt so teuer).")]
        [SerializeField] private float upgradeCostMultiplier = 1f;

        [Header("HUD (selbst im Canvas erstellen)")]
        [SerializeField] private TMP_Text scoreText;
        [SerializeField] private TMP_Text waveText;
        [Tooltip("Großer Text in der Bildschirmmitte, z.B. 'Welle 3' - wird kurz eingeblendet.")]
        [SerializeField] private TMP_Text waveAnnouncementText;
        [SerializeField] private float announcementDuration = 2f;

        [Header("Highscore-Bildschirm (selbst im Canvas erstellen)")]
        [SerializeField] private GameObject highscorePanel;
        [SerializeField] private TMP_Text finalScoreText;
        [Tooltip("Gruppe mit Namensfeld + Eintragen-Button - wird nach dem Eintragen ausgeblendet.")]
        [SerializeField] private GameObject nameEntryGroup;
        [SerializeField] private TMP_InputField nameInput;
        [SerializeField] private Button submitButton;
        [Tooltip("Text mit allen Einträgen - liegt im 'Content' einer Scroll View (siehe Anleitung).")]
        [SerializeField] private TMP_Text leaderboardText;
        [Tooltip("Optional: feste Überschrift-Zeile über der Liste (Platz / Name / Welle / Punkte).")]
        [SerializeField] private TMP_Text leaderboardHeaderText;
        [Tooltip("Die Scroll View der Bestenliste - damit automatisch zum eigenen Eintrag gescrollt wird.")]
        [SerializeField] private ScrollRect leaderboardScrollRect;
        [SerializeField] private Button retryButton;
        [SerializeField] private Button mainMenuButton;
        [SerializeField] private string mainMenuSceneName = "MainMenu";
        [Tooltip("So viele Einträge werden gespeichert und in der (scrollbaren) Liste angezeigt.")]
        [SerializeField] private int maxLeaderboardEntries = 100;
        [SerializeField] private int maxNameLength = 12;
        [SerializeField] private Color highlightColor = new Color(0.89f, 0.27f, 0.67f); // Magenta aus dem Style Guide

        public int Score { get; private set; }
        public int Wave { get; private set; }

        private readonly List<EnemyController> _aliveEnemies = new List<EnemyController>();
        private Vector3 _playerStartPosition;
        private bool _isGameOver;
        private Coroutine _waveRoutine;
        private float _announcementTimer;
        private float _scorePunch;
        private LeaderboardData _leaderboard;
        private int _highlightedEntryIndex = -1;

        private void Awake()
        {
            PauseMenuController.UseCustomGameOverScreen = true;

            // Tasten sofort begrenzen - nicht erst, wenn Welle 1 startet (sonst sieht man am Anfang kurz alle Tasten)
            if (beatLane != null) beatLane.SetActiveKeyCount(Mathf.Max(1, startKeyCount));
            if (player != null) _playerStartPosition = player.transform.position; // vor dem Laden der Speicher-Position merken

            if (submitButton != null) submitButton.onClick.AddListener(SubmitScore);
            if (retryButton != null) retryButton.onClick.AddListener(Retry);
            if (mainMenuButton != null) mainMenuButton.onClick.AddListener(GoToMainMenu);
            if (nameInput != null)
            {
                nameInput.characterLimit = maxNameLength;
                nameInput.onSubmit.AddListener(_ => SubmitScore()); // Enter trägt ein
            }
        }

        private void OnEnable() => EnemyController.Killed += OnEnemyKilled;
        private void OnDisable() => EnemyController.Killed -= OnEnemyKilled;

        private void OnDestroy()
        {
            PauseMenuController.UseCustomGameOverScreen = false;
            PauseMenuController.BlockPauseInput = false;
        }

        private void Start()
        {
            // Nochmal, falls die Beat-Lane ihre Noten erst in ihrem eigenen Start() erzeugt hat
            if (beatLane != null) beatLane.SetActiveKeyCount(Mathf.Max(1, startKeyCount));

            if (player != null)
            {
                player.SetAutoWinCheckEnabled(false);     // "keine Gegner = gewonnen" gibt es hier nicht
                player.TeleportTo(_playerStartPosition);  // immer am Startpunkt der Szene beginnen
            }

            if (highscorePanel != null) highscorePanel.SetActive(false);
            if (waveAnnouncementText != null) waveAnnouncementText.gameObject.SetActive(false);

            BuildUpgradePool();
            _leaderboard = LoadLeaderboard();
            Score = 0;
            UpdateHud();

            _waveRoutine = StartCoroutine(WaveLoop());
        }

        private void LateUpdate()
        {
            // Solange die Upgrade-Auswahl offen ist, bleibt das Spiel pausiert -
            // auch wenn z.B. das Pause-Menü zwischendurch auf "Weiter" gesetzt hat.
            if (_shopOpen && Time.timeScale != 0f) Time.timeScale = 0f;
        }

        private void Update()
        {
            UpdateAnnouncement();
            UpdateScorePunch();
            UpdateShopAnimations();
            HandleShopKeyboard();

            if (!_isGameOver && player != null && player.CurrentHealth <= 0)
            {
                StartCoroutine(GameOver());
            }
        }

        // ------------------------------------------------------------------
        // Wellen
        // ------------------------------------------------------------------

        private IEnumerator WaveLoop()
        {
            yield return new WaitForSeconds(1f);

            while (!_isGameOver)
            {
                Wave++;
                UpdateHud();
                ApplyKeyCount();
                ShowAnnouncement($"Welle {Wave}");

                int enemyCount = Mathf.Min(maxEnemiesPerWave, firstWaveEnemyCount + (Wave - 1) * extraEnemiesPerWave);
                float healthMultiplier = 1f + healthIncreasePerWave * (Wave - 1);

                yield return new WaitForSeconds(1f);

                for (int i = 0; i < enemyCount && !_isGameOver; i++)
                {
                    SpawnEnemy(healthMultiplier);
                    yield return new WaitForSeconds(spawnInterval);
                }

                // Warten, bis alle Gegner dieser Welle besiegt sind
                while (!_isGameOver && CountAliveEnemies() > 0)
                {
                    yield return null;
                }

                if (_isGameOver) yield break;

                AddScore(waveClearBonus * Wave);
                ShowAnnouncement($"Welle {Wave} geschafft!\n+{waveClearBonus * Wave}");

                if (upgradeShopEnabled)
                {
                    yield return new WaitForSeconds(1.2f);          // Todes-Animationen/Ansage kurz wirken lassen
                    if (_isGameOver) yield break;
                    if (player != null) player.CollectAllPickups(); // liegengebliebene Münzen & Orbs automatisch einsammeln
                    yield return RunUpgradeShop();
                    if (_isGameOver) yield break;
                }

                yield return new WaitForSeconds(timeBetweenWaves);
            }
        }

        private void ApplyKeyCount()
        {
            if (beatLane == null) return;

            // Noten werden mit jeder Welle ein Stück schneller
            float speed = Mathf.Min(maxLaneSpeedMultiplier, 1f + laneSpeedIncreasePerWave * (Wave - 1));
            beatLane.SetSpeedMultiplier(speed);

            // Bei Musik-Sync bestimmt der Song das Tempo der Noten -> Song mit beschleunigen, damit beides synchron bleibt
            if (beatLane.IsSyncedToMusic && speedUpMusicWithLane && AudioManager.Instance != null)
            {
                AudioManager.Instance.SetMusicPitch(speed);
            }

            int extraKeys = wavesPerExtraKey > 0 ? (Wave - 1) / wavesPerExtraKey : 0;
            int keyCount = Mathf.Clamp(startKeyCount + extraKeys, 1, beatLane.TotalKeyCount);
            int previous = beatLane.ActiveKeyCount;
            beatLane.SetActiveKeyCount(keyCount);

            if (Wave > 1 && keyCount > previous)
            {
                ShowAnnouncement($"Welle {Wave}\nNeue Taste!");
            }
        }

        private void SpawnEnemy(float healthMultiplier)
        {
            GameObject prefab = PickEnemyPrefab();
            if (prefab == null) return;

            Vector3 position = GetSpawnPosition();
            // Füße genau auf den Boden setzen (statt Prefab-Höhe zu raten) - sonst steckt der
            // CharacterController halb im Boden und fällt durch.
            position.y += GetFeetOffset(prefab);

            GameObject enemyObject = Instantiate(prefab, position, prefab.transform.rotation);

            // Gegner dürfen durch Baumstämme und die unsichtbaren Wände laufen (der Spieler nicht),
            // sonst würden sie im Wald hängen bleiben.
            if (LevelManager.Instance != null)
            {
                foreach (Collider enemyCollider in enemyObject.GetComponentsInChildren<Collider>())
                {
                    LevelManager.Instance.IgnoreGeneratedColliders(enemyCollider);
                }
            }

            EnemyController enemy = enemyObject.GetComponent<EnemyController>();
            if (enemy != null)
            {
                enemy.SetHealthMultiplier(healthMultiplier);
                _aliveEnemies.Add(enemy);
            }
        }

        private GameObject PickEnemyPrefab()
        {
            float totalWeight = 0f;
            foreach (EndlessEnemy entry in enemies)
            {
                if (entry.enemyPrefab != null && Wave >= entry.fromWave) totalWeight += Mathf.Max(0f, entry.weight);
            }
            if (totalWeight <= 0f) return null;

            float roll = Random.value * totalWeight;
            foreach (EndlessEnemy entry in enemies)
            {
                if (entry.enemyPrefab == null || Wave < entry.fromWave) continue;
                roll -= Mathf.Max(0f, entry.weight);
                if (roll <= 0f) return entry.enemyPrefab;
            }
            return null;
        }

        /// <summary>
        /// Zufälliger Punkt im Wald am Rand der Map (vom LevelManager), weit genug vom Spieler weg.
        /// Gibt es keinen Wald in der Szene, erscheinen Gegner auf einem Kreis um den Spieler.
        /// </summary>
        private Vector3 GetSpawnPosition()
        {
            Vector3 playerPosition = player != null ? player.transform.position : Vector3.zero;

            // 1. Bevorzugt: Punkt im Wald am Rand - aber nur, wenn darunter wirklich Boden ist.
            if (LevelManager.Instance != null)
            {
                for (int attempt = 0; attempt < 25; attempt++)
                {
                    if (!LevelManager.Instance.TryGetForestSpawnPoint(out Vector3 candidate)) break;

                    Vector3 flat = candidate - playerPosition;
                    flat.y = 0f;
                    if (flat.magnitude < minSpawnDistanceToPlayer) continue;

                    if (TryFindGround(candidate, out float groundY))
                    {
                        candidate.y = groundY;
                        return candidate;
                    }
                }
            }

            // 2. Ersatz: Kreis um den Spieler, Abstand wird verkleinert bis Boden gefunden wird.
            for (float distance = minSpawnDistanceToPlayer + 4f; distance >= 2f; distance -= 2f)
            {
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    Vector3 direction = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
                    Vector3 candidate = playerPosition + direction * distance;
                    if (TryFindGround(candidate, out float groundY))
                    {
                        candidate.y = groundY;
                        return candidate;
                    }
                }
            }

            return playerPosition; // sollte praktisch nie passieren
        }

        /// <summary>
        /// Sucht senkrecht unter einem Punkt den Boden. Als Boden zählt der größte getroffene
        /// Collider (Baumstämme, Wände, Spieler und Gegner werden dadurch ignoriert).
        /// False = dort ist kein Boden (z.B. außerhalb der Map).
        /// </summary>
        private bool TryFindGround(Vector3 point, out float groundY)
        {
            groundY = 0f;
            RaycastHit[] hits = Physics.RaycastAll(new Vector3(point.x, point.y + 30f, point.z), Vector3.down, 100f, ~0, QueryTriggerInteraction.Ignore);

            float bestArea = 0f;
            bool found = false;
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider is CharacterController) continue;
                Bounds b = hit.collider.bounds;
                float area = b.size.x * b.size.z;
                if (area > bestArea)
                {
                    bestArea = area;
                    groundY = hit.point.y;
                    found = true;
                }
            }
            return found;
        }

        /// <summary>
        /// Wie hoch der Gegner über dem Boden platziert werden muss, damit die Unterkante seines
        /// CharacterControllers genau auf dem Boden steht (plus kleiner Sicherheitsabstand).
        /// </summary>
        private static float GetFeetOffset(GameObject prefab)
        {
            CharacterController controller = prefab.GetComponent<CharacterController>();
            if (controller == null) return Mathf.Max(0f, prefab.transform.position.y);

            float scaleY = prefab.transform.localScale.y;
            float bottom = (controller.center.y - controller.height * 0.5f) * scaleY; // relativ zum Pivot
            return -bottom + controller.skinWidth + 0.05f;
        }

        private int CountAliveEnemies()
        {
            _aliveEnemies.RemoveAll(e => e == null || e.IsDying);
            return _aliveEnemies.Count;
        }

        private void OnEnemyKilled(EnemyController enemy)
        {
            if (_isGameOver) return;
            AddScore(pointsPerKill + extraKillPointsPerWave * Mathf.Max(0, Wave - 1));
        }

        // ------------------------------------------------------------------
        // HUD
        // ------------------------------------------------------------------

        private void AddScore(int points)
        {
            Score += points;
            _scorePunch = 1f;
            UpdateHud();
        }

        private void UpdateHud()
        {
            if (scoreText != null) scoreText.text = $"{Score} Punkte";
            if (waveText != null) waveText.text = $"Welle {Mathf.Max(1, Wave)}";
        }

        private void UpdateScorePunch()
        {
            if (scoreText == null) return;
            _scorePunch = Mathf.MoveTowards(_scorePunch, 0f, Time.unscaledDeltaTime * 4f);
            scoreText.rectTransform.localScale = Vector3.one * (1f + 0.3f * _scorePunch);
        }

        private void ShowAnnouncement(string text)
        {
            if (waveAnnouncementText == null) return;
            waveAnnouncementText.text = text;
            waveAnnouncementText.gameObject.SetActive(true);
            _announcementTimer = announcementDuration;
        }

        private void UpdateAnnouncement()
        {
            if (waveAnnouncementText == null || _announcementTimer <= 0f) return;

            _announcementTimer -= Time.unscaledDeltaTime;
            float t = _announcementTimer / Mathf.Max(0.01f, announcementDuration); // 1 -> 0
            waveAnnouncementText.alpha = Mathf.Clamp01(t * 3f);                     // im letzten Drittel ausblenden
            float pop = Mathf.Clamp01((t - 0.85f) / 0.15f);                          // am Anfang kurz größer
            waveAnnouncementText.rectTransform.localScale = Vector3.one * (1f + 0.4f * pop);

            if (_announcementTimer <= 0f) waveAnnouncementText.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------
        // Game Over / Highscore
        // ------------------------------------------------------------------

        private IEnumerator GameOver()
        {
            _isGameOver = true;
            if (_waveRoutine != null) StopCoroutine(_waveRoutine);
            if (beatLane != null) beatLane.SetActive(false); // damit Tippen im Namensfeld keine Noten auslöst
            if (waveAnnouncementText != null) waveAnnouncementText.gameObject.SetActive(false);

            yield return new WaitForSecondsRealtime(1f); // PauseMenuController hat das Spiel bereits angehalten
            if (AudioManager.Instance != null) AudioManager.Instance.PlayGameOverMusic();

            if (highscorePanel != null) highscorePanel.SetActive(true);
            if (finalScoreText != null) finalScoreText.text = $"{Score} Punkte\nbis Welle {Wave}";
            if (nameEntryGroup != null) nameEntryGroup.SetActive(true);

            _highlightedEntryIndex = -1;
            RefreshLeaderboardText();

            if (nameInput != null)
            {
                nameInput.text = "";
                nameInput.Select();
                nameInput.ActivateInputField();
            }
        }

        private void SubmitScore()
        {
            if (!_isGameOver || nameEntryGroup == null || !nameEntryGroup.activeSelf) return;

            string playerName = nameInput != null ? nameInput.text.Trim() : "";
            if (string.IsNullOrEmpty(playerName)) playerName = "Anonym";
            if (playerName.Length > maxNameLength) playerName = playerName.Substring(0, maxNameLength);

            LeaderboardEntry entry = new LeaderboardEntry { name = playerName, score = Score, wave = Wave };
            _leaderboard.entries.Add(entry);
            _leaderboard.entries.Sort((a, b) => b.score.CompareTo(a.score));
            if (_leaderboard.entries.Count > maxLeaderboardEntries)
            {
                _leaderboard.entries.RemoveRange(maxLeaderboardEntries, _leaderboard.entries.Count - maxLeaderboardEntries);
            }
            SaveLeaderboard(_leaderboard);

            _highlightedEntryIndex = _leaderboard.entries.IndexOf(entry);
            nameEntryGroup.SetActive(false);
            RefreshLeaderboardText();

            if (AudioManager.Instance != null) AudioManager.Instance.PlayLevelWinSound();
        }

        private void RefreshLeaderboardText()
        {
            ApplyLeaderboardFormatting();

            // Spalten in "em" (= Schriftgröße) statt in % der Breite: so stehen Überschrift und
            // Einträge immer exakt untereinander, egal wie breit die beiden Textfelder sind.
            const string colName = "<pos=3.4em>", colWave = "<pos=12.4em>", colScore = "<pos=16.2em>";

            if (leaderboardHeaderText != null)
            {
                leaderboardHeaderText.text = $"Platz{colName}Name{colWave}Welle{colScore}Punkte";
            }

            if (leaderboardText == null) return;

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            string hex = ColorUtility.ToHtmlStringRGB(highlightColor);
            int count = Mathf.Min(maxLeaderboardEntries, _leaderboard.entries.Count);

            if (count == 0) sb.Append("Noch keine Einträge - sei der Erste!");

            for (int i = 0; i < count; i++)
            {
                LeaderboardEntry e = _leaderboard.entries[i];
                string line = $"{i + 1}.{colName}{e.name}{colWave}{e.wave}{colScore}{e.score}";
                // Eigener Eintrag nur farbig (nicht fett), damit er genauso breit ist wie alle anderen
                sb.AppendLine(i == _highlightedEntryIndex ? $"<color=#{hex}>{line}</color>" : line);
            }

            leaderboardText.text = sb.ToString();
            StartCoroutine(ScrollToHighlightedEntry(count));
        }

        /// <summary>
        /// Sorgt für ein einheitliches Aussehen, egal wie die Textfelder im Inspector eingestellt
        /// sind: linksbündig, kein Zeilenumbruch, keine automatische Schriftgröße, und Überschrift
        /// und Liste bekommen dieselbe Schriftgröße - so klein, dass eine ganze Zeile (ca. 21 em)
        /// in das schmalere der beiden Felder passt.
        /// </summary>
        private void ApplyLeaderboardFormatting()
        {
            const float lineWidthInEm = 21f;
            Canvas.ForceUpdateCanvases(); // Panel wurde evtl. gerade erst eingeblendet - Breiten aktualisieren

            float fontSize = leaderboardText != null ? leaderboardText.fontSize : 36f;
            float width = float.MaxValue;
            if (leaderboardText != null) width = Mathf.Min(width, leaderboardText.rectTransform.rect.width);
            if (leaderboardHeaderText != null)
            {
                width = Mathf.Min(width, leaderboardHeaderText.rectTransform.rect.width);
                fontSize = Mathf.Min(fontSize, leaderboardHeaderText.fontSize);
            }
            if (width > 1f && width < float.MaxValue) fontSize = Mathf.Min(fontSize, width / lineWidthInEm);

            foreach (TMP_Text text in new[] { leaderboardHeaderText, leaderboardText })
            {
                if (text == null) continue;
                text.enableAutoSizing = false;
                text.fontSize = fontSize;
                text.alignment = TextAlignmentOptions.TopLeft;
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Overflow;
            }

            if (leaderboardHeaderText != null) leaderboardHeaderText.fontStyle = FontStyles.Bold;
            if (leaderboardText != null) leaderboardText.fontStyle = FontStyles.Normal;
        }

        /// <summary>
        /// Scrollt die Liste so, dass der eigene (hervorgehobene) Eintrag sichtbar ist -
        /// ohne eigenen Eintrag nach ganz oben. Wartet einen Frame, bis die Liste ihre neue Höhe hat.
        /// </summary>
        private IEnumerator ScrollToHighlightedEntry(int count)
        {
            if (leaderboardScrollRect == null) yield break;

            yield return null;
            Canvas.ForceUpdateCanvases();

            if (_highlightedEntryIndex < 0 || count <= 1)
            {
                leaderboardScrollRect.verticalNormalizedPosition = 1f; // ganz oben
            }
            else
            {
                leaderboardScrollRect.verticalNormalizedPosition = 1f - Mathf.Clamp01(_highlightedEntryIndex / (float)(count - 1));
            }
        }

        private static LeaderboardData LoadLeaderboard()
        {
            string json = PlayerPrefs.GetString(LeaderboardKey, "");
            if (string.IsNullOrEmpty(json)) return new LeaderboardData();
            LeaderboardData data = JsonUtility.FromJson<LeaderboardData>(json);
            return data ?? new LeaderboardData();
        }

        private static void SaveLeaderboard(LeaderboardData data)
        {
            PlayerPrefs.SetString(LeaderboardKey, JsonUtility.ToJson(data));
            PlayerPrefs.Save();
        }

        [ContextMenu("Bestenliste löschen")]
        private void ClearLeaderboard()
        {
            PlayerPrefs.DeleteKey(LeaderboardKey);
            PlayerPrefs.Save();
            _leaderboard = new LeaderboardData();
            Debug.Log("EndlessModeController: Bestenliste gelöscht.");
        }

        private void Retry()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        private void GoToMainMenu()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(mainMenuSceneName);
        }

        // ==================================================================
        // UPGRADE-AUSWAHL NACH JEDER WELLE
        // Das komplette Menü wird hier per Script gebaut (eigener Canvas über allem),
        // im Look des Spiels: Pergament, dunkelbraune Umrandung, leicht schiefe Karten.
        // ==================================================================

        private enum UpgradeIcon { Sword, Shield, Heart, Cross, Drop, Ring, Speed, Clover, Bolt }

        private class UpgradeOption
        {
            public string Id;
            public string Tag;          // kleines Schild oben links, z.B. "KARTE 1"
            public string Title;
            public string Description;
            public UpgradeIcon Icon;
            public Color Color;
            public int BaseCost;
            public int MaxLevel;
            public System.Action Apply;
        }

        private class ShopCardView
        {
            public RectTransform Rect;
            public CanvasGroup Group;
            public Vector2 BasePosition;
            public float BaseRotation;
            public float Delay;
            public float AppearTimer;
            public float Hover;          // 0..1, weich animiert
            public bool Hovered;
            public bool Interactable = true;
            public bool Chosen;
            public bool IsSkipButton;
            public float Shake;          // >0 = wackelt kurz (z.B. Taste gedrückt, aber zu teuer)
        }

        // Farben aus dem Rhythmancy-Look
        private static readonly Color ShopBrown = new Color32(0x3D, 0x2A, 0x1F, 0xFF);
        private static readonly Color ShopBrownSoft = new Color32(0x6B, 0x51, 0x40, 0xFF);
        private static readonly Color ShopParchment = new Color32(0xF4, 0xE6, 0xC8, 0xFF);
        private static readonly Color ShopCream = new Color32(0xFB, 0xF6, 0xEA, 0xFF);
        private static readonly Color ShopGold = new Color32(0xD9, 0xB2, 0x6A, 0xFF);
        private static readonly Color ShopAffordable = new Color32(0x3E, 0x8E, 0x2E, 0xFF);
        private static readonly Color ShopTooExpensive = new Color32(0xC0, 0x39, 0x2B, 0xFF);

        private readonly List<UpgradeOption> _upgradePool = new List<UpgradeOption>();
        private readonly Dictionary<string, int> _upgradeLevels = new Dictionary<string, int>();
        private readonly List<ShopCardView> _shopViews = new List<ShopCardView>();
        private GameObject _shopCanvas;
        // Für die Tastatur-Auswahl (1/2/3): Angebote und ihre Karten in derselben Reihenfolge
        private readonly List<UpgradeOption> _shopOffers = new List<UpgradeOption>();
        private readonly List<ShopCardView> _shopOfferViews = new List<ShopCardView>();
        private ShopCardView _skipView;
        private bool _shopOpen;
        private bool _shopChoiceMade;
        private Sprite _roundedSprite;
        private Sprite _circleSprite;

        private void BuildUpgradePool()
        {
            _upgradePool.Clear();
            if (cardAbilities == null) cardAbilities = FindFirstObjectByType<CardAbilityController>();

            Color red = new Color32(0xD6, 0x8A, 0x84, 0xFF), blue = new Color32(0x67, 0xB0, 0xE0, 0xFF), green = new Color32(0x9D, 0xBB, 0x7A, 0xFF);
            Color cyan = new Color32(0x6A, 0xAB, 0xA3, 0xFF), mag = new Color32(0xB0, 0x6B, 0x94, 0xFF), orange = new Color32(0xE6, 0xA2, 0x7A, 0xFF);
            Color yellow = new Color32(0xE4, 0xD3, 0x8A, 0xFF);

            // --- allgemeine Verbesserungen ---
            AddUpgrade("dmg", "ALLGEMEIN", "Schärfere Töne", "+10 % Schaden\nfür jede Noten-Welle", UpgradeIcon.Sword, red, 4, 10,
                () => player?.AddDamageBonusPercent(0.10f));
            AddUpgrade("armor", "ALLGEMEIN", "Dickes Fell", "Du nimmst 10 %\nweniger Schaden", UpgradeIcon.Shield, blue, 5, 6,
                () => player?.AddDamageResistancePercent(0.10f));
            AddUpgrade("hp", "ALLGEMEIN", "Zähes Herz", "+15 maximale HP\n(und sofort geheilt)", UpgradeIcon.Heart, red, 4, 10,
                () => player?.AddMaxHealth(15));
            AddUpgrade("radius", "ALLGEMEIN", "Weiter Klang", "Noten-Welle\n+0,4 Reichweite", UpgradeIcon.Ring, cyan, 4, 8,
                () => player?.AddAuraRadius(0.4f));
            AddUpgrade("speed", "ALLGEMEIN", "Leichte Schritte", "+8 % Lauftempo", UpgradeIcon.Speed, yellow, 3, 6,
                () => player?.AddMoveSpeedPercent(0.08f));
            AddUpgrade("luck", "ALLGEMEIN", "Glückspilz", "Orbs fallen öfter\n(+3 % Chance)", UpgradeIcon.Clover, green, 3, 5,
                () => player?.AddOrbDropChance(0.03f));
            AddUpgrade("mana", "ALLGEMEIN", "Manafluss", "+15 % schnellere\nMana-Regeneration", UpgradeIcon.Drop, mag, 4, 8,
                () => cardAbilities?.AddManaRegenPercent(0.15f));

            // --- Karten-Verbesserungen ---
            AddUpgrade("def_heal", "KARTE 1", "Heilklang", "Verteidigung heilt\n+1 HP mehr pro Tick", UpgradeIcon.Cross, green, 3, 6,
                () => cardAbilities?.AddDefenseHealAmount(1));
            AddUpgrade("def_armor", "KARTE 1", "Panzerung", "Verteidigung blockt\n5 % mehr Schaden", UpgradeIcon.Shield, blue, 4, 6,
                () => cardAbilities?.AddDefenseReduction(0.05f));
            AddUpgrade("atk_dmg", "KARTE 2", "Wucht", "Angriff gibt\n+5 Schaden extra", UpgradeIcon.Sword, orange, 4, 8,
                () => cardAbilities?.AddAttackDamage(5));
            AddUpgrade("atk_range", "KARTE 2", "Fernklang", "Angriff gibt\n+0,5 Reichweite extra", UpgradeIcon.Ring, orange, 3, 6,
                () => cardAbilities?.AddAttackRadius(0.5f));
            AddUpgrade("spc_stun", "KARTE 3", "Lange Stille", "Spezial betäubt\n1 Sekunde länger", UpgradeIcon.Bolt, mag, 4, 5,
                () => cardAbilities?.AddSpecialStunDuration(1f));
            AddUpgrade("spc_radius", "KARTE 3", "Druckwelle", "Spezial erreicht\n+1 Reichweite", UpgradeIcon.Ring, mag, 4, 5,
                () => cardAbilities?.AddSpecialRadius(1f));
        }

        private void AddUpgrade(string id, string tag, string title, string description, UpgradeIcon icon, Color color, int baseCost, int maxLevel, System.Action apply)
        {
            _upgradePool.Add(new UpgradeOption
            {
                Id = id,
                Tag = tag,
                Title = title,
                Description = description,
                Icon = icon,
                Color = color,
                BaseCost = baseCost,
                MaxLevel = maxLevel,
                Apply = apply
            });
        }

        private int GetUpgradeLevel(UpgradeOption option) => _upgradeLevels.TryGetValue(option.Id, out int level) ? level : 0;

        /// <summary>Preis steigt mit jedem Kauf derselben Verbesserung.</summary>
        private int GetUpgradeCost(UpgradeOption option)
        {
            int cost = option.BaseCost + GetUpgradeLevel(option) * upgradeCostIncreasePerPurchase;
            return Mathf.Max(1, Mathf.RoundToInt(cost * upgradeCostMultiplier));
        }

        private List<UpgradeOption> PickRandomOffers(int count)
        {
            List<UpgradeOption> available = _upgradePool.FindAll(o => GetUpgradeLevel(o) < o.MaxLevel);
            List<UpgradeOption> picked = new List<UpgradeOption>();
            while (picked.Count < count && available.Count > 0)
            {
                int index = Random.Range(0, available.Count);
                picked.Add(available[index]);
                available.RemoveAt(index);
            }
            return picked;
        }

        /// <summary>Pausiert das Spiel, zeigt die Auswahl und wartet, bis gekauft oder übersprungen wurde.</summary>
        private IEnumerator RunUpgradeShop()
        {
            List<UpgradeOption> offers = PickRandomOffers(3);
            if (offers.Count == 0) yield break; // alles ausgemaxt

            _shopOpen = true;
            _shopChoiceMade = false;
            PauseMenuController.BlockPauseInput = true;
            Time.timeScale = 0f;
            if (beatLane != null) beatLane.SetActive(false);

            BuildShopUI(offers);

            while (!_shopChoiceMade) yield return null;
            yield return new WaitForSecondsRealtime(0.55f); // kurz "Gekauft!" zeigen

            if (_shopCanvas != null) Destroy(_shopCanvas);
            _shopViews.Clear();
            _shopOpen = false;
            PauseMenuController.BlockPauseInput = false;
            if (!_isGameOver)
            {
                Time.timeScale = 1f;
                if (beatLane != null) beatLane.SetActive(true);
            }
        }

        // ------------------------------------------------------------------
        // UI-Aufbau
        // ------------------------------------------------------------------

        private void BuildShopUI(List<UpgradeOption> offers)
        {
            EnsureShopSprites();
            _shopViews.Clear();
            _shopOffers.Clear();
            _shopOfferViews.Clear();
            _skipView = null;

            _shopCanvas = new GameObject("UpgradeShopCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = _shopCanvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            CanvasScaler scaler = _shopCanvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            RectTransform root = (RectTransform)_shopCanvas.transform;

            // Abgedunkelter Hintergrund (blockiert auch Klicks ins Spiel)
            Image dim = CreateImage(root, "Dim", null, new Color(0.11f, 0.07f, 0.05f, 0.72f), Vector2.zero, Vector2.zero);
            Stretch(dim.rectTransform);
            dim.raycastTarget = true;

            // Banner mit Überschrift
            RectTransform banner = CreatePanel(root, "Banner", new Vector2(880f, 118f), new Vector2(0f, 395f), ShopParchment, 6f);
            banner.localRotation = Quaternion.Euler(0f, 0f, -1.2f);
            CreateText(banner, $"Welle {Wave} geschafft!", 58f, ShopBrown, FontStyles.Bold, new Vector2(840f, 100f), Vector2.zero);

            CreateText(root, "Such dir <b>eine</b> Verbesserung aus – oder spar deine Münzen.", 30f, ShopCream, FontStyles.Normal,
                new Vector2(1200f, 50f), new Vector2(0f, 300f));

            // Karten
            float[] xPositions = offers.Count == 1 ? new[] { 0f } : offers.Count == 2 ? new[] { -220f, 220f } : new[] { -430f, 0f, 430f };
            for (int i = 0; i < offers.Count; i++)
            {
                CreateUpgradeCard(root, offers[i], i + 1, new Vector2(xPositions[i], -10f), Random.Range(-2.6f, 2.6f), 0.08f + i * 0.09f);
            }

            // Überspringen
            CreateSkipButton(root, new Vector2(0f, -405f), 0.08f + offers.Count * 0.09f);
        }

        private void CreateUpgradeCard(RectTransform parent, UpgradeOption option, int keyNumber, Vector2 position, float rotation, float delay)
        {
            int cost = GetUpgradeCost(option);
            bool affordable = GameSession.CoinCount >= cost;
            int level = GetUpgradeLevel(option);
            Vector2 size = new Vector2(350f, 490f);

            // Wurzel (wird animiert)
            RectTransform card = new GameObject("UpgradeCard_" + option.Id, typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
            card.SetParent(parent, false);
            card.sizeDelta = size;
            card.anchoredPosition = position;

            // Schatten nach unten rechts (Licht von oben links)
            Image shadow = CreateImage(card, "Shadow", _roundedSprite, new Color(0f, 0f, 0f, 0.35f), size, new Vector2(10f, -12f), true);
            shadow.raycastTarget = false;

            // Umrandung = klickbare Fläche
            Image border = CreateImage(card, "Border", _roundedSprite, ShopBrown, size, Vector2.zero, true);
            CreateImage(card, "Paper", _roundedSprite, ShopParchment, size - new Vector2(12f, 12f), Vector2.zero, true);

            // farbiger Streifen oben (Familienfarbe)
            Image stripe = CreateImage(card, "Stripe", _roundedSprite, Color.Lerp(option.Color, ShopParchment, 0.55f), new Vector2(size.x - 12f, 150f), new Vector2(0f, 164f), true);
            stripe.raycastTarget = false;

            // Kategorie-Schild oben links
            RectTransform tag = CreatePanel(card, "Tag", new Vector2(124f, 36f), new Vector2(-96f, 222f), ShopBrown, 0f);
            tag.localRotation = Quaternion.Euler(0f, 0f, -4f);
            CreateText(tag, option.Tag, 18f, ShopCream, FontStyles.Bold, new Vector2(120f, 32f), Vector2.zero);

            // Tasten-Hinweis oben rechts (1 / 2 / 3)
            RectTransform keyCap = CreatePanel(card, "KeyHint", new Vector2(48f, 48f), new Vector2(132f, 210f), ShopCream, 3f);
            keyCap.localRotation = Quaternion.Euler(0f, 0f, 5f);
            CreateText(keyCap, keyNumber.ToString(), 28f, ShopBrown, FontStyles.Bold, new Vector2(44f, 44f), Vector2.zero);

            // Icon in rundem Abzeichen
            Image badgeBorder = CreateImage(card, "IconBorder", _circleSprite, ShopBrown, new Vector2(128f, 128f), new Vector2(0f, 130f));
            badgeBorder.raycastTarget = false;
            Image badge = CreateImage(card, "IconBadge", _circleSprite, option.Color, new Vector2(114f, 114f), new Vector2(0f, 130f));
            badge.raycastTarget = false;
            BuildIcon(badge.rectTransform, option.Icon);

            // Überschrift + Strichlinie + Beschreibung
            CreateText(card, option.Title, 34f, ShopBrown, FontStyles.Bold, new Vector2(320f, 50f), new Vector2(0f, 34f));
            for (int d = 0; d < 9; d++)
            {
                Image dash = CreateImage(card, "Dash", null, new Color(ShopBrown.r, ShopBrown.g, ShopBrown.b, 0.35f), new Vector2(18f, 3f), new Vector2(-112f + d * 28f, 2f));
                dash.raycastTarget = false;
            }
            TMP_Text description = CreateText(card, option.Description, 25f, ShopBrownSoft, FontStyles.Normal, new Vector2(300f, 110f), new Vector2(0f, -62f));
            description.alignment = TextAlignmentOptions.Top;

            // Preis-Feld unten: Text grün = leistbar, rot = zu teuer; Leiste darunter in derselben Farbe
            Color costColor = affordable ? ShopAffordable : ShopTooExpensive;
            RectTransform costBox = CreatePanel(card, "Cost", new Vector2(290f, 74f), new Vector2(0f, -170f), ShopCream, 4f);
            CreateCoinIcon(costBox, new Vector2(-46f, 0f), 42f);
            CreateText(costBox, cost.ToString(), 44f, costColor, FontStyles.Bold, new Vector2(120f, 64f), new Vector2(26f, 0f));
            Image bar = CreateImage(card, "CostBar", _roundedSprite, costColor, new Vector2(290f, 16f), new Vector2(0f, -222f), true);
            bar.raycastTarget = false;

            // kleine Info-Zeile über dem Preis: Stufe (falls schon gekauft) und/oder "zu wenig Münzen"
            string info = level > 0 ? $"Stufe {level + 1}" : "";
            if (!affordable) info = string.IsNullOrEmpty(info) ? "zu wenig Münzen" : info + "  ·  zu wenig Münzen";
            if (!string.IsNullOrEmpty(info))
            {
                CreateText(card, info, 18f, affordable ? ShopBrownSoft : ShopTooExpensive, FontStyles.Bold | FontStyles.Italic, new Vector2(300f, 26f), new Vector2(0f, -122f));
            }

            ShopCardView view = new ShopCardView
            {
                Rect = card,
                Group = card.GetComponent<CanvasGroup>(),
                BasePosition = position,
                BaseRotation = rotation,
                Delay = delay,
                Interactable = affordable
            };
            view.Group.alpha = 0f; // wird in der Einflieg-Animation eingeblendet
            _shopViews.Add(view);
            _shopOffers.Add(option);
            _shopOfferViews.Add(view);

            // Unsichtbare, NICHT animierte Klickfläche: würde der Button mit der Karte mitwandern
            // (Hover hebt sie an), könnte die Maus am Rand ständig rein/raus "flackern" -> Hover-Sound-Spam.
            GameObject hitArea = CreateHitArea(parent, "HitArea_" + option.Id, size + new Vector2(0f, 30f), position + new Vector2(0f, 8f));
            Button button = hitArea.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.interactable = affordable;
            button.onClick.AddListener(() => BuyUpgrade(option, view));
            AddHoverEvents(hitArea, view);
        }

        /// <summary>Unsichtbare Fläche, die Klicks/Hover annimmt, aber selbst nie bewegt wird.</summary>
        private GameObject CreateHitArea(RectTransform parent, string name, Vector2 size, Vector2 position)
        {
            Image hit = CreateImage(parent, name, null, new Color(0f, 0f, 0f, 0f), size, position);
            hit.raycastTarget = true;
            hit.rectTransform.SetAsLastSibling();
            return hit.gameObject;
        }

        private void CreateSkipButton(RectTransform parent, Vector2 position, float delay)
        {
            Vector2 size = new Vector2(340f, 84f);
            RectTransform skip = new GameObject("SkipButton", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
            skip.SetParent(parent, false);
            skip.sizeDelta = size;
            skip.anchoredPosition = position;

            Image shadow = CreateImage(skip, "Shadow", _roundedSprite, new Color(0f, 0f, 0f, 0.35f), size, new Vector2(7f, -8f), true);
            shadow.raycastTarget = false;
            CreateImage(skip, "Border", _roundedSprite, ShopBrown, size, Vector2.zero, true);
            CreateImage(skip, "Fill", _roundedSprite, ShopCream, size - new Vector2(10f, 10f), Vector2.zero, true);
            CreateText(skip, "Überspringen", 34f, ShopBrown, FontStyles.Bold, size, Vector2.zero);
            CreateText(skip, "(Leertaste)", 24f, ShopCream, FontStyles.Normal, new Vector2(300f, 34f), new Vector2(0f, -66f));

            ShopCardView view = new ShopCardView
            {
                Rect = skip,
                Group = skip.GetComponent<CanvasGroup>(),
                BasePosition = position,
                BaseRotation = 1.2f,
                Delay = delay,
                IsSkipButton = true
            };
            view.Group.alpha = 0f;
            _shopViews.Add(view);
            _skipView = view;

            // Feste Klickfläche über Button UND "(Leertaste)"-Text - keine Lücke dazwischen, bewegt sich nicht mit.
            GameObject hitArea = CreateHitArea(parent, "HitArea_Skip", new Vector2(size.x, 130f), position + new Vector2(0f, -18f));
            Button button = hitArea.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(SkipUpgradeShop);
            AddHoverEvents(hitArea, view);
        }

        private void SkipUpgradeShop()
        {
            if (_shopChoiceMade) return;
            _shopChoiceMade = true;
            if (_skipView != null) _skipView.Chosen = true;
        }

        /// <summary>
        /// Tastatur in der Upgrade-Auswahl: 1/2/3 kauft die jeweilige Karte (falls genug Münzen),
        /// Leertaste überspringt. Erst nachdem die Karten eingeflogen sind, damit ein noch
        /// gedrückter Finger aus dem Kampf nicht sofort etwas auswählt.
        /// </summary>
        private void HandleShopKeyboard()
        {
            if (!_shopOpen || _shopChoiceMade || Keyboard.current == null) return;
            if (_skipView == null || _skipView.AppearTimer < _skipView.Delay + 0.3f) return;

            if (Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                SkipUpgradeShop();
                if (AudioManager.Instance != null) AudioManager.Instance.PlayButtonClickSound();
                return;
            }

            if (Keyboard.current.digit1Key.wasPressedThisFrame) BuyByKey(0);
            else if (Keyboard.current.digit2Key.wasPressedThisFrame) BuyByKey(1);
            else if (Keyboard.current.digit3Key.wasPressedThisFrame) BuyByKey(2);
        }

        private void BuyByKey(int index)
        {
            if (index >= _shopOffers.Count) return;

            ShopCardView view = _shopOfferViews[index];
            if (!view.Interactable)
            {
                // Zu teuer: kurzes Kopfschütteln der Karte als Rückmeldung
                view.Shake = 0.35f;
                return;
            }
            BuyUpgrade(_shopOffers[index], view);
        }

        private void BuyUpgrade(UpgradeOption option, ShopCardView view)
        {
            if (_shopChoiceMade) return;

            int cost = GetUpgradeCost(option);
            if (!GameSession.SpendCoins(cost)) return;

            option.Apply?.Invoke();
            _upgradeLevels[option.Id] = GetUpgradeLevel(option) + 1;
            _shopChoiceMade = true;
            view.Chosen = true;

            if (player != null) player.RefreshCoinUI();

            // schräger "Gekauft!"-Stempel auf der Karte
            TMP_Text stamp = CreateText(view.Rect, "GEKAUFT!", 54f, ShopTooExpensive, FontStyles.Bold, new Vector2(340f, 80f), new Vector2(0f, 40f));
            stamp.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 14f);
            stamp.outlineWidth = 0.15f;
            stamp.outlineColor = ShopCream;

            if (AudioManager.Instance != null) AudioManager.Instance.PlayCoinPickupSound(Vector3.zero);
        }

        private void AddHoverEvents(GameObject target, ShopCardView view)
        {
            EventTrigger trigger = target.AddComponent<EventTrigger>();
            EventTrigger.Entry enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => view.Hovered = true);
            EventTrigger.Entry exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => view.Hovered = false);
            trigger.triggers.Add(enter);
            trigger.triggers.Add(exit);
        }

        /// <summary>Einflieg-, Hover- und Auswahl-Animation (läuft mit Echtzeit, da das Spiel pausiert ist).</summary>
        private void UpdateShopAnimations()
        {
            if (!_shopOpen) return;
            float dt = Time.unscaledDeltaTime;

            foreach (ShopCardView view in _shopViews)
            {
                if (view.Rect == null) continue;

                view.AppearTimer += dt;
                float appear = Mathf.Clamp01((view.AppearTimer - view.Delay) / 0.38f);
                float eased = 1f - Mathf.Pow(1f - appear, 3f);                    // schnell rein, sanft ankommen
                float overshoot = Mathf.Sin(appear * Mathf.PI) * 0.06f;             // kleines Nachfedern

                bool canHover = !_shopChoiceMade && (view.Interactable || view.IsSkipButton);
                view.Hover = Mathf.MoveTowards(view.Hover, view.Hovered && canHover ? 1f : 0f, dt * 7f);
                float hover = view.Hover * view.Hover * (3f - 2f * view.Hover);

                float scale = Mathf.Lerp(0.85f, 1f, eased) + overshoot + 0.06f * hover;
                float lift = 16f * hover;
                float alphaTarget = view.Interactable || view.IsSkipButton ? 1f : 0.72f;

                if (_shopChoiceMade)
                {
                    if (view.Chosen)
                    {
                        scale += 0.08f;
                        lift += 24f;
                    }
                    else
                    {
                        alphaTarget = 0.3f;
                    }
                }

                float shakeX = 0f;
                if (view.Shake > 0f)
                {
                    view.Shake = Mathf.Max(0f, view.Shake - dt);
                    shakeX = Mathf.Sin(view.Shake * 70f) * 14f * (view.Shake / 0.35f);
                }

                view.Rect.anchoredPosition = view.BasePosition + new Vector2(shakeX, -520f * (1f - eased) + lift);
                view.Rect.localScale = Vector3.one * scale;
                view.Rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(view.BaseRotation - 8f, view.BaseRotation, eased) * (1f - hover));
                view.Group.alpha = Mathf.MoveTowards(view.Group.alpha, appear <= 0f ? 0f : alphaTarget, dt * 4f);
            }
        }

        // ------------------------------------------------------------------
        // Icons - aus einfachen Formen zusammengesetzt (keine Bilddateien nötig)
        // ------------------------------------------------------------------

        private void BuildIcon(RectTransform badge, UpgradeIcon icon)
        {
            Color ink = ShopCream;
            switch (icon)
            {
                case UpgradeIcon.Sword:
                    {
                        RectTransform sword = CreateGroup(badge, -45f);
                        IconPart(sword, null, ink, new Vector2(14f, 62f), new Vector2(0f, 12f));     // Klinge
                        IconPart(sword, null, ink, new Vector2(40f, 10f), new Vector2(0f, -20f));    // Parierstange
                        IconPart(sword, null, ink, new Vector2(10f, 20f), new Vector2(0f, -34f));    // Griff
                        IconPart(sword, null, ink, new Vector2(12f, 12f), new Vector2(0f, 44f), 45f); // Spitze
                        break;
                    }
                case UpgradeIcon.Shield:
                    IconPart(badge, _roundedSprite, ink, new Vector2(56f, 44f), new Vector2(0f, 10f), 0f, true);
                    IconPart(badge, null, ink, new Vector2(40f, 40f), new Vector2(0f, -10f), 45f);
                    IconPart(badge, null, ShopBrown, new Vector2(6f, 46f), new Vector2(0f, 2f));
                    break;
                case UpgradeIcon.Heart:
                    IconPart(badge, _circleSprite, ink, new Vector2(34f, 34f), new Vector2(-13f, 10f));
                    IconPart(badge, _circleSprite, ink, new Vector2(34f, 34f), new Vector2(13f, 10f));
                    IconPart(badge, null, ink, new Vector2(36f, 36f), new Vector2(0f, -6f), 45f);
                    break;
                case UpgradeIcon.Cross:
                    IconPart(badge, null, ink, new Vector2(18f, 58f), Vector2.zero);
                    IconPart(badge, null, ink, new Vector2(58f, 18f), Vector2.zero);
                    break;
                case UpgradeIcon.Drop:
                    IconPart(badge, _circleSprite, ink, new Vector2(44f, 44f), new Vector2(0f, -8f));
                    IconPart(badge, null, ink, new Vector2(31f, 31f), new Vector2(0f, 9f), 45f);
                    break;
                case UpgradeIcon.Ring:
                    IconPart(badge, _circleSprite, ink, new Vector2(70f, 70f), Vector2.zero);
                    IconPart(badge, _circleSprite, ShopBrown, new Vector2(56f, 56f), Vector2.zero);
                    IconPart(badge, _circleSprite, ink, new Vector2(42f, 42f), Vector2.zero);
                    IconPart(badge, _circleSprite, ShopBrown, new Vector2(28f, 28f), Vector2.zero);
                    IconPart(badge, _circleSprite, ink, new Vector2(12f, 12f), Vector2.zero);
                    break;
                case UpgradeIcon.Speed:
                    for (int i = 0; i < 3; i++)
                    {
                        float x = -18f + i * 18f;
                        IconPart(badge, null, ink, new Vector2(8f, 26f), new Vector2(x, 8f), -35f);
                        IconPart(badge, null, ink, new Vector2(8f, 26f), new Vector2(x, -8f), 35f);
                    }
                    break;
                case UpgradeIcon.Clover:
                    IconPart(badge, _circleSprite, ink, new Vector2(28f, 28f), new Vector2(0f, 15f));
                    IconPart(badge, _circleSprite, ink, new Vector2(28f, 28f), new Vector2(0f, -15f));
                    IconPart(badge, _circleSprite, ink, new Vector2(28f, 28f), new Vector2(15f, 0f));
                    IconPart(badge, _circleSprite, ink, new Vector2(28f, 28f), new Vector2(-15f, 0f));
                    IconPart(badge, null, ink, new Vector2(6f, 26f), new Vector2(10f, -30f), 30f);
                    break;
                case UpgradeIcon.Bolt:
                    IconPart(badge, null, ink, new Vector2(14f, 40f), new Vector2(4f, 16f), 25f);
                    IconPart(badge, null, ink, new Vector2(30f, 10f), new Vector2(0f, 0f), -10f);
                    IconPart(badge, null, ink, new Vector2(14f, 40f), new Vector2(-4f, -16f), 25f);
                    break;
            }
        }

        private RectTransform CreateGroup(RectTransform parent, float rotation)
        {
            RectTransform group = new GameObject("Icon", typeof(RectTransform)).GetComponent<RectTransform>();
            group.SetParent(parent, false);
            group.sizeDelta = Vector2.zero;
            group.localRotation = Quaternion.Euler(0f, 0f, rotation);
            return group;
        }

        private void IconPart(RectTransform parent, Sprite sprite, Color color, Vector2 size, Vector2 position, float rotation = 0f, bool sliced = false)
        {
            Image part = CreateImage(parent, "IconPart", sprite, color, size, position, sliced);
            part.raycastTarget = false;
            part.rectTransform.localRotation = Quaternion.Euler(0f, 0f, rotation);
            if (color != ShopBrown)
            {
                Outline outline = part.gameObject.AddComponent<Outline>(); // dunkle Kontur wie handgezeichnet
                outline.effectColor = ShopBrown;
                outline.effectDistance = new Vector2(2f, -2f);
            }
        }

        private void CreateCoinIcon(RectTransform parent, Vector2 position, float size)
        {
            Image outer = CreateImage(parent, "Coin", _circleSprite, ShopBrown, new Vector2(size, size), position);
            outer.raycastTarget = false;
            Image gold = CreateImage(parent, "CoinGold", _circleSprite, ShopGold, new Vector2(size - 6f, size - 6f), position);
            gold.raycastTarget = false;
            Image inner = CreateImage(parent, "CoinInner", _circleSprite, new Color32(0xF0, 0xD2, 0x8C, 0xFF), new Vector2(size * 0.5f, size * 0.5f), position + new Vector2(-2f, 2f));
            inner.raycastTarget = false;
        }

        // ------------------------------------------------------------------
        // kleine UI-Helfer
        // ------------------------------------------------------------------

        private RectTransform CreatePanel(RectTransform parent, string name, Vector2 size, Vector2 position, Color fill, float borderWidth)
        {
            RectTransform panel = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            panel.SetParent(parent, false);
            panel.sizeDelta = size;
            panel.anchoredPosition = position;

            if (borderWidth > 0f)
            {
                Image border = CreateImage(panel, "Border", _roundedSprite, ShopBrown, size, Vector2.zero, true);
                border.raycastTarget = false;
            }
            Image fillImage = CreateImage(panel, "Fill", _roundedSprite, fill, size - Vector2.one * (borderWidth * 2f), Vector2.zero, true);
            fillImage.raycastTarget = false;
            return panel;
        }

        private Image CreateImage(RectTransform parent, string name, Sprite sprite, Color color, Vector2 size, Vector2 position, bool sliced = false)
        {
            GameObject imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            RectTransform rect = imageObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;

            Image image = imageObject.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            if (sprite != null && sliced)
            {
                image.type = Image.Type.Sliced;
                image.pixelsPerUnitMultiplier = 1f;
            }
            return image;
        }

        private TMP_Text CreateText(RectTransform parent, string text, float fontSize, Color color, FontStyles style, Vector2 size, Vector2 position)
        {
            GameObject textObject = new GameObject("Text", typeof(RectTransform));
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;

            TextMeshProUGUI tmp = textObject.AddComponent<TextMeshProUGUI>();
            if (shopFont != null) tmp.font = shopFont;
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.fontStyle = style;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            return tmp;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>Erzeugt einmalig eine abgerundete Fläche (9-Slice) und einen weichen Kreis als Sprites.</summary>
        private void EnsureShopSprites()
        {
            if (_roundedSprite == null) _roundedSprite = CreateRoundedSprite(64, 22);
            if (_circleSprite == null) _circleSprite = CreateRoundedSprite(128, 64, false);
        }

        private static Sprite CreateRoundedSprite(int size, int radius, bool sliced = true)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Abstand zur "inneren" Rechteckfläche -> weiche, abgerundete Kante
                    float dx = Mathf.Max(radius - (x + 0.5f), (x + 0.5f) - (size - radius), 0f);
                    float dy = Mathf.Max(radius - (y + 0.5f), (y + 0.5f) - (size - radius), 0f);
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01(radius - distance + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();

            Vector4 border = sliced ? new Vector4(radius, radius, radius, radius) : Vector4.zero;
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
        }

    }
}