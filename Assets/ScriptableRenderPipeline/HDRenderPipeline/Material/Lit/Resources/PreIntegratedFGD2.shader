Shader "Hidden/HDRenderPipeline/PreIntegratedFGD2"
{
   SubShader {
        Pass {
            ZTest Always Cull Off ZWrite Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5
            #pragma only_renderers d3d11 ps4 metal // TEMP: until we go further in dev

            #include "../../../../ShaderLibrary/Common.hlsl"
            #include "../../../../ShaderLibrary/ImageBasedLighting.hlsl"
            #include "../../../ShaderVariables.hlsl"


            struct Attributes
            {
                float3 vertex : POSITION;
                float2 texcoord : TEXCOORD0;
            };

            struct Varyings
            {
                float4 vertex : SV_POSITION;
                float2 texcoord : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.vertex = TransformWorldToHClip(input.vertex);
                output.texcoord = input.texcoord.xy;
                return output;
            }

            /*

            // Ref: Moving Frostbite to PBR (Appendix A)
            float3 IntegrateSpecularGGXIBLRef(  LightLoopContext lightLoopContext,
                                                float3 V, PreLightData preLightData, EnvLightData lightData, BSDFData bsdfData,
                                                uint sampleCount = 4096)
            {
                float3x3 localToWorld = float3x3(bsdfData.tangentWS, bsdfData.bitangentWS, bsdfData.normalWS);
                float    NdotV = max(preLightData.NdotV, MIN_N_DOT_V);
                float3   acc = float3(0.0, 0.0, 0.0);

                // Add some jittering on Hammersley2d
                float2 randNum = InitRandom(V.xy * 0.5 + 0.5);

                for (uint i = 0; i < sampleCount; ++i)
                {
                    float2 u = Hammersley2d(i, sampleCount);
                    u = frac(u + randNum);

                    float VdotH;
                    float NdotL;
                    float3 L;
                    float weightOverPdf;

                    // GGX BRDF
                    ImportanceSampleAnisoGGX(u, V, localToWorld, bsdfData.roughnessT, bsdfData.roughnessB, NdotV, L, VdotH, NdotL, weightOverPdf);

                    if (NdotL > 0.0)
                    {
                        // Fresnel component is apply here as describe in ImportanceSampleGGX function
                        float3 FweightOverPdf = F_Schlick(bsdfData.fresnel0, VdotH) * weightOverPdf;

                        float4 val = SampleEnv(lightLoopContext, lightData.envIndex, L, 0);

                        acc += FweightOverPdf * val.rgb;
                    }
                }


                return acc / sampleCount;
            }
            */

            float4 Frag(Varyings input) : SV_Target
            {
                /*
                // These coordinate sampling must match the decoding in GetPreIntegratedDFG in lit.hlsl, i.e here we use perceptualRoughness, must be the same in shader
                float perceptualRoughness   = input.texcoord.x;
                float anisotropy            = input.texcoord.y;
                float roughnessT, float roughnessB;
                ConvertAnisotropyToRoughness(sqrt(perceptualRoughness), anisotropy, roughnessT, roughnessB);

                IntegrateSpecularGGXIBLRef(LightLoopContext lightLoopContext,
                                                    float3 V, PreLightData preLightData, EnvLightData lightData, BSDFData bsdfData,
                                                    uint sampleCount = 4096)

                float3 V                    = float3(sqrt(1 - NdotV * NdotV), 0, NdotV);
                float3 N                    = float3(0.0, 0.0, 1.0);

                // Pre integrate GGX with smithJoint visibility as well as DisneyDiffuse
                float4 preFGD = IntegrateGGXAndDisneyFGD(V, N, PerceptualRoughnessToRoughness(perceptualRoughness));

                return float4(preFGD.xyz, 1.0);
                */
                return float4(1, 1, 1, 1);
            }

            ENDHLSL
        }
    }
    Fallback Off
}
