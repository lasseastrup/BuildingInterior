#nullable enable
using System;
using Triband.Storey.Edit;
using Triband.Storey.Generate;

namespace Triband.Storey.Play
{
    /// <summary>Where the player is and how they are moving (world space, metres; y is the feet).</summary>
    public sealed class PlayerState
    {
        public double x, y, z, vy;
        /// <summary>Facing, radians about y (0 = +z).</summary>
        public double face;
        /// <summary>The walk cycle's phase, for an animation.</summary>
        public double walk;
        /// <summary>The lift ride in progress, if any.</summary>
        public LiftRide? ride;

        public void Spawn(double x, double y, double z) { this.x = x; this.y = y; this.z = z; vy = 0; ride = null; }
    }

    /// <summary>A lift ride: the car moves the player to <see cref="to"/> at 4.5 m/s.</summary>
    public sealed class LiftRide
    {
        public BuildingData b = null!;
        public CoreData s = null!;
        public double to;
    }

    /// <summary>What the player is asked to do this frame: a direction relative to the camera (x right, y forward, each −1..1) and running.</summary>
    public struct MoveInput
    {
        public double x, y;
        public bool run;
        public MoveInput(double x, double y, bool run = false) { this.x = x; this.y = y; this.run = run; }
    }

    /// <summary>
    /// The player's step (docs/PLAY.md, slice 5.1): walk or run relative to the camera's yaw in sub-steps of at most
    /// 12 cm, pushed out of walls, never up more than <see cref="PlayWorld.StepUp"/>; fall under gravity onto the
    /// surface below; ride a lift. A port of the prototype's <c>updatePlayer</c>.
    /// </summary>
    public static class Walker
    {
        public const double WalkSpeed = 4.4, RunSpeed = 7, Gravity = 24, LiftSpeed = 4.5;

        public static void Step(PlayWorld world, PlayerState p, MoveInput input, double cameraYaw, double dt)
        {
            if (p.ride != null)
            {
                var r = p.ride; double d = r.to - p.y, sp = LiftSpeed * dt;
                p.y = Math.Abs(d) <= sp ? r.to : p.y + Math.Sign(d) * sp;
                if (p.y == r.to) p.ride = null;
            }
            double ix = input.x, iy = input.y, m = Tiers.Hypot(ix, iy);
            if (m > 1) { ix /= m; iy /= m; m = 1; }
            if (p.ride != null) m = 0;
            if (m > 0.05)
            {
                double fx = -Math.Sin(cameraYaw), fz = -Math.Cos(cameraYaw), rx = -fz, rz = fx;
                double mx = fx * iy + rx * ix, mz = fz * iy + rz * ix, sp = (input.run ? RunSpeed : WalkSpeed) * dt;
                int steps = (int)Math.Ceiling(sp * Math.Max(Math.Abs(mx), Math.Abs(mz)) / 0.12); if (steps == 0) steps = 1;
                for (int s = 0; s < steps; s++)
                {
                    double nx = p.x + mx * sp / steps, nz = p.z + mz * sp / steps;
                    var (cx, cz) = world.Collide(nx, nz, p.y);
                    double sy = world.SurfaceAt(cx, cz, p.y);
                    if (sy > p.y + PlayWorld.StepUp) break;
                    p.x = cx; p.z = cz;
                }
                double tf = Math.Atan2(mx, mz), dA = tf - p.face;
                dA = Math.Atan2(Math.Sin(dA), Math.Cos(dA));
                p.face += dA * Math.Min(1, dt * 12);
                p.walk += dt * m * 9;
            }
            else p.walk *= 0.8;
            if (p.ride == null)
            {
                double sy = world.SurfaceAt(p.x, p.z, p.y);
                if (sy < p.y - 0.02) { p.vy -= Gravity * dt; p.y = Math.Max(sy, p.y + p.vy * dt); }
                else { p.y = sy; p.vy = 0; }
                if (p.y == sy) p.vy = 0;
            }
        }

        /// <summary>Ride the lift the player stands in to storey k (false when there is no lift here, or k is not one it serves).</summary>
        public static bool CallLift(PlayWorld world, PlayerState p, int k)
        {
            if (p.ride != null) return false;
            var at = world.LiftAt(p.x, p.y, p.z); if (at == null) return false;
            var (b, s, floor) = at.Value;
            if (k == floor || !Cores.Levels(b, s).Contains(k)) return false;
            p.ride = new LiftRide { b = b, s = s, to = Derived.FloorBase(b, k) };
            return true;
        }
    }

    /// <summary>
    /// The play camera (docs/PLAY.md, slice 5.1): orbits a target that trails the player's chest, at a yaw, a pitch
    /// (plus the assist's tilt, at most 1.45 rad) and a distance. A port of the prototype's <c>camPlay</c> and <c>applyCam</c>.
    /// </summary>
    public sealed class FollowCamera
    {
        public double tx, ty, tz;
        public double yaw = Math.PI, pitch = 0.78, dist = 13;
        /// <summary>The extra pitch camera assist adds while the player is hidden.</summary>
        public double assist;
        /// <summary>The target's height above the player's feet.</summary>
        public const double Chest = 1.2;

        /// <summary>Put the target on the player at once.</summary>
        public void Snap(PlayerState p) { tx = p.x; ty = p.y + Chest; tz = p.z; }

        /// <summary>The target trails the player: a tenth of the way per 1/100 s.</summary>
        public void Follow(PlayerState p, double dt)
        {
            double k = Math.Min(1, dt * 10);
            tx += (p.x - tx) * k; ty += (p.y + Chest - ty) * k; tz += (p.z - tz) * k;
        }

        /// <summary>The camera's position (it looks at the target).</summary>
        public (double x, double y, double z) Position()
        {
            double pt = Math.Min(pitch + assist, 1.45), cp = Math.Cos(pt);
            return (tx + Math.Sin(yaw) * cp * dist, ty + Math.Sin(pt) * dist, tz + Math.Cos(yaw) * cp * dist);
        }
    }
}
