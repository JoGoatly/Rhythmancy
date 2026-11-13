using TMPro;
using UnityEngine;
using RhythmWitchClone.Levels;

namespace RhythmWitchClone.UI
{
    public class GameplayHudReadout : MonoBehaviour
    {
        public TMP_Text timeText;
        public TMP_Text levelText;
        private float elapsed;
        private void Update()
        {
            elapsed += Time.deltaTime;
            int seconds = Mathf.FloorToInt(elapsed);
            if (timeText != null) timeText.text = $"{seconds / 60:00}:{seconds % 60:00}";
            if (levelText != null)
                levelText.text = LevelManager.Instance != null
                    ? LevelManager.Instance.CurrentWave.ToString() : "1";
        }
    }
}
