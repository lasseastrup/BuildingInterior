#nullable enable
using UnityEngine;

namespace Triband.Storey.Editor
{
    /// <summary>
    /// The text a layout file holds while it is edited (<see cref="StoreyEdit"/>). A separate object because undo
    /// records the edit's whole object: undoing past a save must still show the layout as unsaved.
    /// </summary>
    internal sealed class SavedText : ScriptableObject
    {
        [SerializeField] internal string text = "";
    }
}
