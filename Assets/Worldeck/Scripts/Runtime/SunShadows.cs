using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Worldeck
{
    /// <summary>
    /// Shadows on the ground: trees, rocks, your furniture and whoever lives here are drawn once, seen from the sun,
    /// into one texture (SunCaster.shader), and the ground shader reads it. It is drawn again only when something
    /// changes (a new world, the sun moving, furniture appearing, you jumping far away), never every frame, so it
    /// costs four texture reads on the ground and nothing else. Animals that wander are left out: a shadow that
    /// stays behind looks worse than none.
    /// </summary>
    public sealed class SunShadows : MonoBehaviour
    {
        public WorldBuilder Builder;
        public RoomSkins Skins;
        public WorldLook Look;
        public Transform Head;
        public Material Caster;

        /// <summary>Texels along each side of the shadow texture.</summary>
        public const int Size = 2048;
        /// <summary>How far around you the ground has shadows (metres).</summary>
        public const float Radius = 22f;
        /// <summary>The tallest thing that still casts all of its shadow (metres).</summary>
        public const float Tall = 16f;
        /// <summary>The sun never casts from lower than this (degrees): shadows stay shorter than four times a thing's height.</summary>
        public const float LowestSun = 14f;

        /// <summary>The device can do it (a half-float texture that keeps the nearest of what is drawn into it).</summary>
        public bool Supported { get; private set; }
        /// <summary>How many times the texture has been drawn (for tests and the log).</summary>
        public int Renders { get; private set; }
        /// <summary>How many meshes went into the last drawing.</summary>
        public int Casters { get; private set; }
        /// <summary>The sun the last drawing was made for.</summary>
        public Vector3 Sun { get; private set; }

        static readonly int MapId = Shader.PropertyToID("_SwShadowMap"), VpId = Shader.PropertyToID("_SwShadowVP"),
            OnId = Shader.PropertyToID("_SwShadowOn"), TexelId = Shader.PropertyToID("_SwShadowTexel"),
            CasterMId = Shader.PropertyToID("_SwCasterM"), CasterVpId = Shader.PropertyToID("_SwCasterVP");

        RenderTexture map;
        CommandBuffer cmd;
        MaterialPropertyBlock block;
        Transform lastRoot;
        Vector3 centre;
        int lastSkins = -1;
        float lastAt = -10f, refreshAt = -1f;
        bool on;
        readonly List<MeshFilter> found = new List<MeshFilter>();
        // What casts, collected once per world (and again when the furniture changes), not at every redraw.
        readonly List<MeshFilter> casters = new List<MeshFilter>();
        readonly List<Renderer> casterRenderers = new List<Renderer>();
        bool collect = true;

        void Awake()
        {
            Shader.SetGlobalFloat(OnId, 0f);
            var fmt = RenderTextureFormat.RHalf;
            if (!SystemInfo.SupportsRenderTextureFormat(fmt) || !SystemInfo.SupportsBlendingOnRenderTextureFormat(fmt)) fmt = RenderTextureFormat.ARGBHalf;
            Supported = Caster != null && SystemInfo.SupportsRenderTextureFormat(fmt) && SystemInfo.SupportsBlendingOnRenderTextureFormat(fmt);
            if (!Supported) { Trace.Info("Look", "ground shadows off", "why", Caster == null ? "no material" : "no half-float blending"); return; }
            map = new RenderTexture(Size, Size, 0, fmt, RenderTextureReadWrite.Linear)
            {
                name = "Sun shadows", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, useMipMap = false, antiAliasing = 1
            };
            map.Create();
            cmd = new CommandBuffer { name = "Sun shadows" };
            block = new MaterialPropertyBlock();
        }

        void OnDestroy()
        {
            Shader.SetGlobalFloat(OnId, 0f);
            if (map != null) { map.Release(); Destroy(map); }
            cmd?.Release();
        }

        void OnDisable() { Shader.SetGlobalFloat(OnId, 0f); on = false; }

        void LateUpdate()
        {
            var root = Builder != null ? Builder.Root : null;
            bool want = Supported && root != null && root.gameObject.activeInHierarchy && Look != null && Builder.Room != null;
            if (!want)
            {
                if (on) { Shader.SetGlobalFloat(OnId, 0f); on = false; }
                lastRoot = null;
                return;
            }
            float now = Time.unscaledTime;
            Vector3 sun = SunFor(Look.Now.SunDir);
            Vector3 c = Head != null ? Head.position : Builder.Room.Head;
            c.y = Builder.Room.FloorY;
            int skins = Skins != null ? Skins.All.Count * 2 + (Skins.Growing ? 1 : 0) : 0;
            if (skins != lastSkins) { lastSkins = skins; refreshAt = now + 0.3f; collect = true; }
            bool newWorld = root != lastRoot;
            if (newWorld) collect = true;
            bool moved = Vector3.Angle(sun, Sun) > 1.5f || Flat(c - centre) > 8f;
            bool due = (refreshAt > 0f && now >= refreshAt) || now - lastAt > 5f;
            // A new world at once; anything else at most four times a second.
            if (!newWorld && !((moved || due) && now - lastAt > 0.5f)) return;   // shadows step at most twice a second
            lastRoot = root;
            refreshAt = -1f;
            lastAt = now;
            Draw(root, sun, c);
            if (!on) { Shader.SetGlobalFloat(OnId, 1f); on = true; }
        }

        static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

        /// <summary>The direction towards the sun used for shadows: never lower than <see cref="LowestSun"/>.</summary>
        public static Vector3 SunFor(Vector3 dir)
        {
            if (dir.sqrMagnitude < 1e-6f) return Vector3.up;
            dir.Normalize();
            float low = Mathf.Sin(LowestSun * Mathf.Deg2Rad);
            if (dir.y >= low) return dir;
            Vector3 flat = new Vector3(dir.x, 0f, dir.z);
            flat = flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward;
            return (flat * Mathf.Cos(LowestSun * Mathf.Deg2Rad) + Vector3.up * low).normalized;
        }

        /// <summary>
        /// World position to (u, v, distance), each 0..1: u and v across the texture, distance 0 nearest the sun.
        /// It covers a disc of ground <see cref="Radius"/> wide round <paramref name="c"/> and things up to
        /// <see cref="Tall"/> high on it.
        /// </summary>
        public static Matrix4x4 LightMatrix(Vector3 sun, Vector3 c, out float depthRange)
        {
            Vector3 fwd = -sun;   // the way the light travels
            Vector3 upRef = Mathf.Abs(sun.y) > 0.99f ? Vector3.forward : Vector3.up;
            Vector3 right = Vector3.Cross(upRef, fwd).normalized;
            Vector3 up = Vector3.Cross(fwd, right);
            float sinE = Mathf.Clamp01(sun.y), cosE = Mathf.Sqrt(Mathf.Max(0f, 1f - sinE * sinE));
            float hx = Radius, hy = Radius * sinE + Tall * cosE + 1f, hz = Radius * cosE + Tall * sinE + 2f;
            depthRange = hz * 2f;
            var m = Matrix4x4.identity;
            m.SetRow(0, new Vector4(right.x, right.y, right.z, -Vector3.Dot(right, c)) / (2f * hx) + new Vector4(0f, 0f, 0f, 0.5f));
            m.SetRow(1, new Vector4(up.x, up.y, up.z, -Vector3.Dot(up, c)) / (2f * hy) + new Vector4(0f, 0f, 0f, 0.5f));
            m.SetRow(2, new Vector4(fwd.x, fwd.y, fwd.z, -Vector3.Dot(fwd, c)) / (2f * hz) + new Vector4(0f, 0f, 0f, 0.5f));
            m.SetRow(3, new Vector4(0f, 0f, 0f, 1f));
            return m;
        }

        /// <summary>Whether this mesh stands between the sun and the ground: solid, in the world, and staying put.</summary>
        static bool Casts(MeshFilter f)
        {
            if (f == null || f.sharedMesh == null) return false;
            var r = f.GetComponent<Renderer>();
            if (r == null || r.sharedMaterial == null || r.sharedMaterial.shader == null) return false;
            if (r.sharedMaterial.shader.name != "Worldeck/World") return false;   // not the sky, glows, the photographed ground
            string n = f.gameObject.name;
            if (n == "Ground" || n == "Sea" || n == "Clouds" || n == "Planet" || n == "PlanetRing") return false;
            return f.GetComponentInParent<Wild>() == null;   // wandering animals: a shadow left behind looks worse than none
        }

        void Draw(Transform root, Vector3 sun, Vector3 c)
        {
            var m = LightMatrix(sun, c, out float range);
            if (collect)
            {
                collect = false;
                casters.Clear(); casterRenderers.Clear();
                found.Clear();
                root.GetComponentsInChildren(true, found);
                foreach (var f in found) if (Casts(f)) { casters.Add(f); casterRenderers.Add(f.GetComponent<Renderer>()); }
                if (Skins != null)
                {
                    found.Clear();
                    Skins.GetComponentsInChildren(true, found);
                    foreach (var f in found) if (Casts(f)) { casters.Add(f); casterRenderers.Add(f.GetComponent<Renderer>()); }
                }
            }
            cmd.Clear();
            cmd.SetRenderTarget(map);
            cmd.ClearRenderTarget(false, true, Color.white);
            cmd.SetGlobalMatrix(CasterVpId, m);
            int drawn = 0;
            float reach = Radius + Tall * 4f;
            for (int i = 0; i < casters.Count; i++)
            {
                var f = casters[i];
                // Whoever stepped aside (for Mara's call) or is not grown yet casts nothing just now.
                if (f == null || casterRenderers[i] == null || !casterRenderers[i].enabled || !f.gameObject.activeInHierarchy) continue;
                var b = casterRenderers[i].bounds;
                Vector3 nearest = b.ClosestPoint(new Vector3(c.x, b.center.y, c.z));
                if (Flat(nearest - c) > reach) continue;
                block.SetMatrix(CasterMId, f.transform.localToWorldMatrix);
                for (int s = 0; s < f.sharedMesh.subMeshCount; s++) cmd.DrawMesh(f.sharedMesh, f.transform.localToWorldMatrix, Caster, s, 0, block);
                drawn++;
            }
            Graphics.ExecuteCommandBuffer(cmd);
            Shader.SetGlobalTexture(MapId, map);
            Shader.SetGlobalMatrix(VpId, m);
            // xy: one texel; z: how much nearer the sun a thing must be to shade the ground (0.18 m); w: how sharply.
            Shader.SetGlobalVector(TexelId, new Vector4(1f / Size, 1f / Size, 0.18f / range, range / 0.12f));
            centre = c;
            Sun = sun;
            Casters = drawn;
            Renders++;
            if (Renders == 1) Trace.Info("Look", "ground shadows", "meshes", drawn, "format", map.format.ToString(), "size", Size);
        }

        /// <summary>The share of the texture something stands in (for tests): 0 nothing drawn, 1 everything in shadow.</summary>
        public float Coverage()
        {
            if (map == null) return 0f;
            const int n = 128;
            var small = RenderTexture.GetTemporary(n, n, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            Graphics.Blit(map, small);
            var prev = RenderTexture.active;
            RenderTexture.active = small;
            var tex = new Texture2D(n, n, TextureFormat.RGBAHalf, false, true);
            tex.ReadPixels(new Rect(0, 0, n, n), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(small);
            int covered = 0;
            foreach (var p in tex.GetPixels()) if (p.r < 0.995f) covered++;
            Destroy(tex);
            return covered / (float)(n * n);
        }
    }
}
