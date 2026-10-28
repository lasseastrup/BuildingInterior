using System;
using System.Linq;
using System.Text.RegularExpressions;
using Triband.Storey.Edit;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// A building without colliders (<see cref="BuildingData.colliders"/>, docs/PLAY.md §5), for the old BuildingGenerator's
    /// buildings with <c>_generateColliders</c> off: written only when off, read back from the JSON and the baked layout,
    /// switched by one edit like any other, and passed over by every path the site makes a collider by, while whatever
    /// waits for colliders takes it as ready. The site's half only compiles in Unity, so that is read from its source.
    /// Storey's own.
    /// </summary>
    public class CollidersOffTests
    {
        static string Text(string package, params string[] path) => System.IO.File.ReadAllText(UnityAsset.Package(package, path));

        [Fact]
        public void ABuildingHasCollidersUnlessItsLayoutSaysOtherwise()
        {
            Assert.True(new BuildingData().colliders);
            var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document;
            Assert.All(d.buildings, b => Assert.True(b.colliders));
            // on, the key isn't written: every layout reads and writes as before
            Assert.DoesNotContain("\"colliders\"", PrototypeJson.Write(d));
            Assert.All(BakedLayout.Of(BakedLayout.Bake(d))!.Document().buildings, b => Assert.True(b.colliders));
        }

        [Fact]
        public void CollidersOffIsWrittenAsFalseAndComesBackFromJsonAndTheBakedLayout()
        {
            var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document;
            int off = d.buildings.FindIndex(b => b.name == "Row House");
            d.buildings[off].colliders = false;
            var json = PrototypeJson.Write(d);
            Assert.Single(Regex.Matches(json, "\"colliders\":false"));
            var read = PrototypeJson.Read(json);
            Assert.Empty(read.Unknown);
            Assert.Equal(d.buildings.Select(b => b.colliders), read.Document.buildings.Select(b => b.colliders));
            Assert.False(PrototypeJson.ReadBuilding(PrototypeJson.Write(d.buildings[off])).colliders);
            var baked = BakedLayout.Of(BakedLayout.Bake(read.Document))!;
            Assert.Equal(d.buildings.Select(b => b.colliders), baked.Document().buildings.Select(b => b.colliders));
            Assert.False(baked.Building(off).colliders);
            // as a layout the old generator's converter writes it
            var one = PrototypeJson.Read("{\"v\":2,\"buildings\":[{\"id\":\"a\",\"colliders\":false},{\"id\":\"b\",\"colliders\":true}]}");
            Assert.Empty(one.Unknown);
            Assert.Equal(new[] { false, true }, one.Document.buildings.Select(b => b.colliders));
        }

        /// <summary>Format 6: the field is new since format 5, which projects have imported, so every layout is baked again.</summary>
        [Fact]
        public void TheBakedFormatIsSixSoEveryLayoutIsBakedAgain()
        {
            Assert.Equal(6, BakedLayout.Version);
            var bytes = BakedLayout.Bake(PrototypeJson.Read(Fixtures.Text("demo.json")).Document);
            Assert.Equal(6, bytes[4]);
            var five = (byte[])bytes.Clone(); five[4] = 5;
            Assert.Null(BakedLayout.Of(five));
        }

        /// <summary>The building card's switch is an edit like any other (docs/EDITOR.md §2): one change, undone as one.</summary>
        [Fact]
        public void TurningThemOffIsOneEditThatBuildsTheBuildingAgain()
        {
            var s = new EditSession(Fixtures.Text("demo.json"));
            string before = s.Text;
            var tower = s.Document.buildings.First(b => b.name == "Westgate Tower");   // it stands alone
            tower.colliders = false;
            var c = s.Commit();
            Assert.False(c.structural);
            Assert.Equal(new[] { tower.id }, c.rebuild.ToArray());
            Assert.Contains("\"colliders\":false", s.Text);
            var back = s.Load(before);
            Assert.Equal(new[] { tower.id }, back.rebuild.ToArray());
            Assert.True(s.Document.buildings.First(b => b.id == tower.id).colliders);
            var card = Text("com.triband.storey.authoring", "Editor", "StoreySiteEditor.cs");
            Assert.Contains("e.ApplyTo(col ? \"Colliders on\" : \"Colliders off\", bb => { bb.colliders = col; return true; });", card);
            Assert.Contains("bool col = EditorGUILayout.Toggle(CollidersLabel, sb.colliders);", Method(card, "void BuildingBar(StoreyEdit e)", "static readonly GUIContent CollidersLabel"));
        }

        static string Method(string text, string start, string next)
        {
            int a = text.IndexOf(start, StringComparison.Ordinal); Assert.True(a >= 0, start);
            int b = text.IndexOf(next, a, StringComparison.Ordinal); Assert.True(b > a, next);
            return text.Substring(a, b - a);
        }

        /// <summary>
        /// Every way the site comes to make a collider (with a LOD0 built on the main thread or a build thread, a kept LOD0 shown
        /// again, the switch turned on, every collider at load, the fixed LOD) passes over a building with its colliders off,
        /// and the one place a collider is cooked turns it away too. Whatever waits for colliders (<c>CollidersReadyAround</c>,
        /// the props) takes it as ready: none is coming.
        /// </summary>
        [Fact]
        public void EveryPathTheSiteMakesCollidersByPassesOverIt()
        {
            var r = Text("com.triband.storey", "Unity", "Rendering", "SiteRenderer.cs");
            Assert.Contains("var bt = new Built { id = b.id, idx = blockStart + site!.IndexOf(b), collider = b.colliders };", r);
            // the collision mesh made with LOD0, on the main thread and on a build thread
            Assert.Contains("made = Generate(site!, b, bt.idx, which, (blockStart, blockSize), colliders && bt.collider && bt.collision == null)!;", r);
            Assert.Contains("bool collider = which == 0 && colliders && bt.collider && bt.collision == null;", Method(r, "void StartJob(", "/// <summary>"));
            // asked for: with a LOD0, a kept LOD0 shown again, the fixed LOD, the switch turned on
            Assert.Contains("if (lods != null && colliders && bt.collider && bt.collision == null) colliderPending.Add(bt.id);", Method(r, "void EndDetail(", "/// <summary>"));
            Assert.Contains("if (colliders && cb.collider && cb.collision == null && cb.shape != null && e.Visible(0) && !Baking(cb)) colliderPending.Add(cb.id);", r);
            Assert.Contains("if (colliders && bt.collider) colliderPending.Add(b.id);", Method(r, "void Build(BuildingData b, Lod2Mesh? made = null)", "int[] WriteRows("));
            Assert.Contains("foreach (var bt in built.Values) if (bt.collider) colliderPending.Add(bt.id);", Method(r, "public bool Colliders", "bool allColliders;"));
            Assert.Equal(5, Regex.Matches(r, @"colliderPending\.Add\(").Count);   // those four, and MakeColliders' below
            // every collider at load: neither made nor waited for
            var make = Method(r, "public void MakeColliders()", "// the buildings whose collision meshes MakeColliders");
            Assert.True(make.IndexOf("if (bt.collision != null || !bt.collider) continue;", StringComparison.Ordinal) is int gate && gate > 0
                && gate < make.IndexOf("colliderPending.Add(bt.id);", StringComparison.Ordinal) && gate < make.IndexOf("making.Add(bt);", StringComparison.Ordinal));
            // the one place a collider's mesh goes to be cooked (and from there onto a MeshCollider), after the gate
            var cook = Method(r, "void BuildColliders()", "/// <summary>");
            Assert.Single(Regex.Matches(r, @"\bbaking\.Add\("));
            int turned = cook.IndexOf("if (!built.TryGetValue(id, out var bt) || !bt.collider || (bt.shape == null && bt.collisionMade == null) || site == null) continue;", StringComparison.Ordinal);
            Assert.True(turned > 0 && turned < cook.IndexOf("CollisionMesh.Build(", StringComparison.Ordinal) && turned < cook.IndexOf("baking.Add(", StringComparison.Ordinal), "BuildColliders turns it away first");
            // ready: none is coming
            Assert.Contains("if (bt.collision != null || !bt.collider || !(site.ById(bt.id) is BuildingData b)) continue;", Method(r, "public bool CollidersUpNear(", "/// <summary>"));
            Assert.Contains("internal bool ColliderUp(int idx) => byIdx.TryGetValue(idx, out var b) && (b.collision != null || !b.collider);", r);
            Assert.Contains("return site.CollidersUpNear(", Method(Text("com.triband.storey", "Unity", "Rendering", "StoreySite.cs"), "public bool CollidersReadyAround(", "readonly List<"));
        }
    }
}
