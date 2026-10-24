//=========================================================================================================================
// CS2's csgo_weapon.vfx: weapons' default finishes, knives, the C4, scopes.
//
// From the game's compiled shader (decompiled with Source 2 Viewer: plain, self illuminated, tint mask, translucent and
// alpha tested variants). The colour times the instance tint (through the tint mask's red with F_TINT_MASK);
// roughness in the metalness texture's red, metalness in its green remapped through g_vMetalnessRemapRange, the
// pearlescent mask in its blue (the colour's hue turned by the view angle); the hemi-octahedral normal; diffuse is the
// colour times ( 1 - metalness ) ^ g_flMetalnessTransitionBias, specular the colour by metalness over 0.02; self
// illumination; lit by cs2_lighting.hlsl's CS2PbrShade (GGX, the split sum table, multiple scattering).
//
// g_flZBias is added to the clip space depth as the game does (its depth convention against s&box's is unverified).
//
// F_STATTRAK (the StatTrak module's display, from the decompiled vertex shader): vertices with a negative v are digit
// quads, the integer part of u minus one their place (0 for hundred thousands to 5 for ones); each moves its u along the
// 16 column digit atlas to its digit of the count, column 0 (blank) for leading zeros. The count is the renderer's
// CS2StatTrakValue attribute (the game's g_nStatTrakValue, a dynamic parameter: "return $ent_stattrak;").
//
// Not ported: F_STICKERS (stickers are the paint kit phase; the default finishes have every sticker off),
// F_GLITTER, F_IRIDESCENCE, F_ENABLE_SFX_MASK, F_ENABLE_ADJUSTMENTS / F_DETAIL_TEXTURE and F_OPAQUE_REFRACT (keychains
// and charms), the player and weapon occlusion squashes, wetness.
//=========================================================================================================================

HEADER
{
	Description = "CS2 csgo_weapon";
}

FEATURES
{
	#include "common/features.hlsl"
	Feature( F_ALPHA_TEST, 0..1, "Translucent" );
	Feature( F_TRANSLUCENT, 0..1, "Translucent" );
	FeatureRule( Allow1( F_ALPHA_TEST, F_TRANSLUCENT ), "Translucent and Alpha Test are not compatible" );
	FeatureRule( Requires1( F_ADDITIVE_BLEND, F_TRANSLUCENT ), "Additive requires translucency" );
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
	float4 vColorUv : TEXCOORD5;		// xy: transformed and scrolled colour coordinates; zw: the second coordinates
	float4 vTint : TEXCOORD10;			// the instance tint, linear, with the colour tint; alpha in w
};

VS
{
	#include "cs2_vertex.hlsl"

	float4 g_vTexCoordXform0 < Default4( 1, 0, 0, 0 ); >;
	float4 g_vTexCoordXform1 < Default4( 0, 1, 0, 0 ); >;
	float2 g_vTexCoordScrollSpeed < Default2( 0, 0 ); >;

	float3 g_vColorTint < Default3( 1, 1, 1 ); >;
	float g_flModelTintAmount < Default( 1 ); >;

	float g_flZBias < Default( 0 ); >;

	// F_STATTRAK, and the count the renderer shows.
	float g_flStatTrak < Default( 0 ); >;
	float g_flStatTrakValue < Attribute( "CS2StatTrakValue" ); Default( 0 ); >;

	// 10 to the power of a StatTrak digit's place from the right: the game's table (100000 ... 1) by place.
	uint StatTrakDivisor( int place )
	{
		uint divisor = 1;
		for ( int i = place; i < 5; i++ )
			divisor *= 10;

		return divisor;
	}

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessCS2Vertex( v );

		float2 uv = v.vTexCoord.xy;
		float2 colorUv = float2( dot( uv, g_vTexCoordXform0.xy ) + g_vTexCoordXform0.w, dot( uv, g_vTexCoordXform1.xy ) + g_vTexCoordXform1.w );
		colorUv += g_vTexCoordScrollSpeed * g_flTime;

		if ( g_flStatTrak > 0.5 && uv.y < 0.0 )
		{
			float slot = floor( uv.x ) - 1.0;
			int place = clamp( (int)slot, 0, 5 );
			uint count = (uint)max( g_flStatTrakValue, 0.0 ) / StatTrakDivisor( place );
			uint shown = ( count != 0 || place == 5 ) ? 1 : 0;
			colorUv.x = ( colorUv.x - slot * 0.0625 ) + (float)( shown + count % 10 ) * 0.0625;
		}

		i.vColorUv = float4( colorUv, v.vTexCoord2.xy );

		// The instance colour is gamma space; the game linearizes it, fades it in by the model tint amount, then
		// multiplies the material's colour tint in.
		float4 instance = i.vVertexColor * v.vMeshTint;
		i.vTint = float4( lerp( 1.0, SrgbGammaToLinear( instance.rgb ), g_flModelTintAmount ) * g_vColorTint, instance.a );

		i.vPositionPs.z += g_flZBias;
		return FinalizeCS2Vertex( i );
	}
}

PS
{
	StaticCombo( S_ALPHA_TEST, F_ALPHA_TEST, Sys( ALL ) );
	StaticCombo( S_TRANSLUCENT, F_TRANSLUCENT, Sys( ALL ) );
	StaticCombo( S_ADDITIVE_BLEND, F_ADDITIVE_BLEND, Sys( ALL ) );

	// sbox_pixel supplies culling; suppress the duplicate declaration in vr_common_ps_code.
	#define CULL_MODE_ALREADY_SET
	#include "common/pixel.hlsl"
	#include "common/classes/Light.hlsl"
	#include "cs2_lighting.hlsl"

	//
	// Textures, with the defaults the game's shader gives them
	//
	CreateInputTexture2D( CS2Color, Srgb, 8, "None", "_color", "Color", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( CS2Normal, Linear, 8, "None", "_normal", "Normal Map", Default4( 0.5, 0.5, 0.5, 0 ) );
	CreateInputTexture2D( CS2AmbientOcclusion, Linear, 8, "None", "_ao", "Ambient Occlusion", Default4( 1, 1, 1, 0 ) );
	CreateInputTexture2D( CS2Metalness, Linear, 8, "None", "_metal", "Lighting", Default4( 0.5, 0, 0, 0 ) );
	CreateInputTexture2D( CS2TintMask, Linear, 8, "None", "_mask", "Color", Default4( 1, 1, 1, 0 ) );
	CreateInputTexture2D( CS2SelfIllumMask, Srgb, 8, "None", "_selfillum", "Self Illum", Default4( 0, 0, 0, 0 ) );

	Texture2D g_tColor < Channel( RGBA, Box( CS2Color ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tNormal < Channel( RGBA, Box( CS2Normal ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tAmbientOcclusion < Channel( RGBA, Box( CS2AmbientOcclusion ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tMetalness < Channel( RGBA, Box( CS2Metalness ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tTintMask < Channel( RGBA, Box( CS2TintMask ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tSelfIllumMask < Channel( RGBA, Box( CS2SelfIllumMask ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;

	// csgo_weapon has no skin; CS2PbrShade's table for it is never read.
	Texture2D g_tDiffuseFalloff < Attribute( "CS2UnusedDiffuseFalloff" ); SrgbRead( false ); >;

	TextureAttribute( RepresentativeTexture, g_tColor );
	TextureAttribute( LightSim_DiffuseAlbedoTexture, g_tColor );

	//
	// Parameters, as the game's shader computes them from the material (MaterialImporter in the mount)
	//
	float g_flTintMask < Default( 0 ); >;
	float2 g_vMetalnessRemapRange < Default2( 0, 1 ); >;
	float g_flMetalnessTransitionBias < Default( 1 ); >;
	float g_flPearlescentScale < Default( 0 ); >;

	// F_SELF_ILLUM; without it the scaled tint is 0. Scaled = 2^brightness * scale * linear tint.
	float3 g_vSelfIllumTintScaled < Default3( 0, 0, 0 ); >;
	float2 g_vSelfIllumScrollSpeed < Default2( 0, 0 ); >;
	float g_flSelfIllumAlbedoFactor < Default( 1 ); >;

	float g_flFlipBackfaceNormals < Default( 0 ); >;
	float g_flFogEnabled < Default( 1 ); >;

	#if !S_TRANSLUCENT
		float g_flOpacityScale < Default( 1 ); >;
	#endif

	float3 DecodeHemiOctahedralNormal( float2 encoded )
	{
		float2 t = float2( encoded.x + encoded.y - 1.003921628, encoded.x - encoded.y );
		return normalize( float3( t, 1.0 - abs( t.x ) - abs( t.y ) ) );
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		#if S_RENDER_BACKFACES
			bool isFrontFace = i.face;
		#else
			bool isFrontFace = true;
		#endif

		float2 uv = i.vColorUv.xy;
		float4 color = g_tColor.Sample( g_sAniso, uv );

		float alpha = i.vTint.a;
		#if S_ALPHA_TEST && !S_MODE_DEPTH
		{
			// The game's branch for this depends on a per view setting; the anti-aliased one (unverified which).
			float coverage = color.a * i.vTint.a;
			alpha = lerp( saturate( 0.5 + ( coverage - g_flAlphaTestReference ) / max( fwidth( coverage ), 9.99999997e-07 ) ), coverage, lerp( 1.0, saturate( 4.0 * length( fwidth( uv ) ) ), g_flAntiAliasedEdgeStrength ) );
			if ( alpha - 0.001 < 0.0 )
				discard;
		}
		#elif S_ALPHA_TEST
			alpha = color.a * i.vTint.a;
		#elif S_TRANSLUCENT
			alpha = i.vTint.a * ( color.a * g_flOpacityScale );
		#endif

		float3 albedo = color.rgb * i.vTint.rgb;
		if ( g_flTintMask > 0.5 )
			albedo = lerp( color.rgb, albedo, g_tTintMask.Sample( g_sAniso, uv ).r );

		float ao = g_tAmbientOcclusion.Sample( g_sAniso, uv ).r;
		float4 metal = g_tMetalness.Sample( g_sAniso, uv );
		float metalness = lerp( g_vMetalnessRemapRange.x, g_vMetalnessRemapRange.y, metal.g );
		float2 roughness = metal.rr;

		float4 normalTexel = g_tNormal.Sample( g_sAniso, uv );
		float3 normalTs = DecodeHemiOctahedralNormal( normalTexel.xy );

		bool flip = g_flFlipBackfaceNormals > 0.5 && !isFrontFace;
		if ( !flip )
			normalTs.y = -normalTs.y;

		float3 vertexNormal = normalize( i.vNormalWs.xyz ) * ( flip ? -1.0 : 1.0 );
		float3 tangent = i.vTangentUWs.xyz;
		float3 bitangent = i.vTangentVWs.xyz * ( flip ? -1.0 : 1.0 );
		float3 normalWs = normalize( tangent * normalTs.x + bitangent * normalTs.y + vertexNormal * normalTs.z );

		float3 dx = ddx( vertexNormal );
		float3 dy = ddy( vertexNormal );
		roughness = max( roughness, pow( saturate( max( dot( dx, dx ), dot( dy, dy ) ) ), 0.333 ) );

		// Pearlescence: the hue turned by the view's angle to the surface, by the mask, more so for saturated colours.
		float3 surfaceColor = albedo;
		[branch]
		if ( g_flPearlescentScale != 0.0 )
		{
			float3 worldPosition = i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz;
			float angle = ( g_flPearlescentScale * ( 1.0 - dot( normalize( g_vCameraPositionWs - worldPosition ), normalWs ) ) ) * metal.b;
			float c = cos( angle );
			float brightest = max( albedo.r, max( albedo.g, albedo.b ) );
			float saturation = brightest == 0.0 ? 0.0 : ( brightest - min( albedo.r, min( albedo.g, albedo.b ) ) ) / brightest;
			float3 grey = float3( 0.57735002, 0.57735002, 0.57735002 );
			float3 turned = albedo * c + cross( grey, albedo ) * sin( angle ) + grey * dot( grey, albedo ) * ( 1.0 - c );
			surfaceColor = lerp( dot( albedo, float3( 0.2125, 0.7154, 0.0721 ) ).xxx, turned, pow( saturation, 0.125 ) );
		}

		float3 selfIllum = g_tSelfIllumMask.Sample( g_sAniso, uv + frac( g_vSelfIllumScrollSpeed * g_flTime ) ).rgb * g_vSelfIllumTintScaled;
		float3 emission = lerp( selfIllum, selfIllum * albedo, g_flSelfIllumAlbedoFactor );

		Material m = Material::Init( i.vPositionWithOffsetWs, i.vPositionSs );
		m.Albedo = surfaceColor;
		m.Emission = emission;
		m.Normal = normalWs;
		m.TangentNormal = normalTs;
		m.GeometricNormal = vertexNormal;
		m.WorldTangentU = tangent;
		m.WorldTangentV = bitangent;
		m.TextureCoords = uv;
		m.Roughness = saturate( roughness.x );
		m.Metalness = saturate( metalness );
		m.AmbientOcclusion = ao;
		m.Opacity = alpha;

		#if S_MODE_DEPTH
			return ShadingModelStandard::Shade( m );
		#else
			float3 receiverNormal = ComputeShadowReceiverNormal( m.WorldPositionWithOffset );

			[branch]
			if ( !CS2LightingEnabled() )
				return ShadingModelStandard::Shade( m );

			if ( DepthNormals::WantsDepthNormals() )
				return DepthNormals::Output( m.Normal, m.Roughness, m.Opacity );

			if ( g_bWireframeMode )
				return g_vWireframeColor;

			CS2PbrSurface s = CS2PbrSurfaceDefaults();
			s.Position = m.WorldPosition;
			s.PositionWithOffset = m.WorldPositionWithOffset;
			s.ScreenPosition = m.ScreenPosition;
			s.ShadowReceiverNormal = receiverNormal;
			s.Normal = normalWs;
			s.VertexNormal = vertexNormal;
			s.Tangent = tangent;
			s.Bitangent = normalize( cross( normalWs, tangent ) );
			s.DiffuseColor = surfaceColor * pow( 1.0 - metalness, g_flMetalnessTransitionBias );
			s.SpecularColor = lerp( 0.02.xxx, surfaceColor, metalness );
			s.Roughness = roughness;
			s.AmbientOcclusion = ao;
			s.Emission = emission;
			s.Fog = g_flFogEnabled > 0.5;

			return float4( CS2PbrShade( s, g_tDiffuseFalloff ), alpha );
		#endif
	}
}
