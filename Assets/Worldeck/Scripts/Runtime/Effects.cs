using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// Particle systems built in code with the Glow material: weather (rain, snow, bubbles) in a box that
    /// follows the head, fireflies, embers, flames, fire glow, shooting stars and a burst for moments of
    /// success. Counts stay inside the research budget (≤ 1,000 live particles in total).
    /// </summary>
    public static class Effects
    {
        static ParticleSystem Make(string name, Transform parent, Material mat, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;
            main.loop = true;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.alignment = ParticleSystemRenderSpace.View;
            return ps;
        }

        static void Box(ParticleSystem ps, Vector3 size)
        {
            var sh = ps.shape;
            sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = size;
        }

        public static ParticleSystem Rain(Transform parent, Material mat)
        {
            var ps = Make("Rain", parent, mat, 600);
            var m = ps.main;
            m.startLifetime = 1.1f;
            m.startSpeed = 0f;
            m.startSize3D = true;
            m.startSizeX = 0.006f; m.startSizeY = 0.28f; m.startSizeZ = 0.006f;
            m.startColor = new Color(0.75f, 0.82f, 0.95f, 0.35f);
            m.gravityModifier = 0f;
            var v = ps.velocityOverLifetime;
            v.enabled = true; v.space = ParticleSystemSimulationSpace.World;
            v.x = new ParticleSystem.MinMaxCurve(-0.6f, -0.3f); v.y = new ParticleSystem.MinMaxCurve(-7.5f, -6.5f); v.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
            var e = ps.emission; e.rateOverTime = 420f;
            Box(ps, new Vector3(8f, 0.2f, 8f));
            ps.transform.localPosition = new Vector3(0, 4.5f, 0);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.03f; r.lengthScale = 1f;
            return ps;
        }

        public static ParticleSystem Snow(Transform parent, Material mat)
        {
            var ps = Make("Snow", parent, mat, 500);
            var m = ps.main;
            m.startLifetime = 7f;
            m.startSpeed = 0f;
            m.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.03f);
            m.startColor = new Color(1f, 1f, 1f, 0.8f);
            var v = ps.velocityOverLifetime;
            v.enabled = true; v.space = ParticleSystemSimulationSpace.World;
            v.x = new ParticleSystem.MinMaxCurve(-0.2f, 0.25f); v.y = new ParticleSystem.MinMaxCurve(-0.8f, -0.55f); v.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
            var n = ps.noise; n.enabled = true; n.strength = 0.3f; n.frequency = 0.4f;
            var e = ps.emission; e.rateOverTime = 70f;
            Box(ps, new Vector3(8f, 0.2f, 8f));
            ps.transform.localPosition = new Vector3(0, 4f, 0);
            return ps;
        }

        public static ParticleSystem Bubbles(Transform parent, Material mat)
        {
            var ps = Make("Bubbles", parent, mat, 80);
            var m = ps.main;
            m.startLifetime = 6f;
            m.startSpeed = 0f;
            m.startSize = new ParticleSystem.MinMaxCurve(0.01f, 0.035f);
            m.startColor = new Color(0.75f, 1f, 1f, 0.55f);
            var v = ps.velocityOverLifetime;
            v.enabled = true; v.space = ParticleSystemSimulationSpace.World;
            v.x = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f); v.y = new ParticleSystem.MinMaxCurve(0.3f, 0.6f); v.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            var n = ps.noise; n.enabled = true; n.strength = 0.15f; n.frequency = 0.8f;
            var e = ps.emission; e.rateOverTime = 8f;
            Box(ps, new Vector3(6f, 0.1f, 6f));
            ps.transform.localPosition = new Vector3(0, -1.2f, 0);
            return ps;
        }

        public static ParticleSystem Fireflies(Transform parent, Material mat, Color c)
        {
            var ps = Make("Fireflies", parent, mat, 40);
            var m = ps.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(5f, 9f);
            m.startSpeed = 0f;
            m.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.045f);
            m.startColor = c;
            var n = ps.noise; n.enabled = true; n.strength = 0.25f; n.frequency = 0.25f; n.scrollSpeed = 0.1f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0.3f, 0.45f),
                              new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var e = ps.emission; e.rateOverTime = 5f;
            Box(ps, new Vector3(9f, 1.6f, 9f));
            ps.transform.localPosition = new Vector3(0, 1.0f, 0);
            return ps;
        }

        public static ParticleSystem Embers(Transform parent, Material mat, float rate = 14f)
        {
            var ps = Make("Embers", parent, mat, 60);
            var m = ps.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 3f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.018f);
            m.startColor = new Color(1f, 0.6f, 0.25f, 1f);
            var n = ps.noise; n.enabled = true; n.strength = 0.12f; n.frequency = 1.2f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.85f, 0.5f), 0f), new GradientColorKey(new Color(0.9f, 0.25f, 0.1f), 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var e = ps.emission; e.rateOverTime = rate;
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 18f; sh.radius = 0.08f;
            ps.transform.localRotation = Quaternion.Euler(-90, 0, 0);
            return ps;
        }

        /// <summary>Flames: additive billboards rising from a small disc, white-yellow to orange to red.</summary>
        public static ParticleSystem Flames(Transform parent, Material mat, float radius = 0.08f)
        {
            var ps = Make("Flames", parent, mat, 60);
            var m = ps.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 0.9f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.22f);
            m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            m.startColor = new Color(1f, 0.95f, 0.7f, 1f);
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.95f, 0.69f), 0f), new GradientColorKey(new Color(1f, 0.6f, 0.24f), 0.4f),
                              new GradientColorKey(new Color(0.76f, 0.21f, 0.12f), 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.12f), new GradientAlphaKey(0.5f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var size = ps.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.25f));
            var e = ps.emission; e.rateOverTime = 34f;
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = radius;
            sh.rotation = new Vector3(-90, 0, 0);
            return ps;
        }

        public static ParticleSystem ShootingStars(Transform parent, Material mat)
        {
            var ps = Make("ShootingStars", parent, mat, 3);
            var m = ps.main;
            m.startLifetime = 0.9f;
            m.startSpeed = new ParticleSystem.MinMaxCurve(60f, 90f);
            m.startSize3D = true; m.startSizeX = 0.4f; m.startSizeY = 6f; m.startSizeZ = 0.4f;
            m.startColor = new Color(1f, 1f, 1f, 0.9f);
            var e = ps.emission; e.rateOverTime = 0.12f;
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(180f, 10f, 180f);
            ps.transform.localPosition = new Vector3(0, 110f, 0);
            ps.transform.localRotation = Quaternion.Euler(100f, 30f, 0f);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.08f; r.lengthScale = 2f;
            return ps;
        }

        /// <summary>A one-shot sparkle burst (success, an item placed, a souvenir appearing).</summary>
        /// <summary>Comfort & access: fewer, softer sparks and flashes.</summary>
        public static bool Calm;

        public static ParticleSystem Burst(Transform parent, Material mat, Color c)
        {
            var ps = Make("Burst", parent, mat, 40);
            var m = ps.main;
            m.loop = false;
            m.duration = 0.2f;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.01f, 0.025f);
            m.startColor = c;
            m.gravityModifier = 0.05f;
            var e = ps.emission; e.rateOverTime = 0f; e.SetBursts(new[] { new ParticleSystem.Burst(0f, Calm ? 6 : 24) });
            if (Calm) m.startColor = new Color(c.r, c.g, c.b, c.a * 0.5f);
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.03f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            return ps;
        }

        /// <summary>A soft camera-facing halo quad (fire glow, items, the guide).</summary>
        public static GameObject Halo(string name, Transform parent, Material mat, float size, Color c)
        {
            var go = Util.MeshObject(name, parent, Util.Quad(c, name), mat);
            go.transform.localScale = Vector3.one * size;
            go.AddComponent<Billboard>();
            return go;
        }
    }

    /// <summary>Turns a quad to face the main camera every frame.</summary>
    public sealed class Billboard : MonoBehaviour
    {
        Transform cam;
        void LateUpdate()
        {
            if (cam == null) { var c = Camera.main; if (c == null) return; cam = c.transform; }
            transform.rotation = Quaternion.LookRotation(transform.position - cam.position, Vector3.up);
        }
    }
}
