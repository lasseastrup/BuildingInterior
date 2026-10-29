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
        /// <summary>Follows the baked format's version: a new format imports every layout again.</summary>
        public const int Version = 1 + BakedLayout.Version;
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
