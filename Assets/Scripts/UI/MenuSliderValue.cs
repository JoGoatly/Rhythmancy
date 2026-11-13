using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RhythmWitchClone.UI
{
    [RequireComponent(typeof(Slider))]
    public sealed class MenuSliderValue : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;
        [SerializeField] private string caption;
        public void Configure(TMP_Text target, string title) { label = target; caption = title; }
        private void LateUpdate()
        {
            if (label != null) label.text = caption + "  " + Mathf.RoundToInt(GetComponent<Slider>().value) + " %";
        }
    }
}
