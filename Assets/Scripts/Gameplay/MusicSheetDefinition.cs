using UnityEngine;

namespace RhythmWitchClone.Gameplay
{
    public enum MusicSheetClass { ATK, DEF, SPC }

    [CreateAssetMenu(menuName = "Rhythmancy/Notenblatt", fileName = "MusicSheet")]
    public class MusicSheetDefinition : ScriptableObject
    {
        [Tooltip("Unique stable ID, stored in save files. Do not change after release.")]
        public string id;
        public string displayName;
        public MusicSheetClass sheetClass;
        [TextArea] public string description;
        public Sprite artwork;
        [Min(0)] public int goldPrice;
        public bool availableFromStart;
        [Tooltip("Optional: replaces scene music while the sheet is active. Empty keeps the existing music.")]
        public AudioClip selectedMusic;

        [Header("Aktive ATK-/DEF-Blätter")]
        [Min(0)] public float manaDrainPerSecond = 1;
        [Min(0)] public int auraDamageBonus;
        [Min(0)] public float auraRadiusBonus;
        [Range(0, 1)] public float damageReduction;
        [Min(0)] public int healingAmount;
        [Min(.01f)] public float healingInterval = 2;

        [Header("SPC: einmalige Schockwelle bei vollem Mana")]
        [Min(0)] public float shockwaveRadius = 5;
        [Min(0)] public float knockbackForce = 8;
        [Min(0)] public float stunDuration = 10;
        public Color shockwaveColor = new Color(1, .85f, .2f, 1);
        [Min(.01f)] public float shockwaveVisualDuration = .8f;
        [Min(.01f)] public float selectionPulseDuration = .65f;
    }
}
