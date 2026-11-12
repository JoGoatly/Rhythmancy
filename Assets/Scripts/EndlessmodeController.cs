using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using RhythmWitchClone.Gameplay;
using RhythmWitchClone.UI.Pause;
using RhythmWitchClone.Audio;

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

        [Header("Punkte")]
        [SerializeField] private int pointsPerKill = 10;
        [Tooltip("Zusätzliche Punkte pro Kill für jede Welle nach der ersten (Welle 5 = 10 + 4*5 = 30 pro Kill).")]
        [SerializeField] private int extraKillPointsPerWave = 5;
        [Tooltip("Bonus für eine geschaffte Welle, wird mit der Wellennummer multipliziert.")]
        [SerializeField] private int waveClearBonus = 50;

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

            _leaderboard = LoadLeaderboard();
            Score = 0;
            UpdateHud();

            _waveRoutine = StartCoroutine(WaveLoop());
        }

        private void Update()
        {
            UpdateAnnouncement();
            UpdateScorePunch();

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
                yield return new WaitForSeconds(timeBetweenWaves);
            }
        }

        private void ApplyKeyCount()
        {
            if (beatLane == null) return;

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
    }
}