using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using RhythmWitchClone.Core;

namespace RhythmWitchClone.Audio
{
    /// <summary>
    /// Persistenter Audio-Manager (Singleton, bleibt über alle Szenen hinweg erhalten - DontDestroyOnLoad).
    ///
    /// Funktionsprinzip:
    /// - "Musik" ist ausschließlich das, was über mainMenuMusic oder gameMusicPlaylist abgespielt wird
    ///   (über die interne, automatisch verwaltete Musik-AudioSource dieses Managers).
    /// - JEDE andere AudioSource in der Szene (Schritte, Treffer, Ambiente, Button-Klicks, ...) gilt
    ///   automatisch als SFX - unabhängig davon, wo sie herkommt.
    /// - Der Music-Slider verändert ausschließlich die Musik-Wiedergabe dieses Managers.
    /// - Der SFX-Slider verändert die Lautstärke von allem anderen.
    ///
    /// Platzierung: Dieses Script gehört auf ein GameObject in der allerersten Szene
    /// (z.B. ein "Bootstrap"- oder "Persistent"-GameObject), das dann per DontDestroyOnLoad
    /// über den gesamten Spielverlauf bestehen bleibt.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [Header("Musik-Wiedergabe")]
        [Tooltip("Wird in Dauerschleife abgespielt, solange die Hauptmenü-Szene aktiv ist.")]
        [SerializeField] private AudioClip mainMenuMusic;
        [Tooltip("Wird in JEDER anderen Szene (Safe Zone, Level, ...) der Reihe nach abgespielt (Track 1, dann 2, dann 3, ... danach wieder von vorn) - für Abwechslung statt Dauerschleife auf einem Track.")]
        [SerializeField] private List<AudioClip> gameMusicPlaylist = new List<AudioClip>();
        [Tooltip("Exakter Szenen-Name (Datei ohne .unity) des Hauptmenüs - ausschließlich dort spielt mainMenuMusic.")]
        [SerializeField] private string mainMenuSceneName = "Menü";

        // Eigene AudioSource des AudioManagers, ausschließlich für die automatische Musik-Wiedergabe.
        // Ist die EINZIGE AudioSource, die als Musik gilt (siehe ApplyVolumeToSource).
        private AudioSource _musicPlaybackSource;
        private int _currentPlaylistIndex = -1; // -1 = kein Playlist-Modus aktiv (z.B. Hauptmenü-Track läuft)
        private bool _isMusicPaused;
        private Object _sheetMusicOwner;
        private AudioClip _previousMusic;
        private float _previousMusicTime;
        private int _previousPlaylistIndex;
        private bool _previousLoop;

        public void SetSheetMusic(Object owner, AudioClip clip)
        {
            if (clip == null) { ClearSheetMusic(owner); return; }
            if (_musicPlaybackSource == null) return;
            if (_sheetMusicOwner == null)
            {
                _previousMusic = _musicPlaybackSource.clip;
                _previousMusicTime = _musicPlaybackSource.time;
                _previousPlaylistIndex = _currentPlaylistIndex;
                _previousLoop = _musicPlaybackSource.loop;
            }
            _sheetMusicOwner = owner;
            _currentPlaylistIndex = -1;
            _musicPlaybackSource.clip = clip;
            _musicPlaybackSource.loop = true;
            _musicPlaybackSource.Play();
            if (_isMusicPaused) _musicPlaybackSource.Pause();
        }

        public void ClearSheetMusic(Object owner)
        {
            if (_sheetMusicOwner != owner || _sheetMusicOwner == null || _musicPlaybackSource == null) return;
            _sheetMusicOwner = null;
            _musicPlaybackSource.clip = _previousMusic;
            _musicPlaybackSource.loop = _previousLoop;
            _currentPlaylistIndex = _previousPlaylistIndex;
            if (_previousMusic != null)
            {
                _musicPlaybackSource.time = Mathf.Clamp(_previousMusicTime, 0, Mathf.Max(0, _previousMusic.length - .01f));
                _musicPlaybackSource.Play();
                if (_isMusicPaused) _musicPlaybackSource.Pause();
            }
            else _musicPlaybackSource.Stop();
        }

        [Header("Lautstärke (0 - 1)")]
        [Range(0f, 1f)][SerializeField] private float musicVolume = 1f;
        [Range(0f, 1f)][SerializeField] private float sfxVolume = 1f;
        [Range(0f, 1f)][SerializeField] private float masterVolume = 1f;

        private const string MusicVolumeKey = "audio_music_volume";
        private const string SfxVolumeKey = "audio_sfx_volume";

        // Merkt sich die ursprüngliche (im Editor eingestellte) Lautstärke jeder Quelle,
        // damit die Slider-Werte nicht mehrfach hintereinander multipliziert werden.
        private readonly Dictionary<AudioSource, float> _originalVolumes = new Dictionary<AudioSource, float>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            musicVolume = PlayerPrefs.GetFloat(MusicVolumeKey, 1f);
            sfxVolume = PlayerPrefs.GetFloat(SfxVolumeKey, 1f);
            masterVolume = RhythmancySettings.MasterVolume;
            musicVolume = RhythmancySettings.MusicVolume;
            sfxVolume = RhythmancySettings.SfxVolume;

            // Eigene AudioSource für die automatische Musik-Wiedergabe einrichten.
            _musicPlaybackSource = GetComponent<AudioSource>();
            _musicPlaybackSource.playOnAwake = false;
            _musicPlaybackSource.spatialBlend = 0f; // 2D, damit Musik unabhängig von der Kameraposition gleich laut bleibt
            _musicPlaybackSource.volume = 1f;
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void Start()
        {
            RefreshKnownAudioSources();
            ApplyVolumes();
            PlayMusicForScene(SceneManager.GetActiveScene().name);
        }

        private void Update()
        {
            // Playlist-Modus (nur in der Spiel-Szene aktiv): sobald ein Track fertig ist,
            // automatisch den nächsten starten, am Ende wieder von vorn.
            // Wichtig: NICHT weiterschalten, solange manuell pausiert wurde - isPlaying wird
            // durch Pause() ebenfalls false, ohne den Check würde ein pausierter Track sofort
            // zum nächsten weiterspringen.
            if (_currentPlaylistIndex >= 0 && !_isMusicPaused && !_musicPlaybackSource.isPlaying)
            {
                AdvancePlaylist();
            }

            HandleGlobalButtonSounds();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // PauseMenu ist ein Overlay und soll den laufenden Titel nicht durch Spielmusik ersetzen.
            if (scene.name == "PauseMenu") return;
            _sheetMusicOwner = null;
            // Neue Szene = evtl. neue AudioSources (z.B. Level-Musik, Ambiente-SFX).
            RefreshKnownAudioSources();
            ApplyVolumes();
            PlayMusicForScene(scene.name);
        }

        /// <summary>
        /// Startet die passende Musik für die übergebene Szene:
        /// Hauptmenü-Szene -> mainMenuMusic in Dauerschleife.
        /// JEDE andere Szene (Safe Zone, einzelne Level, ...) -> gameMusicPlaylist der Reihe nach.
        /// So läuft die Hauptmenü-Musik garantiert ausschließlich im Hauptmenü, unabhängig davon,
        /// wie viele weitere Szenen (Level) im Projekt noch dazukommen.
        /// </summary>
        private void PlayMusicForScene(string sceneName)
        {
            if (sceneName == mainMenuSceneName)
            {
                PlaySingleLoopingTrack(mainMenuMusic);
            }
            else
            {
                StartPlaylist();
            }
        }

        private void PlaySingleLoopingTrack(AudioClip clip)
        {
            _currentPlaylistIndex = -1; // Playlist-Modus deaktivieren
            if (clip == null)
            {
                _musicPlaybackSource.Stop();
                return;
            }

            _musicPlaybackSource.clip = clip;
            _musicPlaybackSource.loop = true;
            _musicPlaybackSource.Play();
            _isMusicPaused = false;
        }

        private void StartPlaylist()
        {
            if (gameMusicPlaylist.Count == 0)
            {
                _currentPlaylistIndex = -1;
                _musicPlaybackSource.Stop();
                return;
            }

            _currentPlaylistIndex = 0;
            PlayPlaylistTrack(_currentPlaylistIndex);
        }

        private void AdvancePlaylist()
        {
            if (gameMusicPlaylist.Count == 0) return;

            _currentPlaylistIndex = (_currentPlaylistIndex + 1) % gameMusicPlaylist.Count;
            PlayPlaylistTrack(_currentPlaylistIndex);
        }

        private void PlayPlaylistTrack(int index)
        {
            _musicPlaybackSource.clip = gameMusicPlaylist[index];
            _musicPlaybackSource.loop = false; // Update() erkennt Track-Ende nur, wenn loop = false ist
            _musicPlaybackSource.Play();
            _isMusicPaused = false;
        }

        /// <summary>
        /// Pausiert/setzt die aktuelle Musik fort (Play/Pause-Umschalter, z.B. für ein UI-Widget).
        /// </summary>
        public void ToggleMusicPause()
        {
            if (_isMusicPaused) ResumeMusic();
            else PauseMusic();
        }

        public void PauseMusic()
        {
            if (_isMusicPaused) return;
            _isMusicPaused = true;
            _musicPlaybackSource.Pause();
        }

        public void ResumeMusic()
        {
            if (!_isMusicPaused) return;
            _isMusicPaused = false;
            _musicPlaybackSource.UnPause();
        }

        /// <summary>
        /// Springt zum nächsten Track der Playlist. Wirkt nur im Playlist-Modus (Spiel-Szene) -
        /// im Hauptmenü (Einzeltrack in Dauerschleife) passiert nichts.
        /// </summary>
        public void SkipToNextTrack()
        {
            if (_currentPlaylistIndex < 0) return;
            AdvancePlaylist();
        }

        /// <summary>
        /// Springt zum vorherigen Track der Playlist (mit Umlauf zum letzten Track, falls am Anfang).
        /// Wirkt nur im Playlist-Modus (Spiel-Szene).
        /// </summary>
        public void SkipToPreviousTrack()
        {
            if (_currentPlaylistIndex < 0 || gameMusicPlaylist.Count == 0) return;

            _currentPlaylistIndex = (_currentPlaylistIndex - 1 + gameMusicPlaylist.Count) % gameMusicPlaylist.Count;
            PlayPlaylistTrack(_currentPlaylistIndex);
        }

        /// <summary>
        /// Name des aktuell geladenen Tracks (Dateiname des AudioClips), für die Anzeige im UI.
        /// </summary>
        public string GetCurrentTrackName()
        {
            return _musicPlaybackSource.clip != null ? _musicPlaybackSource.clip.name : "-";
        }

        public bool IsMusicPaused() => _isMusicPaused;

        /// <summary>
        /// Sucht alle AudioSources in der aktuell geladenen Szene(n) und merkt sich ihre
        /// Ursprungslautstärke, falls sie noch nicht bekannt sind. Bereits bekannte, aber
        /// zerstörte Quellen werden aus dem Cache entfernt.
        /// </summary>
        private void RefreshKnownAudioSources()
        {
            AudioSource[] found = FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
            foreach (AudioSource source in found)
            {
                RegisterAudioSource(source);
            }

            List<AudioSource> toRemove = null;
            foreach (var kvp in _originalVolumes)
            {
                if (kvp.Key == null)
                {
                    toRemove ??= new List<AudioSource>();
                    toRemove.Add(kvp.Key);
                }
            }
            if (toRemove != null)
            {
                foreach (var key in toRemove) _originalVolumes.Remove(key);
            }
        }

        /// <summary>
        /// Registriert eine AudioSource manuell. Nützlich für zur Laufzeit instanziierte
        /// Objekte (z.B. ein SFX-Prefab), die nicht automatisch beim Szenenwechsel gefunden werden.
        /// Aufruf z.B. im Awake() des betroffenen Objekts:
        ///   AudioManager.Instance.RegisterAudioSource(GetComponent&lt;AudioSource&gt;());
        /// </summary>
        public void RegisterAudioSource(AudioSource source)
        {
            if (source == null || _originalVolumes.ContainsKey(source)) return;

            _originalVolumes[source] = source.volume;
            ApplyVolumeToSource(source);
        }

        /// <summary>
        /// Praktische Methode, um einen einmaligen Soundeffekt abzuspielen (z.B. Button-Klick,
        /// Treffer-Sound), ohne selbst eine AudioSource im Level platzieren zu müssen.
        /// Wird automatisch mit der aktuellen SFX-Lautstärke abgespielt und zählt NICHT zur Musik.
        /// </summary>
        public void PlaySFX(AudioClip clip, Vector3 position, float volume = 1f)
        {
            if (clip == null) return;

            GameObject tempGO = new GameObject($"SFX_{clip.name}");
            tempGO.transform.position = position;
            AudioSource source = tempGO.AddComponent<AudioSource>();
            source.clip = clip;
            source.spatialBlend = 0f; // 2D-Sound per Standard, bei Bedarf im Aufrufer anpassen
            source.volume = volume * sfxVolume;
            source.Play();

            Destroy(tempGO, clip.length + 0.1f);
        }

        [Header("Sound-Effekte")]
        [Tooltip("Wird abgespielt, wenn der Spieler eine Münze einsammelt.")]
        [SerializeField] private AudioClip coinPickupSfx;
        [Tooltip("Wird abgespielt, wenn der Spieler ein Projektil abfeuert.")]
        [SerializeField] private AudioClip projectileFiredSfx;
        [Tooltip("Wird abgespielt, wenn der Spieler Schaden nimmt.")]
        [SerializeField] private AudioClip playerDamagedSfx;
        [Tooltip("Wird abgespielt, wenn ein Gegner (vom Spieler) Schaden nimmt.")]
        [SerializeField] private AudioClip enemyDamagedSfx;

        [Header("Sound-Effekte - UI")]
        [Tooltip("Wird abgespielt, wenn mit der Maus über einen Button gehovert wird.")]
        [SerializeField] private AudioClip buttonHoverSfx;
        [Tooltip("Wird abgespielt, wenn ein Button angeklickt wird.")]
        [SerializeField] private AudioClip buttonClickSfx;

        [Header("Sound-Effekte - Level-Ergebnis")]
        [SerializeField] private AudioClip levelWinSfx;
        [SerializeField] private AudioClip gameOverSfx;

        [Header("Sound-Effekte - Rhythmus-Treffer (Bälle)")]
        [SerializeField] private AudioClip perfectHitSfx;
        [SerializeField] private AudioClip goodHitSfx;
        [SerializeField] private AudioClip okHitSfx;
        [Tooltip("Wird abgespielt, wenn kein Ball getroffen wurde ('Schlecht').")]
        [SerializeField] private AudioClip missHitSfx;

        public void PlayCoinPickupSound(Vector3 position) => PlaySFX(coinPickupSfx, position);
        public void PlayProjectileFiredSound(Vector3 position) => PlaySFX(projectileFiredSfx, position);
        public void PlayPlayerDamagedSound(Vector3 position) => PlaySFX(playerDamagedSfx, position);
        public void PlayEnemyDamagedSound(Vector3 position) => PlaySFX(enemyDamagedSfx, position);

        public void PlayButtonHoverSound() => PlaySFX(buttonHoverSfx, Vector3.zero);
        public void PlayButtonClickSound() => PlaySFX(buttonClickSfx, Vector3.zero);

        public void PlayLevelWinSound() => PlaySFX(levelWinSfx, Vector3.zero);
        public void PlayGameOverSound() => PlaySFX(gameOverSfx, Vector3.zero);

        public void PlayPerfectHitSound() => PlaySFX(perfectHitSfx, Vector3.zero);
        public void PlayGoodHitSound() => PlaySFX(goodHitSfx, Vector3.zero);
        public void PlayOkHitSound() => PlaySFX(okHitSfx, Vector3.zero);
        public void PlayMissHitSound() => PlaySFX(missHitSfx, Vector3.zero);

        // Für die generische Button-Sound-Erkennung unten - wiederverwendete Liste,
        // damit nicht jeden Frame neuer Speicher alloziert wird.
        private readonly List<RaycastResult> _uiRaycastResults = new List<RaycastResult>();
        private Button _lastHoveredButton;

        /// <summary>
        /// Erkennt automatisch JEDEN Button im Spiel, ganz ohne dass an den Buttons selbst
        /// etwas eingerichtet werden muss: Jeden Frame wird per UI-Raycast geprüft, ob sich
        /// die Maus über einem (interaktiven) Button befindet - falls ja, wird bei Betreten
        /// der Hover-Sound und bei einem Linksklick der Klick-Sound abgespielt.
        /// </summary>
        private void HandleGlobalButtonSounds()
        {
            if (EventSystem.current == null || Mouse.current == null) return;

            PointerEventData pointerData = new PointerEventData(EventSystem.current)
            {
                position = Mouse.current.position.ReadValue()
            };

            _uiRaycastResults.Clear();
            EventSystem.current.RaycastAll(pointerData, _uiRaycastResults);

            Button hoveredButton = null;
            foreach (RaycastResult result in _uiRaycastResults)
            {
                Button button = result.gameObject.GetComponentInParent<Button>();
                if (button != null && button.interactable)
                {
                    hoveredButton = button;
                    break;
                }
            }

            if (hoveredButton != _lastHoveredButton)
            {
                if (hoveredButton != null)
                {
                    PlayButtonHoverSound();
                }
                _lastHoveredButton = hoveredButton;
            }

            if (hoveredButton != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                PlayButtonClickSound();
            }
        }

        private void ApplyVolumes()
        {
            foreach (var kvp in _originalVolumes)
            {
                if (kvp.Key == null) continue;
                ApplyVolumeToSource(kvp.Key);
            }
        }

        private void ApplyVolumeToSource(AudioSource source)
        {
            float baseVolume = _originalVolumes.TryGetValue(source, out float v) ? v : source.volume;

            // Einzige Regel: NUR die interne Musik-AudioSource dieses Managers gilt als Musik
            // (spielt ausschließlich mainMenuMusic oder Tracks aus gameMusicPlaylist ab).
            // Absolut jede andere AudioSource in der Szene wird als SFX behandelt.
            bool isMusic = source == _musicPlaybackSource;
            source.volume = baseVolume * masterVolume * (isMusic ? musicVolume : sfxVolume);
        }

        public void SetMusicVolume(float value)
        {
            RhythmancySettings.SetMusicVolume(value);
        }

        public void SetSfxVolume(float value)
        {
            RhythmancySettings.SetSfxVolume(value);
        }

        public float GetMusicVolume() => musicVolume;
        public float GetSfxVolume() => sfxVolume;

        public void SetMenuVolumes(float master, float music, float sfx)
        {
            masterVolume = Mathf.Clamp01(master);
            musicVolume = Mathf.Clamp01(music);
            sfxVolume = Mathf.Clamp01(sfx);
            ApplyVolumes();
        }
    }
}
