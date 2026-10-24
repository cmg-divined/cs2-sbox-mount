using System;
using System.IO;

namespace Sandbox.Mounting.CS2;

/// <summary>
/// A compiled post processing resource (vpost_c, PostProcessingResource_t): the tonemapping curve, bloom, vignette and
/// local contrast settings a post_processing_volume applies, and its colour correction as a 3D lookup table.
/// </summary>
internal sealed class CompiledPostProcessing
{
	/// <summary>
	/// PostProcessingTonemapParameters_t, in its field order, or null without tonemapping.
	/// </summary>
	public TonemapParameters? Tonemap { get; }

	/// <summary>
	/// PostProcessingBloomParameters_t, or null without bloom.
	/// </summary>
	public BloomParameters Bloom { get; }

	/// <summary>
	/// PostProcessingVignetteParameters_t's values by name, or null without a vignette.
	/// </summary>
	public KVObject Vignette { get; }

	/// <summary>
	/// PostProcessingLocalContrastParameters_t's values by name, or null without local contrast.
	/// </summary>
	public KVObject LocalContrast { get; }

	/// <summary>
	/// The colour correction lookup table's size on each side, or 0 without one.
	/// </summary>
	public int ColorCorrectionSize { get; }

	/// <summary>
	/// The lookup table, RGBA8, red fastest then green then blue.
	/// </summary>
	public byte[] ColorCorrection { get; }

	public CompiledPostProcessing( byte[] data )
	{
		var resource = new ResourceFile( data );
		var root = resource.ReadKeyValues( "DATA" ) ?? throw new InvalidDataException( "Post processing has no DATA block." );

		if ( root.GetBool( "m_bHasTonemapParams" ) && root.GetObject( "m_toneMapParams" ) is { } tonemap )
			Tonemap = new TonemapParameters( tonemap );

		if ( root.GetBool( "m_bHasBloomParams" ) && root.GetObject( "m_bloomParams" ) is { } bloom )
			Bloom = new BloomParameters( bloom );

		if ( root.GetBool( "m_bHasVignetteParams" ) )
			Vignette = root.GetObject( "m_vignetteParams" );

		// The game's own spelling.
		if ( root.GetBool( "m_bHasLocalContrastParams" ) )
			LocalContrast = root.GetObject( "m_localConstrastParams" );

		// Resources from before Aperture Desk Job have no flag and always carry a table.
		var hasColorCorrection = !root.ContainsKey( "m_bHasColorCorrection" ) || root.GetBool( "m_bHasColorCorrection" );
		var size = root.GetInt32( "m_nColorCorrectionVolumeDim" );
		var table = root.GetBlob( "m_colorCorrectionVolumeData" ) ?? root.GetInt64Array( "m_colorCorrectionVolumeData" ).Select( x => (byte)x ).ToArray();

		if ( hasColorCorrection && size > 0 && table.Length == size * size * size * 4 )
		{
			ColorCorrectionSize = size;
			ColorCorrection = table;
		}
	}
}

/// <summary>
/// PostProcessingTonemapParameters_t: an exposure bias (stops) and a filmic curve, ( x( Ax + CB ) + DE ) / ( x( Ax + B ) + DF )
/// - E / F, normalized at the white point (stops); then the local exposure settings.
/// </summary>
internal readonly record struct TonemapParameters(
	float ExposureBias, float ShoulderStrength, float LinearStrength, float LinearAngle, float ToeStrength, float ToeNum,
	float ToeDenom, float WhitePoint, float LuminanceSource, float ExposureBiasShadows, float ExposureBiasHighlights,
	float MinShadowLum, float MaxShadowLum, float MinHighlightLum, float MaxHighlightLum )
{
	public TonemapParameters( KVObject kv ) : this(
		kv.GetFloat( "m_flExposureBias" ), kv.GetFloat( "m_flShoulderStrength" ), kv.GetFloat( "m_flLinearStrength" ),
		kv.GetFloat( "m_flLinearAngle" ), kv.GetFloat( "m_flToeStrength" ), kv.GetFloat( "m_flToeNum" ),
		kv.GetFloat( "m_flToeDenom" ), kv.GetFloat( "m_flWhitePoint" ), kv.GetFloat( "m_flLuminanceSource" ),
		kv.GetFloat( "m_flExposureBiasShadows" ), kv.GetFloat( "m_flExposureBiasHighlights" ), kv.GetFloat( "m_flMinShadowLum" ),
		kv.GetFloat( "m_flMaxShadowLum" ), kv.GetFloat( "m_flMinHighlightLum" ), kv.GetFloat( "m_flMaxHighlightLum" ) )
	{
	}
}

/// <summary>
/// PostProcessingBloomParameters_t. The blend mode is BLOOM_BLEND_ADD (0), _SCREEN (1) or _BLUR (2).
/// </summary>
internal sealed record BloomParameters( int BlendMode, float BloomStrength, float ScreenBloomStrength, float BlurBloomStrength,
	float BloomThreshold, float BloomThresholdWidth, float SkyboxBloomStrength, float BloomStartValue, float[] BlurWeight,
	float[][] BlurTint )
{
	public BloomParameters( KVObject kv ) : this(
		kv["m_blendMode"] switch
		{
			string text => text switch { "BLOOM_BLEND_SCREEN" => 1, "BLOOM_BLEND_BLUR" => 2, _ => 0 },
			{ } value => (int)KVValue.ToInt64( value ),
			_ => 0,
		},
		kv.GetFloat( "m_flBloomStrength" ), kv.GetFloat( "m_flScreenBloomStrength" ), kv.GetFloat( "m_flBlurBloomStrength" ),
		kv.GetFloat( "m_flBloomThreshold" ), kv.GetFloat( "m_flBloomThresholdWidth" ), kv.GetFloat( "m_flSkyboxBloomStrength" ),
		kv.GetFloat( "m_flBloomStartValue" ), kv.GetFloatArray( "m_flBlurWeight" ),
		kv.GetArray( "m_vBlurTint" )?.Select( x => x is object[] tint ? tint.Select( KVValue.ToFloat ).ToArray() : [1f, 1f, 1f] ).ToArray() ?? [] )
	{
	}
}
