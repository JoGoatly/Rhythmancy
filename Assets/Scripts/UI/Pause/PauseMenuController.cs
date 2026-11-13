using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using TMPro;
using RhythmWitchClone.Core;
using RhythmWitchClone.Save;
using RhythmWitchClone.Audio;
using RhythmWitchClone.Gameplay;

namespace RhythmWitchClone.UI.Pause
{
    /// <summary>
    /// Pause-Menü während des Spiels. Nutzt dasselbe Settings-Prefab/-System wie das Hauptmenü
    /// (SettingsController.cs wird 1:1 wiederverwendet, kein zweites Settings-Skript nötig) -
    /// dadurch sind Hauptmenü- und Pause-Einstellungen automatisch synchron, weil beide denselben
    /// persistenten AudioManager ansprechen.
    ///
    /// Buttons: Fortsetzen, Einstellungen, Speichern, Zum Hauptmenü.
    /// "Zum Hauptmenü" fragt vorher, ob gespeichert werden soll.
    ///
    /// Zeigt außerdem den Game-Over-Bildschirm an, sobald player.CurrentHealth auf 0 fällt
    /// (wird per Polling in Update() erkannt, kein Event-System nötig) - mit "Nochmal"
    /// (Szene neu laden) und "Zum Hauptmenü" (ohne Speicher-Abfrage, da der Spieler tot ist).
    /// </summary>
    public class PauseMenuController : MonoBehaviour
    {
        [Header("Buttons - Pause-Übersicht")]
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button saveButton;
        [SerializeField] private Button toMainMenuButton;

        [Header("Panels")]
        [Tooltip("Das gesamte Pause-Menü (wird beim Pausieren ein-, beim Fortsetzen ausgeblendet).")]
        [SerializeField] private GameObject pauseRootPanel;
        [Tooltip("Die Button-Übersicht (Fortsetzen/Einstellungen/Speichern/...).")]
        [SerializeField] private GameObject pauseButtonsPanel;
        [Tooltip("Dasselbe Settings-Panel-Prefab wie im Hauptmenü (mit SettingsController-Skript drauf).")]
        [SerializeField] private GameObject settingsPanel;

        [Header("Speichern-Abfrage (beim Klick auf 'Zum Hauptmenü')")]
        [SerializeField] private GameObject saveConfirmDialog;
        [SerializeField] private Button confirmSaveAndLeaveButton; // "Ja"
        [SerializeField] private Button declineSaveAndLeaveButton; // "Nein"
        [SerializeField] private Button cancelLeaveButton;         // "Abbrechen"

        [Header("Szene")]
        [SerializeField] private string mainMenuSceneName = "Menü";

        [Header("Speichern")]
        [Tooltip("Der Spieler, dessen Position/Blickrichtung beim Speichern mit gesichert wird. Wird auch für die Game-Over-Erkennung (CurrentHealth) genutzt.")]
        [SerializeField] private PlayerController player;

        [Header("Game Over")]
        [SerializeField] private GameObject gameOverPanel;
        [SerializeField] private Button gameOverMainMenuButton;
        [SerializeField] private Button gameOverRetryButton;

        private bool _isGameOver;

        [Header("Musik-Player (unten rechts)")]
        [SerializeField] private TMP_Text currentTrackLabel;
        [SerializeField] private Button playPauseButton;
        [Tooltip("Die Image-Komponente auf dem Play/Pause-Button, deren Sprite zur Laufzeit gewechselt wird.")]
        [SerializeField] private Image playPauseIconImage;
        [Tooltip("Bild, das angezeigt wird, wenn die Musik GERADE PAUSIERT ist (zum Fortsetzen anklicken - i.d.R. ein Play-Dreieck).")]
        [SerializeField] private Sprite playIconSprite;
        [Tooltip("Bild, das angezeigt wird, wenn die Musik GERADE LÄUFT (zum Pausieren anklicken - i.d.R. zwei Striche).")]
        [SerializeField] private Sprite pauseIconSprite;
        [SerializeField] private Button previousTrackButton;
        [SerializeField] private Button nextTrackButton;

        private bool _isPaused;

        private void Awake()
        {
            resumeButton.onClick.AddListener(Resume);
            settingsButton.onClick.AddListener(OpenSettings);
            saveButton.onClick.AddListener(SaveGame);
            toMainMenuButton.onClick.AddListener(OpenSaveConfirmDialog);

            confirmSaveAndLeaveButton.onClick.AddListener(ConfirmSaveAndLeave);
            declineSaveAndLeaveButton.onClick.AddListener(DeclineSaveAndLeave);
            cancelLeaveButton.onClick.AddListener(CancelLeave);

            playPauseButton.onClick.AddListener(ToggleMusicPlayPause);
            previousTrackButton.onClick.AddListener(PreviousTrack);
            nextTrackButton.onClick.AddListener(NextTrack);

            gameOverMainMenuButton.onClick.AddListener(GoToMainMenu);
            gameOverRetryButton.onClick.AddListener(Retry);

            // Falls kein eigenes Icon-Objekt zugewiesen wurde, direkt die Image-Komponente
            // des Play/Pause-Buttons selbst verwenden.
            if (playPauseIconImage == null)
            {
                playPauseIconImage = playPauseButton.GetComponent<Image>();
            }
        }

        private void Start()
        {
            pauseRootPanel.SetActive(false);
            saveConfirmDialog.SetActive(false);
            if (gameOverPanel != null) gameOverPanel.SetActive(false);
        }

        private void Update()
        {
            // Läuft bewusst unabhängig von _isPaused/Time.timeScale weiter, damit die Anzeige
            // auch während das Spiel pausiert ist (Time.timeScale = 0) aktuell bleibt.
            RefreshMusicDisplay();

            if (!_isGameOver && player != null && player.CurrentHealth <= 0)
            {
                TriggerGameOver();
            }
        }

        private void TriggerGameOver()
        {
            _isGameOver = true;
            Time.timeScale = 0f;
            pauseRootPanel.SetActive(false); // falls Pause gerade offen war, schließen
            if (gameOverPanel != null) gameOverPanel.SetActive(true);

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayGameOverSound();
            }
        }

        private void Retry()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        private void RefreshMusicDisplay()
        {
            if (AudioManager.Instance == null) return;

            if (currentTrackLabel != null)
            {
                currentTrackLabel.text = AudioManager.Instance.GetCurrentTrackName();
            }

            if (playPauseIconImage != null)
            {
                bool isPaused = AudioManager.Instance.IsMusicPaused();
                playPauseIconImage.sprite = isPaused ? pauseIconSprite : playIconSprite;
            }
        }

        private void ToggleMusicPlayPause()
        {
            AudioManager.Instance.ToggleMusicPause();
        }

        private void PreviousTrack()
        {
            AudioManager.Instance.SkipToPreviousTrack();
        }

        private void NextTrack()
        {
            AudioManager.Instance.SkipToNextTrack();
        }

        private void TogglePause()
        {
            if (_isPaused) Resume();
            else Pause();
        }

        private void Pause()
        {
            _isPaused = true;
            Time.timeScale = 0f;
            saveConfirmDialog.SetActive(false);
            pauseRootPanel.SetActive(true);
            ShowPauseButtons();
        }

        private void Resume()
        {
            _isPaused = false;
            Time.timeScale = 1f;
            pauseRootPanel.SetActive(false);
        }

        private void OpenSettings()
        {
            pauseButtonsPanel.SetActive(false);
            settingsPanel.SetActive(true);
        }

        /// <summary>
        /// Wird vom SettingsController per UnityEvent (onExitSettings) aufgerufen, wenn man aus der
        /// Kapitelübersicht "Zurück" drückt. Muss public sein, damit es im Inspector wählbar ist.
        /// </summary>
        public void ShowPauseButtons()
        {
            settingsPanel.SetActive(false);
            pauseButtonsPanel.SetActive(true);
        }

        private void OpenSaveConfirmDialog()
        {
            saveConfirmDialog.SetActive(true);
        }

        private void ConfirmSaveAndLeave()
        {
            SaveGame();
            GoToMainMenu();
        }

        private void DeclineSaveAndLeave()
        {
            GoToMainMenu();
        }

        private void CancelLeave()
        {
            saveConfirmDialog.SetActive(false);
        }

        private void SaveGame()
        {
            if (GameSession.SelectedSaveSlot < 0)
            {
                Debug.LogWarning("Kein Speicherstand ausgewählt - Speichern übersprungen.");
                return;
            }

            SaveData data = SaveSystem.Load(GameSession.SelectedSaveSlot);
            data.playtimeSeconds += Time.realtimeSinceStartup;

            if (player != null)
            {
                player.WriteToSaveData(data);
            }

            GameSession.WriteToSaveData(data);

            // TODO: Sobald weitere Spieldaten existieren (Fortschritt, Punktzahl, etc.),
            // hier die entsprechenden Systeme ebenfalls in "data" eintragen lassen.
            SaveSystem.Save(GameSession.SelectedSaveSlot, data);
        }

        private void GoToMainMenu()
        {
            Time.timeScale = 1f; // wichtig: vor dem Szenenwechsel zurücksetzen, sonst bleibt die neue Szene eingefroren
            SceneManager.LoadScene(mainMenuSceneName);
        }
    }
}
