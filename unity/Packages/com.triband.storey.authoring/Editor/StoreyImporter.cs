#nullable enable
using System.IO;
using Triband.Storey.Unity;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Triband.Storey.Editor
{
    /// <summary>
    /// Imports a <c>.storey</c> file (the prototype's JSON layout) as a
    /// <see cref="StoreyDocumentAsset"/>, with the layout baked (<see cref="BakedLayout"/>): what the runtime reads, in a
    /// fraction of the text's time (docs/PERFORMANCE.md §6.4). Nothing is generated at import; generation is a separate,
    /// explicit step (Plan §4.1), so importing a city of layouts costs a parse and a bake each.
    /// </summary>
    [ScriptedImporter(Version, Extension)]
    public sealed class StoreyImporter : ScriptedImporter
    {
        /// <summary>
        /// Follows the baked format's version and the reader's: a new format, or a layout read otherwise, imports every
        /// layout again.
        /// </summary>
        public const int Version = 1 + BakedLayout.Version + Reading;

        /// <summary>
        /// Raised when <see cref="PrototypeJson.Read"/> reads a layout otherwise, so the bakes made before are made again.
        /// 1: buildings that share an id are each given one of their own, and the counter goes past every id
        /// (<see cref="LayoutIds"/>), where a bake held them as the file had them.
        /// </summary>
        const int Reading = 1;
        public const string Extension = "storey";

        public override void OnImportAsset(AssetImportContext ctx)
        {
            string text = File.ReadAllText(ctx.assetPath);
            var asset = ScriptableObject.CreateInstance<StoreyDocumentAsset>();
            asset.name = Path.GetFileNameWithoutExtension(ctx.assetPath);
            try
            {
                var read = PrototypeJson.Read(text);
                var summary = DocumentSummary.Of(read);
                byte[] baked;
                try { baked = BakedLayout.Bake(read.Document); }
                catch (System.Exception e)
                {
                    // the text is read instead, as before baking
                    baked = System.Array.Empty<byte>();
                    ctx.LogImportWarning($"{ctx.assetPath}: not baked ({e.Message}); the layout is read from its text.");
                }
                asset.Set(text, summary, baked);
                foreach (var key in summary.unknownKeys)
                    ctx.LogImportWarning($"{ctx.assetPath}: unknown key {key}: this version of Storey doesn't know it, and leaves it out.");
                // read so every time; the file has the new ids once the layout is opened for editing and saved (StoreyEdit.Begin)
                foreach (var (index, was, now) in read.Renamed)
                    ctx.LogImportWarning($"{ctx.assetPath}: buildings[{index}] ({read.Document.buildings[index].name}) has the id \"{was}\", which a building before it has, " +
                        $"so it is read as \"{now}\". Edit layout in a Storey Site inspector, and the next save writes the new id to the file.");
            }
            catch (System.FormatException e)
            {
                asset.Set(text, new DocumentSummary(), System.Array.Empty<byte>());
                ctx.LogImportError($"{ctx.assetPath}: {e.Message}");
            }
            ctx.AddObjectToAsset("document", asset);
            ctx.SetMainObject(asset);
        }
    }
}
