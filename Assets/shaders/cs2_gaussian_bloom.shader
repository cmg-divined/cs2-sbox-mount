//=========================================================================================================================
// CS2's default bloom (r_csgo_use_compute_bloom 0), and the tonemap space colour buffer it reads, as compute passes.
//
// From the game's compiled shaders (decompiled with Source 2 Viewer) and client.dll (decompiled with IDA: the passes'
// order, targets and constants, CGaussianBloomBlurRenderer and its downsample and recombine functions):
//  - D_STAGE 0, nonmsaa_resolve D_OUTPUT_TONEMAP_SPACE=2: the scene times the exposure and two to the power of the
//    exposure bias, then c / ( 1 + max( c ) ). The game resolves its colour buffer like this
//    (r_csgo_msaa_resolve_apply_exposure_scale 1); with MSAA it averages the samples after the encode, which s&box's
//    resolved frame can't (edges only).
//  - D_STAGE 1, downsample_bloomthreshold (D_COLORBUFFER_TONEMAPSPACE=2, D_THRESHOLD_ALL_SAMPLES, D_DOWNSAMPLE_JIMENEZ):
//    to quarter resolution, 13 taps out of tonemap space, each faded in by the exposure times its luma (Rec. 601) against
//    the threshold, the middle quad at a half and the four corner quads at an eighth, times the bloom scale. The
//    particles' own bloom (QuarterResEffectsBloomInputTexture) it adds isn't drawn here.
//  - D_STAGE 2-6, gaussian_bloom_blur, four levels from quarter resolution down: each level the previous one sampled
//    (D_BLUR_PASS 0), blurred across (1) into a temporary and back down (2), with a 4 tap gaussian; then from the
//    smallest up, each level plus the 2x2 box of the one below times the tints and weights (3, a dual source blend in
//    the game: the source plus the destination times the second output); the last into the bloom buffer (4).
//    r_bloom_tent_filter_radius defaults to 0, so the box, not the tent.
// The game's samplers return black outside the texture (border colour 0); SampleBorder does the same by hand.
//=========================================================================================================================

HEADER
{
	DevShader = true;
	Description = "CS2 gaussian bloom";
}

MODES
{
	Default();
}

COMMON
{
	#include "postprocess/shared.hlsl"

	DynamicCombo( D_STAGE, 0..6, Sys( ALL ) );
}

CS
{
	SamplerState BilinearClamp < Filter( BILINEAR ); AddressU( CLAMP ); AddressV( CLAMP ); >;

	Texture2D g_tSource < Attribute( "CS2BloomSource" ); >;
	Texture2D g_tSource2 < Attribute( "CS2BloomSource2" ); >;
	RWTexture2D<float4> g_tOutput < Attribute( "CS2BloomOutput" ); >;

	// The auto exposure's state (cs2_histogram_cs): the view's exposure at CS2_EXPOSURE.
	StructuredBuffer<float> g_bExposureState < Attribute( "CS2ExposureState" ); >;

	// Two to the power of the tonemap's exposure bias.
	float g_flExposureBiasScale < Attribute( "CS2ExposureBiasScale" ); Default( 1 ); >;

	// The threshold ( -threshold / width, 1 / width, skybox strength ) and the scale: the largest bloom strength.
	float3 g_vThresholdParams < Attribute( "CS2BloomThresholdParams" ); Default3( -1, 1, 0 ); >;
	float g_flBloomScale < Attribute( "CS2BloomScale" ); Default( 1 ); >;

	// The level's tints times their weights: the one below (the smallest level's only) and its own.
	float3 g_vPrevMipBlurTint < Attribute( "CS2BloomPrevMipTint" ); Default3( 1, 1, 1 ); >;
	float3 g_vCurrMipBlurTint < Attribute( "CS2BloomCurrMipTint" ); Default3( 1, 1, 1 ); >;

	#define CS2_EXPOSURE 13

	float2 Size( Texture2D texture )
	{
		float width, height;
		texture.GetDimensions( width, height );
		return float2( width, height );
	}

	float3 TexelBorder( Texture2D texture, int2 texel, int2 size )
	{
		return all( texel >= 0 && texel < size ) ? texture.Load( int3( texel, 0 ) ).rgb : 0;
	}

	// A bilinear sample that's black outside the texture.
	float3 SampleBorder( Texture2D texture, float2 uv )
	{
		int2 size = (int2)Size( texture );
		float2 position = uv * size - 0.5;
		int2 texel = (int2)floor( position );
		float2 f = position - floor( position );
		float3 a = TexelBorder( texture, texel, size );
		float3 b = TexelBorder( texture, texel + int2( 1, 0 ), size );
		float3 c = TexelBorder( texture, texel + int2( 0, 1 ), size );
		float3 d = TexelBorder( texture, texel + int2( 1, 1 ), size );
		return lerp( lerp( a, b, f.x ), lerp( c, d, f.x ), f.y );
	}

	float3 FromTonemapSpace( float3 c )
	{
		return c * ( 1.0 / ( 1.0 - max( c.r, max( c.g, c.b ) ) ) );
	}

	// One threshold tap: out of tonemap space, faded in by the exposed luma.
	float3 ThresholdTap( float2 uv, float2 offset, float2 invSize, float exposure )
	{
		float3 c = FromTonemapSpace( g_tSource.SampleLevel( BilinearClamp, uv + offset * invSize, 0 ).rgb );
		return c * saturate( exposure * dot( c, float3( 0.3, 0.59, 0.11 ) ) * g_vThresholdParams.y + g_vThresholdParams.x );
	}

	// The 4 tap gaussian across ( 1, 0 ) or down ( 0, 1 ), in the level's pixels, kept inside the level at the far
	// side.
	float3 BlurTap( float2 fragCoord, float2 axis, float offset, float2 size )
	{
		return SampleBorder( g_tSource, min( fragCoord + axis * offset, size - 0.5 ) / size );
	}

	float3 Blur( float2 fragCoord, float2 axis )
	{
		float2 size = Size( g_tSource );
		return BlurTap( fragCoord, axis, -2.196197033, size ) * 0.1215969995
			+ BlurTap( fragCoord, axis, -0.4300631285, size ) * 0.5292130113
			+ BlurTap( fragCoord, axis, 1.0, size ) * 0.2275950015
			+ BlurTap( fragCoord, axis, 2.196197033, size ) * 0.1215969995;
	}

	// The 2x2 box of the level below, one of this level's pixels either side, kept inside the level below.
	float3 Box( float2 fragCoord, float2 size )
	{
		float2 below = Size( g_tSource );
		float4 uv = min( float4( ( fragCoord - 1.0 ) / size, ( fragCoord + 1.0 ) / size ), ( ( below - 0.5 ) / below ).xyxy );
		return SampleBorder( g_tSource, uv.xy ) + SampleBorder( g_tSource, uv.xw ) + SampleBorder( g_tSource, uv.zw ) + SampleBorder( g_tSource, uv.zy );
	}

	[numthreads( 8, 8, 1 )]
	void MainCs( uint3 id : SV_DispatchThreadID )
	{
		uint width, height;
		g_tOutput.GetDimensions( width, height );
		if ( id.x >= width || id.y >= height )
			return;

		float2 fragCoord = float2( id.xy ) + 0.5;
		float2 size = float2( width, height );

		#if D_STAGE == 0
		{
			float4 scene = g_tSource.Load( int3( id.xy, 0 ) );
			float3 c = clamp( scene.rgb, 0.0, 65504.0 ) * ( g_bExposureState[CS2_EXPOSURE] * g_flExposureBiasScale );
			g_tOutput[id.xy] = float4( c * ( 1.0 / ( max( c.r, max( c.g, c.b ) ) + 1.0 ) ), scene.a );
		}
		#elif D_STAGE == 1
		{
			float2 invSize = 1.0 / Size( g_tSource );
			float2 uv = fragCoord * invSize * 4.0;
			float exposure = g_bExposureState[CS2_EXPOSURE];

			float3 a = ThresholdTap( uv, float2( -2, -2 ), invSize, exposure );
			float3 b = ThresholdTap( uv, float2( 0, -2 ), invSize, exposure );
			float3 c = ThresholdTap( uv, float2( 2, -2 ), invSize, exposure );
			float3 d = ThresholdTap( uv, float2( -1, -1 ), invSize, exposure );
			float3 e = ThresholdTap( uv, float2( 1, -1 ), invSize, exposure );
			float3 f = ThresholdTap( uv, float2( -2, 0 ), invSize, exposure );
			float3 g = ThresholdTap( uv, float2( 0, 0 ), invSize, exposure );
			float3 h = ThresholdTap( uv, float2( 2, 0 ), invSize, exposure );
			float3 i = ThresholdTap( uv, float2( -1, 1 ), invSize, exposure );
			float3 j = ThresholdTap( uv, float2( 1, 1 ), invSize, exposure );
			float3 k = ThresholdTap( uv, float2( -2, 2 ), invSize, exposure );
			float3 l = ThresholdTap( uv, float2( 0, 2 ), invSize, exposure );
			float3 m = ThresholdTap( uv, float2( 2, 2 ), invSize, exposure );

			float3 bloom = ( d + e + i + j ) * 0.25 * 0.5
				+ ( a + b + f + g ) * 0.25 * 0.125
				+ ( b + c + g + h ) * 0.25 * 0.125
				+ ( f + g + k + l ) * 0.25 * 0.125
				+ ( g + h + l + m ) * 0.25 * 0.125;

			g_tOutput[id.xy] = float4( bloom * g_flBloomScale, 1.0 );
		}
		#elif D_STAGE == 2
		{
			// The first level reads the threshold's output (its own size) at the pixel's middle, the others the level
			// above at the corner its four pixels share.
			float2 source = Size( g_tSource );
			float2 uv = all( source == size ) ? fragCoord / source : fragCoord * 2.0 / source;
			g_tOutput[id.xy] = float4( clamp( SampleBorder( g_tSource, uv ), 0.0, 65504.0 ), 1.0 );
		}
		#elif D_STAGE == 3
		{
			g_tOutput[id.xy] = float4( Blur( fragCoord, float2( 1, 0 ) ), 1.0 );
		}
		#elif D_STAGE == 4
		{
			g_tOutput[id.xy] = float4( Blur( fragCoord, float2( 0, 1 ) ), 1.0 );
		}
		#elif D_STAGE == 5
		{
			float3 below = Box( fragCoord, size ) * ( g_vPrevMipBlurTint * 0.25 );
			g_tOutput[id.xy] = float4( below + g_tOutput[id.xy].rgb * g_vCurrMipBlurTint, 1.0 );
		}
		#else
		{
			// Source2 is the first level, the size of the output.
			float3 below = Box( fragCoord, size ) * 0.25;
			float3 level = SampleBorder( g_tSource2, min( fragCoord / size, ( size - 0.5 ) / size ) );
			g_tOutput[id.xy] = float4( below + g_vCurrMipBlurTint * level, 1.0 );
		}
		#endif
	}
}
