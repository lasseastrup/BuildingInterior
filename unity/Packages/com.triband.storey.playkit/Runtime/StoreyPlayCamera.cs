#nullable enable
using Triband.Storey.Play;
using Triband.Storey.Unity;
using UnityEngine;

namespace Triband.Storey.PlayKit
{
    /// <summary>
    /// The play kit's camera (docs/PLAY.md, slice 5.4): orbits a target that trails the character's chest
    /// (<see cref="FollowCamera"/>), turned and zoomed by the player, tilted by the occlusion system's camera assist.
    /// Tells the occlusion system the point it looks at, so the cutaway tests from the player's exact position.
    /// </summary>
    [AddComponentMenu("Storey/Play Kit/Storey Play Camera")]
    [RequireComponent(typeof(Camera))]
    public sealed class StoreyPlayCamera : MonoBehaviour
    {
        public StoreyCharacter? character;
        public StoreyOcclusion? occlusion;
        [Range(0.2f, 1.4f)] public float pitch = 0.78f;
        [Range(4f, 40f)] public float distance = 13f;

        public readonly FollowCamera Follow = new FollowCamera();
        bool snapped;

        void LateUpdate()
        {
            if (character == null) return;
            var p = character.State; var o = occlusion != null && occlusion.site != null ? occlusion.site.transform.position : Vector3.zero;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            if (!snapped) { Follow.Snap(p); Follow.yaw = character.State.face + Mathf.PI; snapped = true; }
            var input = character.Input;
            Follow.yaw += input.Orbit.x;
            pitch = Mathf.Clamp(pitch + input.Orbit.y, 0.2f, 1.4f);
            distance = Mathf.Clamp(distance * (1 - input.Zoom), 4f, 40f);
            Follow.pitch = pitch; Follow.dist = distance;
            Follow.assist = occlusion != null ? occlusion.Assist : 0;
            Follow.Follow(p, dt);
            var (x, y, z) = Follow.Position();
            var target = o + new Vector3((float)Follow.tx, (float)Follow.ty, (float)Follow.tz);
            transform.position = o + new Vector3((float)x, (float)y, (float)z);
            transform.LookAt(target);
            if (occlusion != null) occlusion.SetCameraTarget(target);
        }
    }
}
