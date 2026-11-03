using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// A NavMesh bake takes in every building (docs/PLAY.md §5.1): it takes the colliders in the scene as it starts, and in
    /// edit mode a site had none unless Colliders in edit mode was on, and then only near the Scene view's camera, so the
    /// switch was turned on by hand before every bake and off after. While <c>StoreySite.EveryCollider</c> is held, every
    /// site makes every building's collider, in edit mode too; the authoring package holds it while a NavMesh Surface can
    /// be baked. All of it only compiles in Unity, so it is read from its source. Storey's own.
    /// </summary>
    public class NavMeshBakeTests
    {
        static string Text(string package, params string[] path) => File.ReadAllText(UnityAsset.Package(package, path));

        static string Method(string text, string start, string next)
        {
            int a = text.IndexOf(start, StringComparison.Ordinal); Assert.True(a > 0, start);
            int b = text.IndexOf(next, a + start.Length, StringComparison.Ordinal); Assert.True(b > a, next);
            return text.Substring(a, b - a);
        }

        [Fact]
        public void WhileHeldEverySiteMakesEveryColliderAndAfterItsOwnSwitchesDecide()
        {
            var site = Text("com.triband.storey", "Unity", "Rendering", "StoreySite.cs");
            // the first hold makes them at once, before it is handed out; the last given back puts every site back
            var every = Method(site, "public static IDisposable EveryCollider()", "// the handles EveryCollider");
            Assert.True(every.IndexOf("holds.Add(hold);", StringComparison.Ordinal) < every.IndexOf("if (holds.Count == 1) ApplyColliders();", StringComparison.Ordinal));
            Assert.Contains("public void Dispose() { if (holds.Remove(this) && holds.Count == 0) ApplyColliders(); }", site);
            // one site throwing loses no hold
            Assert.Contains("try { s.SetColliders(); }", Method(site, "static void ApplyColliders()", "/// <summary>"));
            // held: colliders in edit mode too, every one of them; never where the site makes none
            var set = Method(site, "void SetColliders()", "/// <summary>");
            Assert.Contains("bool every = generateColliders && holds.Count > 0;", set);
            Assert.Contains("site.Colliders = generateColliders && (Application.isPlaying || collidersInEditMode || every);", set);
            Assert.Contains("site.AllColliders = (collidersAtLoad && generateColliders && Application.isPlaying) || every;", set);
            // and the frame sets them the same way: nowhere else
            Assert.Single(Regex.Matches(site, @"site\.Colliders ="));
            Assert.Single(Regex.Matches(site, @"site\.AllColliders ="));
            Assert.Contains("SetColliders();", Method(site, "public void Refresh()", "/// <summary>"));
        }

        [Fact]
        public void EveryColliderIsMadeInEditModeAsInPlayMode()
        {
            var r = Text("com.triband.storey", "Unity", "Rendering", "SiteRenderer.cs");
            // nothing on the way to them asks for Play mode: made on the build threads, cooked and assigned before it returns
            var make = Method(r, "public void MakeColliders()", "// the buildings whose collision meshes MakeColliders");
            Assert.DoesNotContain("isPlaying", make);
            Assert.DoesNotContain("isPlaying", Method(r, "public bool AllColliders", "/// <summary>"));
            Assert.Contains("if (!Application.isPlaying) FinishBake(true);", Method(r, "void BuildColliders()", "/// <summary>"));
        }

        [Fact]
        public void SwitchingCollidersOffTakesTheObjectsThatHeldThem()
        {
            // a building's object is made for its collider alone (docs/PERFORMANCE.md §3.4): every building had one left
            // over, empty, after each bake
            var r = Text("com.triband.storey", "Unity", "Rendering", "SiteRenderer.cs");
            var colliders = Method(r, "get => colliders;", "bool allColliders;");
            Assert.Contains("foreach (var b in built.Values) { RemoveCollider(b); b.collisionMade = null; if (b.root != null) { Kill(b.root); b.root = null; } }", colliders);
        }

        [Fact]
        public void TheEditorHoldsThemWhileASurfaceCanBeBakedAndNeverInPlayMode()
        {
            var bake = Text("com.triband.storey.authoring", "Editor", "Navigation", "StoreyNavMeshBake.cs");
            Assert.Contains("[InitializeOnLoad]", bake);
            // selected, shown in an inspector (a locked one too) or baking; never in Play mode, whose Bake is off
            Assert.Contains("bool want = !EditorApplication.isPlayingOrWillChangePlaymode && (selected || shown.Count > 0 || Baking());", bake);
            Assert.Contains("if (want) hold ??= StoreySite.EveryCollider();", bake);
            Assert.Contains("Selection.selectionChanged +=", bake);
            Assert.Contains("UnityEditor.Editor.finishedDefaultHeaderGUI += Header;", bake);
            Assert.Contains("assets.IsSurfaceBaking(s)", bake);
            // let go before Play mode's scene loads, which with domain reload off would make every collider at load
            Assert.Contains("if (s == PlayModeStateChange.ExitingEditMode) Release();", bake);
        }

        [Fact]
        public void TheEditorsPartIsBuiltOnlyWithTheAiNavigationPackage()
        {
            var asmdef = JsonDocument.Parse(Text("com.triband.storey.authoring", "Editor", "Navigation", "Triband.Storey.Editor.Navigation.asmdef")).RootElement;
            Assert.Equal(new[] { "Editor" }, asmdef.GetProperty("includePlatforms").EnumerateArray().Select(e => e.GetString()).ToArray());
            var define = asmdef.GetProperty("versionDefines").EnumerateArray().Single(v => v.GetProperty("name").GetString() == "com.unity.ai.navigation").GetProperty("define").GetString();
            Assert.Contains(define, asmdef.GetProperty("defineConstraints").EnumerateArray().Select(e => e.GetString()));
            // optional: the authoring package doesn't depend on it
            var manifest = JsonDocument.Parse(Text("com.triband.storey.authoring", "package.json")).RootElement;
            Assert.False(manifest.GetProperty("dependencies").TryGetProperty("com.unity.ai.navigation", out _));
        }
    }
}
