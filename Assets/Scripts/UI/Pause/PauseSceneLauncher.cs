using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using RhythmWitchClone.Core;
using RhythmWitchClone.Gameplay;

namespace RhythmWitchClone.UI.Pause
{
    /// <summary>Globaler ESC-Listener. Lädt PauseMenu additiv, ohne Gameplay neu zu laden.</summary>
    public sealed class PauseSceneLauncher : MonoBehaviour
    {
        private static PauseSceneLauncher _instance;
        private bool _loading;
        private static readonly string[] MenuScenes = { "Menü", "MainMenu", "PauseMenu" };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (_instance != null) return;
            GameObject host = new GameObject("PauseSceneLauncher");
            _instance = host.AddComponent<PauseSceneLauncher>();
            DontDestroyOnLoad(host);
        }

        private void Update()
        {
            if (_loading || Time.timeScale <= 0f || Keyboard.current == null) return;
            Key key = RhythmancySettings.GetKeyForAction("Pause");
            if (!Keyboard.current.escapeKey.wasPressedThisFrame && (key == Key.None || !Keyboard.current[key].wasPressedThisFrame)) return;
            if (SceneManager.GetSceneByName("PauseMenu").isLoaded) return;
            string activeScene = SceneManager.GetActiveScene().name;
            if (MenuScenes.Contains(activeScene) || FindAnyObjectByType<PlayerController>() == null) return;
            if (!Application.CanStreamedLevelBeLoaded("PauseMenu")) { Debug.LogError("PauseMenu fehlt in den Build Settings. Tools → Rhythmancy → Create Menu System ausführen."); return; }

            _loading = true;
            Time.timeScale = 0f;
            AsyncOperation operation = SceneManager.LoadSceneAsync("PauseMenu", LoadSceneMode.Additive);
            if (operation == null)
            {
                _loading = false;
                Time.timeScale = 1f;
                return;
            }
            operation.completed += _ => _loading = false;
        }
    }
}
