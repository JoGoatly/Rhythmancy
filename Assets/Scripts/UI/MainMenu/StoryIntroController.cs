using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RhythmWitchClone.UI.MainMenu
{
    public sealed class StoryIntroController : MonoBehaviour
    {
        private static StoryIntroController active;
        private StoryIntroData data;
        private StoryIntroData tutorialData;
        private Image picture;
        private TMP_Text caption;
        private TMP_Text prompt;
        private RectTransform storyTextPanel;
        private string destination;
        private int index;
        private int shownFrame;
        private bool loading;

        public static void Show(string targetScene)
        {
            if (active != null) return;
            var content = Resources.Load<StoryIntroData>("StoryIntro");
            if (content == null || content.slides == null || content.slides.Length == 0)
            {
                Debug.LogError("StoryIntro: Story-Daten fehlen.");
                SceneManager.LoadScene(targetScene);
                return;
            }
            var root = new GameObject("StoryIntroCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(StoryIntroController));
            active = root.GetComponent<StoryIntroController>();
            active.data = content;
            active.tutorialData = Resources.Load<StoryIntroData>("GameTutorial");
            active.destination = targetScene;
            active.Build();
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        private static TMP_Text Label(string name, Transform parent, Vector2 min, Vector2 max, float size)
        {
            var label = Rect(name, parent, min, max).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = TMP_Settings.defaultFontAsset;
            label.fontSize = size;
            label.enableAutoSizing = true;
            label.fontSizeMin = 20;
            label.fontSizeMax = size;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            return label;
        }

        private void Build()
        {
            var canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            var scaler = GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var panel = Rect("IntroPanel", transform, Vector2.zero, Vector2.one);
            panel.gameObject.AddComponent<Image>().color = Color.black;
            picture = Rect("StoryImage", panel, Vector2.zero, Vector2.one).gameObject.AddComponent<Image>();
            picture.preserveAspect = true;
            picture.raycastTarget = false;
            var box = Rect("StoryTextPanel", panel, new Vector2(.1f, .035f), new Vector2(.9f, .28f));
            storyTextPanel = box;
            box.gameObject.AddComponent<Image>().color = new Color(.025f, .025f, .04f, .92f);
            caption = Label("StoryText", box, new Vector2(.035f, .23f), new Vector2(.965f, .94f), 32);
            prompt = Label("ContinueLabel", box, new Vector2(.035f, .025f), new Vector2(.965f, .2f), 22);
            Display();
        }

        private void Display()
        {
            bool tutorial = index >= data.slides.Length;
            int total = data.slides.Length + (tutorialData?.tutorialImages?.Length ?? 0);
            storyTextPanel.gameObject.SetActive(!tutorial);
            if (tutorial)
            {
                picture.sprite = tutorialData.tutorialImages[index - data.slides.Length];
                // Das Erklärbild enthält bereits seinen Text; der Hinweis sitzt darunter.
                picture.rectTransform.anchorMin = new Vector2(0f, .06f);
                prompt.transform.SetParent(storyTextPanel.parent, false);
                prompt.rectTransform.anchorMin = new Vector2(.1f, 0f);
                prompt.rectTransform.anchorMax = new Vector2(.9f, .055f);
                prompt.rectTransform.offsetMin = prompt.rectTransform.offsetMax = Vector2.zero;
            }
            else
            {
                picture.sprite = data.slides[index].image;
                caption.text = data.slides[index].text;
            }
            prompt.text = (tutorial
                ? $"Spielanleitung {index - data.slides.Length + 1} / {tutorialData.tutorialImages.Length}  ·  "
                : $"Story {index + 1} / {data.slides.Length}  ·  ") +
                (index == total - 1 ? "Klicken oder Leertaste: Tutorial starten" : (index == data.slides.Length - 1 ? "Klicken oder Leertaste: Zur Spielanleitung" : "Klicken oder Leertaste: Weiter"));
            shownFrame = Time.frameCount;
        }

        private void Update()
        {
            if (loading || Time.frameCount <= shownFrame + 1) return;
            bool advance = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
            advance |= Keyboard.current != null && (Keyboard.current.spaceKey.wasPressedThisFrame || Keyboard.current.enterKey.wasPressedThisFrame);
            if (!advance) return;
            if (++index < data.slides.Length + (tutorialData?.tutorialImages?.Length ?? 0)) Display();
            else
            {
                loading = true;
                Time.timeScale = 1f;
                SceneManager.LoadScene(destination);
            }
        }

        private void OnDestroy()
        {
            if (active == this) active = null;
        }
    }
}
