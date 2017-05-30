// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

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

Shader "PostFX"
{
	Properties
	{
		_ColormapTex ("Colormap", 2D) = "white" {}
		_MainTex ("RenderUnity", 2D) = "white" {}
		_RenderMitsubaTex ("RenderMistuba", 2D) = "white" {}
		_ColormapScale ("ColormapScale", Range (0.01, 1.00)) = 0.1
		_MistubaScreenOccupancy ("MistubaScreenOccupancy", Range (0.00, 1.00)) = 0.5
		_ColormapScreenOccupancy ("ColormapScreenOccupancy", Range (0.00, 1.00)) = 1.0
		_Exposure ("Exposure", Range (-16.00, 16.00)) = 1.0
		_Gamma ("Gamma", Range (1, 16.00)) = 2.2
	}

	CGINCLUDE

		#include "UnityCG.cginc"

		struct Attributes
		{
			float4 vertex : POSITION;
			float2 uv : TEXCOORD0;
		};

		struct Varyings
		{
			float4 pos : SV_POSITION;
			float2 uv : TEXCOORD0;
		};

		Varyings vert(Attributes v)
		{
			Varyings o;
			o.pos = UnityObjectToClipPos(v.vertex);
			o.uv = v.uv;
			return o;
		}

		// ====================================================================
		// Uniforms
		sampler2D _ColormapTex;
		sampler2D _RenderMitsubaTex;
		sampler2D _MainTex;
		float _ColormapScale;
		float _MitsubaScreenOccupancy;
		float _ColormapScreenOccupancy;
		float _Exposure;
		float _Gamma;

		// ====================================================================
		// Utils

		// Tonemapper
		float3 tonemap(float3 hdr)
		{
			return hdr * exp2(_Exposure);
		}

		// Gamma corrector
		float3 gamma_correct(float3 ldr)
		{
			return pow(ldr, 1.0 / _Gamma);
		}

		// see http://www.brucelindbloom.com/index.html?Eqn_XYZ_to_Lab.html
		float cielab_spline(float x_r)
		{
			const float kappa = 903.3;
			const float epsilon = 0.008856;
			if (x_r > epsilon) {
				return pow(x_r, 1.f/3.f);
			} else {
				return (kappa * x_r + 16.f) / (116.f);
			}
		}

		// see http://www.brucelindbloom.com/index.html?Eqn_XYZ_to_Lab.html
		// and https://en.wikipedia.org/wiki/Lab_color_space
		float3 ciexyz_to_cielab(float3 xyz)
		{
			const float Xr = 95.047f;
			const float Yr = 100.f;
			const float Zr = 108.883f;
			float3 xyz_r = float3(xyz.x / Xr, xyz.y / Yr, xyz.z / Zr);
			float fx = cielab_spline(xyz_r.x);
			float fy = cielab_spline(xyz_r.y);
			float fz = cielab_spline(xyz_r.z);
			float L = 116.f * fx - 16.f;
			float a = 500.f * (fx - fy);
			float b = 200.f * (fy - fz);
			return float3(L, a, b);
		}

		// see http://www.brucelindbloom.com/index.html?Eqn_RGB_to_XYZ.html
		// and http://www.brucelindbloom.com/index.html?Eqn_RGB_XYZ_Matrix.html
		// note: I use the Adobe RGB (1998) matrix
		float3 rgb_to_ciexyz(float3 rgb)
		{
			float x = dot(half3(0.5767309, 0.1855540, 0.1881852), rgb);
			float y = dot(half3(0.2973769, 0.6273491, 0.0752741), rgb);
			float z = dot(half3(0.0270343, 0.0706872, 0.9911085), rgb);
			return float3(x, y, z);
		}

		// Linear RGB -> CIE LAB
		float3 rgb_to_cielab(float3 rgb)
		{
			return ciexyz_to_cielab(rgb_to_ciexyz(rgb));
		}

		// see https://en.wikipedia.org/wiki/Color_difference
		float3 deltae_cie76(float3 rgb1, float3 rgb2)
		{
			float3 cie1 = rgb_to_cielab(rgb1);
			float3 cie2 = rgb_to_cielab(rgb2);
			float3 delta = cie2 - cie1;
			return sqrt(dot(delta, delta));
		}

        float2 flip_uv_y(float2 uv)
        {
            uv.y = 1 - uv.y;
            return uv;
        }

		// this code renders the colormap version
		half3 render_heatmap(Varyings i)
		{
			float3 img1 = gamma_correct(tonemap(tex2D(_MainTex, i.uv).rgb));
			float3 img2 = gamma_correct(tonemap(tex2D(_RenderMitsubaTex, flip_uv_y(i.uv)).rgb));
			float delta = deltae_cie76(img1, img2);
			return tex2D(_ColormapTex, float2(delta * _ColormapScale, 0));
		}

		// this code renders the mitsuba
		half3 render_mitsuba(Varyings i)
		{
			return gamma_correct(tonemap(tex2D(_RenderMitsubaTex, flip_uv_y(i.uv)).rgb));
		}

		// this code renders the mitsuba
		half3 render_unity(Varyings i)
		{
			return gamma_correct(tonemap(tex2D(_MainTex, i.uv).rgb));
		}

		// render the color bar
		half3 hud_colorbar(half3 color, Varyings i)
		{
			// draw the colorbar so that it occupies a fixed pixel size
			if (i.uv.x >= 0.95 && i.uv.x < 0.96 && i.uv.y >= 0.1 && i.uv.y < 0.9)
				return tex2D(_ColormapTex, float2(i.uv.y, 0)).rgb;

			return color;
		}

		// ====================================================================
		// main render
		half4 frag(Varyings i) : SV_Target
		{
			half3 color = half3(1, 0, 0);
#if !UNITY_UV_STARTS_AT_TOP
			i.uv.y = 1 - i.uv.y;
#endif
			// compute background color
			if (i.uv.x < _MitsubaScreenOccupancy && i.uv.y >= _ColormapScreenOccupancy) {
				color = render_unity(i);
			} else if (i.uv.x >= _MitsubaScreenOccupancy && i.uv.y >= _ColormapScreenOccupancy) {
				color = render_mitsuba(i);
			} else {
				color = render_heatmap(i);
			}
			// compute HUD
			color = hud_colorbar(color, i);

			return half4(color, 1);
		}

	ENDCG

	SubShader
	{
		Cull Off ZWrite Off ZTest Always

		Pass
		{
			CGPROGRAM

				#pragma vertex vert
				#pragma fragment frag

			ENDCG
		}
	}
}
