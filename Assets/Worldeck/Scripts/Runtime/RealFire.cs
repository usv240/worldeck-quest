using System.Collections.Generic;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// A fire that looks like fire: three upright quads of the Fire shader (each its own noise, a little apart so
    /// the flame has depth), turned to face you around the vertical only, so tongues of flame always rise. Grows
    /// with <see cref="Strength"/> (the kindling placed, the stoking).
    /// </summary>
    public sealed class RealFire : MonoBehaviour
    {
        public float Strength = 0.3f;
        public float Radius = 0.08f;

        readonly List<Transform> quads = new List<Transform>();
        readonly List<Renderer> renderers = new List<Renderer>();
        MaterialPropertyBlock block;
        Transform cam;
        static Mesh quad;

        static readonly Vector3[] Offsets = { new Vector3(0f, 0f, 0f), new Vector3(-0.35f, 0f, 0.25f), new Vector3(0.38f, 0f, -0.2f) };
        static readonly float[] Widths = { 1f, 0.72f, 0.66f };
        static readonly float[] Heights = { 1f, 0.78f, 0.7f };

        public static RealFire Make(Transform parent, Material fire, float radius)
        {
            var go = new GameObject("Real fire");
            go.transform.SetParent(parent, false);
            var f = go.AddComponent<RealFire>();
            f.Radius = radius;
            for (int i = 0; i < Offsets.Length; i++)
            {
                var q = Util.MeshObject("Flame " + i, go.transform, Quad(), fire);
                f.quads.Add(q.transform);
                f.renderers.Add(q.GetComponent<Renderer>());
            }
            f.Layout();
            return f;
        }

        /// <summary>A unit quad standing on its bottom edge (uv y 0 at the base, 1 at the top).</summary>
        static Mesh Quad()
        {
            if (quad != null) return quad;
            quad = new Mesh { name = "flame-quad" };
            quad.vertices = new[] { new Vector3(-0.5f, 0f, 0f), new Vector3(-0.5f, 1f, 0f), new Vector3(0.5f, 1f, 0f), new Vector3(0.5f, 0f, 0f) };
            quad.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            quad.RecalculateBounds();
            quad.bounds = new Bounds(new Vector3(0f, 0.5f, 0f), new Vector3(1.2f, 1.4f, 1.2f));
            return quad;
        }

        void Layout()
        {
            float w = Radius * 3.2f, h = Radius * 5.2f * Mathf.Lerp(0.55f, 1.15f, Mathf.Clamp01(Strength));
            for (int i = 0; i < quads.Count; i++)
            {
                quads[i].localPosition = Offsets[i] * Radius;
                quads[i].localScale = new Vector3(w * Widths[i], h * Heights[i], 1f);
            }
        }

        void LateUpdate()
        {
            if (cam == null) { var c = Camera.main; if (c != null) cam = c.transform; }
            Layout();
            block ??= new MaterialPropertyBlock();
            for (int i = 0; i < quads.Count; i++)
            {
                if (cam != null)
                {
                    Vector3 to = quads[i].position - cam.position; to.y = 0f;
                    if (to.sqrMagnitude > 1e-6f) quads[i].rotation = Quaternion.LookRotation(to, Vector3.up);
                }
                renderers[i].GetPropertyBlock(block);
                block.SetFloat("_Strength", Strength);
                block.SetFloat("_Seed", i * 3.7f + 1.3f);
                renderers[i].SetPropertyBlock(block);
            }
        }
    }
}
