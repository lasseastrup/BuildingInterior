#nullable enable
using System.IO;
using Triband.Storey.Unity;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Triband.Storey.Editor
{
    /// <summary>
    /// Imports a <c>.storey</c> file (the prototype's JSON layout) as a
    /// <see cref="StoreyDocumentAsset"/>. Nothing is generated at import; generation is a
    /// separate, explicit step (Plan §4.1), so importing a city of layouts costs a parse each.
    /// </summary>
    [ScriptedImporter(Version, Extension)]
    public sealed class StoreyImporter : ScriptedImporter
    {
        public const int Version = 1;
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
                asset.Set(text, summary);
                foreach (var key in summary.unknownKeys)
                    ctx.LogImportWarning($"{ctx.assetPath}: unknown key {key}; the package's data model lags the prototype.");
            }
            catch (System.FormatException e)
            {
                asset.Set(text, new DocumentSummary());
                ctx.LogImportError($"{ctx.assetPath}: {e.Message}");
            }
            ctx.AddObjectToAsset("document", asset);
            ctx.SetMainObject(asset);
        }
    }
}
