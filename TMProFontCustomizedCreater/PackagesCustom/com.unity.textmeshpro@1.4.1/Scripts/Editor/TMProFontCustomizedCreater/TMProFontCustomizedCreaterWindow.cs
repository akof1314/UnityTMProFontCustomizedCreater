using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

namespace TMPro.EditorUtilities
{
    public class TMProFontCustomizedCreaterWindow : EditorWindow
    {
        // Diagnostics
        System.Diagnostics.Stopwatch m_StopWatch;
        double m_GlyphPackingGenerationTime;
        double m_GlyphRenderingGenerationTime;

        //string[] m_FontSizingOptions = {"Auto Sizing", "Custom Size"};
        int m_PointSizeSamplingMode;
        //string[] m_FontResolutionLabels = {"8", "16", "32", "64", "128", "256", "512", "1024", "2048", "4096", "8192"};
        //int[] m_FontAtlasResolutions = {8, 16, 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192};

        //string[] m_FontCharacterSets =
        //{
        //    "ASCII", "Extended ASCII", "ASCII Lowercase", "ASCII Uppercase", "Numbers + Symbols", "Custom Range",
        //    "Unicode Range (Hex)", "Custom Characters", "Characters from File"
        //};

        enum FontPackingModes
        {
            Fast = 0,
            Optimum = 4
        };

        FontPackingModes m_PackingMode = FontPackingModes.Fast;

        int m_CharacterSetSelectionMode;

        string m_CharacterSequence = "";
        string m_OutputFeedback = "";
        string m_WarningMessage;
        int m_CharacterCount;
        Vector2 m_ScrollPosition;
        Vector2 m_OutputScrollPosition;

        bool m_IsRepaintNeeded;

        float m_AtlasGenerationProgress;
        string m_AtlasGenerationProgressLabel = string.Empty;
        float m_RenderingProgress;
        bool m_IsRenderingDone;
        bool m_IsProcessing;
        bool m_IsGenerationDisabled;
        bool m_IsGenerationCancelled;

        bool m_IsFontAtlasInvalid;
        Object m_SourceFontFile;
        TMP_FontAsset m_SelectedFontAsset;
        TMP_FontAsset m_LegacyFontAsset;
        TMP_FontAsset m_ReferencedFontAsset;

        TextAsset m_CharactersFromFile;
        int m_PointSize;

        int m_Padding = 5;
        //FaceStyles m_FontStyle = FaceStyles.Normal;
        //float m_FontStyleValue = 2;

        GlyphRenderMode m_GlyphRenderMode = GlyphRenderMode.SDFAA;
        int m_AtlasWidth = 512;
        int m_AtlasHeight = 512;
        byte[] m_AtlasTextureBuffer;
        Texture2D m_FontAtlasTexture;
        Texture2D m_SavedFontAtlas;

        //
        List<Glyph> m_FontGlyphTable = new List<Glyph>();
        List<TMP_Character> m_FontCharacterTable = new List<TMP_Character>();

        Dictionary<uint, uint> m_CharacterLookupMap = new Dictionary<uint, uint>();
        Dictionary<uint, List<uint>> m_GlyphLookupMap = new Dictionary<uint, List<uint>>();

        List<Glyph> m_GlyphsToPack = new List<Glyph>();
        List<Glyph> m_GlyphsPacked = new List<Glyph>();
        List<GlyphRect> m_FreeGlyphRects = new List<GlyphRect>();
        List<GlyphRect> m_UsedGlyphRects = new List<GlyphRect>();
        List<Glyph> m_GlyphsToRender = new List<Glyph>();
        List<uint> m_AvailableGlyphsToAdd = new List<uint>();
        List<uint> m_MissingCharacters = new List<uint>();
        List<uint> m_ExcludedCharacters = new List<uint>();

        private FaceInfo m_FaceInfo;

        bool m_IncludeFontFeatures;

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
            FontEngine.DestroyFontEngine();

            ClearGeneratedData();

            // Remove Glyph Report if one was created.
            if (File.Exists("Assets/TextMesh Pro/Glyph Report.txt"))
            {
                File.Delete("Assets/TextMesh Pro/Glyph Report.txt");
                File.Delete("Assets/TextMesh Pro/Glyph Report.txt.meta");

                AssetDatabase.Refresh();
            }

            // Save Font Asset Creation Settings Index
            //SaveCreationSettingsToEditorPrefs(SaveFontCreationSettings());
            //EditorPrefs.SetInt(k_FontAssetCreationSettingsCurrentIndexKey, m_FontAssetCreationSettingsCurrentIndex);

            // Unregister to event
            //TMPro_EventManager.RESOURCE_LOAD_EVENT.Remove(ON_RESOURCES_LOADED);

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
        static uint[] ParseNumberSequence(string sequence)
        {
            List<uint> unicodeList = new List<uint>();
            string[] sequences = sequence.Split(',');

            foreach (string seq in sequences)
            {
                string[] s1 = seq.Split('-');

                if (s1.Length == 1)
                    try
                    {
                        unicodeList.Add(uint.Parse(s1[0]));
                    }
                    catch
                    {
                        Debug.Log("No characters selected or invalid format.");
                    }
                else
                {
                    for (uint j = uint.Parse(s1[0]); j < uint.Parse(s1[1]) + 1; j++)
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
        static uint[] ParseHexNumberSequence(string sequence)
        {
            List<uint> unicodeList = new List<uint>();
            string[] sequences = sequence.Split(',');

            foreach (string seq in sequences)
            {
                string[] s1 = seq.Split('-');

                if (s1.Length == 1)
                    try
                    {
                        unicodeList.Add(uint.Parse(s1[0], NumberStyles.AllowHexSpecifier));
                    }
                    catch
                    {
                        Debug.Log("No characters selected or invalid format.");
                    }
                else
                {
                    for (uint j = uint.Parse(s1[0], NumberStyles.AllowHexSpecifier); j < uint.Parse(s1[1], NumberStyles.AllowHexSpecifier) + 1; j++)
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

            if (m_FontAtlasTexture != null && !EditorUtility.IsPersistent(m_FontAtlasTexture))
            {
                DestroyImmediate(m_FontAtlasTexture);
                m_FontAtlasTexture = null;
            }

            m_AtlasGenerationProgressLabel = string.Empty;
            m_AtlasGenerationProgress = 0;
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

            string missingGlyphReport = string.Empty;

            //string colorTag = m_FontCharacterTable.Count == m_CharacterCount ? "<color=#C0ffff>" : "<color=#ffff00>";
            string colorTag2 = "<color=#C0ffff>";

            missingGlyphReport = "Font: <b>" + colorTag2 + m_FaceInfo.familyName + "</color></b>  Style: <b>" +
                                 colorTag2 + m_FaceInfo.styleName + "</color></b>";

            missingGlyphReport += "\nPoint Size: <b>" + colorTag2 + m_FaceInfo.pointSize +
                                  "</color></b>   SP/PD Ratio: <b>" + colorTag2 +
                                  ((float) m_Padding / m_FaceInfo.pointSize).ToString("0.0%" + "</color></b>");

            missingGlyphReport += "\n\nCharacters included: <color=#ffff00><b>" + m_FontCharacterTable.Count + "/" +
                                  m_CharacterCount + "</b></color>";
            missingGlyphReport +=
                "\nMissing characters: <color=#ffff00><b>" + m_MissingCharacters.Count + "</b></color>";
            missingGlyphReport += "\nExcluded characters: <color=#ffff00><b>" + m_ExcludedCharacters.Count +
                                  "</b></color>";

            // Report characters missing from font file
            missingGlyphReport += "\n\n<b><color=#ffff00>Characters missing from font file:</color></b>";
            missingGlyphReport += "\n----------------------------------------";

            m_OutputFeedback = missingGlyphReport;

            for (int i = 0; i < m_MissingCharacters.Count; i++)
            {
                missingGlyphReport += "\nID: <color=#C0ffff>" + m_MissingCharacters[i] +
                                      "\t</color>Hex: <color=#C0ffff>" + m_MissingCharacters[i].ToString("X") +
                                      "\t</color>Char [<color=#C0ffff>" + (char) m_MissingCharacters[i] + "</color>]";

                if (missingGlyphReport.Length < 16300)
                    m_OutputFeedback = missingGlyphReport;
            }

            // Report characters that did not fit in the atlas texture
            missingGlyphReport += "\n\n<b><color=#ffff00>Characters excluded from packing:</color></b>";
            missingGlyphReport += "\n----------------------------------------";

            for (int i = 0; i < m_ExcludedCharacters.Count; i++)
            {
                missingGlyphReport += "\nID: <color=#C0ffff>" + m_ExcludedCharacters[i] +
                                      "\t</color>Hex: <color=#C0ffff>" + m_ExcludedCharacters[i].ToString("X") +
                                      "\t</color>Char [<color=#C0ffff>" + (char) m_ExcludedCharacters[i] + "</color>]";

                if (missingGlyphReport.Length < 16300)
                    m_OutputFeedback = missingGlyphReport;
            }

            if (missingGlyphReport.Length > 16300)
                m_OutputFeedback +=
                    "\n\n<color=#ffff00>Report truncated.</color>\n<color=#c0ffff>See</color> \"TextMesh Pro\\Glyph Report.txt\"";

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
            if (m_FontAtlasTexture != null)
                DestroyImmediate(m_FontAtlasTexture);

            m_FontAtlasTexture = new Texture2D(m_AtlasWidth, m_AtlasHeight, TextureFormat.Alpha8, false, true);

            Color32[] colors = new Color32[m_AtlasWidth * m_AtlasHeight];

            for (int i = 0; i < colors.Length; i++)
            {
                byte c = m_AtlasTextureBuffer[i];
                colors[i] = new Color32(c, c, c, c);
            }

            // Clear allocation of
            m_AtlasTextureBuffer = null;

            //if ((m_GlyphRenderMode & GlyphRenderMode.RASTER) == GlyphRenderMode.RASTER || (m_GlyphRenderMode & GlyphRenderMode.RASTER_HINTED) == GlyphRenderMode.RASTER_HINTED)
            //    m_FontAtlasTexture.filterMode = FilterMode.Point;

            m_FontAtlasTexture.SetPixels32(colors, 0);
            m_FontAtlasTexture.Apply(false, false);

            // Saving File for Debug
            //var pngData = m_FontAtlasTexture.EncodeToPNG();
            //File.WriteAllBytes("Assets/Textures/Debug Font Texture.png", pngData);
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

            if (((GlyphRasterModes) m_GlyphRenderMode & GlyphRasterModes.RASTER_MODE_BITMAP) ==
                GlyphRasterModes.RASTER_MODE_BITMAP)
            {
                filePath = EditorUtility.SaveFilePanel("Save TextMesh Pro! Font Asset File", saveDirectory,
                    sourceObject.name, "asset");

                if (filePath.Length == 0)
                    return;

                Save_Bitmap_FontAsset(filePath);
            }
            else
            {
                filePath = EditorUtility.SaveFilePanel("Save TextMesh Pro! Font Asset File", saveDirectory,
                    sourceObject.name + " SDF", "asset");

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

            filePath = EditorUtility.SaveFilePanel("Save TextMesh Pro! Font Asset File", saveDirectory,
                sourceObject.name, "asset");

            if (filePath.Length == 0)
                return;

            if (((GlyphRasterModes) m_GlyphRenderMode & GlyphRasterModes.RASTER_MODE_BITMAP) ==
                GlyphRasterModes.RASTER_MODE_BITMAP)
            {
                Save_Bitmap_FontAsset(filePath);
            }
            else
            {
                Save_SDF_FontAsset(filePath);
            }
        }


        void Save_Bitmap_FontAsset(string filePath)
        {
            filePath = filePath.Substring(0, filePath.Length - 6); // Trim file extension from filePath.

            string dataPath = Application.dataPath;

            if (filePath.IndexOf(dataPath, System.StringComparison.InvariantCultureIgnoreCase) == -1)
            {
                Debug.LogError(
                    "You're saving the font asset in a directory outside of this project folder. This is not supported. Please select a directory under \"" +
                    dataPath + "\"");
                return;
            }

            string relativeAssetPath = filePath.Substring(dataPath.Length - 6);
            string tex_DirName = Path.GetDirectoryName(relativeAssetPath);
            string tex_FileName = Path.GetFileNameWithoutExtension(relativeAssetPath);
            string tex_Path_NoExt = tex_DirName + "/" + tex_FileName;

            // Check if TextMeshPro font asset already exists. If not, create a new one. Otherwise update the existing one.
            TMP_FontAsset fontAsset =
                AssetDatabase.LoadAssetAtPath(tex_Path_NoExt + ".asset", typeof(TMP_FontAsset)) as TMP_FontAsset;
            if (fontAsset == null)
            {
                //Debug.Log("Creating TextMeshPro font asset!");
                fontAsset = ScriptableObject.CreateInstance<TMP_FontAsset>(); // Create new TextMeshPro Font Asset.
                AssetDatabase.CreateAsset(fontAsset, tex_Path_NoExt + ".asset");

                // Set version number of font asset
                fontAsset.version = "1.1.0";

                //Set Font Asset Type
                fontAsset.atlasRenderMode = m_GlyphRenderMode;

                // Reference to the source font file GUID.
                fontAsset.m_SourceFontFileGUID =
                    AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(m_SourceFontFile));

                // Add FaceInfo to Font Asset
                fontAsset.faceInfo = m_FaceInfo;

                // Add GlyphInfo[] to Font Asset
                fontAsset.glyphTable = m_FontGlyphTable;

                // Add CharacterTable[] to font asset.
                fontAsset.characterTable = m_FontCharacterTable;

                // Sort glyph and character tables.
                fontAsset.SortGlyphAndCharacterTables();

                // Get and Add Kerning Pairs to Font Asset
                if (m_IncludeFontFeatures)
                    fontAsset.fontFeatureTable = GetKerningTable();


                // Add Font Atlas as Sub-Asset
                fontAsset.atlasTextures = new Texture2D[] {m_FontAtlasTexture};
                m_FontAtlasTexture.name = tex_FileName + " Atlas";
                fontAsset.atlasWidth = m_AtlasWidth;
                fontAsset.atlasHeight = m_AtlasHeight;
                fontAsset.atlasPadding = m_Padding;

                AssetDatabase.AddObjectToAsset(m_FontAtlasTexture, fontAsset);

                // Create new Material and Add it as Sub-Asset
                Shader default_Shader = Shader.Find("TextMeshPro/Bitmap"); // m_shaderSelection;
                Material tmp_material = new Material(default_Shader);
                tmp_material.name = tex_FileName + " Material";
                tmp_material.SetTexture(ShaderUtilities.ID_MainTex, m_FontAtlasTexture);
                fontAsset.material = tmp_material;

                AssetDatabase.AddObjectToAsset(tmp_material, fontAsset);

            }
            else
            {
                // Find all Materials referencing this font atlas.
                Material[] material_references = TMProFontCustomizedCreater.FindMaterialReferences(fontAsset);

                // Set version number of font asset
                fontAsset.version = "1.1.0";

                // Special handling to remove legacy font asset data
                if (fontAsset.m_glyphInfoList != null && fontAsset.m_glyphInfoList.Count > 0)
                    fontAsset.m_glyphInfoList = null;

                // Destroy Assets that will be replaced.
                if (fontAsset.atlasTextures != null && fontAsset.atlasTextures.Length > 0)
                    DestroyImmediate(fontAsset.atlasTextures[0], true);

                //Set Font Asset Type
                fontAsset.atlasRenderMode = m_GlyphRenderMode;

                // Add FaceInfo to Font Asset
                fontAsset.faceInfo = m_FaceInfo;

                // Add GlyphInfo[] to Font Asset
                fontAsset.glyphTable = m_FontGlyphTable;

                // Add CharacterTable[] to font asset.
                fontAsset.characterTable = m_FontCharacterTable;

                // Sort glyph and character tables.
                fontAsset.SortGlyphAndCharacterTables();

                // Get and Add Kerning Pairs to Font Asset
                if (m_IncludeFontFeatures)
                    fontAsset.fontFeatureTable = GetKerningTable();

                // Add Font Atlas as Sub-Asset
                fontAsset.atlasTextures = new Texture2D[] {m_FontAtlasTexture};
                m_FontAtlasTexture.name = tex_FileName + " Atlas";
                fontAsset.atlasWidth = m_AtlasWidth;
                fontAsset.atlasHeight = m_AtlasHeight;
                fontAsset.atlasPadding = m_Padding;

                // Special handling due to a bug in earlier versions of Unity.
                m_FontAtlasTexture.hideFlags = HideFlags.None;
                fontAsset.material.hideFlags = HideFlags.None;

                AssetDatabase.AddObjectToAsset(m_FontAtlasTexture, fontAsset);

                // Assign new font atlas texture to the existing material.
                fontAsset.material.SetTexture(ShaderUtilities.ID_MainTex, fontAsset.atlasTextures[0]);

                // Update the Texture reference on the Material
                for (int i = 0; i < material_references.Length; i++)
                {
                    material_references[i].SetTexture(ShaderUtilities.ID_MainTex, m_FontAtlasTexture);
                }
            }

            // Add list of GlyphRects to font asset.
            fontAsset.freeGlyphRects = m_FreeGlyphRects;
            fontAsset.usedGlyphRects = m_UsedGlyphRects;

            // Save Font Asset creation settings
            m_SelectedFontAsset = fontAsset;
            m_LegacyFontAsset = null;
            fontAsset.creationSettings = SaveFontCreationSettings();

            AssetDatabase.SaveAssets();

            AssetDatabase.ImportAsset(
                AssetDatabase.GetAssetPath(fontAsset)); // Re-import font asset to get the new updated version.

            //EditorUtility.SetDirty(font_asset);
            fontAsset.ReadFontAssetDefinition();

            AssetDatabase.Refresh();

            m_FontAtlasTexture = null;

            // NEED TO GENERATE AN EVENT TO FORCE A REDRAW OF ANY TEXTMESHPRO INSTANCES THAT MIGHT BE USING THIS FONT ASSET
            TMPro_EventManager.ON_FONT_PROPERTY_CHANGED(true, fontAsset);
        }

        void Save_SDF_FontAsset(string filePath)
        {
            //filePath = filePath.Substring(0, filePath.Length - 6); // Trim file extension from filePath.

            //string dataPath = Application.dataPath;

            //if (filePath.IndexOf(dataPath, System.StringComparison.InvariantCultureIgnoreCase) == -1)
            //{
            //    Debug.LogError(
            //        "You're saving the font asset in a directory outside of this project folder. This is not supported. Please select a directory under \"" +
            //        dataPath + "\"");
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

                // Set version number of font asset
                fontAsset.version = "1.1.0";

                // Reference to source font file GUID.
                fontAsset.m_SourceFontFileGUID =
                    AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(m_SourceFontFile));

                //Set Font Asset Type
                fontAsset.atlasRenderMode = m_GlyphRenderMode;

                // Add FaceInfo to Font Asset
                fontAsset.faceInfo = m_FaceInfo;

                // Add GlyphInfo[] to Font Asset
                fontAsset.glyphTable = m_FontGlyphTable;

                // Add CharacterTable[] to font asset.
                fontAsset.characterTable = m_FontCharacterTable;

                // Sort glyph and character tables.
                fontAsset.SortGlyphAndCharacterTables();

                // Get and Add Kerning Pairs to Font Asset
                if (m_IncludeFontFeatures)
                    fontAsset.fontFeatureTable = GetKerningTable();

                // Add Font Atlas as Sub-Asset
                fontAsset.atlasTextures = new Texture2D[] {m_FontAtlasTexture};
                m_FontAtlasTexture.name = tex_FileName + " Atlas";
                fontAsset.atlasWidth = m_AtlasWidth;
                fontAsset.atlasHeight = m_AtlasHeight;
                fontAsset.atlasPadding = m_Padding;
                if (!m_FontAtlasTexture.name.EndsWith(" Atlas")) // 因为图集复用，所以只要加到第一个资产里
                {
                    m_FontAtlasTexture.name = tex_FileName + " Atlas";
                    AssetDatabase.AddObjectToAsset(m_FontAtlasTexture, fontAsset);
                }

                // Create new Material and Add it as Sub-Asset
                Shader default_Shader = Shader.Find("TextMeshPro/Distance Field");
                Material tmp_material = new Material(default_Shader);

                tmp_material.name = tex_FileName + " Material";
                tmp_material.SetTexture(ShaderUtilities.ID_MainTex, m_FontAtlasTexture);
                tmp_material.SetFloat(ShaderUtilities.ID_TextureWidth, m_FontAtlasTexture.width);
                tmp_material.SetFloat(ShaderUtilities.ID_TextureHeight, m_FontAtlasTexture.height);

                int spread = m_Padding + 1;
                tmp_material.SetFloat(ShaderUtilities.ID_GradientScale,
                    spread); // Spread = Padding for Brute Force SDF.

                tmp_material.SetFloat(ShaderUtilities.ID_WeightNormal, fontAsset.normalStyle);
                tmp_material.SetFloat(ShaderUtilities.ID_WeightBold, fontAsset.boldStyle);

                fontAsset.material = tmp_material;

                AssetDatabase.AddObjectToAsset(tmp_material, fontAsset);

            }
            else
            {
                // Find all Materials referencing this font atlas.
                Material[] material_references = TMP_EditorUtility.FindMaterialReferences(fontAsset);

                // Destroy Assets that will be replaced.
                if (fontAsset.atlasTextures != null && fontAsset.atlasTextures.Length > 0)
                    DestroyImmediate(fontAsset.atlasTextures[0], true);

                // Set version number of font asset
                fontAsset.version = "1.1.0";

                // Special handling to remove legacy font asset data
                if (fontAsset.m_glyphInfoList != null && fontAsset.m_glyphInfoList.Count > 0)
                    fontAsset.m_glyphInfoList = null;

                //Set Font Asset Type
                fontAsset.atlasRenderMode = m_GlyphRenderMode;

                // Add FaceInfo to Font Asset  
                fontAsset.faceInfo = m_FaceInfo;

                // Add GlyphInfo[] to Font Asset
                fontAsset.glyphTable = m_FontGlyphTable;

                // Add CharacterTable[] to font asset.
                fontAsset.characterTable = m_FontCharacterTable;

                // Sort glyph and character tables.
                fontAsset.SortGlyphAndCharacterTables();

                // Get and Add Kerning Pairs to Font Asset
                // TODO: Check and preserve existing adjustment pairs.
                if (m_IncludeFontFeatures)
                    fontAsset.fontFeatureTable = GetKerningTable();

                // Add Font Atlas as Sub-Asset
                fontAsset.atlasTextures = new Texture2D[] {m_FontAtlasTexture};
                m_FontAtlasTexture.name = tex_FileName + " Atlas";
                fontAsset.atlasWidth = m_AtlasWidth;
                fontAsset.atlasHeight = m_AtlasHeight;
                fontAsset.atlasPadding = m_Padding;

                // Special handling due to a bug in earlier versions of Unity.
                m_FontAtlasTexture.hideFlags = HideFlags.None;
                fontAsset.material.hideFlags = HideFlags.None;
                if (!m_FontAtlasTexture.name.EndsWith(" Atlas")) // 因为图集复用，所以只要加到第一个资产里
                {
                    m_FontAtlasTexture.name = tex_FileName + " Atlas";
                    AssetDatabase.AddObjectToAsset(m_FontAtlasTexture, fontAsset);
                }

                // Assign new font atlas texture to the existing material.
                fontAsset.material.SetTexture(ShaderUtilities.ID_MainTex, fontAsset.atlasTextures[0]);

                // Update the Texture reference on the Material
                for (int i = 0; i < material_references.Length; i++)
                {
                    material_references[i].SetTexture(ShaderUtilities.ID_MainTex, m_FontAtlasTexture);
                    material_references[i].SetFloat(ShaderUtilities.ID_TextureWidth, m_FontAtlasTexture.width);
                    material_references[i].SetFloat(ShaderUtilities.ID_TextureHeight, m_FontAtlasTexture.height);

                    int spread = m_Padding + 1;
                    material_references[i]
                        .SetFloat(ShaderUtilities.ID_GradientScale, spread); // Spread = Padding for Brute Force SDF.

                    material_references[i].SetFloat(ShaderUtilities.ID_WeightNormal, fontAsset.normalStyle);
                    material_references[i].SetFloat(ShaderUtilities.ID_WeightBold, fontAsset.boldStyle);
                }
            }

            // Saving File for Debug
            //var pngData = destination_Atlas.EncodeToPNG();
            //File.WriteAllBytes("Assets/Textures/Debug Distance Field.png", pngData);

            // Add list of GlyphRects to font asset.
            fontAsset.freeGlyphRects = m_FreeGlyphRects;
            fontAsset.usedGlyphRects = m_UsedGlyphRects;

            // Save Font Asset creation settings
            m_SelectedFontAsset = fontAsset;
            m_LegacyFontAsset = null;
            fontAsset.creationSettings = SaveFontCreationSettings();

            // 提到这里才能保存完整
            fontAsset.ReadFontAssetDefinition();

            AssetDatabase.SaveAssets();

            AssetDatabase.ImportAsset(
                AssetDatabase.GetAssetPath(fontAsset)); // Re-import font asset to get the new updated version.

            //fontAsset.ReadFontAssetDefinition();

            AssetDatabase.Refresh();

            //m_FontAtlasTexture = null;贴图不要删除

            // NEED TO GENERATE AN EVENT TO FORCE A REDRAW OF ANY TEXTMESHPRO INSTANCES THAT MIGHT BE USING THIS FONT ASSET
            TMPro_EventManager.ON_FONT_PROPERTY_CHANGED(true, fontAsset);
        }

        /// <summary>
        /// Internal method to save the Font Asset Creation Settings
        /// </summary>
        /// <returns></returns>
        FontAssetCreationSettings SaveFontCreationSettings()
        {
            FontAssetCreationSettings settings = new FontAssetCreationSettings();

            //settings.sourceFontFileName = m_SourceFontFile.name;
            settings.sourceFontFileGUID = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(m_SourceFontFile));
            settings.pointSizeSamplingMode = m_PointSizeSamplingMode;
            settings.pointSize = m_PointSize;
            settings.padding = m_Padding;
            settings.packingMode = (int) m_PackingMode;
            settings.atlasWidth = m_AtlasWidth;
            settings.atlasHeight = m_AtlasHeight;
            settings.characterSetSelectionMode = m_CharacterSetSelectionMode;
            settings.characterSequence = m_CharacterSequence;
            settings.referencedFontAssetGUID =
                AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(m_ReferencedFontAsset));
            settings.referencedTextAssetGUID =
                AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(m_CharactersFromFile));
            //settings.fontStyle = (int)m_FontStyle;
            //settings.fontStyleModifier = m_FontStyleValue;
            settings.renderMode = (int) m_GlyphRenderMode;
            settings.includeFontFeatures = m_IncludeFontFeatures;

            return settings;
        }

        // Get Kerning Pairs
        public TMP_FontFeatureTable GetKerningTable()
        {
            GlyphPairAdjustmentRecord[] adjustmentRecords =
                FontEngine.GetGlyphPairAdjustmentTable(m_AvailableGlyphsToAdd.ToArray());

            if (adjustmentRecords == null)
                return null;

            TMP_FontFeatureTable fontFeatureTable = new TMP_FontFeatureTable();

            for (int i = 0; i < adjustmentRecords.Length; i++)
            {
                fontFeatureTable.glyphPairAdjustmentRecords.Add(
                    new TMP_GlyphPairAdjustmentRecord(adjustmentRecords[i]));
            }

            fontFeatureTable.SortGlyphPairAdjustmentRecords();

            return fontFeatureTable;
        }

        #region 修改的部分

        private void GenerateFontAtlasButton()
        {
            if (!m_IsProcessing && m_SourceFontFile != null)
            {
                DestroyImmediate(m_FontAtlasTexture);
                m_FontAtlasTexture = null;
                m_OutputFeedback = string.Empty;
                m_SavedFontAtlas = null;

                FontEngineError errorCode = FontEngine.InitializeFontEngine();
                if (errorCode != FontEngineError.Success)
                {
                    Debug.Log("Font Asset Creator - Error [" + errorCode + "] has occurred while Initializing the FreeType Library.");
                }

                string fontPath = AssetDatabase.GetAssetPath(m_SourceFontFile); // Get file path of TTF Font.

                if (errorCode == FontEngineError.Success)
                {
                    errorCode = FontEngine.LoadFontFace(fontPath);

                    if (errorCode != FontEngineError.Success)
                    {
                        Debug.Log("Font Asset Creator - Error Code [" + errorCode + "] has occurred trying to load the [" + m_SourceFontFile.name + "] font file. This typically results from the use of an incompatible or corrupted font file.");
                    }
                }

                // Define an array containing the characters we will render.
                if (errorCode == FontEngineError.Success)
                {
                    uint[] characterSet = null;

                    // Get list of characters that need to be packed and rendered to the atlas texture.
                    if (m_CharacterSetSelectionMode == 7 || m_CharacterSetSelectionMode == 8)
                    {
                        List<uint> char_List = new List<uint>();

                        for (int i = 0; i < m_CharacterSequence.Length; i++)
                        {
                            uint unicode = m_CharacterSequence[i];

                            // Handle surrogate pairs
                            if (i < m_CharacterSequence.Length - 1 && char.IsHighSurrogate((char)unicode) && char.IsLowSurrogate(m_CharacterSequence[i + 1]))
                            {
                                unicode = (uint)char.ConvertToUtf32(m_CharacterSequence[i], m_CharacterSequence[i + 1]);
                                i += 1;
                            }

                            // Check to make sure we don't include duplicates
                            if (char_List.FindIndex(item => item == unicode) == -1)
                                char_List.Add(unicode);
                        }

                        characterSet = char_List.ToArray();
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

                    m_AtlasGenerationProgress = 0;
                    m_IsProcessing = true;
                    m_IsGenerationCancelled = false;

                    GlyphLoadFlags glyphLoadFlags = ((GlyphRasterModes)m_GlyphRenderMode & GlyphRasterModes.RASTER_MODE_HINTED) == GlyphRasterModes.RASTER_MODE_HINTED ? GlyphLoadFlags.LOAD_RENDER : GlyphLoadFlags.LOAD_RENDER | GlyphLoadFlags.LOAD_NO_HINTING;

                    // 
                    AutoResetEvent autoEvent = new AutoResetEvent(false);

                    // Worker thread to pack glyphs in the given texture space.
                    ThreadPool.QueueUserWorkItem(PackGlyphs =>
                    {
                        // Start Stop Watch
                        m_StopWatch = System.Diagnostics.Stopwatch.StartNew();

                        // Clear the various lists used in the generation process.
                        m_AvailableGlyphsToAdd.Clear();
                        m_MissingCharacters.Clear();
                        m_ExcludedCharacters.Clear();
                        m_CharacterLookupMap.Clear();
                        m_GlyphLookupMap.Clear();
                        m_GlyphsToPack.Clear();
                        m_GlyphsPacked.Clear();

                        // Check if requested characters are available in the source font file.
                        for (int i = 0; i < characterSet.Length; i++)
                        {
                            uint unicode = characterSet[i];
                            uint glyphIndex;

                            if (FontEngine.TryGetGlyphIndex(unicode, out glyphIndex))
                            {
                                // Skip over potential duplicate characters.
                                if (m_CharacterLookupMap.ContainsKey(unicode))
                                    continue;

                                // Add character to character lookup map.
                                m_CharacterLookupMap.Add(unicode, glyphIndex);

                                // Skip over potential duplicate glyph references.
                                if (m_GlyphLookupMap.ContainsKey(glyphIndex))
                                {
                                    // Add additional glyph reference for this character.
                                    m_GlyphLookupMap[glyphIndex].Add(unicode);
                                    continue;
                                }

                                // Add glyph reference to glyph lookup map.
                                m_GlyphLookupMap.Add(glyphIndex, new List<uint>() { unicode });

                                // Add glyph index to list of glyphs to add to texture.
                                m_AvailableGlyphsToAdd.Add(glyphIndex);
                            }
                            else
                            {
                                // Add Unicode to list of missing characters.
                                m_MissingCharacters.Add(unicode);
                            }
                        }

                        // Pack available glyphs in the provided texture space.
                        if (m_AvailableGlyphsToAdd.Count > 0)
                        {
                            int packingModifier = ((GlyphRasterModes)m_GlyphRenderMode & GlyphRasterModes.RASTER_MODE_BITMAP) == GlyphRasterModes.RASTER_MODE_BITMAP ? 0 : 1;

                            if (m_PointSizeSamplingMode == 0) // Auto-Sizing Point Size Mode
                            {
                                // Estimate min / max range for auto sizing of point size.
                                int minPointSize = 0;
                                int maxPointSize = (int)Mathf.Sqrt((m_AtlasWidth * m_AtlasHeight) / m_AvailableGlyphsToAdd.Count) * 3;

                                m_PointSize = (maxPointSize + minPointSize) / 2;

                                bool optimumPointSizeFound = false;
                                for (int iteration = 0; iteration < 15 && optimumPointSizeFound == false; iteration++)
                                {
                                    m_AtlasGenerationProgressLabel = "Packing glyphs - Pass (" + iteration + ")";

                                    FontEngine.SetFaceSize(m_PointSize);

                                    m_GlyphsToPack.Clear();
                                    m_GlyphsPacked.Clear();

                                    m_FreeGlyphRects.Clear();
                                    m_FreeGlyphRects.Add(new GlyphRect(0, 0, m_AtlasWidth - packingModifier, m_AtlasHeight - packingModifier));
                                    m_UsedGlyphRects.Clear();

                                    for (int i = 0; i < m_AvailableGlyphsToAdd.Count; i++)
                                    {
                                        uint glyphIndex = m_AvailableGlyphsToAdd[i];
                                        Glyph glyph;

                                        if (FontEngine.TryGetGlyphWithIndexValue(glyphIndex, glyphLoadFlags, out glyph))
                                        {
                                            if (glyph.glyphRect.width > 0 && glyph.glyphRect.height > 0)
                                            {
                                                m_GlyphsToPack.Add(glyph);
                                            }
                                            else
                                            {
                                                m_GlyphsPacked.Add(glyph);
                                            }
                                        }
                                    }

                                    FontEngine.TryPackGlyphsInAtlas(m_GlyphsToPack, m_GlyphsPacked, m_Padding, (GlyphPackingMode)m_PackingMode, m_GlyphRenderMode, m_AtlasWidth, m_AtlasHeight, m_FreeGlyphRects, m_UsedGlyphRects);

                                    if (m_IsGenerationCancelled)
                                    {
                                        DestroyImmediate(m_FontAtlasTexture);
                                        m_FontAtlasTexture = null;
                                        return;
                                    }

                                    //Debug.Log("Glyphs remaining to add [" + m_GlyphsToAdd.Count + "]. Glyphs added [" + m_GlyphsAdded.Count + "].");

                                    if (m_GlyphsToPack.Count > 0)
                                    {
                                        if (m_PointSize > minPointSize)
                                        {
                                            maxPointSize = m_PointSize;
                                            m_PointSize = (m_PointSize + minPointSize) / 2;

                                            //Debug.Log("Decreasing point size from [" + maxPointSize + "] to [" + m_PointSize + "].");
                                        }
                                    }
                                    else
                                    {
                                        if (maxPointSize - minPointSize > 1 && m_PointSize < maxPointSize)
                                        {
                                            minPointSize = m_PointSize;
                                            m_PointSize = (m_PointSize + maxPointSize) / 2;

                                            //Debug.Log("Increasing point size from [" + minPointSize + "] to [" + m_PointSize + "].");
                                        }
                                        else
                                        {
                                            //Debug.Log("[" + iteration + "] iterations to find the optimum point size of : [" + m_PointSize + "].");
                                            optimumPointSizeFound = true;
                                        }
                                    }
                                }
                            }
                            else // Custom Point Size Mode
                            {
                                m_AtlasGenerationProgressLabel = "Packing glyphs...";

                                // Set point size
                                FontEngine.SetFaceSize(m_PointSize);

                                m_GlyphsToPack.Clear();
                                m_GlyphsPacked.Clear();

                                m_FreeGlyphRects.Clear();
                                m_FreeGlyphRects.Add(new GlyphRect(0, 0, m_AtlasWidth - packingModifier, m_AtlasHeight - packingModifier));
                                m_UsedGlyphRects.Clear();

                                for (int i = 0; i < m_AvailableGlyphsToAdd.Count; i++)
                                {
                                    uint glyphIndex = m_AvailableGlyphsToAdd[i];
                                    Glyph glyph;

                                    if (FontEngine.TryGetGlyphWithIndexValue(glyphIndex, glyphLoadFlags, out glyph))
                                    {
                                        if (glyph.glyphRect.width > 0 && glyph.glyphRect.height > 0)
                                        {
                                            m_GlyphsToPack.Add(glyph);
                                        }
                                        else
                                        {
                                            m_GlyphsPacked.Add(glyph);
                                        }
                                    }
                                }

                                FontEngine.TryPackGlyphsInAtlas(m_GlyphsToPack, m_GlyphsPacked, m_Padding, (GlyphPackingMode)m_PackingMode, m_GlyphRenderMode, m_AtlasWidth, m_AtlasHeight, m_FreeGlyphRects, m_UsedGlyphRects);

                                if (m_IsGenerationCancelled)
                                {
                                    DestroyImmediate(m_FontAtlasTexture);
                                    m_FontAtlasTexture = null;
                                    return;
                                }
                                //Debug.Log("Glyphs remaining to add [" + m_GlyphsToAdd.Count + "]. Glyphs added [" + m_GlyphsAdded.Count + "].");
                            }

                        }
                        else
                        {
                            int packingModifier = ((GlyphRasterModes)m_GlyphRenderMode & GlyphRasterModes.RASTER_MODE_BITMAP) == GlyphRasterModes.RASTER_MODE_BITMAP ? 0 : 1;

                            FontEngine.SetFaceSize(m_PointSize);

                            m_GlyphsToPack.Clear();
                            m_GlyphsPacked.Clear();

                            m_FreeGlyphRects.Clear();
                            m_FreeGlyphRects.Add(new GlyphRect(0, 0, m_AtlasWidth - packingModifier, m_AtlasHeight - packingModifier));
                            m_UsedGlyphRects.Clear();
                        }

                        //Stop StopWatch
                        m_StopWatch.Stop();
                        m_GlyphPackingGenerationTime = m_StopWatch.Elapsed.TotalMilliseconds;
                        Debug.Log("Glyph packing completed in: " + m_GlyphPackingGenerationTime.ToString("0.000 ms."));
                        m_StopWatch.Reset();

                        m_FontCharacterTable.Clear();
                        m_FontGlyphTable.Clear();
                        m_GlyphsToRender.Clear();

                        // Add glyphs and characters successfully added to texture to their respective font tables.
                        foreach (Glyph glyph in m_GlyphsPacked)
                        {
                            uint glyphIndex = glyph.index;

                            m_FontGlyphTable.Add(glyph);

                            // Add glyphs to list of glyphs that need to be rendered.
                            if (glyph.glyphRect.width > 0 && glyph.glyphRect.height > 0)
                                m_GlyphsToRender.Add(glyph);

                            foreach (uint unicode in m_GlyphLookupMap[glyphIndex])
                            {
                                // Create new Character
                                m_FontCharacterTable.Add(new TMP_Character(unicode, glyph));
                            }
                        }

                        // 
                        foreach (Glyph glyph in m_GlyphsToPack)
                        {
                            foreach (uint unicode in m_GlyphLookupMap[glyph.index])
                            {
                                m_ExcludedCharacters.Add(unicode);
                            }
                        }

                        // Get the face info for the current sampling point size.
                        m_FaceInfo = FontEngine.GetFaceInfo();

                        autoEvent.Set();
                    });

                    // Worker thread to render glyphs in texture buffer.
                    ThreadPool.QueueUserWorkItem(RenderGlyphs =>
                    {
                        autoEvent.WaitOne();

                        // Start Stop Watch
                        m_StopWatch = System.Diagnostics.Stopwatch.StartNew();

                        m_IsRenderingDone = false;

                        // Allocate texture data
                        m_AtlasTextureBuffer = new byte[m_AtlasWidth * m_AtlasHeight];

                        m_AtlasGenerationProgressLabel = "Rendering glyphs...";

                        // Render and add glyphs to the given atlas texture.
                        if (m_GlyphsToRender.Count > 0)
                        {
                            FontEngine.RenderGlyphsToTexture(m_GlyphsToRender, m_Padding, m_GlyphRenderMode, m_AtlasTextureBuffer, m_AtlasWidth, m_AtlasHeight);
                        }

                        ThreadRenderBackupFont(0, m_AtlasWidth);

                        m_IsRenderingDone = true;

                        // Stop StopWatch
                        m_StopWatch.Stop();
                        m_GlyphRenderingGenerationTime = m_StopWatch.Elapsed.TotalMilliseconds;
                        Debug.Log("Font Atlas generation completed in: " + m_GlyphRenderingGenerationTime.ToString("0.000 ms."));
                        m_StopWatch.Reset();
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

            List<uint> list = new List<uint>();
            for (int i = 0; i < m_MissingCharacters.Count; i++)
            {
                list.Add(m_MissingCharacters[i]);
            }

            // 如果有指定字体，则在这里插入
            if (m_CharacterUseFontBackup != null && m_CharacterUseFontBackup.Length > backupLevel)
            {
                foreach (var character in m_CharacterUseFontBackup[backupLevel])
                {
                    if (!string.IsNullOrEmpty(character))
                    {
                        for (int i = 0; i < character.Length; i++)
                        {
                            // Check to make sure we don't include duplicates
                            if (list.FindIndex(item => item == character[i]) == -1)
                                list.Add(character[i]);
                        }
                    }
                }
            }

            if (list.Count == 0)
            {
                return;
            }

            string fontPath = m_FontBackupPaths[backupLevel];
            FontEngineError errorCode = FontEngine.LoadFontFace(fontPath);
            if (errorCode != FontEngineError.Success)
            {
                return;
            }

            var tmpAtlasWidth = 512;
            var tmpAtlasHeight = 512;
            var tmpTextureBuffer = new byte[tmpAtlasWidth * tmpAtlasHeight];
            GlyphLoadFlags glyphLoadFlags = ((GlyphRasterModes)m_GlyphRenderMode & GlyphRasterModes.RASTER_MODE_HINTED) == GlyphRasterModes.RASTER_MODE_HINTED ? GlyphLoadFlags.LOAD_RENDER : GlyphLoadFlags.LOAD_RENDER | GlyphLoadFlags.LOAD_NO_HINTING;
            List<Glyph> glyphsToRender = new List<Glyph>();
            foreach (var unicode in list)
            {
                uint glyphIndex;

                if (FontEngine.TryGetGlyphIndex(unicode, out glyphIndex))
                {
                    Glyph glyph;

                    if (FontEngine.TryGetGlyphWithIndexValue(glyphIndex, glyphLoadFlags, out glyph))
                    {
                        glyphsToRender.Add(glyph);
                    }
                }
            }

            errorCode = FontEngine.RenderGlyphsToTexture(glyphsToRender, m_Padding, m_GlyphRenderMode, tmpTextureBuffer, tmpAtlasWidth, tmpAtlasHeight);
            if (errorCode != 0)
            {
                return;
            }

            int wordWidth = m_PointSize;
            int xStart = xOffsetDist - m_Padding * 2 - wordWidth;   // 从padding开始拷贝，否则会出现负偏移丢失的情况
            int yStart = m_AtlasHeight - m_Padding - 1;
            int numY = 0;
            for (int index = 0; index < glyphsToRender.Count; ++index)
            {
                if (!Mathf.Approximately(glyphsToRender[index].glyphRect.x, -1))
                {
                    var gi = glyphsToRender[index].glyphRect;
                    var x = Mathf.FloorToInt(gi.x) - m_Padding;
                    var y = tmpAtlasHeight - (Mathf.FloorToInt(gi.y) - m_Padding);
                    var w = Mathf.CeilToInt(gi.width) + m_Padding * 2;
                    var h = Mathf.CeilToInt(gi.height) + m_Padding * 2;

                    for (int r = 0; r < h; r++)
                    {
                        for (int c = 0; c < w; c++)
                        {
                            m_AtlasTextureBuffer[(yStart - r) * m_AtlasWidth + c + xStart] =
                                tmpTextureBuffer[(y - r) * tmpAtlasWidth + c + x];
                        }
                    }
                    var idx = m_GlyphsToRender.FindIndex(glyph => glyph.index == glyphsToRender[index].index);
                    if (idx == -1)
                    {
                        m_GlyphsToRender.Add(glyphsToRender[index]);
                        idx = m_GlyphsToRender.Count - 1;
                    }

                    var gi2 = m_GlyphsToRender[idx].glyphRect;
                    gi2.x = xStart + m_Padding;
                    gi2.y = m_AtlasHeight - yStart + m_Padding;
                    gi2.width = gi.width;
                    gi2.height = gi.height;
                    m_GlyphsToRender[idx].glyphRect = gi2;

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
        private string[][] m_CharacterUseFontBackup;

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
                str1 = "t:TMP_FontAsset " + info.fontName + "_SDF";
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
            m_GlyphRenderMode = (GlyphRenderMode)settings.renderMode;
            m_IncludeFontFeatures = settings.includeFontFeatures;
            m_FontBackupPaths = settings.fontBackupPaths;
            m_CharacterUseFontBackup = settings.characterUseFontBackup;

            if (string.IsNullOrEmpty(m_WarningMessage) || m_SelectedFontAsset || m_LegacyFontAsset || m_SavedFontAtlas || m_IsFontAtlasInvalid)
            {
                // 仅为了去除警告
            }
        }

        private void OnMyGUI()
        {
            GUI.enabled = !this.m_IsProcessing;
            EditorGUI.indentLevel++;
            GUILayout.Label(" Font Word", EditorStyles.boldLabel);
            if (GUILayout.Button("生成字库文本", GUILayout.MinHeight(22f)))
            {
                //Tools.UITools.TextMeshProFontTextGen.GenChineseText();
            }

            if (m_FontAssetInfos.Count > 0)
            {
                GUILayout.Space(10f);
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
                m_AtlasGenerationProgress = FontEngine.generationProgress;
                m_FontAssetInfos[m_CurGenerateIndex].genPercent = m_AtlasGenerationProgress * 100;

                m_IsRepaintNeeded = true;
            }

            // Update Feedback Window & Create Font Texture once Rendering is done.
            if (m_IsRenderingDone)
            {
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
                    m_FontAtlasTexture = null;
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

            // 如果指定字体里有包含的，则要在这里去除掉
            if (m_CharacterUseFontBackup != null)
            {
                foreach (var characters in m_CharacterUseFontBackup)
                {
                    foreach (var character in characters)
                    {
                        if (!string.IsNullOrEmpty(character))
                        {
                            m_CharacterSequence = m_CharacterSequence.Replace(character, System.String.Empty);
                        }
                    }
                }
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
}