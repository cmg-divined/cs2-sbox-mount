#ifndef CS2_VERTEX_HLSL
#define CS2_VERTEX_HLSL

// Vertex processing for the mount's compact vertices (cs2_vertexinput.hlsl): s&box's VS_CommonProcessing with
// the game's packed tangent frame and lightmap coordinates decoded here rather than by the engine.

// The engine's vertex helpers: transforms, instancing data, the compute skinning cache. Without
// common/vertexinput.hlsl, its own VS_CommonProcessing is left out.
#include "vr_common_vs_code.fxc"

// The 32 bit tangent frame (VertexDecoding.UnpackTangentFrame in the mount): an octahedral normal in bits 12 to 31,
// the tangent's rotation around it in bits 1 to 11, the bitangent sign in bit 0.
void DecodeCS2TangentFrame( uint packed, out float3 normal, out float4 tangent )
{
	float x = float( ( packed >> 12 ) & 0x3FF ) / 1023.0 * 2.0 - 1.0;
	float y = float( ( packed >> 22 ) & 0x3FF ) / 1023.0 * 2.0 - 1.0;
	float z = 1.0 - abs( x ) - abs( y );

	// Points below the octahedron's equator fold back over it.
	float fold = saturate( -z );
	x += x >= 0.0 ? -fold : fold;
	y += y >= 0.0 ? -fold : fold;
	normal = normalize( float3( x, y, z ) );

	// A tangent derived from the normal alone, then rotated into place by the stored angle.
	float tangentSign = normal.z >= 0.0 ? 1.0 : -1.0;
	float reciprocal = 1.0 / ( tangentSign + normal.z );
	float3 reference = float3(
		-tangentSign * normal.x * normal.x * reciprocal + 1.0,
		-tangentSign * normal.x * normal.y * reciprocal,
		-tangentSign * normal.x );

	float angle = float( ( packed >> 1 ) & 0x7FF ) / 2047.0 * 6.28318530718;
	float s, c;
	sincos( angle, s, c );

	tangent.xyz = reference * c + cross( normal, reference ) * s;
	tangent.w = ( packed & 1 ) != 0 ? 1.0 : -1.0;
}

// Two 16 bit fractions, or (-1, -1) for geometry the lightmaps don't cover (all bits set).
float2 DecodeCS2LightmapUV( uint packed )
{
	if ( packed == 0xFFFFFFFF )
		return float2( -1.0, -1.0 );

	return float2( packed & 0xFFFF, packed >> 16 ) / 65535.0;
}

PixelInput ProcessCS2Vertex( VertexInput i )
{
	PixelInput o;

	ExtraShaderData_t extraShaderData = GetExtraPerInstanceShaderData( i.nInstanceTransformID );
	o.vVertexColor.rgba = extraShaderData.vTint;

	#if ( D_CS_VERTEX_ANIMATION )
	{
		uint nVertexId = extraShaderData.nVertexCacheOffset + uint( i.nVertexCacheIndex );
		CachedAnimatedVertex_t vertex = FetchCachedVertex( nVertexId );

		float3 vNormal;
		float3 vTangent;
		float flSign;
		DecodeCachedVertexTangentSpace( vertex, vNormal, vTangent, flSign );

		o.vPositionWs.xyz = vertex.vPosWs;
		o.vNormalWs.xyz = vNormal;
		o.vTangentUWs.xyz = vTangent;
		o.vTangentVWs.xyz = cross( vNormal, vTangent ) * flSign;
	}
	#else
	{
		float3 vNormalOs;
		float4 vTangentUOs_flTangentVSign;
		DecodeCS2TangentFrame( i.nTangentFrame, vNormalOs, vTangentUOs_flTangentVSign );

		float3x4 matObjectToWorld = GetTransformMatrix( i.nInstanceTransformID, i.nBoneIndex.x );
		o.vPositionWs = mul( matObjectToWorld, float4( i.vPositionOs.xyz, 1.0 ) );

		float flTransformSign;
		o.vNormalWs.xyz = TransformNormalInverseTranspose( vNormalOs.xyz, (float3x3)matObjectToWorld, flTransformSign );

		// The tangent, kept perpendicular to the normal.
		float3 vTangentUWs = mul( matObjectToWorld, float4( vTangentUOs_flTangentVSign.xyz, 0.0 ) );
		vTangentUWs.xyz = normalize( vTangentUWs.xyz - ( o.vNormalWs.xyz * dot( vTangentUWs.xyz, o.vNormalWs.xyz ) ) );

		o.vTangentUWs.xyz = vTangentUWs.xyz;
		o.vTangentVWs.xyz = cross( o.vNormalWs.xyz, vTangentUWs.xyz ) * vTangentUOs_flTangentVSign.w * flTransformSign;
	}
	#endif

	o.vPositionPs.xyzw = Position3WsToPs( o.vPositionWs.xyz );
	o.vTextureCoords.xy = i.vTexCoord.xy;
	o.vTextureCoords.zw = i.vTexCoord2.xy;
	o.vLightmapUV.xy = DecodeCS2LightmapUV( i.nLightmapUV );

	return o;
}

// A 3D skybox's objects are drawn behind the map, as the game draws its skybox in a pass of its own before the map:
// their depth squeezed towards the far plane (reverse Z, towards zero), still in order among themselves.
float g_flCS2SkyboxDepthScale < Attribute( "CS2SkyboxDepthScale" ); Default( 1.0 ); >;

// The first person view model is drawn squeezed towards the eye, so it never reaches into walls, but lit where the game
// draws it, at its true size in front of the eye: these map the drawn position and directions along the surface (rows of
// an affine transform) and the normal (rows of its inverse transpose) to the true ones. Identity for everything else.
float4 g_vCS2LightingTransform0 < Attribute( "CS2LightingTransform0" ); Default4( 1.0, 0.0, 0.0, 0.0 ); >;
float4 g_vCS2LightingTransform1 < Attribute( "CS2LightingTransform1" ); Default4( 0.0, 1.0, 0.0, 0.0 ); >;
float4 g_vCS2LightingTransform2 < Attribute( "CS2LightingTransform2" ); Default4( 0.0, 0.0, 1.0, 0.0 ); >;
float3 g_vCS2LightingNormal0 < Attribute( "CS2LightingNormal0" ); Default3( 1.0, 0.0, 0.0 ); >;
float3 g_vCS2LightingNormal1 < Attribute( "CS2LightingNormal1" ); Default3( 0.0, 1.0, 0.0 ); >;
float3 g_vCS2LightingNormal2 < Attribute( "CS2LightingNormal2" ); Default3( 0.0, 0.0, 1.0 ); >;

PixelInput FinalizeCS2Vertex( PixelInput i )
{
	PixelInput o = i;

	float3x3 directions = float3x3( g_vCS2LightingTransform0.xyz, g_vCS2LightingTransform1.xyz, g_vCS2LightingTransform2.xyz );
	float3x3 normals = float3x3( g_vCS2LightingNormal0, g_vCS2LightingNormal1, g_vCS2LightingNormal2 );
	o.vPositionWs.xyz = mul( directions, o.vPositionWs.xyz ) + float3( g_vCS2LightingTransform0.w, g_vCS2LightingTransform1.w, g_vCS2LightingTransform2.w );
	o.vNormalWs.xyz = normalize( mul( normals, o.vNormalWs.xyz ) );
	o.vTangentUWs.xyz = normalize( mul( directions, o.vTangentUWs.xyz ) );
	o.vTangentVWs.xyz = normalize( mul( directions, o.vTangentVWs.xyz ) );

	o.vPositionWs.xyz -= g_vHighPrecisionLightingOffsetWs.xyz;
	o.vPositionPs.z *= g_flCS2SkyboxDepthScale;
	return o;
}

#endif
