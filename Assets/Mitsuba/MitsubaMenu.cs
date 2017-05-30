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
	public class MitsubaMenu : MonoBehaviour
	{
		protected static GameObject comparisonCamera;
		protected static GameObject mainCamera;

		static string RenderUnityScene()
		{
		   // Take a screenshot
		   string screenshotFilename = MitsubaPreferences.targetFolder + Path.DirectorySeparatorChar + MitsubaTools.GetSceneName() + "_unity.exr";
		   MitsubaTools.LogMsg("Saving screenshot " + screenshotFilename);
		   ScreenCapture.CaptureScreenshot(screenshotFilename);

		   return screenshotFilename;
		}

		static string ToggleComparison()
		{
			if (comparisonCamera != null)
			{
				DestroyImmediate(comparisonCamera);
				comparisonCamera = null;

				if (mainCamera)
					mainCamera.GetComponent<Camera>().enabled = true;

				return "";
			}

            string inFile = MitsubaScene.CreateMitsubaScene();
            string outFile = MitsubaScene.RenderMitsubaScene(inFile);
            Texture2D mitsubaScene = MitsubaScene.GetMitsubaScene(outFile);

			EditorUtility.DisplayProgressBar(MitsubaTools.appName, "Rendering with Unity...", 0.5f);

			mainCamera = MitsubaTools.GetCamera().gameObject;

			if (mainCamera == null)
				throw new System.Exception("Unable to fetch camera for comparison");

			comparisonCamera = GameObject.Instantiate(mainCamera);
			comparisonCamera.AddComponent<ComparisonFX>();

			Camera camera = comparisonCamera.GetComponent<Camera>();
			camera.enabled = true;
			camera.targetTexture = null;

			Material material = comparisonCamera.GetComponent<ComparisonFX>().material;

			Texture2D colormap = Resources.Load<Texture2D>("divergent");
			colormap.wrapMode = TextureWrapMode.Clamp;

			material.SetTexture("_ColormapTex", colormap);
			material.SetTexture("_RenderMitsubaTex", mitsubaScene);

			mainCamera.GetComponent<Camera>().enabled = false;

			Selection.activeGameObject = comparisonCamera;

			EditorUtility.DisplayProgressBar(MitsubaTools.appName, "Done.", 1.0f);

			return "";
		}

		delegate string Del();

		static void MenuHelper(Del d)
		{
			try
			{
				//MitsubaTools.ClearConsole();

				if (!MitsubaTools.SanityCheck())
				{
					if (!EditorUtility.DisplayDialog(
						"Sanity check failed",
						"Your scene has failed one or more consitency checks and will look different in Mitsuba. Are you sure you want to continue?",
						"Continue",
						"Cancel"))
						return;
				}

				d();
			}
			catch(System.Exception e)
			{
				MitsubaTools.LogError(e.ToString());
			}

			EditorUtility.ClearProgressBar();
		}

		[MenuItem("MitsubaExport/Toggle comparison")]
		static void MenuCC()
		{
			MenuHelper(ToggleComparison);
		}

		[MenuItem("MitsubaExport/Render Unity scene")]
		static void MenuRUR()
		{
			MenuHelper(RenderUnityScene);
		}

		[MenuItem("MitsubaExport/Render Mitsuba scene")]
		static void MenuRMR()
		{
			MenuHelper(MitsubaScene.RenderMitsubaScene);
		}

		[MenuItem("MitsubaExport/Create Mitsuba scene")]
		static void MenuCMS()
		{
			MenuHelper(MitsubaScene.CreateMitsubaScene);
		}
	}
}
