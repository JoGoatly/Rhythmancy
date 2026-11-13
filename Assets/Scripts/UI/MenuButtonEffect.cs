using UnityEngine;
using UnityEngine.EventSystems;

namespace RhythmWitchClone.UI
{
    /// <summary>Weiche Hover- und Klickskalierung, läuft dank unscaledDeltaTime auch im Pause-Menü.</summary>
    public sealed class MenuButtonEffect : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
    {
        [SerializeField, Range(1.01f, 1.15f)] private float hoverScale = 1.07f;
        [SerializeField, Range(.85f, .99f)] private float clickScale = .96f;
        [SerializeField, Min(1f)] private float smoothing = 14f;

        private float _targetScale = 1f;
        private Vector3 _originalScale;

        private void Awake() => _originalScale = transform.localScale;

        private void Update()
        {
            transform.localScale = Vector3.Lerp(transform.localScale, _originalScale * _targetScale, Time.unscaledDeltaTime * smoothing);
        }

        private void OnDisable() { _targetScale = 1f; if (_originalScale != Vector3.zero) transform.localScale = _originalScale; }
        public void OnSelect(BaseEventData eventData) => _targetScale = hoverScale;
        public void OnDeselect(BaseEventData eventData) => _targetScale = 1f;
        public void OnPointerEnter(PointerEventData eventData) => _targetScale = hoverScale;
        public void OnPointerExit(PointerEventData eventData) => _targetScale = 1f;
        public void OnPointerDown(PointerEventData eventData) => _targetScale = clickScale;
        public void OnPointerUp(PointerEventData eventData) => _targetScale = hoverScale;
    }
}
