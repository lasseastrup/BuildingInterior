#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Generate;

namespace Triband.Storey.Edit
{
    /// <summary>What an edit or an undo changed: the buildings to build again, or everything when buildings came, went or were reordered.</summary>
    public sealed class SessionChange
    {
        public bool structural;
        /// <summary>Ids of the buildings that changed, and of the neighbours whose shared walls may have (where they were or are now).</summary>
        public readonly HashSet<string> rebuild = new HashSet<string>(StringComparer.Ordinal);
        public bool None => !structural && rebuild.Count == 0;
    }

    /// <summary>
    /// A layout being edited (docs/EDITOR.md §2), engine-free. The editor mutates <see cref="Document"/> with the edit
    /// operations and calls <see cref="Commit"/>; an undo hands back an earlier <see cref="Text"/> through
    /// <see cref="Load"/>. Either way the session says which buildings to build again. The text is the layout as Storey
    /// writes it, so it is what the <c>.storey</c> file gets on save.
    /// </summary>
    public sealed class EditSession
    {
        public StoreyDocument Document { get; private set; }
        public string Text { get; private set; }
        List<(string id, string json, (double x0, double z0, double x1, double z1) bounds, HashSet<string> linked)> shown;
        string shownMeta = "";   // the layout's own fields (the palette, the spawn), without the buildings

        public EditSession(string text)
        {
            Document = PrototypeJson.Read(text).Document;
            Text = PrototypeJson.Write(Document);
            shown = Snapshot(Document); shownMeta = Meta(Document);
        }

        static string Meta(StoreyDocument d) => PrototypeJson.Write(new StoreyDocument { v = d.v, seq = d.seq, spawn = d.spawn, palette = d.palette });

        static List<(string, string, (double, double, double, double), HashSet<string>)> Snapshot(StoreyDocument d)
        {
            // who bridges to whom, once for the layout (not once per building: that was the square of the count)
            var into = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var a in d.buildings)
                foreach (var br in a.bridges) { if (!into.TryGetValue(br.to, out var l)) into[br.to] = l = new List<string>(); l.Add(a.id); }
            return d.buildings.Select(b => (b.id, PrototypeJson.Write(b), Site.BoundsOf(b), Linked(b, into))).ToList();
        }

        /// <summary>The buildings a bridge joins to b, either way: each builds a door for it, and b's builds it.</summary>
        static HashSet<string> Linked(BuildingData b, Dictionary<string, List<string>> into)
        {
            var o = new HashSet<string>(b.bridges.Select(x => x.to), StringComparer.Ordinal);
            if (into.TryGetValue(b.id, out var l)) o.UnionWith(l);
            return o;
        }

        /// <summary>After editing <see cref="Document"/> in place: the new text and what changed.</summary>
        public SessionChange Commit() => Diff(Document);

        /// <summary>Throw away changes made to <see cref="Document"/> since the last commit (an edit the operation refused).</summary>
        public void Discard()
        {
            // only the buildings the operation changed go back: reading the whole layout takes half a second at 3,000
            var d = Document;
            bool same = d.buildings.Count == shown.Count && Meta(d) == shownMeta;
            for (int i = 0; same && i < shown.Count; i++) same = d.buildings[i].id == shown[i].id;
            if (!same) { Document = PrototypeJson.Read(Text).Document; return; }
            for (int i = 0; i < shown.Count; i++)
                if (PrototypeJson.Write(d.buildings[i]) != shown[i].json) d.buildings[i] = PrototypeJson.ReadBuilding(shown[i].json);
        }

        /// <summary>
        /// Put one building back as <paramref name="json"/> held it (a drag going back to where it began), without
        /// reading the whole layout. The next <see cref="Commit"/> says what changed. False when the building is gone.
        /// </summary>
        public bool Restore(string id, string json)
        {
            int i = Document.buildings.FindIndex(b => b.id == id);
            if (i < 0) return false;
            Document.buildings[i] = PrototypeJson.ReadBuilding(json);
            return true;
        }

        /// <summary>After an undo or redo, or a revert: show this text instead.</summary>
        public SessionChange Load(string text)
        {
            if (text == Text) return new SessionChange();
            return Diff(PrototypeJson.Read(text).Document);
        }

        SessionChange Diff(StoreyDocument next)
        {
            var now = Snapshot(next);
            var c = new SessionChange();
            if (now.Count != shown.Count || now.Where((x, i) => x.Item1 != shown[i].id).Any()) c.structural = true;
            else
            {
                var changed = new List<int>();
                for (int i = 0; i < now.Count; i++) if (now[i].Item2 != shown[i].json) changed.Add(i);
                foreach (int i in changed)
                {
                    c.rebuild.Add(now[i].Item1);
                    foreach (var id in now[i].Item4.Concat(shown[i].linked)) c.rebuild.Add(id);
                    for (int j = 0; j < now.Count; j++)
                        if (j != i && (Touch(now[j].Item3, now[i].Item3) || Touch(now[j].Item3, shown[i].bounds) || Touch(shown[j].bounds, shown[i].bounds)))
                            c.rebuild.Add(now[j].Item1);
                }
            }
            Document = next;
            Text = PrototypeJson.Write(next);
            shown = now; shownMeta = Meta(next);
            return c;
        }

        /// <summary>The prototype's <c>touchingIds</c> test: bounds within 5 cm of each other, so a party wall may join or part.</summary>
        static bool Touch((double x0, double z0, double x1, double z1) A, (double x0, double z0, double x1, double z1) B) =>
            A.x0 <= B.x1 + 0.05 && B.x0 <= A.x1 + 0.05 && A.z0 <= B.z1 + 0.05 && B.z0 <= A.z1 + 0.05;
    }
}
