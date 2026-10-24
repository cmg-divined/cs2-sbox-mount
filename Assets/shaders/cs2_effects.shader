//=========================================================================================================================
// CS2's csgo_effects.vfx: unlit, translucent effect meshes (steam plumes, dust and haze cards, light shafts).
//
// From the game's compiled shader (decompiled with Source 2 Viewer, the S_DEPTH_FEATHER and S_ADDITIVE_BLEND variants):
// the colour texture times the instance and vertex tint and the colour boost; its alpha shaped by three scrolling masks,
// a fresnel term, a soft fade where the mesh meets the scene behind it, and a fade by distance from the camera. Blended
// over the scene, or added to it with F_ADDITIVE_BLEND; fog tints it, or fades it when it's additive.
//
// The texture coordinate transform the game's shader has is fixed at identity in this version, so it isn't read.
// F_TINT_MASK isn't ported (unverified how it gates the tint).
//=========================================================================================================================

HEADER
{
	Description = "CS2 csgo_effects";
}

FEATURES
{
	// Declares F_ADDITIVE_BLEND and F_RENDER_BACKFACES.
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
	float4 vTint : TEXCOORD10;	// the instance and vertex tint, linear, with the colour tint applied; alpha in w
};

VS
{
	#include "cs2_vertex.hlsl"

	float2 g_vTexCoordScrollSpeed < Default2( 0, 0 ); >;

	// The colour tint, linear.
	float3 g_vColorTint < Default3( 1, 1, 1 ); >;

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessCS2Vertex( v );

		i.vTextureCoords.xy = v.vTexCoord.xy + g_vTexCoordScrollSpeed * g_flTime;

		// The instance colour is gamma space; the game linearizes it, then applies the colour tint, then the mesh's
		// painted vertex colour unless it's all zero.
		float4 instance = i.vVertexColor * v.vMeshTint;
		float4 tint = float4( SrgbGammaToLinear( instance.rgb ) * g_vColorTint, instance.a );

		if ( any( v.vMeshColor != 0.0 ) )
			tint *= v.vMeshColor;

		i.vTint = tint;
		return FinalizeCS2Vertex( i );
	}
}

PS
{
	StaticCombo( S_ADDITIVE_BLEND, F_ADDITIVE_BLEND, Sys( ALL ) );

	// sbox_pixel supplies culling; suppress the duplicate declaration in vr_common_ps_code.
	#define CULL_MODE_ALREADY_SET
	#include "common/pixel.hlsl"
	#include "cs2_lighting.hlsl"

	CreateInputTexture2D( CS2Color, Srgb, 8, "None", "_color", "Color", Default4( 1, 1, 1, 0 ) );
	CreateInputTexture2D( CS2Mask1, Linear, 8, "None", "_mask", "Mask 1", Default4( 1, 1, 1, 0 ) );
	CreateInputTexture2D( CS2Mask2, Linear, 8, "None", "_mask", "Mask 2", Default4( 1, 1, 1, 0 ) );
	CreateInputTexture2D( CS2Mask3, Linear, 8, "None", "_mask", "Mask 3", Default4( 1, 1, 1, 0 ) );

	Texture2D g_tColor < Channel( RGBA, Box( CS2Color ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tMask1 < Channel( RGBA, Box( CS2Mask1 ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tMask2 < Channel( RGBA, Box( CS2Mask2 ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tMask3 < Channel( RGBA, Box( CS2Mask3 ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;

	TextureAttribute( RepresentativeTexture, g_tColor );

	float g_flColorBoost < Default( 1 ); >;

	float2 g_vMask1Scale < Default2( 1, 1 ); >;
	float2 g_vMask1PanSpeed < Default2( 0, 0 ); >;
	float2 g_vMask2Scale < Default2( 1, 1 ); >;
	float2 g_vMask2PanSpeed < Default2( 0, 0 ); >;
	float2 g_vMask3Scale < Default2( 1, 1 ); >;
	float2 g_vMask3PanSpeed < Default2( 0, 0 ); >;

	float g_flFresnelExponent < Default( 0.001 ); >;
	float g_flFresnelFalloff < Default( 1 ); >;
	float g_flFresnelMin < Default( 0 ); >;
	float g_flFresnelMax < Default( 1 ); >;

	// F_DEPTH_FEATHER: the mesh fades out within this distance of the scene behind it.
	float g_flDepthFeather < Default( 0 ); >;
	float g_flFeatherDistance < Default( 1 ); >;
	float g_flFeatherFalloff < Default( 1 ); >;

	float g_flFadeDistance < Default( 1 ); >;
	float g_flFadeFalloff < Default( 1 ); >;
	float g_flFadeMin < Default( 0 ); >;
	float g_flFadeMax < Default( 1 ); >;

	float g_flFogEnabled < Default( 1 ); >;

	// F_RENDER_BACKFACES without F_DONT_FLIP_BACKFACE_NORMALS: back faces fresnel with the normal flipped.
	float g_flFlipBackfaceNormals < Default( 0 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		#if S_RENDER_BACKFACES
			bool isFrontFace = i.face;
		#else
			bool isFrontFace = true;
		#endif

		float3 normal = normalize( i.vNormalWs.xyz ) * ( g_flFlipBackfaceNormals > 0.5 && !isFrontFace ? -1.0 : 1.0 );
		float3 positionWs = i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz;
		float3 toPixel = positionWs - g_vCameraPositionWs;
		float2 uv = i.vTextureCoords.xy;

		float4 color = g_tColor.Sample( g_sAniso, uv );

		float mask = g_tMask1.Sample( g_sAniso, uv * g_vMask1Scale + g_vMask1PanSpeed * g_flTime ).r
			* g_tMask2.Sample( g_sAniso, uv * g_vMask2Scale + g_vMask2PanSpeed * g_flTime ).r
			* g_tMask3.Sample( g_sAniso, uv * g_vMask3Scale + g_vMask3PanSpeed * g_flTime ).r;

		float fresnel = lerp( g_flFresnelMin, g_flFresnelMax, saturate( pow( saturate( dot( -normalize( toPixel ), normal ) ), g_flFresnelExponent ) * g_flFresnelFalloff ) );

		// Soft where it meets the scene: by its distance from the scene point behind it.
		float feather = 1.0;
		if ( g_flDepthFeather > 0.5 )
			feather = pow( saturate( distance( positionWs, Depth::GetWorldPosition( i.vPositionSs.xy ) ) / g_flFeatherDistance ), g_flFeatherFalloff );

		float fade = pow( lerp( g_flFadeMin, g_flFadeMax, saturate( length( toPixel / g_flFadeDistance ) ) ), g_flFadeFalloff );

		float alpha = i.vTint.a * color.a * g_flOpacityScale * mask * fresnel * feather * fade;
		float3 rgb = color.rgb * i.vTint.rgb * g_flColorBoost;

		[branch]
		if ( g_flFogEnabled > 0.5 && CS2LightingEnabled() )
		{
			float3 position = CS2ToMap( positionWs );

			#if S_ADDITIVE_BLEND
				alpha *= CS2FogTransmission( position );
			#else
				rgb = CS2Fog( rgb, position );
			#endif
		}

		return float4( rgb, alpha );
	}
}
