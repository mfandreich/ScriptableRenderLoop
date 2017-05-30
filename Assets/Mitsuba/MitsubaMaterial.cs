/*
 * The MIT License (MIT)
 *
 * Copyright (c) 2016, Unity Technologies
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 */

using UnityEngine;
using UnityEditor;
using System.IO;

namespace MitsubaExporter
{
    public struct MitsubaTextureProperties
    {
        public float uscale;
        public float vscale;
        public float uoffset;
        public float voffset;
        public bool isLinear;
        public string filterMode;
        public string wrapMode;
    }

    public struct MitsubaTexture
    {
        public string filename;
        public MitsubaTextureProperties properties;
    }

    public struct MitsubaMaterial
    {
        public bool isSpecularSetup;

        public float metallic;
        public MitsubaTexture metallicTex;

        public float smoothness;
        public MitsubaTexture smoothnessTex;

        public Color diffColor;
        public MitsubaTexture diffColorTex;

        public Color specColor;
        public MitsubaTexture specColorTex;

        public Color emissiveColor;

        public MitsubaTexture normalMapTex;


        public float roughnessU;
        public float roughnessV;



        static bool IsLinear(Texture t)
        {
            if (!t) return true;

            // texture was sampled with gamma?
			string path = AssetDatabase.GetAssetPath(t.GetInstanceID());
			TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
            return ti.linearTexture;
        }

        static void GetProps(ref MitsubaTextureProperties p, Texture t)
        {
            if (!t) return;

			string path = AssetDatabase.GetAssetPath(t.GetInstanceID());

			TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;

            p.isLinear = ti.linearTexture;
            p.wrapMode = (ti.wrapMode == TextureWrapMode.Clamp) ? "clamp" : "repeat";

            if (ti.filterMode == FilterMode.Point)
                p.filterMode = "point";
            else if (ti.filterMode == FilterMode.Trilinear)
                p.filterMode = "trilinear";
            else
                MitsubaTools.LogWarning("Texture filterMode not supported: " + t.name);
        }

        // Mitsuba only support jpeg or PNG format.
        // Unity support various format (even PSD!) and can ack texture in RGB and A. To deal easily with all this we start from the engine texture and export it
        // with the help of a render to texture. This also mean that we will get the same compression artifacts of the runtime texture (like DXT) which may be a good thing for comparison purpose.
        static string RenderToPng(Texture2D texture, bool alpha)
        {
            if (!texture)
                return "";

            // Retrieve filename and check if exist, in this case don't re-export depends on option
            string inFile = MitsubaTools.GetFilename(texture);
            string filename = Path.GetFileNameWithoutExtension(Path.GetFileName(inFile)) + (alpha ? "_a.png" : "_rgb.png");
            string outFile = MitsubaPreferences.targetFolder + Path.DirectorySeparatorChar + filename;

            bool shouldProcess = MitsubaPreferences.textureOverwrite ? true : !File.Exists(outFile);

            string path = AssetDatabase.GetAssetPath(texture.GetInstanceID());
            TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;

            bool isXYDXT5Normal = (ti.textureType == TextureImporterType.NormalMap);

            if (shouldProcess)
            {
                bool isLinear = IsLinear(texture);

                // To be able to call get pixels, we must create a readable texture
                Texture2D readableTex = MitsubaTools.CreateReadableTexture(texture, isLinear);
                Texture2D writeableTex = new Texture2D(readableTex.width, readableTex.height, TextureFormat.RGB24, false);

                // Go through all pixel and extract RGB or A depends on option
                for (int y = 0; y < readableTex.height; ++y)
                {
                    for (int x = 0; x < readableTex.width; ++x)
                    {
                        Color c = readableTex.GetPixel(x, y);

                        Color color = alpha ? new Color(c.a, c.a, c.a, 1) : new Color(c.r, c.g, c.b, 1);

                        if (isXYDXT5Normal)
                        {
                            float nx = c.a * 2.0f - 1.0f;
                            float ny = c.g * 2.0f - 1.0f;
                            float nz = Mathf.Sqrt(1.0f - (nx * nx + ny * ny));

                            // From Mitsuba normapmap.cpp
                            // * To turn the 3D normal directions into (nonnegative) color values
                            // * suitable for this plugin, the
                            // * mapping $x \mapsto (x+1)/2$ must be applied to each component.

                            // TODO: Find orientation convention of Mitsuba for normal map
                            color = new Color(nx * 0.5f + 0.5f, ny * 0.5f + 0.5f, nz * 0.5f + 0.5f, 1);
                        }

                        // gamma sRGB correct if needed (SetPixels don't handle it
                        writeableTex.SetPixel(x, y, (!alpha && !isLinear) ? MitsubaTools.LinearSpaceToGamma(color) : color);
                    }
                }

                MitsubaTools.WritePNG(writeableTex, outFile);
            }

            return filename;
        }

        public MitsubaMaterial(Material material)
        {
            // Texture tiling coefficients
            MitsubaTextureProperties mainTextureProperties;
            mainTextureProperties.uscale = material.mainTextureScale.x;
            mainTextureProperties.vscale = material.mainTextureScale.y;
            mainTextureProperties.uoffset = material.mainTextureOffset.x;
            mainTextureProperties.voffset = material.mainTextureOffset.y;

            mainTextureProperties.isLinear = false;
            mainTextureProperties.wrapMode = "";
            mainTextureProperties.filterMode = "";

            // specular setup?
            isSpecularSetup = material.shader.name == "Standard (Specular setup)";

            // REad each inputs

            // Diffuse texture/color
            diffColor = MitsubaTools.GammaToLinearSpace(material.GetColor("_Color"));
            Texture unity_diffColorTex = material.GetTexture("_MainTex");
            diffColorTex.filename = RenderToPng(unity_diffColorTex as Texture2D, false);
            diffColorTex.properties = mainTextureProperties;
            GetProps(ref diffColorTex.properties, unity_diffColorTex);

            // Normalmap
            // TODO: Check but it should work with stuff like create from grayscale
            Texture unity_bumpMap = material.GetTexture("_BumpMap");
            normalMapTex.filename = RenderToPng(unity_bumpMap as Texture2D, false);
            normalMapTex.properties = mainTextureProperties;
            GetProps(ref normalMapTex.properties, unity_bumpMap);

            // emissive color
            emissiveColor = MitsubaTools.GammaToLinearSpace(material.GetColor("_EmissionColor"));

            float roughness = (1.0f - material.GetFloat("_Smoothness")) * (1.0f - material.GetFloat("_Smoothness"));
            float anisotropy = material.GetFloat("_Anisotropy");

            float anisoAspect = Mathf.Sqrt(1.0f - 0.9f * anisotropy);

            roughnessU = roughness / anisoAspect; // Distort along tangent (rougher)
            roughnessV = roughness * anisoAspect; // Straighten along bitangent (smoother)

            // smoothness
            smoothness = material.GetFloat("_Glossiness");
            // material.IsKeywordEnabled("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            bool isSmoothnessInAlpha = false;
            // Get smoothness map
            Texture unity_SmoothnessTex = material.GetTexture(isSmoothnessInAlpha ? "_MainTex" : (isSpecularSetup ? "_SpecGlossMap" : "_MetallicGlossMap"));
            smoothnessTex.filename = RenderToPng(unity_SmoothnessTex as Texture2D, true);
            smoothnessTex.properties = mainTextureProperties;
            GetProps(ref smoothnessTex.properties, unity_SmoothnessTex);
            // Caution: Smoothness as it is in alpha channel are always linear
            smoothnessTex.properties.isLinear = true;

            // Specular/Metallic
            if (!isSpecularSetup)
            {
                // metallic texture/float
                metallic = material.GetFloat("_Metallic");
				metallic = Mathf.Pow(metallic, 2.2f); // Arf Unity gamma correct this parameter, what a mess... So currently do the same

                Texture unity_MettalicTex = material.GetTexture("_MetallicGlossMap");
                metallicTex.filename = RenderToPng(unity_MettalicTex as Texture2D, false);
                metallicTex.properties = mainTextureProperties;
                GetProps(ref metallicTex.properties, unity_MettalicTex);

                // no specColor
                specColor = new Color(0, 0, 0, 0);
                specColorTex.filename = "";
                specColorTex.properties = mainTextureProperties;
            }
            else
            {
                // specular texture/color
                specColor = MitsubaTools.GammaToLinearSpace(material.GetColor("_SpecColor"));
                Texture unity_SpecTex = material.GetTexture("_SpecGlossMap");
                specColorTex.filename = RenderToPng(unity_SpecTex as Texture2D, false);
                specColorTex.properties = mainTextureProperties;
                GetProps(ref specColorTex.properties, unity_SpecTex);

                // no metallic
                metallic = 0;
                metallicTex.filename = "";
                metallicTex.properties = mainTextureProperties;
            }
        }
    }
}
