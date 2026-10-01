using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// The packages' own metadata, checked without an editor.
    ///
    /// <para>Everything else in this suite tests what the code computes. This tests whether
    /// Unity will agree to compile and import it at all, which is a separate failure mode
    /// and, from here, an invisible one: the headless build goes through a <c>.csproj</c>
    /// that ignores every <c>.asmdef</c> and every line of <c>package.json</c>, so a package
    /// can pass every behavioural test and still refuse to import.</para>
    ///
    /// <para>The rules are inherited from the roads packages, where each one was written after
    /// the mistake it names had shipped: a duplicate test-runner reference that failed the whole
    /// package import, assembly definitions over empty folders, a sample entry pointing at a
    /// folder that was never written, editor code compiled into a player, and <c>.meta</c>
    /// files missing from a git install.</para>
    /// </summary>
    public class PackageLayoutTests
    {
        public static IEnumerable<object[]> Packages() =>
            Layout.Packages.Select(p => new object[] { p.Name });

        // ---- assembly definitions -------------------------------------------------

        /// <summary>
        /// <c>optionalUnityReferences</c> is the deprecated switch whose whole behaviour is to
        /// add references you did not write down. The modern form states them:
        /// <c>precompiledReferences</c> with <c>overrideReferences: true</c>, and
        /// <c>defineConstraints: ["UNITY_INCLUDE_TESTS"]</c> to keep them out of players.
        /// </summary>
        [Theory, MemberData(nameof(Packages))]
        public void NoAssemblyUsesTheImplicitTestReferenceSwitch(string package)
        {
            foreach (var a in Layout.Get(package).Asmdefs)
            {
                Assert.False(a.Has("optionalUnityReferences"),
                    $"{a.Relative} uses optionalUnityReferences, which silently adds " +
                    $"{string.Join(" and ", Asmdef.ImpliedByTestAssemblies)}. State them in " +
                    "precompiledReferences with overrideReferences instead; the implicit form " +
                    "duplicates any you also list, and Unity fails the whole package import for it.");
            }
        }

        [Theory, MemberData(nameof(Packages))]
        public void NoAssemblyReferencesTheSameThingTwice(string package)
        {
            foreach (var a in Layout.Get(package).Asmdefs)
            {
                var all = a.References.Concat(a.PrecompiledReferences).Concat(a.ImpliedReferences).ToList();
                var twice = all.GroupBy(r => r, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
                Assert.True(twice.Count == 0, $"{a.Relative} has duplicate references: {string.Join(", ", twice)}");
            }
        }

        /// <summary>
        /// An assembly definition for a folder with no code in it: harmless in principle, and
        /// in practice the shape a half-finished layer leaves behind. "Its folder" means the
        /// folder and everything below it that no nested assembly definition has claimed,
        /// which is how Unity decides too.
        /// </summary>
        [Theory, MemberData(nameof(Packages))]
        public void EveryAssemblyHasSourceToCompile(string package)
        {
            var p = Layout.Get(package);
            foreach (var a in p.Asmdefs)
            {
                Assert.True(p.SourcesOwnedBy(a).Count > 0,
                    $"{a.Relative} defines an assembly for a folder containing no .cs files. " +
                    "Delete it until there is code, or the package ships an empty assembly.");
            }
        }

        /// <summary>
        /// And the converse. A <c>.cs</c> file in a package with no assembly definition at or
        /// above it is not compiled into anything (Unity's predefined assemblies do not take
        /// package code), so it is silently absent rather than broken, which is worse.
        /// </summary>
        [Theory, MemberData(nameof(Packages))]
        public void EverySourceFileBelongsToAnAssembly(string package)
        {
            var p = Layout.Get(package);
            var orphans = p.Sources.Where(f => p.OwnerOf(f) is null).Select(p.RelativeTo).ToList();
            Assert.True(orphans.Count == 0,
                "no assembly definition covers: " + string.Join(", ", orphans) +
                "; package code outside an .asmdef is not compiled at all.");
        }

        /// <summary>
        /// A reference to one of our own assemblies that no package in the set defines. Unity
        /// reports this as an unresolved reference at import; here it is a typo caught at
        /// commit time.
        /// </summary>
        [Fact]
        public void ReferencesToOurOwnAssembliesResolve()
        {
            var defined = Layout.Packages.SelectMany(p => p.Asmdefs).Select(a => a.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var a in Layout.Packages.SelectMany(p => p.Asmdefs))
            {
                foreach (var r in a.References.Where(r => r.StartsWith("Triband.", StringComparison.Ordinal) && !OptionalTribandAssemblies.ContainsKey(r)))
                {
                    Assert.True(defined.Contains(r),
                        $"{a.Relative} references {r}, which no .asmdef in any package defines " +
                        $"(defined: {string.Join(", ", defined.OrderBy(x => x, StringComparer.Ordinal))}).");
                }
            }
        }

        /// <summary>
        /// Triband assemblies outside this repository that an optional integration may reference, with the
        /// package whose presence gates it (Color Pipeline brings Triband Core). docs/COLOURS.md §3.7.
        /// </summary>
        private static readonly Dictionary<string, string> OptionalTribandAssemblies = new(StringComparer.Ordinal)
        {
            ["Triband.ColorPipeline.Runtime"] = "com.triband.colorpipeline",
            ["Triband.ColorPipeline.Editor"] = "com.triband.colorpipeline",
            ["Triband.Core.Runtime"] = "com.triband.colorpipeline",
        };

        /// <summary>
        /// An assembly referencing a package the manifest does not declare compiles only where that package
        /// happens to be installed. That is allowed for an optional integration and nowhere else: the assembly
        /// must be switched off, through <c>defineConstraints</c>, by a <c>versionDefines</c> entry on the
        /// package, and the manifest must not declare it (or it would not be optional).
        /// </summary>
        [Theory, MemberData(nameof(Packages))]
        public void OptionalIntegrationsAreGatedByTheirPackage(string package)
        {
            var p = Layout.Get(package);
            foreach (var a in p.Asmdefs)
            {
                foreach (var r in a.References.Where(OptionalTribandAssemblies.ContainsKey))
                {
                    string gate = OptionalTribandAssemblies[r];
                    var defines = a.VersionDefines.Where(v => v.name == gate).Select(v => v.define).ToList();
                    Assert.True(defines.Any(a.DefineConstraints.Contains),
                        $"{a.Relative} references {r} but is not gated: it needs a versionDefines entry on {gate} whose define is in its defineConstraints.");
                    Assert.False(p.Dependencies.Contains(gate), $"{p.Name}/package.json declares {gate}, so {a.Relative} is not an optional integration.");
                }
            }
        }

        /// <summary>
        /// An assembly in one package referencing an assembly in another must have that
        /// package in its <c>dependencies</c>, or a consumer installing only the first gets an
        /// unresolved-reference error naming an assembly rather than the package to install.
        /// </summary>
        [Fact]
        public void CrossPackageReferencesAreDeclaredDependencies()
        {
            var owner = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var p in Layout.Packages)
                foreach (var a in p.Asmdefs) owner[a.Name] = p.Name;

            foreach (var p in Layout.Packages)
            {
                var declared = p.Dependencies;
                foreach (var a in p.Asmdefs)
                {
                    foreach (var r in a.References)
                    {
                        if (!owner.TryGetValue(r, out var other) || other == p.Name) continue;
                        Assert.True(declared.Contains(other),
                            $"{p.Name}: {a.Relative} references {r} from {other}, which package.json does not declare.");
                    }
                }
            }
        }

        /// <summary>
        /// Unity treats a mismatch between an assembly definition's filename and the assembly
        /// name inside it as an error.
        /// </summary>
        [Theory, MemberData(nameof(Packages))]
        public void EachAssemblyDefinitionIsNamedAfterItsAssembly(string package)
        {
            foreach (var a in Layout.Get(package).Asmdefs)
                Assert.Equal(Path.GetFileNameWithoutExtension(a.Path), a.Name);
        }

        // ---- the split the whole port depends on -----------------------------------

        /// <summary>
        /// <c>noEngineReferences: true</c> on the generation assembly is the machine-checked
        /// form of "this code does not know Unity exists", and it is what lets the same
        /// sources build as a plain .NET library and be tested with no editor and no licence.
        /// </summary>
        [Fact]
        public void TheGenerationAssemblyCannotReachTheEngine()
        {
            var runtime = Layout.Runtime.Asmdefs.Single(a => a.Name == "Triband.Storey");
            Assert.True(runtime.NoEngineReferences,
                $"{runtime.Relative} must set noEngineReferences: true; it is what makes the engine-free half enforceable.");
            Assert.True(runtime.References.Count == 0,
                $"{runtime.Relative} references {string.Join(", ", runtime.References)}. The generation assembly is the bottom of the stack and depends on nothing.");
        }

        /// <summary>
        /// The headless project must compile the same folder the engine-free assembly
        /// definition governs. Two build systems over one source tree only works while they
        /// point at the same tree.
        /// </summary>
        [Fact]
        public void TheHeadlessBuildCompilesExactlyTheEngineFreeFolder()
        {
            var runtime = Layout.Runtime.Asmdefs.Single(a => a.NoEngineReferences);
            var csproj = Path.Combine(Layout.UnityRoot, "Headless", "Triband.Storey.Headless", "Triband.Storey.Headless.csproj");
            Assert.True(File.Exists(csproj), $"{csproj} is missing");

            var globs = CompileGlobs(csproj);
            Assert.True(globs.Count == 1, $"expected one <Compile Include> in the headless project, found {globs.Count}");
            var wildcard = globs[0].IndexOf("**", StringComparison.Ordinal);
            Assert.True(wildcard > 0, $"the headless glob {globs[0]} is not recursive");
            var rooted = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(csproj)!, globs[0].Substring(0, wildcard)));
            Assert.Equal(Normalise(Path.GetDirectoryName(runtime.Path)!), Normalise(rooted));
        }

        /// <summary>
        /// Nothing in any package is compiled by nothing at all. Every top-level source folder
        /// of every package is covered by a glob in the headless or the stub project; add a
        /// folder no build mentions and the first anyone hears of a typo in it is a bug report.
        /// </summary>
        [Theory, MemberData(nameof(Packages))]
        public void EverySourceFolderIsCompiledBySomething(string package)
        {
            var covered = new List<string>();
            foreach (var project in new[] { "Triband.Storey.Headless", "Triband.Storey.Stubs" })
            {
                var csproj = Directory.GetFiles(Path.Combine(Layout.UnityRoot, "Headless", project), "*.csproj").Single();
                foreach (var glob in CompileGlobs(csproj))
                {
                    int wildcard = glob.IndexOf('*');
                    if (wildcard < 0) continue;
                    covered.Add(Normalise(Path.Combine(Path.GetDirectoryName(csproj)!, glob.Substring(0, wildcard))));
                }
            }

            var p = Layout.Get(package);
            var folders = p.Sources.Select(f => p.RelativeTo(f).Split('/')[0]).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
            Assert.NotEmpty(folders);
            foreach (var folder in folders)
            {
                var full = Normalise(Path.Combine(p.Root, folder));
                Assert.True(covered.Any(c => full == c || full.StartsWith(c + "/", StringComparison.Ordinal)),
                    $"{p.Name}/{folder}/ holds source that no headless project compiles. Covered: {string.Join(", ", covered)}");
            }
        }

        /// <summary>
        /// An editor assembly without <c>includePlatforms: ["Editor"]</c> compiles perfectly,
        /// passes every test here, and puts <c>UnityEditor</c> code into a player build.
        /// </summary>
        [Theory, MemberData(nameof(Packages))]
        public void EditorAssembliesAreConstrainedToTheEditor(string package)
        {
            var p = Layout.Get(package);
            foreach (var a in p.Asmdefs)
            {
                var inEditorFolder = p.RelativeTo(a.Path).Split('/').Any(part => part == "Editor");
                if (!inEditorFolder && !a.Name.EndsWith(".Editor", StringComparison.Ordinal)) continue;
                Assert.Equal(new[] { "Editor" }, a.IncludePlatforms.ToArray());
            }
        }

        /// <summary>
        /// And nothing outside them so much as names the editor. A <c>#if UNITY_EDITOR</c>
        /// block in a runtime assembly is invisible to the rule above and moves a decision
        /// into a layer where nothing here can see it. Editor-only behaviour goes behind an
        /// installed delegate in the editor assembly instead.
        /// </summary>
        [Theory, MemberData(nameof(Packages))]
        public void OnlyEditorAssembliesNameTheEditor(string package)
        {
            var p = Layout.Get(package);
            foreach (var file in p.Sources)
            {
                var relative = p.RelativeTo(file);
                if (relative.Split('/').Any(part => part == "Editor") || relative.Contains("/ThirdParty/")) continue;
                var code = Code(File.ReadAllText(file));
                foreach (var banned in new[] { "UnityEditor", "UNITY_EDITOR", "PrefabUtility" })
                    Assert.False(code.Contains(banned, StringComparison.Ordinal), $"{relative} names {banned}, which belongs in an editor assembly");
            }
        }

        /// <summary>
        /// Referencing <c>Unity.Burst</c> without declaring <c>com.unity.burst</c> compiles in
        /// any project that happens to have the package, including the one it was developed
        /// in, and fails at import for a consumer who does not. Only Unity's own packages are
        /// checked, because those are the ones whose assembly name does not resemble their
        /// package name.
        /// </summary>
        [Theory, MemberData(nameof(Packages))]
        public void EveryUnityPackageReferencedIsAlsoDeclared(string package)
        {
            var p = Layout.Get(package);
            foreach (var a in p.Asmdefs)
            {
                foreach (var r in a.References)
                {
                    if (!UnityPackages.TryGetValue(r, out var dep)) continue;
                    Assert.True(p.Dependencies.Contains(dep),
                        $"{a.Relative} references {r}, but {p.Name}/package.json does not declare {dep}.");
                }
            }
        }

        /// <summary>
        /// Unity compiles assembly definitions with nullable reference types off and offers a
        /// package no way to change that, so sources that use <c>string?</c> without a
        /// <c>#nullable</c> directive emit a CS8632 per annotation into every consumer's
        /// console. The headless project enables nullable in its own settings, so the warnings
        /// never appear here unless this rule exists.
        /// </summary>
        [Theory, MemberData(nameof(Packages))]
        public void EverySourceFileDeclaresItsNullability(string package)
        {
            var p = Layout.Get(package);
            // Vendored code keeps its upstream text; it is compiled by the headless project, where nullable is a project setting.
            var missing = p.Sources.Where(f => !p.RelativeTo(f).Contains("/ThirdParty/") && !Declares(f)).Select(p.RelativeTo).ToList();
            Assert.True(missing.Count == 0, "no #nullable directive at the top of: " + string.Join(", ", missing));
        }

        private static bool Declares(string file)
        {
            foreach (var raw in File.ReadLines(file))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal)) continue;
                return line.StartsWith("#nullable", StringComparison.Ordinal);
            }
            return false;
        }

        private static readonly Dictionary<string, string> UnityPackages = new(StringComparer.Ordinal)
        {
            ["Unity.Mathematics"] = "com.unity.mathematics",
            ["Unity.Burst"] = "com.unity.burst",
            ["Unity.Collections"] = "com.unity.collections",
            ["Unity.InputSystem"] = "com.unity.inputsystem",
            ["Unity.Splines"] = "com.unity.splines",
            ["Unity.RenderPipelines.Core.Runtime"] = "com.unity.render-pipelines.core",
            ["Unity.RenderPipelines.Universal.Runtime"] = "com.unity.render-pipelines.universal",
            ["Unity.TextMeshPro"] = "com.unity.textmeshpro",
            ["UnityEngine.UI"] = "com.unity.ugui",
            ["Unity.Addressables"] = "com.unity.addressables",
        };

        // ---- what Unity needs beside every file ------------------------------------

        /// <summary>
        /// Every asset needs its sidecar <c>.meta</c>, committed. A package installed from a
        /// git URL lands in a folder Unity treats as immutable and cannot write one into; it
        /// logs "has no meta file, but it's in an immutable folder. The asset will be ignored."
        /// and ignores the asset. For a package of scripts that is the whole package, and it
        /// does not reproduce during development, where the embedded copy is mutable.
        /// </summary>
        [Theory, MemberData(nameof(Packages))]
        public void EveryAssetHasItsMetaFileCommitted(string package)
        {
            var p = Layout.Get(package);
            var missing = p.Assets.Where(f => !File.Exists(f + ".meta")).Select(p.RelativeTo).ToList();
            Assert.True(missing.Count == 0,
                $"{p.Name}: {missing.Count} assets have no .meta file: {string.Join(", ", missing.Take(8))}" +
                (missing.Count > 8 ? ", …" : "") + ". Run `python3 unity/tools/meta.py` to write the missing ones.");
        }

        [Theory, MemberData(nameof(Packages))]
        public void NoMetaFileIsLeftBehindWithoutItsAsset(string package)
        {
            var p = Layout.Get(package);
            var orphans = Directory.GetFiles(p.Root, "*.meta", SearchOption.AllDirectories)
                .Where(m => !File.Exists(m[..^".meta".Length]) && !Directory.Exists(m[..^".meta".Length]))
                .Select(p.RelativeTo).ToList();
            Assert.True(orphans.Count == 0, "meta files with no asset: " + string.Join(", ", orphans));
        }

        /// <summary>
        /// GUIDs well formed and distinct across all packages. A duplicate makes Unity reassign
        /// one of them arbitrarily, so the reference that breaks is not necessarily in the file
        /// that was copied.
        /// </summary>
        [Fact]
        public void EveryGuidIsWellFormedAndUnique()
        {
            var seen = new Dictionary<string, string>(StringComparer.Ordinal);
            int assets = 0;
            foreach (var p in Layout.Packages)
            {
                assets += p.Assets.Count;
                foreach (var meta in Directory.GetFiles(p.Root, "*.meta", SearchOption.AllDirectories).OrderBy(p.RelativeTo, StringComparer.Ordinal))
                {
                    var relative = p.Name + "/" + p.RelativeTo(meta);
                    var line = File.ReadAllLines(meta).FirstOrDefault(l => l.StartsWith("guid:", StringComparison.Ordinal));
                    Assert.NotNull(line);
                    var guid = line!.Substring("guid:".Length).Trim();
                    Assert.Matches("^[0-9a-f]{32}$", guid);
                    Assert.False(seen.TryGetValue(guid, out var first), $"{relative} and {first} share the GUID {guid}");
                    seen[guid] = relative;
                }
            }
            Assert.Equal(assets, seen.Count);
        }

        // ---- the manifest -----------------------------------------------------------

        [Theory, MemberData(nameof(Packages))]
        public void TheManifestSaysWhatUnityNeeds(string package)
        {
            var p = Layout.Get(package);
            var m = p.Manifest.RootElement;
            Assert.Equal(Path.GetFileName(p.Root.TrimEnd('/', '\\')), m.GetProperty("name").GetString());
            Assert.Matches(@"^[a-z0-9]+(\.[a-z0-9][a-z0-9\-_]*){2,}$", m.GetProperty("name").GetString());
            Assert.Matches(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.\-]+)?$", m.GetProperty("version").GetString());
            // Unity's format for the minimum editor version: 6.3 LTS is "6000.3".
            Assert.Matches(@"^\d{4}\.\d+$", m.GetProperty("unity").GetString());
            foreach (var required in new[] { "displayName", "description" })
                Assert.True(m.TryGetProperty(required, out var v) && !string.IsNullOrWhiteSpace(v.GetString()), $"{p.Name}/package.json has no {required}");
        }

        /// <summary>All three packages move together: one version, and the same minimum editor.</summary>
        [Fact]
        public void ThePackagesShareOneVersion()
        {
            var versions = Layout.Packages.Select(p => p.Manifest.RootElement.GetProperty("version").GetString()).Distinct().ToList();
            Assert.True(versions.Count == 1, "package versions differ: " + string.Join(", ", versions));
            foreach (var p in Layout.Packages)
            {
                if (!p.Manifest.RootElement.TryGetProperty("dependencies", out var deps)) continue;
                foreach (var d in deps.EnumerateObject().Where(d => d.Name.StartsWith("com.triband.", StringComparison.Ordinal)))
                    Assert.Equal(versions[0], d.Value.GetString());
            }
        }

        /// <summary>
        /// A <c>samples</c> entry whose path does not exist is advertised in the Package
        /// Manager window as an Import button that does nothing.
        /// </summary>
        [Theory, MemberData(nameof(Packages))]
        public void EveryAdvertisedSampleExists(string package)
        {
            var p = Layout.Get(package);
            if (!p.Manifest.RootElement.TryGetProperty("samples", out var samples)) return;
            foreach (var s in samples.EnumerateArray())
            {
                var path = s.GetProperty("path").GetString()!;
                Assert.True(Directory.Exists(Path.Combine(p.Root, path)), $"{p.Name}/package.json advertises the sample at {path}, which does not exist.");
            }
        }

        /// <summary>The test project's manifest lists every package under testables, and pins what the packages depend on.</summary>
        [Fact]
        public void TheProjectManifestKnowsEveryPackage()
        {
            var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Layout.UnityRoot, "Packages", "manifest.json"))).RootElement;
            var testables = manifest.GetProperty("testables").EnumerateArray().Select(t => t.GetString()).ToHashSet();
            var deps = manifest.GetProperty("dependencies").EnumerateObject().Select(d => d.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var p in Layout.Packages)
            {
                Assert.Contains(p.Name, testables);
                foreach (var d in p.Dependencies.Where(d => d.StartsWith("com.unity.", StringComparison.Ordinal)))
                    Assert.True(deps.Contains(d) || d == "com.unity.render-pipelines.core", $"{p.Name} depends on {d}, which unity/Packages/manifest.json does not pin.");
            }
        }

        // ---------------------------------------------------------------------------

        private static List<string> CompileGlobs(string csproj) =>
            Regex.Matches(File.ReadAllText(csproj), @"<Compile\s+Include=""([^""]+)""")
                .Select(m => m.Groups[1].Value.Replace('\\', '/')).ToList();

        private static string Normalise(string path) => Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');

        /// <summary>The text with comments and string literals blanked, so a lint over it reads code only.</summary>
        private static string Code(string text)
        {
            var code = new System.Text.StringBuilder(text.Length);
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
                {
                    while (i < text.Length && text[i] != '\n') code.Append(Blank(text[i++]));
                    continue;
                }
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
                {
                    code.Append("  "); i += 2;
                    while (i < text.Length && !(text[i] == '*' && i + 1 < text.Length && text[i + 1] == '/')) code.Append(Blank(text[i++]));
                    if (i < text.Length) { code.Append("  "); i += 2; }
                    continue;
                }
                if (c == '"')
                {
                    bool verbatim = i > 0 && text[i - 1] == '@';
                    code.Append('"'); i++;
                    while (i < text.Length)
                    {
                        if (!verbatim && text[i] == '\\' && i + 1 < text.Length) { code.Append("  "); i += 2; continue; }
                        if (text[i] == '"') { if (verbatim && i + 1 < text.Length && text[i + 1] == '"') { code.Append("  "); i += 2; continue; } break; }
                        code.Append(Blank(text[i++]));
                    }
                    if (i < text.Length) { code.Append('"'); i++; }
                    continue;
                }
                code.Append(c); i++;
            }
            return code.ToString();
        }

        private static char Blank(char c) => c == '\n' ? '\n' : ' ';

        /// <summary>One assembly definition, with the fields that can be got wrong.</summary>
        private sealed class Asmdef
        {
            public static readonly string[] ImpliedByTestAssemblies = { "UnityEngine.TestRunner", "nunit.framework" };

            public string Path = "";
            public string Relative = "";
            public string Name = "";
            public List<string> References = new();
            public List<string> PrecompiledReferences = new();
            public List<string> IncludePlatforms = new();
            public List<string> DefineConstraints = new();
            public List<(string name, string define)> VersionDefines = new();
            public List<string> ImpliedReferences = new();
            public bool NoEngineReferences;
            private JsonDocument _doc = JsonDocument.Parse("{}");
            public bool Has(string key) => _doc.RootElement.TryGetProperty(key, out _);

            public static Asmdef Load(Pkg p, string path)
            {
                var doc = JsonDocument.Parse(File.ReadAllText(path));
                var r = doc.RootElement;
                var a = new Asmdef
                {
                    _doc = doc, Path = path, Relative = p.Name + "/" + p.RelativeTo(path),
                    Name = r.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    References = Strings(r, "references"),
                    PrecompiledReferences = Strings(r, "precompiledReferences"),
                    IncludePlatforms = Strings(r, "includePlatforms"),
                    DefineConstraints = Strings(r, "defineConstraints"),
                    VersionDefines = r.TryGetProperty("versionDefines", out var vd) && vd.ValueKind == JsonValueKind.Array
                        ? vd.EnumerateArray().Select(v => (v.GetProperty("name").GetString() ?? "", v.GetProperty("define").GetString() ?? "")).ToList()
                        : new List<(string, string)>(),
                    NoEngineReferences = r.TryGetProperty("noEngineReferences", out var e) && e.GetBoolean(),
                };
                if (Strings(r, "optionalUnityReferences").Contains("TestAssemblies")) a.ImpliedReferences = ImpliedByTestAssemblies.ToList();
                return a;
            }

            private static List<string> Strings(JsonElement o, string key) =>
                o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : new List<string>();
        }

        /// <summary>One package on disk.</summary>
        private sealed class Pkg
        {
            public readonly string Name;
            public readonly string Root;
            public readonly JsonDocument Manifest;
            public readonly List<Asmdef> Asmdefs;
            public readonly List<string> Assets;
            public readonly List<string> Sources;
            public readonly HashSet<string> Dependencies;

            public Pkg(string unityRoot, string name)
            {
                Name = name;
                Root = Path.Combine(unityRoot, "Packages", name);
                Assert.True(Directory.Exists(Root), $"{Root} does not exist");
                Manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "package.json")));
                Dependencies = Manifest.RootElement.TryGetProperty("dependencies", out var deps)
                    ? deps.EnumerateObject().Select(d => d.Name).ToHashSet(StringComparer.Ordinal)
                    : new HashSet<string>(StringComparer.Ordinal);
                Asmdefs = Directory.GetFiles(Root, "*.asmdef", SearchOption.AllDirectories).OrderBy(RelativeTo, StringComparer.Ordinal).Select(f => Asmdef.Load(this, f)).ToList();
                // Everything Unity would import: every file and folder, minus the sidecars and anything a trailing ~ hides.
                Assets = Directory.EnumerateFileSystemEntries(Root, "*", SearchOption.AllDirectories)
                    .Where(p => !Folded(p) && !p.EndsWith(".meta", StringComparison.Ordinal)).OrderBy(RelativeTo, StringComparer.Ordinal).ToList();
                Sources = Directory.GetFiles(Root, "*.cs", SearchOption.AllDirectories).Where(f => !Folded(f)).OrderBy(RelativeTo, StringComparer.Ordinal).ToList();
            }

            public string RelativeTo(string path) => Path.GetRelativePath(Root, path).Replace('\\', '/');

            /// <summary>The assembly definition governing a file: the one whose folder is the longest prefix of the file's, as Unity decides.</summary>
            public Asmdef? OwnerOf(string file)
            {
                var dir = Normalise(Path.GetDirectoryName(file)!) + "/";
                return Asmdefs.Where(a => dir.StartsWith(Normalise(Path.GetDirectoryName(a.Path)!) + "/", StringComparison.Ordinal))
                    .OrderByDescending(a => a.Path.Length).FirstOrDefault();
            }

            public List<string> SourcesOwnedBy(Asmdef a) => Sources.Where(f => ReferenceEquals(OwnerOf(f), a)).ToList();

            private bool Folded(string path) => RelativeTo(path).Split('/').Any(p => p.EndsWith("~", StringComparison.Ordinal) || p.StartsWith(".", StringComparison.Ordinal));
        }

        /// <summary>
        /// The packages on disk. Their location arrives as assembly metadata from the test
        /// project rather than by walking up from the output directory looking for a folder
        /// with a familiar name.
        /// </summary>
        private static class Layout
        {
            public static readonly string UnityRoot = Locate();
            public static readonly List<Pkg> Packages = new[] { "com.triband.storey", "com.triband.storey.authoring", "com.triband.storey.playkit" }
                .Select(n => new Pkg(UnityRoot, n)).ToList();
            public static Pkg Runtime => Packages[0];
            public static Pkg Get(string name) => Packages.Single(p => p.Name == name);

            private static string Locate()
            {
                var meta = typeof(PackageLayoutTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "UnityRoot")?.Value;
                Assert.False(string.IsNullOrEmpty(meta), "the test project must pass UnityRoot through as assembly metadata");
                Assert.True(Directory.Exists(meta), $"UnityRoot points at {meta}, which does not exist");
                return meta!;
            }
        }
    }
}
