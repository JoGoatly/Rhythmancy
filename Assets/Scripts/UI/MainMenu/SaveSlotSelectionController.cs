using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using RhythmWitchClone.Core;
using RhythmWitchClone.Save;

namespace RhythmWitchClone.UI.MainMenu
{
    /// <summary>
    /// Zeigt die 3 Speicherstände an. Bei Auswahl wird der Slot in der GameSession
    /// gemerkt und die Spielszene geladen.
    /// </summary>
    public class SaveSlotSelectionController : MonoBehaviour
    {
        [System.Serializable]
        public class SlotUI
        {
            public Button button;
            public TMP_Text label;
        }

        [Header("Genau 3 Einträge (Slot 0, 1, 2)")]
        [SerializeField] private SlotUI[] slots = new SlotUI[3];

        [Header("Navigation")]
        [SerializeField] private Button backButton;
        [SerializeField] private MainMenuController mainMenuController;
        [Tooltip("Szene für einen NEUEN Speicherstand (Slot war leer) - z.B. dein Tutorial-Level.")]
        [SerializeField] private string tutorialSceneName = "Tutorial Level";
        [Tooltip("Szene für einen BEREITS VORHANDENEN Speicherstand.")]
        [SerializeField] private string gameSceneName = "SafeZone";

        private void Awake()
        {
            for (int i = 0; i < slots.Length; i++)
            {
                int slotIndex = i; // lokale Kopie, wichtig für die Closure im Listener
                slots[i].button.onClick.AddListener(() => SelectSlot(slotIndex));
            }

            backButton.onClick.AddListener(() => mainMenuController.ReturnToMainMenu());
        }

        private void OnEnable()
        {
            RefreshSlotLabels();
        }

        private void RefreshSlotLabels()
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].label == null) continue; // Label optional - noch nicht verkabelt

                SaveData data = SaveSystem.Load(i);
                slots[i].label.text = data.exists
                    ? $"Speicherstand {i + 1}\nLevel {data.level}"
                    : $"Speicherstand {i + 1}\n(Leer)";
            }
        }

        private void SelectSlot(int slotIndex)
        {
            GameSession.SelectSaveSlot(slotIndex);

            SaveData data = SaveSystem.Load(slotIndex);
            GameSession.LoadFromSaveData(data);

            // Neuer, bisher leerer Speicherstand -> Tutorial. Bereits vorhandener -> direkt Safe Zone.
            string targetScene = data.exists ? gameSceneName : tutorialSceneName;
            if (data.exists) SceneManager.LoadScene(targetScene);
            else StoryIntroController.Show(targetScene);
        }
    }
}