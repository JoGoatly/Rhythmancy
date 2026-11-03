using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using UnityEngine.InputSystem;
using RhythmWitchClone.Audio;

namespace RhythmWitchClone.Gameplay
{
    /// <summary>
    /// Intro + Tutorial-Dialog-System der Tutorial-Szene.
    ///
    /// INTRO (läuft vor dem Tutorial):
    /// - Vollbild-Panel mit Text unten (Schreibmaschinen-Effekt) und oben beliebig vielen Bildern
    ///   pro Abschnitt, die automatisch nebeneinander angeordnet und sanft eingeblendet werden.
    ///
    /// Jeder Tutorial-Schritt kann das Dialog-Panel zu einem Platzhalter im Canvas wandern lassen
    /// ("Dialogue Panel Position"), damit es dort steht, wo der Schritt gerade etwas erklärt.
    /// - Linksklick oder E: erster Druck während des Schreibens zeigt sofort den ganzen Text, nächster
    ///   Klick geht zum nächsten Abschnitt. Kleiner Klick-Cooldown gegen versehentliche Doppelklicks.
    /// - Nach dem letzten Abschnitt verschwindet das Intro-Panel und das Tutorial startet.
    ///
    /// TUTORIAL - Arten, wie ein Schritt weitergeht:
    /// - "Click To Continue": Linksklick - erster Klick während des Schreibens zeigt sofort den
    ///   kompletten Text, ein weiterer Klick springt zum nächsten Schritt.
    /// - "Wait For Movement": wartet auf WASD-Eingabe.
    /// - "Move To Area": Pfeil um den Spieler zeigt zur Ziel-Zone, weiter sobald erreicht.
    /// - "Wait For Enemy Defeat": wartet, bis der eingetragene Gegner besiegt ist.
    /// - "Wait For Full Health": wartet, bis der Spieler wieder volle HP hat.
    ///
    /// Sobald der letzte Schritt abgeschlossen ist, öffnet sich das Win-Panel des Spielers.
    /// Die automatische "Keine Gegner mehr = Sieg"-Prüfung ist im Tutorial ausgeschaltet.
    ///
    /// Klicks auf Buttons (z.B. Pause-Menü, Win-Panel) zählen NICHT als "Weiter"-Klick, und bei
    /// pausiertem Spiel wird nichts weitergeschaltet.
    /// </summary>
    public class TutorialManager : MonoBehaviour
    {
        public enum StepAdvanceMode
        {
            ClickToContinue,
            WaitForMovement,
            MoveToArea,
            WaitForEnemyDefeat,
            WaitForFullHealth
        }

        [System.Serializable]
        public class IntroSlide
        {
            [TextArea(2, 6)]
            public string text;
            [Tooltip("Bilder, die oben nebeneinander angezeigt werden, sobald dieser Abschnitt beginnt (werden automatisch im Container angeordnet). Leere Liste = die Bilder vom vorherigen Abschnitt bleiben stehen.")]
            public List<Sprite> images = new List<Sprite>();
            [Tooltip("Optional: Sprachaufnahme (Voice Acting) für diesen Abschnitt. Startet mit dem Abschnitt; beim Weiterschalten wird sie sofort gestoppt.")]
            public AudioClip voiceClip;
        }

        [System.Serializable]
        public class TutorialStep
        {
            [TextArea(2, 5)]
            public string text;
            public StepAdvanceMode advanceMode = StepAdvanceMode.ClickToContinue;
            [Tooltip("Optional: Platzhalter im Canvas (leeres UI-Objekt), zu dem das Dialog-Panel bei diesem Schritt hinwandert. Leer = Panel bleibt, wo es gerade ist.")]
            public RectTransform dialoguePanelPosition;
            [Tooltip("Schaltet die Spieler-Bewegung frei, sobald dieser Schritt gezeigt wird (z.B. für 'Probier WASD aus').")]
            public bool unlocksMovement;
            [Tooltip("Fügt dem Spieler sofort diesen Schaden zu, sobald dieser Schritt gezeigt wird (0 = kein Schaden).")]
            public int damageToApplyOnShow;
            [Tooltip("Optional: ein Objekt in der Szene, das NUR WÄHREND dieses Schritts eingeblendet wird.")]
            public GameObject marker;
            [Tooltip("UI-Elemente, die ab diesem Schritt DAUERHAFT eingeblendet werden (z.B. HP-Leiste).")]
            public List<GameObject> uiElementsToReveal = new List<GameObject>();
            [Tooltip("Nur für 'Move To Area': Objekt in der Szene, dessen Position die Mitte der Ziel-Zone markiert.")]
            public Transform targetArea;
            [Tooltip("Nur für 'Move To Area': Radius der Zone - näher als das gilt als 'erreicht'.")]
            public float targetAreaRadius = 1.5f;
            [Tooltip("Nur für 'Wait For Enemy Defeat': der Gegner, der besiegt werden muss.")]
            public EnemyController enemyToDefeat;
            [Tooltip("Nur für 'Wait For Enemy Defeat': Bild, das der Gegner beim Tod auf den Spieler wirft (leer = kein Wurf).")]
            public Sprite thrownObjectSprite;
            [Tooltip("Schaden, den der geworfene Gegenstand beim Einschlag verursacht.")]
            public int thrownObjectDamage;
            [Tooltip("Wie lange der Gegenstand braucht, um beim Spieler anzukommen (Sekunden).")]
            public float thrownObjectFlightDuration = 0.5f;
        }

        [Header("Referenzen")]
        [SerializeField] private PlayerController player;
        [Tooltip("Pfeil-Objekt in der Szene, das bei 'Move To Area'-Schritten um den Spieler kreist und zur Ziel-Zone zeigt.")]
        [SerializeField] private Transform guideArrow;
        [SerializeField] private float guideArrowHeightOffset = 1f;
        [SerializeField] private float guideArrowOrbitRadius = 1.5f;

        [Header("Intro (Vorgeschichte)")]
        [Tooltip("Abschnitte des Intros, der Reihe nach. Leere Liste = kein Intro, Tutorial startet direkt.")]
        [SerializeField] private List<IntroSlide> introSlides = new List<IntroSlide>();
        [Tooltip("Vollbild-Panel des Intros (UI -> Panel).")]
        [SerializeField] private GameObject introPanel;
        [Tooltip("Container oben im Intro mit Horizontal Layout Group - darin werden die Bilder eines Abschnitts automatisch nebeneinander angeordnet.")]
        [SerializeField] private RectTransform introImageContainer;
        [Tooltip("Text unten im Intro (UI -> Text - TextMeshPro).")]
        [SerializeField] private TMP_Text introText;
        [Tooltip("Optional: 'Fortfahren mit Linker Maustaste'-Hinweis im Intro, erscheint wenn der Text fertig ist.")]
        [SerializeField] private GameObject introContinuePrompt;
        [Tooltip("Wie lange neue Intro-Bilder zum Einblenden brauchen (Sekunden).")]
        [SerializeField] private float introImageFadeDuration = 0.5f;
        [Tooltip("Lautstärke der Sprachaufnahmen (wird zusätzlich mit der SFX-Lautstärke aus den Einstellungen multipliziert).")]
        [Range(0f, 1f)][SerializeField] private float introVoiceVolume = 1f;

        [Header("Schritte")]
        [SerializeField] private List<TutorialStep> steps = new List<TutorialStep>();

        [Header("UI")]
        [SerializeField] private GameObject dialoguePanel;
        [SerializeField] private TMP_Text dialogueText;
        [Tooltip("'Fortfahren mit Linker Maustaste'-Hinweis - erscheint nur bei 'Click To Continue'-Schritten, sobald der Text fertig geschrieben ist.")]
        [SerializeField] private GameObject continuePromptUI;
        [Tooltip("Wie schnell das Dialog-Panel zu seiner neuen Position wandert (0 = springt sofort).")]
        [SerializeField] private float dialoguePanelMoveSpeed = 8f;

        [Header("Schreibmaschinen-Effekt / Klick")]
        [SerializeField] private float charactersPerSecond = 30f;
        [Tooltip("Mindestabstand zwischen zwei gezählten Klicks (Sekunden) - verhindert versehentliches Doppelklicken.")]
        [SerializeField] private float clickCooldown = 0.25f;

        private int _currentStepIndex = -1;
        private bool _isTyping;
        private string _currentFullText = "";
        private int _visibleCharacterCount;
        private float _typeTimer;
        private TMP_Text _activeText;
        private float _nextClickAllowedTime;
        private Vector3 _lastKnownEnemyPosition;

        private bool _inIntro;
        private int _currentIntroIndex = -1;
        private float _introImageFadeTimer;
        private readonly List<Image> _currentIntroImages = new List<Image>();
        private RectTransform _dialoguePanelTarget;

        private class ThrownObject
        {
            public Transform Transform;
            public Vector3 StartPosition;
            public float Duration;
            public float Timer;
            public int Damage;
        }

        private readonly List<ThrownObject> _activeThrownObjects = new List<ThrownObject>();

        private AudioSource _voiceSource;
        private bool _voicePausedByGamePause;

        private void Start()
        {
            // Eine einzige AudioSource für alle Sprachaufnahmen - dadurch kann nie mehr als eine gleichzeitig laufen.
            _voiceSource = gameObject.AddComponent<AudioSource>();
            _voiceSource.playOnAwake = false;
            _voiceSource.loop = false;
            _voiceSource.spatialBlend = 0f;

            if (dialoguePanel != null) dialoguePanel.SetActive(false);
            if (continuePromptUI != null) continuePromptUI.SetActive(false);
            HideAllMarkers();

            // Bewegung ist zu Beginn gesperrt - wird erst durch einen Schritt mit "Unlocks Movement" freigeschaltet.
            if (player != null)
            {
                player.SetMovementLocked(true);
                // Im Tutorial gibt es den Sieg erst nach dem letzten Schritt, nicht schon wenn der Dummy tot ist.
                player.SetAutoWinCheckEnabled(false);
            }

            if (introSlides.Count > 0 && introPanel != null)
            {
                StartIntro();
            }
            else
            {
                if (introPanel != null) introPanel.SetActive(false);
                ShowStep(0);
            }
        }

        private void Update()
        {
            // Versteckter Shortcut: M überspringt das komplette Tutorial (vor allem zum schnellen Testen).
            if (Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame)
            {
                SkipWholeTutorial();
            }

            HandleTypewriter();
            UpdateThrownObjects();
            UpdateDialoguePanelPosition();

            if (_inIntro)
            {
                UpdateIntro();
                return;
            }

            if (_currentStepIndex < 0 || _currentStepIndex >= steps.Count)
            {
                if (continuePromptUI != null) continuePromptUI.SetActive(false);
                return;
            }

            TutorialStep currentStep = steps[_currentStepIndex];

            if (currentStep.advanceMode == StepAdvanceMode.WaitForMovement)
            {
                if (continuePromptUI != null) continuePromptUI.SetActive(false);
                if (guideArrow != null) guideArrow.gameObject.SetActive(false);
                SkipTypingOnClick();
                HandleWaitForMovement();
            }
            else if (currentStep.advanceMode == StepAdvanceMode.MoveToArea)
            {
                if (continuePromptUI != null) continuePromptUI.SetActive(false);
                SkipTypingOnClick();
                HandleMoveToArea(currentStep);
            }
            else if (currentStep.advanceMode == StepAdvanceMode.WaitForEnemyDefeat)
            {
                if (continuePromptUI != null) continuePromptUI.SetActive(false);
                if (guideArrow != null) guideArrow.gameObject.SetActive(false);
                SkipTypingOnClick();
                HandleWaitForEnemyDefeat(currentStep);
            }
            else if (currentStep.advanceMode == StepAdvanceMode.WaitForFullHealth)
            {
                if (continuePromptUI != null) continuePromptUI.SetActive(false);
                if (guideArrow != null) guideArrow.gameObject.SetActive(false);
                SkipTypingOnClick();
                HandleWaitForFullHealth();
            }
            else
            {
                if (continuePromptUI != null) continuePromptUI.SetActive(!_isTyping);
                if (guideArrow != null) guideArrow.gameObject.SetActive(false);
                HandleClickToContinue();
            }
        }

        // ------------------------------------------------------------------
        // Intro
        // ------------------------------------------------------------------

        private void StartIntro()
        {
            _inIntro = true;
            introPanel.SetActive(true);
            ClearIntroImages();
            if (introContinuePrompt != null) introContinuePrompt.SetActive(false);
            ShowIntroSlide(0);
        }

        private void UpdateIntro()
        {
            UpdateVoicePause();

            if (introContinuePrompt != null) introContinuePrompt.SetActive(!_isTyping);

            // Bilder sanft einblenden
            if (_introImageFadeTimer < introImageFadeDuration)
            {
                _introImageFadeTimer += Time.deltaTime;
                SetIntroImagesAlpha(Mathf.Clamp01(_introImageFadeTimer / introImageFadeDuration));
            }

            if (!WasContinueClicked()) return;

            if (_isTyping)
            {
                CompleteTyping();
                return;
            }

            ShowIntroSlide(_currentIntroIndex + 1);
        }

        private void ShowIntroSlide(int index)
        {
            if (index >= introSlides.Count)
            {
                EndIntro();
                return;
            }

            _currentIntroIndex = index;
            IntroSlide slide = introSlides[index];

            if (slide.images != null && slide.images.Exists(sprite => sprite != null))
            {
                BuildIntroImages(slide.images);
            }

            PlayVoice(slide.voiceClip);
            StartTyping(introText, slide.text);
        }

        /// <summary>
        /// Stoppt die aktuelle Sprachaufnahme sofort und startet die neue (falls vorhanden) -
        /// keine Überlappung, da immer dieselbe AudioSource benutzt wird.
        /// </summary>
        private void PlayVoice(AudioClip clip)
        {
            if (_voiceSource == null) return;

            _voiceSource.Stop();
            _voicePausedByGamePause = false;
            if (clip == null) return;

            float sfxVolume = AudioManager.Instance != null ? AudioManager.Instance.GetSfxVolume() : 1f;
            _voiceSource.volume = introVoiceVolume * sfxVolume;
            _voiceSource.clip = clip;
            _voiceSource.Play();
        }

        private void StopVoice()
        {
            if (_voiceSource != null) _voiceSource.Stop();
            _voicePausedByGamePause = false;
        }

        /// <summary>Hält die Sprachaufnahme an, solange das Pause-Menü offen ist.</summary>
        private void UpdateVoicePause()
        {
            if (_voiceSource == null) return;

            if (Time.timeScale <= 0f && _voiceSource.isPlaying)
            {
                _voiceSource.Pause();
                _voicePausedByGamePause = true;
            }
            else if (Time.timeScale > 0f && _voicePausedByGamePause)
            {
                _voiceSource.UnPause();
                _voicePausedByGamePause = false;
            }
        }

        /// <summary>
        /// Erzeugt für jedes Bild des Abschnitts ein Image im Container - die Layout Group
        /// ordnet sie automatisch nebeneinander an (gleiches Prinzip wie die Kapitel-Buttons
        /// in den Einstellungen).
        /// </summary>
        private void BuildIntroImages(List<Sprite> sprites)
        {
            ClearIntroImages();
            if (introImageContainer == null) return;

            foreach (Sprite sprite in sprites)
            {
                if (sprite == null) continue;

                GameObject imageObject = new GameObject("IntroImage", typeof(RectTransform), typeof(Image));
                imageObject.transform.SetParent(introImageContainer, false);
                Image image = imageObject.GetComponent<Image>();
                image.preserveAspect = true;
                image.raycastTarget = false;

                image.sprite = sprite;
                _currentIntroImages.Add(image);
            }

            _introImageFadeTimer = 0f;
            SetIntroImagesAlpha(introImageFadeDuration > 0f ? 0f : 1f);

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(introImageContainer);
        }

        private void ClearIntroImages()
        {
            _currentIntroImages.Clear();
            if (introImageContainer == null) return;

            foreach (Transform child in introImageContainer)
            {
                Destroy(child.gameObject);
            }
        }

        private void SetIntroImagesAlpha(float alpha)
        {
            foreach (Image image in _currentIntroImages)
            {
                if (image == null) continue;
                Color color = image.color;
                color.a = alpha;
                image.color = color;
            }
        }

        /// <summary>
        /// Beendet das Intro (nach dem letzten Abschnitt) und startet das Tutorial.
        /// </summary>
        private void EndIntro()
        {
            if (!_inIntro) return;

            _inIntro = false;
            StopVoice();
            if (introPanel != null) introPanel.SetActive(false);
            if (introContinuePrompt != null) introContinuePrompt.SetActive(false);

            // Cooldown neu setzen, damit der Klick, der das Intro beendet hat, nicht gleich den ersten Tutorial-Text überspringt.
            _nextClickAllowedTime = Time.unscaledTime + clickCooldown;
            ShowStep(0);
        }

        // ------------------------------------------------------------------
        // Klick / Schreibmaschine
        // ------------------------------------------------------------------

        /// <summary>
        /// True, wenn in diesem Frame gültig "Weiter" gedrückt wurde - per Linksklick (nicht auf
        /// einem Button) ODER per Taste E. Nicht bei pausiertem Spiel, und erst nach Ablauf des Cooldowns.
        /// </summary>
        private bool WasContinueClicked()
        {
            bool ePressed = Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
            bool leftClicked = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && !IsPointerOverButton();

            if (!ePressed && !leftClicked) return false;
            if (Time.timeScale <= 0f) return false;
            if (Time.unscaledTime < _nextClickAllowedTime) return false;

            _nextClickAllowedTime = Time.unscaledTime + clickCooldown;
            return true;
        }

        /// <summary>
        /// Prüft, ob die Maus gerade über einem anklickbaren UI-Element (Button, Slider, ...) liegt.
        /// Normale Panels/Texte zählen nicht - darauf darf man zum Weiterschalten klicken.
        /// </summary>
        private bool IsPointerOverButton()
        {
            if (EventSystem.current == null || Mouse.current == null) return false;

            PointerEventData pointerData = new PointerEventData(EventSystem.current)
            {
                position = Mouse.current.position.ReadValue()
            };

            List<RaycastResult> results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointerData, results);

            foreach (RaycastResult result in results)
            {
                Selectable selectable = result.gameObject.GetComponentInParent<Selectable>();
                if (selectable != null && selectable.interactable) return true;
            }

            return false;
        }

        private void StartTyping(TMP_Text target, string text)
        {
            _activeText = target;
            _currentFullText = text ?? "";
            _visibleCharacterCount = 0;
            _typeTimer = 0f;
            _isTyping = true;

            if (_activeText != null) _activeText.text = "";
        }

        private void CompleteTyping()
        {
            _visibleCharacterCount = _currentFullText.Length;
            if (_activeText != null) _activeText.text = _currentFullText;
            _isTyping = false;
        }

        private void HandleTypewriter()
        {
            if (!_isTyping) return;

            _typeTimer += Time.deltaTime;
            float secondsPerChar = charactersPerSecond > 0f ? 1f / charactersPerSecond : 0f;

            if (secondsPerChar <= 0f)
            {
                CompleteTyping();
                return;
            }

            while (_typeTimer >= secondsPerChar && _visibleCharacterCount < _currentFullText.Length)
            {
                _typeTimer -= secondsPerChar;
                _visibleCharacterCount++;

                if (_activeText != null)
                {
                    _activeText.text = _currentFullText.Substring(0, _visibleCharacterCount);
                }
            }

            if (_visibleCharacterCount >= _currentFullText.Length)
            {
                _isTyping = false;
            }
        }

        /// <summary>
        /// Für alle Schritte, die nicht per Klick weiterschalten: ein Klick darf trotzdem jederzeit
        /// den Schreib-Effekt überspringen.
        /// </summary>
        private void SkipTypingOnClick()
        {
            if (_isTyping && WasContinueClicked())
            {
                CompleteTyping();
            }
        }

        private void HandleClickToContinue()
        {
            if (!WasContinueClicked()) return;

            if (_isTyping)
            {
                // Erster Klick während des Schreibens: sofort den kompletten Text anzeigen.
                CompleteTyping();
                return;
            }

            ShowStep(_currentStepIndex + 1);
        }

        // ------------------------------------------------------------------
        // Tutorial-Schritte
        // ------------------------------------------------------------------

        private void HandleWaitForMovement()
        {
            if (_isTyping || Keyboard.current == null) return;

            bool movementPressed =
                Keyboard.current.wKey.isPressed || Keyboard.current.aKey.isPressed ||
                Keyboard.current.sKey.isPressed || Keyboard.current.dKey.isPressed ||
                Keyboard.current.upArrowKey.isPressed || Keyboard.current.leftArrowKey.isPressed ||
                Keyboard.current.downArrowKey.isPressed || Keyboard.current.rightArrowKey.isPressed;

            if (movementPressed)
            {
                ShowStep(_currentStepIndex + 1);
            }
        }

        private void HandleMoveToArea(TutorialStep step)
        {
            if (_isTyping || player == null || step.targetArea == null)
            {
                if (guideArrow != null) guideArrow.gameObject.SetActive(false);
                return;
            }

            UpdateGuideArrow(step.targetArea.position);

            float distance = Vector3.Distance(player.transform.position, step.targetArea.position);
            if (distance <= step.targetAreaRadius)
            {
                if (guideArrow != null) guideArrow.gameObject.SetActive(false);
                ShowStep(_currentStepIndex + 1);
            }
        }

        private void HandleWaitForEnemyDefeat(TutorialStep step)
        {
            if (_isTyping) return;

            if (step.enemyToDefeat != null)
            {
                _lastKnownEnemyPosition = step.enemyToDefeat.transform.position;
                return;
            }

            // Gegner ist gerade besiegt worden (Referenz ist jetzt null).
            if (step.thrownObjectSprite != null)
            {
                SpawnThrownObject(_lastKnownEnemyPosition, step.thrownObjectSprite, step.thrownObjectDamage, step.thrownObjectFlightDuration);
            }
            else if (step.thrownObjectDamage > 0 && player != null)
            {
                player.TakeDamage(step.thrownObjectDamage);
            }

            ShowStep(_currentStepIndex + 1);
        }

        private void HandleWaitForFullHealth()
        {
            if (_isTyping || player == null) return;

            if (player.CurrentHealth >= player.MaxHealth)
            {
                ShowStep(_currentStepIndex + 1);
            }
        }

        private void SpawnThrownObject(Vector3 fromPosition, Sprite sprite, int damage, float duration)
        {
            if (player == null) return;

            GameObject thrownObject = new GameObject("ThrownObject");
            Vector3 startPosition = fromPosition + Vector3.up * 1f;
            thrownObject.transform.position = startPosition;

            if (Camera.main != null)
            {
                thrownObject.transform.rotation = Camera.main.transform.rotation;
            }

            SpriteRenderer renderer = thrownObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;

            _activeThrownObjects.Add(new ThrownObject
            {
                Transform = thrownObject.transform,
                StartPosition = startPosition,
                Duration = Mathf.Max(0.01f, duration),
                Timer = 0f,
                Damage = damage
            });
        }

        private void UpdateThrownObjects()
        {
            for (int i = _activeThrownObjects.Count - 1; i >= 0; i--)
            {
                ThrownObject obj = _activeThrownObjects[i];
                obj.Timer += Time.deltaTime;
                float t = Mathf.Clamp01(obj.Timer / obj.Duration);

                Vector3 targetPosition = player != null ? player.transform.position + Vector3.up * 1f : obj.StartPosition;
                obj.Transform.position = Vector3.Lerp(obj.StartPosition, targetPosition, t);

                if (t >= 1f)
                {
                    if (player != null && obj.Damage > 0)
                    {
                        player.TakeDamage(obj.Damage);
                    }

                    Destroy(obj.Transform.gameObject);
                    _activeThrownObjects.RemoveAt(i);
                }
            }
        }

        private void UpdateGuideArrow(Vector3 targetPosition)
        {
            if (guideArrow == null || player == null) return;

            guideArrow.gameObject.SetActive(true);

            Vector3 direction = targetPosition - player.transform.position;
            direction.y = 0f;

            if (direction.sqrMagnitude > 0.0001f)
            {
                direction.Normalize();

                Vector3 orbitOffset = direction * guideArrowOrbitRadius;
                guideArrow.position = player.transform.position + orbitOffset + Vector3.up * guideArrowHeightOffset;

                Vector3 faceCameraDirection = Camera.main != null ? -Camera.main.transform.forward : Vector3.forward;
                guideArrow.rotation = Quaternion.LookRotation(faceCameraDirection, direction);
            }
        }

        /// <summary>
        /// Lässt das Dialog-Panel sanft zum Platzhalter des aktuellen Schritts wandern.
        /// </summary>
        private void UpdateDialoguePanelPosition()
        {
            if (dialoguePanel == null || _dialoguePanelTarget == null) return;

            Transform panelTransform = dialoguePanel.transform;
            if (dialoguePanelMoveSpeed <= 0f)
            {
                panelTransform.position = _dialoguePanelTarget.position;
                return;
            }

            panelTransform.position = Vector3.Lerp(panelTransform.position, _dialoguePanelTarget.position, dialoguePanelMoveSpeed * Time.deltaTime);
        }

        private void ShowStep(int index)
        {
            HideAllMarkers();

            if (index < 0 || index >= steps.Count)
            {
                // Alle Schritte geschafft -> Dialog schließen und Win-Panel öffnen.
                bool wasRunning = _currentStepIndex < steps.Count;
                if (dialoguePanel != null) dialoguePanel.SetActive(false);
                if (guideArrow != null) guideArrow.gameObject.SetActive(false);
                _currentStepIndex = steps.Count;
                if (wasRunning && player != null) player.WinLevel();
                return;
            }

            TutorialStep step = steps[index];

            _currentStepIndex = index;

            if (dialoguePanel != null)
            {
                bool wasHidden = !dialoguePanel.activeSelf;
                dialoguePanel.SetActive(true);

                if (step.dialoguePanelPosition != null)
                {
                    _dialoguePanelTarget = step.dialoguePanelPosition;

                    // War das Panel vorher unsichtbar (z.B. direkt nach dem Intro), sofort hinspringen statt hinzuwandern.
                    if (wasHidden) dialoguePanel.transform.position = _dialoguePanelTarget.position;
                }
            }
            if (step.marker != null) step.marker.SetActive(true);

            foreach (GameObject uiElement in step.uiElementsToReveal)
            {
                if (uiElement != null) uiElement.SetActive(true);
            }

            if (step.unlocksMovement && player != null)
            {
                player.SetMovementLocked(false);
            }

            if (step.damageToApplyOnShow > 0 && player != null)
            {
                player.TakeDamage(step.damageToApplyOnShow);
            }

            StartTyping(dialogueText, step.text);
        }

        /// <summary>
        /// Beendet Intro und Tutorial sofort und öffnet das Win-Panel (versteckt auf Taste M).
        /// </summary>
        private void SkipWholeTutorial()
        {
            if (!_inIntro && _currentStepIndex >= steps.Count) return; // schon fertig

            _inIntro = false;
            _isTyping = false;
            StopVoice();
            if (introPanel != null) introPanel.SetActive(false);
            if (introContinuePrompt != null) introContinuePrompt.SetActive(false);
            if (dialoguePanel != null) dialoguePanel.SetActive(false);
            if (continuePromptUI != null) continuePromptUI.SetActive(false);
            if (guideArrow != null) guideArrow.gameObject.SetActive(false);
            HideAllMarkers();

            _currentStepIndex = steps.Count;

            if (player != null)
            {
                player.SetMovementLocked(false);
                player.WinLevel();
            }
        }

        private void HideAllMarkers()
        {
            foreach (TutorialStep step in steps)
            {
                if (step.marker != null) step.marker.SetActive(false);
            }
        }
    }
}