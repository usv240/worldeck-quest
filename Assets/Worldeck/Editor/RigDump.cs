using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Worldeck.EditorTools
{
    /// <summary>Dev aid: prints the interaction rig's hierarchy (active state and components) to Tools/rig-dump.txt.</summary>
    public static class RigDump
    {
        public static void Dump()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Worldeck/Scenes/Worldeck.unity");
            var sb = new StringBuilder();
            foreach (var root in scene.GetRootGameObjects()) Walk(root.transform, 0, sb);
            System.IO.File.WriteAllText("Tools/rig-dump.txt", sb.ToString());
            Debug.Log("[Worldeck] rig dump written");
        }

        static void Walk(Transform t, int depth, StringBuilder sb)
        {
            if (depth > 9) return;
            var comps = t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name);
            sb.Append(new string(' ', depth * 2)).Append(t.gameObject.activeSelf ? "+ " : "- ").Append(t.name).Append("  [").Append(string.Join(", ", comps)).Append("]\n");
            foreach (Transform c in t) Walk(c, depth + 1, sb);
        }
    }
}
