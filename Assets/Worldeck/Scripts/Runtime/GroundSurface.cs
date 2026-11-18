using Oculus.Interaction.Surfaces;
using UnityEngine;

namespace Worldeck
{
    /// <summary>The world's ground, as a surface Meta's teleport arc can hit: the heightfield, marched along the ray.</summary>
    public sealed class GroundSurface : MonoBehaviour, ISurface
    {
        public WorldBuilder Builder;
        public Locomotion Move;
        public Transform Transform => transform;

        public bool Raycast(in Ray ray, out SurfaceHit hit, float maxDistance = 0)
        {
            hit = default;
            if (Builder == null) return false;
            float max = maxDistance > 0f ? maxDistance : 40f;
            const float step = 0.2f;
            Vector3 prev = ray.origin;
            float prevAbove = prev.y - Builder.GroundAt(prev.x, prev.z);
            if (prevAbove < 0f) return false;
            for (float d = step; d <= max; d += step)
            {
                Vector3 p = ray.origin + ray.direction * d;
                float above = p.y - Builder.GroundAt(p.x, p.z);
                if (above <= 0f)
                {
                    float k = prevAbove / Mathf.Max(1e-5f, prevAbove - above);
                    Vector3 at = Vector3.Lerp(prev, p, k);
                    at.y = Builder.GroundAt(at.x, at.z);
                    if (Move != null && !Move.CanLandAt(at)) return false;
                    hit = new SurfaceHit { Point = at, Normal = NormalAt(at), Distance = (at - ray.origin).magnitude };
                    return true;
                }
                prev = p; prevAbove = above;
            }
            return false;
        }

        public bool ClosestSurfacePoint(in Vector3 point, out SurfaceHit hit, float maxDistance = 0)
        {
            hit = default;
            if (Builder == null) return false;
            var at = new Vector3(point.x, Builder.GroundAt(point.x, point.z), point.z);
            float d = Mathf.Abs(point.y - at.y);
            if (maxDistance > 0f && d > maxDistance) return false;
            hit = new SurfaceHit { Point = at, Normal = NormalAt(at), Distance = d };
            return true;
        }

        Vector3 NormalAt(Vector3 p)
        {
            const float e = 0.3f;
            float hx = Builder.GroundAt(p.x + e, p.z) - Builder.GroundAt(p.x - e, p.z);
            float hz = Builder.GroundAt(p.x, p.z + e) - Builder.GroundAt(p.x, p.z - e);
            return new Vector3(-hx, 2f * e, -hz).normalized;
        }
    }
}
