//=========================================================================================================================
// CS2's csgo_water_fancy.vfx: canals, pools, fountains and coasts (anubis, nuke, aztec, inferno, pool_day).
//
// From the game's compiled shader (decompiled with Source 2 Viewer: anubis_water_canal's lightmapped variant, with
// refraction, caustics and interaction effects, and nuke_water's). The surface's normal is built from up to six layers
// of the waves texture drifting at the wave speed, plus foam and floating debris painted in by the vertex blend values
// (x roughness, y foam, z debris); the scene behind it, read through the frame buffer copy at an offset the normal and
// the water's depth give, is darkened, lit by caustics and fogged by the depth of water in front of it (the decay and
// fog colours); the low end cube map reflects over it by a fresnel term, and the sun's highlight on top. The water's
// own light comes from the lightmap (or the probes) at a point below the surface, the sun with its shadows, and the
// lights; then the map's fog. Near the shore it fades into the unrefracted scene by the edge hardness.
//
// Labelled substitutes (unverified): the scene's depth and colour come from s&box (Depth::GetWorldPosition, the frame
// buffer copy, with the game's linear depth differences as distances along the view); the realtime sun shadow is taken
// at the surface, not the game's point below it, and without its distance fade; the 3D skybox's objects (the renderer's
// CS2SkyboxDepthScale below 1) take the game's skybox view branch, assumed drawn at full scale (the skybox scale
// factor is 1). The view constant read as time is identified from its use.
//
// High variant (cs2_lighting.hlsl's CS2ShaderQuality, default): each wave layer read twice, the lightmap turned by the
// directional lightmap, the lights' falloffs eased by smoothstep, and the map's blended cubemap volumes reflected in
// place of the low end cube map (which stays the fallback without their blend data).
//
// Not ported: the screen space reflections of F_REFLECTION_TYPE 2 (the cube map alone reflects, as the game's fallback
// does), the interaction effects (the game's water effects buffer of ripples, foam and silt from players and
// grenades: none here), rain, the order independent transparency the game composites over it, the particle shadow
// buffer. Materials without F_REFRACTION (one, in anubis' skybox) take the skybox branch.
//=========================================================================================================================

HEADER
{
	Description = "CS2 csgo_water_fancy";
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
	float4 vBlendValues : TEXCOORD9;	// the painted values: x roughness, y foam, z debris
};

VS
{
	#include "cs2_vertex.hlsl"

	float g_flWaterPlaneOffset < Default( 0 ); >;

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessCS2Vertex( v );

		// Outside the 3D skybox the surface is raised along its normal by the plane offset, and pulled back across the
		// view by up to 20 times it.
		if ( g_flCS2SkyboxDepthScale >= 1.0 && g_flWaterPlaneOffset != 0.0 )
		{
			float3 position = i.vPositionWs.xyz;
			float3 toCamera = -normalize( position - g_vCameraPositionWs );
			float2 across = ( -toCamera.xy / toCamera.z ) * 0.7;

			position += normalize( i.vNormalWs.xyz ) * g_flWaterPlaneOffset;
			if ( dot( across, across ) > 0.0 )
				position.xy -= normalize( across ) * clamp( length( across ), 0.0, 20.0 ) * g_flWaterPlaneOffset;

			i.vPositionWs.xyz = position;
			i.vPositionPs = Position3WsToPs( position );
		}

		i.vBlendValues = v.vBlendParams;
		return FinalizeCS2Vertex( i );
	}
}

PS
{
	#define BLEND_MODE_ALREADY_SET 1
	#define DEPTH_STATE_ALREADY_SET 1
	#define CULL_MODE_ALREADY_SET 1

	#include "common/pixel.hlsl"
	#include "common/classes/Light.hlsl"
	#include "cs2_lighting.hlsl"

	// Drawn after the opaque world, over the copy of it, writing its own colour and depth (the game's is opaque too).
	BoolAttribute( bWantsFBCopyTexture, true );
	RenderState( DepthEnable, true );
	RenderState( DepthFunc, GREATER_EQUAL );
	RenderState( DepthWriteEnable, true );
	RenderState( BlendEnable, false );

	CreateInputTexture2D( CS2WavesNormalHeight, Linear, 8, "None", "_normal", "Waves", Default4( 0.5, 0.5, 0.5, 0 ) );
	CreateInputTexture2D( CS2Foam, Linear, 8, "None", "_mask", "Foam", Default4( 0.5, 0.5, 0, 0 ) );
	CreateInputTexture2D( CS2Debris, Srgb, 8, "None", "_color", "Debris", Default4( 1, 1, 1, 0 ) );
	CreateInputTexture2D( CS2DebrisNormal, Linear, 8, "None", "_normal", "Debris", Default4( 0.5, 0.5, 0.5, 0 ) );
	CreateInputTextureCube( CS2LowEndCubeMap, Linear, 8, "None", "_cube", "Reflections", Default4( 0, 0, 0, 0 ) );

	Texture2D g_tWavesNormalHeight < Channel( RGBA, Box( CS2WavesNormalHeight ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tFoam < Channel( RGBA, Box( CS2Foam ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tDebris < Channel( RGBA, Box( CS2Debris ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tDebrisNormal < Channel( RGBA, Box( CS2DebrisNormal ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	CreateTextureCube( g_tLowEndCubeMap ) < Channel( RGBA, Box( CS2LowEndCubeMap ), Linear ); OutputFormat( RGBA8888 ); SrgbRead( false ); >;

	Texture2D g_tFrameBufferCopyTexture < Attribute( "FrameBufferCopyTexture" ); SrgbRead( false ); >;

	TextureAttribute( RepresentativeTexture, g_tDebris );

	// Set by the mount: F_REFRACTION, F_CAUSTICS, whether the material has its cube, back faces' flipped normals.
	float g_flRefraction < Default( 1 ); >;
	float g_flCaustics < Default( 0 ); >;
	float g_flHasLowEndCube < Default( 0 ); >;
	float g_flFlipBackfaceNormals < Default( 0 ); >;
	float g_flFogEnabled < Default( 1 ); >;

	// The 3D skybox's objects (cs2_vertex.hlsl's attribute, here for the pixel shader).
	float g_flCS2SkyboxDepthScalePs < Attribute( "CS2SkyboxDepthScale" ); Default( 1.0 ); >;

	// The material, with the game's defaults; colours as its expressions make them (linear where they linearize).
	float g_flWaterPlaneOffset < Default( 0 ); >;
	float g_flSkyBoxFadeRange < Default( 0.05 ); >;
	float2 g_vMapUVMin < Default2( -10272, -10199 ); >;
	float2 g_vMapUVMax < Default2( 10241, 10241 ); >;
	float g_flLowEndCubeMapIntensity < Default( 1 ); >;
	float g_flWaterRoughnessMin < Default( 0.25 ); >;
	float g_flWaterRoughnessMax < Default( 1 ); >;
	float g_flFoamMin < Default( 0 ); >;
	float g_flFoamMax < Default( 1 ); >;
	float g_flDebrisMin < Default( 0 ); >;
	float g_flDebrisMax < Default( 1 ); >;
	float3 g_vDebrisTint < Default3( 0.7, 0.7, 0.7 ); >;
	float g_flDebrisReflectance < Default( 0.1 ); >;
	float g_flDebrisOilyness < Default( 0.1 ); >;
	float g_flDebrisNormalStrength < Default( 1 ); >;
	float g_flDebrisEdgeSharpness < Default( 10 ); >;
	float g_flDebrisScale < Default( 1 ); >;
	float g_flDebrisWobble < Default( 1 ); >;
	float g_flFoamScale < Default( 1 ); >;
	float g_flFoamWobble < Default( 1 ); >;
	float3 g_vFoamColor < Default3( 0.7, 0.7, 0.7 ); >;
	float g_flWavesHeightOffset < Default( 1 ); >;
	float g_flWavesSharpness < Default( 0.5 ); >;
	float g_flFresnelExponent < Default( 5 ); >;
	float g_flWavesNormalStrength < Default( 1 ); >;
	float g_flWavesNormalJitter < Default( 0.1 ); >;
	float2 g_vWaveScale < Default2( 15, 15 ); >;
	float g_flWaterInitialDirection < Default( 1.5 ); >;
	float g_flWavesSpeed < Default( 1 ); >;
	float g_flLowFreqWeight < Default( 0.5 ); >;
	float g_flMedFreqWeight < Default( 0.5 ); >;
	float g_flHighFreqWeight < Default( 0.5 ); >;
	float g_flWavesPhaseOffset < Default( 0.4 ); >;
	float g_flEdgeHardness < Default( 10 ); >;
	float g_flEdgeShapeEffect < Default( 1 ); >;
	float g_flWaveIterations < Default( 3 ); >;
	float3 g_vWaterFogColor < Default3( 0.5, 0.5, 0.5 ); >;
	float g_flRefractionLimit < Default( 0.1 ); >;
	float g_flWaterFogStrength < Default( 0.5 ); >;
	float3 g_vWaterDecayColor < Default3( 1, 1, 1 ); >;
	float g_flWaterDecayStrength < Default( 8 ); >;
	float g_flWaterMaxDepth < Default( 100 ); >;
	float g_flWaterFogShadowStrength < Default( 0.5 ); >;
	float g_flUnderwaterDarkening < Default( 0.6 ); >;
	float g_flSpecularPower < Default( 300 ); >;
	float g_flSpecularNormalMultiple < Default( 2 ); >;
	float g_flSpecularBloomBoostStrength < Default( 100 ); >;
	float g_flSpecularBloomBoostThreshold < Default( 0.7 ); >;
	float g_flTriplanarCaustics < Default( 0 ); >;
	float g_flCausticUVScaleMultiple < Default( 2.5 ); >;
	float g_flCausticDistortion < Default( 0.5 ); >;
	float g_flCausticsStrength < Default( 40 ); >;
	float g_flCausticSharpness < Default( 0.5 ); >;
	float g_flCausticDepthFallOffDistance < Default( 100 ); >;
	float g_flCausticShadowCutOff < Default( 0.2 ); >;
	float3 g_vCausticsTint < Default3( 0.5, 0.5, 0.5 ); >;
	float g_flReflectance < Default( 0.2 ); >;
	float g_flReflectionDistanceEffect < Default( 0.5 ); >;
	float g_flEnvironmentMapBrightness < Default( 1 ); >;
	float2 g_vRoughness < Default2( 0.25, 0.25 ); >;		// max( 0.01, 1 - glossiness )

	float WaterLuma( float3 color )
	{
		return dot( color, float3( 0.2125, 0.7154, 0.0721 ) );
	}

	// Where one layer of the waves texture is read: its scale, and its drift along its direction at the wave speed.
	float2 WaveUv( float2 uv, float2 scale, float direction )
	{
		return uv / scale + float2( sin( direction ), cos( direction ) ) * ( g_flTime * g_flWavesSpeed * 0.5 ) * sqrt( 1.0 / scale );
	}

	float3 SceneColor( float2 uv )
	{
		return g_tFrameBufferCopyTexture.SampleLevel( g_sTrilinearClamp, saturate( uv ) * g_vFrameBufferCopyInvSizeAndUvScale.zw, 0 ).rgb;
	}

	// The map space point of the scene behind a screen position (0 to 1).
	float3 ScenePosition( float2 uv )
	{
		return CS2ToMap( Depth::GetWorldPosition( saturate( uv ) / g_vInvViewportSize ) );
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init( i.vPositionWithOffsetWs, i.vPositionSs );

		// Derivatives need uniform control flow, so the shadow receiver normal is taken before branching.
		float3 receiverNormal = ComputeShadowReceiverNormal( m.WorldPositionWithOffset );

		bool skybox = g_flCS2SkyboxDepthScalePs < 1.0;
		bool deep = !skybox && g_flRefraction > 0.5;	// the game's branch with the scene behind (not in skybox views)

		float3 position = CS2ToMap( i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz );
		float3 camera = CS2ToMap( g_vCameraPositionWs );
		float3 cameraForward = CS2DirectionToMap( g_vCameraDirWs );

		float3 vertexNormal = normalize( i.vNormalWs.xyz );
		#if S_RENDER_BACKFACES
			if ( g_flFlipBackfaceNormals > 0.5 && !i.face )
				vertexNormal = -vertexNormal;
		#endif
		vertexNormal = CS2DirectionToMap( vertexNormal );

		float4 noise = g_tBlueNoise.Load( int3( uint2( i.vPositionSs.xy ) % TextureDimensions2D( g_tBlueNoise, 0 ).xy, 0 ) );
		float2 screenUv = i.vPositionSs.xy * g_vInvViewportSize;

		float3 view = normalize( position - camera );
		float3 toCamera = -view;
		float distanceToCamera = length( position - camera );
		float3 parallax = skybox ? 0.0 : float3( toCamera.xy / toCamera.z, sqrt( toCamera.z ) );
		float2 flow = -toCamera.xy / ( toCamera.z + 0.25 );

		//
		// The scene behind: its depth, colour, and the depth of water over it (hundredths of units along the view)
		//
		float depth01 = 1.0;
		float lumaBias = 0.0;
		float depthDiff = 1.0;
		float3 floorPosition = 0;
		float3 behind0 = 0;

		[branch]
		if ( deep )
		{
			depth01 = Depth::GetNormalized( i.vPositionSs.xy );
			behind0 = SceneColor( screenUv );
			lumaBias = clamp( WaterLuma( behind0 ), 0.0, 0.4 ) * -0.03;
			floorPosition = ScenePosition( screenUv );
			depthDiff = max( dot( floorPosition - position, cameraForward ), 0.0 ) * 0.01 + lumaBias;
		}

		float depthAboveFloor = max( 0.0, depthDiff - 0.02 );
		float depthAlongView = depthDiff * toCamera.z;

		float roughness = skybox ? g_flWaterRoughnessMax : max( 0.0, lerp( g_flWaterRoughnessMin, g_flWaterRoughnessMax, i.vBlendValues.x ) );
		float debrisAmount = skybox ? 0.0 : max( 0.0, lerp( g_flDebrisMin, g_flDebrisMax, i.vBlendValues.z ) );
		float foamAmount = skybox ? 0.0 : max( 0.0, lerp( g_flFoamMin, g_flFoamMax, i.vBlendValues.y ) );

		float2 waveUv = ( position + parallax * ( 0.5 - g_flWaterPlaneOffset ) ).xy * 0.0333333351;
		float2 waveUvDx = ddx( waveUv );
		float2 waveUvDy = ddy( waveUv );
		float distanceEffect = 0.5 * pow( max( dot( waveUvDx, waveUvDx ), dot( waveUvDy, waveUvDy ) ), 0.1 ) * g_flReflectionDistanceEffect;

		//
		// The waves: each layer smaller by the phase offset and turned, its slope and height summed. The high variant reads
		// each layer twice: the second time where the first moved it to, which gives the slope and height.
		//
		bool high = CS2HighQuality();
		int iterations = (int)g_flWaveIterations;
		float2 offset = 0;
		float2 phase = 0;
		float2 slopeSum = 0;
		float heightSum = 0;
		float2 scale = g_vWaveScale;
		float direction = g_flWaterInitialDirection;

		for ( int k = 0; k < iterations; k++ )
		{
			float t = ( float( k ) / ( float( iterations ) - 1.0 ) ) * 2.0;
			float weight = lerp( lerp( g_flLowFreqWeight, g_flMedFreqWeight, saturate( t ) ), g_flHighFreqWeight * roughness, saturate( t - 1.0 ) );
			float3 s = g_tWavesNormalHeight.SampleBias( g_sAniso, WaveUv( waveUv + offset * 3.0 + phase, scale, direction ), -clamp( distanceEffect, 0.0, 0.5 ) ).xyz - 0.5;

			float h = s.z * weight * length( scale ) * 0.01;
			float2 g = s.xy * 2.0;
			g = float2( g.x * min( 1.0, scale.y / scale.x ), g.y * min( 1.0, scale.x / scale.y ) ) * ( weight * 0.1 );

			float2 offsetStep = -flow * h * g_flWavesHeightOffset * roughness;
			float2 phaseStep = g * g_flWavesSharpness * scale * g_flWavesPhaseOffset;

			[branch]
			if ( high )
			{
				float3 moved = g_tWavesNormalHeight.SampleBias( g_sAniso, WaveUv( waveUv + ( offset + offsetStep ) * 3.0 + phase + phaseStep, scale, direction ), -clamp( distanceEffect, 0.0, 0.5 ) ).xyz - 0.5;
				h = moved.z * weight * length( scale ) * 0.01;
				g = moved.xy * 2.0;
				g = float2( g.x * min( 1.0, scale.y / scale.x ), g.y * min( 1.0, scale.x / scale.y ) ) * ( weight * 0.1 );
			}

			offset += offsetStep;
			phase += phaseStep;
			heightSum += h;
			slopeSum += g;
			scale *= g_flWavesPhaseOffset;
			direction += 3.5 / float( k + 1 );
		}

		float2 wobble = phase * 0.1;
		float2 slope = slopeSum * roughness;
		float waveHeight = heightSum * roughness * 60.0;

		// The floor's own normal behind the water, from its derivatives, and how much the edge shapes the surface there.
		float3 edgeNormal = float3( 0, 0, 1 );
		float edgeShape = g_flEdgeShapeEffect;

		// Derivatives need uniform control flow, so they're taken before branching.
		float3 floorShifted = floorPosition + parallax * clamp( WaterLuma( behind0 ), 0.0, 0.4 );
		float3 floorDx = ddx( floorShifted );
		float3 floorDy = ddy( floorShifted );
		float depthWidth = fwidth( depth01 );

		[branch]
		if ( deep )
		{
			float3 n = -normalize( cross( floorDx, floorDy ) );
			edgeShape = g_flEdgeShapeEffect * saturate( 1.2 - n.z * ( 1.0 - saturate( depthAlongView * 8.0 ) ) );
			n.xy += ( noise.xy - 0.5 ) * 0.05;
			edgeNormal = n;
		}

		float3 surface = position + parallax * ( lerp( 0.5, waveHeight, edgeShape ) - g_flWaterPlaneOffset );
		float3 foamPosition = position + parallax * ( lerp( 0.5, waveHeight, edgeShape * 0.5 ) - g_flWaterPlaneOffset );
		float timeWarp = g_flTime * 3.0 + sin( g_flTime * 0.5 ) * 0.1;
		float2 swirl = float2( sin( timeWarp + surface.y * 0.07 ), cos( timeWarp + surface.x * 0.07 ) );

		//
		// Foam: two readings of its texture, swirling where the water is deep enough
		//
		float2 foamUv = foamPosition.xy / g_flFoamScale;
		float2 foamUvMoved = foamUv + wobble * g_flFoamWobble * 0.5 * ( 1.0 - foamAmount );
		float foamBlend = saturate( depthAboveFloor * 10.0 );
		float4 foamA = g_tFoam.Sample( g_sAniso, lerp( foamUv, foamUvMoved + swirl * 0.05 * 0.03, foamBlend ) );
		float4 foamB = g_tFoam.Sample( g_sAniso, lerp( foamUv.yx * 0.731, foamUvMoved.yx * 0.731 + float2( sin( timeWarp + surface.y * 0.06 ), cos( timeWarp + surface.x * 0.06 ) ) * 0.05 * 0.02, foamBlend ) );
		float foamMask = max( foamA.z, foamB.z ) + sin( noise.x ) * 0.125;
		float foamCover = saturate( foamAmount * ( 1.0 + waveHeight * 0.008 ) );

		//
		// Debris: floating on the surface by its height, cut by the painted amount
		//
		float2 debrisUv = foamPosition.xy / g_flDebrisScale;
		float2 debrisWobble = wobble * g_flDebrisWobble;
		float debrisBlend = saturate( depthAboveFloor * 4.0 );
		float2 debrisUvMoved = lerp( debrisUv, debrisUv + debrisWobble * ( 1.0 - debrisAmount ) + swirl * 0.1 * 0.02, debrisBlend );
		float4 debris = g_tDebris.Sample( g_sAniso, debrisUvMoved );
		float debrisHeight = ( debris.a - 0.5 ) * 2.0;
		float debrisCut = 1.0 - debrisAmount;
		float debrisEdge = saturate( ( debris.a - debrisCut ) * g_flDebrisEdgeSharpness );
		float debrisBelow = max( 0.0, -debrisHeight );
		float debrisMask = saturate( 1.0 - debrisBelow * 10.0 ) * debrisEdge;
		float3 debrisNormal = g_tDebrisNormal.Sample( g_sAniso, debrisUvMoved ).xyz - 0.5;
		debrisNormal.y = -debrisNormal.y;
		debrisNormal.xy *= g_flDebrisNormalStrength;

		float foam = saturate( foamCover * foamMask * 0.25 + saturate( foamCover - ( 1.0 - foamMask ) ) * 0.75 - debrisMask );
		float surfaceHeight = lerp( waveHeight, waveHeight * 0.5 + debrisHeight, debrisMask );

		float3 shaded = surface;
		float shadedDepth = depthDiff;

		[branch]
		if ( deep )
		{
			shaded = position + parallax * ( lerp( 0.5, surfaceHeight, edgeShape ) - g_flWaterPlaneOffset );
			shadedDepth = max( dot( floorPosition - shaded, cameraForward ), 0.0 ) * 0.01 + lumaBias;
		}

		float coverage = saturate( debrisEdge + foam );

		//
		// The normal: the waves' slope, steeper in shallow water, with the debris' and foam's, jittered by the noise;
		// a lighting normal and a steeper one for the view, bent to the floor's at the shore
		//
		float2 n = slope * 2.0 * g_flWavesNormalStrength * lerp( 1.0, 2.0, distanceEffect );
		n *= 1.0 + saturate( 0.2 - shadedDepth ) * 8.0;
		n += debrisNormal.xy * debrisMask * 1.5;
		n += lerp( foamA.xy - 0.5, foamB.xy - 0.5, foamB.z > foamA.z ? 1.0 : 0.0 ) * foam;
		n *= 1.0 + ( noise.xy - 0.5 ) * 2.0 * g_flWavesNormalJitter;

		float3 normalLight = float3( n, sqrt( 1.0 - saturate( dot( n, n ) ) ) );
		float2 n3 = n * 3.0;
		float3 normalView = float3( n3, sqrt( 1.0 - saturate( dot( n3, n3 ) ) ) );

		[branch]
		if ( deep )
		{
			float w = lerp( 60.0, 120.0, edgeNormal.z );
			float edge = ( saturate( saturate( ( 1.0 / w - shadedDepth ) * w ) + saturate( ( 0.025 - shadedDepth ) * 8.0 ) ) - depthWidth * 1000.0 ) / ( 1.0 + distanceToCamera * 0.002 ) * 0.6;
			normalLight = normalize( lerp( normalLight, edgeNormal, edge ) );
			normalView = normalize( lerp( normalView, edgeNormal, edge ) );
		}

		float NdotV = saturate( dot( toCamera, normalView ) );
		float fresnel = pow( 1.0 - NdotV, g_flFresnelExponent );
		float3 foamColor = g_vFoamColor * ( 1.0 + foam * 0.5 );

		//
		// The scene through the water: offset by the normal as far as the water is deep, darkened, with caustics
		//
		float3 underwater = 0;
		float causticDebris = 0.0;
		float waterDepth = shadedDepth;

		[branch]
		if ( deep )
		{
			float2 refraction = ( float2( dot( normalView.xy, float2( -toCamera.y, toCamera.x ) ), dot( normalView.xy, toCamera.xy ) ) + ( noise.xy - 0.5 ) * 0.002 * g_flWaterFogStrength ) * min( g_flRefractionLimit, shadedDepth );
			refraction *= saturate( ( max( dot( ScenePosition( screenUv + refraction ) - shaded, cameraForward ), 0.0 ) * 0.01 + lumaBias ) * 10.0 );

			float2 refractedUv = screenUv + refraction;
			float3 floorRefracted = ScenePosition( refractedUv );
			underwater = pow( SceneColor( refractedUv ), 1.1.xxx ) * g_flUnderwaterDarkening;
			waterDepth = max( dot( floorRefracted - shaded, cameraForward ), 0.0 ) * 0.01 + lumaBias;

			[branch]
			if ( g_flCaustics > 0.5 )
			{
				float shadowCut = saturate( ( WaterLuma( underwater ) - g_flCausticShadowCutOff ) * ( 2.0 + g_flCausticShadowCutOff ) );

				// The floor point the sun's caustics land on, along the sun (or a triplanar axis) through the water.
				float3 causticDirection = g_vCS2SunDirection;
				if ( g_flTriplanarCaustics > 0.5 )
				{
					float3 a = abs( edgeNormal );
					float3 axis = lerp( a.y < a.x ? float3( 1, 0, 1 ) : float3( 0, 1, 1 ), float3( 0, 0, 1 ), a.z > max( a.x, a.y ) ? 1.0 : 0.0 );
					causticDirection = lerp( g_vCS2SunDirection, axis, 0.65 );
				}

				float floorDepth = foamPosition.z - floorRefracted.z;
				float3 causticPosition = lerp( floorRefracted + causticDirection * causticDirection.z * floorDepth, shaded, saturate( noise.x * noise.x * g_flWaterFogStrength * 0.0125 ) );
				float causticDistance = distance( causticPosition, floorRefracted );

				// The debris' shadow on the floor.
				float2 causticDebrisUv = causticPosition.xy / g_flDebrisScale;
				float4 shadowDebris = g_tDebris.SampleBias( g_sAniso, lerp( causticDebrisUv, causticDebrisUv + debrisWobble * debrisCut + flow * debrisBelow * 0.1 + swirl * 0.1 * 0.04, debrisBlend ), floorDepth * 0.05 );
				float debrisShadow = saturate( ( shadowDebris.a - debrisCut * 0.9 - debrisEdge ) * 1.1 );

				float falloff = causticDistance / g_flCausticDepthFallOffDistance;
				float fade = saturate( 1.0 - falloff );
				float strength = shadowCut * saturate( causticDistance * 0.05 ) * fade;
				if ( g_flTriplanarCaustics < 0.5 )
					strength *= saturate( dot( edgeNormal, causticDirection ) );

				float2 causticUv = causticPosition.xy * 0.0333333351 * g_flCausticUVScaleMultiple;
				float sharpness = g_flCausticSharpness * ( 1.0 - saturate( falloff ) );
				float causticBias = ( 1.0 - sharpness ) * 6.0;

				// Three layers of the waves distort the pattern; three more make it, sharper the shallower.
				float2 distortion = 0;
				float2 causticScale = g_vWaveScale;
				float causticDirectionAngle = g_flWaterInitialDirection;

				[unroll]
				for ( int c = 0; c < 3; c++ )
				{
					float2 s = g_tWavesNormalHeight.SampleBias( g_sAniso, WaveUv( causticUv + distortion, causticScale, causticDirectionAngle ), causticBias ).xy - 0.5;
					distortion += s * 0.5 * g_flCausticDistortion * ( 1.0 + causticScale ) * ( 0.25 + falloff );
					causticScale *= g_flWavesPhaseOffset;
					causticDirectionAngle += 3.5 / float( c + 1 );
				}

				float3 caustic = 0;
				causticScale = g_vWaveScale;
				causticDirectionAngle = g_flWaterInitialDirection;

				[unroll]
				for ( int c2 = 0; c2 < 3; c2++ )
				{
					float t = ( float( c2 ) / ( float( iterations ) - 1.0 ) ) * 2.0;
					float h = g_tWavesNormalHeight.SampleBias( g_sAniso, WaveUv( causticUv + distortion, causticScale, causticDirectionAngle ), causticBias ).z;
					float weight = clamp( lerp( lerp( g_flLowFreqWeight, g_flMedFreqWeight, saturate( t ) ), g_flHighFreqWeight * roughness, saturate( t - 1.0 ) ), 0.1, 0.4 );
					caustic += pow( h, sharpness * 5.0 ).xxx * weight * ( 1.0 + caustic * 2.0 ) * fade * sharpness * 2.0;
					causticScale *= g_flWavesPhaseOffset;
					causticDirectionAngle += 3.5 / float( c2 + 1 );
				}

				caustic *= 1.0 - saturate( debrisShadow * 2.0 );

				// Split into colours along the pattern's edges, then brought to the sun's colour on the darkened scene.
				float c0 = caustic.x;
				float3 split = 1.0 + float3( 1.25, -0.25, -1.0 ) * ( clamp( ddx_fine( c0 ) * 200.0, -1.0, 1.0 ) * saturate( 1.0 - c0 * 3.0 ) );
				float3 lit = underwater * ( 1.0 + pow( max( caustic * split, 0.001 ) * 8.0, 2.5.xxx ) * strength * g_vCS2SunColor * g_vCausticsTint * g_flCausticsStrength * 0.1 );

				float l = pow( WaterLuma( lit ), 0.2 );
				float edgeSum = clamp( ddx_fine( l ), -1.0, 1.0 ) + clamp( ddy_fine( l ), -1.0, 1.0 );
				float3 shimmer = lit * ( 1.0 + float3( 2.5, 0.0, -2.0 ) * sign( edgeSum * saturate( abs( edgeSum ) - 0.1 ) ) );
				lit = lerp( lit, shimmer, saturate( 200.0 / distanceToCamera ) * 0.1 );

				if ( shadowCut > 0.0 )
				{
					underwater = lit;
					causticDebris = debrisShadow;
				}
			}
		}

		//
		// The water's fog and decay by its depth
		//
		float depth = min( g_flWaterMaxDepth, waterDepth );
		float3 decay = exp( ( g_vWaterDecayColor - 1.0 ) * g_flWaterDecayStrength * depth );
		float fogAmount = ( 1.0 - exp( -depth * g_flWaterFogStrength ) ) + ( foamCover - saturate( noise.x ) * 0.25 ) * 0.1;
		float3 fogColor = lerp( g_vWaterFogColor, foamColor, foamCover * 0.1 ) * lerp( decay, 1.0, saturate( g_flWaterFogStrength * 0.04 ) );

		// The sun's highlight, on a normal between the vertex's and the view normal (more of the latter far away).
		float3 toCameraShaded = -normalize( shaded - camera );
		float3 specularNormal = normalize( lerp( vertexNormal, normalView, g_flSpecularNormalMultiple * ( 1.0 + distanceToCamera * 0.0005 ) ) );
		float sunAlignment = saturate( dot( -g_vCS2SunDirection, reflect( toCameraShaded, specularNormal ) ) );
		float specularPower = lerp( g_flSpecularPower, g_flDebrisReflectance * 8.0, debrisEdge ) * lerp( 2.0, 0.2, saturate( roughness ) );
		float specular = pow( sunAlignment, specularPower ) * 0.1 + pow( sunAlignment, specularPower * 10.0 );

		float clear = 1.0 - fogAmount;
		float see = saturate( ( 1.0 - debrisEdge ) + debrisBelow ) * saturate( 1.0 - foam * 4.0 ) * clear;
		float see2 = see * see;
		float depthScale = lerp( 1.0, depth * 2.0, 0.75 );

		//
		// The water's light: taken below the surface (deeper the clearer it is), from the lightmap there or the probes
		//
		float3 volume = position - parallax * ( -surfaceHeight + lerp( noise.xxx, float3( noise.xy, 0.0 ), 0.1 ) * 90.0 * see2 + g_flWaterPlaneOffset ) * depthScale;

		float2 lightmapUv = i.vLightmapUV.xy * g_vCS2LightmapUvScale;
		lightmapUv += ( fwidth( lightmapUv ) * 1200.0 / distanceToCamera ) * NdotV * ( -flow ) * float2( -1.0, 1.0 ) * ( -2.0 * surfaceHeight + lerp( noise.yy, noise.yx, 0.1 ) * 20.0 * see2 ) * depthScale;
		float2 lightmapDx = ddx( lightmapUv );
		float2 lightmapDy = ddy( lightmapUv );
		float flatness = CS2LightmapFlatness( vertexNormal, position );

		float3 irradiance;
		float4 bakedShadows;

		[branch]
		if ( i.vLightmapUV.x >= 0.0 && g_nCS2LightmapIrradiance != 0 )
		{
			irradiance = CS2SampleLightmap( g_nCS2LightmapIrradiance, g_nCS2LightmapIrradianceLowerMips, lightmapUv, lightmapDx, lightmapDy ).rgb;
			bakedShadows = g_nCS2LightmapShadows != 0 ? CS2SampleLightmap( g_nCS2LightmapShadows, g_nCS2LightmapShadowsLowerMips, lightmapUv, lightmapDx, lightmapDy ) : 0;

			// The high variant turns it by the directional lightmap, towards the lighting normal (the water's frame is the
			// world's).
			[branch]
			if ( high && g_nCS2LightmapDirectional != 0 )
				irradiance = CS2DirectionalIrradiance( irradiance, CS2SampleLightmap( g_nCS2LightmapDirectional, g_nCS2LightmapDirectionalLowerMips, lightmapUv, lightmapDx, lightmapDy ), normalLight, flatness );
		}
		else
		{
			CS2Probes( volume, normalLight, irradiance, bakedShadows );
		}

		// The sun: its baked shadow, and the realtime one less where the water is clear.
		float bakedSun = 1.0 - dot( bakedShadows, g_vCS2SunShadowMask );
		float realtimeSun = 1.0;

		[branch]
		if ( any( g_vCS2SunColor > 0.0 ) && g_DirectionalLightEnabled && g_DirectionalLightCascadeCount > 0 )
			realtimeSun = CS2SunShadow( m.WorldPositionWithOffset, receiverNormal, m.ScreenPosition );

		float sunVisibility = lerp( realtimeSun * bakedSun, bakedSun, see * 0.5 );
		float3 diffuse = g_vCS2ConstantAmbient;
		if ( dot( g_vCS2SunDirection, normalLight ) * sunVisibility > 0.0 )
			diffuse += max( 0.0, dot( normalLight, g_vCS2SunDirection ) ) * g_vCS2SunColor * sunVisibility;

		CS2GridRange range = CS2GridLookup( volume, 1, (uint)g_nCS2BarnLightCount );

		[loop]
		for ( uint b = 0; b < range.Count; b++ )
		{
			CS2BarnLight light = g_CS2BarnLights[CS2GridItem( range, b )];

			float3 toLight;
			float3 color;
			if ( CS2BarnLightAt( light, volume, bakedShadows, high, toLight, color ) )
				diffuse += max( 0.0, dot( normalLight, toLight ) ) * color;
		}

		//
		// The composite
		//
		float3 debrisColor = debris.rgb * ( 0.5 + debrisMask * 0.5 ) * g_vDebrisTint;
		float3 color = ( diffuse + irradiance ) * lerp( lerp( fogColor * fogAmount * g_flWaterFogShadowStrength, foamColor, foam ), debrisColor, saturate( debrisEdge - debrisBelow ) );
		color = lerp( color, underwater * decay, see );
		color = lerp( color, fogColor * 4.0 * irradiance, fogAmount * saturate( ( 1.0 - coverage ) + debrisBelow ) * ( 1.0 - g_flWaterFogShadowStrength ) );

		// The high variant reflects the map's cubemap volumes (where their blend data is given), off the lighting normal
		// made six times steeper, then twice as steep again along the view's y and z (in s&box's view space, taken to have
		// the game's axes: unverified), normalized by the cubes' own light alone.
		float3 reflection = 0;
		bool blended = false;

		[branch]
		if ( high )
		{
			float3 n = normalize( float3( normalLight.xy * 6.0, normalLight.z ) );
			float3 nWorld = normalize( g_vCS2WorldToMap0.xyz * n.x + g_vCS2WorldToMap1.xyz * n.y + g_vCS2WorldToMap2.xyz * n.z );
			float3 nView = mul( g_matWorldToView, float4( nWorld, 0.0 ) ).xyz;
			nView.yz *= 2.0;
			n = CS2DirectionToMap( mul( float4( normalize( nView ), 0.0 ), g_matWorldToView ).xyz );

			float roughnessHigh = dot( lerp( g_vRoughness, 1.0, clamp( distanceEffect, 0.0, 0.35 ) ), 0.5 );
			float r2 = roughnessHigh * roughnessHigh;
			float a = saturate( 1.0 - r2 );

			float3 cube;
			float4 normalizationSH;
			blended = CS2BlendedCubemaps( position, normalize( lerp( n, reflect( view, n ), a * ( sqrt( a ) + r2 ) ) ), roughnessHigh, cube, normalizationSH );
			if ( blended )
				reflection = cube * min( 1.0 / dot( float4( n, 1.0 ), normalizationSH ), max( roughnessHigh * 2.0 + 1.0, 1.0 ) ) * g_flEnvironmentMapBrightness;
		}

		// The low variant's (and the fallback's): the low end cube map, brought to the brightness of the light here.
		[branch]
		if ( !blended && g_flHasLowEndCube > 0.5 )
		{
			float2 r = lerp( g_vRoughness, 1.0, clamp( distanceEffect, 0.0, 0.35 ) );
			reflection = g_tLowEndCubeMap.SampleLevel( g_sTrilinearClamp, -reflect( toCameraShaded, normalLight ), sqrt( dot( r, 0.5 ) ) * 6.0 ).rgb
				* ( WaterLuma( irradiance ) * g_flLowEndCubeMapIntensity ) * g_flEnvironmentMapBrightness;
		}

		float reflectance = lerp( g_flReflectance, g_flDebrisReflectance, debrisMask );
		float reflectionAmount = ( fresnel * ( 1.0 - reflectance ) + reflectance ) * ( ( 1.0 - coverage * 0.75 ) - foam * 2.0 ) * 1.5;

		color += diffuse * ( ( specular + max( 0.0, specular - ( 1.0 - g_flSpecularBloomBoostThreshold ) ) * g_flSpecularBloomBoostStrength ) * lerp( 1.0, g_flDebrisReflectance * 0.05, debrisEdge ) ) * reflectionAmount * g_vCS2SunColor;

		// Oily debris: a rainbow through six hues by the fresnel and the debris' height.
		float hue = frac( fresnel * 20.0 + ( debris.a - 0.5 ) * 8.0 + g_flTime * 0.1 ) * 6.0;
		float sector = floor( hue );
		float f = hue - sector;
		float3 rainbow = float3( 0.75, 0.0, 0.75 * ( 1.0 - f ) );
		if ( sector == 0.0 )
			rainbow = float3( 0.75, 0.75 * f, 0.0 );
		else if ( sector == 1.0 )
			rainbow = float3( 0.75 * ( 1.0 - f ), 0.75, 0.0 );
		else if ( sector == 2.0 )
			rainbow = float3( 0.0, 0.75, 0.75 * f );
		else if ( sector == 3.0 )
			rainbow = float3( 0.0, 0.75 * ( 1.0 - f ), 0.75 );
		else if ( sector == 4.0 )
			rainbow = float3( 0.75 * f, 0.0, 0.75 );

		color *= lerp( 1.0, irradiance * 0.75, saturate( causticDebris * 4.0 ) * clear );

		float oil = saturate( debrisBelow * 20.0 ) * g_flDebrisOilyness / ( 1.0 + distanceToCamera * 0.005 ) * saturate( 1.0 - depthAlongView * 5.0 );
		color = lerp( color, lerp( reflection, reflection * rainbow, oil ), saturate( reflectionAmount ) );

		[branch]
		if ( g_flFogEnabled > 0.5 && CS2LightingEnabled() )
			color = CS2Fog( color, position );

		[branch]
		if ( deep )
		{
			// Faded out by the noise at the map's edges (the map bounds the material gives).
			float2 mapUv = ( position.xy - g_vMapUVMin ) / ( g_vMapUVMax - g_vMapUVMin );
			mapUv.y = 1.0 - mapUv.y;
			float2 fromMiddle = abs( 0.5 - mapUv ) * 2.0;
			if ( saturate( 1.0 - saturate( ( max( fromMiddle.x, fromMiddle.y ) - ( 1.0 - g_flSkyBoxFadeRange ) ) / g_flSkyBoxFadeRange ) ) - noise.x < 0.0 )
				discard;

			// At the shore, the scene as it is, slightly darker.
			color = lerp( behind0 * lerp( 1.0, 0.6, saturate( depthAlongView * 60.0 ) / ( 1.0 + distanceToCamera * 0.002 ) ), color, saturate( g_flEdgeHardness * depth + saturate( foam ) + ( debrisHeight - 0.5 ) ) );
		}

		return float4( color, 1.0 );
	}
}
