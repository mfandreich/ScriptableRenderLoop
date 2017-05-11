// This file include API abstraction based on platform, validate that various engine defines are correctly setup and also defines other system defines

// Include language header
#if defined(SHADER_API_D3D11)
#include "D3D11.hlsl"
#elif defined(SHADER_API_PSSL)
#include "PSSL.hlsl"
#elif defined(SHADER_API_XBOXONE)
#include "D3D11.hlsl"
#include "D3D11_1.hlsl"
#elif defined(SHADER_API_METAL)
#include "Metal.hlsl"
#else
#error unsupported shader api
#endif

// Wait for a fix from Trunk #error not supported yet
/*
#define REQUIRE_DEFINED(X_) \
    #ifndef X_  \
        #error X_ must be defined (in) the platform include \
    #endif X_  \

REQUIRE_DEFINED(UNITY_UV_STARTS_AT_TOP)
REQUIRE_DEFINED(UNITY_REVERSED_Z)
REQUIRE_DEFINED(UNITY_NEAR_CLIP_VALUE)
REQUIRE_DEFINED(FACE)

REQUIRE_DEFINED(CBUFFER_START)
REQUIRE_DEFINED(CBUFFER_END)

REQUIRE_DEFINED(INITIALIZE_OUTPUT)

*/

// These define are use to abstract the way we sample into a cubemap array.
// Some platform don't support cubemap array so we fallback on 2D latlong
#ifdef UNITY_NO_CUBEMAP_ARRAY
#define TEXTURECUBE_ARRAY_ABSTRACT TEXTURE2D_ARRAY
#define SAMPLERCUBE_ABSTRACT SAMPLER2D
#define TEXTURECUBE_ARRAY_ARGS_ABSTRACT TEXTURE2D_ARRAY_ARGS
#define TEXTURECUBE_ARRAY_PARAM_ABSTRACT TEXTURE2D_ARRAY_PARAM
#define SAMPLE_TEXTURECUBE_ARRAY_LOD_ABSTRACT(textureName, samplerName, coord3, index, lod) SAMPLE_TEXTURE2D_ARRAY_LOD(textureName, samplerName, DirectionToLatLongCoordinate(coord3), index, lod)
#else
#define TEXTURECUBE_ARRAY_ABSTRACT TEXTURECUBE_ARRAY
#define SAMPLERCUBE_ABSTRACT SAMPLERCUBE
#define TEXTURECUBE_ARRAY_ARGS_ABSTRACT TEXTURECUBE_ARRAY_ARGS
#define TEXTURECUBE_ARRAY_PARAM_ABSTRACT TEXTURECUBE_ARRAY_PARAM
#define SAMPLE_TEXTURECUBE_ARRAY_LOD_ABSTRACT(textureName, samplerName, coord3, index, lod) SAMPLE_TEXTURECUBE_ARRAY_LOD(textureName, samplerName, coord3, index, lod)
#endif

// ----------------------------------------------------------------------------
// Instancing API
// ----------------------------------------------------------------------------

// instancing paths
// - UNITY_INSTANCING_ENABLED               Defined if instancing path is taken.
// - UNITY_PROCEDURAL_INSTANCING_ENABLED    Defined if procedural instancing path is taken.
#if defined(UNITY_SUPPORT_INSTANCING) && defined(INSTANCING_ON)
#define UNITY_INSTANCING_ENABLED
#endif
#if defined(UNITY_SUPPORT_INSTANCING) && defined(PROCEDURAL_INSTANCING_ON)
#define UNITY_PROCEDURAL_INSTANCING_ENABLED
#endif

// basic instancing setups
// - UNITY_VERTEX_INPUT_INSTANCE_ID     Declare instance ID field in vertex shader input / output struct.
// - UNITY_GET_INSTANCE_ID  (Internal) Get the instance ID from input struct.
#if defined(UNITY_INSTANCING_ENABLED) || defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)

    // A global instance ID variable that functions can directly access.
    static uint unity_InstanceID;

    CBUFFER_START(UnityDrawCallInfo)
        int unity_BaseInstanceID;   // Where the current batch starts within the instanced arrays.
        int unity_InstanceCount;    // Number of instances before doubling for stereo.
    CBUFFER_END

#else
    #define DEFAULT_UNITY_VERTEX_INPUT_INSTANCE_ID
#endif // UNITY_INSTANCING_ENABLED || UNITY_PROCEDURAL_INSTANCING_ENABLED || UNITY_STEREO_INSTANCING_ENABLED

#if !defined(UNITY_VERTEX_INPUT_INSTANCE_ID)
#   define UNITY_VERTEX_INPUT_INSTANCE_ID DEFAULT_UNITY_VERTEX_INPUT_INSTANCE_ID
#endif

// - UNITY_SETUP_INSTANCE_ID        Should be used at the very beginning of the vertex shader / fragment shader,
//                                  so that succeeding code can have access to the global unity_InstanceID.
//                                  Also procedural function is called to setup instance data.
// - UNITY_TRANSFER_INSTANCE_ID     Copy instance ID from input struct to output struct. Used in vertex shader.

#if defined(UNITY_INSTANCING_ENABLED) || defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
    void UnitySetupInstanceID(uint inputInstanceID)
    {
        unity_InstanceID = inputInstanceID + unity_BaseInstanceID;
    }

    #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
        #ifndef UNITY_INSTANCING_PROCEDURAL_FUNC
            #error "UNITY_INSTANCING_PROCEDURAL_FUNC must be defined."
        #else
            #define DEFAULT_UNITY_SETUP_INSTANCE_ID(input)      { UnitySetupInstanceID(UNITY_GET_INSTANCE_ID(input)); UNITY_INSTANCING_PROCEDURAL_FUNC(); }
    #endif
    #else
        #define DEFAULT_UNITY_SETUP_INSTANCE_ID(input)          UnitySetupInstanceID(UNITY_GET_INSTANCE_ID(input));
    #endif
    #define UNITY_TRANSFER_INSTANCE_ID(input, output)   output.instanceID = UNITY_GET_INSTANCE_ID(input)
#else
    #define DEFAULT_UNITY_SETUP_INSTANCE_ID(input)
    #define UNITY_TRANSFER_INSTANCE_ID(input, output)
#endif

#if !defined(UNITY_SETUP_INSTANCE_ID)
#   define UNITY_SETUP_INSTANCE_ID(input) DEFAULT_UNITY_SETUP_INSTANCE_ID(input)
#endif

////////////////////////////////////////////////////////
// instanced property arrays
#if defined(UNITY_INSTANCING_ENABLED)

    // The maximum number of instances a single instanced draw call can draw.
    // You can define your custom value before including this file.
    #ifndef UNITY_MAX_INSTANCE_COUNT
        #define UNITY_MAX_INSTANCE_COUNT 500
    #endif
    #if (defined(SHADER_API_GLES3) || defined(SHADER_API_GLCORE) || defined(SHADER_API_METAL)) && !defined(UNITY_MAX_INSTANCE_COUNT_GL_SAME)
        // Many devices have max UBO size of 16kb
        #define UNITY_INSTANCED_ARRAY_SIZE (UNITY_MAX_INSTANCE_COUNT / 4)
    #else
        // On desktop, this assumes max UBO size of 64kb
        #define UNITY_INSTANCED_ARRAY_SIZE UNITY_MAX_INSTANCE_COUNT
    #endif

    // Every per-instance property must be defined in a specially named constant buffer.
    // Use this pair of macros to define such constant buffers.

    #if defined(SHADER_API_GLES3) || defined(SHADER_API_GLCORE) || defined(SHADER_API_METAL) || defined(SHADER_API_VULKAN)
        // GLCore and ES3 have constant buffers disabled normally, but not here.
        #define UNITY_INSTANCING_CBUFFER_START(name)    cbuffer UnityInstancing_##name {
        #define UNITY_INSTANCING_CBUFFER_END            }
    #else
        #define UNITY_INSTANCING_CBUFFER_START(name)    CBUFFER_START(UnityInstancing_##name)
        #define UNITY_INSTANCING_CBUFFER_END            CBUFFER_END
    #endif

    // Define a per-instance shader property. Must be used inside a UNITY_INSTANCING_CBUFFER_START / END block.
    #define UNITY_DEFINE_INSTANCED_PROP(type, name) type name[UNITY_INSTANCED_ARRAY_SIZE];

    // Access a per-instance shader property.
    #define UNITY_ACCESS_INSTANCED_PROP(name)       name[unity_InstanceID]

    // Redefine some of the built-in variables / macros to make them work with instancing.
    UNITY_INSTANCING_CBUFFER_START(PerDraw0)
        float4x4 unity_ObjectToWorldArray[UNITY_INSTANCED_ARRAY_SIZE];
        float4x4 unity_WorldToObjectArray[UNITY_INSTANCED_ARRAY_SIZE];
    UNITY_INSTANCING_CBUFFER_END

    #define unity_ObjectToWorld     unity_ObjectToWorldArray[unity_InstanceID]
    #define unity_WorldToObject     unity_WorldToObjectArray[unity_InstanceID]

    inline float4 UnityObjectToClipPosInstanced(in float3 pos)
    {
        return mul(UNITY_MATRIX_VP, mul(unity_ObjectToWorldArray[unity_InstanceID], float4(pos, 1.0)));
    }
    inline float4 UnityObjectToClipPosInstanced(float4 pos)
    {
        return UnityObjectToClipPosInstanced(pos.xyz);
    }
    #define UnityObjectToClipPos UnityObjectToClipPosInstanced

    #ifdef UNITY_INSTANCED_LOD_FADE
        // the quantized fade value (unity_LODFade.y) is automatically used for cross-fading instances
        UNITY_INSTANCING_CBUFFER_START(PerDraw1)
            float unity_LODFadeArray[UNITY_INSTANCED_ARRAY_SIZE];
        UNITY_INSTANCING_CBUFFER_END
        #define unity_LODFade unity_LODFadeArray[unity_InstanceID].xxxx
    #endif

#else // UNITY_INSTANCING_ENABLED

    #ifdef UNITY_MAX_INSTANCE_COUNT
        #undef UNITY_MAX_INSTANCE_COUNT
    #endif

    // in procedural mode we don't need cbuffer, and properties are not uniforms
    #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
        #define UNITY_INSTANCING_CBUFFER_START(name)
        #define UNITY_INSTANCING_CBUFFER_END
        #define UNITY_DEFINE_INSTANCED_PROP(type, name) static type name;
    #else
        #define UNITY_INSTANCING_CBUFFER_START(name)    CBUFFER_START(name)
        #define UNITY_INSTANCING_CBUFFER_END            CBUFFER_END
        #define UNITY_DEFINE_INSTANCED_PROP(type, name) type name;
    #endif

    #define UNITY_ACCESS_INSTANCED_PROP(name)       name

#endif // UNITY_INSTANCING_ENABLED

// ----------------------------------------------------------------------------
// Common intrinsic (general implementation of intrinsic available on some platform)
// ----------------------------------------------------------------------------

#ifndef INTRINSIC_BITFIELD_EXTRACT
// unsigned integer bit field extract implementation
uint BitFieldExtract(uint data, uint size, uint offset)
{
    return (data >> offset) & ((1u << size) - 1u);
}
#endif // INTRINSIC_BITFIELD_EXTRACT

bool IsBitSet(uint number, uint bitPos)
{
    return ((number >> bitPos) & 1) != 0;
}

#ifndef INTRINSIC_CLAMP
// TODO: should we force all clamp to be intrinsic by default ?
// Some platform have one instruction clamp
#define Clamp clamp
#endif // INTRINSIC_CLAMP

#ifndef INTRINSIC_MUL24
int Mul24(int a, int b)
{
    return a * b;
}

uint Mul24(uint a, uint b)
{
    return a * b;
}
#endif // INTRINSIC_MUL24

#ifndef INTRINSIC_MAD24
int Mad24(int a, int b, int c)
{
    return a * b + c;
}

uint Mad24(uint a, uint b, uint c)
{
    return a * b + c;
}
#endif // INTRINSIC_MAD24

#ifndef INTRINSIC_MED3
float Med3(float a, float b, float c)
{
    return Clamp(a, b, c);
}
#endif // INTRINSIC_MED3

#ifndef INTRINSIC_MINMAX3
float Min3(float a, float b, float c)
{
    return min(min(a, b), c);
}

float2 Min3(float2 a, float2 b, float2 c)
{
    return min(min(a, b), c);
}

float3 Min3(float3 a, float3 b, float3 c)
{
    return min(min(a, b), c);
}

float4 Min3(float4 a, float4 b, float4 c)
{
    return min(min(a, b), c);
}

float Max3(float a, float b, float c)
{
    return max(max(a, b), c);
}

float2 Max3(float2 a, float2 b, float2 c)
{
    return max(max(a, b), c);
}

float3 Max3(float3 a, float3 b, float3 c)
{
    return max(max(a, b), c);
}

float4 Max3(float4 a, float4 b, float4 c)
{
    return max(max(a, b), c);
}
#endif // INTRINSIC_MINMAX3

void Swap(inout float a, inout float b)
{
    float  t = a; a = b; b = t;
}

void Swap(inout float2 a, inout float2 b)
{
    float2 t = a; a = b; b = t;
}

void Swap(inout float3 a, inout float3 b)
{
    float3 t = a; a = b; b = t;
}

void Swap(inout float4 a, inout float4 b)
{
    float4 t = a; a = b; b = t;
}

#define CUBEMAPFACE_POSITIVE_X 0
#define CUBEMAPFACE_NEGATIVE_X 1
#define CUBEMAPFACE_POSITIVE_Y 2
#define CUBEMAPFACE_NEGATIVE_Y 3
#define CUBEMAPFACE_POSITIVE_Z 4
#define CUBEMAPFACE_NEGATIVE_Z 5

#ifndef INTRINSIC_CUBEMAP_FACE_ID
// TODO: implement this. Is the reference implementation of cubemapID provide by AMD the reverse of our ?
/*
float CubemapFaceID(float3 dir)
{
    float faceID;
    if (abs(dir.z) >= abs(dir.x) && abs(dir.z) >= abs(dir.y))
    {
        faceID = (dir.z < 0.0) ? 5.0 : 4.0;
    }
    else if (abs(dir.y) >= abs(dir.x))
    {
        faceID = (dir.y < 0.0) ? 3.0 : 2.0;
    }
    else
    {
        faceID = (dir.x < 0.0) ? 1.0 : 0.0;
    }
    return faceID;
}
*/

void GetCubeFaceID(float3 dir, out int faceIndex)
{
    // TODO: Use faceID intrinsic on console
    float3 adir = abs(dir);

    // +Z -Z
    faceIndex = dir.z > 0.0 ? CUBEMAPFACE_NEGATIVE_Z : CUBEMAPFACE_POSITIVE_Z;

    // +X -X
    if (adir.x > adir.y && adir.x > adir.z)
    {
        faceIndex = dir.x > 0.0 ? CUBEMAPFACE_NEGATIVE_X : CUBEMAPFACE_POSITIVE_X;
    }
    // +Y -Y
    else if (adir.y > adir.x && adir.y > adir.z)
    {
        faceIndex = dir.y > 0.0 ? CUBEMAPFACE_NEGATIVE_Y : CUBEMAPFACE_POSITIVE_Y;
    }
}

#endif // INTRINSIC_CUBEMAP_FACE_ID
