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
        List<(string id, string json, (double x0, double z0, double x1, double z1) bounds)> shown;

        public EditSession(string text)
        {
            Document = PrototypeJson.Read(text).Document;
            Text = PrototypeJson.Write(Document);
            shown = Snapshot(Document);
        }

        static List<(string, string, (double, double, double, double))> Snapshot(StoreyDocument d) =>
            d.buildings.Select(b => (b.id, PrototypeJson.Write(b), Site.BoundsOf(b))).ToList();

        /// <summary>After editing <see cref="Document"/> in place: the new text and what changed.</summary>
        public SessionChange Commit() => Diff(Document);

        /// <summary>Throw away changes made to <see cref="Document"/> since the last commit (an edit the operation refused).</summary>
        public void Discard() => Document = PrototypeJson.Read(Text).Document;

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
                    for (int j = 0; j < now.Count; j++)
                        if (j != i && (Touch(now[j].Item3, now[i].Item3) || Touch(now[j].Item3, shown[i].bounds) || Touch(shown[j].bounds, shown[i].bounds)))
                            c.rebuild.Add(now[j].Item1);
                }
            }
            Document = next;
            Text = PrototypeJson.Write(next);
            shown = now;
            return c;
        }

        /// <summary>The prototype's <c>touchingIds</c> test: bounds within 5 cm of each other, so a party wall may join or part.</summary>
        static bool Touch((double x0, double z0, double x1, double z1) A, (double x0, double z0, double x1, double z1) B) =>
            A.x0 <= B.x1 + 0.05 && B.x0 <= A.x1 + 0.05 && A.z0 <= B.z1 + 0.05 && B.z0 <= A.z1 + 0.05;
    }
}
