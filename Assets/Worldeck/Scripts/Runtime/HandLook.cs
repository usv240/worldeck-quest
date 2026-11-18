using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// How your hands look. In your real room they are your real hands (the hand mesh only hides what is behind
    /// them). Inside a world they are glowing outline hands (Meta's tracked hand mesh in the Ghost shader), so you
    /// see bright, clean hands instead of a camera cut-out of your own.
    /// </summary>
    public sealed class HandLook : MonoBehaviour
    {
        public Renderer[] Hands;
        public Material Mask, Glow;
        public bool InWorld;
        bool applied, first = true;

        void LateUpdate()
        {
            if (!first && applied == InWorld) return;
            first = false;
            applied = InWorld;
            var mat = InWorld && Glow != null ? Glow : Mask;
            if (Hands == null || mat == null) return;
            foreach (var r in Hands)
            {
                if (r == null) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
            }
            Trace.Info("Hands", InWorld ? "glowing hands (in a world)" : "real hands (in your room)", "renderers", Hands.Length);
        }
    }
}
