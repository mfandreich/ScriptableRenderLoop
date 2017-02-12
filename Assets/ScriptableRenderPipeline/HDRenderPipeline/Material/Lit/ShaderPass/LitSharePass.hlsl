#ifndef SHADERPASS
#error Undefine_SHADERPASS
#endif

// This first set of define allow to say which attributes will be use by the mesh in the vertex and domain shader (for tesselation)

// Attributes
#define ATTRIBUTES_NEED_NORMAL
#define ATTRIBUTES_NEED_TANGENT // Always present as we require it also in case of anisotropic lighting
#define ATTRIBUTES_NEED_TEXCOORD0
#define ATTRIBUTES_NEED_TEXCOORD1
#define ATTRIBUTES_NEED_COLOR

#if defined(_REQUIRE_UV2) || defined(_REQUIRE_UV3) || defined(DYNAMICLIGHTMAP_ON) || (SHADERPASS == SHADERPASS_DEBUG_VIEW_MATERIAL) 
#define ATTRIBUTES_NEED_TEXCOORD2
#endif
#if defined(_REQUIRE_UV3) || (SHADERPASS == SHADERPASS_DEBUG_VIEW_MATERIAL) 
#define ATTRIBUTES_NEED_TEXCOORD3
#endif

// Varying - Use for pixel shader
// This second set of define allow to say which varyings will be output in the vertex (no more tesselation)
#define VARYINGS_NEED_POSITION_WS
#define VARYINGS_NEED_TANGENT_TO_WORLD
#define VARYINGS_NEED_TEXCOORD0
#define VARYINGS_NEED_TEXCOORD1
#ifdef ATTRIBUTES_NEED_TEXCOORD2
#define VARYINGS_NEED_TEXCOORD2
#endif
#ifdef ATTRIBUTES_NEED_TEXCOORD3
#define VARYINGS_NEED_TEXCOORD3
#endif
#define VARYINGS_NEED_COLOR

#if defined(_DOUBLESIDED_LIGHTING_FLIP) || defined(_DOUBLESIDED_LIGHTING_MIRROR)
#define VARYINGS_NEED_CULLFACE
#endif

// This include will define the various Attributes/Varyings structure
#include "../../ShaderPass/VaryingMesh.hlsl"