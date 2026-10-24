//=========================================================================================================================
// CS2's csgo_foliage.vfx: leaves, grass, bark, cloth and banners.
//
// From the game's compiled shader (decompiled with Source 2 Viewer: alpha tested transmissive leaves lit from the probes
// and from the lightmap, their depth pass, bark, the tint mask variant). The colour times the instance tint (through
// the tint mask with F_TINT_MASK), then the material's colour adjust matrix; the painted vertex colour's height
// gradient (red) brightens it and its ambient occlusion (alpha) darkens the baked light; the hemi-octahedral normal
// with roughness in blue, its contrast and the roughness' contrast and brightness. Always specular. With
// F_TRANSMISSIVE_BACKFACE_NDOTL, light from behind the leaf (the probes' ambient cube, the sun and lights on the
// reversed vertex normal) is seen through it in the transmissive colour, or the albedo with
// F_USE_ALBEDO_FOR_TRANSMISSIVE (cs2_lighting.hlsl). Alpha tested leaves cut out by the colour's alpha, faded at
// grazing angles and boosted with distance, with anti-aliased edges.
//
// Not ported (yet): the vertex animation (wind sway and flutter, and the tree animation), which is driven by the game's
// wind (env_wind) through per view constants not traced yet; so foliage stands still. Also F_FOLIAGE_UV_ANIMATION and
// g_bMaskRoughnessAdjustmentsByTintMask (one material).
//=========================================================================================================================

HEADER
{
	Description = "CS2 csgo_foliage";
}

FEATURES
{
	#include "common/features.hlsl"
	Feature( F_ALPHA_TEST, 0..1, "Translucent" );
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
	float4 vPaint : TEXCOORD9;		// the painted vertex colour: height gradient (r), ambient occlusion (a)
	float4 vTint : TEXCOORD10;		// the instance tint, linear, with the colour tint applied; alpha in w
};

VS
{
	#include "cs2_vertex.hlsl"

	// The material's texture coordinate transform, rows of a 2x3 matrix (x, y, unused, offset), and scroll speed.
	float4 g_vTexCoordXform0 < Default4( 1, 0, 0, 0 ); >;
	float4 g_vTexCoordXform1 < Default4( 0, 1, 0, 0 ); >;
	float2 g_vTexCoordScrollSpeed < Default2( 0, 0 ); >;

	// The colour tint (linear) and how much of the instance tint applies.
	float3 g_vColorTint < Default3( 1, 1, 1 ); >;
	float g_flModelTintAmount < Default( 1 ); >;

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessCS2Vertex( v );

		float2 uv = v.vTexCoord.xy;
		i.vTextureCoords.xy = float2( dot( uv, g_vTexCoordXform0.xy ) + g_vTexCoordXform0.w, dot( uv, g_vTexCoordXform1.xy ) + g_vTexCoordXform1.w ) + g_vTexCoordScrollSpeed * g_flTime;

		i.vPaint = v.vMeshColor;

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
	CreateInputTexture2D( CS2TransmissiveColor, Srgb, 8, "None", "_color", "Transmission", Default4( 1, 1, 1, 0 ) );

	Texture2D g_tColor < Channel( RGBA, Box( CS2Color ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tNormal < Channel( RGBA, Box( CS2Normal ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tAmbientOcclusion < Channel( RGBA, Box( CS2AmbientOcclusion ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tTintMask < Channel( RGBA, Box( CS2TintMask ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tTransmissiveColor < Channel( RGBA, Box( CS2TransmissiveColor ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;

	TextureAttribute( RepresentativeTexture, g_tColor );
	TextureAttribute( LightSim_DiffuseAlbedoTexture, g_tColor );

	//
	// Parameters, as the game's shader computes them from the material (MaterialImporter in the mount)
	//

	// F_TINT_MASK: the tint mask's red channel gates the instance tint.
	float g_flTintMask < Default( 0 ); >;

	// Rows of the colour adjust matrices (MatrixColorTint2 times MatrixColorCorrect2, from the material), applied as
	// dot( row, float4( rgb, 1 ) ): the colour's, and the transmissive colour's.
	float4 g_vTextureColorAdjust0 < Default4( 1, 0, 0, 0 ); >;
	float4 g_vTextureColorAdjust1 < Default4( 0, 1, 0, 0 ); >;
	float4 g_vTextureColorAdjust2 < Default4( 0, 0, 1, 0 ); >;
	float4 g_vTransmissiveColorAdjust0 < Default4( 1, 0, 0, 0 ); >;
	float4 g_vTransmissiveColorAdjust1 < Default4( 0, 1, 0, 0 ); >;
	float4 g_vTransmissiveColorAdjust2 < Default4( 0, 0, 1, 0 ); >;

	float g_fTextureNormalContrast < Default( 1 ); >;
	float g_fTextureRoughnessContrast < Default( 1 ); >;
	float g_fTextureRoughnessBrightness < Default( 1 ); >;

	float g_flReflectance < Default( 0.04 ); >;

	// The painted vertex colour: alpha darkens the baked light, red brightens the albedo.
	float g_flVertexAmbientOcclusionAmount < Default( 0 ); >;
	float g_flVertexAmbientOcclusionPower < Default( 1 ); >;
	float g_flHeightGradientAmount < Default( 0 ); >;
	float g_flHeightGradientPower < Default( 1 ); >;

	// F_TRANSMISSIVE_BACKFACE_NDOTL; F_USE_ALBEDO_FOR_TRANSMISSIVE; g_bApplyColorCorrectionToTransmissiveColor;
	// not F_DISABLE_TRANSMISSIVE_SHADOWS.
	float g_flTransmissive < Default( 0 ); >;
	float g_flUseAlbedoForTransmissive < Default( 0 ); >;
	float g_flTransmissiveColorCorrection < Default( 0 ); >;
	float g_flTransmissiveShadows < Default( 1 ); >;

	// Alpha test: the colour's alpha faded at grazing angles (by this power of the view's angle to the face) and
	// boosted up to the strength with distance, up to the boost distance.
	float g_flGrazingAngleMask < Default( 0 ); >;
	float g_flAlphaBoostStrength < Default( 3 ); >;
	float g_flAlphaBoostDistance < Default( 20000 ); >;

	// F_RENDER_BACKFACES without F_DONT_FLIP_BACKFACE_NORMALS: back faces light with the normal flipped.
	float g_flFlipBackfaceNormals < Default( 0 ); >;

	float g_flFogEnabled < Default( 1 ); >;

	// The game's hemi-octahedral normal decode: x + y and x - y, folded onto the upper hemisphere.
	float3 DecodeHemiOctahedralNormal( float2 encoded )
	{
		float2 t = float2( encoded.x + encoded.y - 1.003921628, encoded.x - encoded.y );
		return normalize( float3( t, 1.0 - abs( t.x ) - abs( t.y ) ) );
	}

	float3 ColorAdjust( float3 rgb, float4 row0, float4 row1, float4 row2 )
	{
		float4 c = float4( rgb, 1.0 );
		return float3( dot( row0, c ), dot( row1, c ), dot( row2, c ) );
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		#if S_RENDER_BACKFACES
			bool isFrontFace = i.face;
		#else
			bool isFrontFace = true;
		#endif

		float2 uv = i.vTextureCoords.xy;
		float4 color = g_tColor.Sample( g_sAniso, uv );

		//
		// Alpha test: by the colour's alpha, faded where the face turns edge on to the view and boosted with distance
		//
		float alpha = 1.0;
		#if S_ALPHA_TEST
		{
			float3 toPixel = i.vPositionWithOffsetWs.xyz;
			float3 faceNormal = normalize( cross( ddx( toPixel ), ddy( toPixel ) ) );
			float facing = pow( max( abs( dot( normalize( toPixel ), faceNormal ) ), 1e-6 ), g_flGrazingAngleMask );
			alpha = color.a * facing * lerp( 1.0, g_flAlphaBoostStrength, saturate( length( toPixel ) / g_flAlphaBoostDistance ) );

			#if !S_MODE_DEPTH
				// Anti-aliased edges (the game's branch for this depends on a per view setting; unverified which).
				alpha = saturate( 0.5 + ( alpha - g_flAlphaTestReference ) / max( fwidth( alpha ), 1e-6 ) );
				if ( alpha - 0.001 < 0.0 )
					discard;
			#endif
		}
		#endif

		//
		// Albedo: the tint (through the mask), the colour adjust, the painted height gradient
		//
		float4 paint = clamp( i.vPaint, 0.001, 1.0 );

		float3 tinted = color.rgb * i.vTint.rgb;
		if ( g_flTintMask > 0.5 )
			tinted = lerp( color.rgb, tinted, g_tTintMask.Sample( g_sAniso, uv ).r );

		float3 adjusted = max( ColorAdjust( tinted, g_vTextureColorAdjust0, g_vTextureColorAdjust1, g_vTextureColorAdjust2 ), 0.0 );
		float3 albedo = adjusted * ( lerp( 0.0, pow( paint.r, g_flHeightGradientPower ), g_flHeightGradientAmount ) + 1.0 );

		float ao = g_tAmbientOcclusion.Sample( g_sAniso, uv ).r * lerp( 1.0, pow( paint.a, g_flVertexAmbientOcclusionPower ), g_flVertexAmbientOcclusionAmount );

		float3 transmission = 0;
		[branch]
		if ( g_flTransmissive > 0.5 )
		{
			if ( g_flUseAlbedoForTransmissive > 0.5 )
			{
				transmission = adjusted;
			}
			else
			{
				transmission = g_tTransmissiveColor.Sample( g_sAniso, uv ).rgb;
				if ( g_flTransmissiveColorCorrection > 0.5 )
					transmission = ColorAdjust( transmission, g_vTransmissiveColorAdjust0, g_vTransmissiveColorAdjust1, g_vTransmissiveColorAdjust2 );
				transmission *= i.vTint.rgb;
			}
		}

		//
		// Normal: tangent space from the vertex frame, flipped on back faces. The game's normal contrast path scales x
		// and y without the y flip the plain path has (in either facing).
		//
		float4 normalTexel = g_tNormal.Sample( g_sAniso, uv );

		bool flip = g_flFlipBackfaceNormals > 0.5 && !isFrontFace;
		float3 vertexNormal = normalize( i.vNormalWs.xyz ) * ( flip ? -1.0 : 1.0 );
		float3 tangent = i.vTangentUWs.xyz;
		float3 bitangent = i.vTangentVWs.xyz;

		float3 normalTs = DecodeHemiOctahedralNormal( normalTexel.xy );
		if ( g_fTextureNormalContrast != 1.0 )
			normalTs.xy *= g_fTextureNormalContrast;
		else if ( !flip )
			normalTs.y = -normalTs.y;

		float3 normalWs = normalize( tangent * normalTs.x + bitangent * normalTs.y + vertexNormal * normalTs.z );

		// Roughness from the normal map's blue channel through its contrast and brightness, raised where the surface
		// normal changes quickly across the pixel (geometric specular antialiasing).
		float3 dx = ddx( vertexNormal );
		float3 dy = ddy( vertexNormal );
		float roughness = max( saturate( ( ( normalTexel.b - 0.5 ) * g_fTextureRoughnessContrast + 0.5 ) * g_fTextureRoughnessBrightness ), pow( saturate( max( dot( dx, dx ), dot( dy, dy ) ) ), 0.333 ) );

		Material m = Material::Init( i.vPositionWithOffsetWs, i.vPositionSs );
		m.Albedo = albedo;
		m.Normal = normalWs;
		m.TangentNormal = normalTs;
		m.GeometricNormal = vertexNormal;
		m.WorldTangentU = tangent;
		m.WorldTangentV = bitangent;
		m.LightmapUV = i.vLightmapUV;
		m.TextureCoords = uv;
		m.Roughness = roughness;
		m.Metalness = 0;
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
			s.Reflectance = g_flReflectance;
			s.AmbientOcclusion = ao;
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
