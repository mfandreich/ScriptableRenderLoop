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
using UnityEditor.SceneManagement;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System;
using System.Globalization;
using UnityEngine.Rendering;
using System.Xml;

namespace MitsubaExporter
{
    public struct MitsubaTools
    {
        public static string appName = "MitsubaExport";
        private static CultureInfo culture = new CultureInfo("en-US");
        private static Camera camera = null; // Allow to overwrite default camera

        // ---------------------------------------------------------------
        // General helper functions
        // ---------------------------------------------------------------

        public static void CreateTargetFolder()
        {
        	LogMsg("Creating output folder " + MitsubaPreferences.targetFolder);
        	System.IO.Directory.CreateDirectory(MitsubaPreferences.targetFolder);
        }

        public static string GetFullDatabasePath(Int32 instanceID)
        {
            string path = AssetDatabase.GetAssetPath(instanceID);
            string fullpath = Path.Combine(Directory.GetCurrentDirectory(), path);

            #if UNITY_EDITOR_WIN
                fullpath = fullpath.Replace("/", "\\");
            #endif

            return fullpath;
        }

        public static string ToString(float val)
        {
            return Convert.ToString(val, culture);
        }

        public static string ToString(Vector3 v)
        {
            return ToString(v.x) + ", " + ToString(v.y) + ", " + ToString(v.z);
        }

        public static void LogMsg(string msg)
        {
            Debug.Log(appName + ": " + msg);
        }

        public static void LogWarning(string msg)
        {
            Debug.LogWarning(appName + ": " + msg);
        }

        public static void LogError(string msg)
        {
            Debug.LogError(appName + ": " + msg);
        }

        public static void ClearConsole ()
        {
            // This simply does "LogEntries.Clear()" the long way:
            var logEntries = System.Type.GetType("UnityEditorInternal.LogEntries,UnityEditor.dll");
            var clearMethod = logEntries.GetMethod("Clear", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
            clearMethod.Invoke(null,null);
        }

        public static string GetSceneName()
        {
            // Export all meshes in a single obj file
            string sceneName = EditorSceneManager.GetActiveScene().name;
            int stripIndex = sceneName.LastIndexOf(Path.PathSeparator);

            if(stripIndex >= 0)
                sceneName = sceneName.Substring(stripIndex + 1).Trim();

            return sceneName;
        }



        // ---------------------------------------------------------------
        // OBJ Export
        // ---------------------------------------------------------------

        public static string MeshToString(MeshFilter mf)
        {
            int vertexOffset = 0;
            int normalOffset = 0;
            int uvOffset = 0;

            Mesh m = mf.sharedMesh;
            Material[] mats = mf.GetComponent<Renderer>().sharedMaterials;

            StringBuilder sb = new StringBuilder();

            sb.Append("usemtl " + mats[0].name + "\n");
            sb.Append("g ").Append(mf.name).Append("\n");

            foreach(Vector3 v in m.vertices)
            {
                sb.Append(string.Format("v {0} {1} {2}\n", v.x, v.y, v.z));
            }

            sb.Append("\n");

            foreach(Vector3 n in m.normals)
            {
                sb.Append(string.Format("vn {0} {1} {2}\n", n.x, n.y, n.z));
            }

            sb.Append("\n");

            foreach(Vector3 uv in m.uv)
            {
                sb.Append(string.Format("vt {0} {1}\n", uv.x, uv.y));
            }

            for(int materialIdx = 0; materialIdx < m.subMeshCount; materialIdx ++)
            {
                sb.Append("\n");

                int[] triangles = m.GetTriangles(materialIdx);
                for(int i = 0; i < triangles.Length; i += 3)
                {
                    sb.Append(string.Format("f {0}/{0}/{0} {1}/{1}/{1} {2}/{2}/{2}\n",
                        triangles[i+0]+1 + vertexOffset, triangles[i+1]+1 + normalOffset, triangles[i+2]+1 + uvOffset));
                }
            }

            vertexOffset += m.vertices.Length;
            normalOffset += m.normals.Length;
            uvOffset += m.uv.Length;

            return sb.ToString();
        }

        public static Hash128 GetGeoHash(MeshFilter mf)
        {
            return Hash128.Parse(
                mf.sharedMesh.ToString() +
                mf.sharedMesh.vertexCount.ToString() +
                mf.sharedMesh.triangles.ToString() +
                mf.sharedMesh.normals.ToString()
            );
        }

        public static void ExportObjMeshes(string folder, string filenamePrefix, List<MeshFilter> mf)
        {
            LogMsg("Exporting OBJ meshes...");

            int i = 0;

            foreach(MeshFilter meshFilter in mf)
            {
                string filename = filenamePrefix + "_" + i.ToString();
                string fullFilename = folder + Path.DirectorySeparatorChar + filename + ".obj";

                //if (!File.Exists(fullFilename))
                {
                    using(StreamWriter sw = new StreamWriter(fullFilename))
                    {
                        EditorUtility.DisplayProgressBar(appName, "Exporting meshes...", (float)i/mf.Count);
                        sw.Write(MeshToString(meshFilter));
                    }
                }
                //else
                //	LogMsg(filename + "already exists, skipping export.");

                i++;
            }
        }

        // ---------------------------------------------------------------
        // Unity Helper Functions
        // ---------------------------------------------------------------

        public static float GammaToLinearSpace(float value)
        {
            if(value <= 0.04045f)
                return value / 12.92f;
            else if(value < 1.0F)
                return Mathf.Pow((value + 0.055f)/1.055f, 2.4f);
            else if(value == 1.0F)
                return 1.0f;
            else
                return Mathf.Pow(value, 2.2f); // Never ever do this! But unity is doing it :( ....
        }

        public static float LinearSpaceToGamma(float value)
        {
            if (value <= 0.0F)
                return 0.0F;
            else if (value <= 0.0031308F)
                return 12.92F * value;
            else if (value < 1.0F)
                return 1.055F * Mathf.Pow(value, 0.4166667f) - 0.055f;
            else if (value == 1.0f)
                return 1.0f;
            else
                return Mathf.Pow(value, 0.45454545454545f);  // Never ever do this! But unity is doing it :( ....
        }

        public static Color GammaToLinearSpace(Color value)
        {
            return new Color(GammaToLinearSpace(value.r), GammaToLinearSpace(value.g), GammaToLinearSpace(value.b));
        }

        public static Color LinearSpaceToGamma(Color value)
        {
            return new Color(LinearSpaceToGamma(value.r), LinearSpaceToGamma(value.g), LinearSpaceToGamma(value.b));
        }

        public static string GetFilename(Texture texture)
        {
             return texture ? GetFullDatabasePath(texture.GetInstanceID()) : "";
        }

        public static void SetCamera(Camera inCamera)
        {
            camera = inCamera;
        }

        public static Camera GetCamera()
        {
            if (camera != null)
                return camera;

            if (Camera.main)
                return Camera.main;

            return SceneView.lastActiveSceneView.camera;
        }

        public static Texture2D CreateReadableTexture(Texture2D texture, bool isLinear)
        {
            // Create a temporary RenderTexture of the same size as the texture
            RenderTexture tmp = RenderTexture.GetTemporary(
                                texture.width,
                                texture.height,
                                0,
                                RenderTextureFormat.ARGB32,
                                isLinear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);

            // Blit the pixels on texture to the RenderTexture
            Graphics.Blit(texture, tmp);

            // Backup the currently set RenderTexture
            RenderTexture previous = RenderTexture.active;

            // Set the current RenderTexture to the temporary one we created
            RenderTexture.active = tmp;

            // Create a new readable Texture2D to copy the pixels to it
            Texture2D readable = new Texture2D(texture.width, texture.height);

            // Copy the pixels from the RenderTexture to the new Texture
            readable.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            readable.Apply();

            // Reset the active RenderTexture
            RenderTexture.active = previous;

            // Release the temporary RenderTexture
            RenderTexture.ReleaseTemporary(tmp);

            return readable;
        }

        public static void WritePNG(Texture2D texture, string filename)
        {
            byte[] bytes = texture.EncodeToPNG();
            File.WriteAllBytes(filename, bytes);
        }

        public static Texture2D LoadPNG(string filename)
        {
            Texture2D tex = null;
            byte[] fileData;

            if (File.Exists(filename))
            {
                fileData = File.ReadAllBytes(filename);
                tex = new Texture2D(2, 2);
                tex.LoadImage(fileData);
                return tex;
            }

            throw new System.Exception(filename + " does not exist.");
        }

        enum ClearFlags { Skybox, SolidColor, Invalid };

        public static bool SanityCheck()
        {
        	bool isSane = true;

        	if (QualitySettings.activeColorSpace != ColorSpace.Linear)
        	{
        		MitsubaTools.LogWarning("Colorspace is not set to linear!");
        		isSane = false;
        	}

        	Dictionary<CameraClearFlags, ClearFlags> CCFdict = new Dictionary<CameraClearFlags, ClearFlags>
            {
        		{ CameraClearFlags.Skybox, ClearFlags.Skybox },
        		{ CameraClearFlags.SolidColor, ClearFlags.SolidColor },
        		{ CameraClearFlags.Depth, ClearFlags.Invalid },
        		{ CameraClearFlags.Nothing, ClearFlags.Invalid }
            };

        	Dictionary<AmbientMode, ClearFlags> AMdict = new Dictionary<AmbientMode, ClearFlags>
            {
        		{ AmbientMode.Skybox, ClearFlags.Skybox },
        		{ AmbientMode.Trilight, ClearFlags.Invalid },
        		{ AmbientMode.Flat, ClearFlags.SolidColor },
        		{ AmbientMode.Custom, ClearFlags.Invalid }
            };

        	Dictionary<ReflectionProbeClearFlags, ClearFlags> RPdict = new Dictionary<ReflectionProbeClearFlags, ClearFlags>
            {
        		{ ReflectionProbeClearFlags.Skybox, ClearFlags.Skybox },
        		{ ReflectionProbeClearFlags.SolidColor, ClearFlags.SolidColor }
            };

        	ClearFlags cf = CCFdict[MitsubaTools.GetCamera().clearFlags];
            Color bgColor = Color.black;

            // Ignore color of scene view camera/set it to black
            if (MitsubaTools.GetCamera() != SceneView.lastActiveSceneView.camera)
            {
                bgColor = MitsubaTools.GetCamera().backgroundColor;
                bgColor.a = Color.black.a; // Ignore alpha value for comparison
            }

        	if (cf != AMdict[RenderSettings.ambientMode])
        	{
        		MitsubaTools.LogWarning("Camera background mode doesn't match with Environment lighting ambient mode");
        		isSane = false;
        	}

        	if (bgColor != Color.black)
        	{
        		MitsubaTools.LogWarning("Solid background color must be black, ambient rendering differs between Unity and Mitsuba (i.e, not " + bgColor + ")!");
        		isSane = false;
        	}

        	if (RenderSettings.ambientLight != bgColor)
        	{
        		MitsubaTools.LogWarning("Environment lighting ambient color doesn't match Camera background color: "  + RenderSettings.ambientLight + " vs " + bgColor);
        		isSane = false;
        	}

        	foreach (ReflectionProbe r in Resources.FindObjectsOfTypeAll(typeof(ReflectionProbe)))
        	{
                // Skip default probes
                if (r.hideFlags == HideFlags.NotEditable || r.hideFlags == HideFlags.HideAndDontSave)
                    continue;

        		if (cf != RPdict[r.clearFlags])
        		{
        			MitsubaTools.LogWarning("Camera background mode doesn't match with a ReflectionProbe background mode");
        			isSane = false;
        		}

        		if (r.backgroundColor != bgColor)
        		{
        			MitsubaTools.LogWarning("A reflection probe ''" + r.name + "'' has different background color from everything else: " + r.backgroundColor + " vs " + bgColor);
        			isSane = false;
        		}

        		if (r.intensity != 1.0f)
        		{
        			MitsubaTools.LogWarning("A reflection probe has an intensity not at 1.0f!");
                    isSane = false;
        		}

                if (!r.hdr)
                {
                    MitsubaTools.LogWarning("A reflection probe is not set to HDR!");
                    isSane = false;
                }
        	}

            if (!MitsubaTools.GetCamera().hdr)
            {
                MitsubaTools.LogWarning("Camera is not HDR!");
                isSane = false;
            }

        	foreach (Light l in Resources.FindObjectsOfTypeAll(typeof(Light)))
        	{
        		if (l.shadowStrength != 1.0f)
        		{
        			MitsubaTools.LogWarning("Shadow strength of a lightsource must be 1.0 to match Mitsuba!");
        			isSane = false;
        		}

        		if (l.bounceIntensity != 1.0f)
        		{
        			MitsubaTools.LogWarning("A lightsource has bounceIntensity not set to 1.0f (not PBS)!");
        			isSane = false;
        		}
        	}

        	if (RenderSettings.reflectionIntensity != 1.0f)
        	{
        		MitsubaTools.LogWarning("Lighting reflection intensity is not 1.0 (not PBS)!");
        		isSane = false;
        	}

        	if (Lightmapping.bounceBoost != 1.0f)
        	{
        		MitsubaTools.LogWarning("Lightmapping bounceBoost is not 1.0 (not PBS)!");
        		isSane = false;
        	}

        	if (Lightmapping.indirectOutputScale != 1.0f)
        	{
        		MitsubaTools.LogWarning("Lightmapping indirectOutputScale is not 1.0 (not PBS)!");
        		isSane = false;
        	}

            if(RenderSettings.ambientMode == AmbientMode.Skybox)
            {
                if (RenderSettings.skybox.shader.name != "Skybox/Cubemap")
                {
                    MitsubaTools.LogWarning("Skybox shader in Lighting has to be Skybox/Cubemap");
                    isSane = false;
                }
            }

        	return isSane;
        }
    }
}
