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
        Lod.LayoutParse? parse;

        public string Json => json;
        public int BuildingCount => buildingCount;
        public string[] BuildingNames => buildingNames;
        /// <summary>Keys in the file the model does not know. Non-empty means the package lags the prototype.</summary>
        public string[] UnknownKeys => unknownKeys;

        /// <summary>
        /// The parsed layout, parsed once per asset load, on the caller's thread: one being parsed on a build thread
        /// (<see cref="DocumentOffMainThread"/>) is waited for.
        /// </summary>
        public StoreyDocument Document
        {
            get
            {
                if (cached == null)
                {
                    cached = parse != null && ReferenceEquals(parse.Text, json) ? parse.Wait() : PrototypeJson.Read(json).Document;
                    parse = null;
                }
                return cached;
            }
        }

        /// <summary>The parsed layout when it is parsed already, otherwise null: nothing is parsed.</summary>
        public StoreyDocument? Parsed => cached;

        /// <summary>
        /// The parsed layout, parsed on a build thread (<see cref="Lod.LayoutParse"/>): null until it is done, the parse
        /// started by the first call. Ask each frame. A site loading over frames in Play mode reads its layouts so, and a
        /// district streamed in isn't held up a frame by its text (docs/CITY.md §2).
        /// </summary>
        public StoreyDocument? DocumentOffMainThread()
        {
            if (cached != null) return cached;
            if (parse == null || !ReferenceEquals(parse.Text, json)) parse = new Lod.LayoutParse(json);
            var d = parse.Document;
            if (d != null) { cached = d; parse = null; }
            return d;
        }

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
            cached = null; parse = null;
        }
    }
}
