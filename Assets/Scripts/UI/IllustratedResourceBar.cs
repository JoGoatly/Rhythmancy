using UnityEngine;
using UnityEngine.UI;

namespace RhythmWitchClone.UI
{
    [RequireComponent(typeof(Slider))]
    public class IllustratedResourceBar : MonoBehaviour
    {
        public Image display;
        // Ascending fill levels; each sprite already includes its ink outline.
        public Sprite[] stages;
        public float[] levels;
        private Slider slider;

        private void OnEnable()
        {
            slider = GetComponent<Slider>();
            slider.onValueChanged.AddListener(OnValueChanged);
            Refresh();
        }

        private void OnDisable()
        {
            if (slider != null) slider.onValueChanged.RemoveListener(OnValueChanged);
        }

        private void OnValueChanged(float value) => Refresh();

        public void Refresh()
        {
            if (slider == null) slider = GetComponent<Slider>();
            if (display == null || stages == null || levels == null || stages.Length != levels.Length || stages.Length == 0) return;
            float value = slider.normalizedValue;
            int index = 0;
            // Reserve the full illustration for genuinely full HP/mana.
            for (int i = 1; i < levels.Length; i++)
                if (value >= levels[i]) index = i;
            if (value > 0 && index == 0) index = 1;
            display.sprite = stages[index];
        }
    }
}
