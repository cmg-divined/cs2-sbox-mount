//=========================================================================================================================
// CS2's csgo_unlitgeneric.vfx: light emitters, glows, additive light cards, screens, hidden arm parts.
//
// From the game's compiled shader (decompiled with Source 2 Viewer: every F_BLEND_MODE, two texture and vertex colour
// variants). The colour times the instance tint (linearized, faded in by the model tint amount, times the colour tint
// and, with F_VERTEX_COLOR, the vertex colour), times the second texture with F_TWOTEXTURE (whose own transform applies
// while the first's then doesn't). By blend mode: 0 opaque, 2 alpha tested at the reference, both fogged by colour; 1
// translucent and 4 additive faded by the gradient and cubemap fog; 3 mod2x as lerp( 0.5, colour, alpha ), fogged by
// colour; 5 multiply as lerp( 1, colour, alpha ) and 6 as lerp( 0, colour, alpha ), faded by the cubemap fog.
//
// The blend states per mode are the same Feature as csgo_static_overlay's, ported there (the union of render states the
// .vfx lists doesn't say which mode has which; unverified for this shader). Not ported: F_GRADIENTMODULATION,
// F_TEXTURE_ANIMATION (1 material), F_DISABLE_Z_BUFFERING (17, hidden arm parts and overlays), scaling coordinates by the
// model's scale, dynamic expressions (animated offsets on 95 materials, e.g. scrolling screens).
//=========================================================================================================================

HEADER
{
	Description = "CS2 csgo_unlitgeneric";
}

FEATURES
{
	#include "common/features.hlsl"
	Feature( F_BLEND_MODE, 0..6 (0="Opaque", 1="Translucent", 2="Alpha Test", 3="Mod2x", 4="Additive", 5="Multiply", 6="ModThenAdd"), "Blend Mode" );
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
	#define S_UV2 1
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
	float4 vUv : TEXCOORD5;			// xy: the colour's coordinates; zw: the second texture's
	float4 vTint : TEXCOORD10;		// the tint, linear; alpha in w
};

VS
{
	#include "cs2_vertex.hlsl"

	float4 g_vTexCoordXform0 < Default4( 1, 0, 0, 0 ); >;
	float4 g_vTexCoordXform1 < Default4( 0, 1, 0, 0 ); >;
	float2 g_vTexCoordScrollSpeed < Default2( 0, 0 ); >;
	float4 g_vTex2CoordXform0 < Default4( 1, 0, 0, 0 ); >;
	float4 g_vTex2CoordXform1 < Default4( 0, 1, 0, 0 ); >;
	float g_flTwoTexture < Default( 0 ); >;
	float g_flVertexColor < Default( 0 ); >;

	float3 g_vColorTint < Default3( 1, 1, 1 ); >;
	float g_flModelTintAmount < Default( 1 ); >;

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessCS2Vertex( v );

		float2 uv = v.vTexCoord.xy;
		float2 colorUv = g_flTwoTexture > 0.5 ? uv : float2( dot( uv, g_vTexCoordXform0.xy ) + g_vTexCoordXform0.w, dot( uv, g_vTexCoordXform1.xy ) + g_vTexCoordXform1.w );
		float2 secondUv = float2( dot( uv, g_vTex2CoordXform0.xy ) + g_vTex2CoordXform0.w, dot( uv, g_vTex2CoordXform1.xy ) + g_vTex2CoordXform1.w );
		i.vUv = float4( colorUv + g_vTexCoordScrollSpeed * g_flTime, secondUv );

		float4 instance = i.vVertexColor * v.vMeshTint;
		float4 tint = float4( lerp( 1.0, SrgbGammaToLinear( instance.rgb ), g_flModelTintAmount ) * g_vColorTint, instance.a );
		if ( g_flVertexColor > 0.5 )
			tint *= v.vMeshColor;

		i.vTint = tint;
		return FinalizeCS2Vertex( i );
	}
}

PS
{
	StaticCombo( S_BLEND_MODE, F_BLEND_MODE, Sys( ALL ) );

	#if S_BLEND_MODE != 0 && S_BLEND_MODE != 2
		// The blend states are the game's per blend mode; sbox_pixel's translucent ones don't cover them.
		#define BLEND_MODE_ALREADY_SET 1
		#define DEPTH_STATE_ALREADY_SET 1
	#endif
	#if S_BLEND_MODE == 2
		#define S_ALPHA_TEST 1
	#endif

	// sbox_pixel supplies culling; suppress the duplicate declaration in vr_common_ps_code.
	#define CULL_MODE_ALREADY_SET 1

	#include "common/pixel.hlsl"
	#include "common/classes/Light.hlsl"
	#include "cs2_lighting.hlsl"

	#if S_BLEND_MODE != 0 && S_BLEND_MODE != 2
		RenderState( DepthEnable, true );
		RenderState( DepthFunc, GREATER_EQUAL );
		RenderState( DepthWriteEnable, false );
		RenderState( BlendEnable, true );

		#if S_BLEND_MODE == 3 || S_BLEND_MODE == 6
			RenderState( SrcBlend, DEST_COLOR );
			RenderState( DstBlend, SRC_COLOR );
		#elif S_BLEND_MODE == 4
			RenderState( SrcBlend, SRC_ALPHA );
			RenderState( DstBlend, ONE );
		#elif S_BLEND_MODE == 5
			RenderState( SrcBlend, DEST_COLOR );
			RenderState( DstBlend, ZERO );
		#else
			RenderState( SrcBlend, SRC_ALPHA );
			RenderState( DstBlend, INV_SRC_ALPHA );
		#endif
	#endif

	CreateInputTexture2D( CS2Color, Srgb, 8, "None", "_color", "Color", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( CS2Color2, Srgb, 8, "None", "_color", "Color", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tColor < Channel( RGBA, Box( CS2Color ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tColor2 < Channel( RGBA, Box( CS2Color2 ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;

	TextureAttribute( RepresentativeTexture, g_tColor );

	float g_flTwoTexturePs < Default( 0 ); >;
	float g_flFogEnabled < Default( 1 ); >;

	#if S_BLEND_MODE != 2
		float g_flAlphaTestReference < Default( 0.5 ); >;
	#endif

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float4 color = g_tColor.Sample( g_sAniso, i.vUv.xy );
		if ( g_flTwoTexturePs > 0.5 )
			color *= g_tColor2.Sample( g_sAniso, i.vUv.zw );

		color *= i.vTint;

		#if S_BLEND_MODE == 2
			if ( color.a - g_flAlphaTestReference < 0.0 )
				discard;
		#endif

		float3 rgb = color.rgb;
		float alpha = color.a;

		#if S_BLEND_MODE == 3
			rgb = lerp( 0.5, rgb, alpha );
		#elif S_BLEND_MODE == 5
			rgb = lerp( 1.0, rgb, alpha );
		#elif S_BLEND_MODE == 6
			rgb = lerp( 0.0, rgb, alpha );
		#endif

		#if S_MODE_DEPTH
			Material m = Material::Init( i.vPositionWithOffsetWs, i.vPositionSs );
			m.Albedo = rgb;
			m.Normal = normalize( i.vNormalWs.xyz );
			m.Opacity = alpha;
			return ShadingModelStandard::Shade( m );
		#else
			[branch]
			if ( g_flFogEnabled > 0.5 && CS2LightingEnabled() )
			{
				float3 position = CS2ToMap( i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz );

				#if S_BLEND_MODE == 1 || S_BLEND_MODE == 4
					alpha *= CS2FogTransmission( position );
				#elif S_BLEND_MODE == 5 || S_BLEND_MODE == 6
					float gradient, cubemap;
					float3 cubemapColor;
					CS2FogAt( position, false, gradient, cubemap, cubemapColor );
					alpha *= 1.0 - cubemap;
				#else
					rgb = CS2Fog( rgb, position );
				#endif
			}

			return float4( rgb, alpha );
		#endif
	}
}
