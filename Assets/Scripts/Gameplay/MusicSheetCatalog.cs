using System.Linq;
using UnityEngine;

namespace RhythmWitchClone.Gameplay
{
    [CreateAssetMenu(menuName = "Rhythmancy/Notenblatt-Katalog", fileName = "MusicSheetCatalog")]
    public class MusicSheetCatalog : ScriptableObject
    {
        public MusicSheetDefinition[] sheets = new MusicSheetDefinition[0];
        public MusicSheetDefinition Find(string id) => string.IsNullOrEmpty(id) ? null : sheets.FirstOrDefault(s => s != null && s.id == id);
        public MusicSheetDefinition GetStarter(MusicSheetClass sheetClass) => sheets.FirstOrDefault(s => s != null && s.sheetClass == sheetClass && s.availableFromStart);
    }
}
