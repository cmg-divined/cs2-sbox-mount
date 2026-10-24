//=========================================================================================================================
// CS2's csgo_glass.vfx: windows and other glass.
//
// From the game's compiled shader (decompiled with Source 2 Viewer). Light the glass reflects is added over the scene,
// and the scene shows through by how much light the glass transmits:
//  - a dust layer (g_tGlassDust, its alpha remapped by g_flTranslucencyRemap and times the instance alpha) lit by the
//    light in front and, through the glass, the light behind it (in the tint texture's alpha);
//  - the sun, lights and cubemap's specular off the normal map, roughness times the instance alpha (the high variant's
//    as cs2_lighting.hlsl's CS2ShadeHigh lights the world: GGX, the split sum table, the directional lightmap);
//  - transmission: one minus the Fresnel reflectance, times the tint raised to the path length through the pane
//    (refracted at an index of 1.5), times one minus the dust; plus that much of the lit dust in the tint;
//  - fog, over the reflected light and dimming what the glass lets through.
// The shader outputs the transmission's luminance as alpha. The blend that composites that, src + dst * src alpha, isn't
// in the shader's render states (the game's translucent pass supplies it); it's inferred from what the outputs are, and
// unverified.
//
// Not ported (yet): F_OPAQUE_CUBEMAP_REFRACTION (17 materials, drawn as plain glass), F_VERTEX_BREAK_ANIMATION (8),
// F_PAINT_VERTEX_COLORS (2). Glass doesn't draw into the depth prepass here, so it casts no shadows.
//=========================================================================================================================

HEADER
{
	Description = "CS2 csgo_glass";
}

FEATURES
{
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
	float4 vTint : TEXCOORD10;	// the instance tint, linear; alpha in w
};

VS
{
	#include "cs2_vertex.hlsl"

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessCS2Vertex( v );

		// The instance colour is gamma space; the game linearizes it.
		float4 instance = i.vVertexColor * v.vMeshTint;
		i.vTint = float4( SrgbGammaToLinear( instance.rgb ), instance.a );

		return FinalizeCS2Vertex( i );
	}
}

PS
{
	// The glass adds its light and lets the scene through by its alpha.
	#define BLEND_MODE_ALREADY_SET 1
	#define DEPTH_STATE_ALREADY_SET 1
	// sbox_pixel supplies culling; suppress the duplicate declaration in vr_common_ps_code.
	#define CULL_MODE_ALREADY_SET 1

	#include "common/pixel.hlsl"
	#include "common/classes/Light.hlsl"
	#include "cs2_lighting.hlsl"

	RenderState( DepthEnable, true );
	RenderState( DepthFunc, GREATER_EQUAL );
	RenderState( DepthWriteEnable, true );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, ONE );
	RenderState( DstBlend, SRC_ALPHA );

	CreateInputTexture2D( CS2GlassDust, Srgb, 8, "None", "_color", "Glass", Default4( 0, 0, 0, 0 ) );
	CreateInputTexture2D( CS2GlassTintColor, Srgb, 8, "None", "_color", "Glass", Default4( 1, 1, 1, 0 ) );
	CreateInputTexture2D( CS2Normal, Linear, 8, "None", "_normal", "Normal Map", Default4( 0.5, 0.5, 0.5, 0 ) );

	Texture2D g_tGlassDust < Channel( RGBA, Box( CS2GlassDust ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tGlassTintColor < Channel( RGBA, Box( CS2GlassTintColor ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tNormal < Channel( RGBA, Box( CS2Normal ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;

	TextureAttribute( RepresentativeTexture, g_tGlassDust );

	// The dust's alpha is remapped from 0..1 to this range.
	float2 g_flTranslucencyRemap < Default2( 0, 0 ); >;
	float g_flModelTintAmount < Default( 1 ); >;
	float g_flReflectance < Default( 0.04 ); >;

	// F_RENDER_BACKFACES without F_DONT_FLIP_BACKFACE_NORMALS: back faces light with the normal flipped.
	float g_flFlipBackfaceNormals < Default( 0 ); >;

	float g_flFogEnabled < Default( 1 ); >;

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

		float2 uv = i.vTextureCoords.xy;
		float3 positionWs = i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz;

		float4 dust = g_tGlassDust.Sample( g_sAniso, uv );
		float4 glassTint = g_tGlassTintColor.Sample( g_sAniso, uv );
		float4 normalTexel = g_tNormal.Sample( g_sAniso, uv );

		float dustAmount = saturate( dust.a * i.vTint.a ) * ( g_flTranslucencyRemap.y - g_flTranslucencyRemap.x ) + g_flTranslucencyRemap.x;
		float3 tint = glassTint.rgb * lerp( 1.0, i.vTint.rgb, g_flModelTintAmount );

		//
		// Normal: tangent space from the vertex frame, flipped on back faces
		//
		bool flip = g_flFlipBackfaceNormals > 0.5 && !isFrontFace;
		float3 vertexNormal = normalize( i.vNormalWs.xyz ) * ( flip ? -1.0 : 1.0 );

		float3 normalTs = DecodeHemiOctahedralNormal( normalTexel.xy );
		if ( !flip )
			normalTs.y = -normalTs.y;

		float3 normalWs = normalize( i.vTangentUWs.xyz * normalTs.x + i.vTangentVWs.xyz * normalTs.y + vertexNormal * normalTs.z );

		float3 dx = ddx( vertexNormal );
		float3 dy = ddy( vertexNormal );
		float roughness = max( normalTexel.b * i.vTint.a, pow( saturate( max( dot( dx, dx ), dot( dy, dy ) ) ), 0.333 ) );

		float3 toCamera = CalculatePositionToCameraDirWs( positionWs );

		// Derivatives need uniform control flow, so the shadow receiver normal is taken before branching.
		float3 receiverNormal = ComputeShadowReceiverNormal( i.vPositionWithOffsetWs.xyz );

		// Outside a mounted map there's no CS2 lighting for it to reflect: the pane is clear.
		[branch]
		if ( !CS2LightingEnabled() )
			return float4( 0, 0, 0, 1 );

		//
		// The light in front of and behind the pane
		//
		CS2Surface s = CS2SurfaceDefaults();
		s.Position = positionWs;
		s.PositionWithOffset = i.vPositionWithOffsetWs.xyz;
		s.ScreenPosition = i.vPositionSs;
		s.Normal = normalWs;
		s.ShadowReceiverNormal = receiverNormal;
		s.Roughness = roughness;
		s.Reflectance = g_flReflectance;
		s.LightmapUV = i.vLightmapUV;
		s.Transmission = 1.0;
		s.GeometricNormal = vertexNormal;
		s.TangentNormal = normalTs;
		s.TransmissionShadows = true;

		// The shader detail setting's variant: the high one's lights are GGX, its lightmap directional (CS2LightHigh).
		bool high = CS2HighQuality();
		CS2LightTerms l;

		[branch]
		if ( high )
			l = CS2LightHigh( s );
		else
			l = CS2Light( s );

		// The high variant weights the cubemap by the split sum table with multiple scattering, and takes that from the
		// baked light on the dust.
		float NdotV = max( 0.0, dot( l.ToCamera, l.Normal ) );
		float3 brdf = 0;
		float3 multipleScattering = 0;
		if ( high )
			CS2EnvironmentBrdfHigh( l.SpecularColor, roughness, NdotV, brdf, multipleScattering );

		float3 light = l.Diffuse + l.Irradiance * ( 1.0 - ( brdf + multipleScattering ) ) + l.Transmitted * ( glassTint.a * dustAmount );
		float3 litDust = dustAmount * dust.rgb * light;

		float3 color = litDust;

		[branch]
		if ( high )
			color += l.Specular * CS2DirectSpecularScale( l.SpecularColor, roughness, NdotV ) + CS2EnvironmentHigh( l, roughness ) * ( brdf + multipleScattering );
		else
			color += l.Specular + CS2EnvironmentSpecular( l, roughness );

		// What the pane lets through: past the reflection, along the refracted path through the tint, past the dust.
		float viewCosine = max( 0.0, dot( toCamera, vertexNormal ) );
		float fresnel = lerp( 0.04, 1.0, pow( 1.0 - max( 0.0, dot( toCamera, normalWs ) ), 5.0 ) );
		float3 transmittance = ( 1.0 - fresnel ) * pow( tint, 1.0 / max( 0.01, sqrt( 1.0 - 0.444444448 * ( 1.0 - viewCosine * viewCosine ) ) ) );
		transmittance *= 1.0 - dustAmount;

		color += transmittance * litDust * pow( tint, 1.6 );

		//
		// Fog over the reflected light, and through the pane
		//
		[branch]
		if ( g_flFogEnabled > 0.5 )
		{
			float gradient, cubemap;
			float3 cubemapColor;
			CS2FogAt( l.Position, true, gradient, cubemap, cubemapColor );

			color = lerp( color, g_vCS2GradientFogColor.rgb, gradient * ( 1.0 - dustAmount ) );
			transmittance *= 1.0 - gradient;
			color = lerp( color, cubemapColor, cubemap * ( 1.0 - dustAmount ) );
			transmittance *= 1.0 - cubemap;
		}

		return float4( color, dot( transmittance, float3( 0.2125, 0.7154, 0.0721 ) ) );
	}
}
