using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
using RhythmWitchClone.Core;
using RhythmWitchClone.Gameplay;

namespace RhythmWitchClone.Levels
{
    /// <summary>
    /// Upgrade-Station in der Safe Zone - gleiches Grundprinzip wie SafeZoneController
    /// (Trigger-Bereich, "E"-Tastenhinweis, öffnet ein Panel). Bietet 4 Upgrades an:
    /// Tasten-Anzahl, Projektil-Anzahl, Projektil-Schaden, Münzmagnet-Radius.
    ///
    /// Die Upgrade-Stufen werden über GameSession pro Speicherstand getrennt gehalten (siehe die
    /// oben in dieser Klasse) -
    /// dadurch wendet BeatLaneController/PlayerController sie beim nächsten Levelstart
    /// automatisch selbst an, ohne dass dieses Script irgendeine Referenz auf laufende
    /// Level-Objekte braucht.
    ///
    /// Bezahlt wird mit GameSession.CoinCount (dieselben Münzen, die in den Levels gesammelt werden).
    /// Die Kosten steigen mit jeder gekauften Stufe.
    ///
    /// Setup:
    /// - Kommt auf ein GameObject mit einem Collider (Is Trigger = an) in der Safe Zone.
    /// - Der Spieler braucht den Tag "Player" (wie beim Level-Auswahl-Trigger).
    /// </summary>
    public class UpgradeController : MonoBehaviour
    {
        // Zentrale Schlüssel für die Upgrade-Stufen (werden in GameSession gehalten, pro
        // Speicherstand getrennt) - public, damit BeatLaneController/PlayerController sie beim Anwenden der Upgrades
        // referenzieren können, ohne den Schlüssel selbst nochmal als String zu tippen.
        public const string BallCountKey = "Upgrade_BallCount";
        public const string ProjectileCountKey = "Upgrade_ProjectileCount";
        public const string ProjectileDamageKey = "Upgrade_ProjectileDamage";
        public const string CoinMagnetKey = "Upgrade_CoinMagnet";

        [Header("Interaktion")]
        [Tooltip("UI-Element mit dem 'E'-Tastenhinweis, das erscheint, wenn der Spieler in Reichweite ist.")]
        [SerializeField] private GameObject keyPromptUI;
        [SerializeField] private GameObject upgradePanel;
        [SerializeField] private Button closeButton;

        [Tooltip("Für die sofortige Aktualisierung der Münzanzeige nach einem Kauf.")]
        [SerializeField] private PlayerController player;

        [Tooltip("Wird ausgeblendet/pausiert, solange das Upgrade-Panel offen ist (wie bei der Level-Auswahl).")]
        [SerializeField] private BeatLaneController beatLane;

        [Header("Kosten")]
        [SerializeField] private int baseCost = 10;
        [SerializeField] private int costIncreasePerLevel = 5;

        [Header("Tasten-Anzahl")]
        [SerializeField] private TMP_Text ballCountLabel;
        [SerializeField] private Button ballCountBuyButton;
        [SerializeField] private int ballCountMaxLevel = 6;

        [Header("Projektil-Anzahl")]
        [SerializeField] private TMP_Text projectileCountLabel;
        [SerializeField] private Button projectileCountBuyButton;
        [SerializeField] private int projectileCountMaxLevel = 4;

        [Header("Projektil-Schaden")]
        [SerializeField] private TMP_Text projectileDamageLabel;
        [SerializeField] private Button projectileDamageBuyButton;
        [SerializeField] private int projectileDamageMaxLevel = 10;

        [Header("Münzmagnet-Radius")]
        [SerializeField] private TMP_Text coinMagnetLabel;
        [SerializeField] private Button coinMagnetBuyButton;
        [SerializeField] private int coinMagnetMaxLevel = 10;

        [SerializeField] private Sprite shopFrame;
        [SerializeField] private Sprite shopButton;
        private TMP_Text balanceLabel;
        private bool _playerInRange;

        private void Awake()
        {
            ballCountBuyButton.onClick.AddListener(() => TryBuy(BallCountKey, ballCountMaxLevel));
            projectileCountBuyButton.onClick.AddListener(() => TryBuy(ProjectileCountKey, projectileCountMaxLevel));
            projectileDamageBuyButton.onClick.AddListener(() => TryBuy(ProjectileDamageKey, projectileDamageMaxLevel));
            coinMagnetBuyButton.onClick.AddListener(() => TryBuy(CoinMagnetKey, coinMagnetMaxLevel));

            if (closeButton != null) closeButton.onClick.AddListener(CloseUpgradePanel);
        }

        private void Start()
        {
            BuildShopUI();
            if (keyPromptUI != null) keyPromptUI.SetActive(false);
            if (upgradePanel != null) upgradePanel.SetActive(false);
        }

        private static RectTransform ShopRect(string name, Transform parent, Vector2 min, Vector2 max)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = min; rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        private TMP_Text ShopLabel(string name, Transform parent, Vector2 min, Vector2 max, float size)
        {
            var label = ShopRect(name, parent, min, max).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = TMP_Settings.defaultFontAsset;
            label.fontSize = size;
            label.enableAutoSizing = true;
            label.fontSizeMin = 14; label.fontSizeMax = size;
            label.color = new Color(.22f, .16f, .10f);
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            return label;
        }

        private void BuildShopUI()
        {
            if (upgradePanel == null || shopFrame == null) return;
            foreach (Transform child in upgradePanel.transform) child.gameObject.SetActive(false);
            var panel = (RectTransform)upgradePanel.transform;
            panel.anchorMin = Vector2.zero; panel.anchorMax = Vector2.one;
            panel.offsetMin = panel.offsetMax = Vector2.zero;
            var shade = upgradePanel.GetComponent<Image>();
            if (shade != null) { shade.sprite = null; shade.color = new Color(0, 0, 0, .72f); }
            var frame = ShopRect("Shop parchment", panel, new Vector2(.10f, .10f), new Vector2(.90f, .90f));
            var paper = frame.gameObject.AddComponent<Image>();
            paper.sprite = shopFrame; paper.color = new Color(1f, .96f, .86f);
            ShopLabel("Title", frame, new Vector2(.1f,.84f), new Vector2(.9f,.96f), 38).text = "Die Werkstatt";
            balanceLabel = ShopLabel("Balance", frame, new Vector2(.1f,.75f), new Vector2(.9f,.84f), 24);
            var buttons = new[] { ballCountBuyButton, projectileCountBuyButton, projectileDamageBuyButton, coinMagnetBuyButton };
            var labels = new TMP_Text[4];
            for (int i = 0; i < buttons.Length; i++)
            {
                float top = .73f - i * .14f;
                var row = ShopRect("Upgrade row " + i, frame, new Vector2(.12f, top-.125f), new Vector2(.88f, top));
                row.gameObject.AddComponent<Image>().color = new Color(.48f,.35f,.19f,.09f);
                labels[i] = ShopLabel("Upgrade", row, new Vector2(.025f,.08f), new Vector2(.64f,.92f), 24);
                labels[i].alignment = TextAlignmentOptions.MidlineLeft;
                StyleShopButton(buttons[i], row, new Vector2(.66f,.08f), new Vector2(.98f,.92f), "Kaufen");
            }
            ballCountLabel = labels[0]; projectileCountLabel = labels[1];
            projectileDamageLabel = labels[2]; coinMagnetLabel = labels[3];
            ShopLabel("Hint", frame, new Vector2(.1f,.115f), new Vector2(.9f,.17f), 18).text = "Verbesserungen gelten ab dem nächsten Level.";
            StyleShopButton(closeButton, frame, new Vector2(.37f,.035f), new Vector2(.63f,.11f), "Schließen");
        }

        private void StyleShopButton(Button button, Transform parent, Vector2 min, Vector2 max, string title)
        {
            if (button == null) return;
            button.transform.SetParent(parent, false);
            button.gameObject.SetActive(true);
            var rect = (RectTransform)button.transform;
            rect.anchorMin = min; rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            foreach (Transform child in rect) child.gameObject.SetActive(false);
            var image = button.GetComponent<Image>();
            if (image != null) { image.sprite = shopButton; image.color = Color.white; image.type = Image.Type.Simple; }
            ShopLabel("Action", rect, new Vector2(.1f,.1f), new Vector2(.9f,.9f), 23).text = title;
        }

        private void Update()
        {
            if (Time.timeScale <= 0f) return;
            if (!_playerInRange) return;
            if (upgradePanel != null && upgradePanel.activeSelf) return; // schon offen

            Key key = RhythmancySettings.GetKeyForAction("Interaktion");
            if (Keyboard.current != null && key != Key.None && Keyboard.current[key].wasPressedThisFrame)
            {
                OpenUpgradePanel();
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

        private void OpenUpgradePanel()
        {
            if (keyPromptUI != null) keyPromptUI.SetActive(false);
            if (upgradePanel != null) upgradePanel.SetActive(true);
            if (beatLane != null) beatLane.SetActive(false);

            RefreshUpgradeUI();
        }

        private void CloseUpgradePanel()
        {
            if (upgradePanel != null) upgradePanel.SetActive(false);
            if (_playerInRange) ShowInteractionPrompt();
            if (beatLane != null) beatLane.SetActive(true);
        }

        private void ShowInteractionPrompt()
        {
            if (keyPromptUI == null) return;
            TMP_Text label = keyPromptUI.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = $"[{RhythmancySettings.GetBinding("Interaktion")}]  UPGRADES";
            keyPromptUI.SetActive(true);
        }

        private int GetLevel(string key) => GameSession.GetUpgradeLevel(key);

        private int GetCost(string key) => baseCost + GetLevel(key) * costIncreasePerLevel;

        /// <summary>
        /// Versucht, eine Upgrade-Stufe zu kaufen: prüft Max-Stufe + genug Münzen, zieht die
        /// Kosten ab und erhöht die gespeicherte Stufe um 1. Projektil-Schaden/-Anzahl und
        /// Münzmagnet wirken sich erst beim nächsten Levelstart aus (siehe PlayerController.
        /// ApplyUpgrades) - Münzanzeige und Tasten-Anzahl werden aber SOFORT aktualisiert, damit
        /// der Kauf schon in der Safe Zone sichtbar ist.
        /// </summary>
        private void TryBuy(string key, int maxLevel)
        {
            int level = GetLevel(key);
            if (level >= maxLevel) return;

            int cost = GetCost(key);
            if (!GameSession.SpendCoins(cost)) return; // nicht genug Münzen

            GameSession.IncreaseUpgradeLevel(key);
            RefreshUpgradeUI();

            if (player != null)
            {
                player.RefreshCoinDisplay();
            }

            if (key == BallCountKey && beatLane != null)
            {
                beatLane.RefreshNoteCount();
            }
        }

        private void RefreshUpgradeUI()
        {
            if (balanceLabel != null) balanceLabel.text = $"Dein Guthaben: {GameSession.CoinCount} Münzen";
            SetPurchaseState(ballCountBuyButton, BallCountKey, ballCountMaxLevel);
            SetPurchaseState(projectileCountBuyButton, ProjectileCountKey, projectileCountMaxLevel);
            SetPurchaseState(projectileDamageBuyButton, ProjectileDamageKey, projectileDamageMaxLevel);
            SetPurchaseState(coinMagnetBuyButton, CoinMagnetKey, coinMagnetMaxLevel);
            UpdateUpgradeLabel(ballCountLabel, "Tasten", BallCountKey, ballCountMaxLevel);
            UpdateUpgradeLabel(projectileCountLabel, "Projektile", ProjectileCountKey, projectileCountMaxLevel);
            UpdateUpgradeLabel(projectileDamageLabel, "Schaden", ProjectileDamageKey, projectileDamageMaxLevel);
            UpdateUpgradeLabel(coinMagnetLabel, "Münzmagnet", CoinMagnetKey, coinMagnetMaxLevel);
        }

        private void SetPurchaseState(Button button, string key, int maxLevel)
        {
            if (button == null) return;
            button.interactable = GetLevel(key) < maxLevel && GameSession.CoinCount >= GetCost(key);
            var label = button.GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = GetLevel(key) >= maxLevel ? "Maximum" : $"{GetCost(key)} Münzen";
        }

        private void UpdateUpgradeLabel(TMP_Text label, string displayName, string key, int maxLevel)
        {
            if (label == null) return;

            int level = GetLevel(key);
            label.text = level >= maxLevel
                ? $"{displayName}: Stufe {level} (Max)"
                : $"<b>{displayName}</b>\nStufe {level} / {maxLevel}";
        }
    }
}
