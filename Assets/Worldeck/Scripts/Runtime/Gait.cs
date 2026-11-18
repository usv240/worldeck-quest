using System;
using System.Collections.Generic;
using Worldeck.Core;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// Legs that step when an animal walks. The models are single rigid meshes with no skeleton, so the walk is done
    /// in the World shader: <see cref="Rig"/> marks each corner of the mesh with how much it belongs to a leg (1 at
    /// the foot, 0 at the body) and which pair of legs it steps with; this component tells the shader, each frame,
    /// which way the animal faces, where it is in its stride, and how far its feet swing (0 while it stands).
    /// Whoever moves the animal (Alive on your table, Wild out in the world) sets <see cref="Walk"/>.
    /// </summary>
    public sealed class Gait : MonoBehaviour
    {
        /// <summary>Set each frame by whoever moves it: it took a step this frame.</summary>
        public bool Walk;
        /// <summary>Its size now (metres, longest side).</summary>
        public Func<float> Size;
        /// <summary>How far its feet swing just now, metres (0 standing). For tests.</summary>
        public float Swing { get; private set; }
        /// <summary>Where it is in its stride (whole strides walked so far). For tests.</summary>
        public float Stride { get; private set; }

        static readonly int GaitId = Shader.PropertyToID("_Gait");
        // No legs to speak of.
        static readonly HashSet<string> NoLegs = new HashSet<string> { "fish", "bee", "caterpillar", "crab" };
        // Walk on two: left and right alternate. Everything else on four: the diagonal pairs step together.
        static readonly HashSet<string> TwoLegs = new HashSet<string>
        {
            "person", "knight", "robot", "alien", "zombie", "astronaut", "penguin", "parrot", "chick", "monkey",
            "t_rex", "velociraptor", "parasaurolophus", "gingerbread", "nutcracker"
        };
        // Short legs under a big body (the cube pets, the storybook people): only the lowest part steps.
        static readonly HashSet<string> ShortLegs = new HashSet<string>
        {
            "person", "knight", "robot", "alien", "zombie", "astronaut", "lion", "tiger", "cat", "bunny", "elephant",
            "giraffe", "monkey", "panda", "polar_bear", "koala", "penguin", "parrot", "chick", "beaver", "pig", "sheep"
        };

        Renderer rend;
        MaterialPropertyBlock block;
        Vector3 last;
        bool has;

        /// <summary>Whether this kind walks on legs.</summary>
        public static bool HasLegs(string kind) => !string.IsNullOrEmpty(kind) && LifeSize.IsCreature(kind) && !NoLegs.Contains(kind);

        /// <summary>
        /// Marks a made mesh (standing on y = 0, facing +z, centred) for walking: for each corner, uv2.x is how much
        /// it swings with a leg (1 at the ground, 0 from the top of the legs up, and 0 along the middle of the body
        /// so bellies, tails and trunks stay put) and uv2.y is which pair it steps with (0 or 0.5 of a stride).
        /// Returns false, leaving the mesh alone, for things with no legs.
        /// </summary>
        public static bool Rig(Mesh mesh, string kind)
        {
            if (mesh == null || !HasLegs(kind)) return false;
            var v = mesh.vertices;
            if (v.Length == 0) return false;
            var b = mesh.bounds;
            float height = Mathf.Max(1e-4f, b.size.y), width = Mathf.Max(1e-4f, b.size.x);
            float legTop = b.min.y + height * (ShortLegs.Contains(kind) ? 0.24f : 0.42f);
            bool two = TwoLegs.Contains(kind);
            var uv = new List<Vector2>(v.Length);
            int marked = 0;
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 p = v[i];
                float down = Mathf.Clamp01((legTop - p.y) / Mathf.Max(1e-4f, legTop - b.min.y));
                float side = Mathf.Clamp01(Mathf.Abs(p.x - b.center.x) / (width * 0.12f));
                float w = down * side;
                bool left = p.x < b.center.x, back = p.z < b.center.z;
                float pair = two ? (left ? 0.5f : 0f) : (left != back ? 0.5f : 0f);
                if (w > 0.01f) marked++;
                uv.Add(new Vector2(w, pair));
            }
            if (marked == 0) return false;
            mesh.SetUVs(1, uv);
            return true;
        }

        void Awake()
        {
            rend = GetComponent<Renderer>();
            block = new MaterialPropertyBlock();
        }

        void OnDisable()
        {
            if (rend != null) rend.SetPropertyBlock(null);
            has = false;
            Swing = 0f;
        }

        void LateUpdate()
        {
            if (rend == null) return;
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) return;
            float size = Mathf.Max(0.02f, Size != null ? Size() : 0.3f);
            Vector3 p = transform.position;
            Vector3 d = p - last; d.y = 0f;
            last = p;
            float moved = d.magnitude;
            // A stride is two steps: about half its length for each. A jump in position (picked up, sent home) is no walk.
            bool walking = Walk && moved > size * 0.0004f * Mathf.Max(0.2f, dt * 72f);
            if (walking && moved < size * 0.5f) Stride += moved / (size * 0.5f);
            float want = walking ? size * 0.07f : 0f;
            Swing = Mathf.MoveTowards(Swing, want, size * 0.45f * dt);
            Walk = false;
            if (Swing <= 1e-5f)
            {
                // Standing: nothing for the shader to do, and the mesh draws with the rest of the world again.
                if (has) { rend.SetPropertyBlock(null); has = false; }
                return;
            }
            Vector3 f = transform.forward; f.y = 0f;
            f = f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
            block.SetVector(GaitId, new Vector4(f.x, f.z, Stride - Mathf.Floor(Stride), Swing));
            rend.SetPropertyBlock(block);
            has = true;
        }
    }
}
