using UnityEngine;
using UnityEngine.UI;

namespace RhythmWitchClone.UI
{
    /// <summary>Assign a sprite in the Inspector to replace the text placeholder automatically.</summary>
    [ExecuteAlways, RequireComponent(typeof(Image))]
    public sealed class MenuSpritePlaceholder : MonoBehaviour
    {
        private void Update()
        {
            var graphic = GetComponent<Image>();
            bool hasSprite = graphic.sprite != null;
            graphic.enabled = hasSprite;
            if (transform.childCount > 0) transform.GetChild(0).gameObject.SetActive(!hasSprite);
        }
    }
}
