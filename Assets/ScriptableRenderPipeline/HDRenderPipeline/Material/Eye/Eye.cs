using UnityEngine.Rendering;

namespace UnityEngine.Experimental.Rendering.HDPipeline
{
	namespace Eye
	{
        //Surface Data
        //----------------------------------------------------------
		[GenerateHLSL(PackingRules.Exact, false, true, 5000)]
        public struct SurfaceData
        {
            [SurfaceDataAttributes("Color", false, true)]
            public Vector3 color;
        }

        //BSDFData
        //----------------------------------------------------------
        [GenerateHLSL(PackingRules.Exact, false, true, 5030)]
        public struct BSDFData
        {
            [SurfaceDataAttributes("", false, true)]
            public Vector3 color;
        };
	}
}