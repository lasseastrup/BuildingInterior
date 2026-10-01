#nullable enable
using Triband.ColorPipeline.Runtime;
using Triband.Core.Utils;
using Triband.Storey.Generate;
using UnityEngine;

namespace Triband.Storey.ColorPipeline
{
    /// <summary>
    /// The project's colours where a style leaves one out, and the palette colours the inspector suggests per field
    /// (docs/COLOURS.md §3.1, §3.8). Put one in a Resources folder named <see cref="ResourcesName"/>. An unset
    /// colour falls back to the prototype's fixed colour, matched to the nearest palette entry.
    /// </summary>
    [CreateAssetMenu(menuName = "Storey/Color Settings", fileName = ResourcesName)]
    public sealed class StoreyColorSettings : ScriptableObject
    {
        public const string ResourcesName = "StoreyColorSettings";

        [Header("Defaults for styles that leave a colour out")]
        [ColorReference] public SerializableGUID door;
        [ColorReference] public SerializableGUID rail;
        [Tooltip("Lift door frames and car trim.")]
        [ColorReference] public SerializableGUID metal;
        [ColorReference] public SerializableGUID ceiling;
        [ColorReference] public SerializableGUID liftInterior;
        [ColorReference] public SerializableGUID liftButton;
        [ColorReference] public SerializableGUID detailMetal;
        [ColorReference] public SerializableGUID grille;
        [ColorReference] public SerializableGUID detailDark;
        [ColorReference] public SerializableGUID dish;

        [Header("Suggested colours per field (the prototype's swatch rows)")]
        [ColorReference] public SerializableGUID[] walls = new SerializableGUID[0];
        [ColorReference] public SerializableGUID[] trims = new SerializableGUID[0];
        [ColorReference] public SerializableGUID[] roofs = new SerializableGUID[0];
        [ColorReference] public SerializableGUID[] interiors = new SerializableGUID[0];
        [ColorReference] public SerializableGUID[] floors = new SerializableGUID[0];

        public static StoreyColorSettings? Load() => Resources.Load<StoreyColorSettings>(ResourcesName);

        public StyleDefaults ToDefaults()
        {
            var d = new StyleDefaults();
            string Or(SerializableGUID id, string fallback) => id.valid ? ColorPipelinePalette.IdOf(id) : fallback;
            d.door = Or(door, d.door); d.rail = Or(rail, d.rail); d.metal = Or(metal, d.metal); d.ceiling = Or(ceiling, d.ceiling);
            d.liftInterior = Or(liftInterior, d.liftInterior); d.liftButton = Or(liftButton, d.liftButton);
            d.detailMetal = Or(detailMetal, d.detailMetal); d.grille = Or(grille, d.grille); d.detailDark = Or(detailDark, d.detailDark); d.dish = Or(dish, d.dish);
            return d;
        }
    }
}
