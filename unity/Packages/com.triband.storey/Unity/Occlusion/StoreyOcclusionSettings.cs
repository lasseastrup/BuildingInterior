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
        [Tooltip("What happens to a building between the camera and the player. Sink: it collapses to a black outline with a low rim of its wall. Slice: it is cut down to the player's storey. Cutout: a soft hole opens around the player. Fade: it ghosts out above a dark base. Dissolve: it ends as Sink does, but fades away instead of collapsing.")]
        public OccluderMode buildingsInTheWay = OccluderMode.Sink;
        [Tooltip("Cut away the walls between the camera and the player in the building they are in.")]
        public bool cutaway = true;
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
        [Tooltip("When the player steps out of a building, keep it cut away while it still stands between the camera and the player, so a trailing camera isn't buried in it.")]
        public bool holdAfterExit = true;

        /// <summary>Copy into the engine-free settings.</summary>
        public void ApplyTo(OcclusionSettings s)
        {
            s.mode = buildingsInTheWay; s.cutaway = cutaway; s.stub = wallStub; s.baseHeight = baseHeight;
            s.holeRadius = holeRadius; s.assist = cameraAssist; s.silhouette = silhouette;
            s.wallMargin = wallMargin; s.holdAfterExit = holdAfterExit;
        }
    }
}
