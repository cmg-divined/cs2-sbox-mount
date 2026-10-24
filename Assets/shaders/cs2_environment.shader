//=========================================================================================================================
// CS2's csgo_environment.vfx and csgo_environment_blend.vfx: the world's layered materials, most of CS2's props and
// surfaces. csgo_environment is one layer; csgo_environment_blend paints up to three over each other by vertex paint
// and height.
//
// Layers follow the game's compiled shaders (decompiled with Source 2 Viewer): colour, height (tint mask in g,
// metalness in a), and hemi-octahedral normal with roughness in b, read on the mesh's first or second texture
// coordinates (a second set gets its tangent frame from screen derivatives) or projected biplanar in world space;
// the normal bent by derivative mapping; the tint mask gating the colour adjust matrix and the colorized instance
// tint; ambient occlusion from the colour's alpha through the layer's levels. csgo_environment's tint mask, roughness
// and normal contrast are baked into its textures, so it takes the blend shader's layer with neutral settings.
//
// The blending of layers 2 and 3 (both the height-band and the older normalized blend), the blend borders and bevels,
// the shared colour overlay and the vertex colour masking follow ValveResourceFormat's port, checked against the
// decompiled blend shaders' parameters and formulas.
//
// Lit by cs2_lighting.hlsl. Not ported (yet): wetness and puddles (driven by per view weather constants not traced
// yet), and texture coordinates scaled by the model's scale.
//=========================================================================================================================

HEADER
{
	Description = "CS2 csgo_environment / csgo_environment_blend";
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
	float4 vLayerUv12 : TEXCOORD5;		// layer 1 (xy) and 2 (zw) texture coordinates
	float4 vLayerUv3Overlay : TEXCOORD8;	// layer 3 (xy), shared colour overlay (zw)
	float4 vDetailUv12 : TEXCOORD9;		// detail normal coordinates of layers 1 (xy) and 2 (zw)
	float4 vDetailUv3Uv2 : TEXCOORD10;	// layer 3's detail coordinates (xy), the mesh's second coordinates (zw)
	float4 vTintModel : TEXCOORD12;		// instance tint, linear (rgb), and how much it applies (w)
	float4 vPaintColor : TEXCOORD13;	// colour tint times the painted vertex colour (rgb), instance alpha (w)
	float4 vBlendValues : TEXCOORD14;	// painted blend to layer 2 (x) and 3 (y), wetness (z), softness (w)
};

VS
{
	#include "cs2_vertex.hlsl"

	// Per layer: which texture coordinates (0 biplanar, 1 the first, 2 the second) and their transform, as the game's
	// xform expressions compute it.
	float g_flUVSet1 < Default( 1 ); >;
	float4 g_vTexCoordXform0_1 < Default4( 1, 0, 0, 0 ); >;
	float4 g_vTexCoordXform1_1 < Default4( 0, 1, 0, 0 ); >;
	float g_flUVSet2 < Default( 1 ); >;
	float4 g_vTexCoordXform0_2 < Default4( 1, 0, 0, 0 ); >;
	float4 g_vTexCoordXform1_2 < Default4( 0, 1, 0, 0 ); >;
	float g_flUVSet3 < Default( 1 ); >;
	float4 g_vTexCoordXform0_3 < Default4( 1, 0, 0, 0 ); >;
	float4 g_vTexCoordXform1_3 < Default4( 0, 1, 0, 0 ); >;

	// Detail normals: their own coordinate set (-1 follows the layer's) and transform.
	float g_flDetailUVSet1 < Default( -1 ); >;
	float4 g_vDetailTexCoordXform0_1 < Default4( 1, 0, 0, 0 ); >;
	float4 g_vDetailTexCoordXform1_1 < Default4( 0, 1, 0, 0 ); >;
	float g_flDetailUVSet2 < Default( -1 ); >;
	float4 g_vDetailTexCoordXform0_2 < Default4( 1, 0, 0, 0 ); >;
	float4 g_vDetailTexCoordXform1_2 < Default4( 0, 1, 0, 0 ); >;
	float g_flDetailUVSet3 < Default( -1 ); >;
	float4 g_vDetailTexCoordXform0_3 < Default4( 1, 0, 0, 0 ); >;
	float4 g_vDetailTexCoordXform1_3 < Default4( 0, 1, 0, 0 ); >;

	float g_flColorOverlayUVSet < Default( 2 ); >;
	float4 g_vOverlayTexCoordXform0 < Default4( 1, 0, 0, 0 ); >;
	float4 g_vOverlayTexCoordXform1 < Default4( 0, 1, 0, 0 ); >;

	float3 g_vColorTint < Default3( 1, 1, 1 ); >;
	float g_flModelTintAmount < Default( 1 ); >;

	// csgo_environment_blend: 1. Layer 3 present: 1.
	float g_flBlend < Default( 0 ); >;
	float g_flLayer3 < Default( 0 ); >;
	float g_flBlendSoftness2 < Default( 0.01 ); >;
	float g_flBlendSoftness3 < Default( 0.01 ); >;

	// Blend by facing direction: layer 2 or 3's paint faded by how much the surface faces a direction (mode 0 off).
	float g_flFacingMode2 < Default( 0 ); >;
	float3 g_vFacingDirection2 < Default3( 0, 0, 1 ); >;
	float2 g_vFacingDirectionMinMax2 < Default2( 0.4, 0.601 ); >;
	float g_flFacingMode3 < Default( 0 ); >;
	float3 g_vFacingDirection3 < Default3( 0, 0, 1 ); >;
	float2 g_vFacingDirectionMinMax3 < Default2( 0.4, 0.601 ); >;

	float2 Xform( float2 uv, float4 row0, float4 row1 )
	{
		return float2( dot( uv, row0.xy ) + row0.w, dot( uv, row1.xy ) + row1.w );
	}

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessCS2Vertex( v );

		float2 uv0 = v.vTexCoord.xy;
		float2 uv1 = v.vTexCoord2.xy;

		i.vLayerUv12.xy = Xform( g_flUVSet1 > 1.5 ? uv1 : uv0, g_vTexCoordXform0_1, g_vTexCoordXform1_1 );
		i.vLayerUv12.zw = Xform( g_flUVSet2 > 1.5 ? uv1 : uv0, g_vTexCoordXform0_2, g_vTexCoordXform1_2 );
		i.vLayerUv3Overlay.xy = Xform( g_flUVSet3 > 1.5 ? uv1 : uv0, g_vTexCoordXform0_3, g_vTexCoordXform1_3 );
		i.vLayerUv3Overlay.zw = Xform( g_flColorOverlayUVSet > 1.5 ? uv1 : uv0, g_vOverlayTexCoordXform0, g_vOverlayTexCoordXform1 );

		float detailSet1 = g_flDetailUVSet1 < -0.5 ? g_flUVSet1 : g_flDetailUVSet1;
		float detailSet2 = g_flDetailUVSet2 < -0.5 ? g_flUVSet2 : g_flDetailUVSet2;
		float detailSet3 = g_flDetailUVSet3 < -0.5 ? g_flUVSet3 : g_flDetailUVSet3;
		i.vDetailUv12.xy = Xform( detailSet1 > 1.5 ? uv1 : uv0, g_vDetailTexCoordXform0_1, g_vDetailTexCoordXform1_1 );
		i.vDetailUv12.zw = Xform( detailSet2 > 1.5 ? uv1 : uv0, g_vDetailTexCoordXform0_2, g_vDetailTexCoordXform1_2 );
		i.vDetailUv3Uv2.xy = Xform( detailSet3 > 1.5 ? uv1 : uv0, g_vDetailTexCoordXform0_3, g_vDetailTexCoordXform1_3 );
		i.vDetailUv3Uv2.zw = uv1;

		// The instance tint is gamma space; the game linearizes it, and applies it less the whiter its darkest channel.
		float4 instance = i.vVertexColor * v.vMeshTint;
		float3 tint = SrgbGammaToLinear( instance.rgb );
		i.vTintModel = float4( tint, g_flModelTintAmount * ( 1.0 - min( tint.r, min( tint.g, tint.b ) ) ) );

		// The painted vertex colour, faded in by its alpha.
		i.vPaintColor = float4( g_vColorTint * lerp( 1.0, v.vMeshColor.rgb, v.vMeshColor.a ), instance.a );

		float4 blend = v.vBlendParams;
		float3 normal = normalize( i.vNormalWs.xyz );

		if ( g_flBlend > 0.5 )
		{
			if ( g_flFacingMode2 > 0.5 )
				blend.x *= smoothstep( g_vFacingDirectionMinMax2.x, g_vFacingDirectionMinMax2.y, dot( g_vFacingDirection2, normal ) * 0.5 + 0.5 );

			float softness = g_flBlendSoftness2;
			if ( g_flLayer3 > 0.5 )
			{
				if ( blend.x < 0.001 )
					softness = g_flBlendSoftness3;

				softness = lerp( softness, g_flBlendSoftness3, blend.y );

				if ( g_flFacingMode3 > 0.5 && g_flFacingMode3 < 1.5 )
					blend.y *= smoothstep( g_vFacingDirectionMinMax3.x, g_vFacingDirectionMinMax3.y, dot( g_vFacingDirection3, normal ) * 0.5 + 0.5 );
			}

			blend.w = clamp( blend.w + softness, 0.001, 1.0 );
		}
		else
		{
			blend.w = clamp( blend.w, 0.001, 1.0 );
		}

		i.vBlendValues = blend;
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
	// Textures, with the game's defaults
	//
	CreateInputTexture2D( CS2Color1, Srgb, 8, "None", "_color", "Layer 1", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( CS2Height1, Linear, 8, "None", "_height", "Layer 1", Default4( 0.5, 1, 1, 0 ) );
	CreateInputTexture2D( CS2Normal1, Linear, 8, "None", "_normal", "Layer 1", Default4( 0.5, 0.5, 0.5, 0 ) );
	CreateInputTexture2D( CS2NormalDetail1, Linear, 8, "None", "_normal", "Layer 1", Default4( 0.5, 0.5, 0.5, 0 ) );
	CreateInputTexture2D( CS2Color2, Srgb, 8, "None", "_color", "Layer 2", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( CS2Height2, Linear, 8, "None", "_height", "Layer 2", Default4( 0.5, 1, 1, 0 ) );
	CreateInputTexture2D( CS2Normal2, Linear, 8, "None", "_normal", "Layer 2", Default4( 0.5, 0.5, 0.5, 0 ) );
	CreateInputTexture2D( CS2NormalDetail2, Linear, 8, "None", "_normal", "Layer 2", Default4( 0.5, 0.5, 0.5, 0 ) );
	CreateInputTexture2D( CS2Color3, Srgb, 8, "None", "_color", "Layer 3", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( CS2Height3, Linear, 8, "None", "_height", "Layer 3", Default4( 0.5, 1, 1, 0 ) );
	CreateInputTexture2D( CS2Normal3, Linear, 8, "None", "_normal", "Layer 3", Default4( 0.5, 0.5, 0.5, 0 ) );
	CreateInputTexture2D( CS2NormalDetail3, Linear, 8, "None", "_normal", "Layer 3", Default4( 0.5, 0.5, 0.5, 0 ) );
	CreateInputTexture2D( CS2SharedColorOverlay, Srgb, 8, "None", "_color", "Overlay", Default4( 0.5, 0.5, 0.5, 1 ) );

	Texture2D g_tColor1 < Channel( RGBA, Box( CS2Color1 ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tHeight1 < Channel( RGBA, Box( CS2Height1 ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tNormal1 < Channel( RGBA, Box( CS2Normal1 ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tNormalDetail1 < Channel( RGBA, Box( CS2NormalDetail1 ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tColor2 < Channel( RGBA, Box( CS2Color2 ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tHeight2 < Channel( RGBA, Box( CS2Height2 ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tNormal2 < Channel( RGBA, Box( CS2Normal2 ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tNormalDetail2 < Channel( RGBA, Box( CS2NormalDetail2 ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tColor3 < Channel( RGBA, Box( CS2Color3 ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tHeight3 < Channel( RGBA, Box( CS2Height3 ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tNormal3 < Channel( RGBA, Box( CS2Normal3 ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tNormalDetail3 < Channel( RGBA, Box( CS2NormalDetail3 ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tSharedColorOverlay < Channel( RGBA, Box( CS2SharedColorOverlay ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;

	TextureAttribute( RepresentativeTexture, g_tColor1 );
	TextureAttribute( LightSim_DiffuseAlbedoTexture, g_tColor1 );

	//
	// Per layer parameters (1, 2, 3), as the game's shader computes them from the material
	//
	#define LAYER_PARAMETERS( n ) \
		float g_flUVSetPs##n < Default( 1 ); >; \
		float2 g_vTexCoordScale##n < Default2( 1, 1 ); >; \
		float4 g_vNormalXform_##n < Default4( 1, 0, 0, 1 ); >; \
		float g_flDetailNormal##n < Default( 0 ); >; \
		float g_flDetailUVSetPs##n < Default( 1 ); >; \
		float2 g_vDetailTexCoordScale##n < Default2( 1, 1 ); >; \
		float4 g_vDetailNormalXform_##n < Default4( 1, 0, 0, 1 ); >; \
		float g_fDetailTextureNormalContrast##n < Default( 1 ); >; \
		float g_fTextureNormalContrast##n < Default( 1 ); >; \
		float g_fTextureRoughnessContrast##n < Default( 1 ); >; \
		float g_fTextureRoughnessBrightness##n < Default( 1 ); >; \
		float g_fTintMaskContrast##n < Default( 1 ); >; \
		float g_fTintMaskBrightness##n < Default( 1 ); >; \
		float3 g_vAmbientOcclusionLevels##n < Default3( 0, 1, 1 ); >; \
		float g_flMetalness##n < Default( 1 ); >; \
		float g_flModelTint##n < Default( 1 ); >; \
		float g_flAdjustBaseColor##n < Default( 0 ); >; \
		float4 g_vTextureAdjust##n##_0 < Default4( 1, 0, 0, 0 ); >; \
		float4 g_vTextureAdjust##n##_1 < Default4( 0, 1, 0, 0 ); >; \
		float4 g_vTextureAdjust##n##_2 < Default4( 0, 0, 1, 0 ); >; \
		float4 g_vTextureColorAdjust##n##_0 < Default4( 1, 0, 0, 0 ); >; \
		float4 g_vTextureColorAdjust##n##_1 < Default4( 0, 1, 0, 0 ); >; \
		float4 g_vTextureColorAdjust##n##_2 < Default4( 0, 0, 1, 0 ); >; \
		float g_flVertexColorMode##n < Default( 0 ); >; \
		float g_flHeightMapScale##n < Default( 1 ); >; \
		float g_flHeightMapZeroPoint##n < Default( 0.5 ); >; \
		float g_flSnowLayer##n < Default( 0 ); >;

	LAYER_PARAMETERS( 1 )
	LAYER_PARAMETERS( 2 )
	LAYER_PARAMETERS( 3 )

	#define BLEND_PARAMETERS( n ) \
		float g_flColorOverlay##n < Default( 0 ); >; \
		float g_flColorReplace##n < Default( 1 ); >; \
		float g_flNormalCombine##n < Default( 0 ); >; \
		float g_flNormalReplace##n < Default( 1 ); >; \
		float g_flRoughnessCombine##n < Default( 0 ); >; \
		float g_flRoughnessReplace##n < Default( 1 ); >; \
		float g_flAOCombine##n < Default( 0 ); >; \
		float g_flAOReplace##n < Default( 1 ); >; \
		float g_flUnderlyingHeightMapInfluence##n < Default( 1 ); >; \
		float g_flMaskWithHeight##n < Default( 0 ); >; \
		float g_flBlendEffects##n < Default( 0 ); >; \
		float g_flBorderBlendMode##n < Default( 0 ); >; \
		float g_flBorderRoughnessEnabled##n < Default( 0 ); >; \
		float g_flBorderSpread##n < Default( 0.1 ); >; \
		float g_flBorderSoftness##n < Default( 0.1 ); >; \
		float g_flBorderOffset##n < Default( 0 ); >; \
		float3 g_vBorderTint##n < Default3( 1, 1, 1 ); >; \
		float2 g_vBorderLayerAmount##n < Default2( 1, 0 ); >; \
		float g_flBorderTintMask##n < Default( 0 ); >; \
		float g_fBorderRoughness##n < Default( 0.5 ); >; \
		float g_flBevelSpread##n < Default( 0.1 ); >; \
		float g_flBevelSoftness##n < Default( 0.1 ); >; \
		float g_flBevelOffset##n < Default( 0 ); >; \
		float g_flBevelStrength##n < Default( 0 ); >; \
		float g_flBevelCurve##n < Default( 0 ); >; \
		float2 g_vBevelLayerAmount##n < Default2( 1, 1 ); >;

	BLEND_PARAMETERS( 2 )
	BLEND_PARAMETERS( 3 )

	// csgo_environment_blend, with layer 3, and its height-band blending (F_USE_NEW_BLENDING).
	float g_flBlendPs < Default( 0 ); >;
	float g_flLayer3Ps < Default( 0 ); >;
	float g_flUseNewBlending < Default( 0 ); >;
	float g_flBlendSoftnessDistanceModifierStrength < Default( 1 ); >;

	// F_SHARED_COLOR_OVERLAY: one colour texture over all layers, by tint mask.
	float g_flSharedColorOverlay < Default( 0 ); >;
	float g_flColorOverlayUVSetPs < Default( 2 ); >;
	float2 g_vOverlayTexCoordScale < Default2( 1, 1 ); >;
	float3 g_vColorOverlayLayerStrengths < Default3( 1, 1, 1 ); >;
	float3 g_vColorOverlayTintMaskStrengths < Default3( 0, 0, 0 ); >;
	float g_flOverlayDarknessContrast < Default( 1 ); >;
	float g_flOverlayBrightnessContrast < Default( 1 ); >;

	float g_flReflectance < Default( 0.04 ); >;

	// F_RENDER_BACKFACES without F_DONT_FLIP_BACKFACE_NORMALS: back faces light with the normal flipped.
	float g_flFlipBackfaceNormals < Default( 0 ); >;

	// The game's biplanar axes per plane (x, y and z facing), and its projection scale.
	float3 BiplanarU( int axis ) { return axis == 1 ? float3( 0, 1, 0 ) : float3( -1, 0, 0 ); }
	float3 BiplanarV( int axis ) { return axis == 1 ? float3( 0, -1, 0 ) : float3( 0, 0, 1 ); }
	#define BIPLANAR_SCALE ( float3( -1, 1, -1 ) / 64.0 )
	#define LUMA float3( 0.2125, 0.7154, 0.0721 )

	struct BiplanarFrame
	{
		int2 MajorAxes;
		float2 MajorUv;
		float2 MajorDdx;
		float2 MajorDdy;
		int2 MedianAxes;
		float2 MedianUv;
		float2 MedianDdx;
		float2 MedianDdy;
		float2 Weights;
	};

	// The two planes a surface faces most, and how much of each (the game's biplanar projection).
	BiplanarFrame GetBiplanarFrame( float3 normal, float3 position )
	{
		float3 a = abs( normal );
		int3 major = ( a.x > a.y && a.x > a.z ) ? int3( 0, 1, 2 ) : ( a.y > a.z ? int3( 1, 2, 0 ) : int3( 2, 0, 1 ) );
		int3 minor = ( a.x < a.y && a.x < a.z ) ? int3( 0, 1, 2 ) : ( a.y < a.z ? int3( 1, 2, 0 ) : int3( 2, 0, 1 ) );
		int3 median = int3( 3, 3, 3 ) - minor - major;

		BiplanarFrame f;
		f.MajorAxes = major.x == 1 ? major.zy : major.yz;
		f.MedianAxes = median.x == 1 ? median.zy : median.yz;

		float3 scaled = position * BIPLANAR_SCALE;
		float3 dx = ddx_fine( scaled );
		float3 dy = ddy_fine( scaled );

		f.MajorUv = float2( scaled[f.MajorAxes.x], scaled[f.MajorAxes.y] );
		f.MajorDdx = float2( dx[f.MajorAxes.x], dx[f.MajorAxes.y] );
		f.MajorDdy = float2( dy[f.MajorAxes.x], dy[f.MajorAxes.y] );
		f.MedianUv = float2( scaled[f.MedianAxes.x], scaled[f.MedianAxes.y] );
		f.MedianDdx = float2( dx[f.MedianAxes.x], dx[f.MedianAxes.y] );
		f.MedianDdy = float2( dy[f.MedianAxes.x], dy[f.MedianAxes.y] );

		float2 weights = saturate( ( float2( a[major.x], a[median.x] ) - 0.5773 ) * 2.365744 );
		f.Weights = weights / ( weights.x + weights.y );
		return f;
	}

	float3 DecodeHemiOctahedralNormal( float2 encoded )
	{
		float2 t = float2( encoded.x + encoded.y - 1.003921628, encoded.x - encoded.y );
		return normalize( float3( t, 1.0 - abs( t.x ) - abs( t.y ) ) );
	}

	// What the shader needs of the surface to read a layer: its frame, the mesh's and the second coordinates' tangents.
	struct SurfaceFrame
	{
		float3 Normal;			// geometric, flipped on back faces
		float3 Tangent;
		float3 Bitangent;
		float3 Tangent2;		// for layers on the second texture coordinates, from screen derivatives
		float3 Bitangent2;
		BiplanarFrame Biplanar;
	};

	struct LayerSample
	{
		float4 Color;
		float4 Height;
		float3 Normal;			// world space
		float Roughness;
	};

	// A normal map texel bent onto the surface by derivative mapping, its rotation applied: the game's layer normal.
	float3 DerivativeNormal( float4 texel, float4 xform, float3 normal, float3 tangent, float3 bitangent )
	{
		float3 n = DecodeHemiOctahedralNormal( texel.xy );
		float x = dot( n.xy, xform.xy );
		float y = dot( n.xy, xform.zw );
		float2 slope = float2( -x, y ) / max( abs( n.z ), 0.0078125 * max( abs( x ), abs( y ) ) );
		return normalize( normal - ( tangent * slope.x + bitangent * slope.y ) );
	}

	// One of the two biplanar planes' normal offset.
	float3 PlaneNormalOffset( float4 texel, int2 axes, float weight )
	{
		float3 n = DecodeHemiOctahedralNormal( texel.xy ) * weight;
		return BiplanarU( axes.x ) * n.x + BiplanarV( axes.y ) * n.y;
	}

	LayerSample SampleLayer( Texture2D color, Texture2D height, Texture2D normalMap, float uvSet, float2 uv, float2 scale, float4 normalXform, SurfaceFrame frame )
	{
		LayerSample l;

		[branch]
		if ( uvSet < 0.5 )
		{
			BiplanarFrame b = frame.Biplanar;
			float2 majorUv = b.MajorUv * scale;
			float2 majorDdx = b.MajorDdx * scale;
			float2 majorDdy = b.MajorDdy * scale;
			float2 medianUv = b.MedianUv * scale;
			float2 medianDdx = b.MedianDdx * scale;
			float2 medianDdy = b.MedianDdy * scale;

			float4 n0 = normalMap.SampleGrad( g_sAniso, majorUv, majorDdx, majorDdy );
			l.Color = color.SampleGrad( g_sAniso, majorUv, majorDdx, majorDdy ) * b.Weights.x;
			l.Height = height.SampleGrad( g_sAniso, majorUv, majorDdx, majorDdy ) * b.Weights.x;
			l.Roughness = n0.z * b.Weights.x;
			float3 offset = PlaneNormalOffset( n0, b.MajorAxes, b.Weights.x );

			if ( b.Weights.y > 0.0 )
			{
				float4 n1 = normalMap.SampleGrad( g_sAniso, medianUv, medianDdx, medianDdy );
				l.Color += color.SampleGrad( g_sAniso, medianUv, medianDdx, medianDdy ) * b.Weights.y;
				l.Height += height.SampleGrad( g_sAniso, medianUv, medianDdx, medianDdy ) * b.Weights.y;
				l.Roughness += n1.z * b.Weights.y;
				offset += PlaneNormalOffset( n1, b.MedianAxes, b.Weights.y );
			}

			l.Normal = normalize( offset + frame.Normal );
		}
		else
		{
			float4 n = normalMap.Sample( g_sAniso, uv );
			l.Color = color.Sample( g_sAniso, uv );
			l.Height = height.Sample( g_sAniso, uv );
			l.Roughness = n.z;

			bool second = uvSet > 1.5;
			l.Normal = DerivativeNormal( n, normalXform, frame.Normal, second ? frame.Tangent2 : frame.Tangent, second ? frame.Bitangent2 : frame.Bitangent );
		}

		return l;
	}

	// A detail normal, read like a layer's.
	float3 SampleDetailNormal( Texture2D detail, float uvSet, float2 uv, float2 scale, float4 normalXform, SurfaceFrame frame )
	{
		[branch]
		if ( uvSet < 0.5 )
		{
			BiplanarFrame b = frame.Biplanar;
			float3 offset = PlaneNormalOffset( detail.SampleGrad( g_sAniso, b.MajorUv * scale, b.MajorDdx * scale, b.MajorDdy * scale ), b.MajorAxes, b.Weights.x );
			if ( b.Weights.y > 0.0 )
				offset += PlaneNormalOffset( detail.SampleGrad( g_sAniso, b.MedianUv * scale, b.MedianDdx * scale, b.MedianDdy * scale ), b.MedianAxes, b.Weights.y );
			return normalize( offset + frame.Normal );
		}

		bool second = uvSet > 1.5;
		return DerivativeNormal( detail.Sample( g_sAniso, uv ), normalXform, frame.Normal, second ? frame.Tangent2 : frame.Tangent, second ? frame.Bitangent2 : frame.Bitangent );
	}

	float ContrastBrightness( float value, float contrast, float brightness )
	{
		return saturate( ( contrast * ( value - 0.5 ) + 0.5 ) * brightness );
	}

	float3 ApplyRows( float4 row0, float4 row1, float4 row2, float3 rgb )
	{
		float4 c = float4( rgb, 1.0 );
		return float3( dot( row0, c ), dot( row1, c ), dot( row2, c ) );
	}

	// Recolours towards the tint's hue keeping the colour's luminance, never brightening past three times the tint.
	float3 ColorizeTint( float3 albedo, float3 tint, float amount )
	{
		float3 tintNorm = normalize( max( tint, 0.001 ) );
		float lum = dot( albedo, LUMA );
		float tinted = min( lum / dot( tintNorm, LUMA ), 3.0 * lum * max( tint.r, max( tint.g, tint.b ) ) );
		return saturate( lerp( albedo, tintNorm * tinted, amount ) );
	}

	// The material side of a layer: tint mask, roughness, metalness, ambient occlusion, colour adjusted and tinted.
	struct Layer
	{
		float3 Color;
		float Occlusion;
		float4 Height;			// raw height (r), tint mask (g), alpha test occlusion (b), metalness (a)
		float3 Normal;
		float Roughness;
		float TintMask;
	};

	#define MAKE_LAYER( n, sample, detailUv ) \
	{ \
		layer##n.TintMask = ContrastBrightness( sample.Height.g, g_fTintMaskContrast##n, g_fTintMaskBrightness##n ); \
		layer##n.Roughness = ContrastBrightness( sample.Roughness, g_fTextureRoughnessContrast##n, g_fTextureRoughnessBrightness##n ); \
		layer##n.Height = sample.Height; \
		layer##n.Height.a *= g_flMetalness##n; \
		layer##n.Occlusion = saturate( lerp( g_vAmbientOcclusionLevels##n.x, g_vAmbientOcclusionLevels##n.z, pow( max( sample.Color.a, 0.0 ), g_vAmbientOcclusionLevels##n.y ) ) ); \
		float3 adjusted = g_flAdjustBaseColor##n > 0.5 ? ApplyRows( g_vTextureAdjust##n##_0, g_vTextureAdjust##n##_1, g_vTextureAdjust##n##_2, sample.Color.rgb ) : sample.Color.rgb; \
		float3 tinted = lerp( adjusted, ApplyRows( g_vTextureColorAdjust##n##_0, g_vTextureColorAdjust##n##_1, g_vTextureColorAdjust##n##_2, sample.Color.rgb ), layer##n.TintMask ); \
		layer##n.Color = ColorizeTint( tinted, i.vTintModel.rgb, i.vTintModel.w * layer##n.TintMask * g_flModelTint##n ); \
		layer##n.Normal = normalize( lerp( frame.Normal, sample.Normal, g_fTextureNormalContrast##n ) ); \
		[branch] \
		if ( g_flDetailNormal##n > 0.5 && g_fDetailTextureNormalContrast##n != 0.0 ) \
		{ \
			float3 detail = SampleDetailNormal( g_tNormalDetail##n, g_flDetailUVSetPs##n, detailUv, g_vDetailTexCoordScale##n, g_vDetailNormalXform_##n, frame ); \
			layer##n.Normal = normalize( layer##n.Normal + normalize( lerp( frame.Normal, detail, g_fDetailTextureNormalContrast##n ) ) - frame.Normal ); \
		} \
	}

	//
	// Layer blending (ValveResourceFormat's port of the blend shader)
	//
	float CombineOcclusion( float lower, float upper, float weight, float combine, float replace )
	{
		return lerp( lower * lerp( 1.0, upper, weight * combine ), upper, weight * replace );
	}

	float CombineRoughness( float lower, float upper, float weight, float combine, float replace )
	{
		return lerp( lower * ( 1.0 + ( upper - lower ) * 2.0 * weight * combine ), upper, weight * replace );
	}

	float3 CombineNormal( float3 lower, float3 upper, float3 flat, float weight, float combine, float replace )
	{
		float3 folded = normalize( lower + upper - flat );
		return normalize( lerp( lerp( lower, folded, weight * combine ), upper, weight * replace ) );
	}

	// Mod2x overlays the gamma encoded upper layer onto the lower, then replaces towards it.
	float3 CombineColor( float3 lower, float3 upper, float weight, float overlay, float replace )
	{
		float3 overlaid = lower;
		if ( overlay > 0.0 )
			overlaid *= lerp( 1.0, SrgbLinearToGamma( saturate( upper ) ) * 2.0, weight * overlay );

		return lerp( overlaid, upper, weight * replace );
	}

	// Softer seams at range, where a few pixel wide seam would turn jagged on small height mips.
	float BlendSoftnessAtRange( Texture2D height, float2 uv, float raw, float scale, float share )
	{
		float lod = height.CalculateLevelOfDetail( g_sAniso, uv );
		return lod * lod * ( 0.0125 + fwidth( raw * scale ) ) * 0.02 * g_flBlendSoftnessDistanceModifierStrength * share;
	}

	struct LayerCarry
	{
		float Weight;
		float Difference;
		float SignedRaw;
		float SignedCarry;
		float UnderHeight;
	};

	float BlendBandWeight( float difference, float bf, float rawHeight, float softness, float maskWithHeight )
	{
		softness = max( softness, 1e-4 );
		float quarter = softness * 0.25;
		float edgeDenom = 0.02 + quarter;

		float crossfade = saturate( 0.5 + difference / ( 2.0 * softness ) );
		float edgeHi = saturate( ( saturate( bf + difference * 0.1 + 0.2 ) - ( 0.98 - quarter ) ) / edgeDenom );
		crossfade = lerp( crossfade, 1.0, edgeHi );
		float edgeLo = saturate( ( edgeDenom - saturate( bf + difference * 0.05 - 0.05 ) ) / edgeDenom );
		crossfade = lerp( crossfade, 0.0, edgeLo );

		return saturate( crossfade * lerp( 0.5, rawHeight, maskWithHeight ) * 2.0 );
	}

	LayerCarry BlendLayer( LayerCarry below, float bf, float rawHeight, float zeroPoint, float scale, float softness, float underlyingInfluence, float maskWithHeight )
	{
		LayerCarry o;
		o.Weight = 0.0;
		o.Difference = 0.0;
		o.SignedRaw = 0.0;
		o.SignedCarry = 0.0;
		o.UnderHeight = below.UnderHeight;

		if ( bf <= 0.0 )
			return o;

		float signedFactor = 2.0 * bf - 1.0;
		float heightPlusSigned = ( rawHeight - zeroPoint ) * scale + signedFactor + max( below.SignedRaw, 0.0 );
		float difference = heightPlusSigned - lerp( below.SignedCarry, below.UnderHeight, underlyingInfluence );

		o.Weight = BlendBandWeight( difference, bf, rawHeight, softness, maskWithHeight );
		o.Difference = difference;
		o.SignedRaw = signedFactor;

		if ( o.Weight > 0.05 )
		{
			o.SignedCarry = signedFactor * o.Weight;
			o.UnderHeight = lerp( below.UnderHeight, max( below.UnderHeight, heightPlusSigned ), o.Weight );
		}

		return o;
	}

	float2 GetBlendWeights( float2 heightTex, float2 heightScale, float2 heightZero, float blendFactor, float blendSoftness )
	{
		float h1 = heightScale.x + blendSoftness;
		float height1 = heightTex.x * h1;

		float h2 = heightScale.y + blendSoftness;
		float h22 = heightTex.y * ( heightScale.y - blendSoftness );

		float blend1 = ( -heightZero.x * h1 - ( ( 1.0 - heightZero.y ) * h2 ) ) - blendSoftness;
		float blend2 = ( 1.0 - heightZero.x ) * h1 - ( ( -heightZero.y ) * h2 );

		float h2x = h22 + lerp( blend1, blend2, blendFactor );
		float2 weights = max( float2( height1, h2x ) - ( max( height1, h2x ) - blendSoftness ), 0.0 );
		weights.x += 0.001;
		return weights / ( weights.x + weights.y );
	}

	struct SeamFrame
	{
		float Difference;
		float PaintMask;
		float HeightScale;
		float Softness;
		float Weight;
		float MaskWithHeight;
		float TintMask;
	};

	SeamFrame GetSeamFrame( float difference, float blendFactor, float heightScale, float softness, float weight, float rawHeight, float maskWithHeight, float tintMask )
	{
		SeamFrame seam;
		seam.Difference = difference;
		seam.PaintMask = g_flUseNewBlending > 0.5 ? saturate( blendFactor * 5.0 ) * saturate( ( 1.0 - blendFactor ) * 5.0 ) : 0.0;
		seam.HeightScale = 0.5 + 0.5 * heightScale;
		seam.Softness = softness;
		seam.Weight = weight;
		seam.MaskWithHeight = lerp( 0.5, rawHeight, maskWithHeight );
		seam.TintMask = tintMask;
		return seam;
	}

	float SeamBand( float difference, float spread, float softness, float spreadParam )
	{
		return ( 1.0 - smoothstep( spread - softness, spread + softness, abs( difference ) ) ) * ( 1.0 - saturate( ( softness / spreadParam ) * 0.01 ) );
	}

	void ApplyBlendBorder( inout float3 lower, inout float3 upper, inout float lowerRoughness, inout float upperRoughness, SeamFrame seam,
		float spreadParam, float softnessParam, float offset, float3 tint, float2 layerAmount, bool useTintMask, float blendMode, bool roughnessEnabled, float roughnessValue )
	{
		if ( spreadParam <= 0.0 )
			return;

		float spread = spreadParam * seam.HeightScale * seam.PaintMask;
		float softness = seam.Softness + softnessParam;

		float amount = SeamBand( seam.Difference + offset, spread, softness, spreadParam )
			* saturate( seam.PaintMask * lerp( 4.0, 1.0, saturate( softness * 0.5 ) ) )
			* lerp( saturate( layerAmount.x * seam.MaskWithHeight * 2.0 ), saturate( layerAmount.y * seam.MaskWithHeight * 2.0 ), seam.Weight );

		float mask = useTintMask ? seam.TintMask : 1.0;
		float weight = amount * mask;

		if ( blendMode > 2.5 )
		{
			lower = lerp( lower, ColorizeTint( lower, tint, mask ), amount );
			upper = lerp( upper, ColorizeTint( upper, tint, mask ), amount );
		}
		else if ( blendMode > 0.5 && blendMode < 1.5 )
		{
			lower = lerp( lower, tint, weight );
			upper = lerp( upper, tint, weight );
		}
		else
		{
			float3 factor = lerp( 1.0, blendMode > 1.5 ? tint * 2.0 : tint, weight );
			lower *= factor;
			upper *= factor;
		}

		if ( roughnessEnabled )
		{
			lowerRoughness = lerp( lowerRoughness, roughnessValue, weight );
			upperRoughness = lerp( upperRoughness, roughnessValue, weight );
		}
	}

	// Rounds the seam off: a height ramp along the geometric normal, differentiated in screen space, gives the edge's
	// normal, folded into the surface normal over the band. Works in world space.
	float3 BlendBevelNormal( float3 normal, float3 flat, float3 positionWs, SeamFrame seam,
		float spreadParam, float softnessParam, float offset, float strength, float curveParam, float2 layerAmount )
	{
		if ( spreadParam == 0.0 || strength == 0.0 || ( layerAmount.x == 0.0 && layerAmount.y == 0.0 ) )
			return normal;

		float spread = spreadParam * seam.HeightScale * seam.PaintMask;
		float softness = ( seam.Softness + softnessParam ) * seam.PaintMask;
		float difference = seam.Difference + offset;

		float ramp = saturate( ( difference / max( spread + softness, 1e-5 ) ) * 0.5 + 0.5 );
		float curve = 1.0 + curveParam * curveParam * 10.0;
		float height = curveParam > 0.0 ? pow( ramp, curve ) : 1.0 - pow( 1.0 - ramp, curve );

		float3 ramped = positionWs + flat * height;
		float3 slope = cross( -ddy_fine( ramped ), ddx_fine( ramped ) );

		if ( seam.PaintMask == 0.0 || dot( slope, slope ) <= 0.0 )
			return normal;

		float amount = SeamBand( difference, spread, softness, spreadParam ) * lerp( layerAmount.x, layerAmount.y, seam.Weight ) * strength;
		float3 bevel = normalize( lerp( flat, normalize( slope ), amount ) );
		return normalize( normal + bevel - flat );
	}

	float OverlayLayerAmount( float tintMask, float maskStrength, float layerStrength, float share )
	{
		float mask = maskStrength > 0.0 ? tintMask : 1.0 - tintMask;
		return lerp( 1.0, mask, abs( maskStrength ) ) * layerStrength * share;
	}

	// Lit by the map's CS2 lighting, as the game's shader lights.
	float4 ShadeCS2( Material m, float3 receiverNormal, bool snow )
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
		s.ShadowReceiverNormal = receiverNormal;

		// The high variants' directional lightmap reads the normal in the surface's frame, as the game projects it.
		s.TangentNormal = normalize( float3( dot( m.Normal, m.WorldTangentU ), dot( m.Normal, m.WorldTangentV ), dot( m.Normal, m.GeometricNormal ) ) );
		s.GeometricNormal = m.GeometricNormal;
		s.Albedo = m.Albedo;
		s.Roughness = m.Roughness;
		s.Metalness = m.Metalness;
		s.Reflectance = g_flReflectance;
		s.AmbientOcclusion = m.AmbientOcclusion;
		s.LightmapUV = m.LightmapUV;
		s.SpecularDirect = true;
		s.SpecularIndirect = true;
		s.Snow = snow;

		return float4( CS2Shade( s ), m.Opacity );
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		#if S_RENDER_BACKFACES
			bool isFrontFace = i.face;
		#else
			bool isFrontFace = true;
		#endif

		float3 positionWs = i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz;
		float3 positionRelative = i.vPositionWithOffsetWs.xyz;

		//
		// The surface frame: the geometric normal, the mesh's tangents, and the second coordinates' from derivatives
		//
		bool flip = g_flFlipBackfaceNormals > 0.5 && !isFrontFace;
		float normalLength = length( i.vNormalWs.xyz );

		SurfaceFrame frame;
		frame.Normal = ( i.vNormalWs.xyz / normalLength ) * ( flip ? -1.0 : 1.0 );
		frame.Tangent = i.vTangentUWs.xyz / normalLength;
		frame.Bitangent = i.vTangentVWs.xyz / normalLength;

		{
			float3 dpdx = ddx_fine( positionRelative );
			float3 dpdy = ddy_fine( positionRelative );
			float2 duvdx = ddx_fine( i.vDetailUv3Uv2.zw );
			float2 duvdy = ddy_fine( i.vDetailUv3Uv2.zw );

			float determinant = dot( duvdx, float2( duvdy.y, -duvdy.x ) );
			float s = determinant < 0.0 ? -1.0 : 1.0;
			float2 w = float2( duvdy.y, -duvdx.y ) * s;
			float3 n = frame.Normal;
			float3 t = ( dpdx - n * dot( dpdx, n ) ) * w.x + ( dpdy - n * dot( dpdy, n ) ) * w.y;
			if ( abs( determinant ) > 0.0 )
				t = normalize( t );

			frame.Tangent2 = t;
			frame.Bitangent2 = cross( n, t ) * ( s * ( dot( dpdy, cross( n, dpdx ) ) < 0.0 ? -1.0 : 1.0 ) );
		}

		frame.Biplanar = GetBiplanarFrame( frame.Normal, positionWs );

		//
		// Layer 1
		//
		LayerSample sample1 = SampleLayer( g_tColor1, g_tHeight1, g_tNormal1, g_flUVSetPs1, i.vLayerUv12.xy, g_vTexCoordScale1, g_vNormalXform_1, frame );
		Layer layer1;
		MAKE_LAYER( 1, sample1, i.vDetailUv12.xy )

		float3 color = layer1.Color;
		float occlusion = layer1.Occlusion;
		float roughness = layer1.Roughness;
		float4 height = layer1.Height;
		float3 normal = layer1.Normal;
		float vertexColorMask = layer1.TintMask;

		//
		// Layers 2 and 3
		//
		[branch]
		if ( g_flBlendPs > 0.5 )
		{
			LayerSample sample2 = SampleLayer( g_tColor2, g_tHeight2, g_tNormal2, g_flUVSetPs2, i.vLayerUv12.zw, g_vTexCoordScale2, g_vNormalXform_2, frame );
			Layer layer2;
			MAKE_LAYER( 2, sample2, i.vDetailUv12.zw )

			float baseHeight1 = layer1.Height.r - g_flHeightMapZeroPoint1;
			float baseHeight2 = layer2.Height.r - g_flHeightMapZeroPoint2;

			float blendFactor2 = saturate( i.vBlendValues.x * 1.1 - 0.05 );
			float blendFactor3 = g_flLayer3Ps > 0.5 ? saturate( i.vBlendValues.y * 1.1 - 0.05 ) : 0.0;
			float3 paintShares = normalize( float3( saturate( 1.0 - blendFactor2 - blendFactor3 ), blendFactor2, blendFactor3 ) );

			float rangeSoftness1 = BlendSoftnessAtRange( g_tHeight1, i.vLayerUv12.xy, layer1.Height.r, g_flHeightMapScale1, paintShares.x );
			float rangeSoftness2 = BlendSoftnessAtRange( g_tHeight2, i.vLayerUv12.zw, layer2.Height.r, g_flHeightMapScale2, paintShares.y );
			float softness2 = i.vBlendValues.w + rangeSoftness1 * 2.0 + rangeSoftness2;

			LayerCarry carry2 = (LayerCarry)0;
			float weight2;
			float weightBase2;

			if ( g_flUseNewBlending > 0.5 )
			{
				LayerCarry carry1 = (LayerCarry)0;
				carry1.UnderHeight = baseHeight1 * g_flHeightMapScale1;
				carry2 = BlendLayer( carry1, blendFactor2, layer2.Height.r, g_flHeightMapZeroPoint2, g_flHeightMapScale2, softness2, g_flUnderlyingHeightMapInfluence2, g_flMaskWithHeight2 );
				weight2 = carry2.Weight;
				weightBase2 = 1.0 - weight2;
			}
			else
			{
				float2 weights = GetBlendWeights( float2( baseHeight1, baseHeight2 ), float2( g_flHeightMapScale1, g_flHeightMapScale2 ), float2( g_flHeightMapZeroPoint1, g_flHeightMapZeroPoint2 ), i.vBlendValues.x, softness2 );
				weight2 = weights.y;
				weightBase2 = weights.x;
			}

			float tintMaskBelow = lerp( layer1.TintMask, layer2.TintMask, weight2 );

			SeamFrame seam2 = GetSeamFrame( carry2.Difference, blendFactor2, max( abs( g_flHeightMapScale1 ), abs( g_flHeightMapScale2 ) ), softness2, weight2, layer2.Height.r, g_flMaskWithHeight2, tintMaskBelow );

			if ( g_flBlendEffects2 > 0.5 )
			{
				ApplyBlendBorder( color, layer2.Color, roughness, layer2.Roughness, seam2,
					g_flBorderSpread2, g_flBorderSoftness2, g_flBorderOffset2, g_vBorderTint2, g_vBorderLayerAmount2,
					g_flBorderTintMask2 > 0.5, g_flBorderBlendMode2, g_flBorderRoughnessEnabled2 > 0.5, g_fBorderRoughness2 );

				normal = BlendBevelNormal( normal, frame.Normal, positionWs, seam2,
					g_flBevelSpread2, g_flBevelSoftness2, g_flBevelOffset2, g_flBevelStrength2, g_flBevelCurve2, g_vBevelLayerAmount2 );
			}

			normal = CombineNormal( normal, layer2.Normal, frame.Normal, weight2, g_flNormalCombine2, g_flNormalReplace2 );
			color = CombineColor( color, layer2.Color, weight2, g_flColorOverlay2, g_flColorReplace2 );
			occlusion = CombineOcclusion( occlusion, layer2.Occlusion, weight2, g_flAOCombine2, g_flAOReplace2 );
			roughness = CombineRoughness( roughness, layer2.Roughness, weight2, g_flRoughnessCombine2, g_flRoughnessReplace2 );

			float4 height1 = float4( baseHeight1, layer1.Height.gba );
			float4 height2 = float4( baseHeight2, layer2.Height.gba );
			height = height1 * weightBase2 + height2 * weight2;

			float weight3 = 0.0;
			float tintMask3 = 0.0;
			float vertexColorMode3 = 0.0;

			[branch]
			if ( g_flLayer3Ps > 0.5 )
			{
				LayerSample sample3 = SampleLayer( g_tColor3, g_tHeight3, g_tNormal3, g_flUVSetPs3, i.vLayerUv3Overlay.xy, g_vTexCoordScale3, g_vNormalXform_3, frame );
				Layer layer3;
				MAKE_LAYER( 3, sample3, i.vDetailUv3Uv2.xy )

				float baseHeight3 = layer3.Height.r - g_flHeightMapZeroPoint3;
				float softness3 = i.vBlendValues.w + rangeSoftness1 + rangeSoftness2
					+ BlendSoftnessAtRange( g_tHeight3, i.vLayerUv3Overlay.xy, layer3.Height.r, g_flHeightMapScale3, paintShares.z );

				LayerCarry carry3 = (LayerCarry)0;

				if ( g_flUseNewBlending > 0.5 )
				{
					carry3 = BlendLayer( carry2, blendFactor3, layer3.Height.r, g_flHeightMapZeroPoint3, g_flHeightMapScale3, softness3, g_flUnderlyingHeightMapInfluence3, g_flMaskWithHeight3 );
					weight3 = carry3.Weight;
				}
				else
				{
					float2 weights3 = GetBlendWeights( float2( max( height1.r, height2.r ), baseHeight3 ),
						float2( max( g_flHeightMapScale1, g_flHeightMapScale2 ), g_flHeightMapScale3 ),
						float2( max( g_flHeightMapZeroPoint1, g_flHeightMapZeroPoint2 ), g_flHeightMapZeroPoint3 ),
						blendFactor3, softness3 );
					weight3 = weights3.y;
				}

				if ( g_flBlendEffects3 > 0.5 )
				{
					SeamFrame seam3 = GetSeamFrame( carry3.Difference, blendFactor3,
						max( abs( lerp( g_flHeightMapScale1, g_flHeightMapScale2, weight2 ) ), abs( g_flHeightMapScale3 ) ),
						softness3, weight3, layer3.Height.r, g_flMaskWithHeight3, lerp( tintMaskBelow, layer3.TintMask, weight3 ) );

					ApplyBlendBorder( color, layer3.Color, roughness, layer3.Roughness, seam3,
						g_flBorderSpread3, g_flBorderSoftness3, g_flBorderOffset3, g_vBorderTint3, g_vBorderLayerAmount3,
						g_flBorderTintMask3 > 0.5, g_flBorderBlendMode3, g_flBorderRoughnessEnabled3 > 0.5, g_fBorderRoughness3 );

					normal = BlendBevelNormal( normal, frame.Normal, positionWs, seam3,
						g_flBevelSpread3, g_flBevelSoftness3, g_flBevelOffset3, g_flBevelStrength3, g_flBevelCurve3, g_vBevelLayerAmount3 );
				}

				normal = CombineNormal( normal, layer3.Normal, frame.Normal, weight3, g_flNormalCombine3, g_flNormalReplace3 );
				color = CombineColor( color, layer3.Color, weight3, g_flColorOverlay3, g_flColorReplace3 );
				occlusion = CombineOcclusion( occlusion, layer3.Occlusion, weight3, g_flAOCombine3, g_flAOReplace3 );
				roughness = CombineRoughness( roughness, layer3.Roughness, weight3, g_flRoughnessCombine3, g_flRoughnessReplace3 );
				height = height * ( 1.0 - weight3 ) + float4( baseHeight3, layer3.Height.gba ) * weight3;

				tintMask3 = layer3.TintMask;
				vertexColorMode3 = g_flVertexColorMode3;
			}

			float share2 = weight2 * ( 1.0 - weight3 );
			float share1 = saturate( ( 1.0 - share2 ) - weight3 );

			[branch]
			if ( g_flSharedColorOverlay > 0.5 )
			{
				float overlayAmount = OverlayLayerAmount( layer1.TintMask, g_vColorOverlayTintMaskStrengths.x, g_vColorOverlayLayerStrengths.x, share1 )
					+ OverlayLayerAmount( layer2.TintMask, g_vColorOverlayTintMaskStrengths.y, g_vColorOverlayLayerStrengths.y, share2 )
					+ OverlayLayerAmount( tintMask3, g_vColorOverlayTintMaskStrengths.z, g_vColorOverlayLayerStrengths.z, weight3 );

				if ( overlayAmount > 0.0 )
				{
					float2 overlayUv = i.vLayerUv3Overlay.zw;
					float3 overlaySample;
					if ( g_flColorOverlayUVSetPs < 0.5 )
					{
						BiplanarFrame b = frame.Biplanar;
						overlaySample = g_tSharedColorOverlay.SampleGrad( g_sAniso, b.MajorUv * g_vOverlayTexCoordScale, b.MajorDdx * g_vOverlayTexCoordScale, b.MajorDdy * g_vOverlayTexCoordScale ).rgb * b.Weights.x;
						if ( b.Weights.y > 0.0 )
							overlaySample += g_tSharedColorOverlay.SampleGrad( g_sAniso, b.MedianUv * g_vOverlayTexCoordScale, b.MedianDdx * g_vOverlayTexCoordScale, b.MedianDdy * g_vOverlayTexCoordScale ).rgb * b.Weights.y;
					}
					else
					{
						overlaySample = g_tSharedColorOverlay.Sample( g_sAniso, overlayUv ).rgb;
					}

					float3 overlay = overlaySample * 2.0 - 1.0;
					float3 factor = max( 0.0, 1.0
						+ ( 1.0 - pow( max( 1.0 - max( 0.0, overlay ), 1e-9 ), g_flOverlayBrightnessContrast ) ) * g_flOverlayBrightnessContrast
						+ ( pow( max( 1.0 + min( 0.0, overlay ), 1e-9 ), g_flOverlayDarknessContrast ) - 1.0 ) * g_flOverlayDarknessContrast );

					color *= lerp( 1.0, factor, overlayAmount );
				}
			}

			// How much the painted vertex colour shows, by each layer's mode (0 masked by tint, 1 unmasked, 2 off).
			float masked = layer1.TintMask * share1 + layer2.TintMask * share2 + tintMask3 * weight3;
			float unmasked = ( g_flVertexColorMode1 > 0.5 ? 1.0 : 0.0 ) * share1 + ( g_flVertexColorMode2 > 0.5 ? 1.0 : 0.0 ) * share2 + ( vertexColorMode3 > 0.5 ? 1.0 : 0.0 ) * weight3;
			float enabled = ( g_flVertexColorMode1 < 1.5 ? 1.0 : 0.0 ) * share1 + ( g_flVertexColorMode2 < 1.5 ? 1.0 : 0.0 ) * share2;
			vertexColorMask = saturate( unmasked + masked ) * enabled;
			if ( g_flLayer3Ps > 0.5 )
				vertexColorMask += ( vertexColorMode3 < 1.5 ? 1.0 : 0.0 ) * weight3;
		}

		color *= lerp( 1.0, i.vPaintColor.rgb, saturate( vertexColorMask ) );

		//
		// Alpha test: the colour's alpha is the opacity, and ambient occlusion moves to the height's blue
		//
		float opacity = i.vPaintColor.a;
		#if S_ALPHA_TEST
			opacity *= g_tColor1.Sample( g_sAniso, i.vLayerUv12.xy ).a;
			occlusion = saturate( lerp( g_vAmbientOcclusionLevels1.x, g_vAmbientOcclusionLevels1.z, pow( max( height.b, 0.0 ), g_vAmbientOcclusionLevels1.y ) ) );
		#endif

		// Roughness raised where the surface normal changes quickly across the pixel (geometric specular antialiasing).
		float3 dx = ddx( frame.Normal );
		float3 dy = ddy( frame.Normal );
		roughness = max( roughness, pow( saturate( max( dot( dx, dx ), dot( dy, dy ) ) ), 0.333 ) );

		Material m = Material::Init( i.vPositionWithOffsetWs, i.vPositionSs );
		m.Albedo = color;
		m.Normal = normal;
		m.GeometricNormal = frame.Normal;
		m.WorldTangentU = frame.Tangent;
		m.WorldTangentV = frame.Bitangent;
		m.LightmapUV = i.vLightmapUV;
		m.TextureCoords = i.vLayerUv12.xy;
		m.Roughness = saturate( roughness );
		m.Metalness = saturate( height.a );
		m.AmbientOcclusion = occlusion;
		m.Opacity = opacity;

		#if S_MODE_DEPTH
			return ShadingModelStandard::Shade( m );
		#else
			// Derivatives need uniform control flow, so the shadow receiver normal is taken before branching.
			float3 receiverNormal = ComputeShadowReceiverNormal( m.WorldPositionWithOffset );

			[branch]
			if ( CS2LightingEnabled() )
				return ShadeCS2( m, receiverNormal, g_flSnowLayer1 > 0.5 );

			return ShadingModelStandard::Shade( m );
		#endif
	}
}
