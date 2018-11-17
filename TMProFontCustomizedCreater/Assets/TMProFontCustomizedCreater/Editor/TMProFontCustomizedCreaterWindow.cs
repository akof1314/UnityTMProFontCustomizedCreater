using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using TMPro;
using TMPro.EditorUtilities;
using UnityEditor;
using UnityEngine;

public class TMProFontCustomizedCreaterWindow : EditorWindow
{
    // Diagnostics
    System.Diagnostics.Stopwatch m_StopWatch;

    //string[] m_FontSizingOptions = { "Auto Sizing", "Custom Size" };
    int m_PointSizeSamplingMode;
    //string[] m_FontResolutionLabels = { "16", "32", "64", "128", "256", "512", "1024", "2048", "4096", "8192" };
    //int[] m_FontAtlasResolutions = { 16, 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192 };
    //string[] m_FontCharacterSets = { "ASCII", "Extended ASCII", "ASCII Lowercase", "ASCII Uppercase", "Numbers + Symbols", "Custom Range", "Unicode Range (Hex)", "Custom Characters", "Characters from File" };
    enum FontPackingModes { Fast = 0, Optimum = 4 };
    FontPackingModes m_PackingMode = FontPackingModes.Fast;

    int m_CharacterSetSelectionMode;

    string m_CharacterSequence = "";
    string m_OutputFeedback = "";
    string m_WarningMessage;
    const string k_OutputNameLabel = "Font: ";
    const string k_OutputSizeLabel = "Pt. Size: ";
    const string k_OutputCountLabel = "Characters packed: ";
    int m_CharacterCount;
    Vector2 m_ScrollPosition;
    Vector2 m_OutputScrollPosition;

    bool m_IsRepaintNeeded;

    float m_RenderingProgress;
    bool m_IsRenderingDone;
    bool m_IsProcessing;
    bool m_IsGenerationDisabled;
    bool m_IsGenerationCancelled;

    Object m_SourceFontFile;
    TMP_FontAsset m_SelectedFontAsset;
    TMP_FontAsset m_LegacyFontAsset;
    TMP_FontAsset m_ReferencedFontAsset;

    TextAsset m_CharacterList;
    int m_PointSize;

    int m_Padding = 5;
    FaceStyles m_FontStyle = FaceStyles.Normal;
    float m_FontStyleValue = 2;
    RenderModes m_RenderMode = RenderModes.DistanceField16;
    int m_AtlasWidth = 512;
    int m_AtlasHeight = 512;

    FT_FaceInfo m_FontFaceInfo;
    FT_GlyphInfo[] m_FontGlyphInfo;
    byte[] m_TextureBuffer;
    Texture2D m_FontAtlas;
    Texture2D m_SavedFontAtlas;

    bool m_IncludeKerningPairs;
    int[] m_KerningSet;

    bool m_Locked;
    bool m_IsFontAtlasInvalid;

    public void OnEnable()
    {
        minSize = new Vector2(315, minSize.y);

        // Used for Diagnostics
        m_StopWatch = new System.Diagnostics.Stopwatch();

        // Initialize & Get shader property IDs.
        ShaderUtilities.GetShaderPropertyIDs();

        // Debug Link to received message from Native Code
        //TMPro_FontPlugin.LinkDebugLog(); // Link with C++ Plugin to get Debug output
        OnMyEnable();
    }

    public void OnDisable()
    {
        //Debug.Log("TextMeshPro Editor Window has been disabled.");

        // Destroy Engine only if it has been initialized already
        TMPro_FontPlugin.Destroy_FontEngine();

        if (m_FontAtlas != null && EditorUtility.IsPersistent(m_FontAtlas) == false)
        {
            //Debug.Log("Destroying font_Atlas!");
            DestroyImmediate(m_FontAtlas);
        }

        if (File.Exists("Assets/TextMesh Pro/Glyph Report.txt"))
        {
            File.Delete("Assets/TextMesh Pro/Glyph Report.txt");
            File.Delete("Assets/TextMesh Pro/Glyph Report.txt.meta");

            AssetDatabase.Refresh();
        }

        Resources.UnloadUnusedAssets();
    }

    public void OnGUI()
    {
        OnMyGUI();
    }

    public void Update()
    {
        MyUpdate();
    }


    /// <summary>
    /// Method which returns the character corresponding to a decimal value.
    /// </summary>
    /// <param name="sequence"></param>
    /// <returns></returns>
    static int[] ParseNumberSequence(string sequence)
    {
        List<int> unicodeList = new List<int>();
        string[] sequences = sequence.Split(',');

        foreach (string seq in sequences)
        {
            string[] s1 = seq.Split('-');

            if (s1.Length == 1)
                try
                {
                    unicodeList.Add(int.Parse(s1[0]));
                }
                catch
                {
                    Debug.Log("No characters selected or invalid format.");
                }
            else
            {
                for (int j = int.Parse(s1[0]); j < int.Parse(s1[1]) + 1; j++)
                {
                    unicodeList.Add(j);
                }
            }
        }

        return unicodeList.ToArray();
    }


    /// <summary>
    /// Method which returns the character (decimal value) from a hex sequence.
    /// </summary>
    /// <param name="sequence"></param>
    /// <returns></returns>
    static int[] ParseHexNumberSequence(string sequence)
    {
        List<int> unicodeList = new List<int>();
        string[] sequences = sequence.Split(',');

        foreach (string seq in sequences)
        {
            string[] s1 = seq.Split('-');

            if (s1.Length == 1)
                try
                {
                    unicodeList.Add(int.Parse(s1[0], NumberStyles.AllowHexSpecifier));
                }
                catch
                {
                    Debug.Log("No characters selected or invalid format.");
                }
            else
            {
                for (int j = int.Parse(s1[0], NumberStyles.AllowHexSpecifier); j < int.Parse(s1[1], NumberStyles.AllowHexSpecifier) + 1; j++)
                {
                    unicodeList.Add(j);
                }
            }
        }

        return unicodeList.ToArray();
    }


    /// <summary>
    /// Clear the previously generated data.
    /// </summary>
    void ClearGeneratedData()
    {
        m_IsFontAtlasInvalid = false;

        if (m_FontAtlas != null)
        {
            DestroyImmediate(m_FontAtlas);
            m_FontAtlas = null;
        }

        m_SavedFontAtlas = null;

        m_OutputFeedback = string.Empty;
        m_WarningMessage = string.Empty;
    }


    /// <summary>
    /// Function to update the feedback window showing the results of the latest generation.
    /// </summary>
    void UpdateRenderFeedbackWindow()
    {
        // 不要设置
        //m_PointSize = m_FontFaceInfo.pointSize;

        string colorTag = m_FontFaceInfo.characterCount == m_CharacterCount ? "<color=#C0ffff>" : "<color=#ffff00>";
        string colorTag2 = "<color=#C0ffff>";

        var missingGlyphReport = k_OutputNameLabel + "<b>" + colorTag2 + m_FontFaceInfo.name + "</color></b>";

        if (missingGlyphReport.Length > 60)
            missingGlyphReport += "\n" + k_OutputSizeLabel + "<b>" + colorTag2 + m_FontFaceInfo.pointSize + "</color></b>";
        else
            missingGlyphReport += "  " + k_OutputSizeLabel + "<b>" + colorTag2 + m_FontFaceInfo.pointSize + "</color></b>";

        missingGlyphReport += "\n" + k_OutputCountLabel + "<b>" + colorTag + m_FontFaceInfo.characterCount + "/" + m_CharacterCount + "</color></b>";

        // Report missing requested glyph
        missingGlyphReport += "\n\n<color=#ffff00><b>Missing Characters</b></color>";
        missingGlyphReport += "\n----------------------------------------";

        m_OutputFeedback = missingGlyphReport;

        for (int i = 0; i < m_CharacterCount; i++)
        {
            if (m_FontGlyphInfo[i].x == -1)
            {
                missingGlyphReport += "\nID: <color=#C0ffff>" + m_FontGlyphInfo[i].id + "\t</color>Hex: <color=#C0ffff>" + m_FontGlyphInfo[i].id.ToString("X") + "\t</color>Char [<color=#C0ffff>" + (char)m_FontGlyphInfo[i].id + "</color>]";

                if (missingGlyphReport.Length < 16300)
                    m_OutputFeedback = missingGlyphReport;
            }
        }

        if (missingGlyphReport.Length > 16300)
            m_OutputFeedback += "\n\n<color=#ffff00>Report truncated.</color>\n<color=#c0ffff>See</color> \"TextMesh Pro\\Glyph Report.txt\"";

        // 多个字体，所以还是用输出日志
        Debug.Log(m_OutputFeedback);
        // Save Missing Glyph Report file
        //if (Directory.Exists("Assets/TextMesh Pro"))
        //{
        //    missingGlyphReport = System.Text.RegularExpressions.Regex.Replace(missingGlyphReport, @"<[^>]*>", string.Empty);
        //    File.WriteAllText("Assets/TextMesh Pro/Glyph Report.txt", missingGlyphReport);
        //    AssetDatabase.Refresh();
        //}
    }


    void CreateFontTexture()
    {
        m_FontAtlas = new Texture2D(m_AtlasWidth, m_AtlasHeight, TextureFormat.Alpha8, false, true);

        Color32[] colors = new Color32[m_AtlasWidth * m_AtlasHeight];

        for (int i = 0; i < (m_AtlasWidth * m_AtlasHeight); i++)
        {
            byte c = m_TextureBuffer[i];
            colors[i] = new Color32(c, c, c, c);
        }
        // Clear allocation of 
        m_TextureBuffer = null;

        if (m_RenderMode == RenderModes.Raster || m_RenderMode == RenderModes.RasterHinted)
            m_FontAtlas.filterMode = FilterMode.Point;

        m_FontAtlas.SetPixels32(colors, 0);
        m_FontAtlas.Apply(false, true);
    }


    /// <summary>
    /// Open Save Dialog to provide the option save the font asset using the name of the source font file. This also appends SDF to the name if using any of the SDF Font Asset creation modes.
    /// </summary>
    /// <param name="sourceObject"></param>
    void SaveNewFontAsset(Object sourceObject)
    {
        string filePath;

        // Save new Font Asset and open save file requester at Source Font File location.
        string saveDirectory = new FileInfo(AssetDatabase.GetAssetPath(sourceObject)).DirectoryName;

        if (m_RenderMode < RenderModes.DistanceField16) // ((int)m_RenderMode & 0x10) == 0x10)
        {
            filePath = EditorUtility.SaveFilePanel("Save TextMesh Pro! Font Asset File", saveDirectory, sourceObject.name, "asset");

            if (filePath.Length == 0)
                return;

            Save_Normal_FontAsset(filePath);
        }
        else if (m_RenderMode >= RenderModes.DistanceField16) // ((RasterModes)m_RenderMode & RasterModes.Raster_Mode_SDF) == RasterModes.Raster_Mode_SDF || m_RenderMode == RenderModes.DistanceFieldAA)
        {
            filePath = EditorUtility.SaveFilePanel("Save TextMesh Pro! Font Asset File", saveDirectory, sourceObject.name + " SDF", "asset");

            if (filePath.Length == 0)
                return;

            Save_SDF_FontAsset(filePath);
        }
    }


    /// <summary>
    /// Open Save Dialog to provide the option to save the font asset under the same name.
    /// </summary>
    /// <param name="sourceObject"></param>
    void SaveNewFontAssetWithSameName(Object sourceObject)
    {
        string filePath;

        // Save new Font Asset and open save file requester at Source Font File location.
        string saveDirectory = new FileInfo(AssetDatabase.GetAssetPath(sourceObject)).DirectoryName;

        filePath = EditorUtility.SaveFilePanel("Save TextMesh Pro! Font Asset File", saveDirectory, sourceObject.name, "asset");

        if (filePath.Length == 0)
            return;

        if (m_RenderMode < RenderModes.DistanceField16) // ((int)m_RenderMode & 0x10) == 0x10)
        {
            Save_Normal_FontAsset(filePath);
        }
        else if (m_RenderMode >= RenderModes.DistanceField16) // ((RasterModes)m_RenderMode & RasterModes.Raster_Mode_SDF) == RasterModes.Raster_Mode_SDF || m_RenderMode == RenderModes.DistanceFieldAA)
        {
            Save_SDF_FontAsset(filePath);
        }
    }


    void Save_Normal_FontAsset(string filePath)
    {
        //filePath = filePath.Substring(0, filePath.Length - 6); // Trim file extension from filePath.

        //string dataPath = Application.dataPath;

        //if (filePath.IndexOf(dataPath, System.StringComparison.InvariantCultureIgnoreCase) == -1)
        //{
        //    Debug.LogError("You're saving the font asset in a directory outside of this project folder. This is not supported. Please select a directory under \"" + dataPath + "\"");
        //    return;
        //}

        string relativeAssetPath = filePath;
        string tex_DirName = Path.GetDirectoryName(relativeAssetPath);
        string tex_FileName = Path.GetFileNameWithoutExtension(relativeAssetPath);
        string tex_Path_NoExt = tex_DirName + "/" + tex_FileName;

        // Check if TextMeshPro font asset already exists. If not, create a new one. Otherwise update the existing one.
        TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath(tex_Path_NoExt + ".asset", typeof(TMP_FontAsset)) as TMP_FontAsset;
        if (fontAsset == null)
        {
            //Debug.Log("Creating TextMeshPro font asset!");
            fontAsset = ScriptableObject.CreateInstance<TMP_FontAsset>(); // Create new TextMeshPro Font Asset.
            AssetDatabase.CreateAsset(fontAsset, tex_Path_NoExt + ".asset");

            //Set Font Asset Type
            fontAsset.fontAssetType = TMP_FontAsset.FontAssetTypes.Bitmap;

            // Reference to the source font file
            //font_asset.sourceFontFile = font_TTF as Font;

            // Add FaceInfo to Font Asset
            FaceInfo face = GetFaceInfo(m_FontFaceInfo, 1);
            fontAsset.AddFaceInfo(face);

            // Add GlyphInfo[] to Font Asset
            TMP_Glyph[] glyphs = GetGlyphInfo(m_FontGlyphInfo, 1);
            fontAsset.AddGlyphInfo(glyphs);

            // Get and Add Kerning Pairs to Font Asset
            if (m_IncludeKerningPairs)
            {
                string fontFilePath = AssetDatabase.GetAssetPath(m_SourceFontFile);
                KerningTable kerningTable = GetKerningTable(fontFilePath, (int)face.PointSize);
                fontAsset.AddKerningInfo(kerningTable);
            }


            // Add Font Atlas as Sub-Asset
            fontAsset.atlas = m_FontAtlas;
            m_FontAtlas.name = tex_FileName + " Atlas";

            AssetDatabase.AddObjectToAsset(m_FontAtlas, fontAsset);

            // Create new Material and Add it as Sub-Asset
            Shader default_Shader = Shader.Find("TextMeshPro/Bitmap"); // m_shaderSelection;
            Material tmp_material = new Material(default_Shader);
            tmp_material.name = tex_FileName + " Material";
            tmp_material.SetTexture(ShaderUtilities.ID_MainTex, m_FontAtlas);
            fontAsset.material = tmp_material;

            AssetDatabase.AddObjectToAsset(tmp_material, fontAsset);

        }
        else
        {
            // Find all Materials referencing this font atlas.
            Material[] material_references = TMP_EditorUtility.FindMaterialReferences(fontAsset);

            // Destroy Assets that will be replaced.
            DestroyImmediate(fontAsset.atlas, true);

            //Set Font Asset Type
            fontAsset.fontAssetType = TMP_FontAsset.FontAssetTypes.Bitmap;

            // Add FaceInfo to Font Asset
            FaceInfo face = GetFaceInfo(m_FontFaceInfo, 1);
            fontAsset.AddFaceInfo(face);

            // Add GlyphInfo[] to Font Asset
            TMP_Glyph[] glyphs = GetGlyphInfo(m_FontGlyphInfo, 1);
            fontAsset.AddGlyphInfo(glyphs);

            // Get and Add Kerning Pairs to Font Asset
            if (m_IncludeKerningPairs)
            {
                string fontFilePath = AssetDatabase.GetAssetPath(m_SourceFontFile);
                KerningTable kerningTable = GetKerningTable(fontFilePath, (int)face.PointSize);
                fontAsset.AddKerningInfo(kerningTable);
            }

            // Add Font Atlas as Sub-Asset
            fontAsset.atlas = m_FontAtlas;
            m_FontAtlas.name = tex_FileName + " Atlas";

            // Special handling due to a bug in earlier versions of Unity.
            m_FontAtlas.hideFlags = HideFlags.None;
            fontAsset.material.hideFlags = HideFlags.None;

            AssetDatabase.AddObjectToAsset(m_FontAtlas, fontAsset);

            // Assign new font atlas texture to the existing material.
            fontAsset.material.SetTexture(ShaderUtilities.ID_MainTex, fontAsset.atlas);

            // Update the Texture reference on the Material
            for (int i = 0; i < material_references.Length; i++)
            {
                material_references[i].SetTexture(ShaderUtilities.ID_MainTex, m_FontAtlas);
            }
        }

        // Save Font Asset creation settings
        m_SelectedFontAsset = fontAsset;
        m_LegacyFontAsset = null;

        AssetDatabase.SaveAssets();

        AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(fontAsset));  // Re-import font asset to get the new updated version.

        //EditorUtility.SetDirty(font_asset);
        fontAsset.ReadFontDefinition();

        AssetDatabase.Refresh();

        m_FontAtlas = null;

        // NEED TO GENERATE AN EVENT TO FORCE A REDRAW OF ANY TEXTMESHPRO INSTANCES THAT MIGHT BE USING THIS FONT ASSET
        TMPro_EventManager.ON_FONT_PROPERTY_CHANGED(true, fontAsset);
    }

    void Save_SDF_FontAsset(string filePath)
    {
        //filePath = filePath.Substring(0, filePath.Length - 6); // Trim file extension from filePath.

        //string dataPath = Application.dataPath;

        //if (filePath.IndexOf(dataPath, System.StringComparison.InvariantCultureIgnoreCase) == -1)
        //{
        //    Debug.LogError("You're saving the font asset in a directory outside of this project folder. This is not supported. Please select a directory under \"" + dataPath + "\"");
        //    return;
        //}

        string relativeAssetPath = filePath;
        string tex_DirName = Path.GetDirectoryName(relativeAssetPath);
        string tex_FileName = Path.GetFileNameWithoutExtension(relativeAssetPath);
        string tex_Path_NoExt = tex_DirName + "/" + tex_FileName;


        // Check if TextMeshPro font asset already exists. If not, create a new one. Otherwise update the existing one.
        TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(tex_Path_NoExt + ".asset");
        if (fontAsset == null)
        {
            //Debug.Log("Creating TextMeshPro font asset!");
            fontAsset = ScriptableObject.CreateInstance<TMP_FontAsset>(); // Create new TextMeshPro Font Asset.
            AssetDatabase.CreateAsset(fontAsset, tex_Path_NoExt + ".asset");

            // Reference to the source font file
            //font_asset.sourceFontFile = font_TTF as Font;

            //Set Font Asset Type
            fontAsset.fontAssetType = TMP_FontAsset.FontAssetTypes.SDF;

            //if (m_destination_Atlas != null)
            //    m_font_Atlas = m_destination_Atlas;

            // If using the C# SDF creation mode, we need the scale down factor.
            int scaleDownFactor = 1; // ((RasterModes)m_RenderMode & RasterModes.Raster_Mode_SDF) == RasterModes.Raster_Mode_SDF || m_RenderMode == RenderModes.DistanceFieldAA ? 1 : font_scaledownFactor;

            // Add FaceInfo to Font Asset
            FaceInfo face = GetFaceInfo(m_FontFaceInfo, scaleDownFactor);
            fontAsset.AddFaceInfo(face);

            // Add GlyphInfo[] to Font Asset
            TMP_Glyph[] glyphs = GetGlyphInfo(m_FontGlyphInfo, scaleDownFactor);
            fontAsset.AddGlyphInfo(glyphs);

            // Get and Add Kerning Pairs to Font Asset
            if (m_IncludeKerningPairs)
            {
                string fontFilePath = AssetDatabase.GetAssetPath(m_SourceFontFile);
                KerningTable kerningTable = GetKerningTable(fontFilePath, (int)face.PointSize);
                fontAsset.AddKerningInfo(kerningTable);
            }

            // Add Line Breaking Rules
            //LineBreakingTable lineBreakingTable = new LineBreakingTable();
            //

            // Add Font Atlas as Sub-Asset
            fontAsset.atlas = m_FontAtlas;
            if (!m_FontAtlas.name.EndsWith(" Atlas")) // 因为图集复用，所以只要加到第一个资产里
            {
                m_FontAtlas.name = tex_FileName + " Atlas";
                AssetDatabase.AddObjectToAsset(m_FontAtlas, fontAsset);
            }

            // Create new Material and Add it as Sub-Asset
            Shader default_Shader = Shader.Find("TextMeshPro/Distance Field"); //m_shaderSelection;
            Material tmp_material = new Material(default_Shader);

            tmp_material.name = tex_FileName + " Material";
            tmp_material.SetTexture(ShaderUtilities.ID_MainTex, m_FontAtlas);
            tmp_material.SetFloat(ShaderUtilities.ID_TextureWidth, m_FontAtlas.width);
            tmp_material.SetFloat(ShaderUtilities.ID_TextureHeight, m_FontAtlas.height);

            int spread = m_Padding + 1;
            tmp_material.SetFloat(ShaderUtilities.ID_GradientScale, spread); // Spread = Padding for Brute Force SDF.

            tmp_material.SetFloat(ShaderUtilities.ID_WeightNormal, fontAsset.normalStyle);
            tmp_material.SetFloat(ShaderUtilities.ID_WeightBold, fontAsset.boldStyle);

            fontAsset.material = tmp_material;

            AssetDatabase.AddObjectToAsset(tmp_material, fontAsset);

        }
        else
        {
            // Find all Materials referencing this font atlas.
            Material[] material_references = TMProFontCustomizedCreater.FindMaterialReferences(fontAsset);

            if (fontAsset.atlas) // 有可能被其他资产删除了
            {
                // Destroy Assets that will be replaced.
                DestroyImmediate(fontAsset.atlas, true);
            }

            //Set Font Asset Type
            fontAsset.fontAssetType = TMP_FontAsset.FontAssetTypes.SDF;

            int scaleDownFactor = 1; // ((RasterModes)m_RenderMode & RasterModes.Raster_Mode_SDF) == RasterModes.Raster_Mode_SDF || m_RenderMode == RenderModes.DistanceFieldAA ? 1 : font_scaledownFactor;
                                     // Add FaceInfo to Font Asset  
            FaceInfo face = GetFaceInfo(m_FontFaceInfo, scaleDownFactor);
            fontAsset.AddFaceInfo(face);

            // Add GlyphInfo[] to Font Asset
            TMP_Glyph[] glyphs = GetGlyphInfo(m_FontGlyphInfo, scaleDownFactor);
            fontAsset.AddGlyphInfo(glyphs);

            // Get and Add Kerning Pairs to Font Asset
            if (m_IncludeKerningPairs)
            {
                string fontFilePath = AssetDatabase.GetAssetPath(m_SourceFontFile);
                KerningTable kerningTable = GetKerningTable(fontFilePath, (int)face.PointSize);
                fontAsset.AddKerningInfo(kerningTable);
            }

            // Add Font Atlas as Sub-Asset
            fontAsset.atlas = m_FontAtlas;
            if (!m_FontAtlas.name.EndsWith(" Atlas")) // 因为图集复用，所以只要加到第一个资产里
            {
                m_FontAtlas.name = tex_FileName + " Atlas";
                AssetDatabase.AddObjectToAsset(m_FontAtlas, fontAsset);
            }

            // Special handling due to a bug in earlier versions of Unity.
            m_FontAtlas.hideFlags = HideFlags.None;
            fontAsset.material.hideFlags = HideFlags.None;

            // Assign new font atlas texture to the existing material.
            fontAsset.material.SetTexture(ShaderUtilities.ID_MainTex, fontAsset.atlas);

            // Update the Texture reference on the Material
            for (int i = 0; i < material_references.Length; i++)
            {
                material_references[i].SetTexture(ShaderUtilities.ID_MainTex, m_FontAtlas);
                material_references[i].SetFloat(ShaderUtilities.ID_TextureWidth, m_FontAtlas.width);
                material_references[i].SetFloat(ShaderUtilities.ID_TextureHeight, m_FontAtlas.height);

                int spread = m_Padding + 1;
                material_references[i].SetFloat(ShaderUtilities.ID_GradientScale, spread); // Spread = Padding for Brute Force SDF.

                material_references[i].SetFloat(ShaderUtilities.ID_WeightNormal, fontAsset.normalStyle);
                material_references[i].SetFloat(ShaderUtilities.ID_WeightBold, fontAsset.boldStyle);
            }
        }

        // Saving File for Debug
        //var pngData = m_FontAtlas.EncodeToPNG();
        //File.WriteAllBytes("Assets/Debug Distance Field.png", pngData);

        // Save Font Asset creation settings
        m_SelectedFontAsset = fontAsset;
        m_LegacyFontAsset = null;

        // 提到这里才能保存完整
        fontAsset.ReadFontDefinition();

        AssetDatabase.SaveAssets();

        AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(fontAsset));  // Re-import font asset to get the new updated version.

        //fontAsset.ReadFontDefinition();

        AssetDatabase.Refresh();

        //m_FontAtlas = null; 贴图不要删除

        // NEED TO GENERATE AN EVENT TO FORCE A REDRAW OF ANY TEXTMESHPRO INSTANCES THAT MIGHT BE USING THIS FONT ASSET
        TMPro_EventManager.ON_FONT_PROPERTY_CHANGED(true, fontAsset);
    }

    // Convert from FT_FaceInfo to FaceInfo
    static FaceInfo GetFaceInfo(FT_FaceInfo ftFace, int scaleFactor)
    {
        FaceInfo face = new FaceInfo();

        face.Name = ftFace.name;
        face.PointSize = (float)ftFace.pointSize / scaleFactor;
        face.Padding = (float)ftFace.padding / scaleFactor;
        face.LineHeight = ftFace.lineHeight / scaleFactor;
        face.CapHeight = 0;
        face.Baseline = 0;
        face.Ascender = ftFace.ascender / scaleFactor;
        face.Descender = ftFace.descender / scaleFactor;
        face.CenterLine = ftFace.centerLine / scaleFactor;
        face.Underline = ftFace.underline / scaleFactor;
        face.UnderlineThickness = ftFace.underlineThickness == 0 ? 5 : ftFace.underlineThickness / scaleFactor; // Set Thickness to 5 if TTF value is Zero.
        face.strikethrough = (face.Ascender + face.Descender) / 2.75f;
        face.strikethroughThickness = face.UnderlineThickness;
        face.SuperscriptOffset = face.Ascender;
        face.SubscriptOffset = face.Underline;
        face.SubSize = 0.5f;
        //face.CharacterCount = ft_face.characterCount;
        face.AtlasWidth = ftFace.atlasWidth / scaleFactor;
        face.AtlasHeight = ftFace.atlasHeight / scaleFactor;

        return face;
    }


    // Convert from FT_GlyphInfo[] to GlyphInfo[]
    TMP_Glyph[] GetGlyphInfo(FT_GlyphInfo[] ftGlyphs, int scaleFactor)
    {
        List<TMP_Glyph> glyphs = new List<TMP_Glyph>();
        List<int> kerningSet = new List<int>();

        for (int i = 0; i < ftGlyphs.Length; i++)
        {
            TMP_Glyph g = new TMP_Glyph();

            g.id = ftGlyphs[i].id;
            g.x = ftGlyphs[i].x / scaleFactor;
            g.y = ftGlyphs[i].y / scaleFactor;
            g.width = ftGlyphs[i].width / scaleFactor;
            g.height = ftGlyphs[i].height / scaleFactor;
            g.xOffset = ftGlyphs[i].xOffset / scaleFactor;
            g.yOffset = ftGlyphs[i].yOffset / scaleFactor;
            g.xAdvance = ftGlyphs[i].xAdvance / scaleFactor;

            // Filter out characters with missing glyphs.
            if (g.x == -1)
                continue;

            glyphs.Add(g);
            kerningSet.Add(g.id);
        }

        m_KerningSet = kerningSet.ToArray();

        return glyphs.ToArray();
    }


    // Get Kerning Pairs
    public KerningTable GetKerningTable(string fontFilePath, int pointSize)
    {
        KerningTable kerningInfo = new KerningTable();
        kerningInfo.kerningPairs = new List<KerningPair>();

        // Temporary Array to hold the kerning pairs from the Native Plug-in.
        FT_KerningPair[] kerningPairs = new FT_KerningPair[7500];

        int kpCount = TMPro_FontPlugin.FT_GetKerningPairs(fontFilePath, m_KerningSet, m_KerningSet.Length, kerningPairs);

        for (int i = 0; i < kpCount; i++)
        {
            // Proceed to add each kerning pairs.
            KerningPair kp = new KerningPair((uint)kerningPairs[i].ascII_Left, (uint)kerningPairs[i].ascII_Right, kerningPairs[i].xAdvanceOffset * pointSize);

            // Filter kerning pairs to avoid duplicates
            int index = kerningInfo.kerningPairs.FindIndex(item => item.firstGlyph == kp.firstGlyph && item.secondGlyph == kp.secondGlyph);

            if (index == -1)
                kerningInfo.kerningPairs.Add(kp);
            else
                if (!TMP_Settings.warningsDisabled) Debug.LogWarning("Kerning Key for [" + kp.firstGlyph + "] and [" + kp.secondGlyph + "] is a duplicate.");

        }

        return kerningInfo;
    }

    #region 修改的部分

    private void GenerateFontAtlasButton()
    {
        if (!m_IsProcessing && m_SourceFontFile != null)
        {
            DestroyImmediate(m_FontAtlas);
            m_FontAtlas = null;
            m_OutputFeedback = string.Empty;
            m_SavedFontAtlas = null;
            int errorCode;

            errorCode = TMPro_FontPlugin.Initialize_FontEngine(); // Initialize Font Engine
            if (errorCode != 0)
            {
                if (errorCode == 0xF0)
                {
                    //Debug.Log("Font Library was already initialized!");
                    errorCode = 0;
                }
                else
                    Debug.Log("Error Code: " + errorCode + "  occurred while Initializing the FreeType Library.");
            }

            string fontPath = AssetDatabase.GetAssetPath(m_SourceFontFile); // Get file path of TTF Font.

            if (errorCode == 0)
            {
                errorCode = TMPro_FontPlugin.Load_TrueType_Font(fontPath); // Load the selected font.

                if (errorCode != 0)
                {
                    if (errorCode == 0xF1)
                    {
                        //Debug.Log("Font was already loaded!");
                        errorCode = 0;
                    }
                    else
                        Debug.Log("Error Code: " + errorCode + "  occurred while Loading the [" + m_SourceFontFile.name + "] font file. This typically results from the use of an incompatible or corrupted font file.");
                }
            }

            if (errorCode == 0)
            {
                if (m_PointSizeSamplingMode == 0) m_PointSize = 72; // If Auto set size to 72 pts.

                errorCode = TMPro_FontPlugin.FT_Size_Font(m_PointSize); // Load the selected font and size it accordingly.
                if (errorCode != 0)
                    Debug.Log("Error Code: " + errorCode + "  occurred while Sizing the font.");
            }

            // Define an array containing the characters we will render.
            if (errorCode == 0)
            {
                int[] characterSet;
                if (m_CharacterSetSelectionMode == 7 || m_CharacterSetSelectionMode == 8)
                {
                    List<int> charList = new List<int>();

                    for (int i = 0; i < m_CharacterSequence.Length; i++)
                    {
                        // Check to make sure we don't include duplicates
                        if (charList.FindIndex(item => item == m_CharacterSequence[i]) == -1)
                            charList.Add(m_CharacterSequence[i]);
                        else
                        {
                            //Debug.Log("Character [" + characterSequence[i] + "] is a duplicate.");
                        }
                    }

                    characterSet = charList.ToArray();
                }
                else if (m_CharacterSetSelectionMode == 6)
                {
                    characterSet = ParseHexNumberSequence(m_CharacterSequence);
                }
                else
                {
                    characterSet = ParseNumberSequence(m_CharacterSequence);
                }

                m_CharacterCount = characterSet.Length;

                m_TextureBuffer = new byte[m_AtlasWidth * m_AtlasHeight];

                m_FontFaceInfo = new FT_FaceInfo();

                m_FontGlyphInfo = new FT_GlyphInfo[m_CharacterCount];

                int padding = m_Padding;

                bool autoSizing = m_PointSizeSamplingMode == 0;

                float strokeSize = m_FontStyleValue;
                if (m_RenderMode == RenderModes.DistanceField16) strokeSize = m_FontStyleValue * 16;
                if (m_RenderMode == RenderModes.DistanceField32) strokeSize = m_FontStyleValue * 32;

                m_IsProcessing = true;
                m_IsGenerationCancelled = false;

                // Start Stop Watch
                m_StopWatch = System.Diagnostics.Stopwatch.StartNew();

                ThreadPool.QueueUserWorkItem(someTask =>
                {
                    m_IsRenderingDone = false;

                    errorCode = TMPro_FontPlugin.Render_Characters(m_TextureBuffer, m_AtlasWidth, m_AtlasHeight,
                        padding, characterSet, m_CharacterCount, m_FontStyle, strokeSize, autoSizing, m_RenderMode,
                        (int) m_PackingMode, ref m_FontFaceInfo, m_FontGlyphInfo);

                    ThreadRenderBackupFont(0, m_AtlasWidth);

                    m_IsRenderingDone = true;
                });

            }
        }
    }

    private void ThreadRenderBackupFont(int backupLevel, int xOffsetDist)
    {
        if (m_FontBackupPaths == null || m_FontBackupPaths.Length <= backupLevel)
        {
            return;
        }

        List<int> list = new List<int>();
        for (int index = 0; index < m_CharacterCount; ++index)
        {
            if (m_FontGlyphInfo[index].x == -1)
            {
                list.Add(m_FontGlyphInfo[index].id);
            }
        }
        if (list.Count == 0)
        {
            return;
        }

        int[] characterSet = list.ToArray();
        string fontPath = m_FontBackupPaths[backupLevel];
        int errorCode = TMPro_FontPlugin.Load_TrueType_Font(fontPath);
        if (errorCode != 0)
        {
            return;
        }

        var tmpAtlasWidth = 512;
        var tmpAtlasHeight = 512;
        var tmpTextureBuffer = new byte[tmpAtlasWidth * tmpAtlasHeight];
        var tmpCharacterCount = characterSet.Length;
        var tmpFontFaceInfo = default(FT_FaceInfo);
        var tmpFontGlyphInfo = new FT_GlyphInfo[tmpCharacterCount];

        bool autoSizing = m_PointSizeSamplingMode == 0;
        float strokeSize = m_FontStyleValue;
        if (m_RenderMode == RenderModes.DistanceField16) strokeSize = m_FontStyleValue * 16;
        if (m_RenderMode == RenderModes.DistanceField32) strokeSize = m_FontStyleValue * 32;

        errorCode = TMPro_FontPlugin.Render_Characters(tmpTextureBuffer, tmpAtlasWidth,
            tmpAtlasHeight, m_Padding, characterSet, tmpCharacterCount, m_FontStyle, strokeSize,
            autoSizing, m_RenderMode, (int)m_PackingMode, ref tmpFontFaceInfo, tmpFontGlyphInfo);
        if (errorCode != 0)
        {
            return;
        }

        int wordWidth = m_PointSize;
        int xStart = xOffsetDist - m_Padding * 2 - wordWidth;   // 从padding开始拷贝，否则会出现负偏移丢失的情况
        int yStart = m_AtlasHeight - m_Padding - 1;
        int numY = 0;
        for (int index = 0; index < tmpCharacterCount; ++index)
        {
            if (!Mathf.Approximately(tmpFontGlyphInfo[index].x, -1))
            {
                var gi = tmpFontGlyphInfo[index];
                var x = Mathf.FloorToInt(gi.x) - m_Padding;
                var y = tmpAtlasHeight - (Mathf.FloorToInt(gi.y) - m_Padding);
                var w = Mathf.CeilToInt(gi.width) + m_Padding * 2;
                var h = Mathf.CeilToInt(gi.height) + m_Padding * 2;

                for (int r = 0; r < h; r++)
                {
                    for (int c = 0; c < w; c++)
                    {
                        m_TextureBuffer[(yStart - r) * m_AtlasWidth + c + xStart] =
                            tmpTextureBuffer[(y - r) * tmpAtlasWidth + c + x];
                    }
                }
                var idx = ArrayUtility.FindIndex(m_FontGlyphInfo, info => info.id == gi.id);
                if (idx != -1)
                {
                    var gi2 = m_FontGlyphInfo[idx];
                    gi2.x = xStart + m_Padding;
                    gi2.y = m_AtlasHeight - yStart + m_Padding;
                    gi2.width = gi.width;
                    gi2.height = gi.height;
                    gi2.xAdvance = gi.xAdvance;
                    gi2.xOffset = gi.xOffset;
                    gi2.yOffset = gi.yOffset;
                    m_FontGlyphInfo[idx] = gi2;
                }

                yStart = yStart - h - m_Padding - 1;
                numY++;

                // 如果超过五个则换一列
                if (numY > 5)
                {
                    numY = 0;
                    xStart = xStart - m_Padding * 2 - wordWidth;
                    yStart = m_AtlasHeight - m_Padding - 1;
                }
            }
        }

        ThreadRenderBackupFont(++backupLevel, xStart);
    }

    private readonly List<FontAssetInfo> m_FontAssetInfos = new List<FontAssetInfo>();
    private int m_CurGenerateIndex;
    private string m_CharacterSequenceFile;
    private string[] m_FontBackupPaths;

    private void OnMyEnable()
    {
        TMProFontCustomizedCreater.CustomizedCreaterSettings settings =
            TMProFontCustomizedCreater.GetCustomizedCreaterSettings();

        // 以字体做索引，相同的字体只会生成一次字体纹理
        string str1 = "t:Font";
        string[] fonts = AssetDatabase.FindAssets(str1, new[] { settings.fontFolderPath });

        m_FontAssetInfos.Clear();
        foreach (var font in fonts)
        {
            FontAssetInfo info = new FontAssetInfo();
            info.fontPath = AssetDatabase.GUIDToAssetPath(font);
            info.fontName = Path.GetFileNameWithoutExtension(info.fontPath);

            List<string> assetPaths = new List<string>();
            str1 = "t:TMP_FontAsset " + info.fontName + " SDF";
            var assets = AssetDatabase.FindAssets(str1, new[] { settings.fontMaterialsFolderPath });
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

        m_PointSizeSamplingMode = settings.pointSizeSamplingMode;
        m_PointSize = settings.pointSize;
        m_Padding = settings.padding;
        m_PackingMode = (FontPackingModes)settings.packingMode;
        m_AtlasWidth = settings.atlasWidth;
        m_AtlasHeight = settings.atlasHeight;
        m_CharacterSetSelectionMode = settings.characterSetSelectionMode;
        m_CharacterSequenceFile = settings.characterSequenceFile;
        m_FontStyle = (FaceStyles)settings.fontStyle;
        m_FontStyleValue = settings.fontStyleModifier;
        m_RenderMode = (RenderModes)settings.renderMode;
        m_IncludeKerningPairs = settings.includeFontFeatures;
        m_FontBackupPaths = settings.fontBackupPaths;

        if (string.IsNullOrEmpty(m_WarningMessage) || m_SelectedFontAsset || m_LegacyFontAsset || m_SavedFontAtlas || m_IsFontAtlasInvalid)
        {
            // 仅为了去除警告
        }
    }

    private void OnMyGUI()
    {
        GUI.enabled = !this.m_IsProcessing;
        EditorGUI.indentLevel++;

        if (m_FontAssetInfos.Count > 0)
        {
            GUILayout.Label(" Font Asset", EditorStyles.boldLabel);
            if (GUILayout.Button("生成字库资产", GUILayout.MinHeight(22f)))
            {
                Generate();
            }
        }

        GUILayout.Space(10f);
        GUILayout.Label(" Font List", EditorStyles.boldLabel);
        foreach (var info in m_FontAssetInfos)
        {
            EditorGUILayout.BeginHorizontal();
            info.toggle = EditorGUILayout.ToggleLeft(info.fontName, info.toggle);
            GUILayout.Space(10f);
            EditorGUILayout.LabelField(new GUIContent(System.String.Format("({0}%)", info.genPercent)));
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

    private void MyUpdate()
    {
        if (m_IsRepaintNeeded)
        {
            //Debug.Log("Repainting...");
            m_IsRepaintNeeded = false;
            Repaint();
        }

        // 第一步创建字体渲染数组
        // Update Progress bar is we are Rendering a Font.
        if (m_IsProcessing)
        {
            m_RenderingProgress = TMPro_FontPlugin.Check_RenderProgress();
            m_FontAssetInfos[m_CurGenerateIndex].genPercent = m_RenderingProgress * 100;

            m_IsRepaintNeeded = true;
        }

        // Update Feedback Window & Create Font Texture once Rendering is done.
        if (m_IsRenderingDone)
        {
            // Stop StopWatch
            m_StopWatch.Stop();
            Debug.Log("Font Atlas generation completed in: " + m_StopWatch.Elapsed.TotalMilliseconds.ToString("0.000 ms."));
            m_StopWatch.Reset();

            m_IsProcessing = false;
            m_IsRenderingDone = false;

            if (m_IsGenerationCancelled == false)
            {
                // 第二步输出渲染结果
                UpdateRenderFeedbackWindow();
                // 第三步将渲染数组填充到纹理贴图（注意，贴图共享不删除）
                CreateFontTexture();
                foreach (var asset in m_FontAssetInfos[m_CurGenerateIndex].assets)
                {
                    // 最后保存信息到字体资产
                    Save_SDF_FontAsset(asset);
                }
                // 最后置空
                m_FontAtlas = null;
            }
            Repaint();
        }
    }

    private void Generate()
    {
        m_CharacterSequence = System.String.Empty;
        if (!string.IsNullOrEmpty(m_CharacterSequenceFile))
        {
            var characterList = AssetDatabase.LoadAssetAtPath<TextAsset>(m_CharacterSequenceFile);
            m_CharacterSequence = characterList.text;
        }

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

        m_SourceFontFile = AssetDatabase.LoadAssetAtPath<Font>(info.fontPath);
        GenerateFontAtlasButton();
    }

    private class FontAssetInfo
    {
        public string fontPath;
        public string fontName;
        public bool toggle = true; // 是否要生成
        public float genPercent;
        public string[] assets;
    }

    #endregion
}
