using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RhythmWitchClone.Core
{
    [Serializable]
    public class AchievementDefinition
    {
        public string id;
        public string title;
        public string description;
        public int target;
        public Sprite icon;
    }

    [Serializable]
    public class AchievementProgress
    {
        public string id;
        public int progress;
        public bool unlocked;
    }

    [Serializable]
    internal class AchievementSaveFile
    {
        public int enemiesDefeated;
        public int highestWave;
        public List<AchievementProgress> achievements = new List<AchievementProgress>();
    }

    /// <summary>Global erweiterbares Achievement-System; Aufrufe: RecordEnemyDefeated / RecordWaveSurvived.</summary>
    public sealed class AchievementManager : MonoBehaviour
    {
        public static AchievementManager Instance { get; private set; }
        public static event Action<AchievementDefinition> AchievementUnlocked;

        private const string FileName = "rhythmancy_achievements.json";
        [SerializeField] private List<AchievementDefinition> definitions = new List<AchievementDefinition>
        {
            new AchievementDefinition { id = "first_note", title = "Erster Ton", description = "Besiege deinen ersten Gegner.", target = 1 },
            new AchievementDefinition { id = "survivor_5", title = "Überlebenskünstler", description = "Überlebe 5 Wellen.", target = 5 },
            new AchievementDefinition { id = "fighter_100", title = "Musikalischer Kämpfer", description = "Besiege insgesamt 100 Gegner.", target = 100 },
            new AchievementDefinition { id = "unstoppable_10", title = "Unaufhaltsam", description = "Überlebe 10 Wellen.", target = 10 }
        };

        private AchievementSaveFile data = new AchievementSaveFile();
        private string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CreateInstance()
        {
            if (Instance != null) return;
            var host = new GameObject("AchievementManager");
            host.AddComponent<AchievementManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            var catalog = Resources.Load<AchievementCatalog>("RhythmancyAchievements");
            if (catalog != null) definitions = catalog.definitions;
            Load();
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        public IReadOnlyList<AchievementDefinition> Definitions => definitions;

        public AchievementProgress GetProgress(string id)
        {
            data.achievements.RemoveAll(item => item == null);
            AchievementProgress progress = data.achievements.Find(item => item.id == id);
            if (progress != null) return progress;
            progress = new AchievementProgress { id = id };
            data.achievements.Add(progress);
            return progress;
        }

        public int GetProgressValue(AchievementDefinition definition)
        {
            if (definition.id == "first_note" || definition.id == "fighter_100") return data.enemiesDefeated;
            if (definition.id == "survivor_5" || definition.id == "unstoppable_10") return data.highestWave;
            return GetProgress(definition.id).progress;
        }

        public void RecordEnemyDefeated()
        {
            data.enemiesDefeated++;
            Check("first_note"); Check("fighter_100");
            Save();
        }

        public void RecordWaveSurvived(int wave)
        {
            data.highestWave = Mathf.Max(data.highestWave, wave);
            Check("survivor_5"); Check("unstoppable_10");
            Save();
        }

        /// <summary>Universelle Schnittstelle für spätere, datengetriebene Erfolge.</summary>
        public void AddProgress(string achievementId, int amount = 1)
        {
            AchievementProgress progress = GetProgress(achievementId);
            progress.progress = Mathf.Max(0, progress.progress + amount);
            Check(achievementId);
            Save();
        }

        private void Check(string id)
        {
            AchievementDefinition definition = definitions.Find(item => item.id == id);
            if (definition == null) return;
            AchievementProgress progress = GetProgress(id);
            if (progress.unlocked || GetProgressValue(definition) < definition.target) return;
            progress.unlocked = true;
            AchievementUnlocked?.Invoke(definition);
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                AchievementSaveFile loaded = JsonUtility.FromJson<AchievementSaveFile>(File.ReadAllText(FilePath));
                if (loaded != null) data = loaded;
                data.achievements ??= new List<AchievementProgress>();
            }
            catch (Exception exception) { Debug.LogWarning($"Achievements konnten nicht geladen werden: {exception.Message}"); }
        }

        private void Save()
        {
            try
            {
                string temporary = FilePath + ".tmp";
                File.WriteAllText(temporary, JsonUtility.ToJson(data, true));
                if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null);
                else File.Move(temporary, FilePath);
            }
            catch (Exception exception) { Debug.LogWarning($"Achievements konnten nicht gespeichert werden: {exception.Message}"); }
        }
    }
}
