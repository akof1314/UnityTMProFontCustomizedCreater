using System;
using TMPro.EditorUtilities;
using UnityEditor;

public class TMProFontCustomizedCreater 
{
    [MenuItem("界面工具/TextMeshPro工具/TextMeshPro 字库生成工具")]
    static void Open()
    {
        EditorWindow.GetWindow<TMProFontCustomizedCreaterWindow>();
    }

    public static CustomizedCreaterSettings GetCustomizedCreaterSettings()
    {
        var settings = new CustomizedCreaterSettings
        {
            fontFolderPath = "Assets/Demo/Fonts",
            fontMaterialsFolderPath = "Assets/Demo/Fonts & Materials",
            fontBackupPaths = new[] { "Assets/Demo/Fonts/FZYaSong-B.TTF", "Assets/Demo/Fonts/SYHT.OTF" },
            pointSizeSamplingMode = 1,
            pointSize = 22,
            padding = 5,
            packingMode = 0,    // Fast
            atlasWidth = 2048,
            atlasHeight = 2048,
            characterSetSelectionMode = 8,  // Character List from File
            characterSequenceFile = "Assets/Demo/chinese_3500.txt",
            fontStyle = (int)FaceStyles.Normal,
            fontStyleModifier = 2,
            renderMode = (int)RenderModes.DistanceField16,
            includeFontFeatures = false
        };
        return settings;
    }

    public struct CustomizedCreaterSettings
    {
        /// <summary>
        /// 字体的目录
        /// </summary>
        public string fontFolderPath;

        /// <summary>
        /// 字体材质的目录
        /// </summary>
        public string fontMaterialsFolderPath;

        /// <summary>
        /// 备用字体路径
        /// </summary>
        public string[] fontBackupPaths;

        /// <summary>
        /// 字体大小模式（0表示自动大小，1表示自定义大小）
        /// </summary>
        public int pointSizeSamplingMode;

        /// <summary>
        /// 字体大小
        /// </summary>
        public int pointSize;

        /// <summary>
        /// 间距
        /// </summary>
        public int padding;

        public int packingMode;
        public int atlasWidth;
        public int atlasHeight;
        public int characterSetSelectionMode;
        public string characterSequenceFile;
        public int fontStyle;
        public float fontStyleModifier;
        public int renderMode;
        public bool includeFontFeatures;
    }
}
