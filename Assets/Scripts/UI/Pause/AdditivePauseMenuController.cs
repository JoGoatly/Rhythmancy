using System;
using System.Collections;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using RhythmWitchClone.Audio;
using RhythmWitchClone.Core;
using RhythmWitchClone.Gameplay;
using RhythmWitchClone.Save;
using RhythmWitchClone.UI;

namespace RhythmWitchClone.UI.Pause
{
    /// <summary>Controller für die eigenständige, additiv geladene PauseMenu-Szene.</summary>
    public sealed class AdditivePauseMenuController : MonoBehaviour
    {
        private GameObject _root;
        private GameObject _settings;
        private GameObject _saveLoad;
        private GameObject _confirm;
        private string _returnPanel = "PauseRootPanel";
        private Action _confirmAction;
        private bool _leaving;
        private bool _transitioning;
        private bool _quitting;
        private string _pendingRebind;

        private void Awake()
        {
            Time.timeScale = 0f;
            if (EventSystem.current == null)
            {
                GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                SceneManager.MoveGameObjectToScene(eventSystem, gameObject.scene);
            }
            if (transform.Find("PauseRootPanel") == null) BuildUi();
            _root = transform.Find("PauseRootPanel")?.gameObject;
            _settings = transform.Find("SettingsPanel")?.gameObject;
            _saveLoad = transform.Find("SaveLoadPanel")?.gameObject;
            _confirm = transform.Find("ConfirmationDialog")?.gameObject;
            Wire();
        }

        // Single-scene transitions must never leave time frozen after the overlay is destroyed.
        private void OnDestroy() => Time.timeScale = 1f;

        private void Update()
        {
            if (_transitioning) return;
            if (_pendingRebind != null && Keyboard.current != null)
            {
                if (Keyboard.current.escapeKey.wasPressedThisFrame) { _pendingRebind = null; RefreshRebindLabels(); return; }
                if (Keyboard.current.anyKey.wasPressedThisFrame)
                {
                    foreach (KeyControl control in Keyboard.current.allKeys)
                    {
                        if (!control.wasPressedThisFrame || control.keyCode == Key.None || control.keyCode == Key.Escape) continue;
                        RhythmancySettings.SetBinding(_pendingRebind, control.keyCode.ToString());
                        _pendingRebind = null; RefreshRebindLabels(); break;
                    }
                }
                return;
            }
            Key pauseKey = RhythmancySettings.GetKeyForAction("Pause");
            if (Keyboard.current != null && (Keyboard.current.escapeKey.wasPressedThisFrame || (pauseKey != Key.None && Keyboard.current[pauseKey].wasPressedThisFrame)))
            {
                if (_settings != null && _settings.activeSelf) ShowRoot();
                else if (_saveLoad != null && _saveLoad.activeSelf) ShowRoot();
                else if (_confirm != null && _confirm.activeSelf) CancelConfirm();
                else Resume();
            }
        }

        private void Wire()
        {
            Listen("ResumeButton", Resume);
            Listen("SaveButton", () => { RefreshSaveSlots(); Show(_saveLoad); });
            Listen("LoadButton", () => { RefreshSaveSlots(); Show(_saveLoad); });
            Listen("SettingsButton", () => Show(_settings));
            Listen("MainMenuButton", ConfirmLeaveToMenu);
            Listen("QuitButton", ConfirmQuit);
            Listen("ConfirmYesButton", () => { Action action = _confirmAction; _confirmAction = null; ShowByName(_returnPanel); action?.Invoke(); });
            Listen("ConfirmNoButton", CancelConfirm);
            Listen("LeaveWithoutSaveButton", () => { _confirmAction = null; ShowByName(_returnPanel); if (_quitting) QuitGame(); else ReturnToMenu(); });
            Listen("BackButton", ShowRoot);
            Listen("SettingsResetButton", ResetSettings);

            for (int slot = 0; slot < SaveSystem.GetSlotCount(); slot++)
            {
                int index = slot;
                Listen($"PauseSaveSlot{slot}", () => SaveSlot(index));
                Listen($"PauseLoadSlot{slot}", () => ShowConfirm("Spielstand laden? Nicht gespeicherter Fortschritt geht verloren.", "SaveLoadPanel", () => LoadSlot(index)));
                Listen($"PauseDeleteSlot{slot}", () => ShowConfirm("Speicherplatz " + (index + 1) + " wirklich löschen?", "SaveLoadPanel", () =>
                {
                    if (!SaveSystem.DeleteSlot(index)) MenuStatus.Show(this, SaveSystem.LastError);
                    RefreshSaveSlots();
                }));
            }

            BindSlider("MasterVolumeSlider", RhythmancySettings.MasterVolume, RhythmancySettings.SetMasterVolume);
            BindSlider("MusicVolumeSlider", RhythmancySettings.MusicVolume, RhythmancySettings.SetMusicVolume);
            BindSlider("SfxVolumeSlider", RhythmancySettings.SfxVolume, RhythmancySettings.SetSfxVolume);
            foreach (string action in new[] { "Links", "Rechts", "Hoch", "Runter", "Fähigkeit 1", "Fähigkeit 2", "Spezialfähigkeit", "Interaktion", "Pause" })
            {
                string captured = action;
                Listen("Rebind_" + captured, () => { _pendingRebind = captured; RefreshRebindLabels(); });
            }
            RefreshRebindLabels();
        }

        private void Listen(string name, UnityEngine.Events.UnityAction action)
        {
            foreach (Button button in GetComponentsInChildren<Button>(true).Where(item => item.name == name))
                button.onClick.AddListener(() => { if (!_transitioning && !_leaving) action(); });
        }

        private void BindSlider(string name, float value, UnityEngine.Events.UnityAction<float> listener)
        {
            Slider slider = GetComponentsInChildren<Slider>(true).FirstOrDefault(item => item.name == name);
            if (slider == null) return;
            slider.SetValueWithoutNotify(value * 100f);
            slider.onValueChanged.AddListener(v => listener(v / 100f));
        }

        private void RefreshRebindLabels()
        {
            foreach (var binding in RhythmancySettings.GetBindings())
            {
                TMP_Text label = GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(item => item.name == "RebindLabel_" + binding.Key);
                if (label != null) label.text = _pendingRebind == binding.Key ? binding.Key + ": Taste drücken… (Esc = Abbrechen)" : binding.Key + ": " + binding.Value;
            }
        }

        private void ResetSettings()
        {
            _pendingRebind = null;
            RhythmancySettings.Reset();
            foreach (var item in new[] { ("MasterVolumeSlider", RhythmancySettings.MasterVolume), ("MusicVolumeSlider", RhythmancySettings.MusicVolume), ("SfxVolumeSlider", RhythmancySettings.SfxVolume) })
            {
                Slider slider = GetComponentsInChildren<Slider>(true).FirstOrDefault(value => value.name == item.Item1);
                if (slider != null) slider.SetValueWithoutNotify(item.Item2 * 100f);
            }
            RefreshRebindLabels();
        }

        private void Show(GameObject target)
        {
            if (target == null || _transitioning) return;
            StartCoroutine(FadeTo(target));
        }

        private IEnumerator FadeTo(GameObject target)
        {
            _transitioning = true;
            CanvasGroup fade = transform.Find("TransitionOverlay")?.GetComponent<CanvasGroup>();
            if (fade != null)
            {
                fade.gameObject.SetActive(true);
                for (float t = 0; t < .12f; t += Time.unscaledDeltaTime) { fade.alpha = t / .12f; yield return null; }
            }
            _root.SetActive(target == _root);
            _settings.SetActive(target == _settings);
            _saveLoad.SetActive(target == _saveLoad);
            _confirm.SetActive(target == _confirm);
            if (fade != null)
            {
                for (float t = .12f; t > 0; t -= Time.unscaledDeltaTime) { fade.alpha = t / .12f; yield return null; }
                fade.alpha = 0; fade.gameObject.SetActive(false);
            }
            _transitioning = false;
            var first = target.GetComponentsInChildren<Button>().FirstOrDefault(b => b.interactable);
            if (first != null) first.Select();
        }

        private void ShowRoot() { _pendingRebind = null; RefreshRebindLabels(); Show(_root); }
        private void ShowByName(string name) => Show(name == "SettingsPanel" ? _settings : name == "SaveLoadPanel" ? _saveLoad : _root);
        private void ShowConfirm(string message, string returnPanel, Action action)
        {
            TMP_Text text = transform.Find("ConfirmationDialog/ConfirmationMessage")?.GetComponent<TMP_Text>();
            if (text != null) text.text = message;
            _returnPanel = returnPanel;
            _confirmAction = action;
            Button noSave = FindButton("LeaveWithoutSaveButton");
            if (noSave != null) noSave.gameObject.SetActive(false);
            Show(_confirm);
        }
        private void CancelConfirm() { _confirmAction = null; ShowByName(_returnPanel); }

        private void ConfirmLeaveToMenu()
        {
            _quitting = false;
            ShowConfirm("Jetzt speichern und zum Hauptmenü zurückkehren?", "PauseRootPanel", () =>
            {
                if (SaveCurrent()) ReturnToMenu();
            });
            Button noSave = FindButton("LeaveWithoutSaveButton");
            if (noSave != null) noSave.gameObject.SetActive(true);
        }

        private void ConfirmQuit()
        {
            _quitting = true;
            ShowConfirm("Fortschritt speichern und Spiel beenden?", "PauseRootPanel", () => { if (SaveCurrent()) QuitGame(); });
            Button noSave = FindButton("LeaveWithoutSaveButton");
            if (noSave != null) noSave.gameObject.SetActive(true);
        }

        private void QuitGame()
        {
#if UNITY_EDITOR
            Debug.Log("Rhythmancy: Beenden angefordert (Editor bleibt geöffnet).");
#else
            Time.timeScale = 1f;
            Application.Quit();
#endif
        }

        private Button FindButton(string name) => GetComponentsInChildren<Button>(true).FirstOrDefault(item => item.name == name);

        private void SaveSlot(int slot)
        {
            SaveData current = SaveSystem.Load(slot);
            if (current.exists || SaveSystem.SlotFileExists(slot))
            {
                ShowConfirm($"Speicherplatz {slot + 1} überschreiben?", "SaveLoadPanel", () => { WriteSave(slot); RefreshSaveSlots(); Show(_saveLoad); });
                return;
            }
            WriteSave(slot); RefreshSaveSlots(); Show(_saveLoad);
        }

        private void LoadSlot(int slot)
        {
            if (!MenuSaveService.Load(slot)) MenuStatus.Show(this, MenuSaveService.LastError);
        }

        private void RefreshSaveSlots()
        {
            for (int i = 0; i < SaveSystem.GetSlotCount(); i++)
            {
                int slot = i;
                SaveData data = SaveSystem.Load(slot);
                TMP_Text label = transform.Find($"SaveLoadPanel/SlotRow{slot}/SlotLabel")?.GetComponent<TMP_Text>();
                bool fileExists = SaveSystem.SlotFileExists(slot);
                if (label != null) label.text = data.exists ? $"Platz {slot + 1} · {(DateTime.TryParse(data.lastPlayedIso, out var date) ? date.ToLocalTime().ToString("dd.MM.yy HH:mm") : "–")}\nWelle {Mathf.Max(1, data.currentWave)} · Level {data.level} · Münzen {data.coinCount}" : fileExists ? $"Speicherplatz {slot + 1} · DATEI FEHLERHAFT · Überschreiben bestätigen" : $"Speicherplatz {slot + 1} · LEER";
                Button load = GetComponentsInChildren<Button>(true).FirstOrDefault(item => item.name == $"PauseLoadSlot{slot}");
                if (load != null) load.interactable = data.exists;
                Button delete = FindButton($"PauseDeleteSlot{slot}");
                if (delete != null) delete.interactable = fileExists;
            }
        }

        private bool WriteSave(int slot)
        {
            bool saved = MenuSaveService.SaveCurrent(slot);
            MenuStatus.Show(this, saved ? "Spielstand gespeichert." : MenuSaveService.LastError);
            return saved;
        }

        private bool SaveCurrent()
        {
            if (GameSession.SelectedSaveSlot >= 0) return WriteSave(GameSession.SelectedSaveSlot);
            MenuStatus.Show(this, "Bitte zuerst unter Speichern einen Speicherplatz auswählen.");
            return false;
        }

        private void Resume()
        {
            if (_leaving) return;
            _leaving = true;
            Time.timeScale = 1f;
            SceneManager.UnloadSceneAsync("PauseMenu");
        }

        private void ReturnToMenu()
        {
            _leaving = true;
            Time.timeScale = 1f;
            SceneManager.LoadScene("Menü", LoadSceneMode.Single);
        }

        public void BuildUi()
        {
            if (transform.Find("PauseRootPanel") != null) return;
            PauseMenuUiBuilder.Build(transform);
        }
    }

    internal static class PauseMenuUiBuilder
    {
        private static readonly Color Accent = new Color(.42f, .82f, .92f, 1);
        private static TMP_FontAsset Font => TMP_Settings.defaultFontAsset;

        public static void Build(Transform root)
        {
            Canvas canvas = root.GetComponent<Canvas>() ?? root.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>() ?? root.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            if (root.GetComponent<GraphicRaycaster>() == null) root.gameObject.AddComponent<GraphicRaycaster>();
            MakePanel(root, "PauseRootPanel", true);
            AddText(root.Find("PauseRootPanel"), "Heading", "PAUSIERT", 48, Accent);
            AddButton(root.Find("PauseRootPanel"), "ResumeButton", "FORTSETZEN");
            AddButton(root.Find("PauseRootPanel"), "SaveButton", "SPEICHERN");
            AddButton(root.Find("PauseRootPanel"), "LoadButton", "SPIELSTAND LADEN");
            AddButton(root.Find("PauseRootPanel"), "SettingsButton", "EINSTELLUNGEN");
            AddButton(root.Find("PauseRootPanel"), "MainMenuButton", "ZUM HAUPTMENÜ");
            AddButton(root.Find("PauseRootPanel"), "QuitButton", "SPIEL BEENDEN");

            Transform settings = MakePanel(root, "SettingsPanel", false).transform;
            AddText(settings, "Heading", "EINSTELLUNGEN", 40, Accent);
            AddSlider(settings, "MasterVolumeSlider", "Gesamtlautstärke");
            AddSlider(settings, "MusicVolumeSlider", "Musiklautstärke");
            AddSlider(settings, "SfxVolumeSlider", "Soundeffekte");
            foreach (string action in new[] { "Links", "Rechts", "Hoch", "Runter", "Fähigkeit 1", "Fähigkeit 2", "Spezialfähigkeit", "Interaktion", "Pause" })
            {
                var button = new GameObject("Rebind_" + action, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement), typeof(MenuButtonEffect)).transform;
                button.SetParent(settings, false); button.GetComponent<Image>().color = new Color(.19f, .21f, .34f); button.GetComponent<LayoutElement>().minHeight = 40; button.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 40);
                button.GetComponent<Button>().targetGraphic = button.GetComponent<Image>();
                AddText(button, "RebindLabel_" + action, action, 17, Color.white);
            }
            var reset = new GameObject("SettingsResetButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement), typeof(MenuButtonEffect)).transform;
            reset.SetParent(settings, false); reset.GetComponent<Image>().color = new Color(.19f, .21f, .34f); reset.GetComponent<LayoutElement>().minHeight = 48; reset.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 48);
            reset.GetComponent<Button>().targetGraphic = reset.GetComponent<Image>(); AddText(reset, "Label", "EINSTELLUNGEN ZURÜCKSETZEN", 18, Color.white);
            AddButton(settings, "BackButton", "ZURÜCK");

            Transform saves = MakePanel(root, "SaveLoadPanel", false).transform;
            AddText(saves, "Heading", "SPIELSTÄNDE", 40, Accent);
            for (int i = 0; i < 3; i++)
            {
                Transform row = new GameObject($"SlotRow{i}", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement)).transform;
                row.SetParent(saves, false); row.GetComponent<LayoutElement>().minHeight = 55; row.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 55);
                TMP_Text label = AddText(row, "SlotLabel", $"Speicherplatz {i + 1}", 18, Color.white); label.alignment = TextAlignmentOptions.MidlineLeft;
                AddButton(row, $"PauseSaveSlot{i}", "SPEICHERN"); AddButton(row, $"PauseLoadSlot{i}", "LADEN"); AddButton(row, $"PauseDeleteSlot{i}", "LÖSCHEN");
            }
            AddButton(saves, "BackButton", "ZURÜCK");

            Transform confirm = MakePanel(root, "ConfirmationDialog", false).transform;
            AddText(confirm, "Heading", "BESTÄTIGEN", 40, Accent);
            AddText(confirm, "ConfirmationMessage", "Bist du sicher?", 21, Color.white);
            AddButton(confirm, "LeaveWithoutSaveButton", "OHNE SPEICHERN VERLASSEN");
            AddButton(confirm, "ConfirmYesButton", "JA"); AddButton(confirm, "ConfirmNoButton", "ABBRECHEN");

            GameObject overlay = new GameObject("TransitionOverlay", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            overlay.transform.SetParent(root, false); Stretch(overlay.GetComponent<RectTransform>()); overlay.GetComponent<Image>().color = Color.black; overlay.GetComponent<CanvasGroup>().alpha = 0; overlay.SetActive(false);
        }

        private static GameObject MakePanel(Transform parent, string name, bool active)
        {
            GameObject panel = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup)); panel.transform.SetParent(parent, false);
            RectTransform rect = panel.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.sizeDelta = new Vector2(900, name == "SettingsPanel" ? 1000 : 780);
            panel.GetComponent<Image>().color = new Color(.08f, .07f, .16f, .97f);
            VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>(); layout.padding = new RectOffset(28, 28, 28, 28); layout.spacing = name == "SettingsPanel" ? 4 : 12; layout.childAlignment = TextAnchor.MiddleCenter; layout.childControlWidth = true; layout.childControlHeight = true; layout.childForceExpandHeight = false;
            panel.SetActive(active); return panel;
        }
        private static TMP_Text AddText(Transform parent, string name, string value, float size, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement)); go.transform.SetParent(parent, false);
            TMP_Text text = go.GetComponent<TMP_Text>(); text.font = Font; text.text = value; text.fontSize = size; text.color = color; text.alignment = TextAlignmentOptions.Center;
            go.GetComponent<LayoutElement>().minHeight = Mathf.Max(40, size * 1.7f); go.GetComponent<LayoutElement>().flexibleWidth = 1;
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(0, go.GetComponent<LayoutElement>().minHeight);
            text.raycastTarget = false;
            if (parent.GetComponent<Button>() != null) Stretch(text.rectTransform);
            return text;
        }
        private static void AddButton(Transform parent, string name, string value)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement), typeof(MenuButtonEffect)); go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(.19f, .21f, .34f); go.GetComponent<LayoutElement>().minHeight = 52;
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 52);
            Button button = go.GetComponent<Button>(); button.targetGraphic = go.GetComponent<Image>();
            AddText(go.transform, "Label", value, 18, Color.white);
        }
        private static void AddSlider(Transform parent, string name, string label)
        {
            AddText(parent, name + "Label", label, 18, Color.white);
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Slider), typeof(LayoutElement)); go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().minHeight = 28; Slider slider = go.GetComponent<Slider>(); slider.minValue = 0; slider.maxValue = 100;
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 28);
            var bg = new GameObject("Background", typeof(RectTransform), typeof(Image)); bg.transform.SetParent(go.transform, false); Stretch(bg.GetComponent<RectTransform>()); bg.GetComponent<Image>().color = new Color(.18f, .2f, .3f);
            var fillArea = new GameObject("Fill Area", typeof(RectTransform)); fillArea.transform.SetParent(go.transform, false); Stretch(fillArea.GetComponent<RectTransform>());
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image)); fill.transform.SetParent(fillArea.transform, false); Stretch(fill.GetComponent<RectTransform>()); fill.GetComponent<Image>().color = Accent;
            var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform)); handleArea.transform.SetParent(go.transform, false); Stretch(handleArea.GetComponent<RectTransform>());
            var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image)); handle.transform.SetParent(handleArea.transform, false); handle.GetComponent<RectTransform>().sizeDelta = new Vector2(18, 28);
            slider.fillRect = fill.GetComponent<RectTransform>(); slider.handleRect = handle.GetComponent<RectTransform>(); slider.targetGraphic = handle.GetComponent<Image>();
        }
        private static void Stretch(RectTransform rect) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
    }
}
