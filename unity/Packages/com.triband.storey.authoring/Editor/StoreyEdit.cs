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
    /// which buildings to build again. Opened when the site is selected, and saved to the <c>.storey</c> file by itself:
    /// when the site is deselected, with the scene, before Play and on quit. Not saved with the scene itself; it survives
    /// domain reloads.
    /// </summary>
    internal sealed class StoreyEdit : ScriptableObject
    {
        [SerializeField] StoreySite? site;
        [SerializeField] string path = "";
        [SerializeField] string text = "";
        [SerializeField] SavedText? file;   // what the file holds: kept out of undo, so undoing past a save still shows unsaved
        [SerializeField] StoreyEditView? view;   // what is selected: kept out of undo too

        EditSession? core;
        static readonly List<StoreyEdit> open = new List<StoreyEdit>();

        public StoreySite? Site => site;
        /// <summary>The layout's text as it stands (the unsaved edit included).</summary>
        public string Text => text;
        /// <summary>A drag is under way: it is one undo step when it ends.</summary>
        public bool Dragging => dragText != null;
        public bool Dirty => file == null || text != file.text;
        public StoreyDocument Document => Core.Document;
        public string Path => path;
        public StoreyEditView View => view != null ? view : (view = NewView());

        StoreyEditView NewView()
        {
            var v = CreateInstance<StoreyEditView>(); v.hideFlags = HideFlags.DontSave;
            if (Core.Document.buildings.Count > 0) v.selectedId = Core.Document.buildings[0].id;
            return v;
        }

        /// <summary>The selected building, if it still exists.</summary>
        public BuildingData? Selected => Document.buildings.FirstOrDefault(b => b.id == View.selectedId);

        /// <summary>
        /// One edit of the selected building (or the one with <paramref name="id"/>), as <see cref="Apply"/>. The operation
        /// gets the building from the current document, so it is right after an undo too.
        /// </summary>
        public bool ApplyTo(string undoName, Func<BuildingData, bool> op, string? id = null)
        {
            string bid = id ?? View.selectedId;
            return Apply(undoName, d => { var b = d.buildings.FirstOrDefault(x => x.id == bid); return b != null && op(b); });
        }

        int group = -1;
        string? dragText;
        StoreyDocument? dragStart;

        /// <summary>Start a drag: every edit until <see cref="EndDrag"/> becomes one undo step.</summary>
        public void BeginDrag(string undoName)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(undoName);
            group = Undo.GetCurrentGroup();
            dragText = text;
            dragStart = null;
        }

        public void EndDrag()
        {
            if (group >= 0) Undo.CollapseUndoOperations(group);
            group = -1;
            dragText = null; dragStart = null;
        }

        /// <summary>The layout as it was when the drag began (for handles that must stay put while it changes), or null.</summary>
        public StoreyDocument? DragStart => dragText == null ? null : dragStart ??= PrototypeJson.Read(dragText).Document;

        /// <summary>
        /// One frame of a drag that is redone from where it began: the layout goes back to the drag's start, then
        /// <paramref name="op"/> runs on the selected building. Wall points and splits move this way, so the walls follow
        /// the pointer while the result is the prototype's single move on release (a point passing over another does not
        /// merge with it on the way).
        /// </summary>
        public bool ApplyFromDragStart(string undoName, Func<BuildingData, bool> op)
        {
            if (dragText == null) return ApplyTo(undoName, op);
            string bid = View.selectedId;
            Undo.RecordObject(this, undoName);
            var back = Core.Load(dragText);
            var b = Core.Document.buildings.FirstOrDefault(x => x.id == bid);
            bool ok = b != null && op(b);
            if (!ok) Core.Discard();
            var change = Core.Commit();
            change.structural |= back.structural;
            change.rebuild.UnionWith(back.rebuild);
            if (change.None) return ok;
            text = Core.Text;
            EditorUtility.SetDirty(this);
            site!.Preview(Core.Document, text, change);
            Poke();
            return ok;
        }
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
            // a layout from the prototype, or one the palette changed under, may hold colours the palette lacks
            if (StoreyColorField.Conform != null) e.Apply("Match colours to the palette", _ => true);
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
            // presets, new buildings and imports carry CSS colours: every edit ends inside the palette
            if (StoreyColorField.Conform != null) Report(StoreyColorField.Conform(Core.Document));
            var change = Core.Commit();
            if (change.None) return true;
            text = Core.Text;
            EditorUtility.SetDirty(this);
            site!.Preview(Core.Document, text, change);
            Poke();
            return true;
        }

        static void Report(List<ConformedColor> changed)
        {
            foreach (var c in changed)
                Debug.Log($"Storey: {c.from} → {c.name} ({PaletteMatch.Similarity(c.distance):P0} similar), {c.uses} use{(c.uses == 1 ? "" : "s")}");
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
            if (site != null) StoreyOpenings.Sync(site, Document);   // the artist's windows and doors the layout uses
            File.WriteAllText(path, text);
            file!.text = text;
            AssetDatabase.ImportAsset(path);
        }

        /// <summary>
        /// The file changed under an edit with nothing unsaved (a version control update, another tool): show what it
        /// holds now, as one undo step.
        /// </summary>
        public void Reload()
        {
            if (Dirty || site == null || site.layout == null || site.layout.Json == text) return;
            Undo.RecordObject(this, "Reload layout");
            text = site.layout.Json; file!.text = text;
            Restored();
        }

        /// <summary>Close the edit; unsaved changes are dropped (callers save first).</summary>
        public void End()
        {
            open.Remove(this);
            if (site != null) site.Preview(null, Dirty ? null : text, null);
            Undo.ClearUndo(this);
            Poke();
            if (file != null) DestroyImmediate(file);
            if (view != null) DestroyImmediate(view);
            if (site != null) site.View = null;
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
            EditorApplication.playModeStateChanged += s => { if (s == PlayModeStateChange.ExitingEditMode) SaveAll(); };
            EditorApplication.wantsToQuit += () => { SaveAll(); return true; };
        }

        static void SaveAll() { foreach (var e in open.ToList()) if (e != null) e.Save(); }

        /// <summary>After a reimport: the open edits with nothing unsaved show what their files hold now.</summary>
        internal static void ReloadAll()
        {
            foreach (var e in open.ToList()) if (e != null) e.Reload();
        }
    }

    /// <summary>A reimported <c>.storey</c> file shows at once in every site using it.</summary>
    internal sealed class StoreyReimport : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (imported.Any(p => p.EndsWith("." + StoreyImporter.Extension, StringComparison.OrdinalIgnoreCase)))
            {
                StoreyEdit.ReloadAll();
                EditorApplication.QueuePlayerLoopUpdate();
                SceneView.RepaintAll();
            }
        }
    }
}
