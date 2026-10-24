//=========================================================================================================================
// CS2's csgo_character.vfx: agents, arms, gloves, hostages, chickens.
//
// From the game's compiled shader (decompiled with Source 2 Viewer: the agents' materials' variants, and plain,
// anisotropic, spherical anisotropic, skin, eyeball, patch, tint mask, translucent and alpha tested variants). The colour
// times the instance tint (through the tint mask's red with F_TINT_MASK, times the painted vertex colour); metalness in
// the metalness texture's green, the cloth mask in its blue (by one less metalness), the rim mask in its alpha; the
// hemi-octahedral normal with roughness in blue, or two roughnesses from the anisotropic gloss texture; up to three
// patches; the eyeballs ray traced into the head's eye holes; lit by cs2_lighting.hlsl's CS2PbrShade (GGX with cloth's
// sheen, the split sum table, skin's softened diffuse and the ambient cube rim).
//
// Static combos that only change what the shader reads are uniforms here (the mount sets them from the material's
// F_* values); render state and the eyeballs stay combos.
//
// The eyeballs need the head's eye bones and their bind pose (the game's eyeball_bone_l_index, eyeball_bind_l_pos...):
// whoever spawns the model sets them as the renderer's attributes CS2EyeLBone, CS2EyeLBindPos, CS2EyeLBindForward,
// CS2EyeLBindUp (and R), CS2EyeTargetBone, CS2EyeTargetBindPos, and CS2Eyes to 1; without them no eyes are drawn.
// First person legs' distortion: the renderer's CS2FirstpersonLegsDistortion attribute above 0.
//
// Not ported: F_ANISOTROPIC_HAIR's shifted highlights and transmission (26 materials: lit as plain anisotropic gloss),
// F_RETRO_REFLECTIVE (4), F_IRIDESCENCE and F_ENABLE_ADJUSTMENTS / F_DETAIL_TEXTURE (chickens and pets), blood decals and
// hit flashes (F_SUPPORTS_DECALS: gameplay), the patches' inspect highlight, the dithered fade out (the game's blue
// noise), the player and weapon occlusion squashes, the legs' prepass shader, wetness, the distance contrast boost for opaque
// characters (the "Boost Player Contrast" setting, whose strength is a per view value). Per vertex curvature for skin:
// the game's character meshes carry none, so it reads 0, as here.
//=========================================================================================================================

HEADER
{
	Description = "CS2 csgo_character";
}

FEATURES
{
	#include "common/features.hlsl"
	Feature( F_ALPHA_TEST, 0..1, "Translucent" );
	Feature( F_TRANSLUCENT, 0..1, "Translucent" );
	Feature( F_EYEBALLS, 0..1, "Eyeballs" );
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
	StaticCombo( S_EYEBALLS, F_EYEBALLS, Sys( ALL ) );
	#include "common/shared.hlsl"
}

struct VertexInput
{
	#include "cs2_vertexinput.hlsl"
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float4 vColorUv : TEXCOORD5;		// xy: transformed and scrolled colour coordinates; zw: the second coordinates
	float4 vAniso : TEXCOORD9;			// xyz: the spherically projected anisotropy direction, world space
	float4 vTint : TEXCOORD10;			// the instance tint (and painted vertex colour), linear, with the colour tint; alpha in w

	#if S_EYEBALLS
		float4 vEyeForward : TEXCOORD12;	// the eye's forward (xyz) and centre x (w)
		float4 vEyeLeft : TEXCOORD13;		// its left (xyz) and centre y
		float4 vEyeUp : TEXCOORD14;			// its up (xyz) and centre z
		float4 vEyeScale : TEXCOORD15;		// its bone's scale (x)
	#endif
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

	// F_SPHERICAL_PROJECTED_ANISOTROPIC_TANGENTS: the anisotropy runs around the pole (model space), turned by the angle
	// (-1 to 1, the material's degrees / 90 - 1).
	float g_flSphericalAnisotropy < Default( 0 ); >;
	float3 g_vSphericalAnisotropyPole < Default3( 0, 0, 0 ); >;
	float g_vSphericalAnisotropyAngle < Default( 0 ); >;

	// F_DISTANCE_CONTRAST_ADJUSTMENT on translucent materials: the tint fades towards the exposure from 400 to 1400 units.
	float g_flTranslucentDistanceContrast < Default( 0 ); >;
	float g_fDistanceContrastExposure < Default( 0.6 ); >;

	// First person legs: the game distorts them while its FirstpersonLegsFade attribute is above 0, unless
	// FirstPersonLegsDisableDistortion is set. Here game code sets CS2FirstpersonLegsDistortion on the legs' renderer.
	float g_flCS2FirstpersonLegsDistortion < Attribute( "CS2FirstpersonLegsDistortion" ); Default( 0 ); >;

	// Looking down, the legs stretch out from the middle of the screen, drop down it and move back in depth, the more
	// the higher the vertex sits on the body (its bind pose height) and the steeper the view; their shading normals tilt
	// up. The world position the pixel shader lights with stays where it was. The view direction is the game's per view
	// constant identified from ValveResourceFormat's layout (unverified).
	void ApplyFirstpersonLegsDistortion( float3 positionOs, inout PixelInput i )
	{
		float amount = pow( saturate( ( positionOs.z + 1.0 ) * 0.0125 ) * 0.5 * pow( saturate( -g_vCameraDirWs.z ), 2.0 ), 1.5 );

		// The game's clip space has y up before its final flip; which way s&box's points is taken from where a point
		// above the view's centre lands.
		float up = Position3WsToPs( g_vCameraPositionWs + g_vCameraDirWs * 100.0 + g_vCameraUpDirWs * 10.0 ).y >= 0.0 ? 1.0 : -1.0;

		float2 xy = i.vPositionPs.xy * ( 1.0 + 5.0 * amount );
		i.vPositionPs.xy = float2( xy.x, xy.y - 200.0 * amount * up );
		i.vNormalWs.xyz = normalize( i.vNormalWs.xyz + float3( 0.0, 0.0, 0.5 ) * sign( amount ) );

		// Not ported: the game also offsets clip space depth by 40 * amount. s&box's reverse Z projection keeps clip
		// space z at the near plane (about 1), so that offset puts the thighs and pelvis past the far plane and culls
		// them, leaving only the boots; ValveResourceFormat leaves it out too.
	}

	#if S_EYEBALLS
		int g_nCS2Eyes < Attribute( "CS2Eyes" ); Default( 0 ); >;
		int g_nCS2EyeLBone < Attribute( "CS2EyeLBone" ); Default( 0 ); >;
		int g_nCS2EyeRBone < Attribute( "CS2EyeRBone" ); Default( 0 ); >;
		int g_nCS2EyeTargetBone < Attribute( "CS2EyeTargetBone" ); Default( 0 ); >;
		float3 g_vCS2EyeLBindPos < Attribute( "CS2EyeLBindPos" ); Default3( 0, 0, 0 ); >;
		float3 g_vCS2EyeLBindForward < Attribute( "CS2EyeLBindForward" ); Default3( 1, 0, 0 ); >;
		float3 g_vCS2EyeLBindUp < Attribute( "CS2EyeLBindUp" ); Default3( 0, 0, 1 ); >;
		float3 g_vCS2EyeRBindPos < Attribute( "CS2EyeRBindPos" ); Default3( 0, 0, 0 ); >;
		float3 g_vCS2EyeRBindForward < Attribute( "CS2EyeRBindForward" ); Default3( 1, 0, 0 ); >;
		float3 g_vCS2EyeRBindUp < Attribute( "CS2EyeRBindUp" ); Default3( 0, 0, 1 ); >;
		float3 g_vCS2EyeTargetBindPos < Attribute( "CS2EyeTargetBindPos" ); Default3( 0, 0, 0 ); >;

		float g_flEyeBallWalleyeL1 < Default( 0 ); >;
		float g_flEyeBallWalleyeR1 < Default( 0 ); >;
		float g_flPointAtEyeTarget1 < Default( 1 ); >;

		// A direction rotated about a unit axis by an angle's cosine and sine, as the game's matrix applies it.
		float3 CS2RotateAbout( float3 v, float3 k, float c, float s )
		{
			float t = 1.0 - c;
			float3 column0 = float3( k.x * k.x + ( 1.0 - k.x * k.x ) * c, k.x * k.y * t - k.z * s, k.z * k.x * t + k.y * s );
			float3 column1 = float3( k.x * k.y * t + k.z * s, k.y * k.y + ( 1.0 - k.y * k.y ) * c, k.y * k.z * t - k.x * s );
			float3 column2 = float3( k.z * k.x * t - k.y * s, k.y * k.z * t + k.x * s, k.z * k.z + ( 1.0 - k.z * k.z ) * c );
			return float3( dot( v, column0 ), dot( v, column1 ), dot( v, column2 ) );
		}

		// Keeps a look direction within 40 degrees of an axis, as the game does: past it, the axis turned 40 degrees
		// towards it.
		float3 CS2ClampEyeDirection( float3 direction, float3 axis )
		{
			if ( acos( clamp( dot( direction, axis ), -1.0, 1.0 ) ) <= 0.698131681 )
				return direction;

			return normalize( CS2RotateAbout( axis, normalize( cross( axis, direction ) ), 0.766044438, 0.642787576 ) );
		}
	#endif

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessCS2Vertex( v );

		float2 uv = v.vTexCoord.xy;
		float2 colorUv = float2( dot( uv, g_vTexCoordXform0.xy ) + g_vTexCoordXform0.w, dot( uv, g_vTexCoordXform1.xy ) + g_vTexCoordXform1.w );
		i.vColorUv = float4( colorUv + g_vTexCoordScrollSpeed * g_flTime, v.vTexCoord2.xy );

		// The instance colour is gamma space; the game linearizes it, fades it in by the model tint amount, then
		// multiplies the material's colour tint in, and the painted vertex colour unless it's all zero.
		float4 instance = i.vVertexColor * v.vMeshTint;
		float4 tint = float4( lerp( 1.0, SrgbGammaToLinear( instance.rgb ), g_flModelTintAmount ) * g_vColorTint, instance.a );
		if ( any( v.vMeshColor != 0.0 ) )
			tint *= v.vMeshColor;

		// The distance is scaled by the view's zoom in the game (unverified: taken as 1).
		if ( g_flTranslucentDistanceContrast > 0.5 )
			tint.rgb *= lerp( 1.0, g_fDistanceContrastExposure, smoothstep( 400.0, 1400.0, distance( g_vCameraPositionWs, i.vPositionWs.xyz ) ) );

		i.vTint = tint;

		// The anisotropy direction, made in the bind pose's model space as the game does, then carried into the world by
		// the vertex's own frame (s&box skins the frame, not this direction).
		i.vAniso = 0;
		if ( g_flSphericalAnisotropy > 0.5 )
		{
			float3 normalOs;
			float4 tangentOs;
			DecodeCS2TangentFrame( v.nTangentFrame, normalOs, tangentOs );

			float3 around = cross( g_vSphericalAnisotropyPole, normalOs );
			if ( length( around ) == 0.0 )
				around = tangentOs.xyz;

			float3 direction = around;
			if ( g_vSphericalAnisotropyAngle != 0.0 )
				direction = lerp( lerp( cross( normalOs, around ), around, max( 0.0, g_vSphericalAnisotropyAngle ) ), -around, max( 0.0, -g_vSphericalAnisotropyAngle ) );

			float3 bitangentOs = cross( normalOs, tangentOs.xyz ) * tangentOs.w;
			i.vAniso.xyz = i.vTangentUWs.xyz * dot( direction, tangentOs.xyz ) + i.vTangentVWs.xyz * dot( direction, bitangentOs ) + i.vNormalWs.xyz * dot( direction, normalOs );
		}

		#if S_EYEBALLS
		{
			i.vEyeForward = 0;
			i.vEyeLeft = 0;
			i.vEyeUp = 0;
			i.vEyeScale = 0;

			if ( g_nCS2Eyes != 0 )
			{
				// The nearer eye of the bind pose.
				bool right = distance( v.vPositionOs.xyz, g_vCS2EyeRBindPos ) < distance( v.vPositionOs.xyz, g_vCS2EyeLBindPos );
				float3x4 bone = GetTransformMatrix( v.nInstanceTransformID, right ? g_nCS2EyeRBone : g_nCS2EyeLBone );

				float3 center = mul( bone, float4( right ? g_vCS2EyeRBindPos : g_vCS2EyeLBindPos, 1.0 ) );
				float3 forward = mul( (float3x3)bone, right ? g_vCS2EyeRBindForward : g_vCS2EyeLBindForward );
				float3 up = mul( (float3x3)bone, right ? g_vCS2EyeRBindUp : g_vCS2EyeLBindUp );
				float walleye = right ? -g_flEyeBallWalleyeR1 : g_flEyeBallWalleyeL1;
				float scale = length( float3( bone[0].x, bone[1].x, bone[2].x ) );
				float3 left = cross( up, forward );

				[branch]
				if ( g_flPointAtEyeTarget1 > 0.5 )
				{
					float3 target = mul( GetTransformMatrix( v.nInstanceTransformID, g_nCS2EyeTargetBone ), float4( g_vCS2EyeTargetBindPos, 1.0 ) );
					float3 look = normalize( target - center );

					if ( walleye != 0.0 )
					{
						float angle = 0.0174532924 * ( walleye * ( 1.0 / scale ) );
						look = normalize( CS2RotateAbout( look, up, cos( angle ), sin( angle ) ) );
					}

					look = CS2ClampEyeDirection( look, normalize( forward + up * 0.5 ) );
					look = CS2ClampEyeDirection( look, normalize( forward + up * -0.5 ) );

					forward = look;
					left = cross( up, forward );
					up = cross( forward, left );
				}

				i.vEyeForward = float4( forward, center.x );
				i.vEyeLeft = float4( left, center.y );
				i.vEyeUp = float4( up, center.z );
				i.vEyeScale = float4( scale, 1.0 / scale, 0, 0 );
			}
		}
		#endif

		if ( g_flCS2FirstpersonLegsDistortion > 0.0 )
			ApplyFirstpersonLegsDistortion( v.vPositionOs.xyz, i );

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
	CreateInputTexture2D( CS2Color, Srgb, 8, "None", "_color", "Color", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( CS2Normal, Linear, 8, "None", "_normal", "Normal Map", Default4( 0.5, 0.5, 0.5, 0 ) );
	CreateInputTexture2D( CS2AmbientOcclusion, Linear, 8, "None", "_ao", "Ambient Occlusion", Default4( 1, 1, 1, 0 ) );
	CreateInputTexture2D( CS2Metalness, Linear, 8, "None", "_metal", "Lighting", Default4( 0, 0, 0, 1 ) );
	CreateInputTexture2D( CS2AnisoGloss, Linear, 8, "None", "_gloss", "Lighting", Default4( 0.5, 0.5, 0, 0 ) );
	CreateInputTexture2D( CS2TintMask, Linear, 8, "None", "_mask", "Color", Default4( 1, 1, 1, 0 ) );
	CreateInputTexture2D( CS2SssMask, Linear, 8, "None", "_mask", "Skin", Default4( 1, 1, 1, 0 ) );
	CreateInputTexture2D( CS2DiffuseFalloff, Linear, 8, "None", "_diffusewarp", "Skin", Default4( 0.5, 0.5, 0.5, 0 ) );
	CreateInputTexture2D( CS2Patch0, Srgb, 8, "None", "_color", "Patches", Default4( 0, 0, 0, 0 ) );
	CreateInputTexture2D( CS2Patch0Backing, Srgb, 8, "None", "_color", "Patches", Default4( 0, 0, 0, 0 ) );
	CreateInputTexture2D( CS2Patch1, Srgb, 8, "None", "_color", "Patches", Default4( 0, 0, 0, 0 ) );
	CreateInputTexture2D( CS2Patch1Backing, Srgb, 8, "None", "_color", "Patches", Default4( 0, 0, 0, 0 ) );
	CreateInputTexture2D( CS2Patch2, Srgb, 8, "None", "_color", "Patches", Default4( 0, 0, 0, 0 ) );
	CreateInputTexture2D( CS2Patch2Backing, Srgb, 8, "None", "_color", "Patches", Default4( 0, 0, 0, 0 ) );

	Texture2D g_tColor < Channel( RGBA, Box( CS2Color ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tNormal < Channel( RGBA, Box( CS2Normal ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tAmbientOcclusion < Channel( RGBA, Box( CS2AmbientOcclusion ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tMetalness < Channel( RGBA, Box( CS2Metalness ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tAnisoGloss < Channel( RGBA, Box( CS2AnisoGloss ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tTintMask < Channel( RGBA, Box( CS2TintMask ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tSssMask < Channel( RGBA, Box( CS2SssMask ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tDiffuseFalloff < Channel( RGBA, Box( CS2DiffuseFalloff ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tPatch0 < Channel( RGBA, Box( CS2Patch0 ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tPatch0Backing < Channel( RGBA, Box( CS2Patch0Backing ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tPatch1 < Channel( RGBA, Box( CS2Patch1 ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tPatch1Backing < Channel( RGBA, Box( CS2Patch1Backing ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tPatch2 < Channel( RGBA, Box( CS2Patch2 ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tPatch2Backing < Channel( RGBA, Box( CS2Patch2Backing ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;


	#if S_EYEBALLS
		CreateInputTexture2D( CS2EyeAlbedo1, Srgb, 8, "None", "_color", "Eyes", Default4( 1, 1, 1, 0 ) );
		CreateInputTexture2D( CS2EyeMask1, Linear, 8, "None", "_eyemask", "Eyes", Default4( 0, 0, 0, 0 ) );
		Texture2D g_tEyeAlbedo1 < Channel( RGBA, Box( CS2EyeAlbedo1 ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
		Texture2D g_tEyeMask1 < Channel( RGBA, Box( CS2EyeMask1 ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
		SamplerState g_sEyeMirrorAniso < Filter( ANISO ); AddressU( MIRROR ); AddressV( MIRROR ); >;
	#endif

	SamplerState g_sPatch < Filter( ANISO ); AddressU( CLAMP ); AddressV( CLAMP ); >;

	TextureAttribute( RepresentativeTexture, g_tColor );
	TextureAttribute( LightSim_DiffuseAlbedoTexture, g_tColor );

	//
	// Parameters, as the game's shader computes them from the material (MaterialImporter in the mount)
	//
	float g_flTintMask < Default( 0 ); >;
	float g_flSecondaryUvForTintMask < Default( 0 ); >;
	float g_flReflectance < Default( 0.04 ); >;

	// F_ANISOTROPIC_GLOSS: two roughnesses from the gloss texture (else the normal map's blue); with
	// F_SPHERICAL_PROJECTED_ANISOTROPIC_TANGENTS along the projected direction, else the vertex tangents.
	float g_flAnisotropicGloss < Default( 0 ); >;
	float g_flSphericalAnisotropyPs < Default( 0 ); >;

	// F_CLOTH_SHADING: the sheen's tint (linear) and scale.
	float g_flClothShading < Default( 0 ); >;
	float3 g_flSheenTintColor < Default3( 1, 1, 1 ); >;
	float g_flSheenScale < Default( 0.667 ); >;

	// The rim's ambient occlusion masking.
	float g_flAmbientOcclusionMasking < Default( 0.4 ); >;

	// F_SUBSURFACE_SCATTERING: the normal softness per channel and the curvature scale.
	float g_flSubsurfaceScattering < Default( 0 ); >;
	float3 g_vNormalSoftness < Default3( 1, 0.3, 0 ); >;
	float g_flCurvatureScale < Default( 1 ); >;

	// F_PATCHES: each patch on or off, its scale, rotation (radians), squash, offset and backing's scale.
	float4 g_vPatchEnable < Default4( 0, 0, 0, 0 ); >;
	float4 g_vPatch0 < Default4( 0, 0, 1, 0 ); >;		// scale, rotation, squash, backing scale
	float4 g_vPatch1 < Default4( 0, 0, 1, 0 ); >;
	float4 g_vPatch2 < Default4( 0, 0, 1, 0 ); >;
	float4 g_vPatchOffset01 < Default4( 0, 0, 0, 0 ); >;	// patch 0 (xy), patch 1 (zw)
	float2 g_vPatchOffset2 < Default2( 0, 0 ); >;

	#if S_EYEBALLS
		float g_flEyeBallRadius1 < Default( 0.6 ); >;
		float g_flEyeIrisSize1 < Default( 1 ); >;
		float g_flEyePupilSize1 < Default( 0.15 ); >;		// times the iris size
		float g_flEyeHueShift1 < Default( 0 ); >;
		float g_flEyeSaturation1 < Default( 1 ); >;
	#endif

	// F_RENDER_BACKFACES without F_DONT_FLIP_BACKFACE_NORMALS: back faces light with the normal flipped.
	float g_flFlipBackfaceNormals < Default( 0 ); >;

	float g_flFogEnabled < Default( 1 ); >;

	// A debug view for checking shading against the game without recompiling, set per renderer: 1 the shading normal,
	// 2 the vertex normal, 3 the vertex tangent (each as colour, x y z in r g b), 4 the lighting without the rim,
	// 5 the lighting without the realtime sun shadow. 0 (default) draws as the game does.
	int g_nCS2CharacterDebug < Attribute( "CS2CharacterDebug" ); Default( 0 ); >;

	#if !S_TRANSLUCENT
		float g_flOpacityScale < Default( 1 ); >;
	#endif

	// The game's hemi-octahedral normal decode: x + y and x - y, folded onto the upper hemisphere.
	float3 DecodeHemiOctahedralNormal( float2 encoded )
	{
		float2 t = float2( encoded.x + encoded.y - 1.003921628, encoded.x - encoded.y );
		return normalize( float3( t, 1.0 - abs( t.x ) - abs( t.y ) ) );
	}

	float2 RotatePatch( float2 p, float squash, float rotation )
	{
		p.y *= squash;
		float c = cos( rotation );
		float s = sin( rotation );
		return float2( p.x * c - p.y * s, p.x * s + p.y * c ) + 0.5;
	}

	// One patch over the albedo, ambient occlusion, roughness, metalness and normal.
	void ApplyPatch( float enable, float4 settings, float2 offset, Texture2D patch, Texture2D backing, float2 uv,
		inout float3 albedo, inout float alpha, inout float ao, inout float2 roughness, inout float metalness, inout float3 normalTs )
	{
		float scale = abs( settings.x );
		if ( enable < 0.5 || settings.x == 0.0 )
			return;

		float2 centred = ( uv - 0.5 ) - offset;
		float2 patchUv = RotatePatch( centred * scale, settings.z, settings.y );
		if ( any( saturate( patchUv ) != patchUv ) )
			return;

		float4 color;
		[branch]
		if ( settings.w != 0.0 )
		{
			float2 backingUv = patchUv;
			if ( settings.w < 1.0 )
				backingUv = RotatePatch( centred * ( scale * ( 1.0 / abs( settings.w ) ) ), settings.z, settings.y );
			else
				patchUv = RotatePatch( centred * ( scale * abs( settings.w ) ), settings.z, settings.y );

			float4 under = backing.Sample( g_sPatch, backingUv );
			float4 over = patch.Sample( g_sPatch, patchUv );
			color = float4( lerp( under.rgb, over.rgb, over.a ), max( over.a, under.a ) );
		}
		else
		{
			color = patch.Sample( g_sPatch, patchUv );
		}

		albedo = lerp( albedo, color.rgb, color.a );
		alpha = max( alpha, color.a );
		ao = lerp( ao, 1.0, color.a );
		roughness = lerp( roughness, 0.7, color.a );
		metalness = lerp( metalness, 0.3, color.a );
		normalTs = lerp( normalTs, float3( 0, 0, 1 ), color.a );
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		#if S_RENDER_BACKFACES
			bool isFrontFace = i.face;
		#else
			bool isFrontFace = true;
		#endif

		float2 uv = i.vColorUv.xy;
		float4 color = g_tColor.Sample( g_sAniso, uv );

		//
		// Alpha: an anti-aliased test on the colour's alpha times the instance's, or translucency by both
		//
		float alpha = i.vTint.a;
		#if S_ALPHA_TEST && !S_MODE_DEPTH
		{
			// The game's branch for this depends on a per view setting; the anti-aliased one (unverified which).
			float coverage = color.a * i.vTint.a;
			alpha = lerp( saturate( 0.5 + ( coverage - g_flAlphaTestReference ) / max( fwidth( coverage ), 9.99999997e-07 ) ), coverage, lerp( 1.0, saturate( 4.0 * length( fwidth( uv ) ) ), g_flAntiAliasedEdgeStrength ) );
			if ( alpha - 0.001 < 0.0 )
				discard;
		}
		#elif S_ALPHA_TEST
			alpha = color.a * i.vTint.a;
		#elif S_TRANSLUCENT
			alpha = i.vTint.a * ( color.a * g_flOpacityScale );
		#endif

		//
		// The material
		//
		float3 albedo = color.rgb * i.vTint.rgb;
		if ( g_flTintMask > 0.5 )
			albedo = lerp( color.rgb, albedo, g_tTintMask.Sample( g_sAniso, g_flSecondaryUvForTintMask > 0.5 ? i.vColorUv.zw : uv ).r );

		float ao = g_tAmbientOcclusion.Sample( g_sAniso, uv ).r;
		float4 metal = g_tMetalness.Sample( g_sAniso, uv );
		float metalness = metal.g;
		float cloth = g_flClothShading > 0.5 ? metal.b * ( 1.0 - metalness ) : 0.0;
		float rimMask = metal.a;

		float4 normalTexel = g_tNormal.Sample( g_sAniso, uv );
		float3 rawNormalTs = DecodeHemiOctahedralNormal( normalTexel.xy );
		float2 roughness = g_flAnisotropicGloss > 0.5 ? g_tAnisoGloss.Sample( g_sAniso, uv ).xy : normalTexel.bb;

		bool flip = g_flFlipBackfaceNormals > 0.5 && !isFrontFace;
		float3 normalTs = rawNormalTs;
		if ( !flip )
			normalTs.y = -normalTs.y;

		ApplyPatch( g_vPatchEnable.x, g_vPatch0, g_vPatchOffset01.xy, g_tPatch0, g_tPatch0Backing, uv, albedo, alpha, ao, roughness, metalness, normalTs );
		ApplyPatch( g_vPatchEnable.y, g_vPatch1, g_vPatchOffset01.zw, g_tPatch1, g_tPatch1Backing, uv, albedo, alpha, ao, roughness, metalness, normalTs );
		ApplyPatch( g_vPatchEnable.z, g_vPatch2, g_vPatchOffset2, g_tPatch2, g_tPatch2Backing, uv, albedo, alpha, ao, roughness, metalness, normalTs );

		//
		// The frame: the vertex normal flipped on back faces, and the bitangent with it
		//
		float3 vertexNormalUnflipped = normalize( i.vNormalWs.xyz );
		float3 vertexNormal = vertexNormalUnflipped * ( flip ? -1.0 : 1.0 );
		float3 tangent = i.vTangentUWs.xyz;
		float3 bitangent = i.vTangentVWs.xyz * ( flip ? -1.0 : 1.0 );
		float3 normalWs = normalize( tangent * normalTs.x + bitangent * normalTs.y + vertexNormal * normalTs.z );

		// Derivatives need uniform control flow: geometric specular antialiasing and the rim's fade.
		float3 vertexNormalDx = ddx( vertexNormal );
		float3 vertexNormalDy = ddy( vertexNormal );
		float3 normalDx = ddx( normalWs );
		float3 normalDy = ddy( normalWs );
		float geometricRoughness = pow( saturate( max( dot( vertexNormalDx, vertexNormalDx ), dot( vertexNormalDy, vertexNormalDy ) ) ), 0.333 );
		roughness = max( roughness, geometricRoughness );
		float rimFade = 1.0 - saturate( max( geometricRoughness, max( dot( normalDx, normalDx ), dot( normalDy, normalDy ) ) ) );

		float3 diffuseColor = albedo * ( 1.0 - metalness );
		float3 sheenColor = g_flClothShading > 0.5 ? lerp( g_flReflectance.xxx, saturate( g_flSheenTintColor * sqrt( albedo ) * g_flSheenScale ), cloth ) : g_flReflectance.xxx;
		float3 specularColor = lerp( sheenColor, albedo, metalness );

		//
		// The eyeballs: the eye hole's mask, the view ray traced into the eye's sphere, the iris and pupil there
		//
		#if S_EYEBALLS
		{
			float mask = g_tEyeMask1.SampleBias( g_sAniso, uv, -0.5 ).r;

			[branch]
			if ( mask > 0.0 && i.vEyeScale.x > 0.0 )
			{
				float3 forward = normalize( i.vEyeForward.xyz );
				float3 center = float3( i.vEyeForward.w, i.vEyeLeft.w, i.vEyeUp.w );
				float radius = g_flEyeBallRadius1 * i.vEyeScale.x;
				float3 ray = normalize( i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz - g_vCameraPositionWs );
				float3 fromCenter = g_vCameraPositionWs - center;
				float b = dot( fromCenter, ray );
				float discriminant = b * b - ( dot( fromCenter, fromCenter ) - radius * radius );
				float t = discriminant > 0.0 ? -b - sqrt( discriminant ) : 0.0;

				[branch]
				if ( t > 0.0 )
				{
					float3 eyeNormal = normalize( ( g_vCameraPositionWs + ray * t ) - center );
					float2 eyeUv = float2( dot( eyeNormal, normalize( i.vEyeLeft.xyz ) ), dot( eyeNormal, normalize( -i.vEyeUp.xyz ) ) );
					float fromMiddle = length( eyeUv );
					float2 irisUv = lerp( eyeUv * ( 2.0 - g_flEyeIrisSize1 ), eyeUv, fromMiddle );

					float4 eye = g_tEyeAlbedo1.SampleBias( g_sEyeMirrorAniso, irisUv * 0.5 + 0.5, -0.5 );
					float c = cos( g_flEyeHueShift1 );
					float3 grey = float3( 0.57735002, 0.57735002, 0.57735002 );
					float3 hued = lerp( eye.rgb, eye.rgb * c + cross( grey, eye.rgb ) * sin( g_flEyeHueShift1 ) + grey * dot( grey, eye.rgb ) * ( 1.0 - c ), eye.a );
					float facing = saturate( dot( forward, eyeNormal ) );
					float3 eyeColor = lerp( hued, lerp( dot( hued, float3( 0.2125, 0.7154, 0.0721 ) ).xxx, hued, g_flEyeSaturation1 ), eye.a )
						* smoothstep( g_flEyePupilSize1, g_flEyePupilSize1 + 0.03, fromMiddle ) * facing;

					// Far away, where the iris shrinks to a few pixels, a highlight is painted in.
					if ( abs( ddx( irisUv.x ) ) > 0.15 )
						eyeColor = lerp( eyeColor, eyeColor * 0.8 + smoothstep( 0.1, 0.08, length( irisUv - float2( g_flEyePupilSize1, -g_flEyePupilSize1 ) ) ), saturate( ( distance( g_vCameraPositionWs, i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz ) - 100.0 ) * 0.005 ) );

					float amount = mask * smoothstep( 0.1, 0.3, facing );
					diffuseColor = lerp( diffuseColor, eyeColor, amount );
					normalWs = normalize( lerp( normalWs, normalize( lerp( eyeNormal, normalize( eyeNormal - forward * 0.5 ), eye.a ) ), sqrt( amount ) ) );
					roughness = lerp( roughness, 0.1, amount );
				}
			}
		}
		#endif

		Material m = Material::Init( i.vPositionWithOffsetWs, i.vPositionSs );
		m.Albedo = albedo;
		m.Normal = normalWs;
		m.TangentNormal = normalTs;
		m.GeometricNormal = vertexNormal;
		m.WorldTangentU = tangent;
		m.WorldTangentV = bitangent;
		m.TextureCoords = uv;
		m.Roughness = saturate( dot( roughness, 0.5 ) );
		m.Metalness = saturate( metalness );
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

			CS2PbrSurface s = CS2PbrSurfaceDefaults();
			s.Position = m.WorldPosition;
			s.PositionWithOffset = m.WorldPositionWithOffset;
			s.ScreenPosition = m.ScreenPosition;
			s.ShadowReceiverNormal = receiverNormal;
			s.Normal = normalWs;
			s.VertexNormal = vertexNormal;

			// The anisotropy's frame: across and along the projected direction, else the vertex tangents.
			if ( g_flSphericalAnisotropyPs > 0.5 )
			{
				s.Tangent = normalize( cross( i.vAniso.xyz, normalWs ) );
				s.Bitangent = normalize( cross( normalWs, s.Tangent ) );
			}
			else
			{
				s.Tangent = normalize( cross( bitangent, normalWs ) );
				s.Bitangent = normalize( cross( normalWs, tangent ) );
			}

			s.DiffuseColor = diffuseColor;
			s.SpecularColor = specularColor;
			s.Roughness = roughness;
			s.Cloth = cloth;
			s.AmbientOcclusion = ao;
			s.Fog = g_flFogEnabled > 0.5;

			[branch]
			if ( g_flSubsurfaceScattering > 0.5 )
			{
				// The skin's normal from the decoded normal unflipped, on the unflipped vertex normal.
				s.Subsurface = g_tSssMask.Sample( g_sTrilinearWrap, uv ).g;
				s.SubsurfaceNormal = normalize( tangent * rawNormalTs.x + bitangent * rawNormalTs.y + vertexNormalUnflipped * rawNormalTs.z );
				s.NormalSoftness = g_vNormalSoftness;
				s.Curvature = 0.0 * g_flCurvatureScale * 0.397677749 - 0.0101010092;
			}

			s.Rim = true;
			s.RimAmount = rimMask * ( saturate( ao - g_flAmbientOcclusionMasking ) / ( 1.0 - g_flAmbientOcclusionMasking ) ) * rimFade;

			[branch]
			if ( g_nCS2CharacterDebug != 0 )
			{
				if ( g_nCS2CharacterDebug == 1 )
					return float4( normalWs * 0.5 + 0.5, 1.0 );
				if ( g_nCS2CharacterDebug == 2 )
					return float4( vertexNormal * 0.5 + 0.5, 1.0 );
				if ( g_nCS2CharacterDebug == 3 )
					return float4( normalize( tangent ) * 0.5 + 0.5, 1.0 );
				if ( g_nCS2CharacterDebug == 4 )
					s.Rim = false;
				if ( g_nCS2CharacterDebug == 5 )
					s.NoRealtimeSunShadow = true;
			}

			return float4( CS2PbrShade( s, g_tDiffuseFalloff ), alpha );
		#endif
	}
}
