using System.Collections.Generic;
using System.IO;
using System.Linq;
using RhythmWitchClone.UI.MainMenu;
using RhythmWitchClone.UI;
using RhythmWitchClone.UI.Pause;
using RhythmWitchClone.Audio;
using RhythmWitchClone.Levels;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RhythmWitchClone.EditorTools
{
    /// <summary>Idempotente Einrichtung der Rhythmancy-Menü-Szenen, UI-Struktur und Platzhalter-Prefab.</summary>
    public static class RhythmancyMenuSetup
    {
        private const string MainMenuPath = "Assets/Scenes/Menü.unity";
        private const string PauseMenuPath = "Assets/Scenes/PauseMenu.unity";
        private const string ButtonPrefabPath = "Assets/Prefabs/MenuButtonPlaceholder.prefab";

        [MenuItem("Tools/Rhythmancy/Create Menu System")]
        public static void CreateMenuSystem()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("Setup nur außerhalb des Play-Modus ausführen."); return; }
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(MainMenuPath))
            {
                EditorUtility.DisplayDialog("Rhythmancy Setup", $"Szene nicht gefunden: {MainMenuPath}", "OK");
                return;
            }

            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.LoadAssetAtPath<RhythmWitchClone.Core.AchievementCatalog>("Assets/Resources/RhythmancyAchievements.asset"))
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<RhythmWitchClone.Core.AchievementCatalog>(), "Assets/Resources/RhythmancyAchievements.asset");
            Scene menuScene = OpenOrCreateScene(MainMenuPath);
            GameObject menuCanvas = FindRoot(menuScene, "MainMenuCanvas");
            if (menuCanvas == null)
            {
                menuCanvas = new GameObject("MainMenuCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                SceneManager.MoveGameObjectToScene(menuCanvas, menuScene);
            }

            RhythmancyMenuController menuController = menuCanvas.GetComponent<RhythmancyMenuController>();
            if (menuController == null) menuController = menuCanvas.AddComponent<RhythmancyMenuController>();
            menuController.BuildUi();
            PrepareMenu(menuCanvas);
            AssignPlaceholderSprites(menuCanvas.transform);
            EnsureAudioManager(menuScene);
            EnsureLevelManager(menuScene);
            EnsureEventSystem(menuScene);
            AddToBuildSettings(MainMenuPath, true);
            EditorSceneManager.MarkSceneDirty(menuScene);
            EditorSceneManager.SaveScene(menuScene);

            Scene pauseScene = OpenOrCreateScene(PauseMenuPath);
            GameObject pauseCanvas = FindRoot(pauseScene, "PauseMenuCanvas");
            if (pauseCanvas == null)
            {
                pauseCanvas = new GameObject("PauseMenuCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                SceneManager.MoveGameObjectToScene(pauseCanvas, pauseScene);
            }
            AddComponent<AdditivePauseMenuController>(pauseCanvas).BuildUi();
            PrepareMenu(pauseCanvas);
            AssignPlaceholderSprites(pauseCanvas.transform);
            // EventSystem живёт в игровой сцене и остаётся единственным при Additive-загрузке.
            AddToBuildSettings(PauseMenuPath, false);
            EditorSceneManager.MarkSceneDirty(pauseScene);
            EditorSceneManager.SaveScene(pauseScene);

            CreateButtonPrefab(menuScene);
            foreach (GameObject root in new[] { menuCanvas, pauseCanvas })
            {
                string prefabPath = "Assets/Prefabs/" + root.name + ".prefab";
                if (!AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath)) PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.SetActiveScene(menuScene);
            EditorSceneManager.CloseScene(pauseScene, true);
            Debug.Log("Rhythmancy Setup abgeschlossen: Menü und PauseMenu sind eingerichtet.");
            Selection.activeGameObject = menuCanvas;
        }

        [MenuItem("Tools/Rhythmancy/Validate Menu System")]
        public static void ValidateMenuSystem()
        {
            var issues = new List<string>();
            Scene menu = SceneManager.GetSceneByPath(MainMenuPath);
            bool openedMenu = false;
            if (!menu.IsValid() || !menu.isLoaded) { menu = EditorSceneManager.OpenScene(MainMenuPath, OpenSceneMode.Additive); openedMenu = true; }
            GameObject menuRoot = FindRoot(menu, "MainMenuCanvas");
            if (menuRoot == null) issues.Add("MainMenuCanvas fehlt in Menü.unity.");
            else
            {
                string[] required = { "MainMenuPanel", "NewGamePanel", "SettingsPanel", "SaveLoadPanel", "AchievementsPanel", "HelpPanel", "CreditsPanel", "ConfirmationDialog" };
                foreach (string child in required) if (menuRoot.transform.Find(child) == null) issues.Add($"Panel fehlt: {child}");
                foreach (string button in new[] { "PlayButton", "ContinueButton", "SettingsButton", "LoadButton", "AchievementsButton", "HelpButton", "CreditsButton", "QuitButton" })
                    if (!menuRoot.GetComponentsInChildren<UnityEngine.UI.Button>(true).Any(item => item.name == button)) issues.Add($"Button fehlt: {button}");
                if (menuRoot.GetComponent<RhythmancyMenuController>() == null) issues.Add("RhythmancyMenuController fehlt.");
            }

            Scene pause = SceneManager.GetSceneByPath(PauseMenuPath);
            bool openedPause = false;
            if (!pause.IsValid() || !pause.isLoaded) { pause = EditorSceneManager.OpenScene(PauseMenuPath, OpenSceneMode.Additive); openedPause = true; }
            GameObject pauseRoot = FindRoot(pause, "PauseMenuCanvas");
            if (pauseRoot == null || pauseRoot.GetComponent<AdditivePauseMenuController>() == null) issues.Add("PauseMenuCanvas oder AdditivePauseMenuController fehlt.");
            else
            {
                foreach (string button in new[] { "ResumeButton", "SaveButton", "LoadButton", "SettingsButton", "MainMenuButton", "QuitButton", "ConfirmYesButton", "ConfirmNoButton" })
                    if (!pauseRoot.GetComponentsInChildren<UnityEngine.UI.Button>(true).Any(item => item.name == button)) issues.Add($"Pause-Button fehlt: {button}");
                foreach (string panel in new[] { "PauseRootPanel", "SettingsPanel", "SaveLoadPanel", "ConfirmationDialog" })
                    if (pauseRoot.transform.Find(panel) == null) issues.Add($"Pause-Panel fehlt: {panel}");
            }
            if (!EditorBuildSettings.scenes.Any(item => item.enabled && item.path == MainMenuPath)) issues.Add("Menü.unity fehlt in den Build Settings.");
            if (!EditorBuildSettings.scenes.Any(item => item.enabled && item.path == PauseMenuPath)) issues.Add("PauseMenu.unity fehlt in den Build Settings.");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ButtonPrefabPath) == null) issues.Add("MenuButtonPlaceholder.prefab fehlt.");
            if (openedPause) EditorSceneManager.CloseScene(pause, true);
            if (openedMenu) EditorSceneManager.CloseScene(menu, true);

            if (issues.Count == 0) Debug.Log("Rhythmancy Menü-Validierung erfolgreich: benötigte Szenen, Panels und Hauptbuttons sind vorhanden.");
            else Debug.LogError("Rhythmancy Menü-Validierung: " + string.Join(" ", issues));
        }

        private static void PrepareMenu(GameObject root)
        {
            var version = AddComponent<MenuSetupVersion>(root);
            if (version.version >= 2) return;
            version.version = 2;
            foreach (VerticalLayoutGroup layout in root.GetComponentsInChildren<VerticalLayoutGroup>(true)) layout.childControlWidth = true;
            if (root.name == "PauseMenuCanvas" && !root.transform.Find("Background"))
            {
                var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
                background.transform.SetParent(root.transform, false); background.transform.SetAsFirstSibling();
                var rect = background.GetComponent<RectTransform>(); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
                background.GetComponent<Image>().color = new Color(.02f, .02f, .05f, .85f);
            }
            foreach (ScrollRect scroll in root.GetComponentsInChildren<ScrollRect>(true))
            {
                var layout = scroll.content.GetComponent<VerticalLayoutGroup>();
                layout.childControlWidth = true; layout.childControlHeight = true;
                layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            }
            foreach (HorizontalLayoutGroup row in root.GetComponentsInChildren<HorizontalLayoutGroup>(true))
                row.childControlWidth = true;
            // Additive upgrades only: existing images, custom positions and button listeners survive reruns.
            foreach (Image image in root.GetComponentsInChildren<Image>(true))
            {
                if (image.name != "Logo" && !image.name.StartsWith("MusicNote_")) continue;
                if (image.sprite == AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd")) image.sprite = null;
                AddComponent<MenuSpritePlaceholder>(image.gameObject);
            }
            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
                if (text.name == "PlaceholderGlyph" && text.text == "♬") text.text = "♫";
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            foreach (Slider slider in root.GetComponentsInChildren<Slider>(true))
            {
                TMP_Text caption = slider.transform.parent.Find(slider.name + "Label")?.GetComponent<TMP_Text>();
                if (caption == null && slider.transform.GetSiblingIndex() > 0)
                    caption = slider.transform.parent.GetChild(slider.transform.GetSiblingIndex() - 1).GetComponent<TMP_Text>();
                if (caption != null && !slider.GetComponent<MenuSliderValue>())
                    slider.gameObject.AddComponent<MenuSliderValue>().Configure(caption, caption.text);
            }
            if (!root.transform.Find("StatusMessage"))
            {
                var go = new GameObject("StatusMessage", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(MenuStatus));
                go.transform.SetParent(root.transform, false);
                var rect = go.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(.1f, .01f); rect.anchorMax = new Vector2(.9f, .07f);
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                var text = go.GetComponent<TMP_Text>(); text.font = TMP_Settings.defaultFontAsset;
                text.fontSize = 23; text.color = new Color(1f, .84f, .4f); text.alignment = TextAlignmentOptions.Center; text.text = ""; text.raycastTarget = false;
            }
            foreach (var row in root.GetComponentsInChildren<HorizontalLayoutGroup>(true))
            {
                if (!row.name.StartsWith("SlotRow")) continue;
                row.spacing = 12; row.childControlHeight = true; row.childForceExpandWidth = false;
                var rect = row.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(0, 110);
                var rowSize = row.GetComponent<LayoutElement>() ?? row.gameObject.AddComponent<LayoutElement>(); rowSize.minHeight = 110;
                foreach (Button button in row.GetComponentsInChildren<Button>(true))
                {
                    var layout = button.GetComponent<LayoutElement>(); layout.minWidth = 125; layout.preferredWidth = 125; layout.flexibleWidth = 0;
                    foreach (TMP_Text text in button.GetComponentsInChildren<TMP_Text>()) text.fontSize = 16;
                }
            }
        }

        private static Scene OpenOrCreateScene(string path)
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            if (scene.IsValid() && scene.isLoaded) return scene;
            if (!File.Exists(path))
            {
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                EditorSceneManager.SaveScene(scene, path);
                return scene;
            }
            return EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        }

        private static GameObject FindRoot(Scene scene, string name) => scene.GetRootGameObjects().FirstOrDefault(item => item.name == name);

        private static T AddComponent<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }

        private static void EnsureEventSystem(Scene scene)
        {
            if (scene.GetRootGameObjects().Any(item => item.GetComponent<EventSystem>() != null)) return;
            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            SceneManager.MoveGameObjectToScene(eventSystem, scene);
        }

        private static void EnsureAudioManager(Scene menuScene)
        {
            if (!menuScene.GetRootGameObjects().Any(item => item.GetComponent<AudioManager>() != null))
                Debug.LogError("Menü.unity benötigt einen konfigurierten AudioManager. Bitte aus der Menüsicherung wiederherstellen.");
        }

        private static void EnsureLevelManager(Scene menuScene)
        {
            if (!menuScene.GetRootGameObjects().Any(item => item.GetComponent<LevelManager>() != null))
                Debug.LogError("Menü.unity benötigt einen konfigurierten LevelManager. Bitte aus der Menüsicherung wiederherstellen.");
        }

        private static void AddToBuildSettings(string path, bool first)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            EditorBuildSettingsScene existing = scenes.FirstOrDefault(item => item.path == path);
            if (existing != null) { existing.enabled = true; scenes.Remove(existing); }
            var entry = new EditorBuildSettingsScene(path, true);
            if (first) scenes.Insert(0, entry); else scenes.Add(entry);
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void CreateButtonPrefab(Scene scene)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ButtonPrefabPath) != null) return;
            GameObject root = FindRoot(scene, "MainMenuCanvas");
            Transform button = root != null ? root.GetComponentsInChildren<UnityEngine.UI.Button>(true).FirstOrDefault(item => item.name == "PlayButton")?.transform : null;
            if (button == null) return;
            PrefabUtility.SaveAsPrefabAsset(button.gameObject, ButtonPrefabPath);
        }

        private static void AssignPlaceholderSprites(Transform root)
        {
            Sprite sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            if (sprite == null) return;
            foreach (UnityEngine.UI.Image image in root.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                if (image.sprite != null || image.name == "Logo" || image.GetComponent<MenuSpritePlaceholder>() != null) continue;
                image.sprite = sprite;
                image.type = UnityEngine.UI.Image.Type.Sliced;
                EditorUtility.SetDirty(image);
            }
        }
    }
}
