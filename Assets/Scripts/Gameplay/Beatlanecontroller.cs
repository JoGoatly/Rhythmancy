using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using TMPro;
using RhythmWitchClone.Audio;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RhythmWitchClone.Gameplay
{
    /// <summary>
    /// Rhythmus-Spur oberhalb des Spielers (UI): Noten wandern von rechts nach links durch eine
    /// Reihe, jede Note verlangt eine bestimmte Taste. Trifft man die richtige Taste, während die
    /// Note die feste Hit-Zone (links) erreicht, gibt's Perfekt/Gut/OK je nach Genauigkeit, sonst
    /// Schlecht. Ersetzt das frühere kreisförmige Bälle-System, funktioniert aber nach demselben
    /// Grundprinzip: BPM-synchron (bei gleichmäßigem Tastendruck im Takt trifft man IMMER),
    /// Kollision über die echte Icon-Größe statt eines künstlichen Toleranz-Fensters.
    ///
    /// Setup:
    /// - Kommt auf das Spieler-Root-GameObject (wie vorher das Bälle-Script).
    /// - "Lane Keys": eine Liste - für jede Note eine Taste + ein Icon-Bild + eine eigene, noch
    ///   ungenutzte "Note Aura Images"-Liste (Notenbilder für die spätere farbige Aura um den
    ///   Spieler, wenn genau diese Taste getroffen wird). Maximal 8 gleichzeitig aktive Noten,
    ///   der Abstand zwischen ihnen passt sich automatisch an die Anzahl an (durch das
    ///   "Tasten-Anzahl"-Upgrade steuerbar, wiederholt die Tasten-Liste bei Bedarf).
    /// - "Lane Container": ein RectTransform im Canvas mit Pivot/Anchor links-mittig (0, 0.5) -
    ///   markiert die volle Breite der Spur. Rechter Rand = wo Noten erscheinen, "Hit Zone Offset
    ///   From Left" = fester Abstand vom linken Rand, an dem getroffen werden muss.
    /// - "Feedback Text": wie gehabt, ein TextMeshProUGUI im Canvas für Perfekt/Gut/OK/Schlecht.
    /// </summary>
    public class BeatLaneController : MonoBehaviour
    {
        [System.Serializable]
        public class LaneKeyDefinition
        {
            [Tooltip("Das Zeichen, das gedrückt werden muss - z.B. 'Z'. Wird zur Laufzeit layoutabhängig aufgelöst (funktioniert also korrekt auf QWERTZ-, QWERTY- und anderen Tastaturen, nicht nur auf deiner eigenen).")]
            public char requiredCharacter = 'A';
            public Sprite icon;
            [Tooltip("Farbe der Schockwellen-Aura, wenn diese Taste getroffen wird - am besten passend zur Icon-Farbe wählen.")]
            public Color auraColor = Color.white;
            [Tooltip("Ziehe hier ein komplettes, in 'Multiple' gesliceltes Spritesheet rein - alle darin enthaltenen Einzel-Sprites werden automatisch als Noten-Streusel für die Aura übernommen.")]
            public Texture2D noteSpriteSheet;

            // Wird automatisch aus noteSpriteSheet befüllt (siehe OnValidate) - im Inspector
            // ausgeblendet, da es rein intern verwaltet wird und nur unnötig Platz wegnehmen würde.
            [HideInInspector] public List<Sprite> noteAuraImages = new List<Sprite>();
        }

        [Header("Tasten")]
        [Tooltip("Ein Eintrag = ein Zeichen mit Icon + eigener Noten-Aura-Bilderliste. Alle Einträge bilden den Zufallspool für die Spur.")]
        [SerializeField] private List<LaneKeyDefinition> laneKeys = new List<LaneKeyDefinition>();

        [Header("Spur (UI)")]
        [Tooltip("RectTransform im Canvas mit Pivot/Anchor links-mittig (0, 0.5) - markiert die volle Breite der Spur.")]
        [SerializeField] private RectTransform laneContainer;
        [SerializeField] private float noteIconSize = 64f;
        [SerializeField, Range(2, 8)] private int visibleNoteCount = 4;

        [Header("Ziel-Markierung (manuell im Editor platziert)")]
        [Tooltip("Selbst gebautes/positioniertes RectTransform als Kind von 'Lane Container', das die Hit-Zone markiert. Die X-Position bestimmt automatisch, wo getroffen werden muss - einfach im Editor an die gewünschte Stelle ziehen.")]
        [SerializeField] private RectTransform hitZoneMarker;

        [Header("Takt")]
        [Tooltip("Schläge pro Minute. Die Geschwindigkeit wird automatisch so berechnet, dass bei gleichmäßigem Tastendruck exakt in diesem Takt IMMER eine Note auf der Hit-Zone steht - unabhängig von der Tasten-Anzahl.")]
        [SerializeField] private float beatsPerMinute = 40f;
        [Tooltip("Abstände in Taktschlägen; leer = gleichmäßiger Takt. Beispiel: 1, 2, 1, 3.")]
        [SerializeField] private float[] beatSpacingPattern = new float[0];

        [Header("Genauigkeit")]
        [Tooltip("Ab welchem Anteil der halben Icon-Größe (0-1) ein Treffer noch als 'Perfekt' zählt (0 = exakte Mitte).")]
        [SerializeField] private float perfectHitRatio = 0.3f;
        [Tooltip("Ab welchem Anteil der halben Icon-Größe (0-1) ein Treffer noch als 'Gut' zählt. Ab 1.0 (Icon-Rand) ist es kein Treffer mehr.")]
        [SerializeField] private float goodHitRatio = 0.6f;

        [Header("Treffereffekt")]
        [SerializeField] private float hitScaleMultiplier = 1.3f;
        [SerializeField] private float hitFlashDuration = 0.2f;
        [SerializeField] private float hitFlashBrightness = 4f;

        [Header("Feedback-Text (UI, im Canvas)")]
        [Tooltip("TextMeshProUGUI-Objekt in einem Canvas für Perfekt/Gut/OK/Schlecht. Position/Größe/Font stellst du ganz normal per RectTransform im Editor ein.")]
        [SerializeField] private TMP_Text feedbackText;
        [SerializeField] private float feedbackDuration = 0.7f;
        [Tooltip("Wie weit der Text (in UI-Pixeln) beim Ausklingen nach oben wandert.")]
        [SerializeField] private float feedbackRiseDistance = 20f;
        [Tooltip("Maximale zufällige Neigung (Grad) beim Erscheinen, für den 'hingeworfen'-Effekt.")]
        [SerializeField] private float feedbackMaxTiltDegrees = 15f;

        private Vector2 _feedbackBaseAnchoredPos;
        private float _feedbackTimer;

        private class LaneNote
        {
            public RectTransform Transform;
            public Image IconImage;
            public int KeyIndex;
            public float PositionOffset;
            public bool JudgedThisPass;
            public float FlashTimer;
            public Vector2 BaseSize;
            public float PreviousPosition; // für die Erkennung eines abgeschlossenen Umlaufs (-> neue Zufalls-Taste)
        }

        private readonly List<LaneNote> _notes = new List<LaneNote>();
        private float _currentBasePosition;
        private float _moveSpeed;
        private float _laneLength;
        private PlayerController _player; // für den Projektil-Abschuss bei Treffer, automatisch vom selben GameObject geholt
        private KeyControl[] _resolvedKeyControls;

#if UNITY_EDITOR
        /// <summary>
        /// Läuft automatisch im Editor bei jeder Inspector-Änderung. Sobald bei einem Tasten-
        /// Eintrag ein "Note Sprite Sheet" zugewiesen ist, werden alle darin enthaltenen
        /// Einzel-Sprites automatisch in "Note Aura Images" übernommen - kein manuelles
        /// Einzeln-Reinziehen mehr nötig. Nur Editor-Code, wird beim Build automatisch entfernt.
        /// </summary>
        private void OnValidate()
        {
            foreach (LaneKeyDefinition keyDefinition in laneKeys)
            {
                if (keyDefinition.noteSpriteSheet == null) continue;

                string path = AssetDatabase.GetAssetPath(keyDefinition.noteSpriteSheet);
                if (string.IsNullOrEmpty(path)) continue;

                Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
                List<Sprite> sprites = new List<Sprite>();
                foreach (Object asset in assets)
                {
                    if (asset is Sprite sprite)
                    {
                        sprites.Add(sprite);
                    }
                }

                if (sprites.Count > 0)
                {
                    keyDefinition.noteAuraImages = sprites;
                }
            }
        }
#endif

        private void Start()
        {
            _player = GetComponent<PlayerController>();

            if (laneContainer != null)
            {
                // Erzwingt den Pivot links-mittig, unabhängig davon, was im Editor eingestellt ist -
                // die gesamte Positions-Rechnung (Spawn rechts, Hit-Zone-Abstand von links) geht
                // davon aus, dass anchoredPosition.x = 0 der linken Kante entspricht.
                laneContainer.pivot = new Vector2(0f, laneContainer.pivot.y);
            }

            Canvas.ForceUpdateCanvases(); // stellt sicher, dass laneContainer.rect schon die echte Breite hat
            SpawnNotes();
            ResolveKeyControls();

            if (feedbackText != null)
            {
                _feedbackBaseAnchoredPos = feedbackText.rectTransform.anchoredPosition;
                feedbackText.gameObject.SetActive(false);
            }
        }

        private void Update()
        {
            if (Time.timeScale <= 0f) return;
            UpdateNotePositionsAndAnimation();
            HandleHitInput();
            UpdateFeedbackText();
        }

        /// <summary>
        /// Blendet die Spur (Noten + Ziel-Linie) komplett ein/aus und pausiert die komplette
        /// Logik dieses Skripts (z.B. während die Level-/Upgrade-Auswahl in der Safe Zone offen ist).
        /// </summary>
        public void SetActive(bool isActive)
        {
            enabled = isActive;

            foreach (LaneNote note in _notes)
            {
                if (note.Transform != null) note.Transform.gameObject.SetActive(isActive);
            }

            if (hitZoneMarker != null) hitZoneMarker.gameObject.SetActive(isActive);

            if (!isActive && feedbackText != null)
            {
                feedbackText.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Zerstört alle vorhandenen Noten und erzeugt sie neu (z.B. falls sich die Lane-Keys-Liste
        /// zur Laufzeit geändert hat). Aktuell nirgends automatisch aufgerufen, aber öffentlich
        /// verfügbar für später.
        /// </summary>
        public void RefreshNoteCount()
        {
            foreach (LaneNote note in _notes)
            {
                if (note.Transform != null) Destroy(note.Transform.gameObject);
            }
            _notes.Clear();

            SpawnNotes();
        }

        private void SpawnNotes()
        {
            if (laneKeys.Count == 0 || laneContainer == null) return;

            // Sichtbare Kacheln und der Pool der acht möglichen Buchstaben sind unabhängig.
            int count = Mathf.Clamp(visibleNoteCount, 2, 8);

            // Auch beim kürzesten Beat-Abstand bleibt Platz zwischen den Kacheln.
            float spacingSum = 0f;
            float minSpacing = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                spacingSum += GetBeatSpacing(i);
                minSpacing = Mathf.Min(minSpacing, GetBeatSpacing(i));
            }
            laneContainer.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                Mathf.Max(laneContainer.rect.width, (noteIconSize + 20f) * spacingSum / minSpacing));
            _laneLength = laneContainer.rect.width;

            // Ein Umlauf enthält die Summe der Abstände in Beats. Dadurch bleiben auch
            // unterschiedliche Pausen auf dem BPM-Raster, einschließlich des letzten Abstands.
            float totalBeats = 0f;
            for (int i = 0; i < count; i++) totalBeats += GetBeatSpacing(i);
            _moveSpeed = (_laneLength / totalBeats) * (beatsPerMinute / 60f);
            _currentBasePosition = 0f;
            float elapsedBeats = 0f;

            for (int i = 0; i < count; i++)
            {
                int keyIndex = Random.Range(0, laneKeys.Count); // welche Taste zuerst kommt, ist zufällig

                GameObject noteObject = new GameObject($"Note_{i}", typeof(RectTransform), typeof(Image));
                noteObject.transform.SetParent(laneContainer, worldPositionStays: false);

                RectTransform rectTransform = noteObject.GetComponent<RectTransform>();
                rectTransform.anchorMin = new Vector2(0f, 0.5f);
                rectTransform.anchorMax = new Vector2(0f, 0.5f);
                rectTransform.pivot = new Vector2(0.5f, 0.5f);
                rectTransform.sizeDelta = new Vector2(noteIconSize, noteIconSize);

                Image image = noteObject.GetComponent<Image>();
                image.preserveAspect = true;
                image.raycastTarget = false; // Noten sollen keine UI-Klicks blockieren
                image.sprite = laneKeys[keyIndex].icon;

                float positionOffset = elapsedBeats / totalBeats * _laneLength;
                elapsedBeats += GetBeatSpacing(i);

                _notes.Add(new LaneNote
                {
                    Transform = rectTransform,
                    IconImage = image,
                    KeyIndex = keyIndex,
                    PositionOffset = positionOffset,
                    BaseSize = rectTransform.sizeDelta,
                    PreviousPosition = positionOffset // Startwert = Position im allerersten Frame
                });
            }
        }

        private float GetBeatSpacing(int index)
        {
            if (beatSpacingPattern == null || beatSpacingPattern.Length == 0) return 1f;
            return Mathf.Max(1f, beatSpacingPattern[index % beatSpacingPattern.Length]);
        }

        private void UpdateNotePositionsAndAnimation()
        {
            if (_laneLength <= 0f) return;

            _currentBasePosition = Mathf.Repeat(_currentBasePosition + _moveSpeed * Time.deltaTime, _laneLength);

            float hitZonePosition = GetHitZonePosition();

            foreach (LaneNote note in _notes)
            {
                float position = Mathf.Repeat(_currentBasePosition + note.PositionOffset, _laneLength);

                // Ein Sprung von "kurz vor laneLength" auf "kurz nach 0" bedeutet: die Note ist
                // gerade komplett einmal durchgelaufen (wieder am Spawnpunkt angekommen) -> neue
                // Zufalls-Taste auswürfeln, kein Duplikat-Check nötig.
                if (position < note.PreviousPosition)
                {
                    AssignRandomKey(note);
                    note.JudgedThisPass = false;
                    note.IconImage.enabled = true;
                }
                note.PreviousPosition = position;

                float xLocal = _laneLength - position; // position 0 (gerade gespawnt, rechts) -> x = laneLength (rechter Rand)
                note.Transform.anchoredPosition = new Vector2(xLocal, note.Transform.anchoredPosition.y);

                // Verpasste Noten verschwinden hinter dem Trefferfenster.
                if (position > hitZonePosition + noteIconSize * 0.5f)
                {
                    note.JudgedThisPass = true;
                    note.IconImage.enabled = false;
                }

                UpdateHitAnimation(note);
            }
        }

        /// <summary>
        /// Würfelt für eine Note eine neue, zufällige Taste (samt passendem Icon) aus - wird bei
        /// jedem abgeschlossenen Umlauf aufgerufen. Bewusst ohne Prüfung auf Wiederholungen
        /// (dieselbe Taste darf mehrfach hintereinander vorkommen).
        /// </summary>
        private void AssignRandomKey(LaneNote note)
        {
            if (laneKeys.Count == 0) return;

            int newKeyIndex = Random.Range(0, laneKeys.Count);
            note.KeyIndex = newKeyIndex;

            if (note.IconImage != null)
            {
                note.IconImage.sprite = laneKeys[newKeyIndex].icon;
            }
        }

        /// <summary>
        /// Kürzester (vorzeichenbehafteter) Abstand zweier Positionen entlang der Spur, unter
        /// Berücksichtigung des Umlaufs (analog zu Mathf.DeltaAngle, nur für eine lineare Schleife
        /// statt Grad 0-360).
        /// </summary>
        private float GetShortestDistance(float position, float target)
        {
            return Mathf.Repeat(position - target + _laneLength / 2f, _laneLength) - _laneLength / 2f;
        }

        /// <summary>
        /// Liest die "Position" (im gleichen 0..laneLength Wertebereich wie die Noten) der Hit-Zone
        /// direkt aus der manuell im Editor platzierten Markierung (hitZoneMarker.anchoredPosition.x
        /// entspricht "Abstand von links", siehe SpawnNotes/UpdateNotePositionsAndAnimation).
        /// </summary>
        private float GetHitZonePosition()
        {
            float offsetFromLeft = hitZoneMarker != null ? hitZoneMarker.anchoredPosition.x : 0f;
            return _laneLength - offsetFromLeft;
        }

        private void UpdateHitAnimation(LaneNote note)
        {
            if (note.FlashTimer <= 0f) return;

            note.FlashTimer -= Time.deltaTime;
            float t = Mathf.Clamp01(note.FlashTimer / hitFlashDuration); // 1 = gerade getroffen, 0 = Effekt vorbei

            note.Transform.sizeDelta = Vector2.Lerp(note.BaseSize, note.BaseSize * hitScaleMultiplier, t);

            if (note.IconImage != null)
            {
                float brightness = Mathf.Lerp(1f, hitFlashBrightness, t);
                Color flashColor = Color.white * brightness;
                flashColor.a = 1f;
                note.IconImage.color = flashColor;
            }
        }

        /// <summary>
        /// Sucht für jede konfigurierte "Lane Key"-Zeichen die tatsächlich passende physische
        /// Taste auf dem AKTUELLEN Tastatur-Layout (per KeyControl.displayName statt fester
        /// Key-Enum-Position). Löst damit das klassische QWERTZ/QWERTY-Vertauschungsproblem
        /// (z.B. Y/Z), das Unitys Key-Enum sonst hätte, weil dieses eine feste physische
        /// US-Layout-Position beschreibt statt des tatsächlich gedruckten Zeichens.
        /// Läuft einmalig beim Start - falls sich das Tastatur-Layout während des Spiels ändert,
        /// müsste man das erneut aufrufen (kommt in der Praxis quasi nie vor).
        /// </summary>
        private void ResolveKeyControls()
        {
            _resolvedKeyControls = new KeyControl[laneKeys.Count];

            for (int i = 0; i < laneKeys.Count; i++)
            {
                _resolvedKeyControls[i] = FindKeyControlForCharacter(laneKeys[i].requiredCharacter);

                if (_resolvedKeyControls[i] == null)
                {
                    Debug.LogWarning($"BeatLaneController: Keine Taste für Zeichen '{laneKeys[i].requiredCharacter}' auf der aktuellen Tastatur gefunden.");
                }
            }
        }

        private KeyControl FindKeyControlForCharacter(char targetChar)
        {
            if (Keyboard.current == null) return null;

            foreach (KeyControl control in Keyboard.current.allKeys)
            {
                string displayName = control.displayName;
                if (!string.IsNullOrEmpty(displayName) && displayName.Length == 1
                    && char.ToUpperInvariant(displayName[0]) == char.ToUpperInvariant(targetChar))
                {
                    return control;
                }
            }

            return null;
        }

        private void LateUpdate()
        {
            if (laneContainer == null || hitZoneMarker == null || Camera.main == null) return;
            BillboardSprite visual = GetComponentInChildren<BillboardSprite>();
            if (visual == null) return;
            Renderer characterRenderer = visual.GetComponent<Renderer>();
            if (characterRenderer == null) return;
            Vector3 head = characterRenderer.bounds.center;
            head.y = characterRenderer.bounds.max.y;
            Vector3 screen = Camera.main.WorldToScreenPoint(head);
            screen.y += 75f;
            Canvas canvas = laneContainer.GetComponentInParent<Canvas>();
            Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera : null;
            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(
                laneContainer.parent as RectTransform, screen, uiCamera, out Vector3 target))
                laneContainer.position += target - hitZoneMarker.position;
        }

        private void HandleHitInput()
        {
            if (Keyboard.current == null || _laneLength <= 0f || _resolvedKeyControls == null) return;

            float hitZonePosition = GetHitZonePosition();
            LaneNote next = null;
            float nearest = float.MaxValue;
            foreach (LaneNote note in _notes)
            {
                if (note.JudgedThisPass) continue;
                float position = Mathf.Repeat(_currentBasePosition + note.PositionOffset, _laneLength);
                float distance = hitZonePosition - position;
                if (distance < -noteIconSize * 0.5f || distance >= nearest) continue;
                nearest = distance;
                next = note;
            }
            if (next == null) return;
            KeyControl control = _resolvedKeyControls[next.KeyIndex];
            if (control == null || !control.wasPressedThisFrame) return;

            float ratio = Mathf.Abs(nearest) / (noteIconSize * 0.5f);
            if (ratio <= 1f) RegisterHit(next, ratio);
            else
            {
                next.JudgedThisPass = true;
                ShowFeedback("Zu früh");
                if (AudioManager.Instance != null) AudioManager.Instance.PlayMissHitSound();
            }
            next.IconImage.enabled = false;
        }

        private void RegisterHit(LaneNote note, float hitRatio)
        {
            note.JudgedThisPass = true;
            note.FlashTimer = hitFlashDuration;

            string rating = hitRatio <= perfectHitRatio ? "Perfekt"
                : hitRatio <= goodHitRatio ? "Gut"
                : "OK";

            ShowFeedback(rating);
            PlayHitSound(rating);

            // Löst die Schockwellen-Aura aus (der eigentliche Schaden) - Farbe + Noten-Streusel
            // kommen von der getroffenen Taste. hitRatio steuert dort den Schaden
            // (0 = exakte Mitte = voller Schaden, 1 = Rand des Icons = kaum Schaden).
            if (_player != null)
            {
                LaneKeyDefinition keyDefinition = laneKeys[note.KeyIndex];
                _player.TriggerAuraHit(keyDefinition.auraColor, keyDefinition.noteAuraImages, hitRatio);
            }
        }

        private void PlayHitSound(string rating)
        {
            if (AudioManager.Instance == null) return;

            switch (rating)
            {
                case "Perfekt":
                    AudioManager.Instance.PlayPerfectHitSound();
                    break;
                case "Gut":
                    AudioManager.Instance.PlayGoodHitSound();
                    break;
                default:
                    AudioManager.Instance.PlayOkHitSound();
                    break;
            }
        }

        private void ShowFeedback(string rating)
        {
            if (feedbackText == null) return;

            feedbackText.text = rating;
            feedbackText.rectTransform.anchoredPosition = _feedbackBaseAnchoredPos;

            float randomTilt = Random.Range(-feedbackMaxTiltDegrees, feedbackMaxTiltDegrees);
            feedbackText.rectTransform.localRotation = Quaternion.Euler(0f, 0f, randomTilt);

            Color c = feedbackText.color;
            c.a = 1f;
            feedbackText.color = c;
            feedbackText.gameObject.SetActive(true);
            _feedbackTimer = feedbackDuration;
        }

        private void UpdateFeedbackText()
        {
            if (feedbackText == null) return;
            if (_feedbackTimer <= 0f)
            {
                if (feedbackText.gameObject.activeSelf) feedbackText.gameObject.SetActive(false);
                return;
            }

            _feedbackTimer -= Time.deltaTime;
            float t = Mathf.Clamp01(_feedbackTimer / feedbackDuration);

            feedbackText.rectTransform.anchoredPosition =
                _feedbackBaseAnchoredPos + Vector2.up * ((1f - t) * feedbackRiseDistance);

            Color c = feedbackText.color;
            c.a = t;
            feedbackText.color = c;
        }
    }
}