using Sandbox.Rendering;
using System;

namespace CS2;

/// <summary>
/// CS2's post processing, for the camera it's on or the cameras in its post process volume: the auto exposure, bloom,
/// tonemapping curve, colour correction and gamma, from the <see cref="CS2PostProcessVolume"/>s the camera is in, as
/// the game draws them. In game it turns off the camera's own s&amp;box effects it replaces (Tonemapping, Bloom,
/// ColorGrading, ColorAdjustments, Sharpen and Vignette) while it draws for it; in the editor, turn them off yourself.
/// </summary>
/// <remarks>
/// The passes are the game's (client.dll and the shaders, decompiled), in cs2_histogram_cs, cs2_gaussian_bloom and
/// cs2_postprocess. Like every s&amp;box post process, it shows in the editor while the camera is selected, and in game.
/// <para>
/// Not verified: how the game weighs overlapping volumes against the master one (here the others fade in over their
/// fade time and the master has what weight they leave), and which volume's exposure settings apply when several
/// have them (here the heaviest).
/// </para>
/// </remarks>
[Title( "CS2 Post Processing" ), Category( "CS2" ), Icon( "camera" )]
public sealed class CS2PostProcessing : BasePostProcess<CS2PostProcessing>
{
	/// <summary>
	/// r_fullscreen_gamma: 2.2 by default (rendersystem), 1 to 4.
	/// </summary>
	[Property, Range( 1, 4 )] public float FullScreenGamma { get; set; } = 2.2f;

	/// <summary>
	/// r_csgo_render_post_bloom.
	/// </summary>
	[Property] public bool Bloom { get; set; } = true;

	/// <summary>
	/// The exposure the auto exposure keeps between, its compensation, speeds and smoothing range where no volume sets
	/// them: CTonemapSystem's defaults (scenesystem.dll), the minimum and maximum as CS2's gameinfo sets them
	/// (Tonemapping_DefaultAutoExposureMin and Max).
	/// </summary>
	[Property, Group( "Default Exposure" )] public float DefaultMinExposure { get; set; } = 1;
	[Property, Group( "Default Exposure" )] public float DefaultMaxExposure { get; set; } = 1;
	[Property, Group( "Default Exposure" )] public float DefaultCompensation { get; set; } = 1;
	[Property, Group( "Default Exposure" )] public float DefaultSpeedUp { get; set; } = 1;
	[Property, Group( "Default Exposure" )] public float DefaultSpeedDown { get; set; } = 2;
	[Property, Group( "Default Exposure" )] public float DefaultSmoothingRange { get; set; } = 100;

	private static readonly Material PostShader = Material.FromShader( "shaders/cs2_postprocess.shader" );
	private static readonly ComputeShader BloomShader = new( "shaders/cs2_gaussian_bloom.shader" );
	private static readonly ComputeShader HistogramShader = new( "shaders/cs2_histogram_cs.shader" );

	/// <summary>
	/// The histogram's log2 luminance range: from -8, 13 stops (client.dll).
	/// </summary>
	private const float LogLuminanceMin = -8;
	private const float LogLuminanceRange = 13;

	/// <summary>
	/// The exposure state's size and first value (cs2_histogram_cs): CTonemapSystem starts with a history of ones and an
	/// exposure of one.
	/// </summary>
	private static readonly float[] InitialExposureState = [0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0];

	private readonly CommandList _commands = new( "CS2 Post Processing" );
	private readonly Dictionary<CS2PostProcessVolume, float> _fades = [];
	private readonly Dictionary<string, Texture> _colorCorrections = new( StringComparer.OrdinalIgnoreCase );
	private GpuBuffer<uint> _bins;
	private GpuBuffer<float> _exposureState;
	private RealTimeSince _sinceRender;
	private bool _rendered;

	/// <summary>
	/// The cameras' own effects this turned off, to turn back on when it's gone.
	/// </summary>
	private readonly List<BasePostProcess> _turnedOff = [];

	/// <summary>
	/// The cameras whose own auto exposure this turned off, to turn back on when it's gone.
	/// </summary>
	private readonly List<CameraComponent> _exposureTurnedOff = [];

	protected override void OnDisabled()
	{
		base.OnDisabled();

		foreach ( var effect in _turnedOff )
		{
			if ( effect.IsValid() )
				effect.Enabled = true;
		}

		_turnedOff.Clear();

		foreach ( var camera in _exposureTurnedOff )
		{
			if ( camera.IsValid() )
				camera.AutoExposure.Enabled = true;
		}

		_exposureTurnedOff.Clear();

		// Frames already queued may still use them.
		var bins = _bins;
		var state = _exposureState;
		_bins = null;
		_exposureState = null;
		_ = DisposeLater( bins, state );
	}

	private static async System.Threading.Tasks.Task DisposeLater( GpuBuffer bins, GpuBuffer state )
	{
		await GameTask.DelayRealtimeSeconds( 2 );
		bins?.Dispose();
		state?.Dispose();
	}

	/// <summary>
	/// Turns off the camera's own s&amp;box effects that do what this does, in game: a map brings this in a volume
	/// (the mount's map scenes), so a game's camera set up for a look of its own shows the map as CS2 does. Not in the
	/// editor, where it would be saved with the camera's scene. CS2 draws no vignette outside gameplay overlays (scopes,
	/// the death cam, the buy menu), so the camera's goes too; and the camera's own auto exposure (the engine's, which
	/// s&amp;box's Tonemapping turns on and leaves on), as this exposes the frame itself.
	/// </summary>
	private void TakeOver( CameraComponent camera )
	{
		if ( Scene.IsEditor || !camera.IsValid() )
			return;

		if ( camera.AutoExposure.Enabled )
		{
			camera.AutoExposure.Enabled = false;
			if ( !_exposureTurnedOff.Contains( camera ) )
				_exposureTurnedOff.Add( camera );
		}

		foreach ( var effect in camera.GameObject.Components.GetAll<BasePostProcess>( FindMode.EnabledInSelfAndDescendants ).ToList() )
		{
			if ( effect is Tonemapping or Sandbox.Bloom or ColorGrading or ColorAdjustments or Sharpen or Vignette )
			{
				effect.Enabled = false;
				_turnedOff.Add( effect );
			}
		}
	}

	public override void Render()
	{
		TakeOver( Camera );

		var deltaTime = _rendered ? (float)_sinceRender : 0;
		_sinceRender = 0;
		_rendered = true;

		if ( _exposureState is null )
		{
			_bins = new GpuBuffer<uint>( 256, GpuBuffer.UsageFlags.Structured, "CS2HistogramBins" );
			_exposureState = new GpuBuffer<float>( InitialExposureState.Length, GpuBuffer.UsageFlags.Structured, "CS2ExposureState" );
			_exposureState.SetData( InitialExposureState );
		}

		var position = Camera.PostProcessAnchor.IsValid() ? Camera.PostProcessAnchor.WorldPosition : Camera.WorldPosition;
		var volumes = WeighVolumes( position, deltaTime );

		var tonemap = BlendTonemap( volumes );
		var bloom = Bloom ? BlendBloom( volumes ) : null;

		var cl = _commands;
		cl.Reset();

		var frame = cl.Attributes.GrabFrameTexture( "CS2Frame" );
		var exposureBiasScale = MathF.Pow( 2, tonemap.ExposureBias );

		AutoExposure( cl, frame, volumes, deltaTime );

		// The colour buffer in tonemap space, as the game resolves it.
		var colorBuffer = cl.GetRenderTarget( "CS2ColorBuffer", ImageFormat.RGBA16161616F, 1, 1 );
		cl.Attributes.Set( "CS2ExposureState", _exposureState );
		cl.Attributes.Set( "CS2ExposureBiasScale", exposureBiasScale );
		cl.Attributes.Set( "CS2BloomSource", frame.ColorTexture );
		Dispatch( cl, 0, colorBuffer );

		// The pass to the screen.
		cl.Attributes.Set( "CS2ColorBuffer", colorBuffer.ColorTexture );
		cl.Attributes.Set( "CS2BloomEnabled", bloom is not null ? 1f : 0f );
		if ( bloom is not null )
		{
			var bloomBuffer = GaussianBloom( cl, colorBuffer, bloom );

			var total = bloom.BloomStrength + bloom.ScreenBloomStrength + bloom.BlurBloomStrength;
			var normalized = total > 0
				? new Vector3( bloom.BloomStrength, bloom.ScreenBloomStrength, bloom.BlurBloomStrength ) / total
				: new Vector3( 1, 0, 0 );

			cl.Attributes.Set( "CS2Bloom", bloomBuffer.ColorTexture );
			cl.Attributes.Set( "CS2NormalizedBloomStrengths", normalized );
			cl.Attributes.Set( "CS2UnNormalizedBloomStrengths", new Vector3( bloom.BloomStrength, bloom.ScreenBloomStrength, bloom.BlurBloomStrength ) );
		}

		var curve = TonemapConstants( tonemap );
		cl.Attributes.Set( "CS2CurveABCD", curve.ABCD );
		cl.Attributes.Set( "CS2CurveEFWhite", curve.EFWhite );

		var (colorCorrection, defaultWeight) = BlendColorCorrection( volumes );
		cl.Attributes.Set( "CS2ColorCorrectionEnabled", colorCorrection is not null ? 1f : 0f );
		cl.Attributes.Set( "CS2ColorCorrectionDefaultWeight", defaultWeight );
		if ( colorCorrection is not null )
			cl.Attributes.Set( "CS2ColorCorrection", colorCorrection );

		cl.Attributes.Set( "CS2ViewFadeColor", Vector4.Zero );
		cl.Attributes.Set( "CS2FadeMode", 0f );
		cl.Attributes.Set( "CS2FullScreenGamma", FullScreenGamma );
		cl.Blit( PostShader );

		InsertCommandList( cl, Stage.Tonemapping, 0, "CS2 Post Processing" );
	}

	/// <summary>
	/// The histogram of the scene before exposure, its average, and the exposure stepped towards it.
	/// </summary>
	private void AutoExposure( CommandList cl, RenderTargetHandle frame, List<(CS2PostProcessVolume Volume, float Weight)> volumes, float deltaTime )
	{
		// The heaviest volume with exposure settings; the defaults without one.
		var min = DefaultMinExposure;
		var max = DefaultMaxExposure;
		var compensation = DefaultCompensation;
		var speedUp = DefaultSpeedUp;
		var speedDown = DefaultSpeedDown;
		var smoothing = DefaultSmoothingRange;

		CS2PostProcessVolume exposure = null;
		var heaviest = 0f;
		foreach ( var (volume, weight) in volumes )
		{
			if ( volume.EnableExposure && weight > heaviest )
			{
				exposure = volume;
				heaviest = weight;
			}
		}

		if ( exposure is not null )
		{
			// As C_PostProcessingVolume hands them over (client.dll).
			min = exposure.MinExposure > 0 ? exposure.MinExposure : MathF.Pow( 2, exposure.MinLogExposure );
			max = exposure.MaxExposure > 0 ? exposure.MaxExposure : MathF.Pow( 2, exposure.MaxLogExposure );
			compensation = MathF.Pow( 2, exposure.ExposureCompensation );
			speedUp = exposure.ExposureSpeedUp;
			speedDown = exposure.ExposureSpeedDown;
			smoothing = exposure.ExposureSmoothingRange;
		}

		cl.Attributes.Set( "CS2HistogramInput", frame.ColorTexture );
		cl.Attributes.Set( "CS2HistogramBins", _bins );
		cl.Attributes.Set( "CS2ExposureState", _exposureState );
		cl.Attributes.Set( "CS2HistogramLogLuminanceMin", LogLuminanceMin );
		cl.Attributes.Set( "CS2HistogramStep", 1 );
		cl.Attributes.Set( "CS2ExposureMin", min );
		cl.Attributes.Set( "CS2ExposureMax", max );
		cl.Attributes.Set( "CS2ExposureCompensation", compensation );
		cl.Attributes.Set( "CS2ExposureSpeedUp", speedUp );
		cl.Attributes.Set( "CS2ExposureSpeedDown", speedDown );
		cl.Attributes.Set( "CS2ExposureSmoothingRange", smoothing );
		cl.Attributes.Set( "CS2ExposureDeltaTime", deltaTime );

		cl.Attributes.SetCombo( "D_STAGE", 0 );
		cl.DispatchCompute( HistogramShader, 256, 1, 1 );
		cl.ResourceBarrierTransition( _bins, ResourceState.UnorderedAccess );

		cl.Attributes.Set( "CS2HistogramLogLuminanceScale", 1 / LogLuminanceRange );
		cl.Attributes.SetCombo( "D_STAGE", 1 );
		cl.DispatchCompute( HistogramShader, frame.Size );
		cl.ResourceBarrierTransition( _bins, ResourceState.UnorderedAccess );

		cl.Attributes.Set( "CS2HistogramLogLuminanceScale", LogLuminanceRange );
		cl.Attributes.SetCombo( "D_STAGE", 2 );
		cl.ResourceBarrierTransition( _exposureState, ResourceState.UnorderedAccess );
		cl.DispatchCompute( HistogramShader, 256, 1, 1 );
		cl.ResourceBarrierTransition( _exposureState, ResourceState.AllShaderResource );
	}

	/// <summary>
	/// downsample_bloomthreshold to quarter resolution, then CGaussianBloomBlurRenderer's levels down and back up into
	/// the bloom buffer (client.dll): four levels from quarter resolution, its five weights' first two going to the
	/// first.
	/// </summary>
	private static RenderTargetHandle GaussianBloom( CommandList cl, RenderTargetHandle colorBuffer, CS2BloomSettings bloom )
	{
		var width = MathF.Max( bloom.BloomThresholdWidth, 0.0001f );
		cl.Attributes.Set( "CS2BloomThresholdParams", new Vector3( -bloom.BloomThreshold / width, 1 / width, bloom.SkyboxBloomStrength ) );
		cl.Attributes.Set( "CS2BloomScale", MathF.Max( MathF.Max( bloom.BloomStrength, bloom.ScreenBloomStrength ), bloom.BlurBloomStrength ) );

		var threshold = Target( cl, "CS2BloomThreshold", 4 );
		cl.Attributes.Set( "CS2BloomSource", colorBuffer.ColorTexture );
		Dispatch( cl, 1, threshold );

		// Down: each level from the one above, blurred across into its temporary and back down.
		var level0 = Level( cl, threshold, Target( cl, "CS2BloomLevel0", 4 ), Target( cl, "CS2BloomTemp0", 4 ) );
		var level1 = Level( cl, level0, Target( cl, "CS2BloomLevel1", 8 ), Target( cl, "CS2BloomTemp1", 8 ) );
		var level2 = Level( cl, level1, Target( cl, "CS2BloomLevel2", 16 ), Target( cl, "CS2BloomTemp2", 16 ) );
		var level3 = Level( cl, level2, Target( cl, "CS2BloomLevel3", 32 ), Target( cl, "CS2BloomTemp3", 32 ) );

		// Up: the smallest level comes in at its own tint, the others at one.
		Vector3 Tint( int i ) => bloom.BlurTint[i] * bloom.BlurWeight[i];
		Combine( cl, level3, level2, Tint( 4 ), Tint( 3 ) );
		Combine( cl, level2, level1, Vector3.One, Tint( 2 ) );

		// The first level has the first two weights.
		var buffer = Target( cl, "CS2BloomBuffer", 4 );
		cl.Attributes.Set( "CS2BloomCurrMipTint", Tint( 0 ) + Tint( 1 ) );
		cl.Attributes.Set( "CS2BloomSource", level1.ColorTexture );
		cl.Attributes.Set( "CS2BloomSource2", level0.ColorTexture );
		Dispatch( cl, 6, buffer );

		return buffer;
	}

	private static RenderTargetHandle Target( CommandList cl, string name, int sizeFactor )
	{
		return cl.GetRenderTarget( name, ImageFormat.RGBA16161616F, 1, sizeFactor );
	}

	private static RenderTargetHandle Level( CommandList cl, RenderTargetHandle above, RenderTargetHandle level, RenderTargetHandle temp )
	{
		cl.Attributes.Set( "CS2BloomSource", above.ColorTexture );
		Dispatch( cl, 2, level );

		cl.Attributes.Set( "CS2BloomSource", level.ColorTexture );
		Dispatch( cl, 3, temp );

		cl.Attributes.Set( "CS2BloomSource", temp.ColorTexture );
		Dispatch( cl, 4, level );
		return level;
	}

	private static void Combine( CommandList cl, RenderTargetHandle below, RenderTargetHandle level, Vector3 belowTint, Vector3 levelTint )
	{
		cl.Attributes.Set( "CS2BloomPrevMipTint", belowTint );
		cl.Attributes.Set( "CS2BloomCurrMipTint", levelTint );
		cl.Attributes.Set( "CS2BloomSource", below.ColorTexture );
		Dispatch( cl, 5, level );
	}

	/// <summary>
	/// One cs2_gaussian_bloom stage into the target, ready to be read after.
	/// </summary>
	private static void Dispatch( CommandList cl, int stage, RenderTargetHandle target )
	{
		cl.ResourceBarrierTransition( target, ResourceState.UnorderedAccess );
		cl.Attributes.Set( "CS2BloomOutput", target.ColorTexture );
		cl.Attributes.SetCombo( "D_STAGE", stage );
		cl.DispatchCompute( BloomShader, target.Size );
		cl.ResourceBarrierTransition( target, ResourceState.AllShaderResource );
	}

	/// <summary>
	/// The volumes the camera takes settings from, with their weights: the others the camera is in, fading in and out
	/// over their fade time, then the master volume with the weight they leave.
	/// </summary>
	private List<(CS2PostProcessVolume Volume, float Weight)> WeighVolumes( Vector3 position, float deltaTime )
	{
		var weighed = new List<(CS2PostProcessVolume Volume, float Weight)>();
		CS2PostProcessVolume master = null;
		var taken = 0f;

		foreach ( var volume in Scene.GetAllComponents<CS2PostProcessVolume>() )
		{
			if ( !volume.Active )
				continue;

			if ( volume.Master && master is null )
			{
				master = volume;
				continue;
			}

			var inside = volume.Bounds.Contains( volume.WorldTransform.PointToLocal( position ) );
			_fades.TryGetValue( volume, out var fade );
			var step = volume.FadeTime > 0 ? deltaTime / volume.FadeTime : 1;
			fade = inside ? MathF.Min( fade + step, 1 ) : MathF.Max( fade - step, 0 );
			_fades[volume] = fade;

			var weight = MathF.Min( fade, 1 - taken );
			if ( weight <= 0 )
				continue;

			weighed.Add( (volume, weight) );
			taken += weight;
		}

		if ( master is not null && taken < 1 )
			weighed.Add( (master, 1 - taken) );

		return weighed;
	}

	/// <summary>
	/// The volumes' curves weighed together (materialsystem2.dll): divided by the total weight if it's at least one,
	/// otherwise the rest of the weight given to the default curve.
	/// </summary>
	private static CS2TonemapCurve BlendTonemap( List<(CS2PostProcessVolume Volume, float Weight)> volumes )
	{
		var sum = new CS2TonemapCurve();
		var total = 0f;
		foreach ( var (volume, weight) in volumes )
		{
			if ( !volume.HasTonemap )
				continue;

			Add( ref sum, volume.Tonemap, weight );
			total += weight;
		}

		if ( total >= 1 )
			return Scaled( sum, 1 / total );

		Add( ref sum, CS2TonemapCurve.DefaultLinear, 1 - total );
		return sum;

		static void Add( ref CS2TonemapCurve sum, CS2TonemapCurve curve, float weight )
		{
			sum.ExposureBias += curve.ExposureBias * weight;
			sum.ShoulderStrength += curve.ShoulderStrength * weight;
			sum.LinearStrength += curve.LinearStrength * weight;
			sum.LinearAngle += curve.LinearAngle * weight;
			sum.ToeStrength += curve.ToeStrength * weight;
			sum.ToeNum += curve.ToeNum * weight;
			sum.ToeDenom += curve.ToeDenom * weight;
			sum.WhitePoint += curve.WhitePoint * weight;
		}

		static CS2TonemapCurve Scaled( CS2TonemapCurve curve, float scale )
		{
			var scaled = new CS2TonemapCurve();
			Add( ref scaled, curve, scale );
			return scaled;
		}
	}

	/// <summary>
	/// The post processing constant buffer's curve (materialsystem2.dll): A to D, then E, F, the white point (two to the
	/// power of it in stops) and one over the curve there.
	/// </summary>
	private static (Vector4 ABCD, Vector4 EFWhite) TonemapConstants( CS2TonemapCurve curve )
	{
		var white = MathF.Pow( 2, curve.WhitePoint );
		var a = curve.ShoulderStrength;
		var b = curve.LinearStrength;
		var c = curve.LinearAngle;
		var d = curve.ToeStrength;
		var e = curve.ToeNum;
		var f = curve.ToeDenom;
		var atWhite = (white * (a * white + c * b) + d * e) / (white * (a * white + b) + d * f) - e / f;

		return (new Vector4( a, b, c, d ), new Vector4( e, f, white, 1 / atWhite ));
	}

	/// <summary>
	/// The volumes' bloom weighed together (materialsystem2.dll): divided by the total weight only if it's over one; none
	/// if no volume has any.
	/// </summary>
	private static CS2BloomSettings BlendBloom( List<(CS2PostProcessVolume Volume, float Weight)> volumes )
	{
		CS2BloomSettings sum = null;
		var total = 0f;
		foreach ( var (volume, weight) in volumes )
		{
			if ( !volume.HasBloom || volume.Bloom is not { } bloom )
				continue;

			sum ??= new CS2BloomSettings { BlurWeight = new float[5], BlurTint = new Vector3[5] };
			sum.BloomStrength += bloom.BloomStrength * weight;
			sum.ScreenBloomStrength += bloom.ScreenBloomStrength * weight;
			sum.BlurBloomStrength += bloom.BlurBloomStrength * weight;
			sum.BloomThreshold += bloom.BloomThreshold * weight;
			sum.BloomThresholdWidth += bloom.BloomThresholdWidth * weight;
			sum.SkyboxBloomStrength += bloom.SkyboxBloomStrength * weight;
			sum.BloomStartValue += bloom.BloomStartValue * weight;
			for ( var i = 0; i < 5; i++ )
			{
				sum.BlurWeight[i] += (i < bloom.BlurWeight.Length ? bloom.BlurWeight[i] : 0) * weight;
				sum.BlurTint[i] += (i < bloom.BlurTint.Length ? bloom.BlurTint[i] : Vector3.One) * weight;
			}

			total += weight;
		}

		if ( sum is null || total <= 1 )
			return sum;

		var scale = 1 / total;
		sum.BloomStrength *= scale;
		sum.ScreenBloomStrength *= scale;
		sum.BlurBloomStrength *= scale;
		sum.BloomThreshold *= scale;
		sum.BloomThresholdWidth *= scale;
		sum.SkyboxBloomStrength *= scale;
		sum.BloomStartValue *= scale;
		for ( var i = 0; i < 5; i++ )
		{
			sum.BlurWeight[i] *= scale;
			sum.BlurTint[i] *= scale;
		}

		return sum;
	}

	/// <summary>
	/// The heaviest volume's colour correction table, and the weight left for the colour without one: one less the
	/// tables' weights (client.dll). The game combines overlapping tables into one; while two fade, only the heavier
	/// shows here.
	/// </summary>
	private (Texture Table, float DefaultWeight) BlendColorCorrection( List<(CS2PostProcessVolume Volume, float Weight)> volumes )
	{
		Texture table = null;
		var heaviest = 0f;
		var total = 0f;
		foreach ( var (volume, weight) in volumes )
		{
			if ( string.IsNullOrEmpty( volume.ColorCorrection ) )
				continue;

			if ( !_colorCorrections.TryGetValue( volume.ColorCorrection, out var texture ) )
			{
				texture = Texture.Load( volume.ColorCorrection, false );
				_colorCorrections[volume.ColorCorrection] = texture;
			}

			if ( texture is null || !texture.IsValid )
				continue;

			total += weight;
			if ( weight > heaviest )
			{
				table = texture;
				heaviest = weight;
			}
		}

		return (table, MathF.Max( 0, 1 - total ));
	}
}
