using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using RhythmWitchClone.Gameplay;
using RhythmWitchClone.Core;
using RhythmWitchClone.Save;
using Object = UnityEngine.Object;

namespace RhythmWitchClone.EditorTools
{
    public static class MainCharacterSetup
    {
        const string CatalogPath = "Assets/Resources/MusicSheetCatalog.asset";
        static float Float(Object target, string field) => new SerializedObject(target).FindProperty(field).floatValue;
        static int Int(Object target, string field) => new SerializedObject(target).FindProperty(field).intValue;

        [MenuItem("Tools/Rhythmancy/Configure Main Character")]
        public static void Build()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                var prefab = PrefabUtility.LoadPrefabContents("Assets/Prefabs/Player.prefab");
                MusicSheetCatalog catalog;
                try
                {
                    var player = prefab.GetComponentInChildren<PlayerController>(true);
                    catalog = CreateCatalog(player.GetComponent<CardAbilityController>());
                    Configure(player, catalog);
                    PrefabUtility.SaveAsPrefabAsset(prefab, "Assets/Prefabs/Player.prefab");
                }
                finally { PrefabUtility.UnloadPrefabContents(prefab); }
                foreach (string name in new[] { "Tutorial", "Tutorial Level", "SafeZone" })
                {
                    var scene = EditorSceneManager.OpenScene("Assets/Scenes/" + name + ".unity");
                    Configure(Object.FindFirstObjectByType<PlayerController>(FindObjectsInactive.Include), catalog);
                    EditorSceneManager.SaveScene(scene);
                }
                AssetDatabase.SaveAssets();
                Validate();
                Debug.Log("MAIN_CHARACTER_SETUP_OK: character values preserved, starter sheets and save compatibility checked.");
            }
            finally
            {
                if (setup.Any(s => s.isLoaded && s.isActive)) EditorSceneManager.RestoreSceneManagerSetup(setup);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        static MusicSheetCatalog CreateCatalog(CardAbilityController cards)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<MusicSheetCatalog>(CatalogPath);
            if (catalog != null) return catalog;
            catalog = ScriptableObject.CreateInstance<MusicSheetCatalog>();
            catalog.sheets = new[] { CreateSheet(cards, MusicSheetClass.ATK), CreateSheet(cards, MusicSheetClass.DEF), CreateSheet(cards, MusicSheetClass.SPC) };
            AssetDatabase.CreateAsset(catalog, CatalogPath);
            return catalog;
        }

        static MusicSheetDefinition CreateSheet(CardAbilityController cards, MusicSheetClass kind)
        {
            string path = "Assets/Resources/Starter" + kind + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<MusicSheetDefinition>(path);
            if (existing != null) return existing;
            var sheet = ScriptableObject.CreateInstance<MusicSheetDefinition>();
            sheet.id = "starter-" + kind.ToString().ToLowerInvariant();
            sheet.displayName = "Startblatt " + kind;
            sheet.sheetClass = kind;
            sheet.availableFromStart = true;
            sheet.artwork = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Handdrawn/HUD/" + kind + "_Boost-HUD.png");
            sheet.manaDrainPerSecond = Float(cards, "manaDrainPerSecond");
            if (kind == MusicSheetClass.ATK)
            {
                sheet.auraDamageBonus = Int(cards, "attackDamageBonus");
                sheet.auraRadiusBonus = Float(cards, "attackRadiusBonus");
            }
            if (kind == MusicSheetClass.DEF)
            {
                sheet.damageReduction = Float(cards, "defenseDamageReduction");
                sheet.healingAmount = Int(cards, "defenseHealAmount");
                sheet.healingInterval = Float(cards, "defenseHealInterval");
            }
            if (kind == MusicSheetClass.SPC)
            {
                sheet.shockwaveRadius = Float(cards, "specialShockwaveRadius");
                sheet.knockbackForce = Float(cards, "specialKnockbackForce");
                sheet.stunDuration = Float(cards, "specialStunDuration");
                sheet.shockwaveColor = new SerializedObject(cards).FindProperty("specialShockwaveColor").colorValue;
                sheet.shockwaveVisualDuration = Float(cards, "specialShockwaveVisualDuration");
                sheet.selectionPulseDuration = Float(cards, "specialCardPulseDuration");
            }
            AssetDatabase.CreateAsset(sheet, path);
            return sheet;
        }

        static void Configure(PlayerController player, MusicSheetCatalog catalog)
        {
            if (player == null) throw new Exception("Missing player");
            var character = player.GetComponent<MainCharacter>();
            if (character == null)
            {
                character = player.gameObject.AddComponent<MainCharacter>();
                var cards = player.GetComponent<CardAbilityController>();
                character.baseStats = new MainCharacterStats
                {
                    maxHealth = Int(player, "maxHealth"), moveSpeed = Float(player, "moveSpeed"),
                    auraDamage = Int(player, "baseAuraDamage"), auraRadius = Float(player, "auraRadius"),
                    maxMana = Float(cards, "maxMana"), manaRegeneration = Float(cards, "manaRegenPerSecond"),
                    projectilesEnabled = new SerializedObject(player).FindProperty("projectilesEnabled").boolValue,
                    projectileDamage = Int(player, "baseProjectileDamage"), projectileCount = 1,
                    coinMagnetRange = Float(player, "coinMagnetRange")
                };
            }
            if (character.musicSheets == null) character.musicSheets = catalog;
            EditorUtility.SetDirty(character);
        }

        public static void Validate()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<MusicSheetCatalog>(CatalogPath);
            Check(catalog != null && catalog.sheets.Length >= 3, "Catalog missing");
            Check(catalog.sheets.All(s => s != null && !string.IsNullOrWhiteSpace(s.id)), "Invalid sheet IDs");
            Check(catalog.sheets.Select(s => s.id).Distinct().Count() == catalog.sheets.Length, "Duplicate sheet IDs");
            foreach (MusicSheetClass kind in Enum.GetValues(typeof(MusicSheetClass))) Check(catalog.GetStarter(kind) != null, "Missing starter " + kind);
            var before = new SaveData(); GameSession.WriteToSaveData(before);
            int slot = GameSession.SelectedSaveSlot;
            var testSheet = ScriptableObject.CreateInstance<MusicSheetDefinition>();
            try
            {
                GameSession.StartNewSaveSlot(0);
                Check(GameSession.Gold == 0 && GameSession.MusicMedallions == 0, "New slot wallet reset");
                Check(GameSession.OwnsMusicSheet(catalog.GetStarter(MusicSheetClass.ATK)), "Free starter missing");
                testSheet.id = "test-sheet"; testSheet.sheetClass = MusicSheetClass.ATK; testSheet.goldPrice = 20;
                Check(!GameSession.TryBuyMusicSheet(testSheet), "Purchase allowed without gold");
                GameSession.AddCoins(30); GameSession.AddMusicMedallions(7);
                Check(GameSession.TryBuyMusicSheet(testSheet), "Gold purchase failed");
                Check(GameSession.Gold == 10 && GameSession.MusicMedallions == 7, "Purchase touched wrong currency");
                Check(!GameSession.TryBuyMusicSheet(testSheet) && GameSession.Gold == 10, "Duplicate purchase charged");
                Check(!GameSession.SpendCoins(-10) && !GameSession.SpendMusicMedallions(-10), "Negative payment accepted");
                Check(GameSession.EquipMusicSheet(testSheet), "Owned sheet equip failed");
                Check(!GameSession.TryImproveCharacterStat(CharacterStat.MaxHealth, 10, 8), "Stat upgrade allowed without medallions");
                Check(!GameSession.TryImproveCharacterStat(CharacterStat.MaxHealth, .5f, 1), "Fractional HP charged");
                Check(GameSession.TryImproveCharacterStat(CharacterStat.MaxHealth, 10, 3), "Medallion stat upgrade failed");
                Check(GameSession.Gold == 10, "Stat upgrade consumed gold");
                var data = new SaveData(); GameSession.WriteToSaveData(data);
                string json = JsonUtility.ToJson(data);
                GameSession.StartNewSaveSlot(1);
                Check(!GameSession.OwnsMusicSheet(testSheet) && GameSession.GetCharacterBonus(CharacterStat.MaxHealth) == 0, "Slot data leaked");
                GameSession.LoadFromSaveData(JsonUtility.FromJson<SaveData>(json));
                Check(GameSession.Gold == 10 && GameSession.MusicMedallions == 4, "Wallet roundtrip failed");
                Check(GameSession.OwnsMusicSheet(testSheet) && GameSession.GetEquippedSheetId(MusicSheetClass.ATK) == testSheet.id, "Sheet roundtrip failed");
                Check(GameSession.GetCharacterBonus(CharacterStat.MaxHealth) == 10, "Stats roundtrip failed");
                GameSession.LoadFromSaveData(JsonUtility.FromJson<SaveData>("{\"coinCount\":42}"));
                Check(GameSession.Gold == 42 && GameSession.MusicMedallions == 0 && GameSession.GetCharacterBonus(CharacterStat.MaxHealth) == 0, "Legacy save migration failed");
                var player = Object.FindFirstObjectByType<PlayerController>();
                var character = player.GetComponent<MainCharacter>();
                Check(character != null, "Character component missing");
                float speed = character.Stats.moveSpeed;
                GameSession.AddCharacterBonus(CharacterStat.MoveSpeed, 1);
                character.RefreshStats(); character.RefreshStats();
                Check(Mathf.Abs(Float(player, "moveSpeed") - speed - 1) < .001f, "Repeated refresh stacked upgrades");
                Check(character.GetEquippedSheet(MusicSheetClass.ATK) == catalog.GetStarter(MusicSheetClass.ATK), "Legacy save starter fallback failed");
                Debug.Log("MAIN_CHARACTER_TESTS_OK: isolated wallets, purchase/equip, JSON roundtrip, slot reset, legacy saves and idempotent stat refresh.");
            }
            finally
            {
                Object.DestroyImmediate(testSheet);
                GameSession.LoadFromSaveData(before); GameSession.SelectSaveSlot(slot);
            }
        }
        static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    }
}
