#nullable enable
using System;
using System.Collections.Generic;
using Triband.Storey.Unity;
using Unity.AI.Navigation;
using Unity.AI.Navigation.Editor;
using UnityEditor;
using UnityEngine;

namespace Triband.Storey.Editor.Navigation
{
    /// <summary>
    /// A NavMesh Surface's Bake takes in every Storey building (docs/PLAY.md §5.1). A bake takes the colliders in the scene
    /// as it starts, and in edit mode a site has none unless its Colliders in edit mode is on, and then only for the
    /// buildings near the Scene view's camera: the switch was turned on by hand on every site before each bake and off
    /// after. Now every site has every building's collider (<see cref="StoreySite.EveryCollider"/>) while a surface can be
    /// baked, that is while one is selected or shown in an inspector (a locked one too), and while one bakes. Then each
    /// site's own switches decide again. No setting changes: nothing goes in the undo history, and the colliders are never
    /// saved with the scene. Edit mode only, as Bake is. Built where the AI Navigation package is installed.
    /// </summary>
    [InitializeOnLoad]
    static class StoreyNavMeshBake
    {
        static IDisposable? hold;
        // the selection changed or an inspector began showing a surface: look again
        static bool look = true;
        // a selected object has a surface: the inspector shows its Bake
        static bool selected;
        // the inspectors that drew the header of an object with a surface: a locked one keeps its Bake whatever is selected
        static readonly List<UnityEditor.Editor> shown = new List<UnityEditor.Editor>();
        // the surfaces Bake could be pressed for while held: the colliders stay while any of them bakes
        static readonly List<NavMeshSurface> offered = new List<NavMeshSurface>();

        static StoreyNavMeshBake()
        {
            Selection.selectionChanged += () => look = true;
            UnityEditor.Editor.finishedDefaultHeaderGUI += Header;
            // let go before Play mode's scene loads: with domain reload off, its sites would make every collider at load
            EditorApplication.playModeStateChanged += s => { look = true; if (s == PlayModeStateChange.ExitingEditMode) Release(); };
            EditorApplication.update += Tick;
        }

        /// <summary>An inspector drew an object's header: with a surface, it shows Bake.</summary>
        static void Header(UnityEditor.Editor editor)
        {
            if (!(editor.target is GameObject) || shown.Contains(editor) || !(SurfaceOf(editor) is NavMeshSurface s)) return;
            shown.Add(editor); Offer(s); look = true;
        }

        /// <summary>A surface on an object the inspector shows, or null.</summary>
        static NavMeshSurface? SurfaceOf(UnityEditor.Editor editor)
        {
            foreach (var t in editor.targets)
                if (t is GameObject go && go.TryGetComponent<NavMeshSurface>(out var s)) return s;
            return null;
        }

        static void Tick()
        {
            if (!look && hold == null) return;
            if (look)
            {
                look = false; selected = false;
                foreach (var go in Selection.gameObjects)
                    if (go != null && go.TryGetComponent<NavMeshSurface>(out var s)) { selected = true; Offer(s); }
            }
            // gone with its inspector, or showing another object or its surface taken off: not counted on to be destroyed
            shown.RemoveAll(e => e == null || SurfaceOf(e) == null);
            bool want = !EditorApplication.isPlayingOrWillChangePlaymode && (selected || shown.Count > 0 || Baking());
            // EveryCollider returns once every site has made them, so they are up before Bake can be pressed
            if (want) hold ??= StoreySite.EveryCollider();
            else Release();
        }

        static void Offer(NavMeshSurface s) { if (!offered.Contains(s)) offered.Add(s); }

        /// <summary>A surface offered while held is still baking (an asynchronous bake, over frames).</summary>
        static bool Baking()
        {
            if (offered.Count == 0) return false;
            var assets = NavMeshAssetManager.instance;
            foreach (var s in offered) if (s != null && assets.IsSurfaceBaking(s)) return true;
            return false;
        }

        /// <summary>Each site's own switches decide its colliders again.</summary>
        static void Release()
        {
            offered.Clear();
            if (hold == null) return;
            hold.Dispose(); hold = null;
        }
    }
}
