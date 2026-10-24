//=========================================================================================================================
// CS2's csgo_decalmodulate.vfx: darkening and lightening overlays laid on the world (cracks, carved glyphs, grime).
//
// From the game's compiled shader (decompiled with Source 2 Viewer: its F_OVERLAY variant, which every material of it
// sets). The colour, read without sRGB decoding, times the vertex colour with F_VERTEX_COLOR (else white: the shader
// takes neither the instance tint nor its colour tint); alpha tested at the reference (0 on most); its alpha faded by the
// gradient and cubemap fog; then lerp( 0.5, colour, alpha ), neutral grey where it fades out.
//
// The .vfx sets no blend state for it (the game's decal layer does); its 0.5 neutral output is a mod2x blend, the
// destination times twice the colour, as csgo_static_overlay's mod2x mode draws (inferred, unverified). The game's depth
// bias of -64 is the view ray pull csgo_static_overlay's port uses.
//=========================================================================================================================

HEADER
{
	Description = "CS2 csgo_decalmodulate";
}

FEATURES
{
	#include "common/features.hlsl"
}

MODES
{
	Forward();
	ToolsShadingComplexity( "tools_shading_complexity.shader" );
}

COMMON
{
	#define CUSTOM_MATERIAL_INPUTS
	#define S_TRANSLUCENT 1
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
	float4 vTint : TEXCOORD10;		// the vertex colour, or white
};

VS
{
	#include "cs2_vertex.hlsl"

	// How far, as a fraction of the distance to the camera, overlays are pulled towards it (the game's depth bias).
	#define OVERLAY_DEPTH_OFFSET 0.0005

	float4 g_vTexCoordXform0 < Default4( 1, 0, 0, 0 ); >;
	float4 g_vTexCoordXform1 < Default4( 0, 1, 0, 0 ); >;
	float g_flVertexColor < Default( 0 ); >;

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessCS2Vertex( v );

		float3 toCamera = g_vCameraPositionWs - i.vPositionWs.xyz;
		i.vPositionWs.xyz += toCamera * OVERLAY_DEPTH_OFFSET;
		i.vPositionPs = Position3WsToPs( i.vPositionWs.xyz );

		float2 uv = v.vTexCoord.xy;
		i.vTextureCoords.xy = float2( dot( uv, g_vTexCoordXform0.xy ) + g_vTexCoordXform0.w, dot( uv, g_vTexCoordXform1.xy ) + g_vTexCoordXform1.w );
		i.vTint = g_flVertexColor > 0.5 ? v.vMeshColor : 1.0;
		return FinalizeCS2Vertex( i );
	}
}

PS
{
	#define BLEND_MODE_ALREADY_SET 1
	#define DEPTH_STATE_ALREADY_SET 1
	#define CULL_MODE_ALREADY_SET 1

	#include "common/pixel.hlsl"
	#include "common/classes/Light.hlsl"
	#include "cs2_lighting.hlsl"

	RenderState( DepthEnable, true );
	RenderState( DepthFunc, GREATER_EQUAL );
	RenderState( DepthWriteEnable, false );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, DEST_COLOR );
	RenderState( DstBlend, SRC_COLOR );

	CreateInputTexture2D( CS2Color, Linear, 8, "None", "_color", "Color", Default4( 0.5, 0.5, 0.5, 0 ) );
	Texture2D g_tColor < Channel( RGBA, Box( CS2Color ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;

	TextureAttribute( RepresentativeTexture, g_tColor );

	float g_flAlphaTestReference < Default( 0.5 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float4 color = g_tColor.Sample( g_sAniso, i.vTextureCoords.xy ) * i.vTint;
		if ( color.a - g_flAlphaTestReference < 0.0 )
			discard;

		float alpha = color.a;

		[branch]
		if ( CS2LightingEnabled() )
			alpha *= CS2FogTransmission( CS2ToMap( i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz ) );

		return float4( lerp( 0.5, color.rgb, alpha ), alpha );
	}
}
