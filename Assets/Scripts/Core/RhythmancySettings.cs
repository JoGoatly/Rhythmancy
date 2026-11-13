using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using RhythmWitchClone.Audio;

namespace RhythmWitchClone.Core
{
    /// <summary>Persistente Audio- und Tasteneinstellungen. UI-Slider verwenden 0 bis 100.</summary>
    public static class RhythmancySettings
    {
        private const string MasterKey = "rhythmancy_master_volume";
        private const string MusicKey = "rhythmancy_music_volume";
        private const string SfxKey = "rhythmancy_sfx_volume";
        private const string BindingPrefix = "rhythmancy_binding_";

        public static float MasterVolume => PlayerPrefs.GetFloat(MasterKey, 1f);
        public static float MusicVolume => PlayerPrefs.GetFloat(MusicKey, PlayerPrefs.GetFloat("audio_music_volume", 1f));
        public static float SfxVolume => PlayerPrefs.GetFloat(SfxKey, PlayerPrefs.GetFloat("audio_sfx_volume", 1f));

        // Diese Belegungen entsprechen den tatsächlich im PlayerController und Cardabilitycontroller verwendeten Tasten.
        private static readonly Dictionary<string, string> DefaultBindings = new Dictionary<string, string>
        {
            { "Links", "A" }, { "Rechts", "D" }, { "Hoch", "W" }, { "Runter", "S" },
            { "Fähigkeit 1", "1" }, { "Fähigkeit 2", "2" }, { "Spezialfähigkeit", "3" },
            { "Interaktion", "E" }, { "Pause", "Escape" }
        };

        public static IReadOnlyDictionary<string, string> GetBindings()
        {
            var result = new Dictionary<string, string>();
            foreach (var pair in DefaultBindings) result[pair.Key] = PlayerPrefs.GetString(BindingPrefix + pair.Key, pair.Value);
            return result;
        }

        public static string GetBinding(string action) => PlayerPrefs.GetString(BindingPrefix + action,
            DefaultBindings.TryGetValue(action, out string value) ? value : string.Empty);

        public static void SetBinding(string action, string key)
        {
            if (string.IsNullOrWhiteSpace(action) || !DefaultBindings.ContainsKey(action) || string.IsNullOrWhiteSpace(key)) return;
            PlayerPrefs.SetString(BindingPrefix + action, key);
            PlayerPrefs.Save();
        }

        public static void SetMasterVolume(float value)
        {
            PlayerPrefs.SetFloat(MasterKey, Mathf.Clamp01(value));
            PlayerPrefs.Save();
            ApplyAudio();
        }

        public static void SetMusicVolume(float value)
        {
            PlayerPrefs.SetFloat(MusicKey, Mathf.Clamp01(value));
            PlayerPrefs.Save();
            ApplyAudio();
        }

        public static void SetSfxVolume(float value)
        {
            PlayerPrefs.SetFloat(SfxKey, Mathf.Clamp01(value));
            PlayerPrefs.Save();
            ApplyAudio();
        }

        public static void Reset()
        {
            SetMasterVolume(1f); SetMusicVolume(1f); SetSfxVolume(1f);
            foreach (string action in DefaultBindings.Keys) PlayerPrefs.DeleteKey(BindingPrefix + action);
            PlayerPrefs.Save();
            ApplyAudio();
        }

        public static void ApplyAudio()
        {
            if (AudioManager.Instance != null)
                AudioManager.Instance.SetMenuVolumes(MasterVolume, MusicVolume, SfxVolume);
        }

        public static string GetMovementDisplay()
        {
            return $"{GetBinding("Links")}/{GetBinding("Rechts")}  ·  {GetBinding("Hoch")}/{GetBinding("Runter")} (Pfeiltasten ebenfalls aktiv)";
        }

        public static Key GetKeyForAction(string action)
        {
            string value = GetBinding(action);
            if (value.Length == 1 && char.IsDigit(value[0])) value = "Digit" + value;
            if (Enum.TryParse(value, true, out Key key) && Enum.IsDefined(typeof(Key), key)) return key;
            return Key.None;
        }
    }
}
