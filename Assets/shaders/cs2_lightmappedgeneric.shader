//=========================================================================================================================
// CS2's csgo_lightmappedgeneric.vfx: the Source 1 style world shader of brushes, blends (WorldVertexTransition) and
// world overlays.
//
// From the game's compiled shader (decompiled with Source 2 Viewer: the plain, specular, alpha tested, translucent and
// overlay variants, layered with each fancy blending mode, with layer 1 and both layers' detail textures, and with
// texture transforms). Layer 1's colour and tint, blended towards layer 2's by the painted blend value, either as is,
// through a blend modulation texture's range, or against its height with a soft edge, a tinted border and a bevel bent
// into the normal; the mod2x detail textures; the hemi-octahedral normal and roughness and the ambient occlusion,
// blended the same way; the instance tint; a uniform metalness. Lit by cs2_lighting.hlsl like the game: the
// lightmaps, the sun and lights, the view's cubemap with F_SPECULAR_INDIRECT, their specular with F_SPECULAR_DIRECT.
//
// Static combos that only change what the shader reads are uniforms here (the mount sets them from the material's
// F_* values), so one compile covers them; the ones that change render state stay combos.
//
// The game keeps overlays in front of the surfaces under them with a depth bias (-64); s&box's depth is reversed, so
// here they are pulled a little towards the camera along the view ray instead, as cs2_static_overlay does.
//
// Not ported (no material uses them): F_ADDBUMPMAPS, F_DETAILBLENDMODE 1, F_METALNESS_TEXTURE. The game's screen
// space ambient occlusion term on the lightmap isn't either (as in cs2_vertexlitgeneric).
//=========================================================================================================================

HEADER
{
	Description = "CS2 csgo_lightmappedgeneric";
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
	float4 vLayer1Uv : TEXCOORD5;		// layer 1's colour (xy) and normal (zw) texture coordinates
	float4 vLayer2Uv : TEXCOORD8;		// layer 2's colour (xy) and normal (zw) texture coordinates
	float4 vDetailUv : TEXCOORD9;		// layer 1's (xy) and layer 2's (zw) detail texture coordinates
	float4 vTint : TEXCOORD10;			// the instance tint, linear, with the colour tint applied; alpha in w
	float4 vBlendUv : TEXCOORD12;		// blend modulation texture coordinates (xy), the painted blend to layer 2 (z)
};

VS
{
	#include "cs2_vertex.hlsl"

	// How far, as a fraction of the distance to the camera, overlays are pulled towards it (the game's depth bias).
	#define OVERLAY_DEPTH_OFFSET 0.0005

	// F_TEXTURETRANSFORMS: rows of a 2x3 matrix (x, y, unused, offset) each, as the game's xform expressions compute
	// them; identity without it. Layer 1's colour coordinates come from the mesh's, everything else from those.
	float4 g_vLayer1TexCoordXform0 < Default4( 1, 0, 0, 0 ); >;
	float4 g_vLayer1TexCoordXform1 < Default4( 0, 1, 0, 0 ); >;
	float4 g_vLayer1NormalTexCoordXform0 < Default4( 1, 0, 0, 0 ); >;
	float4 g_vLayer1NormalTexCoordXform1 < Default4( 0, 1, 0, 0 ); >;
	float4 g_vLayer2TexCoordXform0 < Default4( 1, 0, 0, 0 ); >;
	float4 g_vLayer2TexCoordXform1 < Default4( 0, 1, 0, 0 ); >;
	float4 g_vLayer2NormalTexCoordXform0 < Default4( 1, 0, 0, 0 ); >;
	float4 g_vLayer2NormalTexCoordXform1 < Default4( 0, 1, 0, 0 ); >;
	float4 g_vBlendModulateTexCoordXform0 < Default4( 1, 0, 0, 0 ); >;
	float4 g_vBlendModulateTexCoordXform1 < Default4( 0, 1, 0, 0 ); >;

	float2 g_vLayer1DetailScale < Default2( 4, 4 ); >;
	float2 g_vLayer2DetailScale < Default2( 4, 4 ); >;

	// The colour tint (linear) and how much of the instance tint applies.
	float3 g_vColorTint < Default3( 1, 1, 1 ); >;
	float g_flModelTintAmount < Default( 1 ); >;
	float g_flVertexColorOpacityScale < Default( 1 ); >;

	// F_OVERLAY
	float g_flOverlay < Default( 0 ); >;

	float2 Transform( float2 uv, float4 row0, float4 row1 )
	{
		return float2( dot( uv, row0.xy ) + row0.w, dot( uv, row1.xy ) + row1.w );
	}

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessCS2Vertex( v );

		if ( g_flOverlay > 0.5 )
		{
			float3 toCamera = g_vCameraPositionWs - i.vPositionWs.xyz;
			i.vPositionWs.xyz += toCamera * OVERLAY_DEPTH_OFFSET;
			i.vPositionPs = Position3WsToPs( i.vPositionWs.xyz );
		}

		float2 uv = Transform( v.vTexCoord.xy, g_vLayer1TexCoordXform0, g_vLayer1TexCoordXform1 );

		i.vLayer1Uv = float4( uv, Transform( uv, g_vLayer1NormalTexCoordXform0, g_vLayer1NormalTexCoordXform1 ) );
		i.vLayer2Uv = float4( Transform( uv, g_vLayer2TexCoordXform0, g_vLayer2TexCoordXform1 ), Transform( uv, g_vLayer2NormalTexCoordXform0, g_vLayer2NormalTexCoordXform1 ) );
		i.vDetailUv = float4( uv * g_vLayer1DetailScale, uv * g_vLayer2DetailScale );
		i.vBlendUv = float4( Transform( uv, g_vBlendModulateTexCoordXform0, g_vBlendModulateTexCoordXform1 ), v.vBlendParams.x, 0 );

		// The instance colour is gamma space; the game linearizes it, fades it in by the model tint amount, then
		// multiplies the material's colour tint in.
		float4 instance = i.vVertexColor * v.vMeshTint;
		i.vTint = float4( lerp( 1.0, SrgbGammaToLinear( instance.rgb ), g_flModelTintAmount ) * g_vColorTint, instance.a * g_flVertexColorOpacityScale );

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
	CreateInputTexture2D( CS2Color, Srgb, 8, "None", "_color", "Layer 1", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( CS2Layer1NormalRoughness, Linear, 8, "None", "_normal", "Layer 1", Default4( 0.5, 0.5, 1, 0 ) );
	CreateInputTexture2D( CS2Layer1AmbientOcclusion, Linear, 8, "None", "_ao", "Layer 1", Default4( 1, 1, 1, 0 ) );
	CreateInputTexture2D( CS2Layer1Detail, Linear, 8, "None", "_detail", "Layer 1", Default4( 0.5, 0.5, 0.5, 0 ) );
	CreateInputTexture2D( CS2Layer2Color, Srgb, 8, "None", "_color", "Layer 2", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( CS2Layer2NormalRoughness, Linear, 8, "None", "_normal", "Layer 2", Default4( 0.5, 0.5, 1, 0 ) );
	CreateInputTexture2D( CS2Layer2AmbientOcclusion, Linear, 8, "None", "_ao", "Layer 2", Default4( 1, 1, 1, 0 ) );
	CreateInputTexture2D( CS2Layer2Detail, Linear, 8, "None", "_detail", "Layer 2", Default4( 0.5, 0.5, 0.5, 0 ) );
	CreateInputTexture2D( CS2BlendModulation, Linear, 8, "None", "_mask", "Blend Modulation", Default4( 0, 0.5, 0, 0.5 ) );

	Texture2D g_tColor < Channel( RGBA, Box( CS2Color ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tLayer1NormalRoughness < Channel( RGBA, Box( CS2Layer1NormalRoughness ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tLayer1AmbientOcclusion < Channel( RGBA, Box( CS2Layer1AmbientOcclusion ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tLayer1Detail < Channel( RGBA, Box( CS2Layer1Detail ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tLayer2Color < Channel( RGBA, Box( CS2Layer2Color ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tLayer2NormalRoughness < Channel( RGBA, Box( CS2Layer2NormalRoughness ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tLayer2AmbientOcclusion < Channel( RGBA, Box( CS2Layer2AmbientOcclusion ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tLayer2Detail < Channel( RGBA, Box( CS2Layer2Detail ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tBlendModulation < Channel( RGBA, Box( CS2BlendModulation ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;

	TextureAttribute( RepresentativeTexture, g_tColor );
	TextureAttribute( LightSim_DiffuseAlbedoTexture, g_tColor );

	//
	// Parameters, as the game's shader computes them from the material (MaterialImporter in the mount)
	//

	// F_LAYERS: layer 2 exists. F_FANCY_BLENDING: 0 the painted blend as is, 1 through the blend modulation texture's
	// range (r) around its midpoint (g), 2 and 3 against its height in g or a, with soft edges, borders and bevels.
	float g_flLayers < Default( 0 ); >;
	float g_flFancyBlendingMode < Default( 0 ); >;

	float3 g_vLayer1Tint < Default3( 1, 1, 1 ); >;
	float3 g_vLayer2Tint < Default3( 1, 1, 1 ); >;

	float g_flBlendSoftness < Default( 0.5 ); >;
	float g_flLayerBorderStrength < Default( 0.5 ); >;
	float g_flLayerBorderOffset < Default( 0 ); >;	// the material's, negated
	float g_flLayerBorderSoftness < Default( 0.5 ); >;
	float3 g_vLayerBorderTint < Default3( 1, 1, 1 ); >;
	float g_flBevelBlendWidth < Default( 4 ); >;
	float g_flBevelBlendSharpness < Default( 2 ); >;
	float g_flBevelBlendStrength < Default( 0 ); >;

	// F_DETAILTEXTURE: 0 none, 1 layer 1's (on both layers), 2 each layer's. Tint in xyz, blend amount in w.
	float g_flDetailTexture < Default( 0 ); >;
	float4 g_vLayer1DetailTintAndBlend < Default4( 1, 1, 1, 1 ); >;
	float4 g_vLayer2DetailTintAndBlend < Default4( 1, 1, 1, 1 ); >;

	float g_flMetalness < Default( 0 ); >;
	float g_flReflectance < Default( 0.04 ); >;

	// F_NO_SPECULAR_AT_FULL_ROUGHNESS: roughness near 1 is pushed beyond it.
	float g_flNoSpecularAtFullRoughness < Default( 0 ); >;

	// F_SPECULAR_DIRECT and F_SPECULAR_INDIRECT ($phong and $envmap in Source 1). Without them, diffuse only.
	float g_flSpecularDirect < Default( 0 ); >;
	float g_flSpecularIndirect < Default( 0 ); >;

	// F_OVERLAY: the alpha is taken down by the alpha test reference.
	float g_flOverlay < Default( 0 ); >;

	// F_RENDER_BACKFACES without F_DONT_FLIP_BACKFACE_NORMALS: back faces light with the normal flipped.
	float g_flFlipBackfaceNormals < Default( 0 ); >;

	float g_flFogEnabled < Default( 1 ); >;

	#if !S_ALPHA_TEST
		float g_flAlphaTestReference < Default( 0.01 ); >;
	#endif
	#if !S_TRANSLUCENT
		float g_flOpacityScale < Default( 1 ); >;
	#endif

	// The game's hemi-octahedral normal decode: x + y and x - y, folded onto the upper hemisphere.
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

		float2 uv1 = i.vLayer1Uv.xy;
		float4 color = g_tColor.Sample( g_sAniso, uv1 );

		//
		// Alpha: the colour's alpha times the instance alpha, twice over as the game's shader has it
		//
		float alpha = i.vTint.a;
		#if S_TRANSLUCENT
		{
			alpha = i.vTint.a * color.a * i.vTint.a * g_flOpacityScale;
			if ( g_flOverlay > 0.5 )
				alpha -= g_flAlphaTestReference;

			if ( alpha - 0.001 < 0.0 )
				discard;
		}
		#elif S_ALPHA_TEST && !S_MODE_DEPTH
		{
			alpha = color.a * i.vTint.a * i.vTint.a;

			// Anti-aliased edges: the alpha sharpened around the reference, blended back towards the plain alpha as
			// the texture is minified (the game's branch for this depends on a per view setting; unverified which).
			alpha = lerp( saturate( 0.5 + ( alpha - g_flAlphaTestReference ) / max( fwidth( alpha ), 1e-6 ) ), alpha, lerp( 1.0, saturate( 4.0 * length( fwidth( uv1 ) ) ), g_flAntiAliasedEdgeStrength ) );
			if ( alpha - 0.001 < 0.0 )
				discard;
		}
		#elif S_ALPHA_TEST
			// The depth pass: s&box's shading model cuts it out against the same reference.
			alpha = color.a * i.vTint.a * i.vTint.a;
		#endif

		//
		// Blend towards layer 2: the painted value, through the blend modulation texture with fancy blending
		//
		int fancyBlending = (int)round( g_flFancyBlendingMode );
		int detailTextures = (int)round( g_flDetailTexture );

		float paint = i.vBlendUv.z;
		float blend = 0.0;
		float height = 0.0;
		float4 modulation = 0.0;

		bool layers = g_flLayers > 0.5;
		if ( layers )
		{
			blend = paint;

			[branch]
			if ( fancyBlending > 0 )
				modulation = g_tBlendModulation.Sample( g_sAniso, i.vBlendUv.xy );

			if ( fancyBlending == 1 )
			{
				blend = smoothstep( max( 0.0, modulation.y - modulation.x ), min( 1.0, modulation.y + modulation.x ), paint );
			}
			else if ( fancyBlending > 1 )
			{
				height = fancyBlending == 3 ? modulation.w : modulation.y;
				blend = smoothstep( max( 0.0, height - g_flBlendSoftness ), min( 1.0, height + g_flBlendSoftness ), paint );
			}
		}

		float3 albedo = color.rgb * g_vLayer1Tint;
		float4 normalRoughness = g_tLayer1NormalRoughness.Sample( g_sAniso, i.vLayer1Uv.zw );
		float ao = g_tLayer1AmbientOcclusion.Sample( g_sTrilinearWrap, uv1 ).r;

		[branch]
		if ( layers )
		{
			// Layer 1 tinted along the border where the blend meets its height.
			if ( fancyBlending > 1 )
			{
				float border = smoothstep( max( 0.0, height - g_flLayerBorderSoftness ), min( 1.0, height + g_flLayerBorderSoftness ), saturate( paint - g_flLayerBorderOffset ) );
				albedo *= lerp( 1.0, g_vLayerBorderTint, ( 1.0 - abs( border * 2.0 - 1.0 ) ) * g_flLayerBorderStrength );
			}

			float3 albedo2 = g_tLayer2Color.Sample( g_sAniso, i.vLayer2Uv.xy ).rgb * g_vLayer2Tint;
			albedo = lerp( albedo, albedo2, blend );
			normalRoughness = lerp( normalRoughness, g_tLayer2NormalRoughness.Sample( g_sAniso, i.vLayer2Uv.zw ), blend );
			ao = lerp( ao, g_tLayer2AmbientOcclusion.Sample( g_sTrilinearWrap, i.vLayer2Uv.xy ).r, blend );
		}

		//
		// Mod2x detail: layer 1's, or each layer's blended like the layers
		//
		[branch]
		if ( detailTextures > 0 )
		{
			float3 detail = g_tLayer1Detail.Sample( g_sAniso, i.vDetailUv.xy ).rgb * g_vLayer1DetailTintAndBlend.rgb;
			float amount = g_vLayer1DetailTintAndBlend.w;

			if ( detailTextures > 1 )
			{
				float3 detail2 = g_tLayer2Detail.Sample( g_sAniso, i.vDetailUv.zw ).rgb * g_vLayer2DetailTintAndBlend.rgb;
				detail = lerp( detail, detail2, blend );
				amount = lerp( amount, g_vLayer2DetailTintAndBlend.w, blend );
			}

			albedo *= lerp( 1.0, detail * 2.0, amount );
		}

		albedo *= i.vTint.rgb;

		//
		// Normal: tangent space from the vertex frame, the bitangent signed by the tangent's w, flipped on back faces
		//
		bool flip = g_flFlipBackfaceNormals > 0.5 && !isFrontFace;
		float3 vertexNormal = normalize( i.vNormalWs.xyz ) * ( flip ? -1.0 : 1.0 );
		float3 tangent = i.vTangentUWs.xyz;
		float3 bitangent = i.vTangentVWs.xyz;

		float3 normalTs = DecodeHemiOctahedralNormal( normalRoughness.xy );
		if ( !flip )
			normalTs.y = -normalTs.y;

		float3 normalWs = normalize( tangent * normalTs.x + bitangent * normalTs.y + vertexNormal * normalTs.z );

		// The bevel: where the blend crosses layer 2's height (over a wider soft edge), the surface bent as if layer 2
		// stood out of layer 1 along the vertex normal, its slope from the screen derivatives of that raised surface.
		float bevel = 0.0;
		if ( layers && fancyBlending > 1 )
		{
			float softness = g_flBlendSoftness * g_flBevelBlendWidth;
			bevel = smoothstep( max( 0.0, height - softness ), min( 1.0, height + softness ), paint );
		}

		float3 raised = i.vPositionWithOffsetWs.xyz + i.vNormalWs.xyz * ( bevel * g_flBevelBlendStrength );
		float3 bevelNormal = normalize( cross( ddy_fine( raised ), ddx_fine( raised ) ) );

		if ( layers && fancyBlending > 1 )
			normalWs = normalize( normalWs + bevelNormal * saturate( ( 1.0 - 2.0 * abs( 0.5 - bevel ) ) * g_flBevelBlendSharpness ) );

		// Roughness from the normal map's blue channel, raised where the surface normal changes quickly across the
		// pixel (geometric specular antialiasing).
		float roughness = normalRoughness.b;
		if ( g_flNoSpecularAtFullRoughness > 0.5 )
			roughness += smoothstep( 0.9, 1.0, roughness ) * 2.0;

		float3 dx = ddx( vertexNormal );
		float3 dy = ddy( vertexNormal );
		roughness = max( roughness, pow( saturate( max( dot( dx, dx ), dot( dy, dy ) ) ), 0.333 ) );

		Material m = Material::Init( i.vPositionWithOffsetWs, i.vPositionSs );
		m.Albedo = albedo;
		m.Normal = normalWs;
		m.TangentNormal = normalTs;
		m.GeometricNormal = vertexNormal;
		m.WorldTangentU = tangent;
		m.WorldTangentV = bitangent;
		m.LightmapUV = i.vLightmapUV;
		m.TextureCoords = uv1;
		m.Roughness = saturate( roughness );
		m.Metalness = g_flMetalness;
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
			s.TangentNormal = normalTs;
			s.GeometricNormal = vertexNormal;
			s.ShadowReceiverNormal = receiverNormal;
			s.Albedo = albedo;
			s.Roughness = roughness;
			s.Metalness = g_flMetalness;
			s.Reflectance = g_flReflectance;
			s.AmbientOcclusion = ao;
			s.LightmapUV = i.vLightmapUV;
			s.SpecularDirect = g_flSpecularDirect > 0.5;
			s.SpecularIndirect = g_flSpecularIndirect > 0.5;
			s.Fog = g_flFogEnabled > 0.5;

			return float4( CS2Shade( s ), alpha );
		#endif
	}
}
