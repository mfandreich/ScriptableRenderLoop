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
	public class MitsubaScene
	{
		protected static XmlDocument xml;

		// helper function to add an attribute with a name and value to a node
        static XmlAttribute AddXmlAttribute(XmlNode node, string name, string value)
        {
            XmlAttribute attr = xml.CreateAttribute(name);
            attr.Value = value;
            node.Attributes.Append(attr);
            return attr;
        }

		static XmlNode CreateNode(string name)
		{
			return xml.CreateElement(name);
		}

		static XmlNode CreatePODNode(string type, string name, string value)
		{
			XmlNode pod = CreateNode(type);
			AddXmlAttribute(pod, "name", name);
			AddXmlAttribute(pod, "value", value);
			return pod;
		}

        static XmlNode CreateBooleanNode(string name, bool value)
        {
            return CreatePODNode("boolean", name, value ? "true" : "false");
        }

		static XmlNode CreateIntegerNode(string name, int value)
		{
			return CreatePODNode("integer", name, value.ToString());
		}

		static XmlNode CreateStringNode(string name, string value)
		{
			return CreatePODNode("string", name, value);
		}

		static XmlNode CreateFloatNode(string name, float value)
		{
			return CreatePODNode("float", name, MitsubaTools.ToString(value));
		}

		static XmlNode CreateRGBNode(string name, Vector3 value)
		{
			return CreatePODNode("rgb", name, MitsubaTools.ToString(value));
		}

        static XmlNode CreateRGBNode(string name, Color value)
        {
            return CreateRGBNode(name, new Vector3(value.r, value.g, value.b));
        }

		static XmlNode CreateRotateNode(string axis, float angle)
		{
			XmlNode rotateNode = CreateNode("rotate");

			AddXmlAttribute(rotateNode, axis, "1");
			AddXmlAttribute(rotateNode, "angle", MitsubaTools.ToString(angle));

			return rotateNode;
		}

		static XmlNode CreateLookAtNode(Vector3 position, Vector3 target, Vector3 up)
		{
			XmlNode lookAtNode = CreateNode("lookat");

			AddXmlAttribute(lookAtNode, "origin", MitsubaTools.ToString(position));
			AddXmlAttribute(lookAtNode, "target", MitsubaTools.ToString(target));
			AddXmlAttribute(lookAtNode, "up", MitsubaTools.ToString(up));

			return lookAtNode;
		}

		static XmlNode AddVectorXmlAttribute(XmlNode node, Vector3 vec)
		{
			AddXmlAttribute(node, "x", MitsubaTools.ToString(vec.x));
			AddXmlAttribute(node, "y", MitsubaTools.ToString(vec.y));
			AddXmlAttribute(node, "z", MitsubaTools.ToString(vec.z));

			return node;
		}

		static XmlNode CreateGenericVectorNode(string type, Vector3 vec)
		{
			XmlNode vectorNode = CreateNode(type);

			AddVectorXmlAttribute(vectorNode, vec);

			return vectorNode;
		}

		static XmlNode CreatePointNode(Vector3 point)
		{
			return CreateGenericVectorNode("point", point);
		}

		static XmlNode CreateTranslateNode(Vector3 dir)
		{
			return CreateGenericVectorNode("translate", dir);
		}

		static XmlNode CreateScaleNode(Vector3 scale)
		{
			return CreateGenericVectorNode("scale", scale);
		}

        static XmlNode CreateScaleNode(string axis, float scale)
		{
            XmlNode scaleNode = CreateNode("scale");

			AddXmlAttribute(scaleNode, axis, MitsubaTools.ToString(scale));

			return scaleNode;
		}

		static XmlNode CreateVectorNode(string name, Vector3 vec)
		{
			XmlNode vectorNode = CreateGenericVectorNode("vector", vec);
			AddXmlAttribute(vectorNode, "name", name);
			return vectorNode;
		}

		static XmlNode CreateTextureNode(string name, MitsubaTexture texture)
		{
			XmlNode textureNode = CreateNode("texture");

			AddXmlAttribute(textureNode, "type", "bitmap");
			AddXmlAttribute(textureNode, "name", name);

			textureNode.AppendChild(CreateStringNode("filename", texture.filename));
			textureNode.AppendChild(CreateFloatNode("uscale", texture.properties.uscale));
			textureNode.AppendChild(CreateFloatNode("vscale", texture.properties.vscale));
			textureNode.AppendChild(CreateFloatNode("uoffset", texture.properties.uoffset));
			textureNode.AppendChild(CreateFloatNode("voffset", texture.properties.voffset));
			textureNode.AppendChild(CreateFloatNode("gamma", texture.properties.isLinear ? 1.0f : -1.0f));

			if (texture.properties.filterMode != "")
				textureNode.AppendChild(CreateStringNode("filterType", texture.properties.filterMode));

			textureNode.AppendChild(CreateStringNode("wrapMode", texture.properties.wrapMode));

			string[] split = texture.filename.Split('.');
			string extension = split[split.Length - 1];

			if (extension.ToLower() == "psd")
				MitsubaTools.LogWarning("Mitsuba cannot read Photoshop texture " + texture.filename);

			return textureNode;
		}

		static XmlNode CreateTextureOrSpectrumNode(string name, MitsubaTexture texture, Color color)
		{
			XmlNode textureOrColorNode;

			if(texture.filename != "")
				textureOrColorNode = CreateTextureNode(name, texture);
			else
				textureOrColorNode = CreateRGBNode(name, color);

			return textureOrColorNode;
		}

		static XmlNode CreateTextureOrFloatNode(string name, MitsubaTexture texture, float f)
		{
			XmlNode textureOrColorNode;

			if(texture.filename != "")
				textureOrColorNode = CreateTextureNode(name, texture);
			else
				textureOrColorNode = CreateFloatNode(name, f);

			return textureOrColorNode;
		}

		static XmlNode CreateTransformNode(Transform transform, bool global = true)
		{
			XmlNode transformNode = CreateNode("transform");
			AddXmlAttribute(transformNode, "name", "toWorld");

			transformNode.AppendChild(CreateScaleNode(global ? transform.lossyScale : transform.localScale));

			Vector3 euler = global ? transform.rotation.eulerAngles : transform.localEulerAngles;

			transformNode.AppendChild(CreateRotateNode("x", euler.x));
			transformNode.AppendChild(CreateRotateNode("y", euler.y));
			transformNode.AppendChild(CreateRotateNode("z", euler.z));

			transformNode.AppendChild(CreateTranslateNode(global ? transform.position : transform.localPosition));

			return transformNode;
		}

		static XmlNode CreateSensorNode(Camera camera)
		{
			Vector3 position = camera.transform.position;
			Vector3 up = camera.transform.up;
			Vector3 target = camera.transform.position + camera.transform.forward * 5.0f;

			XmlNode sensorNode = CreateNode("sensor");
			AddXmlAttribute(sensorNode, "type", "perspective");
			{
				sensorNode.AppendChild(CreateStringNode("fovAxis", "y"));
				sensorNode.AppendChild(CreateFloatNode("fov", camera.fieldOfView));
				sensorNode.AppendChild(CreateFloatNode("nearClip", camera.nearClipPlane));
				sensorNode.AppendChild(CreateFloatNode("farClip", camera.farClipPlane));
				sensorNode.AppendChild(CreateFloatNode("focusDistance", 1000));

				XmlNode transformNode = CreateNode("transform");
				AddXmlAttribute(transformNode, "name", "toWorld");
				{
                    // Mitsuba is right handed, Unity is left handed -> Convert with scale
                    transformNode.AppendChild(CreateScaleNode("x", -1.0f));
					transformNode.AppendChild(CreateLookAtNode(position, target, up));
				}
				sensorNode.AppendChild(transformNode);

				XmlNode samplerNode = CreateNode("sampler");
				AddXmlAttribute(samplerNode, "type", "ldsampler");
				{
					samplerNode.AppendChild(CreateIntegerNode("sampleCount", MitsubaPreferences.sampleCount));
				}
				sensorNode.AppendChild(samplerNode);

				XmlNode filmNode = CreateNode("film");
				AddXmlAttribute(filmNode, "type", MitsubaPreferences.exportEXR ? "hdrfilm" : "ldrfilm");
				{
					filmNode.AppendChild(CreateIntegerNode("width", camera.pixelWidth));
					filmNode.AppendChild(CreateIntegerNode("height", camera.pixelHeight));
					filmNode.AppendChild(CreateBooleanNode("banner", false));
					filmNode.AppendChild(CreateFloatNode("gamma", MitsubaPreferences.exportEXR ? 1.0f : -1.0f));
					filmNode.AppendChild(CreateFloatNode("exposure", 0.0f));

					XmlNode rfilterNode = CreateNode("rfilter");
					AddXmlAttribute(rfilterNode, "type", "gaussian");
                    // Don't do a too much wide gaussian else we loose sharpness for comparison
                    rfilterNode.AppendChild(CreateFloatNode("stddev", 0.2f));
					filmNode.AppendChild(rfilterNode);
				}
                sensorNode.AppendChild(filmNode);
			}

			return sensorNode;
		}

		static XmlNode CreateEmitterNode(Material mat)
		{
			MitsubaMaterial m = new MitsubaMaterial(mat);
			XmlNode emissiveEmitterNode = null;

			if (m.emissiveColor != Color.black)
			{
				emissiveEmitterNode = CreateNode("emitter");
				AddXmlAttribute(emissiveEmitterNode, "type", "area");
				{
					emissiveEmitterNode.AppendChild(CreateRGBNode("radiance", m.emissiveColor));
				}
			}

			return emissiveEmitterNode;
		}

		static XmlNode CreateEmitterNode(Light light)
		{
			XmlNode lightNode = CreateNode("emitter");

			if(light.type == LightType.Directional)
			{
				AddXmlAttribute(lightNode, "type", "directional");
				{
					Vector3 direction = light.transform.forward;

					lightNode.AppendChild(CreateVectorNode("direction", direction));

					// TODO: Unity used a wrong conversion :( .... To stay compatible until fixed
					Color linear = MitsubaTools.GammaToLinearSpace(light.intensity * light.color);
					lightNode.AppendChild(CreateRGBNode("irradiance", linear * Mathf.PI));
				}
			}
			else if(light.type == LightType.Point)
			{
				AddXmlAttribute(lightNode, "type", "point");
				{
					Vector3 position = light.transform.position;
					XmlNode positionNode = CreatePointNode(position);
					AddXmlAttribute(positionNode, "name", "position");
					lightNode.AppendChild(positionNode);

					// TODO: Unity used a wrong conversion :( .... To stay compatible until fixed
					Color linear = MitsubaTools.GammaToLinearSpace(light.intensity * light.color);
					lightNode.AppendChild(CreateRGBNode("intensity", linear));
				}
			}
			else if(light.type == LightType.Spot)
			{
				AddXmlAttribute(lightNode, "type", "point");
				{
					lightNode.AppendChild(CreateFloatNode("cutoffAngle", light.spotAngle));

					// TODO: Unity used a wrong conversion :( .... To stay compatible until fixed
					Color linear = MitsubaTools.GammaToLinearSpace(light.intensity * light.color);
					lightNode.AppendChild(CreateRGBNode("intensity", linear));

					lightNode.AppendChild(CreateTransformNode(light.transform));
				}
			}
			else if(light.type == LightType.Area)
			{
				Matrix4x4 transform = light.transform.localToWorldMatrix;

				float area = light.areaSize.x * light.areaSize.y;

				// area / 10000.0f;
				// Area in m^2 to cm^2 conversion.
				float areaNormalisation = area;

				// Mitsuba shapes are oriented along +Z
				Matrix4x4 localTransform = Matrix4x4.Scale(new Vector3(light.areaSize.x * 0.5f, light.areaSize.y * 0.5f, 1.0f));
				localTransform = transform * localTransform;

				Vector3 position = light.transform.position;

				float angle;
				Vector3 axis;
				light.transform.rotation.ToAngleAxis(out angle, out axis);

				lightNode = CreateNode("shape");
				AddXmlAttribute(lightNode, "type", "rectangle");
				{
					XmlNode transformNode = CreateNode("transform");
					AddXmlAttribute(transformNode, "name", "toWorld");
					{
						XmlNode scaleNode = CreateScaleNode(new Vector3(light.areaSize.x * 0.5f, light.areaSize.y * 0.5f, 1.0f));
						transformNode.AppendChild(scaleNode);
					}
					lightNode.AppendChild(transformNode);

					XmlNode translateNode = CreateTranslateNode(position);
					lightNode.AppendChild(translateNode);

					XmlNode emitterNode = CreateNode("emitter");
					AddXmlAttribute(emitterNode, "type", "area");
					{
						Color linear = MitsubaTools.GammaToLinearSpace(light.intensity * light.color) * areaNormalisation;
						emitterNode.AppendChild(CreateRGBNode("radiance", linear));
					}
					lightNode.AppendChild(emitterNode);
				}
			}
			else
				throw new System.Exception("Lightsource " + light.name + " could not be converted.");

			return lightNode;
		}

        static XmlNode CreateBSDFNode(string matName, Material material)
        {
            XmlNode bsdfNode = CreateNode("bsdf");

            if (material.shader.name.Substring(0, 20) == "HDRenderPipeline/Lit")
            {
                MitsubaMaterial props = new MitsubaMaterial(material);
                AddXmlAttribute(bsdfNode, "name", matName);
                AddXmlAttribute(bsdfNode, "type", "roughconductor");
                {
                    bsdfNode.AppendChild(CreateFloatNode("alphaU", props.roughnessU));
                    bsdfNode.AppendChild(CreateFloatNode("alphaV", props.roughnessV));
                    bsdfNode.AppendChild(CreateStringNode("material", "Cr"));

                    /*
                    // if the surface is normalmapped, wrap it in a normalmap BSDF and instead return this
                    if (props.normalMapTex.filename != "")
                    {
                        XmlNode normalMapBSDFNode = CreateNode("bsdf");
                        AddXmlAttribute(normalMapBSDFNode, "type", "normalmap");
                        {
                            XmlNode normalTextureNode = CreateTextureNode("normal", props.normalMapTex);
                            {
                                normalTextureNode.AppendChild(CreateFloatNode("gamma", 1.0f));
                            }

                            normalMapBSDFNode.AppendChild(normalTextureNode);
                            normalMapBSDFNode.AppendChild(bsdfNode);
                        }
                        return normalMapBSDFNode;
                    }
                    */
                }
            }
            else if (material.shader.name.Substring(0, 8) == "Standard")
            {
                MitsubaMaterial props = new MitsubaMaterial(material);
                AddXmlAttribute(bsdfNode, "name", matName);
                AddXmlAttribute(bsdfNode, "type", "unitystd");
                {
                    bsdfNode.AppendChild(CreateBooleanNode("isSpecularSetup", props.isSpecularSetup));
                    if (props.isSpecularSetup) // standard shader (specular)
                    {
                        bsdfNode.AppendChild(CreateTextureOrFloatNode("smoothness", props.smoothnessTex, props.smoothness));
                        bsdfNode.AppendChild(CreateTextureOrSpectrumNode("diffColor", props.diffColorTex, props.diffColor));
                        bsdfNode.AppendChild(CreateTextureOrSpectrumNode("specColor", props.specColorTex, props.specColor));
                    }
                    else // standard shader
                    {
                        bsdfNode.AppendChild(CreateTextureOrFloatNode("metallic", props.metallicTex, props.metallic));
                        bsdfNode.AppendChild(CreateTextureOrSpectrumNode("diffColor", props.diffColorTex, props.diffColor));
                        bsdfNode.AppendChild(CreateTextureOrFloatNode("smoothness", props.smoothnessTex, props.smoothness));
                    }

                    // if the surface is normalmapped, wrap it in a normalmap BSDF and instead return this
                    if (props.normalMapTex.filename != "")
                    {
                        XmlNode normalMapBSDFNode = CreateNode("bsdf");
                        AddXmlAttribute(normalMapBSDFNode, "type", "normalmap");
                        {
                            XmlNode normalTextureNode = CreateTextureNode("normal", props.normalMapTex);
                            {
                                normalTextureNode.AppendChild(CreateFloatNode("gamma", 1.0f));
                            }

                            normalMapBSDFNode.AppendChild(normalTextureNode);
                            normalMapBSDFNode.AppendChild(bsdfNode);
                        }
                        return normalMapBSDFNode;
                    }
                }
            }
            else
            {
                throw new System.Exception("Material " + material.name + " cannot be exported because it isn't of the Standard type.");
            }

            return bsdfNode;
        }
		static XmlNode CreateEnvironmentEmitter()
		{
			XmlNode emitterNode = null;

			if(RenderSettings.ambientMode == AmbientMode.Flat)
			{
				// We cannot use this code for non-black constants:
				// Unity mutliplies ambient and diffuse colors of each material
				// Mitsuba evaluates BRDF for constant emitters

				Color skyColor = MitsubaTools.GammaToLinearSpace(RenderSettings.ambientLight);

				if (skyColor == Color.black)
				{
					MitsubaTools.LogMsg("Skip exporting black constant emitter to reduce noise.");
					return null;
				}

				emitterNode = CreateNode("emitter");
				AddXmlAttribute(emitterNode, "type", "constant");
				{
					emitterNode.AppendChild(CreateRGBNode("radiance", skyColor));
				}
			}
			else if(RenderSettings.ambientMode == AmbientMode.Skybox)
			{
				Material skybox = RenderSettings.skybox;

				if(skybox != null)
				{
					Texture HDRTex = null;

					// Try to find a texture
					for (int i = 0; i < ShaderUtil.GetPropertyCount(skybox.shader) ; i++)
						if (ShaderUtil.GetPropertyType(skybox.shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
						{
							string propName = ShaderUtil.GetPropertyName(skybox.shader, i);
							HDRTex = skybox.GetTexture(propName);
						}

					if(HDRTex != null)
					{
						string HDRFilename = MitsubaTools.GetFilename(HDRTex);

						// Caution: No tint in Mitsuba, Tint color in Unity must be neutral(0.5 in Tint in Unity because of the weird behavior of Unity).
						// Mitsuba support .exr or RGBE(.hdr) - Latlong only!
						emitterNode = CreateNode("emitter");
						AddXmlAttribute(emitterNode, "type", "envmap");
						{
							emitterNode.AppendChild(CreateFloatNode("scale", RenderSettings.ambientIntensity));
							emitterNode.AppendChild(CreateStringNode("filename", "D:\\ScriptableRenderLoop\\ScriptableRenderLoop\\Assets\\TestScenes\\HDTest\\HDRI\\MuirWood - White balanced.exr"));

							XmlNode transformNode = CreateNode("transform");
							AddXmlAttribute(transformNode, "name", "toWorld");
							{
								transformNode.AppendChild(CreateScaleNode(new Vector3(-1, 1, 1)));
							}

							emitterNode.AppendChild(transformNode);
						}
					}
				}
			}

			if (emitterNode == null)
				MitsubaTools.LogWarning("Could not convert environment lighting.");

			return emitterNode;
		}

		static string ExportMitsubaScene(string folder, string sceneName, List<MeshFilter> mfList, List<Light> lightList)
		{
            MitsubaTools.LogMsg("Exporting Mitsuba scene...");

			xml = new XmlDocument();

			// Create default XML header
			XmlNode xmlHeader = xml.CreateXmlDeclaration("1.0", "utf-8", null);
			xml.AppendChild(xmlHeader);

			// Create scene
			XmlNode sceneNode = CreateNode("scene");
			AddXmlAttribute(sceneNode, "version", MitsubaPreferences.version);
			xml.AppendChild(sceneNode);

			// Select integrator scheme
			XmlNode integratorNode = CreateNode("integrator");

			// bdpt (bidirectional), mlt (veach) -> samplertype = independent
			AddXmlAttribute(integratorNode, "type",(MitsubaPreferences.multiBounceEnable) ? "path" : "direct");
			sceneNode.AppendChild(integratorNode);

			// Export camera
			sceneNode.AppendChild(CreateSensorNode(MitsubaTools.GetCamera()));

			// Export Environment
	        if(MitsubaPreferences.exportEnvironment)
	        {
				XmlNode emitterNode = CreateEnvironmentEmitter();

				if (emitterNode != null)
					sceneNode.AppendChild(emitterNode);
	        }

			// Export lights
			if(MitsubaPreferences.exportLights)
			{
				foreach(Light light in lightList)
				{
					XmlNode lightNode = CreateEmitterNode(light);

					if (lightNode != null)
						sceneNode.AppendChild(lightNode);
				}
			}

			int i = 0;

			foreach(MeshFilter mf in mfList)
			{
				EditorUtility.DisplayProgressBar(MitsubaTools.appName, "Exporting scene...", (float)i/mfList.Count);

				// Export Material and objects
				XmlNode shapeNode = CreateNode("shape");
				AddXmlAttribute(shapeNode, "type", "obj");
				{
					shapeNode.AppendChild(CreateTransformNode(mf.transform));

					shapeNode.AppendChild(CreateStringNode("filename", MitsubaTools.GetSceneName() + "_geo_" + i.ToString() + ".obj"));
					i++;

					Material[] materialList = mf.GetComponent<Renderer>().sharedMaterials;

					bool hasEmitter = false;

					foreach(Material mat in materialList)
					{
						shapeNode.AppendChild(CreateBSDFNode(mat.name, mat));

						XmlNode emitterNode = CreateEmitterNode(mat);
						if (emitterNode != null)
						{
							if (!hasEmitter)
								shapeNode.AppendChild(emitterNode);

							hasEmitter = true;
						}
					}
				}
				sceneNode.AppendChild(shapeNode);
			}

			string sceneFilename = folder + Path.DirectorySeparatorChar + sceneName + ".xml";

			MitsubaTools.LogMsg("Saving Mitsuba scene " + sceneFilename);
			xml.Save(sceneFilename);

			return sceneFilename;
		}

        // Create Mitsuba scene from a given camera and game objects
        public static string CreateMitsubaScene(Camera camera, GameObject gameObject)
        {
            MitsubaTools.SetCamera(camera);
            Transform[] selection = gameObject.GetComponentsInChildren<Transform>();
            string str = CreateMitsubaScene(selection);
            MitsubaTools.SetCamera(null);
            return str;
        }

        // Create a Mitsuba scene from selection or select all objects for the scene if nothing is selected
        public static string CreateMitsubaScene()
        {
            // Gather selection then generated obj file
			Transform[] selection = Selection.GetTransforms(SelectionMode.Editable | SelectionMode.ExcludePrefab);

			// If nothing was selected, simply get everything!
			if (selection.Length == 0)
				selection = GameObject.FindObjectsOfType<Transform>();

            return CreateMitsubaScene(selection);
        }

		static string CreateMitsubaScene(Transform[] selection)
		{
			MitsubaTools.CreateTargetFolder();

			List<MeshFilter> mfList = new List<MeshFilter>();
			List<Light> lightList = new List<Light>();

			for(int i = 0; i < selection.Length; i++)
			{
				MeshFilter meshfilter = selection[i].GetComponent<MeshFilter>();
				if(meshfilter != null)
					mfList.Add(meshfilter);

				Light light = selection[i].GetComponent<Light>();
				if(light != null)
					lightList.Add(light);
			}

			if(mfList.Count == 0)
				throw new System.Exception("Nothing to export");

			MitsubaTools.ExportObjMeshes(MitsubaPreferences.targetFolder, MitsubaTools.GetSceneName() + "_geo", mfList);

			string ret = ExportMitsubaScene(MitsubaPreferences.targetFolder, MitsubaTools.GetSceneName(), mfList, lightList);

			return ret;
		}

        // Syntaxic sugar call
        public static string RenderMitsubaScene()
        {
            string inFile = MitsubaExporter.MitsubaScene.CreateMitsubaScene();
            return RenderMitsubaScene(inFile);
        }

        public static string RenderMitsubaScene(string inFile)
		{
			string outFile = MitsubaPreferences.targetFolder +
							 Path.DirectorySeparatorChar +
							 MitsubaTools.GetSceneName() + "_mitsuba." +
							 (MitsubaPreferences.exportEXR ? "exr" : "png");

			string cmd = MitsubaPreferences.mitsubaExecutable;

			inFile = Path.Combine(Directory.GetCurrentDirectory(), inFile);
			outFile = Path.Combine(Directory.GetCurrentDirectory(), outFile);

			inFile = "\"" + inFile + "\"";
			string outFileEsc = "\"" + outFile + "\"";

            // Windows specific
            #if UNITY_EDITOR_WIN
            	inFile = inFile.Replace("/", "\\");
				outFile = outFile.Replace("/", "\\");

				cmd = "\"" + cmd + "\"";
			#endif

			EditorUtility.DisplayProgressBar(MitsubaTools.appName, "Rendering with Mitsuba...", 0.0f);

			System.Diagnostics.Process process = new System.Diagnostics.Process();

			process.StartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Normal;
			process.StartInfo.FileName = cmd;
			process.StartInfo.Arguments = "-o " + outFileEsc + " " + inFile;
			process.StartInfo.UseShellExecute = false;
			process.StartInfo.RedirectStandardOutput = true;
			process.StartInfo.RedirectStandardError = true;

			MitsubaTools.LogMsg("Running " + process.StartInfo.FileName + " " + process.StartInfo.Arguments);

			process.Start();

			process.BeginOutputReadLine();
			string output = process.StandardError.ReadToEnd();
			process.WaitForExit();

			MitsubaTools.LogMsg(output);

			return outFile;
		}

        // Call CreateMitsubaScene and RenderMitsubaScene before calling GetMitsubaScene
        public static Texture2D GetMitsubaScene(string fileIn)
        {
           string fileOut = "Assets" +
                             Path.DirectorySeparatorChar + MitsubaTools.GetSceneName() +
                             "_mitsuba." + (MitsubaPreferences.exportEXR ? "exr" : "png");

            File.Copy(fileIn, fileOut, true);
            AssetDatabase.Refresh();
            return (Texture2D)AssetDatabase.LoadAssetAtPath(fileOut, typeof(Texture2D));
        }
	}
}
