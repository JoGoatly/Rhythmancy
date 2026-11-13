using UnityEngine;
namespace RhythmWitchClone.UI
{
    /// <summary>Marks applied setup migrations, so rerunning setup preserves Inspector customization.</summary>
    public sealed class MenuSetupVersion : MonoBehaviour
    {
        [HideInInspector] public int version;
    }
}
