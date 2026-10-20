#nullable enable
using System.Collections.Generic;
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
    /// <para>The importer bakes the parsed layout too (<see cref="Baked"/>, docs/PERFORMANCE.md §6.4), which is what
    /// <see cref="Document"/> reads when it is there: the 3,000-building test city in 4 ms where its text took 56–65 ms,
    /// with no garbage but the model (headless). In a player the text goes when the asset loads, and <see cref="Json"/> is
    /// empty there: nothing reads it.</para>
    /// <para>In Play mode the sites using a layout say so (<see cref="Use"/>, <see cref="Release"/>): with none, the parse
    /// is let go once the layout falls off the last few no site uses (<see cref="KeepUnused"/>), and parsed again when
    /// asked for. A district's scene unloaded doesn't unload its assets, so every district visited kept its parse for the
    /// session (docs/PERFORMANCE.md §6.5). What it is read from stays, the baked bytes (or the text, with none): a
    /// district's scene loaded again finds this same object, and a player can't read the file back from disk.</para>
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
        [SerializeField, HideInInspector] internal byte[] baked = System.Array.Empty<byte>();

        // the parse, and what it was read from (Source). A reimport writes the new text and bytes into this same object and
        // clears nothing else, so the parse is of what the asset holds only while that is the same. With no domain reload
        // before Play, the parse made before an edit was kept, and Play mode showed the layout as it was before it
        StoreyDocument? cached; object? cachedFrom;
        Lod.LayoutParse? parse;
        BakedLayout? bakedLayout; byte[]? bakedFrom;

        /// <summary>
        /// The layout as the importer baked it, or null when the asset has none: imported by an older Storey, or baked in
        /// another format, which Unity imports again. A neighbouring district's is read for the buildings along the edge
        /// alone (docs/PERFORMANCE.md §6.3).
        /// </summary>
        public BakedLayout? Baked
        {
            get
            {
                if (!ReferenceEquals(bakedFrom, baked)) { bakedLayout = BakedLayout.Of(baked); bakedFrom = baked; }
                return bakedLayout;
            }
        }

        // in a player a baked layout is read from its bytes: the text, two bytes a character for as long as the asset is
        // loaded, goes as it loads. The editor keeps it: it edits and saves the text
        void OnEnable()
        {
            if (!Application.isEditor && Baked != null) json = "";
        }

        // what the layout is read from: its baked bytes when it has them, its text otherwise
        object Source => (object?)Baked ?? json;
        StoreyDocument Read() => Baked is BakedLayout b ? b.Document() : PrototypeJson.Read(json).Document;

        public string Json => json;
        public int BuildingCount => buildingCount;
        public string[] BuildingNames => buildingNames;
        /// <summary>Keys in the file the model does not know. Non-empty means the package lags the prototype.</summary>
        public string[] UnknownKeys => unknownKeys;

        /// <summary>
        /// The parsed layout, read once per asset load and again after a reimport (from <see cref="Baked"/> when there is
        /// one), on the caller's thread: one being read on a build thread (<see cref="DocumentOffMainThread"/>) is waited for.
        /// </summary>
        public StoreyDocument Document
        {
            get
            {
                var from = Source;
                if (cached == null || !ReferenceEquals(cachedFrom, from))
                {
                    cached = parse != null && ReferenceEquals(parse.Source, from) ? parse.Wait() : Read();
                    cachedFrom = from; parse = null;
                }
                return cached;
            }
        }

        /// <summary>The parsed layout when it is parsed already, otherwise null: nothing is parsed.</summary>
        public StoreyDocument? Parsed => cached != null && ReferenceEquals(cachedFrom, Source) ? cached : null;

        /// <summary>
        /// The parsed layout, parsed on a build thread (<see cref="Lod.LayoutParse"/>): null until it is done, the parse
        /// started by the first call. Ask each frame. A site loading over frames in Play mode reads its layouts so, and a
        /// district streamed in isn't held up a frame by its text (docs/CITY.md §2).
        /// </summary>
        public StoreyDocument? DocumentOffMainThread()
        {
            var from = Source;
            if (cached != null && ReferenceEquals(cachedFrom, from)) return cached;
            if (parse == null || !ReferenceEquals(parse.Source, from)) parse = from is BakedLayout b ? new Lod.LayoutParse(b) : new Lod.LayoutParse(json);
            var d = parse.Document;
            if (d != null) { cached = d; cachedFrom = from; parse = null; }
            return d;
        }

        // the sites using each layout in Play mode, and the last few no site uses, kept parsed for a return
        static readonly Lod.Users<StoreyDocumentAsset> users = new Lod.Users<StoreyDocumentAsset>(2);
        static readonly List<StoreyDocumentAsset> letGo = new List<StoreyDocumentAsset>();

        // a Play session without a domain reload starts with no users
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ForgetUsers() => users.Clear();

        /// <summary>
        /// How many layouts no site uses keep their parse, the last let go (2 unless set): a player turning back into a
        /// district they just left finds it parsed. Fewer hold less memory; more parse less on a return.
        /// </summary>
        public static int KeepUnused
        {
            get => users.Keep;
            set { users.Keep = value; users.Trim(letGo); LetGo(); }
        }

        /// <summary>How many users this layout has now: sites showing it, or using it as a neighbour's (Play mode).</summary>
        public int Users => users.Count(this);

        /// <summary>
        /// One more user of this layout: its parse is kept until each user has called <see cref="Release"/>. Storey's sites
        /// call both in Play mode, for their own layout and their neighbours'. Code of a project's own reading
        /// <see cref="Document"/> over frames can too: otherwise the parse may be let go meanwhile, and asking again parses
        /// it again, a new document.
        /// </summary>
        public void Use() => users.Use(this);

        /// <summary>One user fewer. With none left the parse is kept while this is among the last <see cref="KeepUnused"/> layouts let go.</summary>
        public void Release()
        {
            users.Release(this, letGo);
            LetGo();
        }

        static void LetGo()
        {
            // a parse running finishes on its thread, unread; the baked layout's reader goes with its strings, made again when asked for
            foreach (var a in letGo) { a.cached = null; a.cachedFrom = null; a.parse = null; a.bakedLayout = null; a.bakedFrom = null; }
            letGo.Clear();
        }

        internal void Set(string text, DocumentSummary s, byte[] bakedBytes)
        {
            json = text; baked = bakedBytes; bakedFrom = null; bakedLayout = null;
            documentVersion = s.documentVersion;
            buildingCount = s.buildingCount;
            buildingNames = s.buildingNames.ToArray();
            walkInCount = s.walkInCount;
            coreCount = s.coreCount;
            detailCount = s.detailCount;
            unknownKeys = s.unknownKeys.ToArray();
            cached = null; cachedFrom = null; parse = null;
        }
    }
}
