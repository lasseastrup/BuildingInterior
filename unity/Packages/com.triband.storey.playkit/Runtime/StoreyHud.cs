#nullable enable
using System.Linq;
using Triband.Storey.Unity;
using UnityEngine;

namespace Triband.Storey.PlayKit
{
    /// <summary>
    /// The play kit's HUD (docs/PLAY.md, slice 5.4): which building and storey the character is on, the lift panel in a
    /// lift car, and the joystick on a touch screen. A component of its own, which the character adds and switches with
    /// <see cref="StoreyCharacter.showHud"/>: IMGUI calls <c>OnGUI</c> for every event of every frame, so a hidden HUD
    /// costs nothing only when its component is off. What it shows the character works out once a frame
    /// (docs/PERFORMANCE.md §11.4); here it is only drawn.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class StoreyHud : MonoBehaviour
    {
        internal StoreyCharacter? character;
        GUIStyle? label;
        // the place's label, made again only when the place changes (OnGUI runs several times a frame)
        string? whereText; (BuildingData?, int) whereFor;

        void Awake() => useGUILayout = false;   // GUI, not GUILayout: no layout pass and its garbage

        void OnGUI()
        {
            var ch = character; if (ch == null) return;
            using var _t = StoreyTimings.Time("hud: the place and the lift panel");
            label ??= new GUIStyle(GUI.skin.box) { fontSize = Mathf.Max(12, Screen.height / 50), alignment = TextAnchor.MiddleLeft };
            float s = label.fontSize * 2.2f;
            // where the character is
            var loc = ch.Here;
            var at = loc == null ? (null, -1) : (loc.Value.b, loc.Value.floor);
            if (whereText == null || at.Item1 != whereFor.Item1 || at.Item2 != whereFor.Item2)
            {
                whereFor = at;
                whereText = loc == null ? "Outside" : $"{loc.Value.b.name} · {Floor(loc.Value.b, loc.Value.floor)}";
            }
            GUI.Box(new Rect(10, 10, s * 7, s), whereText, label);
            // the lift panel, in a car
            if (ch.Lift is var (b, _, floor) && ch.State.ride == null)
            {
                var levels = ch.Stops;
                GUI.Box(new Rect(10, 20 + s, s * 1.4f + 10, (s + 4) * levels.Count + 10), "");
                for (int i = levels.Count - 1, row = 0; i >= 0; i--, row++)
                {
                    int k = levels[i];
                    GUI.enabled = k != floor;
                    if (GUI.Button(new Rect(15, 25 + s + row * (s + 4), s * 1.4f, s), Short(b, k))) ch.CallLift(k);
                }
                GUI.enabled = true;
            }
            // the on-screen joystick (touch)
            var input = ch.Input;
            if (Application.isMobilePlatform || input.joyActive)
            {
                var c = new Vector2(input.joyCentre.x, Screen.height - input.joyCentre.y); float r = input.joyRadius;
                GUI.Box(new Rect(c.x - r, c.y - r, 2 * r, 2 * r), "");
                var t = c + new Vector2(input.joyThumb.x, -input.joyThumb.y);
                GUI.Box(new Rect(t.x - r * 0.35f, t.y - r * 0.35f, r * 0.7f, r * 0.7f), "");
            }
        }

        static string Floor(BuildingData b, int k) => k == b.floors.Count ? "Roof" : k == 0 ? "Ground floor" : "Floor " + k;
        static string Short(BuildingData b, int k) => k == b.floors.Count ? "R" : k == 0 ? "G" : k < Numbers.Length ? Numbers[k] : k.ToString();
        static readonly string[] Numbers = Enumerable.Range(0, 100).Select(i => i.ToString()).ToArray();
    }
}
