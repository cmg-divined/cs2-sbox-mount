//=========================================================================================================================
// Source 2's core generic.vfx as CS2 draws it: tools and editor materials, effect cards (muzzle flashes), world text
// panels, a few spills and decals.
//
// From the game's compiled shader (core shaders_vulkan, decompiled with Source 2 Viewer: unlit, lit, translucent, alpha
// tested and self illuminated variants). The colour times the colour tint times the instance colour (linearized, no model
// tint amount); unlit, that's all; lit, an ambient colour weighted by the normal's squared components (0.6, 0.4, 1.0
// facing up and 0.6, 0.4, 0.2 facing down) plus the sun through a power of N.L made softer by roughness
// ( 1 - min( 1, gloss average + roughness texture ) ) * 0.8; self illumination blends to its tint by its mask's green.
// Alpha test cuts at the colour's alpha; translucency is the instance alpha times the colour's times the opacity scale.
//
// The lit path's ambient and sun come from engine buffers this shader alone reads (RoughnessLightingGlobals,
// SunLightShaderParams): the sun here is the map's (cs2_lighting), the ambient the map's sky light from above (both
// unverified substitutes). Not ported: the legacy distance fog it applies (per view values CS2 maps don't set through any
// entity here) and its multiply by the view's exposure (s&box's scene shaders don't have the CS2 exposure), F_SPECULAR /
// F_SPECULAR_CUBE_MAP (12 materials), F_OVERLAY (1), F_MORPH_SUPPORTED.
//=========================================================================================================================

HEADER
{
	Description = "CS2 generic";
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
	float4 vUv : TEXCOORD5;			// xy: colour coordinates; zw: normal map coordinates
	float4 vTint : TEXCOORD10;		// the instance colour, linear; alpha in w
};

VS
{
	#include "cs2_vertex.hlsl"

	float2 g_vTexCoordScale < Default2( 1, 1 ); >;
	float2 g_vTexCoordOffset < Default2( 0, 0 ); >;
	float2 g_vNormalTexCoordScale < Default2( 1, 1 ); >;

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessCS2Vertex( v );

		float2 uv = v.vTexCoord.xy;
		i.vUv = float4( uv * g_vTexCoordScale + g_vTexCoordOffset, uv * g_vNormalTexCoordScale * g_vTexCoordScale + g_vTexCoordOffset );

		float4 instance = i.vVertexColor * v.vMeshTint;
		i.vTint = float4( SrgbGammaToLinear( instance.rgb ), instance.a );
		return FinalizeCS2Vertex( i );
	}
}

PS
{
	StaticCombo( S_ALPHA_TEST, F_ALPHA_TEST, Sys( ALL ) );
	StaticCombo( S_TRANSLUCENT, F_TRANSLUCENT, Sys( ALL ) );
	StaticCombo( S_ADDITIVE_BLEND, F_ADDITIVE_BLEND, Sys( ALL ) );

	#define CULL_MODE_ALREADY_SET
	#include "common/pixel.hlsl"
	#include "common/classes/Light.hlsl"
	#include "cs2_lighting.hlsl"

	CreateInputTexture2D( CS2Color, Srgb, 8, "None", "_color", "Color", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( CS2Normal, Linear, 8, "None", "_normal", "Normal Map", Default4( 0.5, 0.5, 1, 0.5 ) );
	CreateInputTexture2D( CS2Roughness, Linear, 8, "None", "_rough", "Lighting", Default4( 0.5, 0.5, 0.5, 0.5 ) );
	CreateInputTexture2D( CS2SelfIllumMask, Linear, 8, "None", "_selfillum", "Self Illum", Default4( 1, 1, 1, 0 ) );

	Texture2D g_tColor < Channel( RGBA, Box( CS2Color ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tNormal < Channel( RGBA, Box( CS2Normal ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tRoughness < Channel( RGBA, Box( CS2Roughness ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tSelfIllumMask < Channel( RGBA, Box( CS2SelfIllumMask ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;

	TextureAttribute( RepresentativeTexture, g_tColor );

	float3 g_vColorTint < Default3( 1, 1, 1 ); >;
	float g_flUnlit < Default( 0 ); >;
	float g_flBumpStrength < Default( 1 ); >;
	float2 g_vRoughnessScaleAndBias < Default2( 0, 1 ); >;
	float g_flSelfIllum < Default( 0 ); >;
	float3 g_vSelfIllumTintScaled < Default3( 1, 1, 1 ); >;

	#if !S_TRANSLUCENT
		float g_flOpacityScale < Default( 1 ); >;
	#endif

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float4 color = g_tColor.Sample( g_sAniso, i.vUv.xy );
		float3 albedo = color.rgb * g_vColorTint * i.vTint.rgb;

		float alpha = i.vTint.a * color.a * g_flOpacityScale;
		#if S_ALPHA_TEST
			if ( color.a < g_flAlphaTestReference )
				discard;
			alpha = color.a;
		#endif

		float3 vertexNormal = normalize( i.vNormalWs.xyz );
		float3 result = albedo;

		[branch]
		if ( g_flUnlit < 0.5 && CS2LightingEnabled() )
		{
			// The normal map in its alpha and green, scaled by the bump strength.
			float2 xy = g_tNormal.Sample( g_sAniso, i.vUv.zw ).wy * 2.0 - 1.0;
			float3 normalTs = float3( xy.x, -xy.y, sqrt( saturate( 1.0 - dot( xy, xy ) ) ) );
			normalTs.xy *= g_flBumpStrength;
			normalTs = normalize( normalTs );

			float3 tangent = normalize( i.vTangentUWs.xyz );
			float3 normal = normalize( tangent * normalTs.x + i.vTangentVWs.xyz * normalTs.y + vertexNormal * normalTs.z );
			normal = CS2DirectionToMap( normal );

			float4 rough = g_tRoughness.Sample( g_sAniso, i.vUv.xy );
			float softness = ( 1.0 - min( 1.0, ( ( rough.z + rough.w ) * 0.5 ) * g_vRoughnessScaleAndBias.x + g_vRoughnessScaleAndBias.y + ( rough.x + rough.y ) * 0.5 ) ) * 0.8;

			float3 up = saturate( normal );
			float3 down = saturate( -normal );
			float3 ambient = CS2Ambient( float3( 0, 0, 1 ) ) * ( dot( float3( 0.6, 0.4, 1.0 ), up * up ) + dot( float3( 0.6, 0.4, 0.2 ), down * down ) );
			float3 sun = g_vCS2SunColor * ( pow( saturate( dot( normal, g_vCS2SunDirection ) ), softness + 0.6 ) * ( ( softness + 1.6 ) * 0.5 ) );
			result = ( ambient + sun ) * albedo;

			if ( g_flSelfIllum > 0.5 )
				result = lerp( result, g_vSelfIllumTintScaled * albedo, g_tSelfIllumMask.Sample( g_sAniso, i.vUv.xy ).g );
		}

		#if S_MODE_DEPTH
			Material m = Material::Init( i.vPositionWithOffsetWs, i.vPositionSs );
			m.Albedo = albedo;
			m.Normal = vertexNormal;
			m.Opacity = alpha;
			return ShadingModelStandard::Shade( m );
		#else
			return float4( result, alpha );
		#endif
	}
}
