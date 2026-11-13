using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using RhythmWitchClone.Gameplay;
using RhythmWitchClone.Core;
using RhythmWitchClone.Save;
using RhythmWitchClone.Audio;
using Object = UnityEngine.Object;

namespace RhythmWitchClone.EditorTools
{
    [InitializeOnLoad]
    public static class MainCharacterRuntimeChecks
    {
        const string Pending = "Rhythmancy.CharacterRuntimeChecks";
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        static double started;
        static MainCharacterRuntimeChecks()
        {
            started = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
        }
        // Batch-only, intentionally no -quit: this test exits after Play Mode checks.
        public static void Run()
        {
            if (!Application.isBatchMode) throw new Exception("Use batch mode for isolated runtime checks");
            EditorSceneManager.OpenScene("Assets/Scenes/Tutorial Level.unity");
            SessionState.SetBool(Pending, true);
            started = EditorApplication.timeSinceStartup;
            EditorApplication.isPlaying = true;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void IsolateSaves()
        {
            if (!SessionState.GetBool(Pending, false)) return;
            SaveSystem.EditorTestDirectory = Path.Combine(Path.GetTempPath(), "RhythmancyCharacterTests-" + Guid.NewGuid().ToString("N"));
            GameSession.StartNewSaveSlot(0);
        }
        static void Tick()
        {
            if (!SessionState.GetBool(Pending, false)) return;
            if (EditorApplication.timeSinceStartup - started > 90) { Finish(new Exception("Play Mode test timed out")); return; }
            if (!EditorApplication.isPlaying || Time.frameCount < 5) return;
            try { CheckRuntime(); Finish(null); }
            catch (Exception error) { Finish(error); }
        }
        static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, Fields).GetValue(target);
        static void Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Fields).Invoke(target, args);
        static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        static void CheckRuntime()
        {
            Check(!string.IsNullOrEmpty(SaveSystem.EditorTestDirectory) &&
                Path.GetFileName(SaveSystem.EditorTestDirectory).StartsWith("RhythmancyCharacterTests-"), "Save isolation was not initialized");
            var player = Object.FindFirstObjectByType<PlayerController>();
            var character = player.GetComponent<MainCharacter>();
            var cards = player.GetComponent<CardAbilityController>();
            Check(player.MaxHealth == character.baseStats.maxHealth, "Base HP changed");
            Check(cards.MaxMana == character.baseStats.maxMana, "Base mana changed");
            Check(Mathf.Abs(Get<float>(player, "moveSpeed") - character.baseStats.moveSpeed) < .001f, "Base speed changed");
            Call(cards, "ToggleCard", 1);
            Check(Get<int>(player, "_auraDamageBonus") == 15 && Get<float>(player, "_auraRadiusBonus") == 1.5f, "Starter ATK changed");
            Call(cards, "ToggleCard", 0);
            Check(Get<int>(player, "_auraDamageBonus") == 0 && Mathf.Abs(Get<float>(player, "_damageReductionPercent") - .3f) < .001f, "Starter DEF changed");
            player.TakeDamage(10);
            Check(player.CurrentHealth == player.MaxHealth - 7, "DEF reduction not applied");
            Call(cards, "ToggleCard", 0);
            Check(Get<float>(player, "_damageReductionPercent") == 0, "DEF bonus leaked after deselection");
            GameSession.AddMusicMedallions(5);
            Check(character.TryImproveStat(CharacterStat.MaxHealth, 10, 2), "Runtime stat purchase failed");
            int hp = player.CurrentHealth;
            character.RefreshStats();
            Check(player.MaxHealth == character.baseStats.maxHealth + 10 && player.CurrentHealth == hp, "Refresh stacked stats or healed player");

            var manager = AudioManager.Instance;
            if (manager == null) manager = new GameObject("Test AudioManager").AddComponent<AudioManager>();
            var background = AudioClip.Create("Test Background", 44100, 1, 44100, false);
            var song = AudioClip.Create("Test Sheet Song", 44100, 1, 44100, false);
            Call(manager, "PlaySingleLoopingTrack", background);
            var source = manager.GetComponent<AudioSource>();
            var sheet = ScriptableObject.CreateInstance<MusicSheetDefinition>();
            sheet.id = "runtime-test-atk"; sheet.sheetClass = MusicSheetClass.ATK;
            sheet.auraDamageBonus = 31; sheet.auraRadiusBonus = 2; sheet.damageReduction = .1f;
            sheet.goldPrice = 10; sheet.selectedMusic = song;
            var catalog = Object.Instantiate(character.musicSheets);
            var list = new System.Collections.Generic.List<MusicSheetDefinition>(catalog.sheets) { sheet };
            catalog.sheets = list.ToArray(); character.musicSheets = catalog;
            Check(!character.TryEquipSheet(sheet.id), "Unowned sheet equipped");
            GameSession.AddCoins(10);
            Check(GameSession.TryBuyMusicSheet(sheet) && character.TryEquipSheet(sheet.id), "New sheet purchase/equip failed");
            Call(cards, "ToggleCard", 1);
            Check(Get<int>(player, "_auraDamageBonus") == 31 && source.clip == song, "Asset boosts/music not applied");
            manager.PauseMusic();
            Call(cards, "ToggleCard", 1);
            Check(source.clip == background && manager.IsMusicPaused(), "Background music/pause not restored");
            manager.ResumeMusic();
            typeof(CardAbilityController).GetField("_currentMana", Fields).SetValue(cards, cards.MaxMana);
            Call(cards, "TriggerSpecialCard");
            Check(cards.CurrentMana == 0 && GameObject.Find("AuraRing") != null, "SPC failed");

            var data = new SaveData(); GameSession.WriteToSaveData(data); player.WriteToSaveData(data);
            Check(SaveSystem.Save(0, data), "Isolated save failed");
            var saved = SaveSystem.Load(0);
            Check(saved.exists && saved.musicMedallions == 3 && saved.equippedAtkSheet == sheet.id && saved.currentHealth == hp, "File roundtrip lost character data");
            Debug.Log("MAIN_CHARACTER_RUNTIME_OK: preserved stats, ATK/DEF damage, stat purchase, owned sheet selection, music/pause restoration, SPC and isolated file roundtrip.");
        }
        static void Finish(Exception error)
        {
            SessionState.SetBool(Pending, false);
            Directory.CreateDirectory("Captures");
            File.WriteAllText("Captures/character-tests.txt", error == null
                ? "PASS: Play Mode stats, starter ATK/DEF, damage reduction, medallion stat purchase, custom sheet purchase/equip/boosts/music, music pause restoration, SPC and isolated save-file roundtrip.\n"
                : "FAIL: " + error);
            if (error != null) Debug.LogException(error);
            EditorApplication.Exit(error == null ? 0 : 1);
        }
    }
}
