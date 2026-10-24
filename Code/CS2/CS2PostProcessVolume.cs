namespace CS2;

/// <summary>
/// A CS2 post_processing_volume: where in the map it applies (its bounds, or everywhere for a master volume), how fast
/// it fades in, its auto exposure settings, and its post processing resource's tonemapping curve, bloom and colour
/// correction. <see cref="CS2PostProcessing"/> on the camera blends the volumes it's in, as the game does.
/// </summary>
[Title( "CS2 Post Process Volume" ), Category( "CS2" ), Icon( "exposure" )]
public sealed class CS2PostProcessVolume : Component
{
	/// <summary>
	/// Where the volume applies, in its own space (its brush model's bounds).
	/// </summary>
	[Property] public BBox Bounds { get; set; }

	/// <summary>
	/// The map's master volume (spawnflag 1): the settings that apply wherever no other volume does.
	/// </summary>
	[Property] public bool Master { get; set; }

	/// <summary>
	/// Seconds the volume takes to fade in and out.
	/// </summary>
	[Property] public float FadeTime { get; set; }

	// The auto exposure settings, defaulting as C_PostProcessingVolume's constructor does (client.dll): the minimum and
	// maximum unset (-1), falling back to two to the power of the log ones; the compensation in stops; the speeds in
	// stops per second (a speed up of 0 snaps); the smoothing range in stops.
	[Property, Group( "Exposure" )] public bool EnableExposure { get; set; }
	[Property, Group( "Exposure" )] public float MinExposure { get; set; } = -1;
	[Property, Group( "Exposure" )] public float MaxExposure { get; set; } = -1;
	[Property, Group( "Exposure" )] public float MinLogExposure { get; set; }
	[Property, Group( "Exposure" )] public float MaxLogExposure { get; set; }
	[Property, Group( "Exposure" )] public float ExposureSpeedUp { get; set; }
	[Property, Group( "Exposure" )] public float ExposureSpeedDown { get; set; }
	[Property, Group( "Exposure" )] public float ExposureCompensation { get; set; }
	[Property, Group( "Exposure" )] public float ExposureSmoothingRange { get; set; } = 100;

	/// <summary>
	/// The post processing resource this volume came from, for reference.
	/// </summary>
	[Property, Group( "Post Processing" )] public string PostProcessing { get; set; }

	/// <summary>
	/// PostProcessingTonemapParameters_t: an exposure bias and a filmic curve. Without it, the volume leaves the curve to
	/// the others (or the default).
	/// </summary>
	[Property, Group( "Post Processing" )] public bool HasTonemap { get; set; }
	[Property, Group( "Post Processing" )] public CS2TonemapCurve Tonemap { get; set; } = CS2TonemapCurve.DefaultLinear;

	/// <summary>
	/// PostProcessingBloomParameters_t.
	/// </summary>
	[Property, Group( "Post Processing" )] public bool HasBloom { get; set; }
	[Property, Group( "Post Processing" )] public CS2BloomSettings Bloom { get; set; } = new();

	/// <summary>
	/// The colour correction lookup table, a 3D texture the mount serves (empty without one).
	/// </summary>
	[Property, Group( "Post Processing" )] public string ColorCorrection { get; set; }
}

/// <summary>
/// PostProcessingTonemapParameters_t, in its field order: the exposure bias (stops), the filmic curve
/// ( x( Ax + CB ) + DE ) / ( x( Ax + B ) + DF ) - E / F and its white point (stops), then the local exposure settings.
/// </summary>
public struct CS2TonemapCurve
{
	public float ExposureBias { get; set; }
	public float ShoulderStrength { get; set; }
	public float LinearStrength { get; set; }
	public float LinearAngle { get; set; }
	public float ToeStrength { get; set; }
	public float ToeNum { get; set; }
	public float ToeDenom { get; set; }
	public float WhitePoint { get; set; }
	public float LuminanceSource { get; set; }
	public float ExposureBiasShadows { get; set; }
	public float ExposureBiasHighlights { get; set; }
	public float MinShadowLum { get; set; }
	public float MaxShadowLum { get; set; }
	public float MinHighlightLum { get; set; }
	public float MaxHighlightLum { get; set; }

	/// <summary>
	/// The curve the game fills in where volumes leave weight over, when the game sets
	/// Tonemapping_DefaultFilmicLinear (CS2 does): nearly linear, white at 1.5 stops (materialsystem2.dll).
	/// </summary>
	public static CS2TonemapCurve DefaultLinear => new()
	{
		ShoulderStrength = 0, LinearStrength = 0.001f, LinearAngle = 0.001f, ToeStrength = 1, ToeNum = 1, ToeDenom = 1,
		WhitePoint = 1.5f, MaxShadowLum = 0.5f, MinHighlightLum = 2, MaxHighlightLum = 8,
	};
}

/// <summary>
/// PostProcessingBloomParameters_t. The blend mode is 0 add, 1 screen, 2 blur.
/// </summary>
public sealed class CS2BloomSettings
{
	public int BlendMode { get; set; }
	public float BloomStrength { get; set; }
	public float ScreenBloomStrength { get; set; }
	public float BlurBloomStrength { get; set; }
	public float BloomThreshold { get; set; }
	public float BloomThresholdWidth { get; set; }
	public float SkyboxBloomStrength { get; set; }
	public float BloomStartValue { get; set; }
	public float[] BlurWeight { get; set; } = [0.2f, 0.2f, 0.2f, 0.2f, 0.2f];
	public Vector3[] BlurTint { get; set; } = [Vector3.One, Vector3.One, Vector3.One, Vector3.One, Vector3.One];
}
