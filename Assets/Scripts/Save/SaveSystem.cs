using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using RhythmWitchClone.Gameplay;

namespace RhythmWitchClone.Save
{
    [Serializable]
    public class SaveData
    {
        public bool exists;
        public int version = 1;
        public string sceneName;
        public int chapterIndex = -1;
        public int levelIndex = -1;
        public int level = 1;
        public int currentWave = 1;
        public int enemiesDefeated;
        public int score;
        public int currentHealth;
        public float playtimeSeconds;
        public string lastPlayedIso;

        [Header("Spieler-Position")]
        public float playerPosX;
        public float playerPosY;
        public float playerPosZ;
        public bool playerFacingRight; // true = Spieler stand zuletzt nach rechts gedreht (Sprite gespiegelt)
        public bool hasPlayerPosition;

        [Header("Münzen + Upgrades (pro Speicherstand getrennt)")]
        public int coinCount; // Gold; keep field name for existing saves.
        public int musicMedallions;
        public List<CharacterStatBonus> characterStatBonuses = new List<CharacterStatBonus>();
        public List<string> ownedMusicSheetIds = new List<string>();
        public string equippedAtkSheet;
        public string equippedDefSheet;
        public string equippedSpcSheet;
        public int ballCountUpgradeLevel;
        public int projectileCountUpgradeLevel;
        public int projectileDamageUpgradeLevel;
        public int coinMagnetUpgradeLevel;

        [Header("Level-Fortschritt (pro Speicherstand getrennt)")]
        [Tooltip("Schlüssel im Format 'KapitelIndex_LevelIndex', ein Eintrag pro geschafftem Level.")]
        public List<string> completedLevelKeys = new List<string>();

        // TODO: Hier weitere eigene Spielstand-Felder ergänzen
        // (Fortschritt, Highscores, freigeschaltete Songs, Kapitel-Status, etc.)
    }

    /// <summary>
    /// Einfaches, dateibasiertes Speichersystem mit 3 festen Slots.
    /// Speichert JSON-Dateien in Application.persistentDataPath.
    /// </summary>
    public static class SaveSystem
    {
        private const int SlotCount = 3;
        public static string LastError { get; private set; }

#if UNITY_EDITOR
        // Explicitly isolated by the editor smoke tests; never present in player builds.
        public static string EditorTestDirectory { get; set; }
#endif
        private static string DirectoryPath
        {
            get
            {
#if UNITY_EDITOR
                if (!string.IsNullOrEmpty(EditorTestDirectory)) return EditorTestDirectory;
#endif
                return Application.persistentDataPath;
            }
        }
        private static string GetPath(int slot) => Path.Combine(DirectoryPath, $"save_{slot}.json");

        public static SaveData Load(int slot)
        {
            if (!IsValidSlot(slot)) return new SaveData { exists = false };

            string path = GetPath(slot);
            if (!File.Exists(path))
            {
                return new SaveData { exists = false };
            }

            try
            {
                string json = File.ReadAllText(path);
                SaveData data = JsonUtility.FromJson<SaveData>(json);
                if (data == null || !data.exists || !DateTime.TryParse(data.lastPlayedIso, out _) ||
                    data.level < 1 || data.currentWave < 1 || data.coinCount < 0 || data.version > 1 ||
                    float.IsNaN(data.playerPosX) || float.IsInfinity(data.playerPosX) ||
                    float.IsNaN(data.playerPosY) || float.IsInfinity(data.playerPosY) ||
                    float.IsNaN(data.playerPosZ) || float.IsInfinity(data.playerPosZ))
                    return new SaveData { exists = false };
                data.completedLevelKeys ??= new List<string>();
                data.exists = true;
                return data;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"SaveSystem: Speicherplatz {slot + 1} konnte nicht gelesen werden: {exception.Message}");
                return new SaveData { exists = false };
            }
        }

        public static bool Save(int slot, SaveData data)
        {
            if (!IsValidSlot(slot)) throw new ArgumentOutOfRangeException(nameof(slot), "Speicherplatz muss zwischen 0 und 2 liegen.");
            if (data == null) throw new ArgumentNullException(nameof(data));

            LastError = null;
            data.exists = true;
            data.lastPlayedIso = DateTime.UtcNow.ToString("o");
            string json = JsonUtility.ToJson(data, true);
            string path = GetPath(slot);
            string temporaryPath = path + ".tmp";
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                File.WriteAllText(temporaryPath, json);
                if (File.Exists(path)) File.Replace(temporaryPath, path, null);
                else File.Move(temporaryPath, path);
                return true;
            }
            catch (Exception exception)
            {
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { /* Die ursprüngliche Datei bleibt unangetastet. */ }
                LastError = "Speichern fehlgeschlagen: " + exception.Message;
                Debug.LogWarning(LastError);
                return false;
            }
        }

        public static bool DeleteSlot(int slot)
        {
            LastError = null;
            if (!IsValidSlot(slot)) return false;
            string path = GetPath(slot);
            try { if (File.Exists(path)) File.Delete(path); return true; }
            catch (Exception exception) { LastError = "Löschen fehlgeschlagen: " + exception.Message; Debug.LogWarning(LastError); return false; }
        }

        public static int GetSlotCount() => SlotCount;
        public static bool IsValidSlot(int slot) => slot >= 0 && slot < SlotCount;
        public static bool SlotFileExists(int slot) => IsValidSlot(slot) && File.Exists(GetPath(slot));

        public static int GetMostRecentOccupiedSlot()
        {
            int selected = -1;
            DateTime latest = DateTime.MinValue;
            int fallback = -1;
            for (int i = 0; i < SlotCount; i++)
            {
                SaveData data = Load(i);
                if (!data.exists) continue;
                if (fallback < 0) fallback = i;
                if (!DateTime.TryParse(data.lastPlayedIso, out DateTime savedAt)) continue;
                if (savedAt > latest) { latest = savedAt; selected = i; }
            }
            return selected >= 0 ? selected : fallback;
        }
    }
}
