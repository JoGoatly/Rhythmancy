using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RhythmWitchClone.Core;
using RhythmWitchClone.Gameplay;
using RhythmWitchClone.Save;
using RhythmWitchClone.UI.MainMenu;
using RhythmWitchClone.UI.Pause;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RhythmWitchClone.EditorTools
{
    /// <summary>Real button/scene smoke test with isolated save files and restored preferences.</summary>
    public static class RhythmancyMenuSmokeTests
    {
        static IEnumerator routine;
        static double next;
        static readonly List<string> results = new List<string>();
        static readonly Dictionary<string, (bool exists, string value)> bindings = new Dictionary<string, (bool, string)>();
        static readonly Dictionary<string, (bool exists, float value)> volumes = new Dictionary<string, (bool, float)>();
        public static string LastResult { get; private set; } = "Not run";

        [MenuItem("Tools/Rhythmancy/Run Menu Smoke Tests (Play Mode)")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying || !UnityEngine.Object.FindFirstObjectByType<RhythmancyMenuController>())
            { Debug.LogWarning("Menü.unity im Play-Modus öffnen, dann Tests ausführen."); return; }
            if (routine != null) return;
            results.Clear(); bindings.Clear(); volumes.Clear();
            foreach (var binding in RhythmancySettings.GetBindings())
            {
                string key = "rhythmancy_binding_" + binding.Key;
                bindings[key] = (PlayerPrefs.HasKey(key), PlayerPrefs.GetString(key));
            }
            foreach (var key in new[] { "rhythmancy_master_volume", "rhythmancy_music_volume", "rhythmancy_sfx_volume" })
                volumes[key] = (PlayerPrefs.HasKey(key), PlayerPrefs.GetFloat(key));
            SaveSystem.EditorTestDirectory = Path.Combine(Path.GetTempPath(), "RhythmancyMenuTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(SaveSystem.EditorTestDirectory);
            LastResult = "Running";
            routine = Steps(); next = 0;
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < next) return;
            try
            {
                if (!EditorApplication.isPlaying) throw new Exception("Play mode stopped during test");
                // Wait for actual UI frames as well as wall time (editor rendering can stall during imports).
                if (Root != null && Root.Find("TransitionOverlay") != null && Root.Find("TransitionOverlay").gameObject.activeSelf && EditorApplication.timeSinceStartup < next + 10) return;
                if (!routine.MoveNext()) { Finish(null); return; }
                next = EditorApplication.timeSinceStartup + (routine.Current is float delay ? delay : .5f);
            }
            catch (Exception exception) { Finish(exception.ToString()); }
        }

        static void Finish(string error)
        {
            EditorApplication.update -= Tick; routine = null;
            foreach (var pair in bindings)
                if (pair.Value.exists) PlayerPrefs.SetString(pair.Key, pair.Value.value); else PlayerPrefs.DeleteKey(pair.Key);
            foreach (var pair in volumes)
                if (pair.Value.exists) PlayerPrefs.SetFloat(pair.Key, pair.Value.value); else PlayerPrefs.DeleteKey(pair.Key);
            PlayerPrefs.Save(); RhythmancySettings.ApplyAudio();
            SaveSystem.EditorTestDirectory = null;
            Time.timeScale = 1;
            if (EditorApplication.isPlaying) SceneManager.LoadScene("Menü");
            LastResult = (error == null ? "PASS" : "FAIL: " + error) + "\n" + string.Join("\n", results);
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "../Captures"));
            File.WriteAllText(Path.Combine(Application.dataPath, "../Captures/menu-tests.txt"), LastResult);
            if (error == null) Debug.Log(LastResult); else Debug.LogError(LastResult);
        }

        static Transform Root => UnityEngine.Object.FindFirstObjectByType<AdditivePauseMenuController>()?.transform ?? UnityEngine.Object.FindFirstObjectByType<RhythmancyMenuController>()?.transform;
        static Button Button(string name) => Root.GetComponentsInChildren<Button>(true).First(b => b.name == name && b.gameObject.activeInHierarchy);
        static void Click(string name) { var button = Button(name); Assert(button.interactable, name + " interactable"); button.onClick.Invoke(); }
        static void Assert(bool condition, string description) { if (!condition) throw new Exception(description); }
        static void Pass(string description) => results.Add(description);
        static void Panel(string name)
        {
            Assert(Root.Find(name).gameObject.activeSelf, name + " visible");
            var panels = new[] { "MainMenuPanel", "NewGamePanel", "SettingsPanel", "SaveLoadPanel", "AchievementsPanel", "HelpPanel", "CreditsPanel", "ConfirmationDialog", "PauseRootPanel" };
            Assert(panels.Count(p => Root.Find(p) != null && Root.Find(p).gameObject.activeSelf) == 1, "Exactly one panel visible");
            Assert(!Root.Find("TransitionOverlay").gameObject.activeSelf, "Fade finished while paused or running");
        }
        static void Escape(bool pressed) => InputSystem.QueueStateEvent(Keyboard.current, pressed ? new KeyboardState(Key.Escape) : new KeyboardState());

        static IEnumerator Steps()
        {
            Click("LoadButton"); yield return .5f;
            Panel("SaveLoadPanel");
            Assert(!Button("LoadSlot0").interactable, "Empty slot disabled");
            Click("BackButton"); yield return .5f;
            Assert(!Button("ContinueButton").interactable, "Continue disabled with empty slots");
            Pass("Empty slots and Continue disabled");
            foreach (var item in new[] { ("SettingsButton", "SettingsPanel"), ("AchievementsButton", "AchievementsPanel"), ("HelpButton", "HelpPanel"), ("CreditsButton", "CreditsPanel") })
            {
                Click(item.Item1); yield return .5f; Panel(item.Item2);
                if (item.Item2 == "SettingsPanel")
                {
                    var slider = Root.GetComponentsInChildren<Slider>().First(s => s.name == "MusicVolumeSlider");
                    slider.value = 37;
                    Assert(Mathf.Abs(RhythmancySettings.MusicVolume - .37f) < .01f, "Music setting persisted");
                    Click("Rebind_Links"); yield return .2f;
                    InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.B)); yield return .2f;
                    InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
                    Assert(RhythmancySettings.GetKeyForAction("Links") == Key.B, "Rebinding stored");
                    Click("SettingsResetButton");
                    Assert(RhythmancySettings.GetKeyForAction("Links") == Key.A && RhythmancySettings.MusicVolume == 1, "Reset defaults");
                }
                if (item.Item2 == "HelpPanel")
                {
                    var body = Root.GetComponentsInChildren<TMP_Text>().First(t => t.name == "SPIELZIELText");
                    Assert(body.rectTransform.rect.width > 400, "Help body has readable layout width");
                }
                if (item.Item2 == "AchievementsPanel")
                    Assert(Root.GetComponentsInChildren<TMP_Text>().First(t => t.name == "Achievement_0").rectTransform.rect.width > 300, "Achievement row has readable width");
                Click("BackButton"); yield return .5f; Panel("MainMenuPanel");
                Pass(item.Item2 + " opens/back/fade");
            }
            // A malformed file is visible for management but cannot be loaded.
            File.WriteAllText(Path.Combine(SaveSystem.EditorTestDirectory, "save_1.json"), "{}");
            Assert(!SaveSystem.Load(1).exists, "Malformed schema rejected");
            Click("LoadButton"); yield return .5f;
            Assert(!Button("LoadSlot1").interactable && Button("DeleteSlot1").interactable, "Corrupt slot manageable");
            Click("DeleteSlot1"); yield return .5f; Panel("ConfirmationDialog");
            Click("ConfirmNoButton"); yield return .5f; Assert(SaveSystem.SlotFileExists(1), "Cancel preserves file");
            Click("DeleteSlot1"); yield return .5f; Click("ConfirmYesButton"); yield return .5f;
            Assert(!SaveSystem.SlotFileExists(1), "Delete confirmed");
            Click("BackButton"); yield return .5f;
            Pass("Malformed file, delete/cancel/confirm");
            Click("PlayButton"); yield return .5f; Panel("NewGamePanel");
            Click("NewGameSlot0"); yield return 1.5f;
            Assert(SceneManager.GetActiveScene().name == "Tutorial Level", "Actual Tutorial Level scene starts");
            Assert(SaveSystem.Load(0).exists, "New save created");
            var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
            Assert(player != null, "Gameplay player exists");
            int id = player.GetInstanceID(); var position = player.transform.position;
            Escape(true); yield return .6f; Escape(false); yield return .3f;
            Panel("PauseRootPanel");
            Assert(Time.timeScale == 0, "Pause stops time");
            Assert(Button("ResumeButton").GetComponent<RectTransform>().rect.width > 400, "Pause buttons have readable width");
            Assert(!Root.GetComponentsInChildren<Button>(true).Any(b => b.name == "PlayButton"), "Pause has no Play");
            Click("SettingsButton"); yield return .5f; Panel("SettingsPanel");
            Click("BackButton"); yield return .5f;
            Click("ResumeButton"); yield return .5f;
            Assert(Time.timeScale == 1 && player != null && player.GetInstanceID() == id, "Resume keeps same player/scene");
            Pass("New game/Tutorial Level, Escape pause, settings fade at timeScale 0, Resume preserves instance");
            Escape(true); yield return .6f; Escape(false); yield return .3f;
            Click("SaveButton"); yield return .5f;
            Click("PauseSaveSlot1"); yield return .5f;
            Click("PauseSaveSlot2"); yield return .5f;
            Assert(SaveSystem.Load(1).exists && SaveSystem.Load(2).exists, "All three slots saved");
            Assert(SaveSystem.Load(2).sceneName == "Tutorial Level", "Scene persisted");
            Assert(SaveSystem.Load(2).currentHealth == player.CurrentHealth, "Health persisted");
            Click("PauseSaveSlot0"); yield return .5f; Panel("ConfirmationDialog");
            Click("ConfirmNoButton"); yield return .5f;
            Click("PauseLoadSlot2"); yield return .5f; Panel("ConfirmationDialog");
            Click("ConfirmYesButton"); yield return 1.5f;
            Assert(SceneManager.GetActiveScene().name == "Tutorial Level" && Time.timeScale == 1, "Saved scene loads unpaused");
            Pass("Three slots, overwrite cancellation, load confirmation, scene/health persisted");
            // Roundtrip via main menu Continue, plus overwrite confirmation on New Game.
            SceneManager.LoadScene("Menü"); yield return 1f;
            Assert(Button("ContinueButton").interactable, "Continue enabled with saves");
            Click("PlayButton"); yield return .5f; Click("NewGameSlot0"); yield return .5f;
            Panel("ConfirmationDialog"); Click("ConfirmNoButton"); yield return .5f; Click("BackButton"); yield return .5f;
            Click("ContinueButton"); yield return 1f;
            Assert(SceneManager.GetActiveScene().name == "Tutorial Level", "Continue loads most recent scene");
            Pass("New-game overwrite protection and Continue roundtrip");
        }
    }
}
