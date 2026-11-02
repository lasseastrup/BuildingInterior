#nullable enable
using System.Collections.Generic;
using Triband.Storey.Generate;
using Triband.Storey.Props;
using Triband.Storey.Unity;
using UnityEditor;
using UnityEngine;

namespace Triband.Storey.Editor
{
    /// <summary>
    /// A building's Storey Props, taken along as the building moves in the editor (docs/EDITOR.md §6.11, docs/PROPS.md §2.4):
    /// the Shape tool's move gizmo and Snap to ground move every prop in it by as much as it moves, each prop's transform its
    /// own undo record in the move's one undo step. The props in it are those the props manager files under it
    /// (<see cref="StoreyProp.BuildingId"/>, as their inspectors say), and those switched off by where their pivot stands
    /// (<see cref="PropLocator.BuildingAt"/>). A prop under another that moves goes with that one. Off with *Its props move
    /// with it* (Shape ▸ Placement), kept per user.
    /// </summary>
    internal sealed class StoreyPropsFollow
    {
        const string Pref = "Storey.PropsFollowBuilding";

        /// <summary>Moving a building moves its props (on by default).</summary>
        public static bool Enabled { get => EditorPrefs.GetBool(Pref, true); set => EditorPrefs.SetBool(Pref, value); }

        readonly Transform site;
        readonly List<(Transform t, Vector3 from)> props;

        StoreyPropsFollow(Transform site, List<(Transform, Vector3)> props) { this.site = site; this.props = props; }

        /// <summary>How many props go with the building (each outermost one: the props under it go with it).</summary>
        public int Count => props.Count;

        /// <summary>The props in building <paramref name="b"/> of the edit's site, where they stand now; null with none, or with the setting off.</summary>
        public static StoreyPropsFollow? Of(StoreyEdit e, BuildingData b)
        {
            if (!Enabled || e.Site == null) return null;
            var site = e.Site; Site? index = null;
            var inIt = new List<Transform>();
            foreach (var p in Object.FindObjectsByType<StoreyProp>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (EditorUtility.IsPersistent(p)) continue;   // an asset's, not the scene's
                bool mine;
                if (p.isActiveAndEnabled) mine = p.Site == site && p.BuildingId == b.id;
                else
                {
                    // not filed by the props manager (switched off): by where its pivot stands, in the site's space
                    index ??= new Site(e.Document.buildings);
                    var at = site.transform.InverseTransformPoint(p.transform.position);
                    mine = PropLocator.BuildingAt(index, at.x, at.y, at.z)?.id == b.id;
                }
                if (mine) inIt.Add(p.transform);
            }
            var o = new List<(Transform, Vector3)>();
            foreach (var t in inIt)
            {
                bool under = false;
                foreach (var u in inIt) if (u != t && t.IsChildOf(u)) { under = true; break; }
                if (!under) o.Add((t, t.position));
            }
            return o.Count == 0 ? null : new StoreyPropsFollow(site.transform, o);
        }

        /// <summary>
        /// Every prop moved from where it stood by <paramref name="by"/>, in the site's space (the building's own move: its
        /// position across and its elevation), each recorded for undo as <paramref name="undoName"/>.
        /// </summary>
        public void MoveBy(Vector3 by, string undoName)
        {
            var d = site.TransformVector(by);
            foreach (var (t, from) in props)
            {
                if (t == null) continue;   // deleted meanwhile
                Undo.RecordObject(t, undoName);
                t.position = from + d;
            }
        }
    }
}
