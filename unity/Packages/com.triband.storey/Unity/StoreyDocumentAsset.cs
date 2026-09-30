#nullable enable
using UnityEngine;

namespace Triband.Storey.Unity
{
    /// <summary>
    /// A <c>.storey</c> file in the project: the prototype's JSON layout, imported.
    /// </summary>
    /// <remarks>
    /// The asset keeps the document as text and parses it on demand. Unity's serializer cannot
    /// hold the model's optional sub-objects (a floor with no style would come back with a
    /// default one), and the model must stay engine-free to be testable headlessly, so the
    /// text is the serialized form and <see cref="Document"/> is a cache. The summary is
    /// computed at import for the inspector.
    /// </remarks>
    public sealed class StoreyDocumentAsset : ScriptableObject
    {
        [SerializeField, TextArea(3, 12)] internal string json = "";
        [SerializeField] internal int documentVersion;
        [SerializeField] internal int buildingCount;
        [SerializeField] internal string[] buildingNames = System.Array.Empty<string>();
        [SerializeField] internal int walkInCount;
        [SerializeField] internal int coreCount;
        [SerializeField] internal int detailCount;
        [SerializeField] internal string[] unknownKeys = System.Array.Empty<string>();

        StoreyDocument? cached;

        public string Json => json;
        public int BuildingCount => buildingCount;
        public string[] BuildingNames => buildingNames;
        /// <summary>Keys in the file the model does not know. Non-empty means the package lags the prototype.</summary>
        public string[] UnknownKeys => unknownKeys;

        /// <summary>The parsed layout, parsed once per asset load.</summary>
        public StoreyDocument Document => cached ??= PrototypeJson.Read(json).Document;

        internal void Set(string text, DocumentSummary s)
        {
            json = text;
            documentVersion = s.documentVersion;
            buildingCount = s.buildingCount;
            buildingNames = s.buildingNames.ToArray();
            walkInCount = s.walkInCount;
            coreCount = s.coreCount;
            detailCount = s.detailCount;
            unknownKeys = s.unknownKeys.ToArray();
            cached = null;
        }
    }
}
