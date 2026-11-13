using System.Collections.Generic;
using UnityEngine;
using RhythmWitchClone.Save;
using RhythmWitchClone.Levels;
using RhythmWitchClone.Gameplay;
using System.Linq;

namespace RhythmWitchClone.Core
{
    /// <summary>
    /// Statische Klasse, die einfache Daten über Szenenwechsel hinweg am Leben hält:
    /// welcher Speicherstand ausgewählt wurde, sowie Münzen + Upgrade-Stufen + Level-Fortschritt
    /// für GENAU diesen Speicherstand (werden beim Auswählen eines Slots aus SaveData geladen und
    /// beim Speichern wieder zurückgeschrieben - jeder Speicherstand hat also seinen eigenen
    /// Münz-/Upgrade-/Fortschritts-Stand, statt sich einen globalen Zustand zu teilen).
    /// Kein MonoBehaviour nötig - kein GameObject, keine DontDestroyOnLoad-Verwaltung erforderlich.
    /// </summary>
    public static class GameSession
    {
        public static int SelectedSaveSlot { get; private set; } = -1;
        public static int CoinCount { get; private set; }
        public static int Gold => CoinCount; // Existing coin saves and drops remain compatible.
        public static int MusicMedallions { get; private set; }
        private static readonly List<CharacterStatBonus> characterBonuses = new List<CharacterStatBonus>();
        private static readonly HashSet<string> ownedMusicSheets = new HashSet<string>();
        private static readonly Dictionary<MusicSheetClass, string> equippedMusicSheets = new Dictionary<MusicSheetClass, string>();

        public static void AddMusicMedallions(int amount)
        {
            if (amount > 0) MusicMedallions = (int)System.Math.Min(int.MaxValue, (long)MusicMedallions + amount);
        }
        public static bool SpendMusicMedallions(int amount)
        {
            if (amount < 0 || amount > MusicMedallions) return false;
            MusicMedallions -= amount;
            return true;
        }
        public static float GetCharacterBonus(CharacterStat stat) => characterBonuses.Where(b => b.stat == stat).Sum(b => b.amount);
        public static bool TryImproveCharacterStat(CharacterStat stat, float amount, int medallionCost)
        {
            if (!System.Enum.IsDefined(typeof(CharacterStat), stat) || float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0 ||
                float.IsInfinity(GetCharacterBonus(stat) + amount) || medallionCost < 0) return false;
            if ((stat == CharacterStat.MaxHealth || stat == CharacterStat.AuraDamage || stat == CharacterStat.ProjectileDamage || stat == CharacterStat.ProjectileCount) && amount != Mathf.Floor(amount)) return false;
            if (!SpendMusicMedallions(medallionCost)) return false;
            AddCharacterBonus(stat, amount);
            return true;
        }
        // The future shop supplies its configured upgrade amount after spending medallions.
        public static void AddCharacterBonus(CharacterStat stat, float amount)
        {
            if (!System.Enum.IsDefined(typeof(CharacterStat), stat) || float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0) return;
            var bonus = characterBonuses.Find(b => b.stat == stat);
            if (bonus == null) characterBonuses.Add(new CharacterStatBonus { stat = stat, amount = amount });
            else if (!float.IsInfinity(bonus.amount + amount)) bonus.amount += amount;
        }
        public static bool OwnsMusicSheet(MusicSheetDefinition sheet) => sheet != null && !string.IsNullOrWhiteSpace(sheet.id) && (sheet.availableFromStart || ownedMusicSheets.Contains(sheet.id));
        public static bool TryBuyMusicSheet(MusicSheetDefinition sheet)
        {
            if (sheet == null || string.IsNullOrWhiteSpace(sheet.id) || OwnsMusicSheet(sheet) || sheet.goldPrice < 0 || !SpendCoins(sheet.goldPrice)) return false;
            ownedMusicSheets.Add(sheet.id);
            return true;
        }
        public static bool EquipMusicSheet(MusicSheetDefinition sheet)
        {
            if (!OwnsMusicSheet(sheet)) return false;
            equippedMusicSheets[sheet.sheetClass] = sheet.id;
            return true;
        }
        public static string GetEquippedSheetId(MusicSheetClass sheetClass) => equippedMusicSheets.TryGetValue(sheetClass, out string id) ? id : null;

        private static void ResetCharacterProgress()
        {
            MusicMedallions = 0;
            characterBonuses.Clear(); ownedMusicSheets.Clear(); equippedMusicSheets.Clear();
        }

        public static int BallCountUpgradeLevel { get; private set; }
        public static int ProjectileCountUpgradeLevel { get; private set; }
        public static int ProjectileDamageUpgradeLevel { get; private set; }
        public static int CoinMagnetUpgradeLevel { get; private set; }

        private static readonly HashSet<string> _completedLevelKeys = new HashSet<string>();
        public static int CurrentWave { get; private set; } = 1;
        public static int EnemiesDefeated { get; private set; }
        public static int Score { get; set; }
        public static void RecordEnemyDefeated() => EnemiesDefeated++;
        // Call only when a real wave manager completes a wave.
        public static void CompleteWave(int wave)
        {
            CurrentWave = Mathf.Max(CurrentWave, wave + 1);
            AchievementManager.Instance?.RecordWaveSurvived(wave);
        }
        private static float _savedPlaytimeSeconds;
        private static float _playSessionStartedAt;

        public static void SelectSaveSlot(int slotIndex)
        {
            SelectedSaveSlot = slotIndex;
        }

        public static void StartNewSaveSlot(int slotIndex)
        {
            SelectedSaveSlot = slotIndex;
            CoinCount = 0;
            ResetCharacterProgress();
            CurrentWave = 1; EnemiesDefeated = 0; Score = 0;
            BallCountUpgradeLevel = 0;
            ProjectileCountUpgradeLevel = 0;
            ProjectileDamageUpgradeLevel = 0;
            CoinMagnetUpgradeLevel = 0;
            _completedLevelKeys.Clear();
            _savedPlaytimeSeconds = 0f;
            _playSessionStartedAt = Time.realtimeSinceStartup;
        }

        public static void AddCoins(int amount)
        {
            if (amount > 0) CoinCount = (int)System.Math.Min(int.MaxValue, (long)CoinCount + amount);
        }

        /// <summary>
        /// Zieht Münzen ab, falls genug vorhanden sind. Gibt true zurück bei Erfolg,
        /// false wenn nicht genug Münzen da waren (dann wird nichts abgezogen).
        /// </summary>
        public static bool SpendCoins(int amount)
        {
            if (amount < 0 || CoinCount < amount) return false;

            CoinCount -= amount;
            return true;
        }

        /// <summary>
        /// Aktuelle Stufe eines Upgrades (per String-Schlüssel, siehe UpgradeController-Konstanten).
        /// </summary>
        public static int GetUpgradeLevel(string key)
        {
            if (key == UpgradeController.BallCountKey) return BallCountUpgradeLevel;
            if (key == UpgradeController.ProjectileCountKey) return ProjectileCountUpgradeLevel;
            if (key == UpgradeController.ProjectileDamageKey) return ProjectileDamageUpgradeLevel;
            if (key == UpgradeController.CoinMagnetKey) return CoinMagnetUpgradeLevel;
            return 0;
        }

        public static void IncreaseUpgradeLevel(string key)
        {
            if (key == UpgradeController.BallCountKey) BallCountUpgradeLevel++;
            else if (key == UpgradeController.ProjectileCountKey) ProjectileCountUpgradeLevel++;
            else if (key == UpgradeController.ProjectileDamageKey) ProjectileDamageUpgradeLevel++;
            else if (key == UpgradeController.CoinMagnetKey) CoinMagnetUpgradeLevel++;
        }

        public static bool IsLevelCompleted(string levelKey) => _completedLevelKeys.Contains(levelKey);

        public static void MarkLevelCompleted(string levelKey) => _completedLevelKeys.Add(levelKey);

        /// <summary>
        /// Lädt Münzen + Upgrade-Stufen + Level-Fortschritt aus den Save-Daten eines
        /// Speicherstands - aufrufen, sobald ein Slot ausgewählt wird (siehe SaveSlotSelectionController).
        /// </summary>
        public static void LoadFromSaveData(SaveData data)
        {
            data ??= new SaveData();
            CoinCount = Mathf.Max(0, data.coinCount);
            ResetCharacterProgress();
            MusicMedallions = Mathf.Max(0, data.musicMedallions);
            foreach (var bonus in data.characterStatBonuses ?? new List<CharacterStatBonus>())
                if (bonus != null) AddCharacterBonus(bonus.stat, bonus.amount);
            foreach (string id in data.ownedMusicSheetIds ?? new List<string>())
                if (!string.IsNullOrWhiteSpace(id)) ownedMusicSheets.Add(id);
            equippedMusicSheets[MusicSheetClass.ATK] = data.equippedAtkSheet;
            equippedMusicSheets[MusicSheetClass.DEF] = data.equippedDefSheet;
            equippedMusicSheets[MusicSheetClass.SPC] = data.equippedSpcSheet;
            CurrentWave = Mathf.Max(1, data.currentWave); EnemiesDefeated = data.enemiesDefeated; Score = data.score;
            BallCountUpgradeLevel = data.ballCountUpgradeLevel;
            ProjectileCountUpgradeLevel = data.projectileCountUpgradeLevel;
            ProjectileDamageUpgradeLevel = data.projectileDamageUpgradeLevel;
            CoinMagnetUpgradeLevel = data.coinMagnetUpgradeLevel;

            _completedLevelKeys.Clear();
            foreach (string key in data.completedLevelKeys ?? new List<string>())
            {
                _completedLevelKeys.Add(key);
            }
            _savedPlaytimeSeconds = Mathf.Max(0f, data.playtimeSeconds);
            _playSessionStartedAt = Time.realtimeSinceStartup;
        }

        /// <summary>
        /// Schreibt die aktuellen Münzen + Upgrade-Stufen + Level-Fortschritt in die übergebenen
        /// Save-Daten, zum anschließenden Speichern über SaveSystem.Save(...).
        /// </summary>
        public static void WriteToSaveData(SaveData data)
        {
            data.currentWave = CurrentWave; data.enemiesDefeated = EnemiesDefeated; data.score = Score;
            data.playtimeSeconds = _savedPlaytimeSeconds + Mathf.Max(0f, Time.realtimeSinceStartup - _playSessionStartedAt);
            if (LevelManager.Instance != null && LevelManager.Instance.CurrentLevelIndex >= 0)
                data.level = LevelManager.Instance.CurrentLevelIndex + 1;
            data.coinCount = CoinCount;
            data.musicMedallions = MusicMedallions;
            data.characterStatBonuses = characterBonuses.Select(b => new CharacterStatBonus { stat = b.stat, amount = b.amount }).ToList();
            data.ownedMusicSheetIds = new List<string>(ownedMusicSheets);
            data.equippedAtkSheet = GetEquippedSheetId(MusicSheetClass.ATK);
            data.equippedDefSheet = GetEquippedSheetId(MusicSheetClass.DEF);
            data.equippedSpcSheet = GetEquippedSheetId(MusicSheetClass.SPC);
            data.ballCountUpgradeLevel = BallCountUpgradeLevel;
            data.projectileCountUpgradeLevel = ProjectileCountUpgradeLevel;
            data.projectileDamageUpgradeLevel = ProjectileDamageUpgradeLevel;
            data.coinMagnetUpgradeLevel = CoinMagnetUpgradeLevel;

            data.completedLevelKeys.Clear();
            data.completedLevelKeys.AddRange(_completedLevelKeys);
        }
    }
}
