using RhythmWitchClone.Core;
using RhythmWitchClone.Gameplay;
using RhythmWitchClone.Levels;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RhythmWitchClone.Save
{
    /// <summary>Shared menu operations. Captures the current run, never data from a destination slot.</summary>
    public static class MenuSaveService
    {
        public static string LastError { get; private set; }

        public static bool SaveCurrent(int slot)
        {
            LastError = null;
            var data = new SaveData { exists = true, sceneName = SceneManager.GetActiveScene().name };
            var player = Object.FindFirstObjectByType<PlayerController>();
            if (player == null) { LastError = "Kein aktiver Spieler zum Speichern vorhanden."; return false; }
            player.WriteToSaveData(data);
            GameSession.WriteToSaveData(data);
            var levels = LevelManager.Instance;
            if (levels != null && levels.CurrentChapterIndex >= 0 &&
                levels.Chapters[levels.CurrentChapterIndex].sceneName == data.sceneName)
            {
                data.chapterIndex = levels.CurrentChapterIndex;
                data.levelIndex = levels.CurrentLevelIndex;
            }
            if (!SaveSystem.Save(slot, data)) { LastError = SaveSystem.LastError; return false; }
            GameSession.SelectSaveSlot(slot);
            return true;
        }

        public static bool Load(int slot, string legacyScene = "SafeZone")
        {
            LastError = null;
            var data = SaveSystem.Load(slot);
            if (!data.exists) { LastError = "Dieser Speicherplatz ist leer oder fehlerhaft."; return false; }
            string scene = string.IsNullOrEmpty(data.sceneName) ? legacyScene : data.sceneName;
            if (scene == "Menü" || scene == "MainMenu" || scene == "PauseMenu" || !Application.CanStreamedLevelBeLoaded(scene))
            { LastError = "Die gespeicherte Spielszene ist nicht in den Build-Einstellungen verfügbar: " + scene; return false; }
            GameSession.SelectSaveSlot(slot);
            GameSession.LoadFromSaveData(data);
            LevelManager.Instance?.RestoreContext(data.chapterIndex, data.levelIndex);
            Time.timeScale = 1f;
            SceneManager.LoadScene(scene);
            return true;
        }
    }
}
