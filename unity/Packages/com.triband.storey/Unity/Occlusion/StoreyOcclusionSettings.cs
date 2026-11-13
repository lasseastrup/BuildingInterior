#nullable enable
using Triband.Storey.Occlusion;
using UnityEngine;

namespace Triband.Storey.Unity
{
    /// <summary>
    /// The project's occlusion settings (SPEC §5, §10 decision 8): tuned per project by its designers, never by players.
    /// One asset per project, referenced by each <see cref="StoreyOcclusion"/>; without one the defaults apply.
    /// </summary>
    [CreateAssetMenu(menuName = "Storey/Occlusion Settings", fileName = "StoreyOcclusionSettings")]
    public sealed class StoreyOcclusionSettings : ScriptableObject
    {
        [Tooltip("What happens to a building between the camera and the player. Sink: it collapses to a black outline with a low rim of its wall. Slice: it is cut down to the player's storey. Cutout: a soft hole opens around the player. Fade: it ghosts out above a dark base. Dissolve: it ends as Sink does, but fades away instead of collapsing, and its Dissolve Look says what it leaves.")]
        public OccluderMode buildingsInTheWay = OccluderMode.Sink;
        [Tooltip("Dissolve: what is left of a building in the way once it has gone. Plain: its footprint alone. Outline: a wire outline of it, its corners, roof line and storeys. Ghost: a pale x-ray screen of it. Hatch: diagonal ink stripes across its faces. Stub: its storey at the player's height up to the wall stub, capped as the cutaway leaves walls, with no footprint. Blueprint: Ghost with Outline's lines. Can be changed in Play mode.")]
        public DissolveLook dissolveLook = DissolveLook.Plain;
        [Tooltip("Dissolve's Ghost and Blueprint: the share of the building's pixels left. Hatch: the stripes' share of their spacing.")]
        [Range(0.06f, 0.6f)] public float dissolveKeeps = 0.25f;
        [Tooltip("Dissolve's Hatch: metres from one stripe to the next.")]
        [Range(0.15f, 2f)] public float hatchSpacing = 0.45f;
        [Tooltip("Dissolve's Outline and Blueprint: the lines' thickness, in metres.")]
        [Range(0.02f, 0.25f)] public float outlineWidth = 0.06f;
        [Tooltip("Dissolve's Outline and Blueprint: a line round every storey too, not only the corners and the roof line.")]
        public bool outlineStoreys = true;
        [Tooltip("The colour of a cut wall's top: where the cutaway or the ceiling clip cuts a wall, its section is drawn as a solid top face in this colour, lit.")]
        public Color cutCapColour = new Color(0.72f, 0.71f, 0.68f);
        [Tooltip("Cut away the walls between the camera and the player in the building they are in.")]
        public bool cutaway = true;
        [Tooltip("Cutaway: take down every wall facing the camera too, not only those between the camera and the player. The storey opens on the camera's side like a doll's house and its far walls stand; a wall goes down or up only as the camera turns past it, never as the player walks about. Room walls, which face both ways, go down as before, when they are between the camera and the player.")]
        public bool wallsFacingCamera = false;
        [Tooltip("How high a cut wall stays, in metres above the floor.")]
        [Range(0.2f, 2.6f)] public float wallStub = 1.0f;
        [Tooltip("Cutout and Fade: the solid dark base above the floor of the player's storey, in metres.")]
        [Range(0.3f, 2.5f)] public float baseHeight = 1.0f;
        [Tooltip("Cutout: the hole's radius around the player, in metres.")]
        [Range(1f, 5f)] public float holeRadius = 2.4f;
        [Tooltip("Tilt the camera a little steeper when the player has been hidden for half a second.")]
        public bool cameraAssist = false;
        [Tooltip("Draw the character through walls (the play kit's character does; a project's own character reads this).")]
        public bool silhouette = true;
        [Tooltip("How far past a wall the player must be before the cutaway starts to drop it, in metres. Keeps a wall seen edge-on from dropping and rising again as the player passes it.")]
        [Range(0f, 1f)] public float wallMargin = 0.3f;
        [Tooltip("When the player steps out of a building, keep it cut away while they are at its door and it stands between the camera and the player, so a trailing camera isn't buried in it. Past the door it goes as any building in the way does, from how it was seen (with Cutout and Fade, once the camera is out of it too; with buildings in the way Off, while it is in the way at all).")]
        public bool holdAfterExit = true;

        /// <summary>Copy into the engine-free settings.</summary>
        public void ApplyTo(OcclusionSettings s)
        {
            s.mode = buildingsInTheWay; s.cutaway = cutaway; s.wallsFacingCamera = wallsFacingCamera; s.stub = wallStub; s.baseHeight = baseHeight;
            s.holeRadius = holeRadius; s.assist = cameraAssist; s.silhouette = silhouette;
            s.wallMargin = wallMargin; s.holdAfterExit = holdAfterExit;
            s.dissolveLook = dissolveLook; s.dissolveKeeps = dissolveKeeps; s.hatchSpacing = hatchSpacing;
            s.outlineWidth = outlineWidth; s.outlineStoreys = outlineStoreys;
        }
    }
}
