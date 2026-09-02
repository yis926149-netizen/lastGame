using UnityEditor;
using UnityEngine;

/// <summary>
/// 卡面贴图导入规范：强制 Mesh Type = Full Rect。
///
/// 为什么必须是 Full Rect：
/// Unity 默认的 Tight 会沿不透明像素轮廓生成紧贴的多边形网格，裁掉四周透明边距。
/// 边缘流光（CardEdgeFlow.shader）要画的描边与外发光正好落在图形**外侧**的透明区域，
/// 那些像素在 Tight 网格之外，根本不会被光栅化 —— 表现为“描边宽度调到多大都没反应”。
///
/// 只作用于卡面目录，不影响其他 UI 图（Tight 对普通 UI 图是合理默认，能省填充率）。
/// </summary>
public class CardSpriteImportPostprocessor : AssetPostprocessor
{
    /// <summary>需要强制 Full Rect 的卡面贴图目录。新增卡面目录时在此追加。</summary>
    private static readonly string[] CardSpriteFolders =
    {
        "Assets/UI/UnitCards/",
        "Assets/UI/BuildingCards/",
    };

    private void OnPreprocessTexture()
    {
        if (!IsCardSprite(assetPath)) return;

        TextureImporter importer = (TextureImporter)assetImporter;

        // 非 Sprite 的图（若目录里混入了普通贴图）不动，避免误改导入类型。
        if (importer.textureType != TextureImporterType.Sprite) return;

        if (importer.spriteImportMode == SpriteImportMode.None) return;

        TextureImporterSettings settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);

        if (settings.spriteMeshType == SpriteMeshType.FullRect) return;

        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);

        Debug.Log($"[CardSpriteImport] {assetPath} -> Mesh Type = Full Rect（边缘流光需要完整矩形网格）");
    }

    private static bool IsCardSprite(string path)
    {
        foreach (string folder in CardSpriteFolders)
        {
            if (path.StartsWith(folder, System.StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
