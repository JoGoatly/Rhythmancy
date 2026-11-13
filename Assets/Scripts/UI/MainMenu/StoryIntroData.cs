using UnityEngine;

namespace RhythmWitchClone.UI.MainMenu
{
    [CreateAssetMenu(menuName = "Rhythmancy/Story Intro")]
    public sealed class StoryIntroData : ScriptableObject
    {
        [System.Serializable]
        public class Slide
        {
            public Sprite image;
            [TextArea(3, 8)] public string text;
        }
        public Slide[] slides;
        public Sprite[] tutorialImages;
    }
}
