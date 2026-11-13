using System;
using UnityEngine;
using RhythmWitchClone.Core;

namespace RhythmWitchClone.Gameplay
{
    public enum CharacterStat { MaxHealth, MoveSpeed, AuraDamage, AuraRadius, MaxMana, ManaRegeneration, ProjectileDamage, ProjectileCount, CoinMagnetRange }

    [Serializable]
    public class CharacterStatBonus
    {
        public CharacterStat stat;
        public float amount;
    }

    [Serializable]
    public class MainCharacterStats
    {
        [Min(1)] public int maxHealth = 100;
        [Min(0)] public float moveSpeed = 3.5f;
        [Min(0)] public int auraDamage = 25;
        [Min(0)] public float auraRadius = 3;
        [Min(1)] public float maxMana = 100;
        [Min(0)] public float manaRegeneration = 5;
        public bool projectilesEnabled;
        [Min(0)] public int projectileDamage = 25;
        [Min(1)] public int projectileCount = 1;
        [Min(0)] public float coinMagnetRange = 3;

        public MainCharacterStats Copy() => (MainCharacterStats)MemberwiseClone();
    }

    /// <summary>Base character values plus persistent, slot-specific improvements.
    /// PlayerController remains responsible for movement, combat and health.</summary>
    [DisallowMultipleComponent]
    public class MainCharacter : MonoBehaviour
    {
        public MainCharacterStats baseStats = new MainCharacterStats();
        public MusicSheetCatalog musicSheets;

        public MainCharacterStats Stats
        {
            get
            {
                var s = baseStats.Copy();
                s.maxHealth = Mathf.Max(1, s.maxHealth + Mathf.RoundToInt(GameSession.GetCharacterBonus(CharacterStat.MaxHealth)));
                s.moveSpeed = Mathf.Max(0, s.moveSpeed + GameSession.GetCharacterBonus(CharacterStat.MoveSpeed));
                s.auraDamage = Mathf.Max(0, s.auraDamage + Mathf.RoundToInt(GameSession.GetCharacterBonus(CharacterStat.AuraDamage)));
                s.auraRadius = Mathf.Max(0, s.auraRadius + GameSession.GetCharacterBonus(CharacterStat.AuraRadius));
                s.maxMana = Mathf.Max(1, s.maxMana + GameSession.GetCharacterBonus(CharacterStat.MaxMana));
                s.manaRegeneration = Mathf.Max(0, s.manaRegeneration + GameSession.GetCharacterBonus(CharacterStat.ManaRegeneration));
                s.projectileDamage = Mathf.Max(0, s.projectileDamage + Mathf.RoundToInt(GameSession.GetCharacterBonus(CharacterStat.ProjectileDamage)));
                s.projectileCount = Mathf.Max(1, s.projectileCount + Mathf.RoundToInt(GameSession.GetCharacterBonus(CharacterStat.ProjectileCount)));
                s.coinMagnetRange = Mathf.Max(0, s.coinMagnetRange + GameSession.GetCharacterBonus(CharacterStat.CoinMagnetRange));
                return s;
            }
        }

        public MusicSheetDefinition GetEquippedSheet(MusicSheetClass sheetClass)
        {
            if (musicSheets == null) return null;
            var sheet = musicSheets.Find(GameSession.GetEquippedSheetId(sheetClass));
            return sheet != null && sheet.sheetClass == sheetClass && GameSession.OwnsMusicSheet(sheet)
                ? sheet : musicSheets.GetStarter(sheetClass);
        }

        public bool TryEquipSheet(string id)
        {
            var sheet = musicSheets != null ? musicSheets.Find(id) : null;
            if (sheet == null || !GameSession.OwnsMusicSheet(sheet)) return false;
            GameSession.EquipMusicSheet(sheet);
            GetComponent<CardAbilityController>()?.RefreshMusicSheets();
            return true;
        }

        public bool TryImproveStat(CharacterStat stat, float amount, int medallionCost)
        {
            if (!GameSession.TryImproveCharacterStat(stat, amount, medallionCost)) return false;
            RefreshStats();
            return true;
        }

        // Call after a future shop purchase. Recomputes from base values, never stacks twice.
        public void RefreshStats()
        {
            GetComponent<PlayerController>()?.ApplyCharacterStats();
            GetComponent<CardAbilityController>()?.RefreshCharacterStats();
        }
    }
}
