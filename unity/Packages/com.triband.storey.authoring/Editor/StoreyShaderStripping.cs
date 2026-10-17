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
    /// global settings). After the build the Console has a line on each of Storey's shaders: its variants compiled, and
    /// how many were left out.
    /// </summary>
    sealed class StoreyShaderStripping : IPreprocessShaders, IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        static bool keepSimple = true;
        static readonly Dictionary<string, (int kept, int stripped)> counts = new Dictionary<string, (int, int)>();
        static readonly ShaderKeyword simple = new ShaderKeyword(StoreyGlobals.SimpleLightingKeyword);

        public void OnPreprocessBuild(BuildReport report)
        {
            counts.Clear();
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
            counts[shader.name] = (c.kept + data.Count, c.stripped + stripped);
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (counts.Count == 0) return;
            var b = new StringBuilder("Storey shaders in this build (docs/PERFORMANCE.md §7.9):");
            foreach (var kv in counts.OrderBy(kv => kv.Key, System.StringComparer.Ordinal))
                b.Append("\n  ").Append(kv.Key).Append(": ").Append(kv.Value.kept.ToString("N0")).Append(" variants compiled")
                    .Append(kv.Value.stripped > 0 ? $", {kv.Value.stripped:N0} with {StoreyGlobals.SimpleLightingKeyword} left out (no quality asset in the build turns Specular And Reflections off)" : "");
            Debug.Log(b.ToString());
        }
    }
}
