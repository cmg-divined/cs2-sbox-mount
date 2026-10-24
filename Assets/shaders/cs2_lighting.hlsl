//=========================================================================================================================
// CS2's forward lighting, as csgo_vertexlitgeneric's pixel shader lights (decompiled with Source 2 Viewer: its
// D_BAKED_LIGHTING_FROM_LIGHTMAP and D_BAKED_LIGHTING_FROM_PROBE variants with S_SPECULAR_DIRECT/INDIRECT).
//
// A CS2 material shader fills a CS2Surface and CS2Shade lights it the way the game does: the irradiance lightmap or the
// light probe volumes' ambient cubes, the sun and the stationary and dynamic light2 lights with the game's specular
// approximation, one cubemap per view scaled by the light around the surface, and the gradient and cubemap fog. The
// map's lighting comes from the CS2 Map Lighting component as scene render attributes.
//
// Not ported (yet): the game's realtime shadow maps for light2 lights (they are shadowed by their baked channel only),
// its screen space ambient occlusion, and the AO proxies. Sun shadows on what moves are s&box's cascades.
//=========================================================================================================================

#ifndef CS2_LIGHTING_HLSL
#define CS2_LIGHTING_HLSL

#include "common/classes/Bindless.hlsl"

//
// Layouts, matching the structs in CS2MapLighting.cs
//

struct CS2ProbeVolume
{
	float4 WorldToBox0;
	float4 WorldToBox1;
	float4 WorldToBox2;
	float4 BoxMin;
	float4 BoxMax;
	float4 Fade;			// inverse edge fade widths
	float4 WorldToAtlas0;
	float4 WorldToAtlas1;
	float4 WorldToAtlas2;
	float4 BorderMin;		// w: ambient cube atlas
	float4 BorderMax;		// w: direct light shadows atlas
	float4 AtlasScale;
	float4 AtlasOffset;
	float4 Tint;
};

struct CS2CubemapVolume
{
	float4 WorldToLocal0;
	float4 WorldToLocal1;
	float4 WorldToLocal2;
	float4 BoxMin;			// w: cube array slice
	float4 BoxMax;			// w: 1 for a sphere, radius in x
	float4 Center;			// w: indoor level
};

struct CS2BarnLight
{
	float4 Frustum0;
	float4 Frustum1;
	float4 Frustum2;
	float4 Frustum3;
	float4 Illumination0;
	float4 Illumination1;
	float4 Illumination2;
	float4 Position;		// w: squared distance of full brightness, 0 for parallel rays along xyz
	float4 DistanceFade;	// distance fade bias and scale, near and far skirts
	float4 Color;			// w: cookie texture, negative for a spherical cookie
	float4 Orientation;
	float4 AngleFade;		// cone fade bias and scale, luminaire half length
	float4 Shape;
	float4 BakedShadowMask;
	float4 Parameters;		// min roughness
};

//
// Scene attributes
//

int g_nCS2Lighting < Attribute( "CS2Lighting" ); Default( 0 ); >;

// The map can be placed anywhere; its lighting is in its own space.
float4 g_vCS2WorldToMap0 < Attribute( "CS2WorldToMap0" ); Default4( 1, 0, 0, 0 ); >;
float4 g_vCS2WorldToMap1 < Attribute( "CS2WorldToMap1" ); Default4( 0, 1, 0, 0 ); >;
float4 g_vCS2WorldToMap2 < Attribute( "CS2WorldToMap2" ); Default4( 0, 0, 1, 0 ); >;

int g_nCS2LightmapIrradiance < Attribute( "CS2LightmapIrradiance" ); Default( 0 ); >;
int g_nCS2LightmapShadows < Attribute( "CS2LightmapShadows" ); Default( 0 ); >;

// The mips below the lightmaps' top ones, for lightmaps taken in two parts (0 when the textures above hold them all).
int g_nCS2LightmapIrradianceLowerMips < Attribute( "CS2LightmapIrradianceLowerMips" ); Default( 0 ); >;
int g_nCS2LightmapShadowsLowerMips < Attribute( "CS2LightmapShadowsLowerMips" ); Default( 0 ); >;
float2 g_vCS2LightmapUvScale < Attribute( "CS2LightmapUvScale" ); Default2( 1, 1 ); >;

int g_nCS2ProbeVolumeCount < Attribute( "CS2ProbeVolumeCount" ); Default( 0 ); >;
StructuredBuffer<CS2ProbeVolume> g_CS2ProbeVolumes < Attribute( "CS2ProbeVolumes" ); >;

int g_nCS2EnvironmentMaps < Attribute( "CS2EnvironmentMaps" ); Default( 0 ); >;
float g_flCS2EnvironmentMapMips < Attribute( "CS2EnvironmentMapMips" ); Default( 0 ); >;
int g_nCS2CubemapVolumeCount < Attribute( "CS2CubemapVolumeCount" ); Default( 0 ); >;
StructuredBuffer<CS2CubemapVolume> g_CS2CubemapVolumes < Attribute( "CS2CubemapVolumes" ); >;

float4 g_vCS2AmbientR < Attribute( "CS2AmbientR" ); Default4( 0, 0, 0, 0 ); >;
float4 g_vCS2AmbientG < Attribute( "CS2AmbientG" ); Default4( 0, 0, 0, 0 ); >;
float4 g_vCS2AmbientB < Attribute( "CS2AmbientB" ); Default4( 0, 0, 0, 0 ); >;
float3 g_vCS2ConstantAmbient < Attribute( "CS2ConstantAmbient" ); Default3( 0, 0, 0 ); >;

float3 g_vCS2SunDirection < Attribute( "CS2SunDirection" ); Default3( 0, 0, 0 ); >;
float3 g_vCS2SunColor < Attribute( "CS2SunColor" ); Default3( 0, 0, 0 ); >;
float4 g_vCS2SunShadowMask < Attribute( "CS2SunShadowMask" ); Default4( 0, 0, 0, 0 ); >;
float g_flCS2SunMinRoughness < Attribute( "CS2SunMinRoughness" ); Default( 0 ); >;

int g_nCS2BarnLightCount < Attribute( "CS2BarnLightCount" ); Default( 0 ); >;
StructuredBuffer<CS2BarnLight> g_CS2BarnLights < Attribute( "CS2BarnLights" ); >;

// The game's shader detail setting (setting.shaderquality, mat_set_shader_quality: the shaders' S_SHADER_QUALITY):
// 1 lights the world as its high variants do (CS2ShadeHigh), 0 as its low ones (CS2ShadeLow). High by default.
int g_nCS2ShaderQuality < Attribute( "CS2ShaderQuality" ); Default( 1 ); >;

// The game's split sum table, textures/dev/brdf_lut (64x64, three layers: GGX in layer 1), by its bindless index.
int g_nCS2BRDFLookup < Attribute( "CS2BRDFLookup" ); Default( 0 ); >;

// The directional lightmap (directional_irradiance: the light's dominant direction in tangent space in xy, how
// directional it is in z), in two parts like the others. The high variants read it.
int g_nCS2LightmapDirectional < Attribute( "CS2LightmapDirectional" ); Default( 0 ); >;
int g_nCS2LightmapDirectionalLowerMips < Attribute( "CS2LightmapDirectionalLowerMips" ); Default( 0 ); >;

// What the game blends the cubemap volumes by, one per volume of CS2CubemapVolumes in the same order (which must be
// the game's: most indoor first, then by array index). Without it (count 0) one cube reflects, as the low variants do.
struct CS2CubemapBlend
{
	float4 InvEdgeWidth;		// xyz: 1 / ( edge_fade_dists + the bounds' extension )
	float4 Tint;				// xyz
	float4 NormalizationSH;		// the cube's own luminance as an L1 harmonic, for a dot with ( normal, 1 )
};

int g_nCS2CubemapBlendCount < Attribute( "CS2CubemapBlendCount" ); Default( 0 ); >;
StructuredBuffer<CS2CubemapBlend> g_CS2CubemapBlend < Attribute( "CS2CubemapBlend" ); >;

// A uniform grid over the map (CS2MapLighting's) listing per cell the probe volumes (list 0), lights (list 1) and
// cubemap volumes (list 2) that can reach into it, so a point visits only those instead of all of them. Each list
// keeps the global order, so the result is the same. Per cell: first and count of each list; the lists follow.
StructuredBuffer<uint> g_CS2Grid < Attribute( "CS2Grid" ); >;
float4 g_vCS2GridOrigin < Attribute( "CS2GridOrigin" ); Default4( 0, 0, 0, 0 ); >;	// w: 1 / cell size
float4 g_vCS2GridSize < Attribute( "CS2GridSize" ); Default4( 0, 0, 0, 0 ); >;		// w: 1 with a grid

int g_nCS2GradientFog < Attribute( "CS2GradientFog" ); Default( 0 ); >;
float4 g_vCS2GradientFogBiasAndScale < Attribute( "CS2GradientFogBiasAndScale" ); Default4( 0, 0, 0, 0 ); >;
float2 g_vCS2GradientFogExponents < Attribute( "CS2GradientFogExponents" ); Default2( 1, 1 ); >;
float4 g_vCS2GradientFogColor < Attribute( "CS2GradientFogColor" ); Default4( 0, 0, 0, 0 ); >;
float2 g_vCS2GradientFogCulling < Attribute( "CS2GradientFogCulling" ); Default2( 0, 0 ); >;

int g_nCS2CubemapFog < Attribute( "CS2CubemapFog" ); Default( 0 ); >;
float4 g_vCS2CubemapFogDistance < Attribute( "CS2CubemapFogDistance" ); Default4( 0, 0, 0, 1 ); >;	// offset, scale, mip bias, exponent
float4 g_vCS2CubemapFogHeight < Attribute( "CS2CubemapFogHeight" ); Default4( 1, 0, 0, 0 ); >;		// offset, scale, exponent, mip
float4 g_vCS2CubemapFogParameters < Attribute( "CS2CubemapFogParameters" ); Default4( 0, 1, 0, 0 ); >;	// start distance², exposure, -, opacity
int g_nCS2CubemapFogTexture < Attribute( "CS2CubemapFogTexture" ); Default( 0 ); >;
float3 g_vCS2CubemapFogRotation0 < Attribute( "CS2CubemapFogRotation0" ); Default3( 1, 0, 0 ); >;
float3 g_vCS2CubemapFogRotation1 < Attribute( "CS2CubemapFogRotation1" ); Default3( 0, 1, 0 ); >;
float3 g_vCS2CubemapFogRotation2 < Attribute( "CS2CubemapFogRotation2" ); Default3( 0, 0, 1 ); >;

//
// What a CS2 material hands the lighting for a pixel
//

struct CS2Surface
{
	float3 Position;				// world space
	float3 PositionWithOffset;		// camera relative, for s&box's sun shadows
	float4 ScreenPosition;
	float3 Normal;
	float3 ShadowReceiverNormal;	// from screen derivatives, taken in uniform control flow
	float3 Albedo;
	float Roughness;
	float Metalness;
	float Reflectance;				// a dielectric's specular colour (g_flReflectance)
	float AmbientOcclusion;
	float3 Emission;				// self illumination, added after lighting
	float2 LightmapUV;				// before the map's scale; negative where the lightmaps don't reach
	bool SpecularDirect;			// F_SPECULAR_DIRECT: the sun and lights' specular
	bool SpecularIndirect;			// F_SPECULAR_INDIRECT: the cubemap's
	bool Snow;						// csgo_environment's snow layer: its baked diffuse light is tinted colder
	bool Fog;						// the material's g_bFogEnabled

	// csgo_foliage's transmission: light from behind the surface (the reversed vertex normal), seen through it in this
	// colour; zero for none. With TransmissionShadows the sun's realtime shadows dim it too, else only its baked ones.
	float3 Transmission;
	float3 GeometricNormal;			// the vertex normal, flipped on back faces
	bool TransmissionShadows;

	// The high variants' directional lightmap reads the normal map's tangent space normal, as the frame is built from
	// it (y as it multiplies the bitangent); flat (0, 0, 1) when a shader doesn't give it.
	float3 TangentNormal;
};

CS2Surface CS2SurfaceDefaults()
{
	CS2Surface s = (CS2Surface)0;
	s.Reflectance = 0.04;
	s.AmbientOcclusion = 1.0;
	s.LightmapUV = float2( -1.0, -1.0 );
	s.Fog = true;
	s.TangentNormal = float3( 0.0, 0.0, 1.0 );
	return s;
}

bool CS2LightingEnabled()
{
	return g_nCS2Lighting != 0;
}

// The sun's realtime shadow: s&box's cascades alone, for whatever moves (the game's shaders take its own cascades). s&box's
// screen space shadow mask, which its GetVisibility multiplies in, is left out: the game has no such term, and it
// marches the depth buffer, where the viewmodel is drawn squeezed in depth, so its low poly facets shadow each other.
float CS2SunShadow( float3 positionWithOffset, float3 receiverNormal, float4 screenPosition )
{
	float3 positionLs;
	int cascade = FindCascadeWithOffset( positionWithOffset, positionLs );
	if ( cascade < 0 )
		return 1.0;

	return DirectionalLightShadow::SampleCascadeWithOffset( cascade, positionWithOffset, receiverNormal, screenPosition.xy );
}

struct CS2GridRange
{
	int Cell;
	uint First;
	uint Count;
};

// The probe volumes (list 0), lights (list 1) or cubemap volumes (list 2) of the grid cell around a map space point;
// everything without a grid, nothing outside it (nothing reaches there).
CS2GridRange CS2GridLookup( float3 position, uint list, uint total )
{
	CS2GridRange range;
	range.Cell = -1;
	range.First = 0;
	range.Count = total;

	if ( g_vCS2GridSize.w == 0.0 )
		return range;

	// Written so a NaN position (which fails every comparison) counts as outside, never indexing the grid.
	float3 cell = floor( ( position - g_vCS2GridOrigin.xyz ) * g_vCS2GridOrigin.w );
	if ( !all( cell >= 0.0 && cell < g_vCS2GridSize.xyz ) )
	{
		range.Count = 0;
		return range;
	}

	range.Cell = (int)( ( cell.z * g_vCS2GridSize.y + cell.y ) * g_vCS2GridSize.x + cell.x );
	uint header = (uint)range.Cell * 6 + list * 2;
	range.First = g_CS2Grid[header];
	range.Count = g_CS2Grid[header + 1];
	return range;
}

// The k-th item of a range.
uint CS2GridItem( CS2GridRange range, uint k )
{
	return range.Cell >= 0 ? g_CS2Grid[range.First + k] : k;
}

float3 CS2ToMap( float3 position )
{
	float4 p = float4( position, 1.0 );
	return float3( dot( g_vCS2WorldToMap0, p ), dot( g_vCS2WorldToMap1, p ), dot( g_vCS2WorldToMap2, p ) );
}

float3 CS2DirectionToMap( float3 direction )
{
	return normalize( float3( dot( g_vCS2WorldToMap0.xyz, direction ), dot( g_vCS2WorldToMap1.xyz, direction ), dot( g_vCS2WorldToMap2.xyz, direction ) ) );
}

float3 CS2QuaternionAxisX( float4 q )
{
	return float3( 1.0 - 2.0 * q.y * q.y - 2.0 * q.z * q.z, 2.0 * q.x * q.y + 2.0 * q.w * q.z, 2.0 * q.x * q.z - 2.0 * q.w * q.y );
}

float3 CS2QuaternionAxisY( float4 q )
{
	return float3( 2.0 * q.x * q.y - 2.0 * q.w * q.z, 1.0 - 2.0 * q.x * q.x - 2.0 * q.z * q.z, 2.0 * q.y * q.z + 2.0 * q.w * q.x );
}

// The game's direct light: Lambert diffuse, and for specular a GGX distribution with its visibility and fresnel
// folded into a single term (a4 / ( d² LdotH² ( 4a + 2 ) ), the mobile approximation), on the specular colour.
void CS2DirectLight( float3 normal, float3 toCamera, float3 toLight, float roughness, float3 specularColor, float3 color, inout float3 diffuse, inout float3 specular )
{
	float NdotL = max( 0.0, dot( normal, toLight ) );
	float3 halfVector = normalize( toLight + toCamera );
	float NdotH = dot( halfVector, normal );
	float LdotH = max( 0.0, dot( toLight, halfVector ) );

	float a2 = roughness * roughness;
	float a4 = a2 * a2;
	float d = NdotH * NdotH * ( a4 - 1.0 ) + 1.0;

	// Only keeps perfectly smooth highlights from dividing zero by zero.
	float denominator = max( d * d * LdotH * LdotH * ( roughness * 4.0 + 2.0 ), 1.17549435e-38 );

	specular += specularColor * ( a4 / denominator ) * NdotL * color;
	diffuse += NdotL * color;
}

// A barn light's shape: soft edged, square to round by a superellipse exponent.
float CS2BarnShape( float2 ndc, float4 shape )
{
	float2 a = abs( ndc );

	if ( shape.z == 0.0 )
		return smoothstep( 1.0, shape.x, a.x ) * smoothstep( 1.0, shape.y, a.y );

	float power = 2.0 / shape.z;
	float inverse = -0.5 * shape.z;
	float inner = shape.x * shape.y * pow( max( pow( shape.y * a.x, power ) + pow( shape.x * a.y, power ), 1.17549435e-38 ), inverse );
	float outer = pow( max( pow( a.x, power ) + pow( a.y, power ), 1.17549435e-38 ), inverse );

	return inner < outer ? smoothstep( outer, inner, 1.0 ) : ( outer > 1.0 ? 1.0 : 0.0 );
}

// One light2 face at a point: the direction to it and the colour it lights with, false where it doesn't reach. With
// smoothFalloff (the high variants, characters and weapons) its near and far skirts and its distance falloff ease in
// by smoothstep; the low variants clamp them linearly.
bool CS2BarnLightAt( CS2BarnLight light, float3 position, float4 bakedShadows, bool smoothFalloff, out float3 toLight, out float3 color )
{
	toLight = 0;
	color = 0;

	float4 p = float4( position, 1.0 );
	float4 clip = float4( dot( light.Frustum0, p ), dot( light.Frustum1, p ), dot( light.Frustum2, p ), dot( light.Frustum3, p ) );
	float3 ndc = clip.xyz / clip.w;

	if ( !all( ndc > float3( -1.0, -1.0, 0.0 ) ) || !all( ndc < 1.0 ) )
		return false;

	float3 box = float3( dot( light.Illumination0, p ), dot( light.Illumination1, p ), dot( light.Illumination2, p ) );
	if ( !all( abs( box ) <= 1.0 ) )
		return false;

	// The near and far skirts.
	float fade = 1.0;
	if ( light.DistanceFade.z > 0.0 )
		fade = smoothFalloff ? smoothstep( 0.0, 1.0, ndc.z * light.DistanceFade.z ) : min( 1.0, ndc.z * light.DistanceFade.z );
	if ( light.DistanceFade.w > 0.0 )
		fade *= smoothFalloff ? smoothstep( 0.0, 1.0, ( 1.0 - ndc.z ) * light.DistanceFade.w ) : min( 1.0, ( 1.0 - ndc.z ) * light.DistanceFade.w );

	float attenuation;

	if ( light.Position.w != 0.0 )
	{
		float3 delta = light.Position.xyz - position;
		float distanceSquared = dot( delta, delta );
		float distance = sqrt( distanceSquared );

		// The cone fades by the direction to the nearest point of the luminaire.
		float3 segment = CS2QuaternionAxisY( light.Orientation ) * light.AngleFade.z;
		float3 segmentStart = delta - segment;
		float3 segmentDirection = ( delta + segment ) - segmentStart;
		float along = dot( -segmentStart, segmentDirection );
		float3 nearest = along <= 0.0 ? segmentStart : segmentStart + segmentDirection * min( 1.0, along / dot( segmentDirection, segmentDirection ) );

		toLight = delta / distance;
		float falloff = light.DistanceFade.x + light.DistanceFade.y * distance;
		attenuation = fade * ( light.Position.w / max( distanceSquared, light.Position.w ) )
			* ( smoothFalloff ? smoothstep( 0.0, 1.0, falloff ) : saturate( falloff ) )
			* saturate( light.AngleFade.x + light.AngleFade.y * dot( CS2QuaternionAxisX( light.Orientation ), normalize( nearest ) ) );
	}
	else
	{
		toLight = light.Position.xyz;
		attenuation = fade;
	}

	color = light.Color.rgb * ( 1.0 - dot( bakedShadows, light.BakedShadowMask ) ) * attenuation;

	[branch]
	if ( light.Color.w == 0.0 )
	{
		color *= CS2BarnShape( ndc.xy, light.Shape );
	}
	else if ( light.Color.w < 0.0 )
	{
		// Spherical cookie, by the direction from the light in its own space.
		float3 q = light.Orientation.xyz;
		float w = light.Orientation.w;
		float3 v = -toLight;
		float s = -dot( v, q );
		float3 t = v * w + cross( v, q );
		float3 local = t * w - q * s + cross( -q, t );
		float2 uv = float2( atan2( local.y, -local.x ) * 0.159154937, acos( clamp( local.z, -1.0, 1.0 ) ) * 0.318309873 );
		color *= Bindless::GetTexture2D( (int)-light.Color.w ).SampleLevel( g_sTrilinearWrap, uv * light.Shape.zw + light.Shape.xy, 0 ).rgb;
	}
	else
	{
		float2 uv = ndc.xy * float2( 0.5, -0.5 ) + 0.5;
		color *= Bindless::GetTexture2D( (int)light.Color.w ).SampleLevel( g_sTrilinearClamp, uv * light.Shape.zw + light.Shape.xy, 0 ).rgb;
	}

	return any( color != 0.0 );
}

bool CS2BarnLightAt( CS2BarnLight light, float3 position, float4 bakedShadows, out float3 toLight, out float3 color )
{
	return CS2BarnLightAt( light, position, bakedShadows, false, toLight, color );
}

// With transmission, each light also adds what it lights the back of the surface with (backNormal faces away).
void CS2BarnLights( float3 position, float3 normal, float3 toCamera, float roughness, float3 specularColor, float4 bakedShadows, bool transmits, float3 backNormal, inout float3 diffuse, inout float3 specular, inout float3 transmitted )
{
	CS2GridRange range = CS2GridLookup( position, 1, (uint)g_nCS2BarnLightCount );

	[loop]
	for ( uint k = 0; k < range.Count; k++ )
	{
		CS2BarnLight light = g_CS2BarnLights[CS2GridItem( range, k )];

		float3 toLight;
		float3 color;
		if ( CS2BarnLightAt( light, position, bakedShadows, toLight, color ) )
		{
			CS2DirectLight( normal, toCamera, toLight, max( roughness, light.Parameters.x ), specularColor, color, diffuse, specular );
			if ( transmits )
				transmitted += max( 0.0, dot( backNormal, toLight ) ) * color;
		}
	}
}

// The sky light, for what the probe volumes leave uncovered: (normal, 1) dotted with a harmonic per channel.
float3 CS2Ambient( float3 normal )
{
	float4 n = float4( normal, 1.0 );
	return float3( dot( g_vCS2AmbientR, n ), dot( g_vCS2AmbientG, n ), dot( g_vCS2AmbientB, n ) );
}

// The light probe volumes at a point, front to back until they cover it: the ambient cubes' irradiance along the
// normal, and the stationary lights' baked shadows.
void CS2Probes( float3 position, float3 normal, out float3 irradiance, out float4 shadows )
{
	irradiance = 0;
	shadows = 0;

	float4 p = float4( position, 1.0 );
	float weight = 0.0;

	float3 faces = lerp( float3( 0.0, 1.0, 2.0 ) / 6.0, float3( 3.0, 4.0, 5.0 ) / 6.0, step( normal, 0.0 ) );
	float3 faceWeights = normal * normal;

	CS2GridRange range = CS2GridLookup( position, 0, (uint)g_nCS2ProbeVolumeCount );

	[loop]
	for ( uint k = 0; k < range.Count; k++ )
	{
		CS2ProbeVolume volume = g_CS2ProbeVolumes[CS2GridItem( range, k )];

		float3 box = float3( dot( volume.WorldToBox0, p ), dot( volume.WorldToBox1, p ), dot( volume.WorldToBox2, p ) );
		float3 fadeMin = saturate( ( box - volume.BoxMin.xyz ) * volume.Fade.xyz );
		float3 fadeMax = saturate( ( volume.BoxMax.xyz - box ) * volume.Fade.xyz );
		float edge = min( min( fadeMin.x, min( fadeMin.y, fadeMin.z ) ), min( fadeMax.x, min( fadeMax.y, fadeMax.z ) ) );
		if ( !( edge > 0.0 ) || volume.BorderMin.w == 0.0 )
			continue;

		float contribution = edge * edge * ( 3.0 - 2.0 * edge ) * ( 1.0 - weight );
		weight += contribution;

		// The atlas stacks the six ambient cube faces in depth.
		float3 local = float3( dot( volume.WorldToAtlas0, p ), dot( volume.WorldToAtlas1, p ), dot( volume.WorldToAtlas2, p ) );
		float3 coords = local;
		coords.z *= 1.0 / 6.0;
		coords = clamp( coords, volume.BorderMin.xyz, volume.BorderMax.xyz );
		coords.z *= 6.0;
		coords = coords * volume.AtlasScale.xyz + volume.AtlasOffset.xyz;
		coords.z *= 1.0 / 6.0;

		Texture3D cubes = Bindless::GetTexture3D( (int)volume.BorderMin.w );
		float3 cube = cubes.SampleLevel( g_sBilinearClamp, coords + float3( 0, 0, faces.x ), 0 ).rgb * faceWeights.x
			+ cubes.SampleLevel( g_sBilinearClamp, coords + float3( 0, 0, faces.y ), 0 ).rgb * faceWeights.y
			+ cubes.SampleLevel( g_sBilinearClamp, coords + float3( 0, 0, faces.z ), 0 ).rgb * faceWeights.z;

		irradiance += cube * volume.Tint.rgb * contribution;
		if ( volume.BorderMax.w != 0.0 )
			shadows += Bindless::GetTexture3D( (int)volume.BorderMax.w ).SampleLevel( g_sBilinearClamp, saturate( local ) * volume.AtlasScale.xyz + volume.AtlasOffset.xyz, 0 ) * contribution;

		if ( weight > 0.99 )
			break;
	}

	if ( weight < 0.99 )
		irradiance += CS2Ambient( normal ) * ( 1.0 - weight );
}

// An ambient cube read along a direction: each face by the squared component of the direction towards it.
float3 CS2AmbientCubeAlong( float3 direction, float3 positiveX, float3 positiveY, float3 positiveZ, float3 negativeX, float3 negativeY, float3 negativeZ )
{
	float3 positive = step( 0.0, direction ) * direction * direction;
	float3 negative = step( direction, 0.0 ) * direction * direction;
	return positiveX * positive.x + positiveY * positive.y + positiveZ * positive.z
		+ negativeX * negative.x + negativeY * negative.y + negativeZ * negative.z;
}

// csgo_foliage's probes: the volumes' whole ambient cubes (with the sky light filling the faces where they leave the
// point uncovered), read along the normal for the light in front and along backNormal for the light through it.
void CS2ProbesTwoSided( float3 position, float3 normal, float3 backNormal, out float3 irradiance, out float3 backIrradiance, out float4 shadows )
{
	float3 positiveX = 0, positiveY = 0, positiveZ = 0, negativeX = 0, negativeY = 0, negativeZ = 0;
	shadows = 0;

	float4 p = float4( position, 1.0 );
	float weight = 0.0;

	CS2GridRange range = CS2GridLookup( position, 0, (uint)g_nCS2ProbeVolumeCount );

	[loop]
	for ( uint k = 0; k < range.Count; k++ )
	{
		CS2ProbeVolume volume = g_CS2ProbeVolumes[CS2GridItem( range, k )];

		float3 box = float3( dot( volume.WorldToBox0, p ), dot( volume.WorldToBox1, p ), dot( volume.WorldToBox2, p ) );
		float3 fadeMin = saturate( ( box - volume.BoxMin.xyz ) * volume.Fade.xyz );
		float3 fadeMax = saturate( ( volume.BoxMax.xyz - box ) * volume.Fade.xyz );
		float edge = min( min( fadeMin.x, min( fadeMin.y, fadeMin.z ) ), min( fadeMax.x, min( fadeMax.y, fadeMax.z ) ) );
		if ( !( edge > 0.0 ) || volume.BorderMin.w == 0.0 )
			continue;

		float contribution = edge * edge * ( 3.0 - 2.0 * edge ) * ( 1.0 - weight );
		weight += contribution;

		float3 local = float3( dot( volume.WorldToAtlas0, p ), dot( volume.WorldToAtlas1, p ), dot( volume.WorldToAtlas2, p ) );
		float3 coords = local;
		coords.z *= 1.0 / 6.0;
		coords = clamp( coords, volume.BorderMin.xyz, volume.BorderMax.xyz );
		coords.z *= 6.0;
		coords = coords * volume.AtlasScale.xyz + volume.AtlasOffset.xyz;
		coords.z *= 1.0 / 6.0;

		Texture3D cubes = Bindless::GetTexture3D( (int)volume.BorderMin.w );
		float3 scale = volume.Tint.rgb * contribution;
		positiveX += cubes.SampleLevel( g_sBilinearClamp, coords, 0 ).rgb * scale;
		positiveY += cubes.SampleLevel( g_sBilinearClamp, coords + float3( 0, 0, 1.0 / 6.0 ), 0 ).rgb * scale;
		positiveZ += cubes.SampleLevel( g_sBilinearClamp, coords + float3( 0, 0, 2.0 / 6.0 ), 0 ).rgb * scale;
		negativeX += cubes.SampleLevel( g_sBilinearClamp, coords + float3( 0, 0, 3.0 / 6.0 ), 0 ).rgb * scale;
		negativeY += cubes.SampleLevel( g_sBilinearClamp, coords + float3( 0, 0, 4.0 / 6.0 ), 0 ).rgb * scale;
		negativeZ += cubes.SampleLevel( g_sBilinearClamp, coords + float3( 0, 0, 5.0 / 6.0 ), 0 ).rgb * scale;

		if ( volume.BorderMax.w != 0.0 )
			shadows += Bindless::GetTexture3D( (int)volume.BorderMax.w ).SampleLevel( g_sBilinearClamp, saturate( local ) * volume.AtlasScale.xyz + volume.AtlasOffset.xyz, 0 ) * contribution;

		if ( weight > 0.99 )
			break;
	}

	if ( weight < 0.99 )
	{
		float sky = 1.0 - weight;
		positiveX += CS2Ambient( float3( 1, 0, 0 ) ) * sky;
		positiveY += CS2Ambient( float3( 0, 1, 0 ) ) * sky;
		positiveZ += CS2Ambient( float3( 0, 0, 1 ) ) * sky;
		negativeX += CS2Ambient( float3( -1, 0, 0 ) ) * sky;
		negativeY += CS2Ambient( float3( 0, -1, 0 ) ) * sky;
		negativeZ += CS2Ambient( float3( 0, 0, -1 ) ) * sky;
	}

	irradiance = CS2AmbientCubeAlong( normal, positiveX, positiveY, positiveZ, negativeX, negativeY, negativeZ );
	backIrradiance = CS2AmbientCubeAlong( backNormal, positiveX, positiveY, positiveZ, negativeX, negativeY, negativeZ );
}

// Whether a cubemap volume claims a point over the best so far: one containing it over one that doesn't, then the
// more indoor, then the one whose origin is nearer; among volumes that don't contain it, the nearest
// (ValveResourceFormat's choice of the environment map for a position; the game's is unverified).
void CS2ConsiderCubemap( CS2CubemapVolume volume, float3 position, inout float slice, inout bool bestInside, inout float bestPriority, inout float bestScore )
{
	float4 p = float4( position, 1.0 );
	float3 local = float3( dot( volume.WorldToLocal0, p ), dot( volume.WorldToLocal1, p ), dot( volume.WorldToLocal2, p ) );
	float score = dot( local, local );
	bool inside;

	if ( volume.BoxMax.w != 0.0 )
	{
		float radius = volume.BoxMax.x;
		inside = score < radius * radius;
		if ( !inside )
		{
			float outside = sqrt( score ) - radius;
			score = outside * outside;
		}
	}
	else
	{
		float3 outside = max( volume.BoxMin.xyz - local, 0.0 ) + max( local - volume.BoxMax.xyz, 0.0 );
		float distance = dot( outside, outside );
		inside = distance == 0.0;
		if ( !inside )
			score = distance;
	}

	if ( inside != bestInside )
	{
		if ( !inside )
			return;
	}
	else if ( inside && volume.Center.w != bestPriority )
	{
		if ( volume.Center.w < bestPriority )
			return;
	}
	else if ( score > bestScore )
	{
		return;
	}

	slice = volume.BoxMin.w;
	bestInside = inside;
	bestPriority = volume.Center.w;
	bestScore = score;
}

// The cube a view reflects: the game binds one per view; it's chosen here by where the camera is.
float CS2ViewCubemapSlice()
{
	float3 camera = CS2ToMap( g_vCameraPositionWs );

	float slice = 0.0;
	bool inside = false;
	float priority = -3.402823e38;
	float score = 3.402823e38;

	CS2GridRange range = CS2GridLookup( camera, 2, (uint)g_nCS2CubemapVolumeCount );

	[loop]
	for ( uint k = 0; k < range.Count; k++ )
		CS2ConsiderCubemap( g_CS2CubemapVolumes[CS2GridItem( range, k )], camera, slice, inside, priority, score );

	// Outside every volume: the nearest of all of them.
	[branch]
	if ( !inside )
	{
		[loop]
		for ( uint k = 0; k < (uint)g_nCS2CubemapVolumeCount; k++ )
			CS2ConsiderCubemap( g_CS2CubemapVolumes[k], camera, slice, inside, priority, score );
	}

	return slice;
}

// env_gradient_fog and env_cubemap_fog at a map space point: how much of each covers it (each is zero nearer than it
// starts), and the cubemap fog's colour when asked for.
void CS2FogAt( float3 position, bool sampleCubemap, out float gradient, out float cubemap, out float3 cubemapColor )
{
	gradient = 0;
	cubemap = 0;
	cubemapColor = 0;

	float3 ray = position - CS2ToMap( g_vCameraPositionWs );
	float distanceSquared = dot( ray, ray );

	[branch]
	if ( g_nCS2GradientFog != 0 && distanceSquared > g_vCS2GradientFogCulling.x )
	{
		float2 f = saturate( g_vCS2GradientFogBiasAndScale.xy + g_vCS2GradientFogBiasAndScale.zw * float2( sqrt( distanceSquared ), position.z ) );
		gradient = pow( f.x, g_vCS2GradientFogExponents.x ) * pow( f.y, g_vCS2GradientFogExponents.y ) * g_vCS2GradientFogColor.w;
	}

	[branch]
	if ( g_nCS2CubemapFog != 0 && distanceSquared > g_vCS2CubemapFogParameters.x )
	{
		float f = saturate( pow( max( 0.0, sqrt( distanceSquared ) * g_vCS2CubemapFogDistance.y + g_vCS2CubemapFogDistance.x ), g_vCS2CubemapFogDistance.w ) )
			* saturate( pow( max( 0.0, position.z * g_vCS2CubemapFogHeight.y + g_vCS2CubemapFogHeight.x ), g_vCS2CubemapFogHeight.z ) );
		cubemap = saturate( f ) * g_vCS2CubemapFogParameters.w;

		[branch]
		if ( sampleCubemap )
		{
			float3 direction = normalize( float3( dot( g_vCS2CubemapFogRotation0, ray ), dot( g_vCS2CubemapFogRotation1, ray ), dot( g_vCS2CubemapFogRotation2, ray ) ) );
			float lod = g_vCS2CubemapFogHeight.w * saturate( 1.0 - f * g_vCS2CubemapFogDistance.z );
			cubemapColor = Bindless::GetTextureCube( g_nCS2CubemapFogTexture ).SampleLevel( g_sTrilinearClamp, direction, lod ).rgb * g_vCS2CubemapFogParameters.y;
		}
	}
}

// The fog over a surface's colour.
float3 CS2Fog( float3 color, float3 position )
{
	float gradient, cubemap;
	float3 cubemapColor;
	CS2FogAt( position, true, gradient, cubemap, cubemapColor );

	color = lerp( color, g_vCS2GradientFogColor.rgb, gradient );
	return lerp( color, cubemapColor, cubemap );
}

// How much of an additive surface the fog in front of it lets through: the game fades additive surfaces by the fog
// rather than tinting them, as what's behind them is fogged already.
float CS2FogTransmission( float3 position )
{
	float gradient, cubemap;
	float3 cubemapColor;
	CS2FogAt( position, false, gradient, cubemap, cubemapColor );
	return ( 1.0 - gradient ) * ( 1.0 - cubemap );
}

// A lightmap sampled as the game samples it, trilinear: in two parts (its top mip, and the mips below it), it is filtered
// across both as the GPU filters one texture with the whole chain. Magnified or at the top mip's level, the top mip
// bilinear; between the two, the blend of both by the fractional level; smaller, the lower mips' own trilinear.
float4 CS2SampleLightmap( int top, int lower, float2 uv, float2 dx, float2 dy )
{
	Texture2D topMip = Bindless::GetTexture2D( top );

	[branch]
	if ( lower == 0 )
		return topMip.SampleGrad( g_sTrilinearClamp, uv, dx, dy );

	float width, height;
	topMip.GetDimensions( width, height );
	float2 size = float2( width, height );
	float2 footprintX = dx * size;
	float2 footprintY = dy * size;
	float level = 0.5 * log2( max( dot( footprintX, footprintX ), dot( footprintY, footprintY ) ) );

	Texture2D lowerMips = Bindless::GetTexture2D( lower );

	[branch]
	if ( level >= 1.0 )
		return lowerMips.SampleGrad( g_sTrilinearClamp, uv, dx, dy );

	float4 filtered = topMip.SampleLevel( g_sTrilinearClamp, uv, 0 );

	[branch]
	if ( level > 0.0 )
		filtered = lerp( filtered, lowerMips.SampleLevel( g_sTrilinearClamp, uv, 0 ), level );

	return filtered;
}

// The light reaching a surface, before its material composites it: all in the map's space.
struct CS2LightTerms
{
	float3 Position;
	float3 Normal;
	float3 ToCamera;
	float3 SpecularColor;
	float3 Irradiance;				// baked: the lightmap or the probes, along the normal
	float3 Diffuse;					// direct: the constant ambient, the sun and the lights
	float3 Specular;				// the sun and the lights' specular
	float3 Transmitted;				// with transmission: what lights the surface from behind
};

CS2LightTerms CS2Light( CS2Surface s )
{
	CS2LightTerms l = (CS2LightTerms)0;

	// Everything from here on is in the map's space; only s&box's sun shadows stay in the world's.
	l.ToCamera = CS2DirectionToMap( CalculatePositionToCameraDirWs( s.Position ) );
	l.Position = CS2ToMap( s.Position );
	l.Normal = CS2DirectionToMap( s.Normal );
	l.SpecularColor = lerp( s.Reflectance.xxx, s.Albedo, s.Metalness );

	float3 position = l.Position;
	float3 normal = l.Normal;

	// Transmission comes through from behind the vertex normal.
	bool transmits = any( s.Transmission > 0.0 );
	float3 backNormal = -CS2DirectionToMap( s.GeometricNormal );

	//
	// Baked light: the lightmaps where they reach, the light probe volumes elsewhere
	//
	float4 bakedShadows;

	// Derivatives before branching per pixel.
	float2 lightmapUV = s.LightmapUV * g_vCS2LightmapUvScale;
	float2 lightmapDx = ddx( lightmapUV );
	float2 lightmapDy = ddy( lightmapUV );

	[branch]
	if ( s.LightmapUV.x >= 0.0 && g_nCS2LightmapIrradiance != 0 )
	{
		l.Irradiance = CS2SampleLightmap( g_nCS2LightmapIrradiance, g_nCS2LightmapIrradianceLowerMips, lightmapUV, lightmapDx, lightmapDy ).rgb;
		bakedShadows = g_nCS2LightmapShadows != 0 ? CS2SampleLightmap( g_nCS2LightmapShadows, g_nCS2LightmapShadowsLowerMips, lightmapUV, lightmapDx, lightmapDy ) : 0;
	}
	else if ( transmits )
	{
		// Lightmapped, the back of a transmissive surface gets no baked light; from the probes it does.
		CS2ProbesTwoSided( position, normal, backNormal, l.Irradiance, l.Transmitted, bakedShadows );
	}
	else
	{
		CS2Probes( position, normal, l.Irradiance, bakedShadows );
	}

	//
	// The sun: its baked shadow channel, times s&box's cascades for whatever moves
	//
	l.Diffuse = g_vCS2ConstantAmbient;

	float bakedSunVisibility = 1.0 - dot( bakedShadows, g_vCS2SunShadowMask );
	float sunVisibility = bakedSunVisibility;
	float sunFront = dot( g_vCS2SunDirection, normal );
	float sunBack = transmits ? max( 0.0, dot( g_vCS2SunDirection, backNormal ) ) : 0.0;

	[branch]
	if ( any( g_vCS2SunColor > 0.0 ) && max( sunFront, sunBack ) * sunVisibility > 0.0 )
	{
		if ( g_DirectionalLightEnabled && g_DirectionalLightCascadeCount > 0 )
			sunVisibility *= CS2SunShadow( s.PositionWithOffset, s.ShadowReceiverNormal, s.ScreenPosition );

		if ( sunFront > 0.0 && sunVisibility > 0.0 )
			CS2DirectLight( normal, l.ToCamera, g_vCS2SunDirection, max( s.Roughness, g_flCS2SunMinRoughness ), l.SpecularColor, g_vCS2SunColor * sunVisibility, l.Diffuse, l.Specular );

		l.Transmitted += sunBack * g_vCS2SunColor * ( s.TransmissionShadows ? sunVisibility : bakedSunVisibility );
	}

	CS2BarnLights( position, normal, l.ToCamera, s.Roughness, l.SpecularColor, bakedShadows, transmits, backNormal, l.Diffuse, l.Specular, l.Transmitted );

	return l;
}

// The view's cubemap reflected off a surface, brought to the light around it (before ambient occlusion).
float3 CS2EnvironmentSpecular( CS2LightTerms l, float roughness )
{
	[branch]
	if ( g_nCS2EnvironmentMaps == 0 )
		return 0;

	// The split sum's reflection energy from the analytic fit to it (Karis).
	float r2 = roughness * roughness;
	float4 c = float4( -1.0, -0.0275, -0.572, 0.022 ) * roughness + float4( 1.0, 0.0425, 1.04, -0.04 );
	float a004 = min( c.x * c.x, exp2( -9.28 * max( 0.0, dot( l.ToCamera, l.Normal ) ) ) ) * c.x + c.y;
	float2 ab = float2( -1.04, 1.04 ) * a004 + c.zw;
	float3 environmentBrdf = ab.y + ab.x * l.SpecularColor;

	// Along the reflection, bent towards the normal as roughness grows; blurrier mips for rougher surfaces.
	float a = saturate( 1.0 - r2 );
	float3 direction = normalize( lerp( l.Normal, reflect( -l.ToCamera, l.Normal ), a * ( sqrt( a ) + r2 ) ) );
	float3 cube = Bindless::GetTextureCubeArray( g_nCS2EnvironmentMaps ).SampleLevel( g_sTrilinearClamp, float4( direction, CS2ViewCubemapSlice() ), g_flCS2EnvironmentMapMips * sqrt( roughness ) ).rgb;

	// The one cube is brought to the light around the surface: its colour and (up to a point) brightness.
	float3 light = l.Diffuse + l.Irradiance;
	cube *= normalize( light + 0.001 ) * clamp( length( light ), 0.0, 1.75 );
	cube *= 1.0 + normalize( l.Irradiance + 0.001 ) * 0.5;

	return environmentBrdf * cube;
}

float3 CS2ShadeLow( CS2Surface s )
{
	CS2LightTerms l = CS2Light( s );

	float ao = s.AmbientOcclusion;
	float3 bakedDiffuse = l.Irradiance * ao;
	if ( s.Snow )
		bakedDiffuse = pow( bakedDiffuse, float3( 1.05, 0.95, 0.8 ) );

	float3 color = ( l.Diffuse + bakedDiffuse ) * ( s.Albedo * ( 1.0 - s.Metalness ) );

	if ( s.SpecularDirect )
		color += l.Specular * ao;

	[branch]
	if ( s.SpecularIndirect )
		color += CS2EnvironmentSpecular( l, s.Roughness ) * ao;

	color += l.Transmitted * s.Transmission;
	color += s.Emission;
	return s.Fog ? CS2Fog( color, l.Position ) : color;
}

//=========================================================================================================================
// The lighting of what moves: csgo_character and csgo_weapon (decompiled with Source 2 Viewer: their probe lit
// variants). Unlike the low world variants' approximation above, each light is GGX over two roughnesses (along a tangent
// and a bitangent, equal for isotropic surfaces) with Smith visibility (k = ( r + 1 )² / 8, r the larger roughness) and
// Schlick's fresnel, with cloth's sheen lobe (Charlie) blended in by its mask; the lights' falloffs ease in by smoothstep;
// the cubemap is weighted by the game's split sum table (textures/dev/brdf_lut, layer 1, the CS2BRDFLookup attribute)
// with multiple scattering; and the baked irradiance is scaled down by what the specular takes. Characters also get a rim
// from the ambient cube (CS2PbrRim) and their skin a softened diffuse. The high world variants (CS2ShadeHigh) share it.
//
// Not ported, as in the world shaders: the screen space ambient occlusion and realtime light2 shadows; also the game's
// AO based light occlusion of characters (a per view switch whose value isn't known) and its wetness.
// The cubemap volumes blend as the game blends them where the map's blend data is given (CS2BlendedCubemaps); without
// it the view's cube reflects, brought to the light the way CS2EnvironmentSpecular does (unverified substitute).
//=========================================================================================================================

// An ambient cube's six faces.
struct CS2AmbientCube
{
	float3 PositiveX;
	float3 PositiveY;
	float3 PositiveZ;
	float3 NegativeX;
	float3 NegativeY;
	float3 NegativeZ;
};

float3 CS2CubeAlong( CS2AmbientCube cube, float3 direction )
{
	return CS2AmbientCubeAlong( direction, cube.PositiveX, cube.PositiveY, cube.PositiveZ, cube.NegativeX, cube.NegativeY, cube.NegativeZ );
}

// The probe volumes' ambient cubes at a point, front to back until they cover it (without the sky's fill), how much
// they cover it, and their baked shadows.
CS2AmbientCube CS2ProbeCube( float3 position, out float coverage, out float4 shadows )
{
	CS2AmbientCube cube = (CS2AmbientCube)0;
	shadows = 0;
	coverage = 0.0;

	float4 p = float4( position, 1.0 );
	CS2GridRange range = CS2GridLookup( position, 0, (uint)g_nCS2ProbeVolumeCount );

	[loop]
	for ( uint k = 0; k < range.Count; k++ )
	{
		CS2ProbeVolume volume = g_CS2ProbeVolumes[CS2GridItem( range, k )];

		float3 box = float3( dot( volume.WorldToBox0, p ), dot( volume.WorldToBox1, p ), dot( volume.WorldToBox2, p ) );
		float3 fadeMin = saturate( ( box - volume.BoxMin.xyz ) * volume.Fade.xyz );
		float3 fadeMax = saturate( ( volume.BoxMax.xyz - box ) * volume.Fade.xyz );
		float edge = min( min( fadeMin.x, min( fadeMin.y, fadeMin.z ) ), min( fadeMax.x, min( fadeMax.y, fadeMax.z ) ) );
		if ( !( edge > 0.0 ) || volume.BorderMin.w == 0.0 )
			continue;

		float contribution = edge * edge * ( 3.0 - 2.0 * edge ) * ( 1.0 - coverage );
		coverage += contribution;

		float3 local = float3( dot( volume.WorldToAtlas0, p ), dot( volume.WorldToAtlas1, p ), dot( volume.WorldToAtlas2, p ) );
		float3 coords = local;
		coords.z *= 1.0 / 6.0;
		coords = clamp( coords, volume.BorderMin.xyz, volume.BorderMax.xyz );
		coords.z *= 6.0;
		coords = coords * volume.AtlasScale.xyz + volume.AtlasOffset.xyz;
		coords.z *= 1.0 / 6.0;

		Texture3D cubes = Bindless::GetTexture3D( (int)volume.BorderMin.w );
		float3 scale = volume.Tint.rgb * contribution;
		cube.PositiveX += cubes.SampleLevel( g_sBilinearClamp, coords, 0 ).rgb * scale;
		cube.PositiveY += cubes.SampleLevel( g_sBilinearClamp, coords + float3( 0, 0, 1.0 / 6.0 ), 0 ).rgb * scale;
		cube.PositiveZ += cubes.SampleLevel( g_sBilinearClamp, coords + float3( 0, 0, 2.0 / 6.0 ), 0 ).rgb * scale;
		cube.NegativeX += cubes.SampleLevel( g_sBilinearClamp, coords + float3( 0, 0, 3.0 / 6.0 ), 0 ).rgb * scale;
		cube.NegativeY += cubes.SampleLevel( g_sBilinearClamp, coords + float3( 0, 0, 4.0 / 6.0 ), 0 ).rgb * scale;
		cube.NegativeZ += cubes.SampleLevel( g_sBilinearClamp, coords + float3( 0, 0, 5.0 / 6.0 ), 0 ).rgb * scale;

		if ( volume.BorderMax.w != 0.0 )
			shadows += Bindless::GetTexture3D( (int)volume.BorderMax.w ).SampleLevel( g_sBilinearClamp, saturate( local ) * volume.AtlasScale.xyz + volume.AtlasOffset.xyz, 0 ) * contribution;

		if ( coverage > 0.99 )
			break;
	}

	return cube;
}

// The cube with the sky light filling each face where the volumes leave the point uncovered.
CS2AmbientCube CS2FillCubeWithSky( CS2AmbientCube cube, float coverage )
{
	if ( coverage < 0.99 )
	{
		float sky = 1.0 - coverage;
		cube.PositiveX += CS2Ambient( float3( 1, 0, 0 ) ) * sky;
		cube.PositiveY += CS2Ambient( float3( 0, 1, 0 ) ) * sky;
		cube.PositiveZ += CS2Ambient( float3( 0, 0, 1 ) ) * sky;
		cube.NegativeX += CS2Ambient( float3( -1, 0, 0 ) ) * sky;
		cube.NegativeY += CS2Ambient( float3( 0, -1, 0 ) ) * sky;
		cube.NegativeZ += CS2Ambient( float3( 0, 0, -1 ) ) * sky;
	}

	return cube;
}

// What a moving surface is, for CS2PbrShade. Directions are world space.
struct CS2PbrSurface
{
	float3 Position;				// world space
	float3 PositionWithOffset;		// camera relative, for s&box's sun shadows
	float4 ScreenPosition;
	float3 ShadowReceiverNormal;	// from screen derivatives, taken in uniform control flow
	float3 Normal;					// the shading normal
	float3 VertexNormal;			// the interpolated vertex normal, flipped on back faces
	float3 Tangent;					// Roughness.x is along it, Roughness.y along Bitangent
	float3 Bitangent;
	float3 DiffuseColor;			// what the diffuse light multiplies
	float3 SpecularColor;
	float2 Roughness;
	float Cloth;					// the sheen lobe's amount
	float AmbientOcclusion;
	float3 Emission;
	bool Fog;

	// csgo_character's skin (F_SUBSURFACE_SCATTERING): its mask, the unflipped vertex frame's normal, the normal
	// softness per channel, the falloff table's curvature coordinate.
	float Subsurface;
	float3 SubsurfaceNormal;
	float3 NormalSoftness;
	float Curvature;

	// csgo_character's rim (CS2PbrRim): on, and its amount (the rim mask, AO masking and normal derivative fade).
	bool Rim;
	float RimAmount;
	bool NoRealtimeSunShadow;		// a debug switch: the sun without s&box's cascades
};

CS2PbrSurface CS2PbrSurfaceDefaults()
{
	CS2PbrSurface s = (CS2PbrSurface)0;
	s.AmbientOcclusion = 1.0;
	s.Fog = true;
	return s;
}

// One light's specular, times NdotL (the caller's light colour is times NdotL's diffuse separately).
float3 CS2PbrSpecular( float3 normal, float3 tangent, float3 bitangent, float3 toCamera, float3 toLight, float2 roughness, float3 specularColor, float cloth )
{
	float NdotL = max( 0.0, dot( normal, toLight ) );
	float3 h = normalize( toLight + toCamera );
	float NdotH = dot( h, normal );
	float NdotV = max( 0.0, dot( normal, toCamera ) );

	float2 a = roughness * roughness;
	float3 stretched = float3( dot( h, tangent ), dot( h, bitangent ), NdotH ) / float3( a, 1.0 );
	float q = dot( stretched, stretched );
	float r1 = max( roughness.x, roughness.y ) + 1.0;
	float k = r1 * ( r1 * 0.125 );
	float term = 1.0 / ( ( q * ( ( q * a.x ) * a.y ) ) * ( ( 4.0 * ( NdotL * ( 1.0 - k ) + k ) ) * ( NdotV * ( 1.0 - k ) + k ) ) );

	[branch]
	if ( cloth > 0.0 )
	{
		float r = dot( roughness, 0.5 );
		float r2 = max( r * r, 9.99999975e-06 );
		float v = saturate( NdotV + 0.001 );
		float sheen = ( ( ( 2.0 + 1.0 / r2 ) * pow( max( 0.0, 1.0 - NdotH * NdotH ), 0.5 / r2 ) ) / ( ( NdotL + v ) - NdotL * v ) ) * 0.124999918 * NdotL;
		term = lerp( term, sheen, cloth );
	}

	float3 fresnel = specularColor + ( 1.0 - specularColor ) * pow( max( 9.99999997e-07, 1.0 - max( 0.0, dot( toLight, h ) ) ), 5.0 );
	return fresnel * term * NdotL;
}

// One light's diffuse: NdotL; skin's per channel along normals softened towards the vertex frame's, more so where it
// faces away, plus the diffuse falloff table at the half Lambert of the vertex frame's normal.
float3 CS2PbrDiffuse( CS2PbrSurface s, float3 toLight, Texture2D diffuseFalloff )
{
	float NdotL = max( 0.0, dot( s.Normal, toLight ) );

	[branch]
	if ( s.Subsurface <= 0.0 )
		return NdotL;

	float softNdotL = dot( s.SubsurfaceNormal, toLight );
	float wrap = saturate( 1.0 - softNdotL );
	wrap *= wrap;

	float3 soft = s.NormalSoftness;
	float3 perChannel = float3(
		saturate( dot( normalize( lerp( s.Normal, s.SubsurfaceNormal, soft.x + wrap * ( 1.0 - soft.x ) ) ), toLight ) ),
		saturate( dot( normalize( lerp( s.Normal, s.SubsurfaceNormal, soft.y + wrap * ( 1.0 - soft.y ) ) ), toLight ) ),
		saturate( dot( normalize( lerp( s.Normal, s.SubsurfaceNormal, soft.z + wrap * ( 1.0 - soft.z ) ) ), toLight ) ) );

	float3 falloff = diffuseFalloff.SampleLevel( g_sTrilinearClamp, float2( softNdotL * 0.5 + 0.5, s.Curvature ), 0 ).rgb * 0.5 - 0.25;
	return lerp( NdotL.xxx, saturate( perChannel + falloff ), s.Subsurface );
}

// The rim csgo_character lights with: two sheen lobes of the ambient cube along the normal, one towards a direction
// above and to the side of the view (the view's up crossed with its direction; the view constants' members are
// identified from ValveResourceFormat's layout, unverified), one towards where the cube is brightest; nearer than 800
// units it's weaker. The game takes the brighter of it and the cubemap's reflection.
float3 CS2PbrRim( CS2AmbientCube cube, float3 irradiance, float3 normal, float3 toCamera, float distanceToCamera )
{
	float3 luma = float3( 0.2125, 0.7154, 0.0721 );
	float3 gradient = float3( dot( cube.NegativeX, luma ) - dot( cube.PositiveX, luma ), dot( cube.NegativeY, luma ) - dot( cube.PositiveY, luma ),
		dot( cube.PositiveZ, luma ) - dot( cube.NegativeZ, luma ) );

	float NdotH = dot( normalize( normal + toCamera ), normal );
	float sin2 = 1.0 - NdotH * NdotH;
	float v = saturate( max( 0.0, dot( toCamera, normal ) ) + 0.001 );
	float distanceFade = smoothstep( 0.0, 800.0, distanceToCamera );
	float offset = distanceFade * 0.123;

	float3 rimDirection = CS2DirectionToMap( normalize( float3( cross( g_vCameraUpDirWs, g_vCameraDirWs ).xy, 1.0 ) ) );
	float towardsRim = max( 0.0, dot( normal, rimDirection ) );
	float towardsBright = max( 0.0, dot( normal, normalize( length( gradient ) == 0.0 ? float3( 0, 0, 1 ) : gradient ) ) );

	float3 rim = normalize( irradiance + 0.001 ) * max( lerp( 0.025, 0.1, distanceFade ), length( irradiance ) * 0.4 )
		* smoothstep( 0.125 - offset, 0.3 - offset, ( ( pow( max( 0.0, sin2 ), 0.5 ) / ( ( towardsRim + v ) - towardsRim * v ) ) * 0.374999762 ) * towardsRim );
	float3 bright = irradiance * ( ( ( pow( max( 0.0, sin2 ), 2.17013884 ) / ( ( towardsBright + v ) - towardsBright * v ) ) * 0.792534113 ) * towardsBright );
	return max( rim, bright );
}

// The view's cube along a direction, brought to the light around the surface as CS2EnvironmentSpecular does.
float3 CS2PbrCubemap( float3 direction, float roughness, float3 diffuseLight, float3 irradiance )
{
	[branch]
	if ( g_nCS2EnvironmentMaps == 0 )
		return 0;

	float3 cube = Bindless::GetTextureCubeArray( g_nCS2EnvironmentMaps ).SampleLevel( g_sTrilinearClamp, float4( direction, CS2ViewCubemapSlice() ), g_flCS2EnvironmentMapMips * sqrt( roughness ) ).rgb;
	float3 light = diffuseLight + irradiance;
	cube *= normalize( light + 0.001 ) * clamp( length( light ), 0.0, 1.75 );
	cube *= 1.0 + normalize( irradiance + 0.001 ) * 0.5;
	return cube;
}

// The split sum table at a roughness and N.V: the reflectance at F0 = 0 in x, at F0 = 1 in y. Without the table bound,
// the analytic fit the low variants use, rearranged the same way (a stand in, unverified against the table).
float2 CS2BrdfLookup( float roughness, float NdotV )
{
	[branch]
	if ( g_nCS2BRDFLookup != 0 )
		return Bindless::GetTexture2DArray( g_nCS2BRDFLookup ).SampleLevel( g_sBilinearClamp, float3( float2( roughness, sqrt( 1.0 - NdotV ) ) * 0.984375 + 0.0078125, 1.0 ), 0 ).xy;

	float4 c = float4( -1.0, -0.0275, -0.572, 0.022 ) * roughness + float4( 1.0, 0.0425, 1.04, -0.04 );
	float a004 = min( c.x * c.x, exp2( -9.28 * NdotV ) ) * c.x + c.y;
	float2 ab = float2( -1.04, 1.04 ) * a004 + c.zw;
	return float2( ab.y, ab.x + ab.y );
}

// The cubemap volumes around a point blended as the game blends them (decompiled: the high variants, characters and
// weapons): in order, each faded in by a smoothstep of its distance from its box's nearest edge over its fade width
// (times what the ones before it left), until they cover the point; each read along the reflection carried onto its
// box, less so the rougher the surface, times its tint; their normalization harmonics blended alike. False without
// the blend data.
bool CS2BlendedCubemaps( float3 position, float3 direction, float roughness, out float3 cube, out float4 normalizationSH )
{
	cube = 0;
	normalizationSH = 0;

	[branch]
	if ( g_nCS2EnvironmentMaps == 0 || g_nCS2CubemapBlendCount <= 0 || g_nCS2CubemapBlendCount != g_nCS2CubemapVolumeCount )
		return false;

	TextureCubeArray cubes = Bindless::GetTextureCubeArray( g_nCS2EnvironmentMaps );
	float mip = g_flCS2EnvironmentMapMips * sqrt( roughness );
	float4 p = float4( position, 1.0 );
	float weight = 0.01;

	CS2GridRange range = CS2GridLookup( position, 2, (uint)g_nCS2CubemapVolumeCount );

	[loop]
	for ( uint k = 0; k < range.Count; k++ )
	{
		uint index = CS2GridItem( range, k );
		CS2CubemapVolume volume = g_CS2CubemapVolumes[index];
		CS2CubemapBlend blend = g_CS2CubemapBlend[index];

		float3 local = float3( dot( volume.WorldToLocal0, p ), dot( volume.WorldToLocal1, p ), dot( volume.WorldToLocal2, p ) );
		float3 fadeMin = saturate( ( local - volume.BoxMin.xyz ) * blend.InvEdgeWidth.xyz );
		float3 fadeMax = saturate( ( volume.BoxMax.xyz - local ) * blend.InvEdgeWidth.xyz );
		float edge = min( min( fadeMin.x, min( fadeMin.y, fadeMin.z ) ), min( fadeMax.x, min( fadeMax.y, fadeMax.z ) ) );
		if ( edge == 0.0 )
			continue;

		// Where the reflection leaves the box, from the box's centre (x / 0 gives the infinities that drop that axis).
		float3 localDirection = float3( dot( volume.WorldToLocal0.xyz, direction ), dot( volume.WorldToLocal1.xyz, direction ), dot( volume.WorldToLocal2.xyz, direction ) );
		float3 exits = max( ( volume.BoxMax.xyz - local ) / localDirection, ( volume.BoxMin.xyz - local ) / localDirection );
		float3 lookup = lerp( local + localDirection * abs( min( exits.x, min( exits.y, exits.z ) ) ), localDirection, roughness );

		float contribution = edge * edge * ( 3.0 - 2.0 * edge ) * ( 1.0 - weight );
		weight += contribution;
		cube += cubes.SampleLevel( g_sTrilinearWrap, float4( lookup, volume.BoxMin.w ), mip ).rgb * blend.Tint.rgb * contribution;
		normalizationSH += blend.NormalizationSH * contribution;

		if ( weight > 0.99 )
			break;
	}

	cube /= weight;
	normalizationSH /= weight;
	return true;
}

// The environment a surface reflects, before the split sum. With the blend data, the blended cubes brought to the light
// at the surface as the game does: by the luminance of its baked irradiance over the cubes' own along the normal, the
// brightening capped at max( roughness * 2 + 1, 1 ) (the game's per view scale and bias for that cap aren't known:
// ValveResourceFormat's, unverified). Without it, the view's cube as CS2PbrCubemap brings it to the light.
float3 CS2ReflectedEnvironment( float3 position, float3 direction, float roughness, float3 normal, float3 diffuseLight, float3 irradiance )
{
	float3 cube;
	float4 normalizationSH;

	[branch]
	if ( CS2BlendedCubemaps( position, direction, roughness, cube, normalizationSH ) )
		return cube * min( dot( irradiance, float3( 0.2125, 0.7154, 0.0721 ) ) / dot( float4( normal, 1.0 ), normalizationSH ), max( roughness * 2.0 + 1.0, 1.0 ) );

	return CS2PbrCubemap( direction, roughness, diffuseLight, irradiance );
}

float3 CS2PbrShade( CS2PbrSurface s, Texture2D diffuseFalloff )
{
	// In the map's space, as everything else.
	float3 position = CS2ToMap( s.Position );
	float3 normal = CS2DirectionToMap( s.Normal );
	float3 toCamera = CS2DirectionToMap( CalculatePositionToCameraDirWs( s.Position ) );
	float3 tangent = CS2DirectionToMap( s.Tangent );
	float3 bitangent = CS2DirectionToMap( s.Bitangent );

	CS2PbrSurface m = s;
	m.Normal = normal;
	m.SubsurfaceNormal = CS2DirectionToMap( s.SubsurfaceNormal );
	float3 vertexNormal = CS2DirectionToMap( s.VertexNormal );

	//
	// The probes: the irradiance along the normal (the sky along it where they leave the point uncovered), the whole cube
	//
	float coverage;
	float4 bakedShadows;
	CS2AmbientCube probes = CS2ProbeCube( position, coverage, bakedShadows );
	float3 irradiance = CS2CubeAlong( probes, normal );
	if ( coverage < 0.99 )
		irradiance += CS2Ambient( normal ) * ( 1.0 - coverage );

	CS2AmbientCube cube = CS2FillCubeWithSky( probes, coverage );

	//
	// The sun and the lights
	//
	float3 diffuse = g_vCS2ConstantAmbient;
	float3 specular = 0;

	float sunVisibility = 1.0 - dot( bakedShadows, g_vCS2SunShadowMask );

	[branch]
	if ( any( g_vCS2SunColor > 0.0 ) && dot( g_vCS2SunDirection, normal ) * sunVisibility > 0.0 )
	{
		if ( !s.NoRealtimeSunShadow && g_DirectionalLightEnabled && g_DirectionalLightCascadeCount > 0 )
			sunVisibility *= CS2SunShadow( s.PositionWithOffset, s.ShadowReceiverNormal, s.ScreenPosition );

		float3 color = g_vCS2SunColor * sunVisibility;
		diffuse += CS2PbrDiffuse( m, g_vCS2SunDirection, diffuseFalloff ) * color;
		specular += CS2PbrSpecular( normal, tangent, bitangent, toCamera, g_vCS2SunDirection, max( s.Roughness, g_flCS2SunMinRoughness ), s.SpecularColor, s.Cloth ) * color;
	}

	CS2GridRange range = CS2GridLookup( position, 1, (uint)g_nCS2BarnLightCount );

	[loop]
	for ( uint k = 0; k < range.Count; k++ )
	{
		CS2BarnLight light = g_CS2BarnLights[CS2GridItem( range, k )];

		float3 toLight;
		float3 color;
		if ( CS2BarnLightAt( light, position, bakedShadows, true, toLight, color ) )
		{
			diffuse += CS2PbrDiffuse( m, toLight, diffuseFalloff ) * color;
			specular += CS2PbrSpecular( normal, tangent, bitangent, toCamera, toLight, max( s.Roughness, light.Parameters.x ), s.SpecularColor, s.Cloth ) * color;
		}
	}

	//
	// The cubemap: along the reflection, bent towards the normal as roughness grows, and for anisotropic surfaces
	// across the rougher direction; weighted by the split sum table, cloth's by its sheen
	//
	float roughness = dot( s.Roughness, 0.5 );
	float NdotV = max( 0.0, dot( toCamera, normal ) );
	float3 reflectNormal = normal;

	[branch]
	if ( s.Roughness.x != s.Roughness.y )
	{
		float2 ratio = s.Roughness.xy / s.Roughness.yx;
		float3 across = ratio.x >= ratio.y ? bitangent : tangent;
		reflectNormal = normalize( lerp( normal, normalize( cross( cross( toCamera, across ), across ) ), ( 1.0 - min( ratio.x, ratio.y ) ) * 0.5 ) );
		reflectNormal = lerp( reflectNormal, normal, s.Cloth );
	}

	float r2 = roughness * roughness;
	float a = saturate( 1.0 - r2 );
	float3 direction = normalize( lerp( reflectNormal, reflect( -toCamera, reflectNormal ), a * ( sqrt( a ) + r2 ) ) );

	float2 table = CS2BrdfLookup( roughness, NdotV );
	float3 environmentBrdf = lerp( table.xxx, table.yyy, s.SpecularColor );

	[branch]
	if ( s.Cloth > 0.0 )
	{
		float sheenNdotH = dot( normalize( normal + toCamera ), normal );
		float sheenR2 = max( r2, 9.99999975e-06 );
		float v = saturate( NdotV + 0.001 );
		environmentBrdf = lerp( environmentBrdf, s.SpecularColor * ( ( ( ( 2.0 + 1.0 / sheenR2 ) * pow( max( 0.0, 1.0 - sheenNdotH * sheenNdotH ), 0.5 / sheenR2 ) ) / ( ( 1.0 + v ) - v ) ) * 0.124999918 ), s.Cloth );
	}

	// Multiple scattering, from the table's second term and the average fresnel; not for cloth.
	float notCloth = 1.0 - s.Cloth;
	float missing = 1.0 - table.y;
	float3 averageFresnel = s.SpecularColor + ( 1.0 - s.SpecularColor ) * 0.0476190485;
	float3 multipleScattering = ( ( environmentBrdf * averageFresnel ) / ( 1.0 - averageFresnel * missing ) ) * missing;

	// The direct specular's own multiple scattering.
	float roughnessSum = s.Roughness.x + s.Roughness.y;
	float roughnessSum2 = roughnessSum * roughnessSum;
	float3 specularScale = lerp( 1.0 + s.SpecularColor * ( 0.125 * ( roughnessSum2 * roughnessSum2 ) * saturate( dot( normal, toCamera ) ) ), 1.0, s.Cloth );

	float3 indirect = CS2ReflectedEnvironment( position, direction, roughness, normal, diffuse, irradiance ) * ( environmentBrdf + multipleScattering * notCloth );

	[branch]
	if ( s.Rim )
		indirect = max( CS2PbrRim( cube, irradiance, normal, toCamera, distance( s.Position, g_vCameraPositionWs ) ) * s.RimAmount, indirect );

	//
	// Skin's irradiance per channel along its softened normals
	//
	float3 baked = irradiance;
	[branch]
	if ( s.Subsurface > 0.0 )
	{
		float3 soft = s.NormalSoftness * s.Subsurface;
		baked = float3(
			CS2CubeAlong( cube, normalize( lerp( normal, vertexNormal, soft.x ) ) ).x,
			CS2CubeAlong( cube, normalize( lerp( normal, vertexNormal, soft.y ) ) ).y,
			CS2CubeAlong( cube, normalize( lerp( normal, vertexNormal, soft.z ) ) ).z );
	}

	float ao = s.AmbientOcclusion;
	float3 color = ( diffuse + baked * lerp( 1.0, 1.0 - ( environmentBrdf + multipleScattering ), notCloth ) * ao ) * s.DiffuseColor;
	color += specular * specularScale * ao;
	color += indirect * ao;
	color += s.Emission;

	return s.Fog ? CS2Fog( color, position ) : color;
}

//=========================================================================================================================
// The world shaders' high variants: the game's S_SHADER_QUALITY 1, its "Shader Detail: High" (decompiled with Source 2
// Viewer: the high variants of csgo_vertexlitgeneric, csgo_lightmappedgeneric, csgo_environment and its blend,
// csgo_complex, csgo_foliage, csgo_glass, csgo_static_overlay, csgo_simple_2way_blend and csgo_water_fancy, lightmapped
// and probe lit). The baked light is the low variants', except that the lightmap's directional data turns its
// irradiance towards the normal map; the sun and the lights are GGX as for characters (CS2PbrSpecular), the lights'
// falloffs eased by smoothstep; the cubemap is weighted by the split sum table with multiple scattering, and the baked
// diffuse scaled down by what that specular takes; the direct specular gains its multiple scattering too.
//
// Not ported: the bicubic filtering of the lightmapped shadows (the game's switch for it compares per view and per
// lightmap constants that aren't known: bilinear, as the low variants read them), the per view bias added to the
// directional lightmap's directionality (taken as 0, unverified), the screen space ambient occlusion.
//=========================================================================================================================

bool CS2HighQuality()
{
	return g_nCS2ShaderQuality != 0;
}

// How flat the surface is over a lightmap texel, for the directional lightmap: its vertex normal's change across the
// pixel against its position's. Derivatives: call it in uniform control flow.
float CS2LightmapFlatness( float3 vertexNormal, float3 position )
{
	return smoothstep( 0.1, 0.01, length( fwidth( vertexNormal ) ) / length( fwidth( position ) ) ) * 0.8;
}

// The lightmap's irradiance turned towards a tangent space normal by its directional texel: the directional part over
// the light's dominant direction (kept 5 degrees above the surface, steeper or shallower on flat surfaces), the rest as
// it is.
float3 CS2DirectionalIrradiance( float3 irradiance, float4 directional, float3 tangentNormal, float flatness )
{
	float2 xy = directional.xy * 2.0 - 1.0;
	xy *= 0.996190846 / max( 0.996190846, length( xy ) );
	float z = sqrt( 1.0 - dot( xy, xy ) );
	float3 dominant = normalize( float3( xy, z * lerp( 1.0, lerp( 0.1, 2.0, saturate( ( 1.0 - z ) * 1.5 ) ), flatness ) ) );

	float3 nonDirectional = irradiance * saturate( directional.z );
	return nonDirectional + ( ( irradiance - nonDirectional ) / dominant.z ) * max( 0.0, dot( dominant, tangentNormal ) );
}

// A tangent and bitangent around a normal, for isotropic GGX (any perpendicular pair gives the same).
void CS2Perpendiculars( float3 normal, out float3 tangent, out float3 bitangent )
{
	float3 up = abs( normal.z ) < 0.999 ? float3( 0.0, 0.0, 1.0 ) : float3( 1.0, 0.0, 0.0 );
	tangent = normalize( cross( up, normal ) );
	bitangent = cross( normal, tangent );
}

CS2LightTerms CS2LightHigh( CS2Surface s )
{
	CS2LightTerms l = (CS2LightTerms)0;

	// Everything from here on is in the map's space; only s&box's sun shadows stay in the world's.
	l.ToCamera = CS2DirectionToMap( CalculatePositionToCameraDirWs( s.Position ) );
	l.Position = CS2ToMap( s.Position );
	l.Normal = CS2DirectionToMap( s.Normal );
	l.SpecularColor = lerp( s.Reflectance.xxx, s.Albedo, s.Metalness );

	float3 position = l.Position;
	float3 normal = l.Normal;

	bool transmits = any( s.Transmission > 0.0 );
	float3 backNormal = -CS2DirectionToMap( s.GeometricNormal );

	// Derivatives before branching per pixel.
	float2 lightmapUV = s.LightmapUV * g_vCS2LightmapUvScale;
	float2 lightmapDx = ddx( lightmapUV );
	float2 lightmapDy = ddy( lightmapUV );
	float flatness = CS2LightmapFlatness( s.GeometricNormal, s.Position );

	//
	// Baked light: the lightmaps where they reach (turned by the directional one), the light probe volumes elsewhere
	//
	float4 bakedShadows;

	[branch]
	if ( s.LightmapUV.x >= 0.0 && g_nCS2LightmapIrradiance != 0 )
	{
		l.Irradiance = CS2SampleLightmap( g_nCS2LightmapIrradiance, g_nCS2LightmapIrradianceLowerMips, lightmapUV, lightmapDx, lightmapDy ).rgb;
		bakedShadows = g_nCS2LightmapShadows != 0 ? CS2SampleLightmap( g_nCS2LightmapShadows, g_nCS2LightmapShadowsLowerMips, lightmapUV, lightmapDx, lightmapDy ) : 0;

		[branch]
		if ( g_nCS2LightmapDirectional != 0 )
			l.Irradiance = CS2DirectionalIrradiance( l.Irradiance, CS2SampleLightmap( g_nCS2LightmapDirectional, g_nCS2LightmapDirectionalLowerMips, lightmapUV, lightmapDx, lightmapDy ), s.TangentNormal, flatness );
	}
	else if ( transmits )
	{
		CS2ProbesTwoSided( position, normal, backNormal, l.Irradiance, l.Transmitted, bakedShadows );
	}
	else
	{
		CS2Probes( position, normal, l.Irradiance, bakedShadows );
	}

	//
	// The sun: its baked shadow channel, times s&box's cascades for whatever moves
	//
	l.Diffuse = g_vCS2ConstantAmbient;

	float3 tangent, bitangent;
	CS2Perpendiculars( normal, tangent, bitangent );

	float bakedSunVisibility = 1.0 - dot( bakedShadows, g_vCS2SunShadowMask );
	float sunVisibility = bakedSunVisibility;
	float sunFront = dot( g_vCS2SunDirection, normal );
	float sunBack = transmits ? max( 0.0, dot( g_vCS2SunDirection, backNormal ) ) : 0.0;

	[branch]
	if ( any( g_vCS2SunColor > 0.0 ) && max( sunFront, sunBack ) * sunVisibility > 0.0 )
	{
		if ( g_DirectionalLightEnabled && g_DirectionalLightCascadeCount > 0 )
			sunVisibility *= CS2SunShadow( s.PositionWithOffset, s.ShadowReceiverNormal, s.ScreenPosition );

		float3 color = g_vCS2SunColor * sunVisibility;
		if ( sunFront > 0.0 && sunVisibility > 0.0 )
		{
			l.Diffuse += sunFront * color;
			l.Specular += CS2PbrSpecular( normal, tangent, bitangent, l.ToCamera, g_vCS2SunDirection, max( s.Roughness, g_flCS2SunMinRoughness ).xx, l.SpecularColor, 0.0 ) * color;
		}

		l.Transmitted += sunBack * g_vCS2SunColor * ( s.TransmissionShadows ? sunVisibility : bakedSunVisibility );
	}

	//
	// The lights
	//
	CS2GridRange range = CS2GridLookup( position, 1, (uint)g_nCS2BarnLightCount );

	[loop]
	for ( uint k = 0; k < range.Count; k++ )
	{
		CS2BarnLight light = g_CS2BarnLights[CS2GridItem( range, k )];

		float3 toLight;
		float3 color;
		if ( CS2BarnLightAt( light, position, bakedShadows, true, toLight, color ) )
		{
			l.Diffuse += max( 0.0, dot( normal, toLight ) ) * color;
			l.Specular += CS2PbrSpecular( normal, tangent, bitangent, l.ToCamera, toLight, max( s.Roughness, light.Parameters.x ).xx, l.SpecularColor, 0.0 ) * color;
			if ( transmits )
				l.Transmitted += max( 0.0, dot( backNormal, toLight ) ) * color;
		}
	}

	return l;
}

// The split sum's weights for the cubemap: the table's reflectance at the roughness and N.V, and multiple scattering
// (from the table's second term and the average fresnel).
void CS2EnvironmentBrdfHigh( float3 specularColor, float roughness, float NdotV, out float3 brdf, out float3 multipleScattering )
{
	float2 table = CS2BrdfLookup( roughness, NdotV );
	brdf = lerp( table.xxx, table.yyy, specularColor );

	float missing = 1.0 - table.y;
	float3 averageFresnel = specularColor + ( 1.0 - specularColor ) * 0.0476190485;
	multipleScattering = ( ( brdf * averageFresnel ) / ( 1.0 - averageFresnel * missing ) ) * missing;
}

// The direct specular's own multiple scattering: 1 + F0 * ( 2 roughness )^4 / 8 * N.V.
float3 CS2DirectSpecularScale( float3 specularColor, float roughness, float NdotV )
{
	float roughnessSum2 = ( roughness * 2.0 ) * ( roughness * 2.0 );
	return 1.0 + specularColor * ( 0.125 * ( roughnessSum2 * roughnessSum2 ) * saturate( NdotV ) );
}

// The cubemap a surface reflects (before the split sum): along the reflection, bent towards the normal as roughness
// grows, through the blended volumes.
float3 CS2EnvironmentHigh( CS2LightTerms l, float roughness )
{
	float r2 = roughness * roughness;
	float a = saturate( 1.0 - r2 );
	float3 direction = normalize( lerp( l.Normal, reflect( -l.ToCamera, l.Normal ), a * ( sqrt( a ) + r2 ) ) );
	return CS2ReflectedEnvironment( l.Position, direction, roughness, l.Normal, l.Diffuse, l.Irradiance );
}

float3 CS2ShadeHigh( CS2Surface s )
{
	CS2LightTerms l = CS2LightHigh( s );

	float ao = s.AmbientOcclusion;
	float NdotV = max( 0.0, dot( l.ToCamera, l.Normal ) );

	float3 brdf = 0;
	float3 multipleScattering = 0;
	if ( s.SpecularIndirect )
		CS2EnvironmentBrdfHigh( l.SpecularColor, s.Roughness, NdotV, brdf, multipleScattering );

	float3 bakedDiffuse = l.Irradiance * ( 1.0 - ( brdf + multipleScattering ) ) * ao;
	if ( s.Snow )
		bakedDiffuse = pow( bakedDiffuse, float3( 1.05, 0.95, 0.8 ) );

	float3 color = ( l.Diffuse + bakedDiffuse ) * ( s.Albedo * ( 1.0 - s.Metalness ) );

	if ( s.SpecularDirect )
		color += l.Specular * CS2DirectSpecularScale( l.SpecularColor, s.Roughness, NdotV ) * ao;

	[branch]
	if ( s.SpecularIndirect )
		color += CS2EnvironmentHigh( l, s.Roughness ) * ( brdf + multipleScattering ) * ao;

	color += l.Transmitted * s.Transmission;
	color += s.Emission;
	return s.Fog ? CS2Fog( color, l.Position ) : color;
}

// A world surface lit as the shader detail setting asks.
float3 CS2Shade( CS2Surface s )
{
	[branch]
	if ( CS2HighQuality() )
		return CS2ShadeHigh( s );

	return CS2ShadeLow( s );
}

#endif // CS2_LIGHTING_HLSL
