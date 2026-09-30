#nullable enable
namespace Triband.Storey
{
    /// <summary>
    /// Version constants for the package and its data format.
    /// </summary>
    /// <remarks>
    /// <see cref="DataFormat"/> is the version stamped into every building asset
    /// (<c>BuildingData.version</c>). It is bumped whenever the serialized layout changes
    /// and the importer's migration code keys off it, so scenes saved with an older
    /// package keep loading after a MINOR package bump (Plan §3, "Versioning").
    /// It is independent of the package version in package.json.
    /// </remarks>
    public static class StoreyVersion
    {
        /// <summary>Data format version written into building assets.</summary>
        public const int DataFormat = 1;

        /// <summary>
        /// Version of the prototype JSON schema this package can import
        /// (the prototype's <c>state.buildings[]</c> layout, SPEC §3; the prototype
        /// stores it under the local-storage key <c>storey-builder-v9</c>).
        /// </summary>
        public const int PrototypeSchema = 9;
    }
}
