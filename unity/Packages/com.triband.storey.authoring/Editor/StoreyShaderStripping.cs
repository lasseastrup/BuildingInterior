#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Triband.Storey.Unity;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace Triband.Storey.Editor
{
    /// <summary>
    /// What a player build compiles of Storey's shaders (docs/PERFORMANCE.md §7.9). <c>STOREY_SIMPLE_LIGHTING</c>'s
    /// variants, the minimum tier's lighting, are left out when no quality asset the build can use turns
    /// <b>Specular And Reflections</b> off: one that a scene in the build depends on, or any in the project's own
    /// <c>Assets</c> folder (Resources, Addressables or code may load it). That halves the opaque and massing colour passes.
    /// The scripting define <c>STOREY_KEEP_ALL_SHADER_VARIANTS</c> keeps them. Storey's other keywords are all needed:
    /// <c>STOREY_STEADY</c> must stay a variant of its own, since a <c>discard</c> anywhere in a shader is what costs
    /// hidden-surface removal. URP strips its own keywords by its pipeline assets' features (Strip Unused Variants in its
    /// global settings). After the build the Console has a line on each of Storey's shaders (and the Color Palette Lit
    /// sample's): the vertex and fragment programs compiled, how many were left out, and the keywords in the fragment
    /// programs, how many of them each is in: what the build's settings keep, and what a shader could stop declaring.
    /// </summary>
    sealed class StoreyShaderStripping : IPreprocessShaders, IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        /// <summary>After URP's own stripping (its order is 0), so the counts are what the build compiles.</summary>
        public int callbackOrder => 1000;

        static bool keepSimple = true;
        static readonly Dictionary<string, (int vertex, int fragment, int stripped)> counts = new Dictionary<string, (int, int, int)>();
        static readonly Dictionary<string, Dictionary<string, int>> keywords = new Dictionary<string, Dictionary<string, int>>();
        static readonly ShaderKeyword simple = new ShaderKeyword(StoreyGlobals.SimpleLightingKeyword);

        public void OnPreprocessBuild(BuildReport report)
        {
            counts.Clear(); keywords.Clear();
#if STOREY_KEEP_ALL_SHADER_VARIANTS
            keepSimple = true;
#else
            keepSimple = SimpleLightingUsed();
#endif
        }

        /// <summary>A quality asset with Specular And Reflections off that the build can use (see the class).</summary>
        static bool SimpleLightingUsed()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            var used = new HashSet<string>(scenes.Length > 0 ? AssetDatabase.GetDependencies(scenes, true) : new string[0]);
            foreach (var guid in AssetDatabase.FindAssets("t:StoreyQualitySettings"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/", System.StringComparison.Ordinal) && !used.Contains(path)) continue;
                var q = AssetDatabase.LoadAssetAtPath<StoreyQualitySettings>(path);
                if (q != null && !q.specularAndReflections) return true;
            }
            return false;
        }

        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (!shader.name.StartsWith("Storey/", System.StringComparison.Ordinal) && !shader.name.StartsWith("Flamingo/Storey/", System.StringComparison.Ordinal)) return;
            int stripped = 0;
            if (!keepSimple)
                for (int i = data.Count - 1; i >= 0; i--)
                    if (data[i].shaderKeywordSet.IsEnabled(simple)) { data.RemoveAt(i); stripped++; }
            counts.TryGetValue(shader.name, out var c);
            bool fragment = snippet.shaderType == UnityEditor.Rendering.ShaderType.Fragment;
            counts[shader.name] = (c.vertex + (fragment ? 0 : data.Count), c.fragment + (fragment ? data.Count : 0), c.stripped + stripped);
            if (!fragment) return;
            // the keywords the fragment programs kept have (a fragment program has every keyword its pass declares)
            if (!keywords.TryGetValue(shader.name, out var k)) keywords[shader.name] = k = new Dictionary<string, int>();
            foreach (var d in data)
                foreach (var kw in d.shaderKeywordSet.GetShaderKeywords()) { k.TryGetValue(kw.name, out int n); k[kw.name] = n + 1; }
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (counts.Count == 0) return;
            var b = new StringBuilder("Storey shaders in this build (docs/PERFORMANCE.md §7.9):");
            foreach (var kv in counts.OrderBy(kv => kv.Key, System.StringComparer.Ordinal))
            {
                var (vertex, fragment, stripped) = kv.Value;
                b.Append("\n  ").Append(kv.Key).Append(": ").Append(fragment.ToString("N0")).Append(" fragment and ").Append(vertex.ToString("N0")).Append(" vertex programs compiled")
                    .Append(stripped > 0 ? $", {stripped:N0} with {StoreyGlobals.SimpleLightingKeyword} left out (no quality asset in the build turns Specular And Reflections off)" : "");
                if (keywords.TryGetValue(kv.Key, out var k) && k.Count > 0)
                    b.Append("\n    keywords (in how many of its fragment programs): ")
                        .Append(string.Join(", ", k.OrderByDescending(e => e.Value).ThenBy(e => e.Key, System.StringComparer.Ordinal).Select(e => $"{e.Key} {e.Value:N0}")));
            }
            Debug.Log(b.ToString());
        }
    }
}
