using System.Collections.Generic;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// A see-through hand that acts out a gesture for the coach (HandCoach). Built from smooth capsules so the
    /// Ghost shader's rim light outlines it. Local space follows the Interaction SDK hand convention: fingers
    /// along +Z, palm facing -Y, back of the hand +Y; the thumb is on -X for a right hand and +X for a left.
    /// Pose: <see cref="Curl"/> (0 open … 1 fist, middle/ring/little), <see cref="Pinch"/> (index and thumb
    /// meet: the thumb reaches the index tip by a small CCD solve) and <see cref="Point"/> (index straight,
    /// the rest folded).
    /// </summary>
    public sealed class GhostHand : MonoBehaviour
    {
        public float Curl = 0.1f;
        public float Pinch;
        public float Point;
        [Range(0f, 1f)] public float Alpha = 1f;
        public bool Left;
        public Color Tint = new Color(1f, 0.86f, 0.68f, 1f);

        public enum Anchor { Wrist, IndexTip, PinchPoint, Palm, WristBack }

        // Finger 0 thumb, 1 index, 2 middle, 3 ring, 4 little. Right hand values (x is mirrored for a left).
        static readonly Vector3[] Knuckles =
        {
            new Vector3(-0.028f, -0.012f, 0.03f), new Vector3(-0.025f, 0f, 0.086f), new Vector3(-0.007f, 0.001f, 0.09f),
            new Vector3(0.011f, 0f, 0.087f), new Vector3(0.027f, -0.002f, 0.079f)
        };
        static readonly float[][] Lengths =
        {
            new[] { 0.044f, 0.034f, 0.027f }, new[] { 0.041f, 0.025f, 0.02f }, new[] { 0.045f, 0.028f, 0.021f },
            new[] { 0.042f, 0.026f, 0.02f }, new[] { 0.033f, 0.02f, 0.018f }
        };
        static readonly float[] Radii = { 0.0105f, 0.0088f, 0.009f, 0.0085f, 0.0074f };
        // Curl angles per segment at full curl (degrees about local +X: towards the palm).
        static readonly Vector3 FistCurl = new Vector3(78f, 98f, 62f);

        readonly Transform[][] joints = new Transform[5][];
        readonly Transform[] tips = new Transform[5];
        readonly List<Renderer> renderers = new List<Renderer>();
        readonly Quaternion[] thumbOpen = new Quaternion[3];
        MaterialPropertyBlock block;
        static readonly int AlphaId = Shader.PropertyToID("_Alpha");
        static readonly int TintId = Shader.PropertyToID("_Tint");

        public Vector3 IndexTip => tips[1] != null ? tips[1].position : transform.position;
        public Vector3 ThumbTip => tips[0] != null ? tips[0].position : transform.position;
        public Vector3 PinchPoint => (IndexTip + ThumbTip) * 0.5f;
        public Vector3 PalmCenter => transform.TransformPoint(new Vector3(0f, -0.012f, 0.05f));
        /// <summary>Where a watch would sit: the back of the wrist.</summary>
        public Vector3 WristBack => transform.TransformPoint(new Vector3(0f, 0.022f, -0.01f));

        public static GhostHand Make(string name, Transform parent, Material ghost, bool left)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var g = go.AddComponent<GhostHand>();
            g.Left = left;
            g.Build(ghost);
            go.SetActive(false);
            return g;
        }

        float X(float x) => Left ? -x : x;

        void Build(Material ghost)
        {
            block = new MaterialPropertyBlock();
            // Palm: a flattened ellipsoid; the heel of the hand; a short forearm that fades out.
            var palm = Util.MeshObject("Palm", transform, Capsule(0f, 0.5f, 0.5f, 14, 5), ghost);
            palm.transform.localPosition = new Vector3(0f, 0f, 0.048f);
            palm.transform.localScale = new Vector3(0.084f, 0.03f, 0.1f);
            renderers.Add(palm.GetComponent<Renderer>());
            var heel = Util.MeshObject("Wrist", transform, Capsule(0.05f, 0.024f, 0.027f, 12, 3, 0f), ghost);
            heel.transform.localPosition = new Vector3(0f, 0f, -0.05f);
            heel.transform.localScale = new Vector3(1f, 0.62f, 1f);
            renderers.Add(heel.GetComponent<Renderer>());

            for (int f = 0; f < 5; f++)
            {
                joints[f] = new Transform[3];
                Transform parentT = transform;
                Vector3 at = new Vector3(X(Knuckles[f].x), Knuckles[f].y, Knuckles[f].z);
                for (int s = 0; s < 3; s++)
                {
                    var j = new GameObject("Joint " + f + "." + s).transform;
                    j.SetParent(parentT, false);
                    j.localPosition = at;
                    float len = Lengths[f][s];
                    float r0 = Radii[f] * (1f - 0.1f * s), r1 = Radii[f] * (0.9f - 0.1f * s);
                    var seg = Util.MeshObject("Segment", j, Capsule(len, r0, r1, 10, 3), ghost);
                    renderers.Add(seg.GetComponent<Renderer>());
                    joints[f][s] = j;
                    parentT = j;
                    at = new Vector3(0f, 0f, len);
                }
                var tip = new GameObject("Tip " + f).transform;
                tip.SetParent(parentT, false);
                tip.localPosition = new Vector3(0f, -Radii[f] * 0.35f, Lengths[f][2] + Radii[f] * 0.4f);
                tips[f] = tip;
            }
            // The thumb leaves the palm forwards, outwards and a little down.
            thumbOpen[0] = Quaternion.Euler(14f, X(-42f), X(-30f));
            thumbOpen[1] = Quaternion.Euler(8f, 0f, 0f);
            thumbOpen[2] = Quaternion.Euler(10f, 0f, 0f);
            Apply();
        }

        /// <summary>Puts the hand so that <paramref name="anchor"/> lands on <paramref name="point"/>, turned to <paramref name="rotation"/>.</summary>
        public void PlaceBy(Vector3 point, Quaternion rotation, Anchor anchor)
        {
            transform.rotation = rotation;
            Apply();
            Vector3 a = anchor switch
            {
                Anchor.IndexTip => IndexTip,
                Anchor.PinchPoint => PinchPoint,
                Anchor.Palm => PalmCenter,
                Anchor.WristBack => WristBack,
                _ => transform.position
            };
            transform.position += point - a;
        }

        /// <summary>Sets the joints from Curl, Pinch and Point (called by PlaceBy and every frame).</summary>
        public void Apply()
        {
            if (joints[1] == null) return;
            float point = Mathf.Clamp01(Point), pinch = Mathf.Clamp01(Pinch) * (1f - point), curl = Mathf.Clamp01(Curl);
            // Index: a soft bend at rest, the pinch bend, straight when pointing; follows a fist otherwise.
            float index = Mathf.Lerp(Mathf.Max(0.08f, curl * 0.9f), 0.42f, pinch);
            index = Mathf.Lerp(index, 0f, point);
            SetCurl(1, index);
            for (int f = 2; f < 5; f++)
            {
                float c = Mathf.Max(curl, Mathf.Lerp(0.12f + 0.04f * f, 0.3f + 0.05f * f, pinch));
                SetCurl(f, Mathf.Lerp(c, 0.95f, point));
            }
            // Thumb: open, tucked over the fingers (point / fist), or reaching the index tip (pinch).
            float tuck = Mathf.Max(point, curl * 0.8f);
            Quaternion[] tucked = { Quaternion.Euler(30f, X(-10f), X(-55f)), Quaternion.Euler(28f, 0f, 0f), Quaternion.Euler(35f, 0f, 0f) };
            for (int s = 0; s < 3; s++) joints[0][s].localRotation = Quaternion.Slerp(thumbOpen[s], tucked[s], tuck);
            if (pinch > 0.001f)
            {
                var start = new Quaternion[3];
                for (int s = 0; s < 3; s++) start[s] = joints[0][s].localRotation;
                Vector3 target = tips[1].position;
                for (int it = 0; it < 8; it++)
                    for (int s = 2; s >= 0; s--)
                    {
                        Vector3 toTip = tips[0].position - joints[0][s].position, toTarget = target - joints[0][s].position;
                        if (toTip.sqrMagnitude < 1e-8f || toTarget.sqrMagnitude < 1e-8f) continue;
                        joints[0][s].rotation = Quaternion.FromToRotation(toTip, toTarget) * joints[0][s].rotation;
                    }
                for (int s = 0; s < 3; s++) joints[0][s].localRotation = Quaternion.Slerp(start[s], joints[0][s].localRotation, Mathf.SmoothStep(0f, 1f, pinch));
            }
        }

        void SetCurl(int f, float c)
        {
            for (int s = 0; s < 3; s++)
                joints[f][s].localRotation = Quaternion.Euler(FistCurl[s] * c, s == 0 ? X((f - 2.3f) * -2.5f * (1f - c)) : 0f, 0f);
        }

        void LateUpdate()
        {
            Apply();
            block ??= new MaterialPropertyBlock();
            foreach (var r in renderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetFloat(AlphaId, Alpha);
                block.SetColor(TintId, Tint);
                r.SetPropertyBlock(block);
            }
        }

        // ---------- meshes ----------

        static readonly Dictionary<string, Mesh> cache = new Dictionary<string, Mesh>();

        /// <summary>
        /// A smooth capsule along +Z from 0 to <paramref name="length"/>, radius <paramref name="r0"/> at the base
        /// and <paramref name="r1"/> at the tip, with round ends (length 0 and equal radii: a sphere). Vertex alpha
        /// runs from <paramref name="baseAlpha"/> at the base end to 1 (a forearm that fades out).
        /// </summary>
        public static Mesh Capsule(float length, float r0, float r1, int sides = 10, int capRings = 3, float baseAlpha = 1f)
        {
            string key = length + "/" + r0 + "/" + r1 + "/" + sides + "/" + capRings + "/" + baseAlpha;
            if (cache.TryGetValue(key, out var cached) && cached != null) return cached;
            var rings = new List<(float z, float r, float nz, float nr)>();
            for (int k = 0; k <= capRings; k++)
            {
                float phi = Mathf.Lerp(-90f, 0f, k / (float)capRings) * Mathf.Deg2Rad;
                rings.Add((r0 * Mathf.Sin(phi), r0 * Mathf.Cos(phi), Mathf.Sin(phi), Mathf.Cos(phi)));
            }
            for (int k = 0; k <= capRings; k++)
            {
                float phi = Mathf.Lerp(0f, 90f, k / (float)capRings) * Mathf.Deg2Rad;
                rings.Add((length + r1 * Mathf.Sin(phi), r1 * Mathf.Cos(phi), Mathf.Sin(phi), Mathf.Cos(phi)));
            }
            var v = new List<Vector3>(); var n = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
            float zMin = -r0, zMax = length + r1;
            foreach (var ring in rings)
            {
                float a = Mathf.Lerp(baseAlpha, 1f, Mathf.InverseLerp(zMin, zMin + (zMax - zMin) * 0.7f, ring.z));
                for (int j = 0; j < sides; j++)
                {
                    float th = j * Mathf.PI * 2f / sides;
                    float cs = Mathf.Cos(th), sn = Mathf.Sin(th);
                    v.Add(new Vector3(cs * ring.r, sn * ring.r, ring.z));
                    n.Add(new Vector3(cs * ring.nr, sn * ring.nr, ring.nz).normalized);
                    c.Add(new Color(1f, 1f, 1f, a));
                }
            }
            for (int i = 0; i + 1 < rings.Count; i++)
                for (int j = 0; j < sides; j++)
                {
                    int a = i * sides + j, b = i * sides + (j + 1) % sides, d = (i + 1) * sides + j, e = (i + 1) * sides + (j + 1) % sides;
                    // Outward-facing for Unity (cross(b-a, c-a) points out: checked against Util.Quad's winding).
                    t.Add(a); t.Add(b); t.Add(d);
                    t.Add(b); t.Add(e); t.Add(d);
                }
            var mesh = new Mesh { name = "ghost-capsule" };
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetColors(c); mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            cache[key] = mesh;
            return mesh;
        }
    }
}
