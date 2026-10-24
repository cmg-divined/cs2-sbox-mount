//=========================================================================================================================
// CS2's auto exposure: histogram_cs.vfx's average luminance, then CTonemapSystem's exposure, kept on the GPU.
//
// From the game's compiled shader (decompiled with Source 2 Viewer, D_HISTOGRAM_MODE 1 and 2):
//  - D_STAGE 0: the bins cleared (the game clears them on the CPU side).
//  - D_STAGE 1 (mode 1): each thread takes a tile of pixels (the step), each pixel's luma (Rec. 709) of the scene before
//    exposure, at least 0.005, binned by its log2 across the log luminance range into 256 bins, counted in group memory
//    and added to the bins.
//  - D_STAGE 2 (mode 2): the mean bin, back from log space: 2^( meanBin / 255 * range + minimum ).
// Then, where the game reads that back and steps the exposure on the CPU (scenesystem.dll, CTonemapSystem, decompiled
// with IDA), stage 2 steps it here, so it's ready the same frame:
//  - the target: 0.18 over the average luminance; kept in a history of the last ten, and once there are ten, their
//    average weighted by |5 - i| / 5 (oldest first); clamped to the minimum and maximum exposure; times the
//    compensation.
//  - the exposure moves to the target in log2 space at the speed up or down (per second), slower within the smoothing
//    range (at most half the distance), never past the target. A speed up of 0 snaps it there.
// The game also blends towards an override exposure some events set, which nothing here sets.
//
// The state (CS2ExposureState, floats): the history's count, the ten, the target, the current exposure, the exposure the
// view uses, the last average luminance.
//=========================================================================================================================

HEADER
{
	DevShader = true;
	Description = "CS2 histogram_cs";
}

MODES
{
	Default();
}

COMMON
{
	#include "postprocess/shared.hlsl"

	DynamicCombo( D_STAGE, 0..2, Sys( ALL ) );
}

CS
{
	Texture2D g_tInput < Attribute( "CS2HistogramInput" ); >;
	RWStructuredBuffer<uint> g_bHistogramBins < Attribute( "CS2HistogramBins" ); >;
	RWStructuredBuffer<float> g_bState < Attribute( "CS2ExposureState" ); >;

	// The log2 luminance range's minimum and (binning) one over its size or (averaging) its size, and the pixels each
	// thread takes along each side.
	float g_flLogLuminanceMin < Attribute( "CS2HistogramLogLuminanceMin" ); Default( -8 ); >;
	float g_flLogLuminanceScale < Attribute( "CS2HistogramLogLuminanceScale" ); Default( 1 ); >;
	int g_nStep < Attribute( "CS2HistogramStep" ); Default( 1 ); >;

	// The exposure's settings: the minimum and maximum, the compensation multiplier, the speeds up and down, the smoothing
	// range (stops), and the seconds since the last step.
	float g_flMinExposure < Attribute( "CS2ExposureMin" ); Default( 1 ); >;
	float g_flMaxExposure < Attribute( "CS2ExposureMax" ); Default( 1 ); >;
	float g_flCompensation < Attribute( "CS2ExposureCompensation" ); Default( 1 ); >;
	float g_flSpeedUp < Attribute( "CS2ExposureSpeedUp" ); Default( 1 ); >;
	float g_flSpeedDown < Attribute( "CS2ExposureSpeedDown" ); Default( 2 ); >;
	float g_flSmoothingRange < Attribute( "CS2ExposureSmoothingRange" ); Default( 100 ); >;
	float g_flDeltaTime < Attribute( "CS2ExposureDeltaTime" ); Default( 0 ); >;

	#define CS2_HISTORY_COUNT 0
	#define CS2_HISTORY 1
	#define CS2_TARGET 11
	#define CS2_CURRENT 12
	#define CS2_EXPOSURE 13
	#define CS2_LUMINANCE 14

	groupshared uint Bins[256];

	bool IsFinite( float x )
	{
		return ( asuint( x ) & 0x7F800000 ) != 0x7F800000;
	}

	float2 InputSize()
	{
		float width, height;
		g_tInput.GetDimensions( width, height );
		return float2( width, height );
	}

	// CTonemapSystem's step, from the frame's average luminance.
	void StepExposure( float luminance )
	{
		g_bState[CS2_LUMINANCE] = luminance;

		float sample = 0.18000001 / luminance;
		if ( !IsFinite( sample ) )
			return;

		uint count = (uint)g_bState[CS2_HISTORY_COUNT];
		if ( count >= 10 )
		{
			for ( uint i = 0; i < 9; i++ )
				g_bState[CS2_HISTORY + i] = g_bState[CS2_HISTORY + i + 1];

			g_bState[CS2_HISTORY + 9] = sample;
		}
		else
		{
			g_bState[CS2_HISTORY + count] = sample;
			count++;
			g_bState[CS2_HISTORY_COUNT] = count;
		}

		if ( count == 10 )
		{
			float sum = 0, weights = 0;
			for ( int i = 0; i < 10; i++ )
			{
				float weight = abs( 5 - i ) * 0.2;
				weights += weight;
				sum += weight * g_bState[CS2_HISTORY + i];
			}

			float average = ( 1.0 / weights ) * sum;
			float target = g_flMinExposure <= average ? min( g_flMaxExposure, average ) : g_flMinExposure;
			if ( IsFinite( target ) )
				g_bState[CS2_TARGET] = target;
		}
		else
		{
			g_bState[CS2_TARGET] = g_flMinExposure <= sample ? min( g_flMaxExposure, sample ) : g_flMinExposure;
		}

		if ( g_flCompensation > 0 )
			g_bState[CS2_TARGET] *= g_flCompensation;

		float goal = g_bState[CS2_TARGET];
		float current = g_bState[CS2_CURRENT];

		if ( g_flSpeedUp == 0 )
		{
			current = goal;
		}
		else
		{
			float speed = current > goal ? g_flSpeedDown : g_flSpeedUp;
			float logCurrent = log2( current );
			float distance = abs( log2( goal ) - logCurrent );
			if ( g_flSmoothingRange > distance )
				speed = min( distance * 0.5, speed );

			if ( current > goal )
				speed = -speed;

			current = exp2( speed * max( g_flDeltaTime, 0 ) + logCurrent );
			current = speed >= 0 ? min( current, goal ) : max( current, goal );
			if ( !IsFinite( current ) )
				current = goal;
		}

		g_bState[CS2_CURRENT] = current;
		g_bState[CS2_EXPOSURE] = current;
	}

	#if D_STAGE == 1

	[numthreads( 16, 16, 1 )]
	void MainCs( uint3 id : SV_DispatchThreadID, uint index : SV_GroupIndex )
	{
		Bins[index] = 0;
		GroupMemoryBarrierWithGroupSync();

		uint2 size = (uint2)InputSize();
		uint2 tile = id.xy * (uint)g_nStep;
		for ( uint y = 0; y < (uint)g_nStep; y++ )
		{
			for ( uint x = 0; x < (uint)g_nStep; x++ )
			{
				uint2 pixel = tile + uint2( x, y );
				if ( pixel.x >= size.x || pixel.y >= size.y )
					break;

				float luma = dot( g_tInput.Load( int3( pixel, 0 ) ).rgb, float3( 0.2125, 0.7154, 0.0721 ) );
				uint bin = (uint)( saturate( ( log2( max( luma, 0.005 ) ) - g_flLogLuminanceMin ) * g_flLogLuminanceScale ) * 255.0 );
				InterlockedAdd( Bins[bin], 1 );
			}
		}

		GroupMemoryBarrierWithGroupSync();
		if ( Bins[index] > 0 )
			InterlockedAdd( g_bHistogramBins[index], Bins[index] );
	}

	#else

	[numthreads( 256, 1, 1 )]
	void MainCs( uint index : SV_GroupIndex )
	{
		#if D_STAGE == 0
			g_bHistogramBins[index] = 0;
		#else
			Bins[index] = g_bHistogramBins[index] * index;
			GroupMemoryBarrierWithGroupSync();

			for ( uint stride = 128; stride > 0; stride >>= 1 )
			{
				if ( index < stride )
					Bins[index] += Bins[index + stride];

				GroupMemoryBarrierWithGroupSync();
			}

			if ( index == 0 )
			{
				uint2 size = (uint2)InputSize();
				float meanBin = float( Bins[0] ) / max( float( size.x * size.y ), 1.0 );
				StepExposure( exp2( meanBin * 0.0039215689 * g_flLogLuminanceScale + g_flLogLuminanceMin ) );
			}
		#endif
	}

	#endif
}
