using TMPro;
using UnityEngine;

namespace RhythmWitchClone.UI
{
    /// <summary>Non-modal feedback that remains visible above all menu panels.</summary>
    public sealed class MenuStatus : MonoBehaviour
    {
        private float until;
        public void Show(string message)
        {
            GetComponent<TMP_Text>().text = message;
            until = Time.unscaledTime + 7f;
        }
        private void Update()
        {
            if (until > 0 && Time.unscaledTime >= until) { GetComponent<TMP_Text>().text = ""; until = 0; }
        }
        public static void Show(Component menu, string message)
        {
            var status = menu.GetComponentInChildren<MenuStatus>(true);
            if (status != null) status.Show(message);
            else Debug.LogWarning(message);
        }
    }
}
