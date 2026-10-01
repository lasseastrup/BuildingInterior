#nullable enable
using Triband.Storey.Edit;
using UnityEngine;

namespace Triband.Storey.Editor
{
    /// <summary>
    /// A facade style kept as an asset (Plan §4.1), shared and versioned: the Facade tab lists the project's presets
    /// next to the five built-in ones. Applying one keeps the building's roof, as the built-in presets do. The style is
    /// stored as Storey's JSON for a style, because the model's optional fields do not survive Unity's serializer.
    /// </summary>
    [CreateAssetMenu(menuName = "Storey/Facade Style", fileName = "Facade Style")]
    public sealed class FacadeStylePreset : ScriptableObject
    {
        [SerializeField, TextArea(4, 16)] internal string style = PrototypeJson.WriteStyle(Styles.Preset("brick"));

        public FacadeStyle Style => PrototypeJson.ReadStyle(style);

        internal void Set(FacadeStyle s) => style = PrototypeJson.WriteStyle(s);
    }
}
