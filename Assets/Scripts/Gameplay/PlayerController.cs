using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using TMPro;
using RhythmWitchClone.Core;
using RhythmWitchClone.Save;
using RhythmWitchClone.Levels;
using RhythmWitchClone.Audio;

namespace RhythmWitchClone.Gameplay
{
    /// <summary>
    /// Bewegt den Spieler per WASD (bzw. Pfeiltasten) auf der Boden-Ebene (X/Z-Achse),
    /// spiegelt das Charakterbild horizontal je nach Laufrichtung und verwaltet die Gesundheit
    /// inkl. optionaler HP-Anzeige. Der Game-Over-Bildschirm selbst wird vom PauseMenuController
    /// verwaltet (der beobachtet CurrentHealth) - so bleibt dieses Script auf reine
    /// Spieler-Mechanik fokussiert, ohne UI-Overlay-Logik.
    ///
    /// Setup:
    /// - Kommt auf das Spieler-Root-GameObject (bekommt automatisch einen CharacterController).
    /// - "Visual Transform" zeigt auf das Kind-Objekt mit dem Quad + BillboardSprite-Script.
    /// - HP-Anzeige (Health Slider / Health Text) ist optional - im Canvas selbst erstellen
    ///   und hier reinziehen, oder leer lassen, falls (noch) nicht gebraucht.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Bewegung")]
        [SerializeField] private float moveSpeed = 5f;
        [SerializeField] private float gravity = -9.81f;

        [Header("Sprite-Spiegelung")]
        [Tooltip("Das Kind-Objekt mit dem sichtbaren Bild (Quad mit BillboardSprite-Script drauf).")]
        [SerializeField] private Transform visualTransform;

        [Header("Mauszeiger")]
        [Tooltip("Normaler Mauszeiger. Die Textur muss im Import auf Texture Type = 'Cursor' stehen (32x32 oder 64x64 empfohlen).")]
        [SerializeField] private Texture2D cursorNormal;
        [Tooltip("Mauszeiger beim Hovern über klickbare UI-Elemente (Buttons, Slider, etc.).")]
        [SerializeField] private Texture2D cursorHover;
        [Tooltip("Klickpunkt im Cursor-Bild, in Pixeln von oben links. Bei einem Fadenkreuz die Mitte eintragen (z.B. 16,16 bei 32x32).")]
        [SerializeField] private Vector2 cursorHotspot = Vector2.zero;

        [Header("Gesundheit")]
        [SerializeField] private int maxHealth = 100;

        [Header("HP-Anzeige (optional, selbst im Canvas erstellt)")]
        [Tooltip("Optional: Slider für eine grafische Lebensleiste.")]
        [SerializeField] private Slider healthSlider;
        [Tooltip("Optional: Text-Anzeige, z.B. '80 / 100'.")]
        [SerializeField] private TMP_Text healthText;

        [Header("Noten-Aura (Standard-Schaden bei einem Balltreffer)")]
        [Tooltip("Radius der Schockwelle in Weltraum-Einheiten - alle Gegner darin bekommen Schaden.")]
        [SerializeField] private float auraRadius = 3f;
        [Tooltip("Schaden bei exakt mittigem Balltreffer. Je ungenauer der Treffer, desto weniger Schaden.")]
        [SerializeField] private int baseAuraDamage = 25;
        [Tooltip("Wie lange die Schockwelle sichtbar ist (Sekunden) - expandiert währenddessen und blendet aus.")]
        [SerializeField] private float auraDuration = 0.4f;
        [Tooltip("Wie viele Noten-Streusel maximal gleichzeitig in der Aura verteilt werden.")]
        [SerializeField] private int auraNoteSprinkleCount = 8;
        [Tooltip("Größe der Noten-Streusel-Bilder (nicht die Tasten-Icons in der Spur, sondern die Noten-Bilder aus der Aura).")]
        [SerializeField] private float auraNoteSpriteScale = 0.7f;

        // Generische, von außen gesetzte Karten-Wirkungen (Werte selbst kommen komplett aus
        // CardAbilityController - PlayerController kennt keine Karten-Zahlen, führt nur aus,
        // was ihm über die Methoden unten übergeben wird).
        private float _damageReductionPercent; // 0-1, von SetDamageReduction gesetzt
        private int _auraDamageBonus;
        private float _auraRadiusBonus;

        [Header("Projektile (standardmäßig deaktiviert - später als Upgrade freischaltbar)")]
        [SerializeField] private bool projectilesEnabled = false;
        [Tooltip("Bild des Projektils. Zeichne es idealerweise nach 'oben' zeigend - das Skript dreht es automatisch in Flugrichtung.")]
        [SerializeField] private Texture2D projectileTexture;
        [SerializeField] private float projectileSpeed = 12f;
        [SerializeField] private float projectileScale = 0.4f;
        [SerializeField] private float projectileSpawnHeight = 1.2f;
        [Tooltip("Sicherheitsnetz: Projektil wird nach dieser Strecke zerstört, falls es sein Ziel nicht erreicht (z.B. Ziel woanders hin verschwunden).")]
        [SerializeField] private float projectileMaxRange = 30f;
        [Tooltip("Schaden bei exakt mittigem Balltreffer (0 Abstand zur Ballmitte). Je ungenauer der Treffer, desto weniger Schaden.")]
        [SerializeField] private int baseProjectileDamage = 25;
        [Tooltip("Zusätzlicher Schaden pro gekaufter 'Projektil-Schaden'-Upgrade-Stufe (siehe UpgradeController).")]
        [SerializeField] private int projectileDamagePerUpgradeLevel = 5;
        [Tooltip("Zusätzliche gleichzeitig abgefeuerte Projektile pro gekaufter 'Projektil-Anzahl'-Upgrade-Stufe.")]
        [SerializeField] private int projectilesPerUpgradeLevel = 1;

        private class Projectile
        {
            public Transform Transform;
            public EnemyController TargetEnemy;
            public int Damage;
            public float TraveledDistance;
        }

        private readonly List<Projectile> _activeProjectiles = new List<Projectile>();

        private class AuraSprinkle
        {
            public Transform Transform;
            public SpriteRenderer Renderer;
            public float Angle;
            public float TargetDistance;
        }

        private class AuraEffect
        {
            public Transform RingTransform;
            public SpriteRenderer RingRenderer;
            public readonly List<AuraSprinkle> Sprinkles = new List<AuraSprinkle>();
            public float Timer;
            public float Duration; // pro Instanz, damit normale und Spezial-Schockwelle unterschiedlich lang sein können
            public float TargetRadius; // pro Instanz, damit die Spezial-Schockwelle größer sein kann
            public Vector3 CenterPosition; // Spielerposition zum Zeitpunkt des Treffers
        }

        private readonly List<AuraEffect> _activeAuras = new List<AuraEffect>();
        private Sprite _auraRingSprite; // wird einmalig prozedural erzeugt und wiederverwendet

        [Header("Münzen (fallen von toten Gegnern)")]
        [Tooltip("Bild der Münze.")]
        [SerializeField] private Texture2D coinTexture;
        [SerializeField] private float coinScale = 0.4f;
        [Tooltip("Höhe über dem Boden, auf der die Münze liegt, sobald sie gelandet ist.")]
        [SerializeField] private float coinGroundHeight = 0.3f;
        [SerializeField] private float coinFallGravity = -9.81f;
        [Tooltip("Abstand, ab dem die Münze anfängt langsam zum Spieler zu 'laufen'.")]
        [SerializeField] private float coinMagnetRange = 3f;
        [Tooltip("Zusätzlicher Magnet-Radius pro gekaufter 'Münzmagnet'-Upgrade-Stufe.")]
        [SerializeField] private float coinMagnetRangePerUpgradeLevel = 1f;
        [SerializeField] private float coinMoveSpeed = 4f;
        [Tooltip("Abstand, ab dem die Münze als eingesammelt gilt (Spieler berührt sie).")]
        [SerializeField] private float coinCollectDistance = 0.5f;

        [Header("Münzen-Anzeige (optional, selbst im Canvas erstellt)")]
        [Tooltip("Nur der Text für die Zahl - das Icon daneben erstellst du separat direkt im Canvas.")]
        [SerializeField] private TMP_Text coinCountText;

        // CoinCount lebt jetzt in GameSession (siehe RhythmWitchClone.Core), damit die Münzanzahl
        // Szenenwechsel übersteht, ohne dass der ganze Spieler persistent gemacht werden muss.
        public int CoinCount => GameSession.CoinCount;

        [Header("Orbs (fallen selten von toten Gegnern)")]
        [Tooltip("Bild des grünen Lebens-Orbs. Leer = einfacher grüner Kreis.")]
        [SerializeField] private Texture2D healthOrbTexture;
        [Tooltip("Bild des blauen Mana-Orbs. Leer = einfacher blauer Kreis.")]
        [SerializeField] private Texture2D manaOrbTexture;
        [SerializeField] private float orbScale = 0.45f;
        [Tooltip("Chance pro besiegtem Gegner auf einen Lebens-Orb (0.06 = 6%).")]
        [Range(0f, 1f)][SerializeField] private float healthOrbDropChance = 0.06f;
        [Tooltip("Chance pro besiegtem Gegner auf einen Mana-Orb (0.06 = 6%).")]
        [Range(0f, 1f)][SerializeField] private float manaOrbDropChance = 0.06f;
        [SerializeField] private int healthOrbAmount = 25;
        [SerializeField] private float manaOrbAmount = 25f;
        [Tooltip("Wird für die Mana-Orbs gebraucht. Leer = wird automatisch in der Szene gesucht.")]
        [SerializeField] private CardAbilityController cardAbilities;

        private enum PickupType { Coin, HealthOrb, ManaOrb }

        private class Coin
        {
            public Transform Transform;
            public bool HasLanded;
            public float FallVelocityY;
            public PickupType Type;
            public float BobTimer;
        }

        private readonly List<Coin> _activeCoins = new List<Coin>();

        [Header("Level-Sieg (keine Gegner mehr übrig)")]
        [SerializeField] private GameObject winPanel;
        [SerializeField] private TMP_Text winText;
        [SerializeField] private Button backToMenuButton;
        [SerializeField] private string safeZoneSceneName = "SafeZone";
        [Tooltip("Wartezeit nach Levelstart, bevor auf 'keine Gegner mehr' geprüft wird - verhindert einen sofortigen Sieg, falls z.B. beim Laden kurzzeitig noch keine Gegner in der Szene stehen.")]
        [SerializeField] private float winCheckDelay = 1f;

        private bool _hasWonLevel;
        private bool _autoWinCheckEnabled = true;
        private float _winCheckTimer;

        public int CurrentHealth { get; private set; }
        public int MaxHealth => maxHealth;

        private CharacterController _controller;
        private Vector3 _velocity;
        private Vector3 _visualBaseScale;
        private bool _movementLocked;

        /// <summary>
        /// Sperrt/entsperrt die Bewegung von außen (z.B. TutorialManager, während ein
        /// Dialog-Schritt gerade erklärt wird, bevor der Spieler etwas ausprobieren soll).
        /// </summary>
        public void SetMovementLocked(bool locked)
        {
            _movementLocked = locked;
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

        /// <summary>
        /// Schaltet die automatische Sieg-Prüfung ("keine Gegner mehr = gewonnen") an/aus.
        /// Das Tutorial schaltet sie aus und ruft WinLevel() selbst nach dem letzten Schritt auf.
        /// </summary>
        public void SetAutoWinCheckEnabled(bool enabled)
        {
            _autoWinCheckEnabled = enabled;
        }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();

            if (visualTransform != null)
            {
                // Merkt sich die ursprüngliche Größe, damit beim Spiegeln nur das Vorzeichen
                // der X-Achse gedreht wird, statt die Skalierung jedes Mal draufzumultiplizieren.
                _visualBaseScale = visualTransform.localScale;
            }

            if (backToMenuButton != null) backToMenuButton.onClick.AddListener(BackToMenuFromWin);
        }

        private void Start()
        {
            CurrentHealth = maxHealth;
            UpdateCoinUI();
            if (winPanel != null) winPanel.SetActive(false);
            _winCheckTimer = winCheckDelay;

            ApplyCursor(false);

            ApplyUpgrades();
            LoadFromSave();
        }

        /// <summary>
        /// Lädt beim Betreten der Spiel-Szene automatisch die zuletzt gespeicherte Position
        /// für den aktuell ausgewählten Speicherstand (GameSession.SelectedSaveSlot).
        /// Passiert nichts, falls kein Slot gewählt wurde oder der Slot noch leer ist
        /// (dann bleibt der Spieler an der im Editor platzierten Startposition).
        /// </summary>
        private void LoadFromSave()
        {
            if (GameSession.SelectedSaveSlot < 0) return;

            SaveData data = SaveSystem.Load(GameSession.SelectedSaveSlot);
            if (!data.exists) return;

            ApplySaveData(data);
        }

        /// <summary>
        /// Versetzt den Spieler an die in den Save-Daten gespeicherte Position/Blickrichtung.
        /// Public, damit z.B. ein "Neu laden"-Button das auch nutzen könnte, falls später gewünscht.
        /// </summary>
        public void ApplySaveData(SaveData data)
        {
            Vector3 savedPosition = new Vector3(data.playerPosX, data.playerPosY, data.playerPosZ);

            // CharacterController kurz deaktivieren, da er sonst eigene Kollisions-Korrekturen
            // auf eine direkte Positionsänderung anwendet und die Teleportation verfälschen kann.
            _controller.enabled = false;
            transform.position = savedPosition;
            _controller.enabled = true;

            if (visualTransform != null)
            {
                float sign = data.playerFacingRight ? -1f : 1f; // gleiche Vorzeichen-Logik wie in HandleFlip()
                visualTransform.localScale = new Vector3(
                    Mathf.Abs(_visualBaseScale.x) * sign,
                    _visualBaseScale.y,
                    _visualBaseScale.z);
            }
        }

        /// <summary>
        /// Schreibt die aktuelle Position/Blickrichtung des Spielers in das übergebene SaveData-Objekt.
        /// Speichert NICHT selbst auf die Festplatte - das übernimmt der Aufrufer (z.B. PauseMenuController)
        /// per SaveSystem.Save(...), nachdem alle relevanten Systeme ihre Daten eingetragen haben.
        /// </summary>
        public void WriteToSaveData(SaveData data)
        {
            data.playerPosX = transform.position.x;
            data.playerPosY = transform.position.y;
            data.playerPosZ = transform.position.z;

            if (visualTransform != null)
            {
                data.playerFacingRight = visualTransform.localScale.x < 0f;
            }
        }

        /// <summary>
        /// Zieht dem Spieler Schaden ab (z.B. von EnemyController aufgerufen). Public, damit
        /// jedes System, das Schaden verursachen soll, das direkt aufrufen kann.
        /// </summary>
        public void TakeDamage(int amount)
        {
            if (_damageReductionPercent > 0f)
            {
                amount = Mathf.RoundToInt(amount * (1f - _damageReductionPercent));
            }

            CurrentHealth = Mathf.Max(0, CurrentHealth - amount);

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayPlayerDamagedSound(transform.position);
            }
        }

        private void Update()
        {
            // Sobald die HP auf 0 sind, hört der Spieler auf sich zu bewegen -
            // PauseMenuController zeigt parallel dazu den Game-Over-Bildschirm an.
            // _movementLocked kann von außen gesetzt werden (z.B. TutorialManager), um Bewegung
            // vorübergehend zu sperren, ohne die HP anzufassen.
            if (CurrentHealth > 0 && !_movementLocked)
            {
                HandleMovement();
                HandleFlip();
            }

            UpdateHealthUI();
            UpdateProjectiles();
            UpdateCoins();
            UpdateAuras();
            UpdateCursor();
            CheckForLevelWin();
        }

        private int _projectilesPerShot = 1;

        /// <summary>
        /// Wendet die gekauften Upgrade-Stufen (siehe UpgradeController, gehalten in GameSession -
        /// pro Speicherstand getrennt) auf die Spieler-Werte an. Läuft einmal beim Levelstart -
        /// wirkt sich also erst beim nächsten Betreten einer Szene aus, nicht sofort beim Kauf
        /// in der Safe Zone (das Panel dort ist ohnehin eine andere Szene als das eigentliche Level).
        /// </summary>
        private void ApplyUpgrades()
        {
            int damageLevel = GameSession.GetUpgradeLevel(UpgradeController.ProjectileDamageKey);
            baseProjectileDamage += damageLevel * projectileDamagePerUpgradeLevel;

            int magnetLevel = GameSession.GetUpgradeLevel(UpgradeController.CoinMagnetKey);
            coinMagnetRange += magnetLevel * coinMagnetRangePerUpgradeLevel;

            int projectileCountLevel = GameSession.GetUpgradeLevel(UpgradeController.ProjectileCountKey);
            _projectilesPerShot = 1 + projectileCountLevel * projectilesPerUpgradeLevel;
        }

        /// <summary>
        /// Wird von BeatLaneController bei jedem erfolgreichen Treffer aufgerufen - das ist der
        /// eigentliche Standard-Schaden (Schockwellen-Aura um den Spieler). Projektile feuern hier
        /// zusätzlich nur, wenn "Projectiles Enabled" aktiv ist (standardmäßig aus, später als
        /// Upgrade freischaltbar). hitRatio kommt 1:1 von BeatLaneController
        /// (0 = exakte Mitte, 1 = Rand getroffen).
        /// </summary>
        public void TriggerAuraHit(Color auraColor, List<Sprite> noteSprites, float hitRatio)
        {
            float effectiveRadius = auraRadius + _auraRadiusBonus;
            int effectiveBaseDamage = baseAuraDamage + _auraDamageBonus;
            int damage = Mathf.Max(1, Mathf.RoundToInt(effectiveBaseDamage * Mathf.Clamp01(1f - hitRatio)));

            EnemyController[] enemies = FindObjectsByType<EnemyController>(FindObjectsSortMode.None);
            foreach (EnemyController enemy in enemies)
            {
                float distance = Vector3.Distance(enemy.transform.position, transform.position);
                if (distance <= effectiveRadius)
                {
                    enemy.TakeDamage(damage, transform.position);
                }
            }

            SpawnAuraVisual(auraColor, noteSprites, effectiveRadius, auraDuration);

            if (projectilesEnabled)
            {
                FireProjectileAtNearestEnemy(hitRatio);
            }
        }

        /// <summary>
        /// Setzt, wie viel Prozent Schaden aktuell reduziert werden (0 = keine Reduktion,
        /// 0.3 = 30% weniger). Wird von CardAbilityController beim An-/Abwählen der
        /// Verteidigungskarte aufgerufen (0 zum Zurücksetzen).
        /// </summary>
        public void SetDamageReduction(float reductionPercent)
        {
            _damageReductionPercent = reductionPercent;
        }

        /// <summary>
        /// Heilt den Spieler um den angegebenen Betrag (bis maximal Max Health). Public, damit
        /// z.B. CardAbilityController das periodisch für die Verteidigungskarte aufrufen kann.
        /// </summary>
        /// <summary>Setzt den Spieler direkt an eine Position (z.B. Startpunkt im Endlos-Modus).</summary>
        public void TeleportTo(Vector3 position)
        {
            _controller.enabled = false;
            transform.position = position;
            _controller.enabled = true;
        }

        public void Heal(int amount)
        {
            CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + amount);
        }

        /// <summary>
        /// Setzt einen zusätzlichen Schadens-/Radius-Bonus für die Noten-Aura (0/0 zum
        /// Zurücksetzen). Wird von CardAbilityController beim An-/Abwählen der Angriffskarte
        /// aufgerufen.
        /// </summary>
        public void SetAuraBonus(int damageBonus, float radiusBonus)
        {
            _auraDamageBonus = damageBonus;
            _auraRadiusBonus = radiusBonus;
        }

        /// <summary>
        /// Löst eine Schockwelle mit den übergebenen Werten aus: stößt alle Gegner im Radius weg
        /// und betäubt sie, plus die passende visuelle Schockwelle. Komplett parametrisiert -
        /// PlayerController kennt selbst keine Karten-Werte, die kommen von CardAbilityController.
        /// </summary>
        public void TriggerSpecialShockwave(float radius, float knockbackForce, float stunDuration, Color visualColor, float visualDuration)
        {
            EnemyController[] enemies = FindObjectsByType<EnemyController>(FindObjectsSortMode.None);
            foreach (EnemyController enemy in enemies)
            {
                float distance = Vector3.Distance(enemy.transform.position, transform.position);
                if (distance <= radius)
                {
                    Vector3 direction = enemy.transform.position - transform.position;
                    direction.y = 0f;
                    enemy.ApplyStun(direction, knockbackForce, stunDuration);
                }
            }

            SpawnAuraVisual(visualColor, null, radius, visualDuration);
        }

        /// <summary>
        /// Erzeugt die visuelle Schockwelle: ein expandierender, ausblendender Ring in der
        /// übergebenen Farbe, plus (falls Noten-Bilder übergeben werden) Noten-Streusel, die
        /// vom Zentrum aus nach außen wandern - werden quasi vom Ring "mitgetragen". radius/duration
        /// bestimmen Größe und Dauer - dadurch nutzt auch die größere Spezial-Schockwelle exakt
        /// dieselbe Logik wie ein normaler Balltreffer, nur mit anderen Werten.
        /// </summary>
        private void SpawnAuraVisual(Color auraColor, List<Sprite> noteSprites, float radius, float duration)
        {
            Vector3 centerPosition = transform.position;

            GameObject ringObject = new GameObject("AuraRing");
            ringObject.transform.position = centerPosition + Vector3.up * 0.05f;
            ringObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // flach auf den Boden gelegt
            ringObject.transform.localScale = Vector3.one * 0.01f; // startet winzig, wächst dann

            SpriteRenderer ringRenderer = ringObject.AddComponent<SpriteRenderer>();
            ringRenderer.sprite = GetAuraRingSprite();
            Color startColor = auraColor;
            startColor.a = 1f;
            ringRenderer.color = startColor;

            AuraEffect aura = new AuraEffect
            {
                RingTransform = ringObject.transform,
                RingRenderer = ringRenderer,
                Timer = 0f,
                Duration = duration,
                TargetRadius = radius,
                CenterPosition = centerPosition
            };

            if (noteSprites != null && noteSprites.Count > 0)
            {
                for (int i = 0; i < auraNoteSprinkleCount; i++)
                {
                    Sprite noteSprite = noteSprites[Random.Range(0, noteSprites.Count)];

                    GameObject noteObject = new GameObject("AuraNoteSprinkle");
                    noteObject.transform.position = centerPosition + Vector3.up * 0.6f; // startet mittig, wandert dann per UpdateAuras nach außen
                    noteObject.transform.localScale = Vector3.one * auraNoteSpriteScale;

                    if (Camera.main != null)
                    {
                        // Kamera dreht sich in diesem Projekt nie - einmaliges Ausrichten reicht.
                        noteObject.transform.rotation = Camera.main.transform.rotation;
                    }

                    SpriteRenderer noteRenderer = noteObject.AddComponent<SpriteRenderer>();
                    noteRenderer.sprite = noteSprite;

                    aura.Sprinkles.Add(new AuraSprinkle
                    {
                        Transform = noteObject.transform,
                        Renderer = noteRenderer,
                        Angle = Random.Range(0f, 360f) * Mathf.Deg2Rad,
                        TargetDistance = Random.Range(radius * 0.3f, radius)
                    });
                }
            }

            _activeAuras.Add(aura);
        }

        /// <summary>
        /// Erzeugt einmalig eine prozedurale Ring-Textur (heller Ring, zur Mitte und zum äußeren
        /// Rand hin transparent) und cached sie - jede Aura verwendet dasselbe Sprite, nur mit
        /// unterschiedlicher Farbe/Größe/Ausblenden.
        /// </summary>
        private Sprite GetAuraRingSprite()
        {
            if (_auraRingSprite != null) return _auraRingSprite;

            const int size = 128;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Vector2 center = new Vector2(size / 2f, size / 2f);
            float maxDist = size / 2f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float normalizedDist = Vector2.Distance(new Vector2(x, y), center) / maxDist;
                    float alpha = Mathf.Clamp01(1f - Mathf.Abs(normalizedDist - 0.75f) * 6f);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            texture.Apply();

            _auraRingSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
            return _auraRingSprite;
        }

        private void UpdateAuras()
        {
            for (int i = _activeAuras.Count - 1; i >= 0; i--)
            {
                AuraEffect aura = _activeAuras[i];
                aura.Timer += Time.deltaTime;
                float t = Mathf.Clamp01(aura.Timer / aura.Duration);

                float currentDiameter = Mathf.Lerp(0f, aura.TargetRadius * 2f, t);
                aura.RingTransform.localScale = new Vector3(currentDiameter, currentDiameter, 1f);

                Color ringColor = aura.RingRenderer.color;
                ringColor.a = 1f - t;
                aura.RingRenderer.color = ringColor;

                foreach (AuraSprinkle sprinkle in aura.Sprinkles)
                {
                    if (sprinkle.Transform == null || sprinkle.Renderer == null) continue;

                    float currentDistance = Mathf.Lerp(0f, sprinkle.TargetDistance, t);
                    Vector3 direction = new Vector3(Mathf.Cos(sprinkle.Angle), 0f, Mathf.Sin(sprinkle.Angle));
                    sprinkle.Transform.position = aura.CenterPosition + direction * currentDistance + Vector3.up * 0.6f;

                    Color c = sprinkle.Renderer.color;
                    c.a = 1f - t;
                    sprinkle.Renderer.color = c;
                }

                if (t >= 1f)
                {
                    Destroy(aura.RingTransform.gameObject);
                    foreach (AuraSprinkle sprinkle in aura.Sprinkles)
                    {
                        if (sprinkle.Transform != null) Destroy(sprinkle.Transform.gameObject);
                    }
                    _activeAuras.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Feuert bei einem erfolgreichen Balltreffer ein oder mehrere Projektile ab (je nach
        /// gekaufter "Projektil-Anzahl"-Stufe) - jedes auf einen der nächstgelegenen Gegner.
        /// Gibt es weniger Gegner als Projektile, werden die Ziele einfach mehrfach verwendet.
        /// hitRatio kommt 1:1 von BeatLaneController (0 = exakte Mitte, 1 = Rand getroffen).
        /// </summary>
        public void FireProjectileAtNearestEnemy(float hitRatio)
        {
            List<EnemyController> targets = FindNearestEnemies(_projectilesPerShot);
            if (targets.Count == 0) return;

            int damage = Mathf.Max(1, Mathf.RoundToInt(baseProjectileDamage * Mathf.Clamp01(1f - hitRatio)));

            for (int i = 0; i < _projectilesPerShot; i++)
            {
                EnemyController target = targets[i % targets.Count];
                SpawnProjectile(target, damage);
            }
        }

        /// <summary>
        /// Gibt bis zu "count" Gegner zurück, sortiert vom nächstgelegenen an aufsteigend.
        /// </summary>
        private List<EnemyController> FindNearestEnemies(int count)
        {
            EnemyController[] enemies = FindObjectsByType<EnemyController>(FindObjectsSortMode.None);
            List<EnemyController> sorted = new List<EnemyController>(enemies);

            sorted.Sort((a, b) =>
            {
                float distA = (a.transform.position - transform.position).sqrMagnitude;
                float distB = (b.transform.position - transform.position).sqrMagnitude;
                return distA.CompareTo(distB);
            });

            if (sorted.Count > count)
            {
                sorted.RemoveRange(count, sorted.Count - count);
            }

            return sorted;
        }

        private void SpawnProjectile(EnemyController target, int damage)
        {
            GameObject projectileObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            projectileObject.name = "Projectile";

            Collider existingCollider = projectileObject.GetComponent<Collider>();
            if (existingCollider != null) Destroy(existingCollider);

            projectileObject.transform.position = transform.position + Vector3.up * projectileSpawnHeight;
            projectileObject.transform.localScale = Vector3.one * projectileScale;

            MeshRenderer renderer = projectileObject.GetComponent<MeshRenderer>();

            // Erzeugt automatisch ein passendes, transparenzfähiges Material - kein manuelles
            // Material-Erstellen/Zuweisen im Editor nötig.
            Shader spriteShader = Shader.Find("Sprites/Default");
            if (spriteShader != null)
            {
                renderer.material = new Material(spriteShader);
            }
            if (projectileTexture != null)
            {
                renderer.material.mainTexture = projectileTexture;
            }

            _activeProjectiles.Add(new Projectile
            {
                Transform = projectileObject.transform,
                TargetEnemy = target,
                Damage = damage,
                TraveledDistance = 0f
            });

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayProjectileFiredSound(transform.position);
            }
        }

        private void UpdateProjectiles()
        {
            for (int i = _activeProjectiles.Count - 1; i >= 0; i--)
            {
                Projectile p = _activeProjectiles[i];

                // Ziel existiert nicht mehr (z.B. zwischenzeitlich von einem anderen Projektil getötet).
                if (p.TargetEnemy == null)
                {
                    Destroy(p.Transform.gameObject);
                    _activeProjectiles.RemoveAt(i);
                    continue;
                }

                Vector3 targetPosition = p.TargetEnemy.transform.position + Vector3.up * projectileSpawnHeight;
                Vector3 toTarget = targetPosition - p.Transform.position;
                float stepDistance = projectileSpeed * Time.deltaTime;

                if (toTarget.magnitude <= stepDistance)
                {
                    // Treffer!
                    p.TargetEnemy.TakeDamage(p.Damage, p.Transform.position);
                    Destroy(p.Transform.gameObject);
                    _activeProjectiles.RemoveAt(i);
                    continue;
                }

                Vector3 direction = toTarget.normalized;
                p.Transform.position += direction * stepDistance;

                // Zeigt immer in Richtung des Ziels UND bleibt dabei zur Kamera ausgerichtet
                // (wie ein Billboard, das zusätzlich in der Bildebene zur Flugrichtung rotiert ist).
                Vector3 faceCameraDirection = Camera.main != null ? -Camera.main.transform.forward : Vector3.forward;
                p.Transform.rotation = Quaternion.LookRotation(faceCameraDirection, direction);

                p.TraveledDistance += stepDistance;
                if (p.TraveledDistance > projectileMaxRange)
                {
                    Destroy(p.Transform.gameObject);
                    _activeProjectiles.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Erzeugt eine Münze an der übergebenen Position (z.B. von EnemyController beim Tod
        /// aufgerufen). Die Münze fällt zunächst zu Boden, "läuft" danach bei Nähe zum Spieler
        /// langsam hinterher und wird bei Berührung eingesammelt.
        /// </summary>
        public void SpawnCoin(Vector3 position)
        {
            SpawnPickup(position, PickupType.Coin);
        }

        /// <summary>
        /// Würfelt (unabhängig voneinander) aus, ob ein grüner Lebens-Orb und/oder ein blauer
        /// Mana-Orb fällt. Wird von EnemyController beim Tod aufgerufen.
        /// </summary>
        public void TrySpawnOrbs(Vector3 position)
        {
            if (Random.value < healthOrbDropChance) SpawnPickup(position + new Vector3(0.4f, 0f, 0f), PickupType.HealthOrb);
            if (Random.value < manaOrbDropChance) SpawnPickup(position + new Vector3(-0.4f, 0f, 0f), PickupType.ManaOrb);
        }

        private void SpawnPickup(Vector3 position, PickupType type)
        {
            GameObject pickupObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            pickupObject.name = type.ToString();

            Collider existingCollider = pickupObject.GetComponent<Collider>();
            if (existingCollider != null) Destroy(existingCollider);

            pickupObject.transform.position = position + Vector3.up * 1f; // startet leicht erhöht, fällt dann runter
            pickupObject.transform.localScale = Vector3.one * (type == PickupType.Coin ? coinScale : orbScale);

            BillboardSprite billboard = pickupObject.AddComponent<BillboardSprite>();
            Texture2D texture = type == PickupType.Coin ? coinTexture : type == PickupType.HealthOrb ? healthOrbTexture : manaOrbTexture;
            if (texture != null)
            {
                billboard.SetTexture(texture);
            }
            else if (type != PickupType.Coin)
            {
                // Kein Bild eingetragen: einfach grün/blau einfärben, damit man den Orb trotzdem erkennt.
                Renderer pickupRenderer = pickupObject.GetComponent<Renderer>();
                if (pickupRenderer != null)
                {
                    pickupRenderer.material.color = type == PickupType.HealthOrb ? new Color(0.45f, 0.9f, 0.35f) : new Color(0.35f, 0.6f, 1f);
                }
            }

            _activeCoins.Add(new Coin
            {
                Transform = pickupObject.transform,
                HasLanded = false,
                FallVelocityY = 0f,
                Type = type,
                BobTimer = Random.value * 10f
            });
        }

        private void CollectPickup(Coin pickup)
        {
            switch (pickup.Type)
            {
                case PickupType.HealthOrb:
                    Heal(healthOrbAmount);
                    break;
                case PickupType.ManaOrb:
                    if (cardAbilities == null) cardAbilities = FindFirstObjectByType<CardAbilityController>();
                    if (cardAbilities != null) cardAbilities.AddMana(manaOrbAmount);
                    break;
                default:
                    GameSession.AddCoins(1);
                    UpdateCoinUI();
                    break;
            }

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayCoinPickupSound(pickup.Transform.position);
            }
        }

        private void UpdateCoins()
        {
            for (int i = _activeCoins.Count - 1; i >= 0; i--)
            {
                Coin coin = _activeCoins[i];

                if (!coin.HasLanded)
                {
                    coin.FallVelocityY += coinFallGravity * Time.deltaTime;

                    Vector3 pos = coin.Transform.position;
                    pos.y += coin.FallVelocityY * Time.deltaTime;

                    if (pos.y <= coinGroundHeight)
                    {
                        pos.y = coinGroundHeight;
                        coin.HasLanded = true;
                        coin.FallVelocityY = 0f;
                    }

                    coin.Transform.position = pos;
                    continue;
                }

                // Orbs schweben sanft auf und ab, damit sie auffallen.
                if (coin.Type != PickupType.Coin)
                {
                    coin.BobTimer += Time.deltaTime;
                    Vector3 bobPosition = coin.Transform.position;
                    bobPosition.y = coinGroundHeight + 0.15f + Mathf.Sin(coin.BobTimer * 3f) * 0.12f;
                    coin.Transform.position = bobPosition;
                }

                Vector3 flatOffset = coin.Transform.position - transform.position;
                flatOffset.y = 0f;
                float distance = flatOffset.magnitude;

                if (distance <= coinCollectDistance)
                {
                    CollectPickup(coin);
                    Destroy(coin.Transform.gameObject);
                    _activeCoins.RemoveAt(i);
                    continue;
                }

                if (distance <= coinMagnetRange)
                {
                    Vector3 targetPosition = transform.position;
                    targetPosition.y = coin.Transform.position.y; // Höhe beibehalten, nur horizontal "laufen"

                    Vector3 direction = (targetPosition - coin.Transform.position).normalized;
                    coin.Transform.position += direction * coinMoveSpeed * Time.deltaTime;
                }
            }
        }

        private void UpdateCoinUI()
        {
            if (coinCountText != null)
            {
                coinCountText.text = CoinCount.ToString();
            }
        }

        /// <summary>
        /// Aktualisiert die Münzanzeige sofort von außen (z.B. von UpgradeController direkt
        /// nach einem Kauf, statt erst beim nächsten Münzfund oder Szenenstart).
        /// </summary>
        public void RefreshCoinDisplay()
        {
            UpdateCoinUI();
        }

        private void UpdateHealthUI()
        {
            if (healthSlider != null)
            {
                healthSlider.maxValue = maxHealth;
                healthSlider.value = CurrentHealth;
            }

            if (healthText != null)
            {
                int healthPercent = maxHealth > 0 ? Mathf.RoundToInt(100f * CurrentHealth / maxHealth) : 0;
                healthText.text = $"{healthPercent}%";
            }

            FollowBarEnd(healthText, healthSlider);
        }

        // Für die Hover-Erkennung - wiederverwendete Liste, damit nicht jeden Frame neuer
        // Speicher alloziert wird.
        private readonly List<RaycastResult> _cursorRaycastResults = new List<RaycastResult>();
        private bool _isHoveringClickable;

        /// <summary>
        /// Prüft jeden Frame per UI-Raycast, ob die Maus über einem klickbaren Element (Button,
        /// Slider, Toggle, Dropdown, Eingabefeld) steht, und wechselt entsprechend den Mauszeiger.
        /// Funktioniert automatisch für JEDES klickbare UI im Spiel, ohne dass dort etwas
        /// eingerichtet werden muss.
        /// </summary>
        private void UpdateCursor()
        {
            if (EventSystem.current == null || Mouse.current == null) return;

            PointerEventData pointerData = new PointerEventData(EventSystem.current)
            {
                position = Mouse.current.position.ReadValue()
            };

            _cursorRaycastResults.Clear();
            EventSystem.current.RaycastAll(pointerData, _cursorRaycastResults);

            bool hoveringClickable = false;
            foreach (RaycastResult result in _cursorRaycastResults)
            {
                Selectable selectable = result.gameObject.GetComponentInParent<Selectable>();
                if (selectable != null && selectable.interactable)
                {
                    hoveringClickable = true;
                    break;
                }
            }

            // Nur bei einem tatsächlichen Wechsel neu setzen - Cursor.SetCursor jeden Frame
            // aufzurufen kann auf manchen Systemen zu Flackern führen.
            if (hoveringClickable != _isHoveringClickable)
            {
                _isHoveringClickable = hoveringClickable;
                ApplyCursor(hoveringClickable);
            }
        }

        private void ApplyCursor(bool hovering)
        {
            Texture2D texture = hovering ? cursorHover : cursorNormal;
            Cursor.SetCursor(texture, cursorHotspot, CursorMode.Auto);
        }

        private void HandleMovement()
        {
            float horizontal = GetHorizontalInput(); // A/D bzw. Pfeiltasten links/rechts
            float vertical = GetVerticalInput();      // W/S bzw. Pfeiltasten hoch/runter

            Vector3 moveDirection = new Vector3(horizontal, 0f, vertical).normalized;

            // Einfache Schwerkraft, damit der CharacterController zuverlässig auf dem Boden bleibt.
            if (_controller.isGrounded && _velocity.y < 0f)
            {
                _velocity.y = -2f;
            }
            _velocity.y += gravity * Time.deltaTime;

            float groundSpeedMultiplier = LevelManager.Instance != null ? LevelManager.Instance.GetSpeedMultiplierAt(transform.position) : 1f; // z.B. Büsche bremsen
            Vector3 motion = moveDirection * moveSpeed * groundSpeedMultiplier + Vector3.up * _velocity.y;
            _controller.Move(motion * Time.deltaTime);
        }

        private float GetHorizontalInput()
        {
            if (Keyboard.current == null) return 0f;

            float value = 0f;
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) value -= 1f;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) value += 1f;
            return value;
        }

        private float GetVerticalInput()
        {
            if (Keyboard.current == null) return 0f;

            float value = 0f;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) value -= 1f;
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) value += 1f;
            return value;
        }

        private void HandleFlip()
        {
            if (visualTransform == null) return;

            float horizontal = GetHorizontalInput();
            if (Mathf.Approximately(horizontal, 0f)) return; // Nur vor/zurück laufen ändert die Blickrichtung nicht

            bool movingRight = horizontal > 0f;
            float flippedSignX = movingRight ? -1f : 1f;

            visualTransform.localScale = new Vector3(
                Mathf.Abs(_visualBaseScale.x) * flippedSignX,
                _visualBaseScale.y,
                _visualBaseScale.z);
        }

        /// <summary>
        /// Prüft, ob keine Gegner mehr in der Szene übrig sind (jeder Gegner-Prefab läuft über
        /// EnemyController und liegt auf der "Enemy"-Layer - das Zählen der EnemyController-
        /// Instanzen entspricht hier also genau dem Zählen der Objekte auf dieser Layer).
        /// Sobald keine mehr da sind, gilt das Level als gewonnen.
        /// </summary>
        private void CheckForLevelWin()
        {
            if (_hasWonLevel || !_autoWinCheckEnabled) return;
            if (SceneManager.GetActiveScene().name == safeZoneSceneName) return; // Safe Zone ist kein Level

            if (_winCheckTimer > 0f)
            {
                _winCheckTimer -= Time.deltaTime;
                return;
            }

            EnemyController[] remainingEnemies = FindObjectsByType<EnemyController>(FindObjectsSortMode.None);
            if (remainingEnemies.Length == 0)
            {
                WinLevel();
            }
        }

        /// <summary>
        /// Löst den Level-Sieg aus (Win-Panel öffnen, Level als geschafft markieren, Sound
        /// abspielen). Public, damit auch TutorialManager das nach dem letzten Tutorial-Schritt direkt
        /// aufrufen kann, nicht nur die automatische Gegner-Zähl-Prüfung.
        /// </summary>
        public void WinLevel()
        {
            _hasWonLevel = true;

            if (LevelManager.Instance != null)
            {
                LevelManager.Instance.MarkCurrentLevelCompleted();
            }

            if (winText != null)
            {
                winText.text = "Level geschafft!";
            }

            if (winPanel != null)
            {
                winPanel.SetActive(true);
            }

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayLevelWinSound();
            }

            // Bewusst KEIN Time.timeScale = 0 - das Spiel läuft im Hintergrund normal weiter,
            // damit der Spieler noch herumlaufende Münzen einsammeln kann.
        }

        private void BackToMenuFromWin()
        {
            SceneManager.LoadScene(safeZoneSceneName);
        }
    }
}