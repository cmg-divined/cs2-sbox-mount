//=========================================================================================================================
// CS2's post_process.vfx: the pass that takes the lit scene to the screen.
//
// From the game's compiled shader (decompiled with Source 2 Viewer: D_COLOR_CORRECTION_LUT, D_BLOOM,
// D_COLORBUFFER_TONEMAPSPACE=2, the variant the game draws: its colour buffer is in tonemap space, already exposed, see
// cs2_gaussian_bloom). The colour back out of tonemap space; bloom blended in (the blur mode) and added (the add mode);
// the filmic curve on the colour scaled by 2.8 and clamped at the white point, normalized by the curve at the white
// point; the sRGB encode; screen mode bloom, exposed, compressed and screened over; the colour correction lookup table;
// the screen fade; the fullscreen gamma. The game writes that to the screen as it is; s&box encodes what it's given to
// sRGB, so it's decoded last.
//
// The constants are the game's (CS2PostProcessing computes them as the game's CPU side does): the curve's are
// PostProcessingTonemapParameters_t's, its white point two to the power of the white point in stops and the
// normalization one over the curve there (materialsystem2.dll, decompiled).
//
// Not ported: vignette and local contrast (only gameplay overlays use them: scopes, the death cam, the buy menu), film
// grain and lens dirt (no map's post processing has them).
//=========================================================================================================================

HEADER
{
	Description = "CS2 post_process";
	DevShader = true;
}

MODES
{
	Default();
	Forward();
}

COMMON
{
	#include "postprocess/shared.hlsl"
}

struct VertexInput
{
	float3 vPositionOs : POSITION < Semantic( PosXyz ); >;
	float2 vTexCoord : TEXCOORD0 < Semantic( LowPrecisionUv ); >;
};

struct PixelInput
{
	float2 vTexCoord : TEXCOORD0;

	#if ( PROGRAM == VFX_PROGRAM_VS )
		float4 vPositionPs : SV_Position;
	#endif
};

VS
{
	PixelInput MainVs( VertexInput i )
	{
		PixelInput o;
		o.vPositionPs = float4( i.vPositionOs.xyz, 1.0f );
		o.vPositionPs.z = 1;
		o.vTexCoord = i.vTexCoord;
		return o;
	}
}

PS
{
	#include "postprocess/common.hlsl"

	// The colour buffer in tonemap space (cs2_gaussian_bloom's first stage), the bloom buffer, the lookup table.
	Texture2D g_tColorBuffer < Attribute( "CS2ColorBuffer" ); SrgbRead( false ); >;
	Texture2D g_tBloom < Attribute( "CS2Bloom" ); SrgbRead( false ); >;
	Texture3D g_tColorCorrection < Attribute( "CS2ColorCorrection" ); SrgbRead( false ); >;

	// The auto exposure's state (cs2_histogram_cs): the view's exposure at CS2_EXPOSURE.
	StructuredBuffer<float> g_bExposureState < Attribute( "CS2ExposureState" ); >;

	// Two to the power of the tonemap's exposure bias.
	float g_flExposureBiasScale < Attribute( "CS2ExposureBiasScale" ); Default( 1 ); >;

	// The filmic curve: shoulder strength A, linear strength B, linear angle C, toe strength D, toe numerator E,
	// toe denominator F; the white point the colour clamps to, and one over the curve there.
	float4 g_vCurveABCD < Attribute( "CS2CurveABCD" ); Default4( 0, 0.001, 0.001, 1 ); >;
	float4 g_vCurveEFWhite < Attribute( "CS2CurveEFWhite" ); Default4( 1, 1, 2.828427, 1 ); >;

	// Bloom: whether there is any, and its strengths as the game's expressions give them (add, screen, blur), normalized
	// and not.
	float g_flBloom < Attribute( "CS2BloomEnabled" ); Default( 0 ); >;
	float3 g_vNormalizedBloomStrengths < Attribute( "CS2NormalizedBloomStrengths" ); Default3( 0, 0, 0 ); >;
	float3 g_vUnNormalizedBloomStrengths < Attribute( "CS2UnNormalizedBloomStrengths" ); Default3( 0, 0, 0 ); >;

	// The colour correction table's weight against the colour without it.
	float g_flColorCorrection < Attribute( "CS2ColorCorrectionEnabled" ); Default( 0 ); >;
	float g_flColorCorrectionDefaultWeight < Attribute( "CS2ColorCorrectionDefaultWeight" ); Default( 1 ); >;

	// The screen fade (colour, amount in w) and its mode (0 over, 1 multiplied), and the fullscreen gamma.
	float4 g_vViewFadeColor < Attribute( "CS2ViewFadeColor" ); Default4( 0, 0, 0, 0 ); >;
	float g_flFadeMode < Attribute( "CS2FadeMode" ); Default( 0 ); >;
	float g_flFullScreenGamma < Attribute( "CS2FullScreenGamma" ); Default( 2.2 ); >;

	#define CS2_EXPOSURE 13

	// The game's sRGB encode and decode, piecewise.
	float LinearToSrgb( float x )
	{
		return x <= 0.0031308 ? x * 12.92 : pow( x, 0.4166666567 ) * 1.055 - 0.055;
	}

	float3 LinearToSrgb( float3 x )
	{
		return float3( LinearToSrgb( x.r ), LinearToSrgb( x.g ), LinearToSrgb( x.b ) );
	}

	float SrgbToLinear( float x )
	{
		return x <= 0.04045 ? x * 0.0773993805 : pow( x * 0.947867334 + 0.0521327034, 2.4 );
	}

	float3 SrgbToLinear( float3 x )
	{
		return float3( SrgbToLinear( x.r ), SrgbToLinear( x.g ), SrgbToLinear( x.b ) );
	}

	float3 FilmicCurve( float3 x )
	{
		float A = g_vCurveABCD.x, B = g_vCurveABCD.y, C = g_vCurveABCD.z, D = g_vCurveABCD.w;
		float E = g_vCurveEFWhite.x, F = g_vCurveEFWhite.y;
		float3 ax = x * A;
		return ( ( x * ( ax + C * B ) + D * E ) / ( x * ( ax + B ) + D * F ) ) - E / F;
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = i.vTexCoord;
		float4 buffer = g_tColorBuffer.SampleLevel( g_sBilinearClamp, uv, 0 );
		float3 color = buffer.rgb * ( 1.0 / ( 1.0 - max( buffer.r, max( buffer.g, buffer.b ) ) ) );

		float3 bloom = 0;
		[branch]
		if ( g_flBloom > 0.5 )
		{
			// The bloom buffer covers the screen in whole quarter pixels; the game's clamp keeps to its last texel.
			float width, height;
			g_tBloom.GetDimensions( width, height );
			float2 bloomUv = min( uv, ( float2( width, height ) - 0.5 ) / float2( width, height ) );
			bloom = g_tBloom.SampleLevel( g_sBilinearClamp, bloomUv, 0 ).rgb;

			// The blur mode blends towards the bloom; the add mode adds it.
			color = color * ( 1.0 - g_vUnNormalizedBloomStrengths.z ) + bloom * g_vNormalizedBloomStrengths.z;
			color += bloom * g_vNormalizedBloomStrengths.x;
		}

		// The curve, normalized at the white point.
		float3 curved = FilmicCurve( min( color * 2.8, g_vCurveEFWhite.z ) ) * g_vCurveEFWhite.w;
		color = LinearToSrgb( curved );

		[branch]
		if ( g_flBloom > 0.5 )
		{
			// The screen mode: the bloom exposed again, compressed, then screened over the encoded colour.
			float3 exposedBloom = bloom * ( g_bExposureState[CS2_EXPOSURE] * g_flExposureBiasScale );
			float3 screen = saturate( exposedBloom / ( exposedBloom + 0.187 ) * 1.035 ) * g_vNormalizedBloomStrengths.y;
			color = color + screen - color * screen;
		}

		[branch]
		if ( g_flColorCorrection > 0.5 )
		{
			float3 graded = g_tColorCorrection.SampleLevel( g_sTrilinearClamp, saturate( color ) * 0.96875 + 0.015625, 0 ).rgb;
			color = lerp( graded, color, g_flColorCorrectionDefaultWeight );
		}

		color = lerp( color, lerp( g_vViewFadeColor.rgb, g_vViewFadeColor.rgb * color, g_flFadeMode ), g_vViewFadeColor.w );
		color = pow( max( color, 0 ), g_flFullScreenGamma * 0.454545438 );

		// What the game puts on screen, as the linear colour s&box encodes to it.
		return float4( SrgbToLinear( saturate( color ) ), buffer.a );
	}
}
