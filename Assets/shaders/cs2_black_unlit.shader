//=========================================================================================================================
// CS2's csgo_black_unlit.vfx: black, unlit geometry (backdrops and the like), only fogged.
//
// From the game's compiled shader (decompiled with Source 2 Viewer): black, through the gradient and cubemap fog when
// the material keeps fog enabled (cs2_lighting.hlsl).
//=========================================================================================================================

HEADER
{
	Description = "CS2 csgo_black_unlit";
}

FEATURES
{
	#include "common/features.hlsl"
}

MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
	ToolsShadingComplexity( "tools_shading_complexity.shader" );
}

COMMON
{
	#define CUSTOM_MATERIAL_INPUTS
	StaticCombo( S_RENDER_BACKFACES, F_RENDER_BACKFACES, Sys( ALL ) );
	#include "common/shared.hlsl"
}

struct VertexInput
{
	#include "cs2_vertexinput.hlsl"
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
};

VS
{
	#include "cs2_vertex.hlsl"

	PixelInput MainVs( VertexInput v )
	{
		return FinalizeCS2Vertex( ProcessCS2Vertex( v ) );
	}
}

PS
{
	// sbox_pixel supplies culling; suppress the duplicate declaration in vr_common_ps_code.
	#define CULL_MODE_ALREADY_SET
	#include "common/pixel.hlsl"
	#include "common/classes/Light.hlsl"
	#include "cs2_lighting.hlsl"

	float g_flFogEnabled < Default( 1 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		#if S_MODE_DEPTH
			Material m = Material::Init( i.vPositionWithOffsetWs, i.vPositionSs );
			m.Albedo = 0;
			m.Normal = normalize( i.vNormalWs.xyz );
			return ShadingModelStandard::Shade( m );
		#else
			float3 rgb = 0;

			[branch]
			if ( g_flFogEnabled > 0.5 && CS2LightingEnabled() )
				rgb = CS2Fog( rgb, CS2ToMap( i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz ) );

			return float4( rgb, 1.0 );
		#endif
	}
}
