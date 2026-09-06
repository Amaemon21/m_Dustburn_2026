#ifndef REPETITIONLESSHDRPSAMPLETERRAINNEW_INCLUDED
#define REPETITIONLESSHDRPSAMPLETERRAINNEW_INCLUDED

// Fixing a TerrainLit error in vulkan when decals are enabled
// This is a temporary workaround until Unity fixes it
#if defined(SHADER_API_VULKAN) && defined(HAVE_DECALS)
#undef HAVE_DECALS
#endif

#ifndef _TERRAIN_8_LAYERS
TEXTURE2D(_Control1);
#endif

#include "RepetitionlessHDRPSampleTerrain.hlsl"

#endif