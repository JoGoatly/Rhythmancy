using System.Collections.Generic;
using UnityEngine;

namespace RhythmWitchClone.Core
{
    /// <summary>Editable definitions and icons; progress is stored separately in persistentDataPath.</summary>
    [CreateAssetMenu(menuName = "Rhythmancy/Achievement Catalog")]
    public sealed class AchievementCatalog : ScriptableObject
    {
        public List<AchievementDefinition> definitions = new List<AchievementDefinition>
        {
            new AchievementDefinition { id = "first_note", title = "Erster Ton", description = "Besiege deinen ersten Gegner.", target = 1 },
            new AchievementDefinition { id = "survivor_5", title = "Überlebenskünstler", description = "Überlebe 5 Wellen.", target = 5 },
            new AchievementDefinition { id = "fighter_100", title = "Musikalischer Kämpfer", description = "Besiege insgesamt 100 Gegner.", target = 100 },
            new AchievementDefinition { id = "unstoppable_10", title = "Unaufhaltsam", description = "Überlebe 10 Wellen.", target = 10 }
        };
    }
}
