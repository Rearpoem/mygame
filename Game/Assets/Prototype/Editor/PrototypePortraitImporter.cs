using System;
using UnityEditor;

namespace Prototype.Editor
{
    /// <summary>Imports the five byte-identical temporary common portraits as transparent UI sprites.</summary>
    public sealed class PrototypePortraitImporter : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Prototype/Art/UICandidates/Common/Resources/PrototypeUI/Portraits/", StringComparison.Ordinal)) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.sRGBTexture = true;
            importer.maxTextureSize = 256;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.spritePixelsPerUnit = 100;
        }
    }
}
