//
// This file was automatically generated from Assets/ScriptableRenderPipeline/HDRenderPipeline/Material/Eye/Eye.cs.  Please don't edit by hand.
//

#ifndef EYE_CS_HLSL
#define EYE_CS_HLSL
//
// UnityEngine.Experimental.Rendering.HDPipeline.Eye.SurfaceData:  static fields
//
#define DEBUGVIEW_SURFACEDATA_COLOR (5000)

//
// UnityEngine.Experimental.Rendering.HDPipeline.Eye.BSDFData:  static fields
//
#define DEBUGVIEW_BSDFDATA_COLOR (5030)

// Generated from UnityEngine.Experimental.Rendering.HDPipeline.Eye.SurfaceData
// PackingRules = Exact
struct SurfaceData
{
    float3 color;
};

// Generated from UnityEngine.Experimental.Rendering.HDPipeline.Eye.BSDFData
// PackingRules = Exact
struct BSDFData
{
    float3 color;
};

//
// Debug functions
//
void GetGeneratedSurfaceDataDebug(uint paramId, SurfaceData surfacedata, inout float3 result, inout bool needLinearToSRGB)
{
    switch (paramId)
    {
        case DEBUGVIEW_SURFACEDATA_COLOR:
            result = surfacedata.color;
            needLinearToSRGB = true;
            break;
    }
}

//
// Debug functions
//
void GetGeneratedBSDFDataDebug(uint paramId, BSDFData bsdfdata, inout float3 result, inout bool needLinearToSRGB)
{
    switch (paramId)
    {
        case DEBUGVIEW_BSDFDATA_COLOR:
            result = bsdfdata.color;
            needLinearToSRGB = true;
            break;
    }
}


#endif
