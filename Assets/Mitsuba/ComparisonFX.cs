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
    [ExecuteInEditMode, RequireComponent(typeof(Camera))]
    public class ComparisonFX : MonoBehaviour
    {
        [Range (0, 1)]
        public float ColormapScreenOccupancy = 1.0f;
        [Range (0, 1)]
        public float MitsubaScreenOccupancy = 0.5f;
        [Range(0, 1)]
        public float ColormapScale = 0.1f;
        [Range(-16, 16)]
        public float Exposure = 0.0f;
        [Range(1, 16)]
        public float Gamma = 1.0f;

        public Material m_Material;

        public Material material
        {
            get
            {
                if (m_Material == null)
                    m_Material = new Material(Shader.Find("PostFX")) { hideFlags = HideFlags.DontSave };

                return m_Material;
            }
        }

        private void OnDisable()
        {
            if (m_Material != null)
                DestroyImmediate(m_Material);

            m_Material = null;
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            material.SetFloat("_MitsubaScreenOccupancy", MitsubaScreenOccupancy);
            material.SetFloat("_ColormapScreenOccupancy", ColormapScreenOccupancy);
            material.SetFloat("_ColormapScale", ColormapScale);
            material.SetFloat("_Exposure", Exposure);
            material.SetFloat("_Gamma", Gamma);
            Graphics.Blit(source, destination, material);
        }
    }
}
