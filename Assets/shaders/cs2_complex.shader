//=========================================================================================================================
// CS2's csgo_complex.vfx: the general prop shader (always specular, unlike csgo_vertexlitgeneric).
//
// From the game's compiled shader (decompiled with Source 2 Viewer: plain, metalness and tint mask, self illumination,
// each detail mode, secondary texture coordinates, decals, vertex paint, depth biased translucency, alpha tested
// metalness and transmission variants) and csgo_depth_only.vfx, where it alpha tests. The colour times the instance tint
// (through the tint mask with F_TINT_MASK, times the painted vertex colour with F_PAINT_VERTEX_COLORS); the detail
// texture as mod2x, as a gamma space overlay, and/or as a detail normal; a decal over it, blended or multiplied;
// metalness from the colour's alpha, the metalness texture when the alpha is opacity, or the material; the
// hemi-octahedral normal with roughness in blue; ambient occlusion and self illumination; transmission like
// csgo_foliage's. Lit by cs2_lighting.hlsl.
//
// Static combos that only change what the shader reads are uniforms here (the mount sets them from the material's
// F_* values), so one compile covers them; the ones that change render state stay combos.
//
// F_DEPTH_BIAS (the game's depth bias of -64) pulls the surface a little towards the camera along the view ray instead,
// as cs2_static_overlay does.
//
// Not ported (yet): F_CLOTH_SHADING (4 materials, lit as plain surfaces), F_ANISOTROPIC_GLOSS (2), F_TEXTURE_ANIMATION
// (2), F_IGNORE_FACE_NORMALS_FOR_LIGHTING (4), and scaling texture coordinates by the model's scale (a dozen materials;
// the game does it outside this shader).
//=========================================================================================================================

HEADER
{
	Description = "CS2 csgo_complex";
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
	float4 vDetailUv : TEXCOORD9;		// xy: detail texture coordinates
	float4 vTint : TEXCOORD10;			// the instance tint (and painted vertex colour), linear, with the colour tint; alpha in w
};

VS
{
	#include "cs2_vertex.hlsl"

	// How far, as a fraction of the distance to the camera, depth biased surfaces are pulled towards it.
	#define DEPTH_BIAS_OFFSET 0.0005

	// The material's texture coordinate transform, rows of a 2x3 matrix (x, y, unused, offset), and scroll speed.
	float4 g_vTexCoordXform0 < Default4( 1, 0, 0, 0 ); >;
	float4 g_vTexCoordXform1 < Default4( 0, 1, 0, 0 ); >;
	float2 g_vTexCoordScrollSpeed < Default2( 0, 0 ); >;

	// The detail texture's 2x2 transform (rows in xy and zw) and offset, of the first or second coordinates.
	float4 g_vDetailTexCoordXform < Default4( 1, 0, 0, 1 ); >;
	float2 g_vDetailTexCoordOffset < Default2( 0, 0 ); >;
	float g_flSecondaryUvForDetail < Default( 0 ); >;

	// The colour tint (linear) and how much of the instance tint applies.
	float3 g_vColorTint < Default3( 1, 1, 1 ); >;
	float g_flModelTintAmount < Default( 1 ); >;

	// F_PAINT_VERTEX_COLORS: the tint is multiplied by the painted vertex colour, unless it's all zero.
	float g_flPaintVertexColors < Default( 0 ); >;

	// F_DEPTH_BIAS
	float g_flDepthBias < Default( 0 ); >;

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessCS2Vertex( v );

		if ( g_flDepthBias > 0.5 )
		{
			float3 toCamera = g_vCameraPositionWs - i.vPositionWs.xyz;
			i.vPositionWs.xyz += toCamera * DEPTH_BIAS_OFFSET;
			i.vPositionPs = Position3WsToPs( i.vPositionWs.xyz );
		}

		float2 uv = v.vTexCoord.xy;
		float2 colorUv = float2( dot( uv, g_vTexCoordXform0.xy ) + g_vTexCoordXform0.w, dot( uv, g_vTexCoordXform1.xy ) + g_vTexCoordXform1.w );
		i.vColorUv = float4( colorUv + g_vTexCoordScrollSpeed * g_flTime, v.vTexCoord2.xy );

		float2 detailUv = g_flSecondaryUvForDetail > 0.5 ? v.vTexCoord2.xy : uv;
		i.vDetailUv = float4( float2( dot( g_vDetailTexCoordXform.xy, detailUv ), dot( g_vDetailTexCoordXform.zw, detailUv ) ) + g_vDetailTexCoordOffset, 0, 0 );

		// The instance colour is gamma space; the game linearizes it, fades it in by the model tint amount, then
		// multiplies the material's colour tint in.
		float4 instance = i.vVertexColor * v.vMeshTint;
		float4 tint = float4( lerp( 1.0, SrgbGammaToLinear( instance.rgb ), g_flModelTintAmount ) * g_vColorTint, instance.a );

		if ( g_flPaintVertexColors > 0.5 && any( v.vMeshColor != 0.0 ) )
			tint *= v.vMeshColor;

		i.vTint = tint;
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
	CreateInputTexture2D( CS2TintMask, Linear, 8, "None", "_mask", "Color", Default4( 1, 1, 1, 0 ) );
	CreateInputTexture2D( CS2Metalness, Linear, 8, "None", "_metal", "Lighting", Default4( 0, 0, 0, 0 ) );
	CreateInputTexture2D( CS2SelfIllumMask, Srgb, 8, "None", "_selfillum", "Self Illum", Default4( 0, 0, 0, 0 ) );
	CreateInputTexture2D( CS2Detail, Linear, 8, "None", "_detail", "Detail Texture", Default4( 0.5, 0.5, 0.5, 1 ) );
	CreateInputTexture2D( CS2DetailMask, Linear, 8, "None", "_detailmask", "Detail Texture", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( CS2NormalDetail, Linear, 8, "None", "_normal", "Detail Texture", Default4( 0.5, 0.5, 0.5, 0 ) );
	CreateInputTexture2D( CS2Decal, Srgb, 8, "None", "_color", "Decal Texture", Default4( 0, 0, 0, 0 ) );
	CreateInputTexture2D( CS2TransmissiveColor, Srgb, 8, "None", "_color", "Transmission", Default4( 1, 1, 1, 0 ) );

	Texture2D g_tColor < Channel( RGBA, Box( CS2Color ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tNormal < Channel( RGBA, Box( CS2Normal ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tAmbientOcclusion < Channel( RGBA, Box( CS2AmbientOcclusion ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tTintMask < Channel( RGBA, Box( CS2TintMask ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tMetalness < Channel( RGBA, Box( CS2Metalness ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tSelfIllumMask < Channel( RGBA, Box( CS2SelfIllumMask ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tDetail < Channel( RGBA, Box( CS2Detail ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tDetailMask < Channel( RGBA, Box( CS2DetailMask ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tNormalDetail < Channel( RGBA, Box( CS2NormalDetail ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tDecal < Channel( RGBA, Box( CS2Decal ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tTransmissiveColor < Channel( RGBA, Box( CS2TransmissiveColor ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;

	TextureAttribute( RepresentativeTexture, g_tColor );
	TextureAttribute( LightSim_DiffuseAlbedoTexture, g_tColor );

	//
	// Parameters, as the game's shader computes them from the material (MaterialImporter in the mount)
	//

	// F_TINT_MASK: the tint mask's red channel gates the tint.
	float g_flTintMask < Default( 0 ); >;

	// Where metalness comes from: 0 the material's g_flMetalness, 1 the colour's alpha (F_METALNESS_TEXTURE), 2 the
	// metalness texture's green (F_METALNESS_TEXTURE when the colour's alpha is opacity).
	float g_flMetalnessSource < Default( 0 ); >;
	float g_flMetalness < Default( 0 ); >;
	float g_flReflectance < Default( 0.04 ); >;

	// With F_SECONDARY_UV, which textures read the second coordinates.
	float g_flSecondaryUvForTintMask < Default( 0 ); >;
	float g_flSecondaryUvForAmbientOcclusion < Default( 0 ); >;
	float g_flSecondaryUvForDetailMask < Default( 0 ); >;
	float g_flSecondaryUvForDecal < Default( 0 ); >;
	float g_flSecondaryUvForSelfIllum < Default( 0 ); >;

	// F_DETAIL_TEXTURE: 0 none, 1 mod2x, 2 overlay, 3 normals, 4 overlay and normals; by the mask's red, raised to the
	// blend to full, times the blend factor.
	float g_flDetailTexture < Default( 0 ); >;
	float g_flDetailBlendFactor < Default( 1 ); >;
	float g_flDetailBlendToFull < Default( 0 ); >;
	float4 g_vDetailNormalStrengthOffsetScale < Default4( 0, 0, 0, 1 ); >;	// ( 0, 0, 1 - strength, strength )

	// F_DECAL_TEXTURE, and F_DECAL_BLEND_MODE: 0 blended by its alpha, 1 multiplied.
	float g_flDecalTexture < Default( 0 ); >;
	float g_flDecalBlendMode < Default( 0 ); >;

	// F_SELF_ILLUM; without it the scaled tint is 0. Scaled = 2^brightness * scale * linear tint.
	float3 g_vSelfIllumTintScaled < Default3( 0, 0, 0 ); >;
	float2 g_vSelfIllumScrollSpeed < Default2( 0, 0 ); >;
	float g_flSelfIllumAlbedoFactor < Default( 1 ); >;

	// F_TRANSMISSIVE_BACKFACE_NDOTL; F_USE_ALBEDO_FOR_TRANSMISSIVE; not F_DISABLE_TRANSMISSIVE_SHADOWS.
	float g_flTransmissive < Default( 0 ); >;
	float g_flUseAlbedoForTransmissive < Default( 0 ); >;
	float g_flTransmissiveShadows < Default( 1 ); >;

	// F_RENDER_BACKFACES without F_DONT_FLIP_BACKFACE_NORMALS: back faces light with the normal flipped.
	float g_flFlipBackfaceNormals < Default( 0 ); >;

	float g_flFogEnabled < Default( 1 ); >;

	#if !S_TRANSLUCENT
		float g_flOpacityScale < Default( 1 ); >;
	#endif

	// The game's hemi-octahedral normal decode: x + y and x - y, folded onto the upper hemisphere.
	float3 DecodeHemiOctahedralNormal( float2 encoded )
	{
		float2 t = float2( encoded.x + encoded.y - 1.003921628, encoded.x - encoded.y );
		return normalize( float3( t, 1.0 - abs( t.x ) - abs( t.y ) ) );
	}

	float2 PickUv( PixelInput i, float secondary )
	{
		return secondary > 0.5 ? i.vColorUv.zw : i.vColorUv.xy;
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		#if S_RENDER_BACKFACES
			bool isFrontFace = i.face;
		#else
			bool isFrontFace = true;
		#endif

		float2 colorUv = i.vColorUv.xy;
		float4 color = g_tColor.Sample( g_sAniso, colorUv );

		//
		// Alpha: csgo_depth_only's test on the colour's alpha, or translucency by it and the instance alpha
		//
		float alpha = i.vTint.a;
		#if S_ALPHA_TEST && !S_MODE_DEPTH
		{
			// Anti-aliased edges (the game's branch for this depends on a per view setting; unverified which).
			alpha = lerp( saturate( 0.5 + ( color.a - g_flAlphaTestReference ) / max( fwidth( color.a ), 1e-6 ) ), color.a, lerp( 1.0, saturate( 4.0 * length( fwidth( colorUv ) ) ), g_flAntiAliasedEdgeStrength ) );
			if ( alpha - 0.001 < 0.0 )
				discard;
		}
		#elif S_ALPHA_TEST
			// The depth pass: s&box's shading model cuts it out against the same reference.
			alpha = color.a;
		#elif S_TRANSLUCENT
			alpha = i.vTint.a * color.a * g_flOpacityScale;
		#endif

		int detailMode = (int)round( g_flDetailTexture );
		float detailBlend = 0.0;
		[branch]
		if ( detailMode > 0 )
			detailBlend = max( g_tDetailMask.Sample( g_sAniso, PickUv( i, g_flSecondaryUvForDetailMask ) ).r, g_flDetailBlendToFull ) * g_flDetailBlendFactor;

		//
		// Albedo: the tint where the mask allows, the detail texture, the decal
		//
		float3 albedo = color.rgb * i.vTint.rgb;
		if ( g_flTintMask > 0.5 )
			albedo = lerp( color.rgb, albedo, g_tTintMask.Sample( g_sAniso, PickUv( i, g_flSecondaryUvForTintMask ) ).r );

		[branch]
		if ( detailMode == 1 || detailMode == 2 || detailMode == 4 )
		{
			float3 detail = g_tDetail.Sample( g_sAniso, i.vDetailUv.xy ).rgb;

			if ( detailMode == 1 )
			{
				albedo *= lerp( 1.0, detail * 1.99215686, detailBlend );
			}
			else
			{
				// Overlay, in gamma space: multiply below middle grey, screen above.
				float3 base = pow( albedo, 1.0 / 2.2 );
				float3 layer = detail * 0.996078431;
				float3 overlay = lerp( 1.0 - 2.0 * ( 1.0 - base ) * ( 1.0 - layer ), 2.0 * base * layer, step( base, 0.5 ) );
				albedo = lerp( albedo, pow( overlay, 2.2 ), detailBlend );
			}
		}

		[branch]
		if ( g_flDecalTexture > 0.5 )
		{
			float4 decal = g_tDecal.Sample( g_sAniso, PickUv( i, g_flSecondaryUvForDecal ) );
			albedo = g_flDecalBlendMode > 0.5 ? albedo * decal.rgb : lerp( albedo, decal.rgb, decal.a );
		}

		float metalness = g_flMetalness;
		if ( g_flMetalnessSource > 1.5 )
			metalness = g_tMetalness.Sample( g_sAniso, colorUv ).g;
		else if ( g_flMetalnessSource > 0.5 )
			metalness = color.a;

		float ao = g_tAmbientOcclusion.Sample( g_sAniso, PickUv( i, g_flSecondaryUvForAmbientOcclusion ) ).r;

		//
		// Normal: the detail normal bent in, then tangent space from the vertex frame, flipped on back faces
		//
		float4 normalTexel = g_tNormal.Sample( g_sAniso, colorUv );
		float3 normalTs = DecodeHemiOctahedralNormal( normalTexel.xy );

		[branch]
		if ( detailMode >= 3 )
		{
			float3 detailNormal = DecodeHemiOctahedralNormal( g_tNormalDetail.Sample( g_sAniso, i.vDetailUv.xy ).xy );
			float3 bend = lerp( float3( 0, 0, 1 ), g_vDetailNormalStrengthOffsetScale.xyz + g_vDetailNormalStrengthOffsetScale.w * detailNormal, detailBlend );
			normalTs *= bend.z;
			normalTs.xy += bend.xy * normalTs.z;
			normalTs = normalize( normalTs );
		}

		bool flip = g_flFlipBackfaceNormals > 0.5 && !isFrontFace;
		float3 vertexNormal = normalize( i.vNormalWs.xyz ) * ( flip ? -1.0 : 1.0 );
		float3 tangent = i.vTangentUWs.xyz;
		float3 bitangent = i.vTangentVWs.xyz;

		if ( !flip )
			normalTs.y = -normalTs.y;

		float3 normalWs = normalize( tangent * normalTs.x + bitangent * normalTs.y + vertexNormal * normalTs.z );

		// Roughness from the normal map's blue channel, raised where the surface normal changes quickly across the
		// pixel (geometric specular antialiasing).
		float3 dx = ddx( vertexNormal );
		float3 dy = ddy( vertexNormal );
		float roughness = max( normalTexel.b, pow( saturate( max( dot( dx, dx ), dot( dy, dy ) ) ), 0.333 ) );

		//
		// Self illumination and transmission
		//
		float3 selfIllum = g_tSelfIllumMask.Sample( g_sAniso, PickUv( i, g_flSecondaryUvForSelfIllum ) + frac( g_vSelfIllumScrollSpeed * g_flTime ) ).rgb * g_vSelfIllumTintScaled;
		float3 emission = lerp( selfIllum, selfIllum * albedo, g_flSelfIllumAlbedoFactor );

		float3 transmission = 0;
		[branch]
		if ( g_flTransmissive > 0.5 )
			transmission = g_flUseAlbedoForTransmissive > 0.5 ? albedo : g_tTransmissiveColor.Sample( g_sTrilinearWrap, colorUv ).rgb * i.vTint.rgb;

		Material m = Material::Init( i.vPositionWithOffsetWs, i.vPositionSs );
		m.Albedo = albedo;
		m.Emission = emission;
		m.Normal = normalWs;
		m.TangentNormal = normalTs;
		m.GeometricNormal = vertexNormal;
		m.WorldTangentU = tangent;
		m.WorldTangentV = bitangent;
		m.LightmapUV = i.vLightmapUV;
		m.TextureCoords = colorUv;
		m.Roughness = saturate( roughness );
		m.Metalness = saturate( metalness );
		m.AmbientOcclusion = ao;
		m.Opacity = alpha;

		#if S_MODE_DEPTH
			return ShadingModelStandard::Shade( m );
		#else
			// Derivatives need uniform control flow, so the shadow receiver normal is taken before branching.
			float3 receiverNormal = ComputeShadowReceiverNormal( m.WorldPositionWithOffset );

			[branch]
			if ( !CS2LightingEnabled() )
				return ShadingModelStandard::Shade( m );

			if ( DepthNormals::WantsDepthNormals() )
				return DepthNormals::Output( m.Normal, m.Roughness, m.Opacity );

			if ( g_bWireframeMode )
				return g_vWireframeColor;

			CS2Surface s = CS2SurfaceDefaults();
			s.Position = m.WorldPosition;
			s.PositionWithOffset = m.WorldPositionWithOffset;
			s.ScreenPosition = m.ScreenPosition;
			s.Normal = normalWs;
			s.ShadowReceiverNormal = receiverNormal;
			s.Albedo = albedo;
			s.Roughness = roughness;
			s.Metalness = metalness;
			s.Reflectance = g_flReflectance;
			s.AmbientOcclusion = ao;
			s.Emission = emission;
			s.LightmapUV = i.vLightmapUV;
			s.SpecularDirect = true;
			s.SpecularIndirect = true;
			s.Fog = g_flFogEnabled > 0.5;
			s.Transmission = transmission;
			s.GeometricNormal = vertexNormal;
			s.TangentNormal = normalTs;
			s.TransmissionShadows = g_flTransmissiveShadows > 0.5;

			return float4( CS2Shade( s ), alpha );
		#endif
	}
}
