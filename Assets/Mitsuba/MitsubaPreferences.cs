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

namespace MitsubaExporter
{
    [InitializeOnLoad]
    public class MitsubaPreferences
    {
        static bool loaded = false;

        public static string version = "0.5.0";
        public static string targetFolder = "MitsubaExport";

        #if UNITY_EDITOR_WIN
			public const string MITSUBA_EXECUTABLE_DEFAULT = "C:/Program Files/Mitsuba 0.5.0/mitsuba.exe";
        #else
			public const string MITSUBA_EXECUTABLE_DEFAULT = "/Applications/Mitsuba.app/Contents/MacOS/mitsuba";
        #endif

		public static string mitsubaExecutable = MITSUBA_EXECUTABLE_DEFAULT;

        public static bool multiBounceEnable = true;
        public static bool exportEnvironment = true;
        public static bool exportLights = true;
        public static int sampleCount = 128;
        public static bool preliminarySpecular = true;
        public static bool exportEXR = false;
        public static bool textureOverwrite = false;

        static MitsubaPreferences()
        {
            Load();
        }

        [PreferenceItem("Mitsuba")]
        static void PreferenceGUI()
        {
            if(!loaded)
                Load();

            GUILayout.Space(10);
            version = EditorGUILayout.TextField("version", version);
            targetFolder = EditorGUILayout.TextField("targetFolder", targetFolder);
			mitsubaExecutable = EditorGUILayout.TextField("mitsubaExectuable", mitsubaExecutable);
            multiBounceEnable = EditorGUILayout.Toggle("multiBounceEnable", multiBounceEnable);
            exportEnvironment = EditorGUILayout.Toggle("exportEnvironment", exportEnvironment);
            exportLights = EditorGUILayout.Toggle("exportLights", exportLights);
            sampleCount = EditorGUILayout.IntField("sampleCount", sampleCount);
            exportEXR = EditorGUILayout.Toggle("exportEXR", exportEXR);
            textureOverwrite = EditorGUILayout.Toggle("textureOverwrite", textureOverwrite);

            if(GUI.changed)
                Save();
        }

        static void Load()
        {
            version = EditorPrefs.GetString("Mitsuba.version", "0.5.0");
            targetFolder = EditorPrefs.GetString("Mitsuba.targetFolder", "MitsubaExport");
			mitsubaExecutable = EditorPrefs.GetString("Mitsuba.mitsubaExecutable", MITSUBA_EXECUTABLE_DEFAULT);
            multiBounceEnable = EditorPrefs.GetBool("Mitsuba.multiBounceEnable", true);
            exportEnvironment = EditorPrefs.GetBool("Mitsuba.exportEnvironment", true);
            exportLights = EditorPrefs.GetBool("Mitsuba.exportLights", true);
            sampleCount = EditorPrefs.GetInt("Mitsuba.sampleCount", 64);
            exportEXR = EditorPrefs.GetBool("Mitsuba.exportEXR", false);
            textureOverwrite = EditorPrefs.GetBool("Mitsuba.textureOverwrite", false);

            loaded = true;
        }

        static void Save()
        {
            EditorPrefs.SetString("Mitsuba.version", version);
            EditorPrefs.SetString("Mitsuba.targetFolder", targetFolder);
			EditorPrefs.SetString("Mitsuba.mitsubaExecutable", mitsubaExecutable);
            EditorPrefs.SetBool("Mitsuba.multiBounceEnable", multiBounceEnable);
            EditorPrefs.SetBool("Mitsuba.exportEnvironment", exportEnvironment);
            EditorPrefs.SetBool("Mitsuba.exportLights", exportLights);
            EditorPrefs.SetInt("Mitsuba.sampleCount", sampleCount);
            EditorPrefs.SetBool("Mitsuba.exportEXR", exportEXR);
            EditorPrefs.SetBool("Mitsuba.textureOverwrite", textureOverwrite);
        }
    }
}
