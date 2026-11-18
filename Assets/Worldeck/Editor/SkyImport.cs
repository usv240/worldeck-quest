using UnityEditor;
using UnityEngine;

namespace Worldeck.EditorTools
{
    /// <summary>
    /// How the photographed skies import: equirectangular, wrapping sideways only, no mipmaps (the shader always
    /// reads the full-size image, so the seam never blurs), sRGB, ASTC 6x6 on Quest. The campfire's night sky
    /// (the first one everyone sees) keeps 4096 px; the rest are 2048.
    /// </summary>
    public sealed class SkyImport : AssetPostprocessor
    {
        public const string Folder = "Assets/Worldeck/Skies/";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder, System.StringComparison.Ordinal)) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.textureShape = TextureImporterShape.Texture2D;
            ti.sRGBTexture = true;
            ti.mipmapEnabled = false;
            ti.wrapModeU = TextureWrapMode.Repeat;
            ti.wrapModeV = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Bilinear;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.alphaSource = TextureImporterAlphaSource.None;
            int size = assetPath.Contains("campfire_night") ? 4096 : 2048;
            ti.maxTextureSize = size;
            ti.textureCompression = TextureImporterCompression.Compressed;
            ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Android", overridden = true, maxTextureSize = size, format = TextureImporterFormat.ASTC_6x6,
                compressionQuality = 60
            });
        }
    }
}
