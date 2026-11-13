using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;
using RhythmWitchClone.Core;
using RhythmWitchClone.Gameplay;

namespace RhythmWitchClone.Levels
{
    /// <summary>
    /// Safe-Zone-Interaktionsbereich: Zeigt ein "E"-Tastenhinweis-UI, sobald der Spieler in der
    /// Nähe ist, und öffnet bei Tastendruck die Level-Auswahl. Später auch für Upgrades
    /// erweiterbar - gleiches Prinzip, einfach ein weiteres Interaktions-Panel + eigene Taste
    /// oder ein zusätzlicher Menüpunkt in der Level-Auswahl.
    ///
    /// Setup:
    /// - Kommt auf ein GameObject mit einem Collider (Is Trigger = an), das den Safe-Zone-
    ///   Interaktionsbereich markiert (z.B. um die Level-Auswahl-Station herum).
    /// - Der Spieler braucht den Tag "Player", damit die Trigger-Erkennung funktioniert.
    /// </summary>
    public class SafeZoneController : MonoBehaviour
    {
        [Header("Interaktion")]
        [Tooltip("UI-Element mit dem 'E'-Tastenhinweis, das erscheint, wenn der Spieler in Reichweite ist.")]
        [SerializeField] private GameObject keyPromptUI;

        [Header("Level-Auswahl")]
        [SerializeField] private GameObject levelSelectPanel;

        [Header("Sonstiges")]
        [Tooltip("Wird ausgeblendet/pausiert, solange die Level-Auswahl offen ist.")]
        [SerializeField] private BeatLaneController beatLane;

        private bool _playerInRange;

        private void Start()
        {
            if (keyPromptUI != null) keyPromptUI.SetActive(false);
            if (levelSelectPanel != null) levelSelectPanel.SetActive(false);
        }

        private void Update()
        {
            if (Time.timeScale <= 0f) return;
            if (!_playerInRange) return;
            if (levelSelectPanel != null && levelSelectPanel.activeSelf) return; // schon offen

            Key key = RhythmancySettings.GetKeyForAction("Interaktion");
            if (Keyboard.current != null && key != Key.None && Keyboard.current[key].wasPressedThisFrame)
            {
                OpenLevelSelect();
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;

            _playerInRange = true;
            ShowInteractionPrompt();
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag("Player")) return;

            _playerInRange = false;
            if (keyPromptUI != null) keyPromptUI.SetActive(false);
        }

        private void OpenLevelSelect()
        {
            if (keyPromptUI != null) keyPromptUI.SetActive(false);
            if (levelSelectPanel != null) levelSelectPanel.SetActive(true);
            if (beatLane != null) beatLane.SetActive(false);
        }

        /// <summary>
        /// Public, damit LevelSelectController.onExitLevelSelect (UnityEvent, im Inspector
        /// verkabelt) das aufrufen kann, wenn man die Level-Auswahl komplett verlässt.
        /// </summary>
        public void CloseLevelSelect()
        {
            if (levelSelectPanel != null) levelSelectPanel.SetActive(false);
            if (_playerInRange) ShowInteractionPrompt();
            if (beatLane != null) beatLane.SetActive(true);
        }

        private void ShowInteractionPrompt()
        {
            if (keyPromptUI == null) return;
            TMP_Text label = keyPromptUI.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = $"[{RhythmancySettings.GetBinding("Interaktion")}]  LEVEL AUSWÄHLEN";
            keyPromptUI.SetActive(true);
        }
    }
}
