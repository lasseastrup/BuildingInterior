#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Triband.Storey.Editor
{
    /// <summary>
    /// Editing a site's layout (docs/EDITOR.md §2). Holds the layout's text, which is what Unity's undo records: every edit
    /// is one named undo step, and an undo hands the text back to the engine-free <see cref="EditSession"/>, which says
    /// which buildings to build again. Saved to the <c>.storey</c> file on Save, with the scene, or when asked on quit.
    /// Not saved with the scene itself; it survives domain reloads.
    /// </summary>
    internal sealed class StoreyEdit : ScriptableObject
    {
        [SerializeField] StoreySite? site;
        [SerializeField] string path = "";
        [SerializeField] string text = "";
        [SerializeField] SavedText? file;   // what the file holds: kept out of undo, so undoing past a save still shows unsaved
        [SerializeField] internal int selected;

        EditSession? core;
        static readonly List<StoreyEdit> open = new List<StoreyEdit>();

        public StoreySite? Site => site;
        public bool Dirty => file == null || text != file.text;
        public StoreyDocument Document => Core.Document;
        public string Path => path;
        EditSession Core => core ??= new EditSession(text);

        /// <summary>The edit open on a site, if any.</summary>
        public static StoreyEdit? Of(StoreySite s) => open.FirstOrDefault(e => e != null && e.site == s);

        /// <summary>Start editing a site's layout (or return the edit already open).</summary>
        public static StoreyEdit Begin(StoreySite s)
        {
            var e = Of(s);
            if (e != null) return e;
            if (s.layout == null) throw new InvalidOperationException("the site has no layout");
            e = CreateInstance<StoreyEdit>();
            e.hideFlags = HideFlags.DontSave;
            e.name = "Storey edit: " + s.layout.name;
            e.site = s; e.path = AssetDatabase.GetAssetPath(s.layout);
            e.core = new EditSession(s.layout.Json);
            e.text = e.core.Text;
            e.file = CreateInstance<SavedText>(); e.file.hideFlags = HideFlags.DontSave; e.file.text = e.text;
            e.Attach();
            return e;
        }

        void OnEnable()
        {
            // a domain reload brings the edit back from its serialized fields
            if (site == null || string.IsNullOrEmpty(text)) return;
            core = null;
            Attach();
        }

        void Attach()
        {
            if (!open.Contains(this)) open.Add(this);
            site!.Preview(Core.Document, text, null);
            Poke();
        }

        void OnDisable() => open.Remove(this);

        /// <summary>
        /// One edit, one undo step named <paramref name="undoName"/>: <paramref name="op"/> changes the layout with the
        /// operations in <c>Triband.Storey.Edit</c> and returns false to refuse (nothing changes then).
        /// </summary>
        public bool Apply(string undoName, Func<StoreyDocument, bool> op)
        {
            Undo.RecordObject(this, undoName);
            if (!op(Core.Document)) { Core.Discard(); return false; }
            var change = Core.Commit();
            if (change.None) return true;
            text = Core.Text;
            EditorUtility.SetDirty(this);
            site!.Preview(Core.Document, text, change);
            Poke();
            return true;
        }

        /// <summary>After undo or redo: show the text Unity restored.</summary>
        void Restored()
        {
            if (site == null || text == Core.Text) return;
            var change = Core.Load(text);
            site.Preview(Core.Document, text, change);
            Poke();
        }

        /// <summary>Write the layout to its file and reimport it.</summary>
        public void Save()
        {
            if (!Dirty) return;
            File.WriteAllText(path, text);
            file!.text = text;
            AssetDatabase.ImportAsset(path);
        }

        /// <summary>Back to the file's layout, as one undo step.</summary>
        public void Revert()
        {
            if (!Dirty) return;
            Undo.RecordObject(this, "Revert layout");
            text = file!.text;
            Restored();
        }

        /// <summary>Stop editing; unsaved changes are dropped (the caller asks first).</summary>
        public void End()
        {
            open.Remove(this);
            if (site != null) site.Preview(null, Dirty ? null : text, null);
            Undo.ClearUndo(this);
            Poke();
            if (file != null) DestroyImmediate(file);
            DestroyImmediate(this);
        }

        static void Poke()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }

        [InitializeOnLoadMethod]
        static void Hooks()
        {
            Undo.undoRedoPerformed += () => { foreach (var e in open.ToList()) if (e != null) e.Restored(); };
            EditorSceneManager.sceneSaved += _ => { foreach (var e in open.ToList()) if (e != null) e.Save(); };   // Ctrl+S saves the layouts too
            EditorApplication.wantsToQuit += () =>
            {
                var dirty = open.Where(e => e != null && e.Dirty).ToList();
                if (dirty.Count == 0) return true;
                int r = EditorUtility.DisplayDialogComplex("Unsaved layouts", string.Join("\n", dirty.Select(e => e.path)) + "\n\nSave the changes?", "Save", "Cancel", "Don't save");
                if (r == 1) return false;
                if (r == 0) foreach (var e in dirty) e.Save();
                return true;
            };
        }
    }

    /// <summary>A reimported <c>.storey</c> file shows at once in every site using it.</summary>
    internal sealed class StoreyReimport : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (imported.Any(p => p.EndsWith("." + StoreyImporter.Extension, StringComparison.OrdinalIgnoreCase)))
            {
                EditorApplication.QueuePlayerLoopUpdate();
                SceneView.RepaintAll();
            }
        }
    }
}
