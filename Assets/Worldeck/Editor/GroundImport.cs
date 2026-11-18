using UnityEditor;
using UnityEngine;

namespace Worldeck.EditorTools
{
    /// <summary>
    /// How the photographic ground textures import: mipmaps and anisotropic filtering (the ground is seen at a
    /// slant), 1024 px, ASTC on Quest; the normal maps as normal maps (OpenGL convention, as Poly Haven's _nor_gl).
    /// </summary>
    public sealed class GroundImport : AssetPostprocessor
    {
        public const string Folder = "Assets/Worldeck/Grounds/";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder, System.StringComparison.Ordinal)) return;
            var ti = (TextureImporter)assetImporter;
            bool normal = assetPath.EndsWith("_normal.png", System.StringComparison.Ordinal);
            ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = !normal;
            ti.mipmapEnabled = true;
            ti.anisoLevel = 4;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.filterMode = FilterMode.Trilinear;
            ti.maxTextureSize = 1024;
            ti.textureCompression = TextureImporterCompression.Compressed;
            ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Android", overridden = true, maxTextureSize = 1024,
                format = normal ? TextureImporterFormat.ASTC_5x5 : TextureImporterFormat.ASTC_6x6, compressionQuality = 60
            });
        }
    }
}
