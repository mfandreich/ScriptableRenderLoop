using System;
using UnityEditor;

//using EditorGUIUtility=UnityEditor.EditorGUIUtility;

namespace UnityEngine.Experimental.ScriptableRenderLoop
{
    [CustomEditor(typeof(HDRenderLoop))]
    public class HDRenderLoopInspector : Editor
    {
        private class Styles
        {
            public readonly GUIContent debugParameters = new GUIContent("Debug Parameters");
            public readonly GUIContent debugViewMaterial = new GUIContent("DebugView Material", "Display various properties of Materials.");

            public readonly GUIContent displayOpaqueObjects = new GUIContent("Display Opaque Objects", "Toggle opaque objects rendering on and off.");
            public readonly GUIContent displayTransparentObjects = new GUIContent("Display Transparent Objects", "Toggle transparent objects rendering on and off.");
            public readonly GUIContent enableTonemap = new GUIContent("Enable Tonemap");
            public readonly GUIContent exposure = new GUIContent("Exposure");

            public readonly GUIContent useForwardRenderingOnly = new GUIContent("Use Forward Rendering Only");
            public readonly GUIContent useDepthPrepass = new GUIContent("Use Depth Prepass");

            public bool isDebugViewMaterialInit = false;
            public GUIContent[] debugViewMaterialStrings = null;
            public int[] debugViewMaterialValues = null;

            public readonly GUIContent skyParameters = new GUIContent("Sky Parameters");
            public readonly GUIContent skyExposure = new GUIContent("Sky Exposure");
            public readonly GUIContent skyRotation = new GUIContent("Sky Rotation");
            public readonly GUIContent skyMultiplier = new GUIContent("Sky Multiplier");
        }

        private static Styles s_Styles = null;

        private static Styles styles
        {
            get
            {
                if (s_Styles == null)
                    s_Styles = new Styles();
                return s_Styles;
            }
        }

        const float k_MaxExposure = 32.0f;

        string GetSubNameSpaceName(Type type)
        {
            return type.Namespace.Substring(type.Namespace.LastIndexOf((".")) + 1) + "/";
        }

        void FillWithProperties(Type type, GUIContent[] debugViewMaterialStrings, int[] debugViewMaterialValues, bool isBSDFData, string strSubNameSpace, ref int index)
        {
            var attributes = type.GetCustomAttributes(true);
            // Get attribute to get the start number of the value for the enum
            var attr = attributes[0] as GenerateHLSL;

            if (!attr.needParamDefines)
            {
                return ;
            }

            var fields = type.GetFields();

            var localIndex = 0;
            foreach (var field in fields)
            {
                var fieldName = field.Name;

                // Check if the display name have been override by the users
                if (Attribute.IsDefined(field, typeof(SurfaceDataAttributes)))
                {
                    var propertyAttr = (SurfaceDataAttributes[])field.GetCustomAttributes(typeof(SurfaceDataAttributes), false);
                    if (propertyAttr[0].displayName != "")
                    {
                        fieldName = propertyAttr[0].displayName;
                    }
                }

                fieldName = (isBSDFData ? "Engine/" : "") + strSubNameSpace + fieldName;

                debugViewMaterialStrings[index] = new GUIContent(fieldName);
                debugViewMaterialValues[index] = attr.paramDefinesStart + (int)localIndex;
                index++;
                localIndex++;
            }
        }

        void FillWithPropertiesEnum(Type type, GUIContent[] debugViewMaterialStrings, int[] debugViewMaterialValues, string prefix, bool isBSDFData, ref int index)
        {
            var names = Enum.GetNames(type);

            var localIndex = 0;
            foreach (var value in Enum.GetValues(type))
            {
                var valueName = (isBSDFData ? "Engine/" : "" + prefix) + names[localIndex];

                debugViewMaterialStrings[index] = new GUIContent(valueName);
                debugViewMaterialValues[index] = (int)value;
                index++;
                localIndex++;
            }
        }

        public override void OnInspectorGUI()
        {
            var renderLoop = target as HDRenderLoop;

            if (!renderLoop)
                return;

            var debugParameters = renderLoop.debugParameters;

            EditorGUILayout.LabelField(styles.debugParameters);
            EditorGUI.indentLevel++;
            EditorGUI.BeginChangeCheck();

            if (!styles.isDebugViewMaterialInit)
            {
                var varyingNames = Enum.GetNames(typeof(Attributes.DebugViewVarying));
                var gbufferNames = Enum.GetNames(typeof(Attributes.DebugViewGbuffer));

                // +1 for the zero case
                var num = 1 + varyingNames.Length
                          + gbufferNames.Length
                          + typeof(Builtin.BuiltinData).GetFields().Length * 2 // BuildtinData are duplicated for each material
                          + typeof(Lit.SurfaceData).GetFields().Length
                          + typeof(Lit.BSDFData).GetFields().Length
                          + typeof(Unlit.SurfaceData).GetFields().Length
                          + typeof(Unlit.BSDFData).GetFields().Length;

                styles.debugViewMaterialStrings = new GUIContent[num];
                styles.debugViewMaterialValues = new int[num];

                var index = 0;

                // 0 is a reserved number
                styles.debugViewMaterialStrings[0] = new GUIContent("None");
                styles.debugViewMaterialValues[0] = 0;
                index++;

                FillWithPropertiesEnum(typeof(Attributes.DebugViewVarying), styles.debugViewMaterialStrings, styles.debugViewMaterialValues, GetSubNameSpaceName(typeof(Attributes.DebugViewVarying)), false, ref index);
                FillWithProperties(typeof(Builtin.BuiltinData), styles.debugViewMaterialStrings, styles.debugViewMaterialValues, false, GetSubNameSpaceName(typeof(Lit.SurfaceData)), ref index);
                FillWithProperties(typeof(Lit.SurfaceData), styles.debugViewMaterialStrings, styles.debugViewMaterialValues, false, GetSubNameSpaceName(typeof(Lit.SurfaceData)), ref index);
                FillWithProperties(typeof(Builtin.BuiltinData), styles.debugViewMaterialStrings, styles.debugViewMaterialValues, false, GetSubNameSpaceName(typeof(Unlit.SurfaceData)), ref index);
                FillWithProperties(typeof(Unlit.SurfaceData), styles.debugViewMaterialStrings, styles.debugViewMaterialValues, false, GetSubNameSpaceName(typeof(Unlit.SurfaceData)), ref index);                

                // Engine
                FillWithPropertiesEnum(typeof(Attributes.DebugViewGbuffer), styles.debugViewMaterialStrings, styles.debugViewMaterialValues, "", true, ref index);
                FillWithProperties(typeof(Lit.BSDFData), styles.debugViewMaterialStrings, styles.debugViewMaterialValues, true, "", ref index);
                FillWithProperties(typeof(Unlit.BSDFData), styles.debugViewMaterialStrings, styles.debugViewMaterialValues, true, "", ref index);

                styles.isDebugViewMaterialInit = true;
            }

            debugParameters.debugViewMaterial = EditorGUILayout.IntPopup(styles.debugViewMaterial, (int)debugParameters.debugViewMaterial, styles.debugViewMaterialStrings, styles.debugViewMaterialValues);

            EditorGUILayout.Space();
            debugParameters.enableTonemap = EditorGUILayout.Toggle(styles.enableTonemap, debugParameters.enableTonemap);
            debugParameters.exposure = Mathf.Max(Mathf.Min(EditorGUILayout.FloatField(styles.exposure, debugParameters.exposure), k_MaxExposure), -k_MaxExposure);

            EditorGUILayout.Space();
            debugParameters.displayOpaqueObjects = EditorGUILayout.Toggle(styles.displayOpaqueObjects, debugParameters.displayOpaqueObjects);
            debugParameters.displayTransparentObjects = EditorGUILayout.Toggle(styles.displayTransparentObjects, debugParameters.displayTransparentObjects);
			debugParameters.useForwardRenderingOnly = EditorGUILayout.Toggle(styles.useForwardRenderingOnly, debugParameters.useForwardRenderingOnly);
            debugParameters.useDepthPrepass = EditorGUILayout.Toggle(styles.useDepthPrepass, debugParameters.useDepthPrepass);            

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(renderLoop); // Repaint
            }
            EditorGUI.indentLevel--;

            var skyParameters = renderLoop.skyParameters;

            EditorGUILayout.LabelField(styles.skyParameters);
            EditorGUI.indentLevel++;
            EditorGUI.BeginChangeCheck();

            skyParameters.skyHDRI = (Cubemap)EditorGUILayout.ObjectField("Cubemap", skyParameters.skyHDRI, typeof(Cubemap), false);
            skyParameters.exposure = Mathf.Max(Mathf.Min(EditorGUILayout.FloatField(styles.skyExposure, skyParameters.exposure), 32), -32);
            skyParameters.multiplier = Mathf.Max(EditorGUILayout.FloatField(styles.skyMultiplier, skyParameters.multiplier), 0);
            skyParameters.rotation = Mathf.Max(Mathf.Min(EditorGUILayout.FloatField(styles.skyRotation, skyParameters.rotation), 360), 0);
 
            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(renderLoop); // Repaint
            }
            EditorGUI.indentLevel--;
        }
    }
}