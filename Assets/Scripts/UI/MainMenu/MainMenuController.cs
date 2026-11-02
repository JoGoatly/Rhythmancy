using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using UnityEngine.InputSystem;

namespace RhythmWitchClone.UI.MainMenu
{
    /// <summary>
    /// Steuert das Hauptmenü mit den drei Buttons: Spielen, Einstellungen, Verlassen.
    /// "Spielen" öffnet die Speicherstand-Auswahl, "Einstellungen" öffnet das Einstellungsmenü,
    /// "Verlassen" schließt das Spiel.
    /// </summary>
    public class MainMenuController : MonoBehaviour
    {
        [Header("Buttons")]
        [SerializeField] private Button playButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button quitButton;

        [Header("Panels")]
        [SerializeField] private GameObject mainMenuPanel;
        [SerializeField] private GameObject saveSlotPanel;
        [SerializeField] private GameObject settingsPanel;

        [Header("Hintergrund-Video")]
        [Tooltip("VideoPlayer, der das Loop-Video im Hintergrund abspielt.")]
        [SerializeField] private VideoPlayer backgroundVideoPlayer;

        [Header("Video-Parallax")]
        [Tooltip("RectTransform des Hintergrund-Videobilds (RawImage). Sollte etwas größer als der Bildschirm sein, damit beim Verschieben keine Ränder sichtbar werden.")]
        [SerializeField] private RectTransform videoParallaxTarget;
        [Tooltip("Wie stark sich das Video zur Maus hin verschiebt (in UI-Pixeln).")]
        [SerializeField] private float parallaxStrength = 30f;
        [Tooltip("Wie schnell die Verschiebung der Mausbewegung folgt (höher = direkter).")]
        [SerializeField] private float parallaxSmoothing = 5f;

        private Vector2 _videoParallaxBasePosition;

        private void Awake()
        {
            playButton.onClick.AddListener(OpenSaveSlotSelection);
            settingsButton.onClick.AddListener(OpenSettings);
            quitButton.onClick.AddListener(QuitGame);

            if (videoParallaxTarget != null)
            {
                _videoParallaxBasePosition = videoParallaxTarget.anchoredPosition;
            }
        }

        private void Start()
        {
            ShowOnly(mainMenuPanel);

            if (backgroundVideoPlayer != null)
            {
                backgroundVideoPlayer.isLooping = true;
                backgroundVideoPlayer.Play();
            }
        }

        private void Update()
        {
            UpdateVideoParallax();
        }

        /// <summary>
        /// Verschiebt das Hintergrund-Video leicht in Richtung der Mausposition (Parallax-Effekt).
        /// Die Mausposition wird auf -0.5..0.5 relativ zur Bildschirmmitte normalisiert.
        /// </summary>
        private void UpdateVideoParallax()
        {
            if (videoParallaxTarget == null) return;

            if (Mouse.current == null) return;

            Vector2 mousePosition = Mouse.current.position.ReadValue();
            Vector2 viewportPos = new Vector2(
                mousePosition.x / Screen.width,
                mousePosition.y / Screen.height);

            Vector2 centeredOffset = viewportPos - new Vector2(0.5f, 0.5f); // -0.5 .. 0.5
            Vector2 targetOffset = _videoParallaxBasePosition + centeredOffset * 2f * parallaxStrength;

            videoParallaxTarget.anchoredPosition = Vector2.Lerp(
                videoParallaxTarget.anchoredPosition,
                targetOffset,
                Time.deltaTime * parallaxSmoothing);
        }

        private void OpenSaveSlotSelection()
        {
            ShowOnly(saveSlotPanel);
        }

        private void OpenSettings()
        {
            ShowOnly(settingsPanel);
        }

        /// <summary>
        /// Wird von anderen Controllern (Speicherstand-Auswahl, Einstellungen) aufgerufen,
        /// um wieder genau einen Schritt zurück ins Hauptmenü zu gelangen.
        /// </summary>
        public void ReturnToMainMenu()
        {
            ShowOnly(mainMenuPanel);
        }

        private void ShowOnly(GameObject panelToShow)
        {
            mainMenuPanel.SetActive(panelToShow == mainMenuPanel);
            saveSlotPanel.SetActive(panelToShow == saveSlotPanel);
            settingsPanel.SetActive(panelToShow == settingsPanel);
        }

        private void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}