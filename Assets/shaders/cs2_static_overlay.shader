//=========================================================================================================================
// CS2's csgo_static_overlay.vfx: decals, signs, grime and road markings laid on the world as their own geometry, flush
// with the surfaces under them.
//
// From the game's compiled shader (decompiled with Source 2 Viewer: lit translucent, lit alpha test and unlit mod2x
// variants). The colour texture through the material's colour adjust matrix (tint, contrast, saturation, brightness)
// and the instance and vertex tint; lit overlays are lit like csgo_vertexlitgeneric (cs2_lighting.hlsl) with their
// normal, roughness, metalness, ambient occlusion and self illumination; unlit ones are their colour. Blended by the
// material's blend mode: translucent, alpha tested with anti-aliased edges, mod2x (whose colour is read without sRGB
// decoding, and whose fog keeps it neutral), additive, or multiply.
//
// The game keeps overlays in front of the surfaces under them with a depth bias (-64); s&box's depth is reversed, so
// here they are pulled a little towards the camera along the view ray instead, which moves them in depth only.
//
// Not ported (yet): F_MATERIAL_REFERENCE (5 materials take their layer from a csgo_environment material), blend
// effects, the tint mask, texture animation (1 material), ModThenAdd (5 materials, drawn as mod2x; unverified) and
// Multiply (none in the game; drawn as plain multiply, unverified).
//=========================================================================================================================

HEADER
{
	Description = "CS2 csgo_static_overlay";
}

FEATURES
{
	// Declares F_RENDER_BACKFACES.
	#include "common/features.hlsl"
	Feature( F_BLEND_MODE, 0..6 (0="Opaque", 1="Translucent", 2="Alpha Test", 3="Mod2x", 4="Additive", 5="Multiply", 6="ModThenAdd"), "Blend Mode" );
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

	// How far, as a fraction of the distance to the camera, overlays are pulled towards it (the game's depth bias).
	#define OVERLAY_DEPTH_OFFSET 0.0005

	float4 g_vTexCoordXform0 < Default4( 1, 0, 0, 0 ); >;
	float4 g_vTexCoordXform1 < Default4( 0, 1, 0, 0 ); >;
	float2 g_vTexCoordScrollSpeed < Default2( 0, 0 ); >;

	float3 g_vColorTint < Default3( 1, 1, 1 ); >;
	float g_flModelTintAmount < Default( 1 ); >;

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessCS2Vertex( v );

		float3 toCamera = g_vCameraPositionWs - i.vPositionWs.xyz;
		i.vPositionWs.xyz += toCamera * OVERLAY_DEPTH_OFFSET;
		i.vPositionPs = Position3WsToPs( i.vPositionWs.xyz );

		float2 uv = v.vTexCoord.xy;
		i.vTextureCoords.xy = float2( dot( uv, g_vTexCoordXform0.xy ) + g_vTexCoordXform0.w, dot( uv, g_vTexCoordXform1.xy ) + g_vTexCoordXform1.w ) + g_vTexCoordScrollSpeed * g_flTime;

		// The instance colour is gamma space; the game linearizes it, fades it in by the model tint amount, applies the
		// colour tint, then the painted vertex colour unless it's all zero.
		float4 instance = i.vVertexColor * v.vMeshTint;
		float4 tint = float4( lerp( 1.0, SrgbGammaToLinear( instance.rgb ), g_flModelTintAmount ) * g_vColorTint, instance.a );

		if ( any( v.vMeshColor != 0.0 ) )
			tint *= v.vMeshColor;

		i.vTint = tint;
		return FinalizeCS2Vertex( i );
	}
}

PS
{
	StaticCombo( S_BLEND_MODE, F_BLEND_MODE, Sys( ALL ) );

	// The blend states are the game's per blend mode; sbox_pixel's translucent ones don't cover them.
	#define BLEND_MODE_ALREADY_SET 1
	#define DEPTH_STATE_ALREADY_SET 1
	// sbox_pixel supplies culling; suppress the duplicate declaration in vr_common_ps_code.
	#define CULL_MODE_ALREADY_SET 1

	#include "common/pixel.hlsl"
	#include "common/classes/Light.hlsl"
	#include "cs2_lighting.hlsl"

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

	CreateInputTexture2D( CS2Color, Srgb, 8, "None", "_color", "Color", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( CS2Normal, Linear, 8, "None", "_normal", "Normal Map", Default4( 0.5, 0.5, 0.5, 0 ) );
	CreateInputTexture2D( CS2Metalness, Linear, 8, "None", "_metal", "Lighting", Default4( 0, 0, 0, 0 ) );
	CreateInputTexture2D( CS2AmbientOcclusion, Linear, 8, "None", "_ao", "Ambient Occlusion", Default4( 1, 1, 1, 0 ) );
	CreateInputTexture2D( CS2SelfIllumMask, Srgb, 8, "None", "_selfillum", "Self Illum", Default4( 0, 0, 0, 0 ) );

	// Mod2x reads its colour as stored, without sRGB decoding.
	#if S_BLEND_MODE == 3
		Texture2D g_tColor < Channel( RGBA, Box( CS2Color ), Srgb ); OutputFormat( BC7 ); SrgbRead( false ); >;
	#else
		Texture2D g_tColor < Channel( RGBA, Box( CS2Color ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	#endif
	Texture2D g_tNormal < Channel( RGBA, Box( CS2Normal ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tMetalness < Channel( RGBA, Box( CS2Metalness ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tAmbientOcclusion < Channel( RGBA, Box( CS2AmbientOcclusion ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tSelfIllumMask < Channel( RGBA, Box( CS2SelfIllumMask ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;

	TextureAttribute( RepresentativeTexture, g_tColor );

	// F_LIT: lit by the map's lighting; otherwise the overlay is its colour.
	float g_flLit < Default( 0 ); >;

	// Rows of the colour adjust matrix (MatrixColorTint2 times MatrixColorCorrect2, from the material), applied as
	// dot( row, float4( rgb, 1 ) ).
	float4 g_vTextureColorAdjust0 < Default4( 1, 0, 0, 0 ); >;
	float4 g_vTextureColorAdjust1 < Default4( 0, 1, 0, 0 ); >;
	float4 g_vTextureColorAdjust2 < Default4( 0, 0, 1, 0 ); >;

	float g_fTextureNormalContrast < Default( 1 ); >;
	float g_fTextureRoughnessContrast < Default( 1 ); >;
	float g_fTextureRoughnessBrightness < Default( 1 ); >;

	// F_AUTOMATIC_PBR_COLOR_FITTING, and the albedo levels the game fits colours to.
	float g_flAutomaticPBRColorFitting < Default( 0 ); >;
	float3 g_vAlbedoLevels < Default3( 0.045, 0.737, 0.85 ); >;
	float3 g_vMetallicAlbedoLevels < Default3( 0.45, 0.737, 0.92 ); >;
	float3 g_vDarkMetallicAlbedoLevels < Default3( 0.1, 0.737, 0.92 ); >;

	float g_flReflectance < Default( 0.04 ); >;

	float3 g_vSelfIllumTintScaled < Default3( 0, 0, 0 ); >;
	float2 g_vSelfIllumScrollSpeed < Default2( 0, 0 ); >;
	float g_flSelfIllumAlbedoFactor < Default( 1 ); >;

	float g_flAlphaTestReference < Default( 0.5 ); >;
	float g_flAntiAliasedEdgeStrength < Default( 1 ); >;

	float g_flFogEnabled < Default( 1 ); >;

	float3 DecodeHemiOctahedralNormal( float2 encoded )
	{
		float2 t = float2( encoded.x + encoded.y - 1.003921628, encoded.x - encoded.y );
		return normalize( float3( t, 1.0 - abs( t.x ) - abs( t.y ) ) );
	}

	float3 ColorAdjust( float3 rgb )
	{
		float4 c = float4( rgb, 1.0 );
		return float3( dot( g_vTextureColorAdjust0, c ), dot( g_vTextureColorAdjust1, c ), dot( g_vTextureColorAdjust2, c ) );
	}

	// The game's automatic PBR colour fitting: brings the albedo into the range of physically plausible ones.
	float3 FitPBRColor( float3 color, float metalness )
	{
		const float3 luminance = float3( 0.2125, 0.7154, 0.0721 );
		float3 direction = normalize( max( 0.0003, color ) ) * 1.06;
		float lum = dot( saturate( color ), luminance );
		float3 levels = lerp( g_vAlbedoLevels, lerp( g_vMetallicAlbedoLevels, g_vDarkMetallicAlbedoLevels, smoothstep( 0.55, 0.0156, lum ) ), metalness );
		float floorLevel = lerp( g_vAlbedoLevels.x, g_vDarkMetallicAlbedoLevels.x, metalness );
		float3 fitted = max( direction * floorLevel * 1.732 / length( direction ) / dot( direction, luminance ), direction * lerp( levels.x, levels.z, saturate( pow( max( color.r, max( color.g, color.b ) ), levels.y ) ) ) );
		return lerp( floorLevel.xxx, fitted, pow( smoothstep( 0.0003, floorLevel, lum ), 0.5 ) );
	}

	// Mod2x keeps the fog's colour out of what it multiplies: it fades towards neutral by the fog, more so for darker,
	// greyer fog, and lets a saturated fog's colour through.
	void Mod2xFog( inout float3 rgb, inout float alpha, float amount, float3 fogColor )
	{
		float low = min( fogColor.r, min( fogColor.g, fogColor.b ) );
		float high = max( fogColor.r, max( fogColor.g, fogColor.b ) );
		float saturation = high == 0.0 ? 0.0 : ( high - low ) / high;

		alpha *= 1.0 - pow( amount, lerp( 1.0, 2.0, pow( 1.0 - saturate( ( low + high ) * 0.5 ), 8.0 ) ) );
		rgb = lerp( rgb * 2.0, max( rgb, fogColor * ( normalize( max( 0.0003, fogColor ) ) * 1.06 ) ), saturation * amount ) * 0.5;
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = i.vTextureCoords.xy;
		float3 positionWs = i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz;

		float4 color = g_tColor.Sample( g_sAniso, uv );
		float alpha = i.vTint.a * color.a;

		#if S_BLEND_MODE == 2
		{
			// Anti-aliased edges: the alpha sharpened around the reference, blended back towards the plain alpha as
			// the texture is minified (the game's branch for this depends on a per view setting; unverified which).
			float aa = lerp( saturate( 0.5 + ( alpha - g_flAlphaTestReference ) / max( fwidth( alpha ), 1e-6 ) ), alpha, lerp( 1.0, saturate( 4.0 * length( fwidth( uv ) ) ), g_flAntiAliasedEdgeStrength ) );
			if ( aa - 0.001 < 0.0 )
				discard;
			alpha = aa;
		}
		#else
			alpha *= g_flOpacityScale;
		#endif

		#if S_BLEND_MODE == 0
			alpha = 1.0;
		#endif

		float3 albedo = ColorAdjust( color.rgb * i.vTint.rgb );

		// Derivatives need uniform control flow, so the shadow receiver normal is taken before branching.
		float3 receiverNormal = ComputeShadowReceiverNormal( i.vPositionWithOffsetWs.xyz );

		float3 rgb;

		[branch]
		if ( g_flLit > 0.5 && CS2LightingEnabled() )
		{
			float4 normalTexel = g_tNormal.Sample( g_sAniso, uv );
			float metalness = g_tMetalness.Sample( g_sAniso, uv ).g;
			float ao = g_tAmbientOcclusion.Sample( g_sAniso, uv ).r;

			if ( g_flAutomaticPBRColorFitting > 0.5 )
				albedo = FitPBRColor( albedo, metalness );

			float3 normalTs = DecodeHemiOctahedralNormal( normalTexel.xy );
			float2 bend = float2( normalTs.x, -normalTs.y ) * g_fTextureNormalContrast;
			float3 vertexNormal = normalize( i.vNormalWs.xyz );
			float3 normalWs = normalize( i.vTangentUWs.xyz * bend.x + i.vTangentVWs.xyz * bend.y + vertexNormal * normalTs.z );

			float3 dx = ddx( vertexNormal );
			float3 dy = ddy( vertexNormal );
			float roughness = max( saturate( ( ( normalTexel.b - 0.5 ) * g_fTextureRoughnessContrast + 0.5 ) * g_fTextureRoughnessBrightness ), pow( saturate( max( dot( dx, dx ), dot( dy, dy ) ) ), 0.333 ) );

			float3 selfIllum = g_tSelfIllumMask.Sample( g_sAniso, uv + frac( g_vSelfIllumScrollSpeed * g_flTime ) ).rgb * g_vSelfIllumTintScaled;

			CS2Surface s = CS2SurfaceDefaults();
			s.Position = positionWs;
			s.PositionWithOffset = i.vPositionWithOffsetWs.xyz;
			s.ScreenPosition = i.vPositionSs;
			s.Normal = normalWs;
			s.TangentNormal = normalize( float3( bend, normalTs.z ) );
			s.GeometricNormal = vertexNormal;
			s.ShadowReceiverNormal = receiverNormal;
			s.Albedo = albedo;
			s.Roughness = roughness;
			s.Metalness = metalness;
			s.Reflectance = g_flReflectance;
			s.AmbientOcclusion = ao;
			s.Emission = lerp( selfIllum, selfIllum * color.rgb * i.vTint.rgb, g_flSelfIllumAlbedoFactor );
			s.LightmapUV = i.vLightmapUV;
			s.SpecularDirect = true;
			s.SpecularIndirect = true;

			// CS2Shade fogs it already.
			return float4( CS2Shade( s ), alpha );
		}
		else
		{
			rgb = albedo;
		}

		[branch]
		if ( g_flFogEnabled > 0.5 && CS2LightingEnabled() )
		{
			float3 position = CS2ToMap( positionWs );

			#if S_BLEND_MODE == 3 || S_BLEND_MODE == 6
				float gradient, cubemap;
				float3 cubemapColor;
				CS2FogAt( position, true, gradient, cubemap, cubemapColor );

				if ( gradient > 0.0 )
					Mod2xFog( rgb, alpha, gradient, g_vCS2GradientFogColor.rgb );
				if ( cubemap > 0.0 )
					Mod2xFog( rgb, alpha, cubemap, cubemapColor );
			#elif S_BLEND_MODE == 4
				alpha *= CS2FogTransmission( position );
			#else
				rgb = CS2Fog( rgb, position );
			#endif
		}

		#if S_BLEND_MODE == 3 || S_BLEND_MODE == 6
			// Neutral grey multiplies by one.
			return float4( lerp( 0.5, rgb, alpha ), alpha );
		#elif S_BLEND_MODE == 5
			return float4( lerp( 1.0, rgb, alpha ), alpha );
		#else
			return float4( rgb, alpha );
		#endif
	}
}
