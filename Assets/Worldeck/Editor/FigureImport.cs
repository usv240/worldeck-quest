using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Worldeck.EditorTools
{
    /// <summary>
    /// Mara's figure (Resources/Mara/mara.fbx, Kenney Mini Characters, CC0): legacy animation clips played by name
    /// ("idle" loops), no imported materials (she wears Worldeck/Figure with her colour map), no cameras or lights.
    /// </summary>
    public sealed class FigureImport : AssetPostprocessor
    {
        static bool Ours(string path) => path.Replace('\\', '/').Contains("/Resources/Mara/");

        void OnPreprocessModel()
        {
            if (!Ours(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            mi.animationType = ModelImporterAnimationType.Legacy;
            mi.importAnimation = true;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importBlendShapes = false;
            mi.isReadable = false;
            mi.globalScale = 1f;
        }

        void OnPreprocessAnimation()
        {
            if (!Ours(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            var clips = mi.defaultClipAnimations;
            if (clips == null || clips.Length == 0) return;
            foreach (var c in clips)
            {
                bool loop = c.name == "idle" || c.name.EndsWith("|idle") || c.name == "walk";
                c.loopTime = loop;
                c.wrapMode = loop ? WrapMode.Loop : WrapMode.Once;
            }
            mi.clipAnimations = clips;
        }

        /// <summary>For setup and tests: the clip names the figure has.</summary>
        public static string[] ClipNames(string path) =>
            AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).Select(c => c.name).ToArray();
    }
}
