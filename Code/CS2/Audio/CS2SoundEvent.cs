using System;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CS2;

/// <summary>
/// A CS2 sound event (soundevents/*.vsndevts) as the CS2 mount serves it, at <c>mount://cs2/soundevents/name.vsndevt</c>,
/// its base events merged in: a random track of its list played with its volume, pitch, delay and distance curve, and
/// its child events with it. Play one with <see cref="Play(string, Vector3)"/>.
/// </summary>
/// <remarks>
/// Plays the game's "csgo_mega" event type, which nearly every CS2 event is, as ValveResourceFormat's sound event player
/// does. Not ported: mix groups, instance limits and blocking, retriggers, occlusion strength (any is on or off), the
/// stereo mapping curve.
/// </remarks>
public sealed class CS2SoundEvent : GameResource
{
	[Hide, JsonIgnore] public string Name { get; set; }

	/// <summary>
	/// The event type, e.g. <c>csgo_mega</c>.
	/// </summary>
	[Hide, JsonIgnore] public string Type { get; set; }

	/// <summary>
	/// The sounds one is picked from (vsnd_files_track_01), e.g. <c>sounds/weapons/ak47/ak47_01.vsnd</c>.
	/// </summary>
	[Hide, JsonIgnore] public string[] Tracks { get; set; } = [];

	[Hide, JsonIgnore] public float Volume { get; set; } = 1f;
	[Hide, JsonIgnore] public float Pitch { get; set; } = 1f;

	/// <summary>
	/// Random amounts added to the volume and pitch each play.
	/// </summary>
	[Hide, JsonIgnore] public Vector2 VolumeRandom { get; set; }
	[Hide, JsonIgnore] public Vector2 PitchRandom { get; set; }

	/// <summary>
	/// Seconds before it starts.
	/// </summary>
	[Hide, JsonIgnore] public float Delay { get; set; }

	/// <summary>
	/// Added to where it plays, e.g. a weapon's shot 60 units above its owner's origin.
	/// </summary>
	[Hide, JsonIgnore] public Vector3 PositionOffset { get; set; }

	/// <summary>
	/// Events started with this one (soundevent_01, when enable_child_events is set), such as a shot's distant layer.
	/// </summary>
	[Hide, JsonIgnore] public string[] Children { get; set; } = [];

	/// <summary>
	/// The mix group it plays in (mixgroup), e.g. Weapons, Footsteps, Foley; its volume scales with the group's
	/// (<see cref="SetVolume"/>).
	/// </summary>
	[Hide, JsonIgnore] public string MixGroup { get; set; }

	/// <summary>
	/// Whether geometry between the listener and the sound muffles it (occlusion_intensity above 0).
	/// </summary>
	[Hide, JsonIgnore] public bool Occludable { get; set; }

	/// <summary>
	/// The distance to volume curve (distance_volume_mapping_curve), or null when the event has none.
	/// </summary>
	[Hide, JsonIgnore] public CS2SoundCurve DistanceVolume { get; set; }

	private SoundEvent _sound;

	/// <summary>
	/// The volume every CS2 sound plays at, and each mix group's (by name, case doesn't matter; 1 when unset).
	/// </summary>
	public static float MasterVolume { get; set; } = 1f;

	private static readonly Dictionary<string, float> MixGroupVolumes = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// Sets a mix group's volume, e.g. <c>SetVolume( "Footsteps", 0.5f )</c>.
	/// </summary>
	public static void SetVolume( string mixGroup, float volume ) => MixGroupVolumes[mixGroup ?? ""] = volume;

	private float GroupVolume => MasterVolume * (MixGroupVolumes.TryGetValue( MixGroup ?? "", out var volume ) ? volume : 1f);

	/// <summary>
	/// A mounted sound event by name (case doesn't matter), or null when the CS2 mount has none.
	/// </summary>
	public static CS2SoundEvent Find( string name )
	{
		if ( string.IsNullOrWhiteSpace( name ) )
			return null;

		return GameResource.Load<CS2SoundEvent>( $"mount://cs2/soundevents/{name.ToLowerInvariant()}.vsndevt" ) is { Name: not null } sound ? sound : null;
	}

	/// <summary>
	/// Plays a sound event by name at a point, with its children; see <see cref="Play(Vector3, float)"/>.
	/// </summary>
	public static SoundHandle Play( string name, Vector3 position, float volume = 1f ) => Find( name )?.Play( position, volume );

	/// <summary>
	/// Plays the event at a point (its own offset added), with its children, its volume scaled. Returns the event's own
	/// sound, unless it starts later (or has none).
	/// </summary>
	public SoundHandle Play( Vector3 position, float volume = 1f )
	{
		return Play( position, volume, 0 );
	}

	private SoundHandle Play( Vector3 position, float volume, int depth )
	{
		// Children could name each other in a loop.
		if ( depth > 8 )
			return null;

		SoundHandle handle = null;
		if ( Delay > 0f )
			_ = PlayDelayed( position + PositionOffset, volume, Delay );
		else
			handle = Start( position + PositionOffset, volume );

		foreach ( var child in Children )
			Find( child )?.Play( position, volume, depth + 1 );

		return handle;
	}

	private async Task PlayDelayed( Vector3 position, float volume, float delay )
	{
		await GameTask.DelaySeconds( delay );
		Start( position, volume );
	}

	private SoundHandle Start( Vector3 position, float volume )
	{
		if ( Resolve() is not { } sound )
			return null;

		volume *= GroupVolume;
		if ( volume <= 0f )
			return null;

		var handle = Sound.Play( sound, position );
		if ( handle.IsValid && volume != 1f )
			handle.Volume *= volume;

		return handle;
	}

	/// <summary>
	/// The event as an s&amp;box sound event: its tracks, its volume and pitch ranges, and its distance curve as the
	/// falloff over the curve's reach (a flat curve is a constant gain instead, and without one it reaches 1000 units,
	/// as ValveResourceFormat has it).
	/// </summary>
	private SoundEvent Resolve()
	{
		if ( _sound is not null )
			return _sound;

		var sounds = new List<SoundFile>();
		foreach ( var track in Tracks )
		{
			if ( SoundFile.Load( $"mount://cs2/{track.Replace( '\\', '/' )}" ) is not { IsValid: true } file )
				continue;

			// Decoded now, or the first play of it is silent.
			file.Preload();
			sounds.Add( file );
		}

		if ( sounds.Count == 0 )
			return null;

		var gain = 1f;
		var distance = 1000f;
		Curve falloff = new( new Curve.Frame( 0, 1 ), new Curve.Frame( 1, 0 ) );

		if ( DistanceVolume is { } curve )
		{
			if ( curve.Attenuates )
			{
				distance = curve.MaxX;
				const int samples = 32;
				var frames = new Curve.Frame[samples + 1];
				for ( var i = 0; i <= samples; i++ )
				{
					var t = i / (float)samples;
					frames[i] = new Curve.Frame( t, curve.Evaluate( t * distance ) ) { Mode = Curve.HandleMode.Linear };
				}

				falloff = new Curve( frames );
			}
			else
			{
				gain = curve.Evaluate( 0 );
			}
		}

		float VolumeWith( float random ) => Math.Clamp( Volume + random, 0f, 1f ) * gain;
		float PitchWith( float random ) => Math.Clamp( Pitch + random, 0.25f, 4f );

		return _sound = new SoundEvent
		{
			Sounds = sounds,
			SelectionMode = SoundEvent.SoundSelectionMode.Random,
			Volume = new RangedFloat( VolumeWith( VolumeRandom.x ), VolumeWith( VolumeRandom.y ) ),
			Pitch = new RangedFloat( PitchWith( PitchRandom.x ), PitchWith( PitchRandom.y ) ),
			DistanceAttenuation = DistanceVolume?.Attenuates ?? true,
			Distance = distance,
			Falloff = falloff,
			OcclusionEnabled = Occludable,
		};
	}
}
