//=========================================================================================================================
// CS2's csgo_vertexlitgeneric.vfx ("(S1) vertexlit, bump, phong shader for CSGO"), its most used prop shader.
//
// The material side follows the game's compiled shader (decompiled with Source 2 Viewer): texture coordinate transforms
// from the material's xform expressions, the per instance tint gated by the tint mask or the colour's alpha, the mod2x
// detail texture, the hemi-octahedral normal map with roughness in its blue channel and geometric specular
// antialiasing, metalness in g_tMetalness's green channel, the remapped ambient occlusion on the second texture
// coordinates, and self illumination.
//
// Static combos that only change what the shader reads are uniforms here (the mount sets them from the material's
// F_* values), so one compile covers them; the ones that change render state stay combos.
//
// Lighting: in a mounted map, the game's own (cs2_lighting.hlsl, from the same decompiled shader): the lightmaps or the
// light probes, the sun and lights, the view's cubemap and the fog. Elsewhere, s&box's lights (ShadingModelStandard).
//=========================================================================================================================

HEADER
{
	Description = "CS2 csgo_vertexlitgeneric";
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
	float4 vColorUv : TEXCOORD5;		// xy: transformed and scrolled colour coordinates; zw: ambient occlusion's
	float4 vNormalDetailUv : TEXCOORD9;	// xy: normal map coordinates; zw: detail texture coordinates
	float4 vTint : TEXCOORD10;			// the instance tint, linear, with the tint colour applied; alpha in w
};

VS
{
	#include "cs2_vertex.hlsl"

	// The material's texture coordinate transforms, rows of a 2x3 matrix (x, y, unused, offset), as the game's
	// xform expressions compute them from scale, rotation, center and offset (CS2MaterialParameters in the mount).
	float4 g_vTexCoordXform0 < Default4( 1, 0, 0, 0 ); >;
	float4 g_vTexCoordXform1 < Default4( 0, 1, 0, 0 ); >;
	float2 g_vTexCoordScrollSpeed < Default2( 0, 0 ); >;
	float4 g_vNormalTexCoordXform0 < Default4( 1, 0, 0, 0 ); >;
	float4 g_vNormalTexCoordXform1 < Default4( 0, 1, 0, 0 ); >;
	float4 g_vDetailTexCoordXform < Default4( 1, 0, 0, 1 ); >;
	float2 g_vDetailTexCoordOffset < Default2( 0, 0 ); >;

	// The colour tint (linear) and how much of the instance tint applies.
	float3 g_vColorTint < Default3( 1, 1, 1 ); >;
	float g_flModelTintAmount < Default( 1 ); >;

	// F_FORCE_UV2 or F_DECAL_TEXTURE: the second texture coordinates exist, and ambient occlusion reads them.
	float g_flSecondaryUv < Default( 0 ); >;

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessCS2Vertex( v );

		float2 uv = v.vTexCoord.xy;
		float2 colorUv = float2( dot( uv, g_vTexCoordXform0.xy ) + g_vTexCoordXform0.w, dot( uv, g_vTexCoordXform1.xy ) + g_vTexCoordXform1.w );
		colorUv += g_vTexCoordScrollSpeed * g_flTime;

		i.vColorUv = float4( colorUv, g_flSecondaryUv > 0.5 ? v.vTexCoord2.xy : uv );
		i.vNormalDetailUv.xy = float2( dot( uv, g_vNormalTexCoordXform0.xy ) + g_vNormalTexCoordXform0.w, dot( uv, g_vNormalTexCoordXform1.xy ) + g_vNormalTexCoordXform1.w );
		i.vNormalDetailUv.zw = float2( dot( g_vDetailTexCoordXform.xy, uv ), dot( g_vDetailTexCoordXform.zw, uv ) ) + g_vDetailTexCoordOffset;

		// The instance colour is gamma space; the game linearizes it, fades it in by the model tint amount, then
		// multiplies the material's colour tint in.
		float4 instance = i.vVertexColor * v.vMeshTint;
		i.vTint = float4( lerp( 1.0, SrgbGammaToLinear( instance.rgb ), g_flModelTintAmount ) * g_vColorTint, instance.a );

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
	CreateInputTexture2D( CS2Color, Srgb, 8, "None", "_color", "Color", Default4( 1, 1, 1, 0 ) );
	CreateInputTexture2D( CS2TintMask, Linear, 8, "None", "_mask", "Color", Default4( 1, 1, 1, 0 ) );
	CreateInputTexture2D( CS2Normal, Linear, 8, "None", "_normal", "Normal Map", Default4( 0.5, 0.5, 0.5, 0 ) );
	CreateInputTexture2D( CS2Metalness, Linear, 8, "None", "_metal", "Lighting", Default4( 0, 0, 0, 0 ) );
	CreateInputTexture2D( CS2AmbientOcclusion, Linear, 8, "None", "_ao", "Ambient Occlusion", Default4( 1, 1, 1, 0 ) );
	CreateInputTexture2D( CS2Detail, Linear, 8, "None", "_detail", "Detail Texture", Default4( 0.5, 0.5, 0.5, 1 ) );
	CreateInputTexture2D( CS2DetailMask, Linear, 8, "None", "_detailmask", "Detail Texture", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( CS2SelfIllumMask, Srgb, 8, "None", "_selfillum", "Self Illum", Default4( 0, 0, 0, 0 ) );

	Texture2D g_tColor < Channel( RGBA, Box( CS2Color ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tTintMask < Channel( RGBA, Box( CS2TintMask ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tNormal < Channel( RGBA, Box( CS2Normal ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tMetalness < Channel( RGBA, Box( CS2Metalness ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tAmbientOcclusion < Channel( RGBA, Box( CS2AmbientOcclusion ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tDetail < Channel( RGBA, Box( CS2Detail ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tDetailMask < Channel( RGBA, Box( CS2DetailMask ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tSelfIllumMask < Channel( RGBA, Box( CS2SelfIllumMask ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;

	TextureAttribute( RepresentativeTexture, g_tColor );
	TextureAttribute( LightSim_DiffuseAlbedoTexture, g_tColor );

	//
	// Parameters, as the game's shader computes them from the material (CS2MaterialParameters in the mount)
	//

	// F_NOTINT ? -1 : 1 - F_TINT_MASK: added to the tint mask (or the colour's alpha without one) to gate the tint.
	float g_flInverseBlendTint < Default( 1 ); >;
	float g_flTintMask < Default( 0 ); >;	// F_TINT_MASK: the tint mask's red channel gates the tint, else colour alpha

	float g_flReflectance < Default( 0.04 ); >;

	float g_flAmbientOcclusionScale < Default( 0 ); >;

	// F_DETAIL_TEXTURE (mod2x); without it the blend factor is 0.
	float g_flDetailBlendFactor < Default( 0 ); >;
	float g_flDetailBlendToFull < Default( 0 ); >;

	// F_SELF_ILLUM; without it the scaled tint is 0. Scaled = 2^brightness * scale * linear tint.
	float3 g_vSelfIllumTintScaled < Default3( 0, 0, 0 ); >;
	float2 g_vSelfIllumScrollSpeed < Default2( 0, 0 ); >;
	float g_flSelfIllumAlbedoFactor < Default( 1 ); >;

	// F_RENDER_BACKFACES without F_DONT_FLIP_BACKFACE_NORMALS: back faces light with the normal flipped.
	float g_flFlipBackfaceNormals < Default( 0 ); >;

	// F_SPECULAR_DIRECT and F_SPECULAR_INDIRECT ($phong and $envmap in Source 1): the lights' specular and the
	// cubemap's. Without them the game's shader lights diffuse only.
	float g_flSpecularDirect < Default( 0 ); >;
	float g_flSpecularIndirect < Default( 0 ); >;

	#if !S_TRANSLUCENT
		float g_flOpacityScale < Default( 1 ); >;
	#endif

	// The game's hemi-octahedral normal decode: x + y and x - y, folded onto the upper hemisphere.
	float3 DecodeHemiOctahedralNormal( float2 encoded )
	{
		float2 t = float2( encoded.x + encoded.y - 1.003921628, encoded.x - encoded.y );
		return normalize( float3( t, 1.0 - abs( t.x ) - abs( t.y ) ) );
	}

	// Lit by the map's CS2 lighting, as the game's shader lights.
	float4 ShadeCS2( Material m, float3 receiverNormal )
	{
		Decals::Apply( m.WorldPosition, m );
		AdjustAlphaToCoverage( m );

		if ( DepthNormals::WantsDepthNormals() )
			return DepthNormals::Output( m.Normal, m.Roughness, m.Opacity );

		if ( g_bWireframeMode )
			return g_vWireframeColor;

		CS2Surface s = CS2SurfaceDefaults();
		s.Position = m.WorldPosition;
		s.PositionWithOffset = m.WorldPositionWithOffset;
		s.ScreenPosition = m.ScreenPosition;
		s.Normal = m.Normal;
		s.TangentNormal = m.TangentNormal;
		s.GeometricNormal = m.GeometricNormal;
		s.ShadowReceiverNormal = receiverNormal;
		s.Albedo = m.Albedo;
		s.Roughness = m.Roughness;
		s.Metalness = m.Metalness;
		s.Reflectance = g_flReflectance;
		s.AmbientOcclusion = m.AmbientOcclusion;
		s.Emission = m.Emission;
		s.LightmapUV = m.LightmapUV;
		s.SpecularDirect = g_flSpecularDirect > 0.5;
		s.SpecularIndirect = g_flSpecularIndirect > 0.5;

		return float4( CS2Shade( s ), m.Opacity );
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
		float4 tintMask = g_tTintMask.Sample( g_sAniso, colorUv );
		float4 metalnessTexel = g_tMetalness.Sample( g_sAniso, colorUv );
		float4 detailMask = g_tDetailMask.Sample( g_sAniso, colorUv );
		float4 normalTexel = g_tNormal.Sample( g_sAniso, i.vNormalDetailUv.xy );
		float4 detail = g_tDetail.Sample( g_sAniso, i.vNormalDetailUv.zw );

		float ao = g_tAmbientOcclusion.Sample( g_sAniso, i.vColorUv.zw ).r;
		if ( g_flAmbientOcclusionScale > 0.0 )
			ao = min( lerp( 1.0, 2.0 * ao, g_flAmbientOcclusionScale ), 1.0 );

		//
		// Albedo: the tint where the mask (or the colour's alpha) allows, then the mod2x detail
		//
		float tintGate = saturate( ( g_flTintMask > 0.5 ? tintMask.r : color.a ) + g_flInverseBlendTint );
		float3 albedo = lerp( color.rgb, color.rgb * i.vTint.rgb, tintGate );
		albedo *= lerp( 1.0, detail.rgb * 1.99215686, max( detailMask.r, g_flDetailBlendToFull ) * g_flDetailBlendFactor );

		float metalness = metalnessTexel.g;

		//
		// Normal: tangent space from the vertex frame, the bitangent signed by the tangent's w, flipped on back faces
		//
		bool flip = g_flFlipBackfaceNormals > 0.5 && !isFrontFace;
		float3 vertexNormal = normalize( i.vNormalWs.xyz ) * ( flip ? -1.0 : 1.0 );
		float3 tangent = i.vTangentUWs.xyz;
		float3 bitangent = i.vTangentVWs.xyz;

		float3 normalTs = DecodeHemiOctahedralNormal( normalTexel.xy );
		if ( !flip )
			normalTs.y = -normalTs.y;

		float3 normalWs = normalize( tangent * normalTs.x + bitangent * normalTs.y + vertexNormal * normalTs.z );

		// Roughness from the normal map's blue channel, raised where the surface normal changes quickly across the
		// pixel (geometric specular antialiasing).
		float3 dx = ddx( vertexNormal );
		float3 dy = ddy( vertexNormal );
		float roughness = max( normalTexel.b, pow( saturate( max( dot( dx, dx ), dot( dy, dy ) ) ), 0.333 ) );

		//
		// Self illumination
		//
		float3 selfIllum = g_tSelfIllumMask.Sample( g_sAniso, colorUv + frac( g_vSelfIllumScrollSpeed * g_flTime ) ).rgb * g_vSelfIllumTintScaled;
		float3 emission = lerp( selfIllum, selfIllum * albedo, g_flSelfIllumAlbedoFactor );

		float opacity = i.vTint.a;
		#if S_TRANSLUCENT || S_ALPHA_TEST
			opacity *= color.a * g_flOpacityScale;
		#endif

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
		m.Opacity = opacity;

		#if S_MODE_DEPTH
			return ShadingModelStandard::Shade( m );
		#else
			// Derivatives need uniform control flow, so the shadow receiver normal is taken before branching.
			float3 receiverNormal = ComputeShadowReceiverNormal( m.WorldPositionWithOffset );

			[branch]
			if ( CS2LightingEnabled() )
				return ShadeCS2( m, receiverNormal );

			return ShadingModelStandard::Shade( m );
		#endif
	}
}
