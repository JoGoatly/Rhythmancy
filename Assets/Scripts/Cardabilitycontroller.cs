using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using TMPro;
using RhythmWitchClone.Core;
using RhythmWitchClone.Audio;

namespace RhythmWitchClone.Gameplay
{
    /// <summary>
    /// Karten-System: 3 Karten (Verteidigung, Angriff, Spezial), per Taste 1/2/3 oder per
    /// Linksklick auf die Karte an-/abgewählt.
    /// Es kann immer nur eine Karte gleichzeitig aktiv sein - wählt man eine neue, wird die
    /// vorherige automatisch abgelegt. Nochmal dieselbe Zahl drücken legt die aktuell aktive
    /// Karte wieder ab.
    ///
    /// Karte 1 (Verteidigung) und Karte 2 (Angriff) sind Dauerzustände, die Mana verbrauchen
    /// (-1/Sekunde, siehe Mana Drain Per Second) und sich bei 0 Mana automatisch ablegen.
    /// Karte 3 (Spezial) ist ein Sofort-Effekt, der NUR bei vollem Mana funktioniert: löst die
    /// Schockwelle einmalig aus und zieht
    /// dabei das komplette Mana ab - bleibt danach nicht "ausgewählt".
    ///
    /// Setup:
    /// - Irgendwo in der Level-Szene platzieren (z.B. am Player-Objekt oder als eigenes
    ///   GameObject unter dem Canvas).
    /// - "Player": dein Player-GameObject (mit PlayerController).
    /// - "Def/Atk/Spc Card Transform": die drei Karten-UI-Objekte im Canvas (siehe Skizze -
    ///   fächerartig angeordnet). Werden beim Auswählen automatisch nach oben gezogen.
    /// - "Def/Atk/Spc Card Text": je ein TextMeshProUGUI-Objekt als Kind der jeweiligen Karte -
    ///   wird beim Start automatisch mit dem Effekt der Karte befüllt (liest die Werte, die du
    ///   weiter unten in diesem Script einstellst - alle Karten-Zahlen leben komplett hier,
    ///   PlayerController kennt selbst keine).
    /// - "Mana Bar Slider": optional, ein UI-Slider für die Mana-Anzeige.
    /// </summary>
    public class CardAbilityController : MonoBehaviour
    {
        [Header("Referenzen")]
        [SerializeField] private PlayerController player;

        [Header("Mana")]
        [SerializeField] private float maxMana = 100f;
        [Tooltip("Wie viel Mana pro Sekunde verbraucht wird, solange Karte 1 oder 2 aktiv ist.")]
        [SerializeField] private float manaDrainPerSecond = 1f;
        [Tooltip("Wie viel Mana pro Sekunde zurückkommt, solange keine Karte aktiv ist.")]
        [SerializeField] private float manaRegenPerSecond = 5f;
        [SerializeField] private Slider manaBarSlider;
        [Tooltip("Optional: Prozent-Anzeige (z.B. '80%'). Als Kind von 'Fill Area' des Sliders anlegen - wandert automatisch mit dem Balkenende mit.")]
        [SerializeField] private TMP_Text manaText;

        [Header("Karten (UI)")]
        [SerializeField] private RectTransform defCardTransform;
        [Tooltip("Text auf der Verteidigungs-Karte, wird beim Start automatisch aus den Werten unten generiert.")]
        [SerializeField] private TMP_Text defCardText;
        [SerializeField] private RectTransform atkCardTransform;
        [Tooltip("Text auf der Angriffs-Karte, wird beim Start automatisch aus den Werten unten generiert.")]
        [SerializeField] private TMP_Text atkCardText;
        [SerializeField] private RectTransform spcCardTransform;
        [Tooltip("Text auf der Spezial-Karte, wird beim Start automatisch aus den Werten unten generiert.")]
        [SerializeField] private TMP_Text spcCardText;
        [Tooltip("Wie weit eine ausgewählte Karte nach oben gezogen wird, in UI-Pixeln.")]
        [SerializeField] private float cardLiftDistance = 40f;
        [Tooltip("Wie schnell die Karte hoch-/runtergleitet.")]
        [SerializeField] private float cardLiftSpeed = 8f;
        [Tooltip("Wie lange Karte 3 (Spezial) beim Auslösen kurz hochgezogen bleibt, bevor sie automatisch wieder absinkt.")]
        [SerializeField] private float specialCardPulseDuration = 0.3f;
        [SerializeField] private TMP_Text specialFeedbackText;
        private float _specialFeedbackTimer;

        [Header("Karte 1: Verteidigung")]
        [Tooltip("Wie viel Prozent Schaden reduziert werden, solange die Karte aktiv ist (0.3 = 30% weniger).")]
        [SerializeField] private float defenseDamageReduction = 0.3f;
        [Tooltip("HP, die pro Heilintervall geheilt werden, solange die Karte aktiv ist.")]
        [SerializeField] private int defenseHealAmount = 1;
        [Tooltip("Wie oft geheilt wird (Sekunden), solange die Karte aktiv ist.")]
        [SerializeField] private float defenseHealInterval = 2f;

        [Header("Karte 2: Angriff")]
        [Tooltip("Zusätzlicher Schaden bei einem Balltreffer, solange die Karte aktiv ist.")]
        [SerializeField] private int attackDamageBonus = 15;
        [Tooltip("Zusätzlicher Aura-Radius, solange die Karte aktiv ist.")]
        [SerializeField] private float attackRadiusBonus = 1.5f;

        [Header("Karte 3: Spezial")]
        [Tooltip("Reichweite der Schockwelle - alle Gegner darin werden weggestoßen und betäubt.")]
        [SerializeField] private float specialShockwaveRadius = 5f;
        [SerializeField] private float specialKnockbackForce = 8f;
        [SerializeField] private float specialStunDuration = 10f;
        [Tooltip("Farbe der visuellen Schockwelle - unabhängig von der Tasten-Farbe, damit sie sich klar von normalen Treffern abhebt.")]
        [SerializeField] private Color specialShockwaveColor = new Color(1f, 0.85f, 0.2f, 1f);
        [Tooltip("Wie lange die visuelle Schockwelle sichtbar ist (länger als bei normalen Treffern wirkt mächtiger).")]
        [SerializeField] private float specialShockwaveVisualDuration = 0.8f;

        private MusicSheetDefinition _activeSheet;
        private MusicSheetDefinition _defSheet, _atkSheet, _spcSheet;
        public float CurrentMana => _currentMana;
        public float MaxMana => maxMana;
        private float _currentMana;
        private int _activeCardIndex = -1; // -1 = keine, 0 = Verteidigung, 1 = Angriff
        private float _specialCardPulseTimer;
        private float _defenseHealTimer;

        private readonly Dictionary<RectTransform, Quaternion> _cardRotations = new Dictionary<RectTransform, Quaternion>();
        private readonly Dictionary<RectTransform, Vector3> _cardScales = new Dictionary<RectTransform, Vector3>();
        private int _frontCard = -2;

        private Vector2 _defCardBasePos;
        private Vector2 _atkCardBasePos;
        private Vector2 _spcCardBasePos;

        private void Start()
        {
            RefreshCharacterStats();
            RefreshMusicSheets();
            _currentMana = maxMana;

            if (defCardTransform != null) _defCardBasePos = defCardTransform.anchoredPosition;
            if (atkCardTransform != null) _atkCardBasePos = atkCardTransform.anchoredPosition;
            if (spcCardTransform != null) _spcCardBasePos = spcCardTransform.anchoredPosition;

            foreach (var card in new[] { defCardTransform, atkCardTransform, spcCardTransform })
            {
                if (card == null) continue;
                _cardRotations[card] = card.localRotation;
                _cardScales[card] = card.localScale;
            }
            GenerateCardTexts();
        }

        /// <summary>
        /// Applies persistent character stats without refilling mana or stacking bonuses.
        /// </summary>
        public void RefreshCharacterStats()
        {
            var character = GetComponent<MainCharacter>();
            if (character == null) return;
            var stats = character.Stats;
            maxMana = stats.maxMana;
            manaRegenPerSecond = stats.manaRegeneration;
            _currentMana = Mathf.Clamp(_currentMana, 0, maxMana);
            UpdateManaBarUI();
        }

        public void RefreshMusicSheets()
        {
            var character = GetComponent<MainCharacter>();
            if (character == null || character.musicSheets == null) return;
            DeactivateCurrentCard();
            _specialCardPulseTimer = 0;
            _defSheet = character.GetEquippedSheet(MusicSheetClass.DEF);
            _atkSheet = character.GetEquippedSheet(MusicSheetClass.ATK);
            _spcSheet = character.GetEquippedSheet(MusicSheetClass.SPC);
            ApplySheetArtwork(defCardTransform, _defSheet);
            ApplySheetArtwork(atkCardTransform, _atkSheet);
            ApplySheetArtwork(spcCardTransform, _spcSheet);
            if (_spcSheet != null)
            {
                specialShockwaveRadius = _spcSheet.shockwaveRadius;
                specialKnockbackForce = _spcSheet.knockbackForce;
                specialStunDuration = _spcSheet.stunDuration;
                specialShockwaveColor = _spcSheet.shockwaveColor;
                specialShockwaveVisualDuration = _spcSheet.shockwaveVisualDuration;
                specialCardPulseDuration = _spcSheet.selectionPulseDuration;
            }
            GenerateCardTexts();
        }

        private static void ApplySheetArtwork(RectTransform card, MusicSheetDefinition sheet)
        {
            if (card != null && sheet != null && sheet.artwork != null &&
                card.TryGetComponent<Image>(out var artworkImage))
                artworkImage.sprite = sheet.artwork;
        }

        private void OnDisable()
        {
            DeactivateCurrentCard();
            _specialCardPulseTimer = 0;
        }

        private static void ShowCardDescription(TMP_Text text)
        {
            if (text == null) return;
            text.gameObject.SetActive(true);
            text.enabled = true;
            text.raycastTarget = false;
            text.color = new Color(0.22f, 0.16f, 0.11f, 1f);
            text.alignment = TextAlignmentOptions.Center;
            text.enableAutoSizing = true;
            text.fontSizeMin = 16f;
            text.fontSizeMax = 21f;
            RectTransform rect = text.rectTransform;
            rect.anchorMin = new Vector2(0.12f, 0.18f);
            rect.anchorMax = new Vector2(0.88f, 0.78f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }

        private void GenerateCardTexts()
        {
            ShowCardDescription(defCardText);
            ShowCardDescription(atkCardText);
            ShowCardDescription(spcCardText);
            if (defCardText != null)
            {
                int reductionPercent = Mathf.RoundToInt((_defSheet != null ? _defSheet.damageReduction : defenseDamageReduction) * 100f);
                defCardText.text =
                    $"<b>Verteidigung</b>\n\n-{reductionPercent}% Schaden\n" +
                    $"+{(_defSheet != null ? _defSheet.healingAmount : defenseHealAmount)} HP alle {(_defSheet != null ? _defSheet.healingInterval : defenseHealInterval):0.#}s";
            }

            if (atkCardText != null)
            {
                atkCardText.text =
                    $"<b>Angriff</b>\n\n+{(_atkSheet != null ? _atkSheet.auraDamageBonus : attackDamageBonus)} Schaden\n" +
                    $"+{(_atkSheet != null ? _atkSheet.auraRadiusBonus : attackRadiusBonus):0.#} Reichweite";
            }

            if (spcCardText != null)
            {
                spcCardText.text =
                    $"<b>Schockwelle</b>\n\nStößt Gegner weg\n" +
                    $"{specialStunDuration:0.#}s betäubt\n" +
                    $"(nur bei vollem Mana)";
            }
        }

        private void Update()
        {
            if (_specialFeedbackTimer > 0f)
            {
                _specialFeedbackTimer -= Time.deltaTime;
                if (_specialFeedbackTimer <= 0f && specialFeedbackText != null) specialFeedbackText.text = "";
            }
            HandleCardInput();
            UpdateMana();
            UpdateCardVisuals(Time.deltaTime);
            UpdateManaBarUI();
            UpdateDefenseHealing();
        }

        private void HandleCardInput()
        {
            if (Time.timeScale <= 0f) return; // pausiert / Game Over - keine Karten

            if (Keyboard.current != null)
            {
                if (BindingPressed("Fähigkeit 1")) ToggleCard(0);
                if (BindingPressed("Fähigkeit 2")) ToggleCard(1);
                if (BindingPressed("Spezialfähigkeit")) TriggerSpecialCard();
            }

            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                int clickedCard = GetCardUnderMouse();
                if (clickedCard == 0) ToggleCard(0);
                else if (clickedCard == 1) ToggleCard(1);
                else if (clickedCard == 2) TriggerSpecialCard();
            }
        }

        private static bool BindingPressed(string action)
        {
            Key key = RhythmancySettings.GetKeyForAction(action);
            return key != Key.None && Keyboard.current[key].wasPressedThisFrame;
        }

        /// <summary>
        /// Prüft per UI-Raycast, welche Karte unter der Maus liegt (-1 = keine). Die Ergebnisse
        /// kommen von oben nach unten sortiert - bei überlappenden Karten (Fächer) gewinnt also
        /// die, die sichtbar oben liegt. Klicks auf den Kartentext zählen mit, da er ein Kind
        /// der Karte ist.
        /// </summary>
        private int GetCardUnderMouse()
        {
            if (EventSystem.current == null) return -1;

            PointerEventData pointerData = new PointerEventData(EventSystem.current)
            {
                position = Mouse.current.position.ReadValue()
            };

            List<RaycastResult> results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointerData, results);

            foreach (RaycastResult result in results)
            {
                Transform hit = result.gameObject.transform;
                if (defCardTransform != null && hit.IsChildOf(defCardTransform)) return 0;
                if (atkCardTransform != null && hit.IsChildOf(atkCardTransform)) return 1;
                if (spcCardTransform != null && hit.IsChildOf(spcCardTransform)) return 2;
            }

            return -1;
        }

        /// <summary>
        /// Wählt Karte 0 (Verteidigung) oder 1 (Angriff) an/ab. Ist bereits eine andere Karte
        /// aktiv, wird die zuerst automatisch abgelegt - es kann immer nur eine gleichzeitig
        /// aktiv sein.
        /// </summary>
        private void ToggleCard(int cardIndex)
        {
            if (_activeCardIndex == cardIndex)
            {
                DeactivateCurrentCard();
                return;
            }

            DeactivateCurrentCard();
            _specialCardPulseTimer = 0;
            _activeCardIndex = cardIndex;
            _activeSheet = cardIndex == 0 ? _defSheet : _atkSheet;
            AudioManager.Instance?.SetSheetMusic(this, _activeSheet != null ? _activeSheet.selectedMusic : null);
            _defenseHealTimer = 0f;

            if (player == null) return;

            if (_activeSheet != null)
            {
                player.SetDamageReduction(_activeSheet.damageReduction);
                player.SetAuraBonus(_activeSheet.auraDamageBonus, _activeSheet.auraRadiusBonus);
            }
            else if (cardIndex == 0) player.SetDamageReduction(defenseDamageReduction);
            else if (cardIndex == 1) player.SetAuraBonus(attackDamageBonus, attackRadiusBonus);
        }

        private void DeactivateCurrentCard()
        {
            if (player != null)
            {
                player.SetDamageReduction(0f);
                player.SetAuraBonus(0, 0f);
            }

            _activeCardIndex = -1;
            _activeSheet = null;
            AudioManager.Instance?.ClearSheetMusic(this);
        }

        /// <summary>
        /// Karte 3 (Spezial) - kein Dauerzustand, sondern ein Sofort-Effekt. Funktioniert NUR
        /// bei vollem Mana (sonst passiert gar nichts) - löst dann die Schockwelle aus, zieht
        /// dabei das komplette Mana ab und legt eine evtl. aktive Karte 1/2 mit ab.
        /// </summary>
        private void TriggerSpecialCard()
        {
            if (_currentMana < maxMana)
            {
                if (specialFeedbackText != null)
                    specialFeedbackText.text = $"SPC braucht volles Mana ({Mathf.FloorToInt(100f * _currentMana / maxMana)} %).\nATK/DEF abwählen, um Mana zu laden.";
                _specialFeedbackTimer = 3f;
                return;
            }

            DeactivateCurrentCard();

            _currentMana = 0f;
            _specialCardPulseTimer = specialCardPulseDuration;
            AudioManager.Instance?.SetSheetMusic(this, _spcSheet != null ? _spcSheet.selectedMusic : null);
            if (specialFeedbackText != null) specialFeedbackText.text = "Schockwelle!";
            _specialFeedbackTimer = 1f;

            if (player != null)
            {
                player.TriggerSpecialShockwave(specialShockwaveRadius, specialKnockbackForce, specialStunDuration, specialShockwaveColor, specialShockwaveVisualDuration);
            }
        }

        /// <summary>
        /// Heilt periodisch, solange Karte 1 (Verteidigung) aktiv ist - läuft hier statt in
        /// PlayerController, da Intervall/Betrag reine Karten-Werte sind.
        /// </summary>
        private void UpdateDefenseHealing()
        {
            int healing = _activeSheet != null ? _activeSheet.healingAmount : (_activeCardIndex == 0 ? defenseHealAmount : 0);
            float interval = _activeSheet != null ? _activeSheet.healingInterval : defenseHealInterval;
            if (_activeCardIndex < 0 || healing <= 0 || player == null) return;

            _defenseHealTimer += Time.deltaTime;
            if (_defenseHealTimer >= Mathf.Max(.01f, interval))
            {
                _defenseHealTimer -= Mathf.Max(.01f, interval);
                player.Heal(healing);
            }
        }

        private void UpdateMana()
        {
            if (_activeCardIndex == 0 || _activeCardIndex == 1)
            {
                _currentMana = Mathf.Max(0f, _currentMana - (_activeSheet != null ? _activeSheet.manaDrainPerSecond : manaDrainPerSecond) * Time.deltaTime);

                if (_currentMana <= 0f)
                {
                    // Mana leer - die Karte kann nicht weiter gehalten werden.
                    DeactivateCurrentCard();
                }
            }
            else
            {
                _currentMana = Mathf.Min(maxMana, _currentMana + manaRegenPerSecond * Time.deltaTime);
            }
        }

        private void UpdateCardVisuals(float deltaTime)
        {
            if (_specialCardPulseTimer > 0f)
            {
                _specialCardPulseTimer -= deltaTime;
                if (_specialCardPulseTimer <= 0) AudioManager.Instance?.ClearSheetMusic(this);
            }

            int front = _specialCardPulseTimer > 0f ? 2 : (_activeCardIndex >= 0 ? _activeCardIndex : 1);
            if (_frontCard != front)
            {
                var cards = new[] { defCardTransform, atkCardTransform, spcCardTransform };
                // Keep ATK above the remaining side sheet, as in the illustrated fan.
                foreach (var card in new[] { spcCardTransform, defCardTransform, atkCardTransform })
                    if (card != null) card.SetAsLastSibling();
                if (cards[front] != null) cards[front].SetAsLastSibling();
                _frontCard = front;
            }
            UpdateSingleCardVisual(defCardTransform, _defCardBasePos, _activeCardIndex == 0, deltaTime);
            UpdateSingleCardVisual(atkCardTransform, _atkCardBasePos, _activeCardIndex == 1, deltaTime);
            UpdateSingleCardVisual(spcCardTransform, _spcCardBasePos, _specialCardPulseTimer > 0f, deltaTime);
        }

        private void UpdateSingleCardVisual(RectTransform cardTransform, Vector2 basePosition, bool isLifted, float deltaTime)
        {
            if (cardTransform == null) return;

            Vector2 targetPosition = basePosition + (isLifted ? Vector2.up * cardLiftDistance : Vector2.zero);
            float blend = 1f - Mathf.Exp(-cardLiftSpeed * deltaTime);
            cardTransform.anchoredPosition = Vector2.Lerp(cardTransform.anchoredPosition, targetPosition, blend);
            if (_cardScales.TryGetValue(cardTransform, out Vector3 scale))
                cardTransform.localScale = Vector3.Lerp(cardTransform.localScale, scale * (isLifted ? 1.18f : 1f), blend);
            if (_cardRotations.TryGetValue(cardTransform, out Quaternion rotation))
                cardTransform.localRotation = Quaternion.Slerp(cardTransform.localRotation, rotation, blend);
        }

        /// <summary>
        /// Schiebt den Prozent-Text horizontal genau an das aktuelle Ende des Balkens.
        /// Der Text muss ein Kind von "Fill Area" des Sliders sein - sein Anker wird hier
        /// auf den aktuellen Füllstand (0 = links, 1 = rechts) gesetzt. Funktioniert auch,
        /// wenn der Balken per "Image Type: Filled" gefüllt wird statt über die Breite.
        /// </summary>
        private static void FollowBarEnd(TMP_Text text, Slider slider)
        {
            if (text == null || slider == null) return;

            RectTransform rect = text.rectTransform;
            float x = slider.normalizedValue;
            rect.anchorMin = new Vector2(x, rect.anchorMin.y);
            rect.anchorMax = new Vector2(x, rect.anchorMax.y);
            rect.anchoredPosition = new Vector2(0f, rect.anchoredPosition.y);
        }

        private void UpdateManaBarUI()
        {
            if (manaBarSlider != null)
            {
                manaBarSlider.maxValue = maxMana;
                manaBarSlider.value = _currentMana;
            }

            if (manaText != null)
            {
                int manaPercent = maxMana > 0f ? Mathf.RoundToInt(100f * _currentMana / maxMana) : 0;
                manaText.text = $"{manaPercent}%";
            }

            FollowBarEnd(manaText, manaBarSlider);
        }
    }
}
