#nullable enable
using System;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Unity;
using UnityEditor;
using UnityEngine;

namespace Triband.Storey.Editor
{
    /// <summary>
    /// The stress-test city (docs/CITY.md §1) as a window, <i>Tools ▸ Storey ▸ Test City…</i>: how many buildings, how many
    /// of them walk-in, the seed, the heights and the lifts, made round the selected Storey Site's own buildings, or taken
    /// out. Not in the site's inspector, so nobody adds a few thousand buildings by accident. The city is an edit like any
    /// other: saved with the layout, undone the same way. The settings are kept for next time, per user.
    /// </summary>
    internal sealed class StoreyTestCityWindow : EditorWindow
    {
        [MenuItem(StoreyEditorInfo.MenuRoot + "Test City...", priority = 20)]
        static void Open()
        {
            var w = GetWindow<StoreyTestCityWindow>(false, "Storey Test City", true);
            w.minSize = new Vector2(340, 470);
        }

        const string Key = "Storey.TestCity.";
        /// <summary>The most buildings asked for: every site loaded at once shares the building table.</summary>
        const int MostBuildings = 8000;

        // what the city is made with: the prototype's but for the count and a tenth walk-in, until changed
        int count = 1000, seed = TestCity.Seed, maxStoreys = 40, liftAbove = 5;
        float walkInPercent = 10, heightScale = 1;
        bool prototypeMix, nearMiddle;

        static readonly GUIContent
            BuildingsLabel = new GUIContent("Buildings", "How many generated buildings, in blocks round the layout's own (12 m clear of each)."),
            SeedLabel = new GUIContent("Seed", "The generator's seed: another one lays out another city. The prototype's is 20260928."),
            ScaleLabel = new GUIContent("Height ×", "Multiplies each building's storeys. The prototype's are 1 to 40, taller towards the middle."),
            MostLabel = new GUIContent("Most storeys", "No building is taller than this."),
            PrototypeLabel = new GUIContent("The prototype's mix", "Walk-in as the web prototype makes them: about 6% of the buildings up to 12 storeys, with a stair core in the middle where one fits, and no lifts."),
            WalkInLabel = new GUIContent("Walk-in %", "The share of the buildings with interiors to walk: stairs from the ground to the top, and a lift above the storeys below, where they fit. The others are shells."),
            NearLabel = new GUIContent("Nearest the middle", "The walk-in buildings are the ones nearest the middle of the city, where it is made from. Off, they are spread through it at random."),
            LiftLabel = new GUIContent("Lift above (storeys)", "A walk-in building taller than this gets a lift beside its stairs, where one fits."),
            NewSeed = new GUIContent("New", "Another seed, at random."),
            PrototypeCity = new GUIContent("Prototype's city", "The web prototype's own city: its seed, heights and walk-in mix. The count stays.");

        void OnEnable()
        {
            count = EditorPrefs.GetInt(Key + "Count", count);
            seed = EditorPrefs.GetInt(Key + "Seed", seed);
            heightScale = EditorPrefs.GetFloat(Key + "HeightScale", heightScale);
            maxStoreys = EditorPrefs.GetInt(Key + "MaxStoreys", maxStoreys);
            prototypeMix = EditorPrefs.GetBool(Key + "PrototypeMix", prototypeMix);
            walkInPercent = EditorPrefs.GetFloat(Key + "WalkIn", walkInPercent);
            nearMiddle = EditorPrefs.GetBool(Key + "NearMiddle", nearMiddle);
            liftAbove = EditorPrefs.GetInt(Key + "LiftAbove", liftAbove);
            Undo.undoRedoPerformed += Repaint;   // an undone city shows as undone
        }

        void OnDisable() => Undo.undoRedoPerformed -= Repaint;
        void OnSelectionChange() => Repaint();
        void OnHierarchyChange() => Repaint();
        void OnFocus() => Repaint();

        void Save()
        {
            EditorPrefs.SetInt(Key + "Count", count);
            EditorPrefs.SetInt(Key + "Seed", seed);
            EditorPrefs.SetFloat(Key + "HeightScale", heightScale);
            EditorPrefs.SetInt(Key + "MaxStoreys", maxStoreys);
            EditorPrefs.SetBool(Key + "PrototypeMix", prototypeMix);
            EditorPrefs.SetFloat(Key + "WalkIn", walkInPercent);
            EditorPrefs.SetBool(Key + "NearMiddle", nearMiddle);
            EditorPrefs.SetInt(Key + "LiftAbove", liftAbove);
        }

        // the fields are floats, as the sliders and preferences are: rounded so 0.7 is 0.7, not 0.69999998
        TestCity.Options Options() => new TestCity.Options
        {
            Count = count,
            WalkIn = prototypeMix ? (double?)null : Math.Round((double)walkInPercent, 1) / 100,
            WalkInNearMiddle = nearMiddle,
            Seed = seed,
            HeightScale = Math.Round((double)heightScale, 2),
            MaxStoreys = maxStoreys,
            LiftAbove = liftAbove,
        };

        static StoreySite? Selected => Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<StoreySite>() : null;

        void OnGUI()
        {
            var site = Selected;
            var doc = site != null && site.layout != null ? Document(site) : null;
            var now = doc != null ? TestCity.Summary(doc) : default;
            Target(site, doc, now);
            EditorGUILayout.Space();

            EditorGUI.BeginChangeCheck();
            GUILayout.Label("City", EditorStyles.boldLabel);
            count = EditorGUILayout.IntSlider(BuildingsLabel, count, 1, MostBuildings);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                foreach (int n in new[] { 300, 1000, 3000 })
                    if (GUILayout.Button(n.ToString("N0"), EditorStyles.miniButton, GUILayout.Width(52))) { count = n; GUI.FocusControl(null); }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                seed = EditorGUILayout.IntField(SeedLabel, seed);
                if (GUILayout.Button(NewSeed, EditorStyles.miniButton, GUILayout.Width(52))) { seed = new System.Random().Next(); GUI.FocusControl(null); }
            }
            heightScale = (float)Math.Round(EditorGUILayout.Slider(ScaleLabel, heightScale, 0.25f, 3f), 2);
            maxStoreys = EditorGUILayout.IntSlider(MostLabel, maxStoreys, 1, TestCity.MostStoreys);

            EditorGUILayout.Space();
            GUILayout.Label("Walk-in buildings", EditorStyles.boldLabel);
            prototypeMix = EditorGUILayout.Toggle(PrototypeLabel, prototypeMix);
            using (new EditorGUI.DisabledScope(prototypeMix))
            {
                walkInPercent = (float)Math.Round(EditorGUILayout.Slider(WalkInLabel, walkInPercent, 0, 100), 1);
                EditorGUILayout.LabelField(" ", prototypeMix ? "about 6% of those of 12 storeys or fewer" : $"{TestCity.WalkInWanted(Options()):N0} of the {count:N0} buildings", EditorStyles.miniLabel);
                using (new EditorGUI.DisabledScope(walkInPercent <= 0))
                {
                    nearMiddle = EditorGUILayout.Toggle(NearLabel, nearMiddle);
                    liftAbove = EditorGUILayout.IntSlider(LiftLabel, liftAbove, 1, TestCity.MostStoreys);
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(PrototypeCity, EditorStyles.miniButton))
                {
                    var p = new TestCity.Options();
                    prototypeMix = true; seed = p.Seed; heightScale = (float)p.HeightScale; maxStoreys = p.MaxStoreys; liftAbove = p.LiftAbove;
                    GUI.FocusControl(null);
                }
            }
            if (EditorGUI.EndChangeCheck()) Save();

            if (count > 3000)
                EditorGUILayout.HelpBox($"Every Storey Site loaded at once shares a table of {BuildingTable.Capacity:N0} buildings, and a city this size is slow to edit.", MessageType.Info);
            EditorGUILayout.Space();

            // made after this pass of the window, so a long build or a failure doesn't leave its layout half drawn
            using (new EditorGUI.DisabledScope(doc == null))
            {
                if (GUILayout.Button(new GUIContent(now.buildings > 0 ? "Make test city again" : "Make test city", "Replaces any test city already in the layout. The layout's own buildings stay."), GUILayout.Height(28)))
                    EditorApplication.delayCall += () => { if (site != null) Make(site); };
                using (new EditorGUI.DisabledScope(now.buildings == 0))
                    if (GUILayout.Button(new GUIContent("Remove test city", "Takes the generated buildings out of the layout. Its own stay.")))
                        EditorApplication.delayCall += () => { if (site != null) Remove(site); };
            }
            EditorGUILayout.Space();
            GUILayout.Label("The city is an edit like any other: it saves with the layout, and Undo takes it back.", EditorStyles.wordWrappedMiniLabel);
        }

        /// <summary>The site it acts on, and its test city now; with no site selected, the scene's to pick from.</summary>
        void Target(StoreySite? site, StoreyDocument? doc, (int buildings, int walkIn, int storeys, int tallest, int lifts) now)
        {
            GUILayout.Label("Site", EditorStyles.boldLabel);
            if (site == null)
            {
                EditorGUILayout.HelpBox("Select a Storey Site: the test city is made round its layout's own buildings.", MessageType.Info);
                foreach (var s in FindObjectsByType<StoreySite>(FindObjectsSortMode.None).OrderBy(s => s.name).Take(8))
                    if (GUILayout.Button("Select " + s.name, EditorStyles.miniButton)) Selection.activeObject = s.gameObject;
                return;
            }
            if (site.layout == null) { EditorGUILayout.HelpBox($"\"{site.name}\" has no layout to make the city in.", MessageType.Info); return; }
            EditorGUILayout.LabelField("Storey Site", $"{site.name} ({site.layout.name})");
            if (doc == null) { EditorGUILayout.HelpBox("Its layout doesn't read: the site's inspector says why.", MessageType.Warning); return; }
            var (n, walk, storeys, tallest, lifts) = now;
            EditorGUILayout.LabelField("Own buildings", $"{doc.buildings.Count - n:N0}");
            EditorGUILayout.LabelField("Test city now", n == 0 ? "none" : $"{n:N0} buildings, {walk:N0} walk-in ({100.0 * walk / n:0.#}%)");
            if (n > 0) EditorGUILayout.LabelField(" ", $"{storeys:N0} storeys, the tallest {tallest}; {lifts:N0} lifts");
            if (site.lodMode == StoreySite.LodMode.Fixed)
                EditorGUILayout.HelpBox("The site's LOD mode is Fixed, so every building builds every LOD. Set it to Automatic in Site settings for a city of any size.", MessageType.Warning);
        }

        /// <summary>The site's layout as it stands: the open edit's, or as saved. Null if it doesn't read.</summary>
        static StoreyDocument? Document(StoreySite site)
        {
            try { return StoreyEdit.Of(site)?.Document ?? site.layout!.Document; }
            catch (Exception) { return null; }
        }

        void Make(StoreySite site)
        {
            var e = StoreyEdit.Begin(site);
            var o = Options(); int made = 0;
            string mix = o.WalkIn is double w ? $"{w * 100:0.#}% walk-in" : "the prototype's mix";
            e.Apply($"Made a test city of {o.Count:N0}, {mix}", d => { made = TestCity.Generate(d, o); return true; });
            var (n, walkIn, _, _, _) = TestCity.Summary(e.Document);
            ShowNotification(new GUIContent($"{n:N0} buildings, {walkIn:N0} walk-in"), 2);
            if (made < o.Count) Debug.LogWarning($"Storey: {made:N0} of the {o.Count:N0} buildings asked for fit round the layout's own.");
            Repaint();
        }

        void Remove(StoreySite site)
        {
            StoreyEdit.Begin(site).Apply("Removed the test city", d => TestCity.Clear(d) > 0);
            Repaint();
        }
    }
}
