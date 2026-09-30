#nullable enable
using System.Collections.Generic;

namespace Triband.Storey
{
    /// <summary>
    /// What an inspector shows about a <c>.storey</c> file without parsing it again:
    /// computed at import, stored beside the text.
    /// </summary>
    public sealed class DocumentSummary
    {
        public int documentVersion;
        public int buildingCount;
        public List<string> buildingIds = new List<string>();
        public List<string> buildingNames = new List<string>();
        public List<int> floorCounts = new List<int>();
        public int walkInCount;
        public int coreCount;
        public int detailCount;
        /// <summary>Keys the reader did not recognise; a non-empty list means the model lags the prototype.</summary>
        public List<string> unknownKeys = new List<string>();

        public static DocumentSummary Of(PrototypeJson.ReadResult r)
        {
            var s = new DocumentSummary { documentVersion = r.Document.v, buildingCount = r.Document.buildings.Count, unknownKeys = r.Unknown };
            foreach (var b in r.Document.buildings)
            {
                s.buildingIds.Add(b.id);
                s.buildingNames.Add(b.name);
                s.floorCounts.Add(b.floors.Count);
                if (b.interior) s.walkInCount++;
                s.coreCount += b.shafts.Count;
                s.detailCount += b.details.Count;
            }
            return s;
        }
    }
}
