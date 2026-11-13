using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using RhythmWitchClone.Core;
using RhythmWitchClone.Gameplay;
using RhythmWitchClone.Save;
using RhythmWitchClone.UI;

namespace RhythmWitchClone.UI.MainMenu
{
    /// <summary>
    /// Controller für die Menü-Szene „Menü“. Das UI wird vom Editor-Setup-Tool erzeugt und liegt
    /// danach als normale, im Inspector gestaltbare Unity-UI-Hierarchie in der Szene.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RhythmancyMenuController : MonoBehaviour
    {
        [Header("Szenen")]
        [SerializeField] private string newGameScene = "Tutorial Level";
        [SerializeField] private string savedGameScene = "SafeZone";

        private readonly Stack<string> _navigation = new Stack<string>();
        private readonly Dictionary<string, GameObject> _panels = new Dictionary<string, GameObject>();
        private string _activePanel = "MainMenuPanel";
        private Action _confirmationAction;
        private string _confirmationReturnPanel = "MainMenuPanel";
        private string _pendingRebind;
        private TMP_Text _rebindStatus;
        private bool _transitioning;

        private void Awake()
        {
            Time.timeScale = 1f;
            if (transform.Find("MainMenuPanel") == null) BuildUi();
            CachePanels();
            WireButtons();
            RefreshContinueButton();
            RefreshSaveSlots();
            RefreshSettings();
            RefreshHelp();
            RhythmancySettings.ApplyAudio();
        }

        private void Update()
        {
            if (Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame || _transitioning) return;
            if (!string.IsNullOrEmpty(_pendingRebind)) { _pendingRebind = null; RefreshSettings(); return; }
            if (_activePanel == "ConfirmationDialog") { _confirmationAction = null; ShowPanel(_confirmationReturnPanel, false); return; }
            if (_navigation.Count > 0) ShowPanel(_navigation.Pop(), false);
        }

        private void CachePanels()
        {
            string[] names = { "MainMenuPanel", "NewGamePanel", "SettingsPanel", "SaveLoadPanel", "AchievementsPanel", "HelpPanel", "CreditsPanel", "ConfirmationDialog" };
            foreach (string name in names)
            {
                Transform item = transform.Find(name);
                if (item != null) _panels[name] = item.gameObject;
            }
        }

        private void WireButtons()
        {
            Listen("PlayButton", StartNewGameFlow);
            Listen("ContinueButton", ContinueGame);
            Listen("SettingsButton", () => ShowPanel("SettingsPanel"));
            Listen("LoadButton", () => { RefreshSaveSlots(); ShowPanel("SaveLoadPanel"); });
            Listen("AchievementsButton", () => { RefreshAchievements(); ShowPanel("AchievementsPanel"); });
            Listen("HelpButton", () => { RefreshHelp(); ShowPanel("HelpPanel"); });
            Listen("CreditsButton", () => ShowPanel("CreditsPanel"));
            Listen("QuitButton", QuitGame);
            Listen("SettingsResetButton", ResetSettings);

            for (int slot = 0; slot < SaveSystem.GetSlotCount(); slot++)
            {
                int index = slot;
                Listen($"NewGameSlot{slot}", () => RequestNewGame(index));
                Listen($"LoadSlot{slot}", () => LoadSlot(index));
                Listen($"DeleteSlot{slot}", () => ConfirmDelete(index));
            }

            Listen("ConfirmYesButton", () => { Action action = _confirmationAction; _confirmationAction = null; ShowPanel(_confirmationReturnPanel, false); action?.Invoke(); });
            Listen("ConfirmNoButton", () => { _confirmationAction = null; ShowPanel(_confirmationReturnPanel, false); });
            foreach (Button button in GetComponentsInChildren<Button>(true))
            {
                if (button.name == "BackButton") button.onClick.AddListener(GoBack);
                if (button.name.StartsWith("Rebind_", StringComparison.Ordinal))
                {
                    string action = button.name.Substring("Rebind_".Length);
                    button.onClick.AddListener(() => BeginRebind(action));
                }
            }

            var master = FindSlider("MasterVolumeSlider");
            var music = FindSlider("MusicVolumeSlider");
            var sfx = FindSlider("SfxVolumeSlider");
            if (master != null) { master.value = RhythmancySettings.MasterVolume * 100f; master.onValueChanged.AddListener(v => RhythmancySettings.SetMasterVolume(v / 100f)); }
            if (music != null) { music.value = RhythmancySettings.MusicVolume * 100f; music.onValueChanged.AddListener(v => RhythmancySettings.SetMusicVolume(v / 100f)); }
            if (sfx != null) { sfx.value = RhythmancySettings.SfxVolume * 100f; sfx.onValueChanged.AddListener(v => RhythmancySettings.SetSfxVolume(v / 100f)); }
        }

        private void Listen(string buttonName, UnityEngine.Events.UnityAction callback)
        {
            Button button = FindButton(buttonName);
            if (button != null) button.onClick.AddListener(() => { if (!_transitioning) callback(); });
        }

        private Button FindButton(string buttonName) => GetComponentsInChildren<Button>(true).FirstOrDefault(button => button.name == buttonName);
        private Slider FindSlider(string sliderName) => GetComponentsInChildren<Slider>(true).FirstOrDefault(slider => slider.name == sliderName);

        private void StartNewGameFlow()
        {
            RefreshSaveSlots();
            ShowPanel("NewGamePanel");
        }

        private void RequestNewGame(int slot)
        {
            SaveData previous = SaveSystem.Load(slot);
            if (!previous.exists && !SaveSystem.SlotFileExists(slot)) { StartNewGame(slot); return; }
            ShowConfirmation($"Speicherplatz {slot + 1} überschreiben und ein neues Spiel beginnen?", () => StartNewGame(slot));
        }

        private void StartNewGame(int slot)
        {
            if (!Application.CanStreamedLevelBeLoaded(newGameScene)) { MenuStatus.Show(this, "Spielszene nicht verfügbar: " + newGameScene); return; }
            var initial = new SaveData { exists = true, level = 1, currentWave = 1, sceneName = newGameScene };
            if (!SaveSystem.Save(slot, initial)) { MenuStatus.Show(this, SaveSystem.LastError); return; }
            GameSession.StartNewSaveSlot(slot);
            RhythmWitchClone.Levels.LevelManager.Instance?.RestoreContext(-1, -1);
            Time.timeScale = 1f;
            _transitioning = true;
            StoryIntroController.Show(newGameScene);
        }

        private void ContinueGame()
        {
            int slot = SaveSystem.GetMostRecentOccupiedSlot();
            if (slot >= 0) LoadGame(slot);
        }

        private void LoadSlot(int slot)
        {
            if (SaveSystem.Load(slot).exists) LoadGame(slot);
        }

        private void LoadGame(int slot)
        {
            if (!MenuSaveService.Load(slot, savedGameScene)) MenuStatus.Show(this, MenuSaveService.LastError);
        }

        private void ConfirmDelete(int slot)
        {
            ShowConfirmation($"Speicherplatz {slot + 1} wirklich löschen?", () =>
            {
                if (!SaveSystem.DeleteSlot(slot)) MenuStatus.Show(this, SaveSystem.LastError);
                RefreshSaveSlots(); RefreshContinueButton();
            });
        }

        private void ShowConfirmation(string message, Action action)
        {
            TMP_Text label = FindText("ConfirmationMessage");
            if (label != null) label.text = message;
            _confirmationReturnPanel = _activePanel;
            _confirmationAction = action;
            ShowPanel("ConfirmationDialog", false);
        }

        private void ShowPanel(string panelName, bool pushCurrent = true)
        {
            if (!_panels.ContainsKey(panelName) || _transitioning) return;
            if (pushCurrent && panelName != _activePanel) _navigation.Push(_activePanel);
            StartCoroutine(TransitionTo(panelName));
        }

        private IEnumerator TransitionTo(string panelName)
        {
            _transitioning = true;
            CanvasGroup overlay = transform.Find("TransitionOverlay")?.GetComponent<CanvasGroup>();
            if (overlay != null)
            {
                overlay.gameObject.SetActive(true);
                for (float t = 0; t < .16f; t += Time.unscaledDeltaTime) { overlay.alpha = t / .16f; yield return null; }
            }
            foreach (var pair in _panels) pair.Value.SetActive(pair.Key == panelName);
            _activePanel = panelName;
            if (overlay != null)
            {
                for (float t = .16f; t > 0; t -= Time.unscaledDeltaTime) { overlay.alpha = t / .16f; yield return null; }
                overlay.alpha = 0f; overlay.gameObject.SetActive(false);
            }
            _transitioning = false;
            var first = _panels[panelName].GetComponentsInChildren<Button>().FirstOrDefault(b => b.interactable);
            if (first != null) first.Select();
        }

        private void GoBack()
        {
            if (_transitioning) return;
            if (_pendingRebind != null) { _pendingRebind = null; RefreshSettings(); return; }
            if (_navigation.Count > 0) ShowPanel(_navigation.Pop(), false);
            else ShowPanel("MainMenuPanel", false);
        }

        private void RefreshContinueButton()
        {
            Button button = FindButton("ContinueButton");
            if (button != null) button.interactable = SaveSystem.GetMostRecentOccupiedSlot() >= 0;
        }

        private void RefreshSaveSlots()
        {
            for (int i = 0; i < SaveSystem.GetSlotCount(); i++)
            {
                SaveData data = SaveSystem.Load(i);
                string date = data.exists && DateTime.TryParse(data.lastPlayedIso, null, DateTimeStyles.RoundtripKind, out DateTime saved) ? saved.ToLocalTime().ToString("dd.MM.yyyy HH:mm") : "Leer";
                bool fileExists = SaveSystem.SlotFileExists(i);
                string summary = data.exists ? $"Speicherplatz {i + 1}  ·  {date}\nWelle {Mathf.Max(1, data.currentWave)}  ·  Level {data.level}  ·  Münzen {data.coinCount}" : fileExists ? $"Speicherplatz {i + 1}  ·  DATEI FEHLERHAFT · Löschen oder Überschreiben bestätigen" : $"Speicherplatz {i + 1}  ·  LEER";
                TMP_Text label = FindText($"SaveSlotLabel{i}");
                if (label != null) label.text = summary;
                Button load = FindButton($"LoadSlot{i}");
                if (load != null) load.interactable = data.exists;
                Button delete = FindButton($"DeleteSlot{i}");
                if (delete != null) delete.interactable = fileExists;
                Button newGame = FindButton($"NewGameSlot{i}");
                TMP_Text newGameLabel = newGame != null ? newGame.GetComponentInChildren<TMP_Text>() : null;
                if (newGameLabel != null) newGameLabel.text = fileExists ? $"SPEICHERPLATZ {i + 1} · BELEGT · ÜBERSCHREIBEN" : $"SPEICHERPLATZ {i + 1} · LEER";
            }
            RefreshContinueButton();
        }

        private void RefreshAchievements()
        {
            Transform list = transform.Find("AchievementsPanel/AchievementListViewport/AchievementList");
            if (list == null || AchievementManager.Instance == null) return;
            for (int i = 0; i < AchievementManager.Instance.Definitions.Count; i++)
            {
                AchievementDefinition definition = AchievementManager.Instance.Definitions[i];
                if (list.Find($"AchievementRow_{i}") == null && list.childCount > 0)
                {
                    Transform copy = Instantiate(list.GetChild(0), list);
                    copy.name = $"AchievementRow_{i}";
                    copy.Find("Achievement_0").name = $"Achievement_{i}";
                    copy.Find("AchievementIcon_0").name = $"AchievementIcon_{i}";
                }
                TMP_Text label = list.Find($"AchievementRow_{i}/Achievement_{i}")?.GetComponent<TMP_Text>();
                Image icon = list.Find($"AchievementRow_{i}/AchievementIcon_{i}")?.GetComponent<Image>();
                if (label == null) continue;
                if (icon != null && definition.icon != null) icon.sprite = definition.icon;
                int progress = AchievementManager.Instance.GetProgressValue(definition);
                bool unlocked = AchievementManager.Instance.GetProgress(definition.id).unlocked;
                label.text = $"{(unlocked ? "[+]" : "[-]")}  {definition.title}  {(unlocked ? "FREIGESCHALTET" : $"{Mathf.Min(progress, definition.target)}/{definition.target}")}\n{definition.description}";
                label.color = unlocked ? new Color(.98f, .82f, .35f) : new Color(.68f, .7f, .78f);
            }
        }

        private void RefreshHelp()
        {
            TMP_Text bindings = FindText("HelpControlsText");
            if (bindings != null) bindings.text = $"Bewegung: {RhythmancySettings.GetMovementDisplay()}\nFähigkeit 1: {RhythmancySettings.GetBinding("Fähigkeit 1")}   Fähigkeit 2: {RhythmancySettings.GetBinding("Fähigkeit 2")}   Spezial: {RhythmancySettings.GetBinding("Spezialfähigkeit")}\nInteraktion: {RhythmancySettings.GetBinding("Interaktion")}   Pause: {RhythmancySettings.GetBinding("Pause")}";
        }

        private void RefreshSettings()
        {
            foreach (var pair in RhythmancySettings.GetBindings())
            {
                TMP_Text label = FindText($"RebindLabel_{pair.Key}");
                if (label != null) label.text = _pendingRebind == pair.Key ? "Taste drücken…  (Esc = Abbrechen)" : $"{pair.Key}: {pair.Value}";
            }
        }

        private void BeginRebind(string action)
        {
            _pendingRebind = action;
            RefreshSettings();
            StopAllCoroutines();
            StartCoroutine(WaitForKey(action));
        }

        private IEnumerator WaitForKey(string action)
        {
            yield return null;
            while (_pendingRebind == action)
            {
                if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
                {
                    foreach (KeyControl control in Keyboard.current.allKeys)
                    {
                        if (control.wasPressedThisFrame && control.keyCode != Key.None && control.keyCode != Key.Escape)
                        {
                            RhythmancySettings.SetBinding(action, control.keyCode.ToString());
                            _pendingRebind = null;
                            RefreshSettings();
                            RefreshHelp();
                            break;
                        }
                    }
                }
                yield return null;
            }
        }

        private void ResetSettings()
        {
            _pendingRebind = null;
            RhythmancySettings.Reset();
            RefreshSettings(); RefreshHelp();
            foreach (var pair in new[] { ("MasterVolumeSlider", RhythmancySettings.MasterVolume), ("MusicVolumeSlider", RhythmancySettings.MusicVolume), ("SfxVolumeSlider", RhythmancySettings.SfxVolume) })
            {
                Slider slider = FindSlider(pair.Item1);
                if (slider != null) slider.SetValueWithoutNotify(pair.Item2 * 100f);
            }
        }

        private void QuitGame()
        {
#if UNITY_EDITOR
            Debug.Log("Rhythmancy: Beenden angefordert (im Unity Editor bleibt der Editor geöffnet).");
#else
            Application.Quit();
#endif
        }

        private TMP_Text FindText(string objectName) => GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(text => text.name == objectName);

        /// <summary>Wird vom Setup-Menü ausgeführt. Erzeugt normale UI-Objekte, keine Laufzeit-Fakes.</summary>
        public void BuildUi()
        {
            if (transform.Find("MainMenuPanel") != null) return;
            MenuUiBuilder.Build(transform);
        }
    }

    /// <summary>Gemeinsamer Platzhalter-Baukasten. Sprites, Farben und Labels können im Inspector ersetzt werden.</summary>
    internal static class MenuUiBuilder
    {
        private static readonly Color Ink = new Color(.08f, .07f, .16f, .97f);
        private static readonly Color PanelColor = new Color(.13f, .12f, .23f, .96f);
        private static readonly Color Accent = new Color(.42f, .82f, .92f, 1f);
        private static TMP_FontAsset Font => TMP_Settings.defaultFontAsset;

        public static void Build(Transform canvasRoot)
        {
            RectTransform canvasRect = canvasRoot as RectTransform;
            if (canvasRect == null) canvasRect = canvasRoot.gameObject.AddComponent<RectTransform>();
            Canvas canvas = canvasRoot.GetComponent<Canvas>() ?? canvasRoot.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasRoot.GetComponent<CanvasScaler>() ?? canvasRoot.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            if (canvasRoot.GetComponent<GraphicRaycaster>() == null) canvasRoot.gameObject.AddComponent<GraphicRaycaster>();

            MakeImage("Background", canvasRoot, new Color(.035f, .04f, .1f), true);
            Transform decoration = MakeRect("Decorations", canvasRoot, true);
            Transform notes = MakeRect("MusicNotes", decoration, true);
            AddDecorationNote(notes, "MusicNote_01", "♪", new Vector2(.10f, .80f));
            AddDecorationNote(notes, "MusicNote_02", "♫", new Vector2(.82f, .72f));
            AddDecorationNote(notes, "MusicNote_03", "♫", new Vector2(.18f, .19f));
            AddDecorationNote(notes, "MusicNote_04", "♪", new Vector2(.90f, .30f));
            Transform lines = MakeRect("MusicLines", decoration, true);
            for (int i = 0; i < 5; i++)
            {
                GameObject line = new GameObject($"MusicLine_{i + 1:00}", typeof(RectTransform), typeof(Image));
                line.transform.SetParent(lines, false);
                RectTransform lineRect = line.GetComponent<RectTransform>();
                lineRect.anchorMin = new Vector2(.04f, .12f + i * .018f); lineRect.anchorMax = new Vector2(.96f, .12f + i * .018f);
                lineRect.sizeDelta = new Vector2(0, 2); lineRect.anchoredPosition = Vector2.zero;
                line.GetComponent<Image>().color = new Color(.3f, .42f, .64f, .25f); line.GetComponent<Image>().raycastTarget = false;
            }

            GameObject main = MakePanel("MainMenuPanel", canvasRoot, true, false);
            GameObject logo = new GameObject("Logo", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            logo.transform.SetParent(main.transform, false); logo.GetComponent<Image>().color = Color.white;
            logo.GetComponent<Image>().enabled = false;
            logo.GetComponent<LayoutElement>().minHeight = 90;
            logo.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 90);
            TMP_Text logoText = MakeLabel("PlaceholderText", logo.transform, "RHYTHMANCY", 64, Accent, TextAlignmentOptions.Center);
            logoText.fontStyle = FontStyles.Bold;
            logoText.rectTransform.anchorMin = Vector2.zero; logoText.rectTransform.anchorMax = Vector2.one;
            logoText.rectTransform.offsetMin = logoText.rectTransform.offsetMax = Vector2.zero;
            AddSubheading(main.transform, "MUSIKALISCHES ACTION-ABENTEUER", 18);
            AddButton(main.transform, "PlayButton", "SPIELEN");
            AddButton(main.transform, "ContinueButton", "WEITER");
            AddButton(main.transform, "SettingsButton", "EINSTELLUNGEN");
            AddButton(main.transform, "LoadButton", "SPIELSTAND LADEN");
            AddButton(main.transform, "AchievementsButton", "ERFOLGE");
            AddButton(main.transform, "HelpButton", "HILFE");
            AddButton(main.transform, "CreditsButton", "CREDITS");
            AddButton(main.transform, "QuitButton", "BEENDEN");

            GameObject newGame = MakePanel("NewGamePanel", canvasRoot, false, true);
            AddHeading(newGame.transform, "NEUES SPIEL", 42);
            AddBody(newGame.transform, "Wähle einen Speicherplatz. Ein belegter Platz wird erst nach Bestätigung überschrieben.");
            for (int i = 0; i < 3; i++) AddButton(newGame.transform, $"NewGameSlot{i}", $"SPEICHERPLATZ {i + 1}");
            AddBack(newGame.transform);

            GameObject settings = MakePanel("SettingsPanel", canvasRoot, false, true);
            AddHeading(settings.transform, "EINSTELLUNGEN", 42);
            AddSlider(settings.transform, "MasterVolumeSlider", "Gesamtlautstärke");
            AddSlider(settings.transform, "MusicVolumeSlider", "Musiklautstärke");
            AddSlider(settings.transform, "SfxVolumeSlider", "Soundeffekte");
            AddSubheading(settings.transform, "TASTENBELEGUNG · KLICKEN UND NEUE TASTE DRÜCKEN", 16);
            foreach (string action in new[] { "Links", "Rechts", "Hoch", "Runter", "Fähigkeit 1", "Fähigkeit 2", "Spezialfähigkeit", "Interaktion", "Pause" })
            {
                var row = MakeButton("Rebind_" + action, settings.transform, action, 45);
                row.transform.Find("Label").name = "RebindLabel_" + action;
            }
            AddButton(settings.transform, "SettingsResetButton", "EINSTELLUNGEN ZURÜCKSETZEN");
            AddBack(settings.transform);

            GameObject saves = MakePanel("SaveLoadPanel", canvasRoot, false, true);
            AddHeading(saves.transform, "SPIELSTÄNDE", 42);
            for (int i = 0; i < 3; i++)
            {
                Transform row = MakeRect($"SlotRow{i}", saves.transform);
                row.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 64);
                row.gameObject.AddComponent<HorizontalLayoutGroup>().spacing = 12;
                row.GetComponent<HorizontalLayoutGroup>().childControlWidth = true;
                row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
                var label = MakeLabel($"SaveSlotLabel{i}", row, $"Speicherplatz {i + 1}", 18, Color.white, TextAlignmentOptions.MidlineLeft);
                label.GetComponent<LayoutElement>().flexibleWidth = 1;
                AddSmallButton(row, $"LoadSlot{i}", "LADEN"); AddSmallButton(row, $"DeleteSlot{i}", "LÖSCHEN");
            }
            AddBack(saves.transform);

            GameObject achievements = MakePanel("AchievementsPanel", canvasRoot, false, true);
            AddHeading(achievements.transform, "ERFOLGE", 42);
            Transform achievementList = MakeScrollContent(achievements.transform, "AchievementList");
            string[] names = { "Erster Ton", "Überlebenskünstler", "Musikalischer Kämpfer", "Unaufhaltsam" };
            string[] descriptions = { "Besiege deinen ersten Gegner.", "Überlebe 5 Wellen.", "Besiege insgesamt 100 Gegner.", "Überlebe 10 Wellen." };
            for (int i = 0; i < names.Length; i++)
            {
                Transform row = MakeRect($"AchievementRow_{i}", achievementList);
                row.gameObject.AddComponent<HorizontalLayoutGroup>().spacing = 14;
                row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
                row.GetComponent<HorizontalLayoutGroup>().childControlHeight = true;
                row.GetComponent<HorizontalLayoutGroup>().childControlWidth = true;
                row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
                row.gameObject.AddComponent<LayoutElement>().minHeight = 86;
                GameObject icon = new GameObject($"AchievementIcon_{i}", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                icon.transform.SetParent(row, false); icon.GetComponent<LayoutElement>().minWidth = icon.GetComponent<LayoutElement>().minHeight = 64;
                icon.GetComponent<Image>().color = new Color(.35f, .38f, .55f, .8f);
                TMP_Text label = MakeLabel($"Achievement_{i}", row, $"[-]  {names[i]}\n{descriptions[i]}", 20, new Color(.68f, .7f, .78f), TextAlignmentOptions.MidlineLeft);
                label.GetComponent<LayoutElement>().flexibleWidth = 1;
            }
            AddBack(achievements.transform);

            GameObject help = MakePanel("HelpPanel", canvasRoot, false, true);
            AddHeading(help.transform, "HILFE", 42);
            Transform helpContent = MakeScrollContent(help.transform, "HelpContent");
            AddInfo(helpContent, "SPIELZIEL", "Rhythmancy ist ein musikalisches Action-Spiel. Kämpfe als Musiker gegen feindliche Musiknoten und überstehe möglichst viele Gegnerwellen.");
            AddInfo(helpContent, "STEUERUNG", "Aktuelle Bewegung und Fähigkeiten stehen unten. Die Notenspur zeigt ihre jeweiligen Angriffstasten direkt an. Diese sind szenenabhängig: im Tutorial zunächst U, im Spieler-Prefab Z/U/I/O/H/J/K/L.");
            AddInfo(helpContent, "KAMPF", "Triff die Noten im Takt in der Beatlane. Erfolgreiche Treffer lösen eine Aura aus, die Gegner in Reichweite schädigt. Bestimmte Upgrades ergänzen Projektile.");
            AddInfo(helpContent, "WELLEN", "Gegner erscheinen in aufeinanderfolgenden Wellen. Überlebe, um die nächste Welle zu erreichen.");
            AddInfo(helpContent, "FÄHIGKEITEN", "Fähigkeit 1 stärkt die Verteidigung, Fähigkeit 2 den Angriff. Die Spezialfähigkeit löst einen Flächenangriff aus.");
            MakeLabel("HelpControlsText", helpContent, "", 18, Color.white, TextAlignmentOptions.TopLeft);
            AddBack(help.transform);

            GameObject credits = MakePanel("CreditsPanel", canvasRoot, false, true);
            AddHeading(credits.transform, "RHYTHMANCY", 50);
            AddBody(credits.transform, "Ein Diplomarbeitsprojekt der HTL Wien West\n\nENTWICKLUNGSTEAM\nAlexander Kaczmarczyk\nJohannes Grieshofer\nAsefa Mirzad\nAngelo Schreiner\n\nBETREUUNG\nProf. Niel Widy\n\nHTL Wien West");
            AddBack(credits.transform);

            GameObject confirmation = MakePanel("ConfirmationDialog", canvasRoot, false, true);
            AddHeading(confirmation.transform, "BESTÄTIGEN", 36);
            MakeLabel("ConfirmationMessage", confirmation.transform, "Bist du sicher?", 22, Color.white, TextAlignmentOptions.Center);
            AddButton(confirmation.transform, "ConfirmYesButton", "JA");
            AddButton(confirmation.transform, "ConfirmNoButton", "ABBRECHEN");

            GameObject transition = new GameObject("TransitionOverlay", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            transition.transform.SetParent(canvasRoot, false);
            Stretch(transition.GetComponent<RectTransform>());
            transition.GetComponent<Image>().color = new Color(.02f, .02f, .06f, 1f);
            transition.GetComponent<CanvasGroup>().alpha = 0f;
            transition.SetActive(false);
        }

        private static GameObject MakePanel(string name, Transform parent, bool active, bool card)
        {
            GameObject panel = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            panel.transform.SetParent(parent, false);
            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = card ? new Vector2(960, name == "SettingsPanel" ? 1000 : 880) : new Vector2(680, 920);
            panel.GetComponent<Image>().color = card ? PanelColor : new Color(.07f, .07f, .14f, .65f);
            VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(36, 36, 28, 28); layout.spacing = name == "SettingsPanel" ? 4 : 12;
            layout.childAlignment = TextAnchor.MiddleCenter; layout.childControlWidth = true; layout.childControlHeight = true; layout.childForceExpandHeight = false;
            panel.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            panel.SetActive(active);
            return panel;
        }

        private static Transform MakeRect(string name, Transform parent, bool stretch = false)
        {
            GameObject go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
            if (stretch) Stretch(go.GetComponent<RectTransform>());
            return go.transform;
        }

        private static void MakeImage(string name, Transform parent, Color color, bool stretch)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>(); image.color = color; image.raycastTarget = false;
            if (stretch) Stretch(go.GetComponent<RectTransform>());
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static TMP_Text MakeLabel(string name, Transform parent, string value, float size, Color color, TextAlignmentOptions alignment)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)); go.transform.SetParent(parent, false);
            TMP_Text text = go.GetComponent<TMP_Text>(); text.font = Font; text.text = value; text.fontSize = size; text.color = color;
            text.alignment = alignment; text.textWrappingMode = TextWrappingModes.Normal; text.raycastTarget = false;
            LayoutElement element = go.AddComponent<LayoutElement>(); element.minHeight = Mathf.Max(30, size * 1.8f); element.flexibleWidth = 1;
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(0, element.minHeight);
            if (parent.GetComponent<Button>() != null) Stretch(text.rectTransform);
            return text;
        }

        private static void AddHeading(Transform parent, string value, float size) => MakeLabel("Heading", parent, value, size, Accent, TextAlignmentOptions.Center).fontStyle = FontStyles.Bold;
        private static void AddSubheading(Transform parent, string value, float size) => MakeLabel("Subheading", parent, value, size, new Color(.7f, .76f, .85f), TextAlignmentOptions.Center);
        private static void AddBody(Transform parent, string value) => MakeLabel("BodyText", parent, value, 21, Color.white, TextAlignmentOptions.Center);
        private static void AddBack(Transform parent) => AddButton(parent, "BackButton", "ZURÜCK");

        private static Button AddButton(Transform parent, string name, string value)
        {
            GameObject go = MakeButton(name, parent, value, 58);
            return go.GetComponent<Button>();
        }

        private static GameObject MakeButton(string name, Transform parent, string value, float height)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement), typeof(MenuButtonEffect));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(.19f, .21f, .34f, .98f);
            go.GetComponent<LayoutElement>().minHeight = height;
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(0, height);
            go.GetComponent<Button>().targetGraphic = go.GetComponent<Image>();
            MakeLabel("Label", go.transform, value, 20, Color.white, TextAlignmentOptions.Center);
            return go;
        }

        private static void AddSmallButton(Transform parent, string name, string value) => MakeButton(name, parent, value, 48);

        private static void AddSlider(Transform parent, string name, string label)
        {
            AddSubheading(parent, label, 18);
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Slider), typeof(LayoutElement)); go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().minHeight = 28;
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 28);
            Slider slider = go.GetComponent<Slider>(); slider.minValue = 0; slider.maxValue = 100; slider.wholeNumbers = false;
            GameObject bg = new GameObject("Background", typeof(RectTransform), typeof(Image)); bg.transform.SetParent(go.transform, false); Stretch(bg.GetComponent<RectTransform>()); bg.GetComponent<Image>().color = new Color(.18f, .2f, .3f);
            GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform)); fillArea.transform.SetParent(go.transform, false);
            RectTransform fillRect = fillArea.GetComponent<RectTransform>(); fillRect.anchorMin = new Vector2(0, .25f); fillRect.anchorMax = new Vector2(1, .75f); fillRect.offsetMin = new Vector2(8, 0); fillRect.offsetMax = new Vector2(-8, 0);
            GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(Image)); fill.transform.SetParent(fillArea.transform, false); Stretch(fill.GetComponent<RectTransform>()); fill.GetComponent<Image>().color = Accent;
            GameObject handleArea = new GameObject("Handle Slide Area", typeof(RectTransform)); handleArea.transform.SetParent(go.transform, false); Stretch(handleArea.GetComponent<RectTransform>());
            GameObject handle = new GameObject("Handle", typeof(RectTransform), typeof(Image)); handle.transform.SetParent(handleArea.transform, false);
            RectTransform handleRect = handle.GetComponent<RectTransform>(); handleRect.sizeDelta = new Vector2(20, 30); handle.GetComponent<Image>().color = Color.white;
            slider.fillRect = fill.GetComponent<RectTransform>(); slider.handleRect = handleRect; slider.targetGraphic = handle.GetComponent<Image>();
        }

        private static Transform MakeScrollContent(Transform parent, string name)
        {
            GameObject viewport = new GameObject(name + "Viewport", typeof(RectTransform), typeof(Image), typeof(Mask)); viewport.transform.SetParent(parent, false);
            viewport.GetComponent<RectTransform>().sizeDelta = new Vector2(900, 450);
            LayoutElement viewportLayout = viewport.AddComponent<LayoutElement>(); viewportLayout.minHeight = 430; viewportLayout.flexibleHeight = 1; viewportLayout.flexibleWidth = 1;
            viewport.GetComponent<Image>().color = new Color(.04f, .04f, .08f, .01f); viewport.GetComponent<Mask>().showMaskGraphic = false;
            GameObject content = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter)); content.transform.SetParent(viewport.transform, false);
            RectTransform contentRect = content.GetComponent<RectTransform>(); contentRect.anchorMin = new Vector2(0, 1); contentRect.anchorMax = Vector2.one; contentRect.pivot = new Vector2(.5f, 1); contentRect.sizeDelta = Vector2.zero;
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 18; layout.childControlWidth = true; layout.childControlHeight = true;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            ScrollRect scroll = viewport.AddComponent<ScrollRect>(); scroll.viewport = viewport.GetComponent<RectTransform>(); scroll.content = contentRect; scroll.horizontal = false;
            return content.transform;
        }

        private static void AddInfo(Transform parent, string heading, string body)
        {
            MakeLabel(heading, parent, heading, 22, Accent, TextAlignmentOptions.Left).fontStyle = FontStyles.Bold;
            MakeLabel(heading + "Text", parent, body, 18, Color.white, TextAlignmentOptions.TopLeft);
        }

        private static void AddDecorationNote(Transform parent, string name, string glyph, Vector2 anchor)
        {
            GameObject noteObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            noteObject.transform.SetParent(parent, false);
            noteObject.GetComponent<Image>().color = Color.white;
            noteObject.GetComponent<Image>().enabled = false;
            TMP_Text note = MakeLabel("PlaceholderGlyph", noteObject.transform, glyph, 56, new Color(.32f, .57f, .74f, .5f), TextAlignmentOptions.Center);
            RectTransform rect = noteObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = anchor; rect.sizeDelta = new Vector2(90, 90); rect.anchoredPosition = Vector2.zero;
            note.rectTransform.anchorMin = Vector2.zero; note.rectTransform.anchorMax = Vector2.one; note.rectTransform.offsetMin = note.rectTransform.offsetMax = Vector2.zero;
        }
    }
}
