//=========================================================================================================================
// CS2's csgo_simple_2way_blend.vfx: two materials blended by a mask and the painted blend value (walls, cobblestone,
// wet concrete).
//
// From the game's compiled shader (decompiled with Source 2 Viewer: its lightmapped variants with and without tint
// masks, ambient occlusion and painted vertex colours). Material A at the transformed coordinates; B, its normal map
// and the mask at those times g_vTexCoordScale2. The blend is smoothstep( max( 0, mask - softness ),
// min( 1, mask + softness ), painted blend ), the painted blend values' x and their w (at least 0.1) the softness. Both
// colours take the tint (the instance tint faded in by the model tint amount, times the colour tint and, with
// F_PAINT_VERTEX_COLORS, the painted colour unless it's all zero), gated by the tint mask's red for A and green for B
// with F_ENABLE_TINT_MASKS. Colours and hemi-octahedral normal maps (roughness in blue) blend by it, metalness between
// the two constants; F_ENABLE_AMBIENT_OCCLUSION reads the blended colour's alpha.
//
// Lit as the world shaders are (cs2_lighting.hlsl's CS2Shade, the same composite and environment fit as this variant):
// direct and indirect specular always on. The game's S_SHADER_QUALITY variant (GGX, the split sum table, directional
// lightmaps, blended cubemap volumes) isn't ported, as in the other world shaders. Not ported either: scaling texture
// coordinates by the model's scale (g_nScaleTexCoord*ByModelScaleAxis), the texture address modes (wrap here; every
// material checked uses 0), the screen space ambient occlusion.
//=========================================================================================================================

HEADER
{
	Description = "CS2 csgo_simple_2way_blend";
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
	float4 vColorUv : TEXCOORD5;		// xy: material A's coordinates; zw: material B's and the mask's
	float4 vBlendValues : TEXCOORD9;	// the painted blend values; w: the blend softness
	float4 vTint : TEXCOORD10;			// the tint, linear; alpha in w
};

VS
{
	#include "cs2_vertex.hlsl"

	float4 g_vTexCoordXform0 < Default4( 1, 0, 0, 0 ); >;
	float4 g_vTexCoordXform1 < Default4( 0, 1, 0, 0 ); >;
	float2 g_vTexCoordScrollSpeed < Default2( 0, 0 ); >;
	float2 g_vTexCoordScale2 < Default2( 1, 1 ); >;

	float3 g_vColorTint < Default3( 1, 1, 1 ); >;
	float g_flModelTintAmount < Default( 1 ); >;

	// F_PAINT_VERTEX_COLORS: the painted colour multiplies the tint.
	float g_flPaintVertexColors < Default( 0 ); >;

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessCS2Vertex( v );

		float2 uv = v.vTexCoord.xy;
		float2 colorUv = float2( dot( uv, g_vTexCoordXform0.xy ) + g_vTexCoordXform0.w, dot( uv, g_vTexCoordXform1.xy ) + g_vTexCoordXform1.w );
		colorUv += g_vTexCoordScrollSpeed * g_flTime;
		i.vColorUv = float4( colorUv, colorUv * g_vTexCoordScale2 );

		float4 instance = i.vVertexColor * v.vMeshTint;
		float4 tint = float4( lerp( 1.0, SrgbGammaToLinear( instance.rgb ), g_flModelTintAmount ) * g_vColorTint, instance.a );
		if ( g_flPaintVertexColors > 0.5 && any( v.vMeshColor != 0.0 ) )
			tint *= v.vMeshColor;

		i.vTint = tint;
		i.vBlendValues = float4( v.vBlendParams.xyz, max( v.vBlendParams.w, 0.1 ) );
		return FinalizeCS2Vertex( i );
	}
}

PS
{
	// sbox_pixel supplies culling; suppress the duplicate declaration in vr_common_ps_code.
	#define CULL_MODE_ALREADY_SET
	#include "common/pixel.hlsl"
	#include "common/classes/Light.hlsl"
	#include "cs2_lighting.hlsl"

	CreateInputTexture2D( CS2ColorA, Srgb, 8, "None", "_color", "Material A", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( CS2NormalA, Linear, 8, "None", "_normal", "Material A", Default4( 0.5, 0.5, 0.5, 0 ) );
	CreateInputTexture2D( CS2ColorB, Srgb, 8, "None", "_color", "Material B", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( CS2NormalB, Linear, 8, "None", "_normal", "Material B", Default4( 0.5, 0.5, 0.5, 0 ) );
	CreateInputTexture2D( CS2Mask, Linear, 8, "None", "_mask", "Mask", Default4( 1, 1, 1, 0 ) );
	CreateInputTexture2D( CS2TintMask, Linear, 8, "None", "_mask", "Color", Default4( 1, 1, 1, 0 ) );

	Texture2D g_tColorA < Channel( RGBA, Box( CS2ColorA ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tNormalA < Channel( RGBA, Box( CS2NormalA ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tColorB < Channel( RGBA, Box( CS2ColorB ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tNormalB < Channel( RGBA, Box( CS2NormalB ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tMask < Channel( RGBA, Box( CS2Mask ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tTintMask < Channel( RGBA, Box( CS2TintMask ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;

	TextureAttribute( RepresentativeTexture, g_tColorA );
	TextureAttribute( LightSim_DiffuseAlbedoTexture, g_tColorA );

	float g_flMetalnessA < Default( 0 ); >;
	float g_flMetalnessB < Default( 0 ); >;
	float g_flReflectance < Default( 0.04 ); >;

	// F_ENABLE_TINT_MASKS and F_ENABLE_AMBIENT_OCCLUSION.
	float g_flTintMasks < Default( 0 ); >;
	float g_flAmbientOcclusion < Default( 0 ); >;

	float g_flFogEnabled < Default( 1 ); >;

	// The game's hemi-octahedral normal decode: x + y and x - y, folded onto the upper hemisphere.
	float3 DecodeHemiOctahedralNormal( float2 encoded )
	{
		float2 t = float2( encoded.x + encoded.y - 1.003921628, encoded.x - encoded.y );
		return normalize( float3( t, 1.0 - abs( t.x ) - abs( t.y ) ) );
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float4 colorA = g_tColorA.Sample( g_sAniso, i.vColorUv.xy );
		float4 colorB = g_tColorB.Sample( g_sAniso, i.vColorUv.zw );
		float4 normalA = g_tNormalA.Sample( g_sAniso, i.vColorUv.xy );
		float4 normalB = g_tNormalB.Sample( g_sAniso, i.vColorUv.zw );
		float mask = g_tMask.Sample( g_sAniso, i.vColorUv.zw ).r;

		float softness = i.vBlendValues.w;
		float blend = smoothstep( max( 0.0, mask - softness ), min( 1.0, mask + softness ), i.vBlendValues.x );

		// The tint, gated per material by the tint mask's red and green.
		float2 tintGate = 1.0;
		if ( g_flTintMasks > 0.5 )
			tintGate = g_tTintMask.Sample( g_sAniso, i.vColorUv.xy ).rg;

		colorA.rgb = lerp( colorA.rgb, colorA.rgb * i.vTint.rgb, tintGate.x );
		colorB.rgb = lerp( colorB.rgb, colorB.rgb * i.vTint.rgb, tintGate.y );

		float4 color = lerp( colorA, colorB, blend );
		float4 normalTexel = lerp( normalA, normalB, blend );
		float metalness = lerp( g_flMetalnessA, g_flMetalnessB, blend );
		float ao = g_flAmbientOcclusion > 0.5 ? color.a : 1.0;

		//
		// Normal: tangent space from the vertex frame, the bitangent signed by the tangent's w
		//
		float3 vertexNormal = normalize( i.vNormalWs.xyz );
		float3 tangent = i.vTangentUWs.xyz;
		float3 bitangent = i.vTangentVWs.xyz;

		float3 normalTs = DecodeHemiOctahedralNormal( normalTexel.xy );
		normalTs.y = -normalTs.y;

		float3 normalWs = normalize( tangent * normalTs.x + bitangent * normalTs.y + vertexNormal * normalTs.z );

		// Roughness from the blended normal maps' blue channel, with geometric specular antialiasing.
		float3 dx = ddx( vertexNormal );
		float3 dy = ddy( vertexNormal );
		float roughness = max( normalTexel.b, pow( saturate( max( dot( dx, dx ), dot( dy, dy ) ) ), 0.333 ) );

		Material m = Material::Init( i.vPositionWithOffsetWs, i.vPositionSs );
		m.Albedo = color.rgb;
		m.Normal = normalWs;
		m.TangentNormal = normalTs;
		m.GeometricNormal = vertexNormal;
		m.WorldTangentU = tangent;
		m.WorldTangentV = bitangent;
		m.LightmapUV = i.vLightmapUV;
		m.TextureCoords = i.vColorUv.xy;
		m.Roughness = saturate( roughness );
		m.Metalness = saturate( metalness );
		m.AmbientOcclusion = ao;
		m.Opacity = i.vTint.a;

		#if S_MODE_DEPTH
			return ShadingModelStandard::Shade( m );
		#else
			// Derivatives need uniform control flow, so the shadow receiver normal is taken before branching.
			float3 receiverNormal = ComputeShadowReceiverNormal( m.WorldPositionWithOffset );

			[branch]
			if ( CS2LightingEnabled() )
			{
				Decals::Apply( m.WorldPosition, m );

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
				s.LightmapUV = m.LightmapUV;
				s.SpecularDirect = true;
				s.SpecularIndirect = true;
				s.Fog = g_flFogEnabled > 0.5;

				return float4( CS2Shade( s ), m.Opacity );
			}

			return ShadingModelStandard::Shade( m );
		#endif
	}
}
