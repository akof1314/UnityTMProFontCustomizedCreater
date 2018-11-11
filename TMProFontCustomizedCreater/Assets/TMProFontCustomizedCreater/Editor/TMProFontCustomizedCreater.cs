using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using TMPro;
using TMPro.EditorUtilities;
using UnityEngine;
using UnityEditor;

public class TMProFontCustomizedCreater : EditorWindow
{
    [MenuItem("界面工具/TextMeshPro工具/TextMeshPro 字库生成工具")]
    static void Open()
    {
        var settings = new CustomizedCreaterSettings
        {
            fontFolderPath = "Assets/Demo/Fonts",
            fontMaterialsFolderPath = "Assets/Demo/Fonts & Materials",
            fontBackupPaths = new[] {kFontFolderPath + "/FZYaSong-B.TTF", kFontFolderPath + "/SYHT.OTF"},
            pointSizeSamplingMode = 1,
            pointSize = 22,
            padding = 5,
            packingMode = 0,    // Fast
            atlasWidth = 2048,
            atlasHeight = 2048,
            characterSetSelectionMode = 8,  // Character List from File
            characterSequenceFile = "Assets/Demo/chinese_3500.txt",
            fontStyle = 
        };

        var window = GetWindow<TMProFontCustomizedCreaterWindow>();
        window.SetCustomizedCreaterSettings(settings);
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

    /// <summary>
    /// 字体的目录
    /// </summary>
    private const string kFontFolderPath = "Assets/Demo/Fonts";

    /// <summary>
    /// 字体材质的目录
    /// </summary>
    private const string kFontMaterialsFolderPath = "Assets/Demo/Fonts & Materials";

    /// <summary>
    /// 备用字体路径
    /// </summary>
    private static string[] kFontBackupPaths = {kFontFolderPath + "/FZYaSong-B.TTF", kFontFolderPath + "/SYHT.OTF"};

    private UnityEngine.Object font_TTF;
    private int font_size;
    private string characterSequence = "";
    private string output_feedback = "";
    private string output_name_label = "Font: ";
    private string output_size_label = "Pt. Size: ";
    private string output_count_label = "Characters packed: ";
    private int font_padding = 5;
    private int font_atlas_width = 512;
    private int font_atlas_height = 512;
    private FaceStyles font_style = FaceStyles.Normal;
    private RenderModes font_renderMode = RenderModes.DistanceField16;
    private int m_fontPackingSelection = 0;

    private int m_character_Count;
    private byte[] m_texture_buffer;
    private FT_FaceInfo m_font_faceInfo;
    private FT_GlyphInfo[] m_font_glyphInfo;
    private float font_style_mod = 2f;

    private bool isRenderingDone = false;
    private bool isProcessing = false;

    private Texture2D m_font_Atlas;

    private int DoCreate()
    {
        int error_Code = TMPro_FontPlugin.Initialize_FontEngine();
        if (error_Code != 0)
        {
            if (error_Code == 99)
            {
                error_Code = 0;
            }
            else
            {
                Debug.Log(
                    (object)("Error Code: " + error_Code + "  occurred while Initializing the FreeType Library."));
            }
        }

        string assetPath = AssetDatabase.GetAssetPath(this.font_TTF);
        if (error_Code == 0)
        {
            error_Code = TMPro_FontPlugin.Load_TrueType_Font(assetPath);
            if (error_Code != 0)
            {
                if (error_Code == 99)
                {
                    error_Code = 0;
                }
                else
                {
                    Debug.Log((object)("Error Code: " + error_Code + "  occurred while Loading the font."));
                }
            }
        }

        if (error_Code == 0)
        {
            //if (this.FontSizingOption_Selection == 0)
            //{
            //    this.font_size = 72;
            //}
            error_Code = TMPro_FontPlugin.FT_Size_Font(this.font_size);
            if (error_Code != 0)
            {
                Debug.Log((object)("Error Code: " + error_Code + "  occurred while Sizing the font."));
            }
        }

        if (error_Code == 0)
        {
            int[] character_Set = null;
            //if (this.font_CharacterSet_Selection == 7 || this.font_CharacterSet_Selection == 8)
            {
                List<int> list = new List<int>();
                int i;
                for (i = 0; i < this.characterSequence.Length; i++)
                {
                    if (list.FindIndex((int item) => item == this.characterSequence[i]) == -1)
                    {
                        list.Add(this.characterSequence[i]);
                    }
                }

                character_Set = list.ToArray();
            }
            //else if (this.font_CharacterSet_Selection == 6)
            //{
            //    character_Set = this.ParseHexNumberSequence(this.characterSequence);
            //}
            //else
            //{
            //    character_Set = this.ParseNumberSequence(this.characterSequence);
            //}
            this.m_character_Count = character_Set.Length;
            this.m_texture_buffer = new byte[this.font_atlas_width * this.font_atlas_height];
            this.m_font_faceInfo = default(FT_FaceInfo);
            this.m_font_glyphInfo = new FT_GlyphInfo[this.m_character_Count];
            int padding = this.font_padding;
            bool autoSizing = false;
            float strokeSize = this.font_style_mod;
            if (this.font_renderMode == RenderModes.DistanceField16)
            {
                strokeSize = this.font_style_mod * 16f;
            }
            if (this.font_renderMode == RenderModes.DistanceField32)
            {
                strokeSize = this.font_style_mod * 32f;
            }
            this.isProcessing = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                this.isRenderingDone = false;
                error_Code = TMPro_FontPlugin.Render_Characters(this.m_texture_buffer, this.font_atlas_width,
                    this.font_atlas_height, padding, character_Set, this.m_character_Count, this.font_style, strokeSize,
                    autoSizing, this.font_renderMode, (int)this.m_fontPackingSelection, ref this.m_font_faceInfo,
                    this.m_font_glyphInfo);

                this.ThreadRenderBackupFont(1, font_atlas_width);

                this.isRenderingDone = true;
            });
        }

        return error_Code;
    }

    private void ThreadRenderBackupFont(int backupLevel, int xOffsetDist)
    {
        string fontPath;
        if (backupLevel == 1)
        {
            fontPath = "Assets/TextMeshPro/NewFonts/FZYaSong-B.TTF";
        }
        else if (backupLevel == 2)
        {
            fontPath = "Assets/TextMeshPro/NewFonts/SYHT.OTF";
        }
        else
        {
            return;
        }

        List<int> list = new List<int>();
        for (int index = 0; index < this.m_character_Count; ++index)
        {
            if ((double)this.m_font_glyphInfo[index].x == -1.0)
            {
                list.Add(m_font_glyphInfo[index].id);
            }
        }
        if (list.Count == 0)
        {
            return;
        }

        int[] character_Set = list.ToArray();
        int error_Code = TMPro_FontPlugin.Load_TrueType_Font(fontPath);
        if (error_Code != 0)
        {
            return;
        }

        var tmp_font_atlas_width = 512;
        var tmp_font_atlas_height = 512;
        var tmp_texture_buffer = new byte[tmp_font_atlas_width * tmp_font_atlas_height];
        var tmp_character_Count = character_Set.Length;
        var tmp_font_faceInfo = default(FT_FaceInfo);
        var tmp_font_glyphInfo = new FT_GlyphInfo[tmp_character_Count];
        error_Code = TMPro_FontPlugin.Render_Characters(tmp_texture_buffer, tmp_font_atlas_width,
            tmp_font_atlas_height, font_padding, character_Set, tmp_character_Count, this.font_style, font_style_mod * 16f,
            false, this.font_renderMode, (int)this.m_fontPackingSelection, ref tmp_font_faceInfo,
            tmp_font_glyphInfo);
        if (error_Code != 0)
        {
            return;
        }

        // 目前固定宽度，因为缺字数量比较少，以后可以进行优化
        int wordWidth = 22;
        int xStart = xOffsetDist - font_padding * 2 - wordWidth;   // 从padding开始拷贝，否则会出现负偏移丢失的情况
        int yStart = font_atlas_height - font_padding - 1;
        int numY = 0;
        for (int index = 0; index < tmp_character_Count; ++index)
        {
            if (!Mathf.Approximately(tmp_font_glyphInfo[index].x, -1))
            {
                var gi = tmp_font_glyphInfo[index];
                var x = Mathf.FloorToInt(gi.x) - font_padding;
                var y = tmp_font_atlas_height - (Mathf.FloorToInt(gi.y) - font_padding);
                var w = Mathf.CeilToInt(gi.width) + font_padding * 2;
                var h = Mathf.CeilToInt(gi.height) + font_padding * 2;

                for (int r = 0; r < h; r++)
                {
                    for (int c = 0; c < w; c++)
                    {
                        this.m_texture_buffer[(yStart - r) * font_atlas_width + c + xStart] =
                            tmp_texture_buffer[(y - r) * tmp_font_atlas_width + c + x];
                    }
                }
                var idx = ArrayUtility.FindIndex(m_font_glyphInfo, info => info.id == gi.id);
                if (idx != -1)
                {
                    var gi2 = m_font_glyphInfo[idx];
                    gi2.x = xStart + font_padding;
                    gi2.y = font_atlas_height - yStart + font_padding;
                    gi2.width = gi.width;
                    gi2.height = gi.height;
                    gi2.xAdvance = gi.xAdvance;
                    gi2.xOffset = gi.xOffset;
                    gi2.yOffset = gi.yOffset;
                    m_font_glyphInfo[idx] = gi2;
                }

                yStart = yStart - h - font_padding - 1;
                numY++;

                // 如果超过五个则换一列
                if (numY > 5)
                {
                    numY = 0;
                    xStart = xStart - font_padding * 2 - wordWidth;
                    yStart = font_atlas_height - font_padding - 1;
                }
            }
        }

        ThreadRenderBackupFont(++backupLevel, xStart);
    }

    private void UpdateRenderFeedbackWindow()
    {
        //this.font_size = this.m_font_faceInfo.pointSize;
        //string empty = string.Empty;
        string str1 = this.m_font_faceInfo.characterCount == this.m_character_Count ? "<color=#C0ffff>" : "<color=#ffff00>";
        string str2 = "<color=#C0ffff>";
        string str3 = this.output_name_label + "<b>" + str2 + this.m_font_faceInfo.name + "</color></b>";
        string str4;
        if (str3.Length > 60)
            str4 = str3 + "\n" + this.output_size_label + "<b>" + str2 + (object)this.m_font_faceInfo.pointSize + "</color></b>";
        else
            str4 = str3 + "  " + this.output_size_label + "<b>" + str2 + (object)this.m_font_faceInfo.pointSize + "</color></b>";
        string input = str4 + "\n" + this.output_count_label + "<b>" + str1 + (object)this.m_font_faceInfo.characterCount + "/" + (object)this.m_character_Count + "</color></b>" + "\n\n<color=#ffff00><b>Missing Characters</b></color>" + "\n----------------------------------------";
        this.output_feedback = input;
        for (int index = 0; index < this.m_character_Count; ++index)
        {
            if ((double)this.m_font_glyphInfo[index].x == -1.0)
            {
                input = input + "\nID: <color=#C0ffff>" + (object)this.m_font_glyphInfo[index].id + "\t</color>Hex: <color=#C0ffff>" + this.m_font_glyphInfo[index].id.ToString("X") + "\t</color>Char [<color=#C0ffff>" + ((char)this.m_font_glyphInfo[index].id).ToString() + "</color>]";
                if (input.Length < 16300)
                    this.output_feedback = input;
            }
        }
        if (input.Length > 16300)
            this.output_feedback += "\n\n<color=#ffff00>Report truncated.</color>\n<color=#c0ffff>See</color> \"TextMesh Pro\\Glyph Report.txt\"";
        Debug.Log(output_feedback);
        //File.WriteAllText(Path.GetFullPath("Assets/..") + "/Assets/Glyph Report.txt", Regex.Replace(input, "<[^>]*>", string.Empty));
        //AssetDatabase.Refresh();
    }

    private void CreateFontTexture()
    {
        this.m_font_Atlas = new Texture2D(this.font_atlas_width, this.font_atlas_height, TextureFormat.Alpha8, false, true);
        Color32[] colors = new Color32[this.font_atlas_width * this.font_atlas_height];
        for (int index = 0; index < this.font_atlas_width * this.font_atlas_height; ++index)
        {
            byte num = this.m_texture_buffer[index];
            colors[index] = new Color32(num, num, num, num);
        }
        if (this.font_renderMode == RenderModes.RasterHinted)
            this.m_font_Atlas.filterMode = UnityEngine.FilterMode.Point;
        this.m_font_Atlas.SetPixels32(colors, 0);
        this.m_font_Atlas.Apply(false, true);
    }

    private FaceInfo GetFaceInfo(FT_FaceInfo ft_face, int scaleFactor)
    {
        FaceInfo faceInfo = new FaceInfo();
        faceInfo.Name = ft_face.name;
        faceInfo.PointSize = (float)ft_face.pointSize / (float)scaleFactor;
        faceInfo.Padding = (float)(ft_face.padding / scaleFactor);
        faceInfo.LineHeight = ft_face.lineHeight / (float)scaleFactor;
        faceInfo.CapHeight = 0.0f;
        faceInfo.Baseline = 0.0f;
        faceInfo.Ascender = ft_face.ascender / (float)scaleFactor;
        faceInfo.Descender = ft_face.descender / (float)scaleFactor;
        faceInfo.CenterLine = ft_face.centerLine / (float)scaleFactor;
        faceInfo.Underline = ft_face.underline / (float)scaleFactor;
        faceInfo.UnderlineThickness = (double)ft_face.underlineThickness == 0.0 ? 5f : ft_face.underlineThickness / (float)scaleFactor;
        faceInfo.strikethrough = (float)(((double)faceInfo.Ascender + (double)faceInfo.Descender) / 2.75);
        faceInfo.strikethroughThickness = faceInfo.UnderlineThickness;
        faceInfo.SuperscriptOffset = faceInfo.Ascender;
        faceInfo.SubscriptOffset = faceInfo.Underline;
        faceInfo.SubSize = 0.5f;
        faceInfo.AtlasWidth = (float)(ft_face.atlasWidth / scaleFactor);
        faceInfo.AtlasHeight = (float)(ft_face.atlasHeight / scaleFactor);
        return faceInfo;
    }

    private TMP_Glyph[] GetGlyphInfo(FT_GlyphInfo[] ft_glyphs, int scaleFactor)
    {
        List<TMP_Glyph> tmpGlyphList = new List<TMP_Glyph>();
        List<int> intList = new List<int>();
        for (int index = 0; index < ft_glyphs.Length; ++index)
        {
            TMP_Glyph tmpGlyph = new TMP_Glyph();
            tmpGlyph.id = ft_glyphs[index].id;
            tmpGlyph.x = ft_glyphs[index].x / (float)scaleFactor;
            tmpGlyph.y = ft_glyphs[index].y / (float)scaleFactor;
            tmpGlyph.width = ft_glyphs[index].width / (float)scaleFactor;
            tmpGlyph.height = ft_glyphs[index].height / (float)scaleFactor;
            tmpGlyph.xOffset = ft_glyphs[index].xOffset / (float)scaleFactor;
            tmpGlyph.yOffset = ft_glyphs[index].yOffset / (float)scaleFactor;
            tmpGlyph.xAdvance = ft_glyphs[index].xAdvance / (float)scaleFactor;
            if ((double)tmpGlyph.x != -1.0)
            {
                tmpGlyphList.Add(tmpGlyph);
                intList.Add(tmpGlyph.id);
            }
        }
        //this.m_kerningSet = intList.ToArray();
        return tmpGlyphList.ToArray();
    }

    private void Save_SDF_FontAsset(string filePath)
    {
        string withoutExtension = Path.GetFileNameWithoutExtension(filePath);
        TMP_FontAsset tmpFontAsset = AssetDatabase.LoadAssetAtPath(filePath, typeof(TMP_FontAsset)) as TMP_FontAsset;
        Material[] materialReferences = TMP_EditorUtility.FindMaterialReferences(tmpFontAsset);
        if (tmpFontAsset.atlas)
        {
            UnityEngine.Object.DestroyImmediate((UnityEngine.Object)tmpFontAsset.atlas, true);
        }
        tmpFontAsset.fontAssetType = TMP_FontAsset.FontAssetTypes.SDF;
        //int scaleFactor = this.font_renderMode >= RenderModes.DistanceField16 ? 1 : this.font_scaledownFactor;
        int scaleFactor = 1;
        FaceInfo faceInfo = this.GetFaceInfo(this.m_font_faceInfo, scaleFactor);
        tmpFontAsset.AddFaceInfo(faceInfo);
        TMP_Glyph[] glyphInfo = this.GetGlyphInfo(this.m_font_glyphInfo, scaleFactor);
        tmpFontAsset.AddGlyphInfo(glyphInfo);
        //if (this.includeKerningPairs)
        //{
        //    KerningTable kerningTable = this.GetKerningTable(AssetDatabase.GetAssetPath(this.font_TTF), (int)faceInfo.PointSize);
        //    tmpFontAsset.AddKerningInfo(kerningTable);
        //}
        tmpFontAsset.atlas = this.m_font_Atlas;
        if (!m_font_Atlas.name.EndsWith(" Atlas"))
        {
            this.m_font_Atlas.name = withoutExtension + " Atlas";
            this.m_font_Atlas.hideFlags = HideFlags.None;
            AssetDatabase.AddObjectToAsset((UnityEngine.Object)this.m_font_Atlas, (UnityEngine.Object)tmpFontAsset);
        }
        tmpFontAsset.material.hideFlags = HideFlags.None;
        tmpFontAsset.material.SetTexture(ShaderUtilities.ID_MainTex, (Texture)tmpFontAsset.atlas);
        for (int index = 0; index < materialReferences.Length; ++index)
        {
            materialReferences[index].SetTexture(ShaderUtilities.ID_MainTex, (Texture)this.m_font_Atlas);
            materialReferences[index].SetFloat(ShaderUtilities.ID_TextureWidth, (float)this.m_font_Atlas.width);
            materialReferences[index].SetFloat(ShaderUtilities.ID_TextureHeight, (float)this.m_font_Atlas.height);
            int num = this.font_padding + 1;
            materialReferences[index].SetFloat(ShaderUtilities.ID_GradientScale, (float)num);
            materialReferences[index].SetFloat(ShaderUtilities.ID_WeightNormal, tmpFontAsset.normalStyle);
            materialReferences[index].SetFloat(ShaderUtilities.ID_WeightBold, tmpFontAsset.boldStyle);
        }
        //var pngData = m_font_Atlas.EncodeToPNG();
        //File.WriteAllBytes("Assets/Textures/Debug Distance Field.png", pngData);
        tmpFontAsset.ReadFontDefinition();
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath((UnityEngine.Object)tmpFontAsset));
        AssetDatabase.Refresh();
        //this.m_font_Atlas = (Texture2D)null; // 贴图不删除
        TMPro_EventManager.ON_FONT_PROPERTY_CHANGED(true, tmpFontAsset);
    }

    private readonly List<FontAssetInfo> m_FontAssetInfos = new List<FontAssetInfo>();
    private int m_CurGenerateIndex;

    void Awake()
    {
        titleContent.text = "字库生成";
        minSize = new Vector2(340f, 360f);
    }

    void OnEnable()
    {
        FindFonts();
    }

    void OnDisable()
    {
        if (TMPro_FontPlugin.Initialize_FontEngine() == 99)
            TMPro_FontPlugin.Destroy_FontEngine();
        if ((UnityEngine.Object)this.m_font_Atlas != (UnityEngine.Object)null && !EditorUtility.IsPersistent((UnityEngine.Object)this.m_font_Atlas))
            UnityEngine.Object.DestroyImmediate((UnityEngine.Object)this.m_font_Atlas);
        UnityEngine.Resources.UnloadUnusedAssets();
    }

    public void Update()
    {
        // 第一步创建字体渲染数组
        if (this.isProcessing)
        {
            var progress = TMPro_FontPlugin.Check_RenderProgress();
            m_FontAssetInfos[m_CurGenerateIndex].genPercent = progress * 100;
            this.Repaint();
        }
        if (!this.isRenderingDone)
        {
            return;
        }
        this.isProcessing = false;
        this.isRenderingDone = false;
        // 第二步输出渲染结果
        this.UpdateRenderFeedbackWindow();
        // 第三步将渲染数组填充到纹理贴图（注意，贴图共享不删除）
        this.CreateFontTexture();
        foreach (var asset in m_FontAssetInfos[m_CurGenerateIndex].assets)
        {
            // 最后保存信息到字体资产
            Save_SDF_FontAsset(asset);
        }
        this.m_font_Atlas = (Texture2D)null;
        GenerateNext();
    }

    public void OnGUI()
    {
        GUI.enabled = !this.isProcessing;
        EditorGUI.indentLevel++;
        
        GUILayout.Label(" Font Asset", EditorStyles.boldLabel);
        if (GUILayout.Button("生成字库资产", GUILayout.MinHeight(22f)))
        {
            Generate();
        }

        GUILayout.Space(10f);
        GUILayout.Label(" Font List", EditorStyles.boldLabel);
        foreach (var info in m_FontAssetInfos)
        {
            EditorGUILayout.BeginHorizontal();
            info.toggle = EditorGUILayout.ToggleLeft(info.fontName, info.toggle);
            GUILayout.Space(10f);
            EditorGUILayout.LabelField(new GUIContent(String.Format("({0}%)", info.genPercent)));
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel++;
            EditorGUI.indentLevel++;
            foreach (var asset in info.assets)
            {
                EditorGUILayout.LabelField(new GUIContent(Path.GetFileNameWithoutExtension(asset)));
            }
            EditorGUI.indentLevel--;
            EditorGUI.indentLevel--;
        }
        EditorGUI.indentLevel--;
        GUI.enabled = true;
    }

    private void FindFonts()
    {
        // 以字体做索引，相同的字体只会生成一次字体纹理
        string str1 = "t:Font";
        string[] fonts = AssetDatabase.FindAssets(str1, new[] { kFontFolderPath });

        m_FontAssetInfos.Clear();
        foreach (var font in fonts)
        {
            FontAssetInfo info = new FontAssetInfo();
            info.fontPath = AssetDatabase.GUIDToAssetPath(font);
            info.fontName = Path.GetFileNameWithoutExtension(info.fontPath);
            info.fontObj = AssetDatabase.LoadAssetAtPath<Font>(info.fontPath);
            if (info.fontObj == null)
            {
                continue;
            }

            List<string> assetPaths = new List<string>();
            str1 = "t:TMP_FontAsset " + info.fontName + " SDF";
            var assets = AssetDatabase.FindAssets(str1, new[] { kFontMaterialsFolderPath });
            foreach (var asset in assets)
            {
                assetPaths.Add(AssetDatabase.GUIDToAssetPath(asset));
            }

            if (assetPaths.Count > 0)
            {
                info.assets = assetPaths.ToArray();
                m_FontAssetInfos.Add(info);
            }
        }
    }

    private void Generate()
    {
        m_CurGenerateIndex = -1;
        GenerateNext();
    }

    private void GenerateNext()
    {
        m_CurGenerateIndex++;
        if (m_CurGenerateIndex >= m_FontAssetInfos.Count)
        {
            EditorUtility.DisplayDialog("提示", "生成字库资产成功!", "OK");
            return;
        }

        var info = m_FontAssetInfos[m_CurGenerateIndex];
        if (!info.toggle)
        {
            GenerateNext();
            return;
        }

        font_TTF = info.fontObj;
        font_size = 22;
        font_atlas_width = 2048;
        font_atlas_height = 2048;
        if (string.IsNullOrEmpty(characterSequence))
        {
            var characterList = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/TextMeshPro/chinese_3500.txt");
            this.characterSequence = characterList.text;
        }
        DoCreate();
    }

    private class FontAssetInfo
    {
        public string fontPath;
        public string fontName;
        public UnityEngine.Object fontObj;
        public bool toggle = true; // 是否要生成
        public float genPercent;
        public string[] assets;
    }
}

