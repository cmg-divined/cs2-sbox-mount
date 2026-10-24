using System;
using System.Globalization;
using CS2;

namespace Sandbox.Mounting.CS2;

/// <summary>
/// Loads a CS2 sound event, its base events merged in, as a <see cref="CS2SoundEvent"/>. Fields are read as the game's
/// data has them, leniently as ValveResourceFormat does: a number written as text (or several, "1.0,1.0") reads its
/// first value.
/// </summary>
internal sealed class SoundEventLoader( string name ) : ResourceLoader<CS2Mount>
{
	protected override object Load()
	{
		var data = Host.GetSoundEvent( name ) ?? throw new InvalidOperationException( $"Sound event '{name}' isn't mounted." );
		var type = data.GetString( "type", "" );

		return new CS2SoundEvent
		{
			Name = name,
			Type = type,
			Tracks = StringOrArray( data, "vsnd_files_track_01" ),
			Volume = Float( data, "volume", 1f ),
			Pitch = Float( data, "pitch", 1f ),
			VolumeRandom = new Vector2( Float( data, "volume_random_min" ), Float( data, "volume_random_max" ) ),
			PitchRandom = new Vector2( Float( data, "pitch_random_min" ), Float( data, "pitch_random_max" ) ),
			Delay = Float( data, "delay" ),
			PositionOffset = Vector( data, "position_offset" ),
			Children = Bool( data, "enable_child_events" ) ? StringOrArray( data, "soundevent_01" ) : [],
			MixGroup = data.GetString( "mixgroup" ),
			Occludable = Float( data, "occlusion_intensity" ) > 0f,
			DistanceVolume = data.GetArray( "distance_volume_mapping_curve" ) is { Length: > 0 } points
				? CS2SoundCurve.FromPoints( points.OfType<object[]>().Select( x => x.Select( Number ).ToArray() ) )
				: null,
		};
	}

	private static string[] StringOrArray( KVObject data, string key ) => data[key] switch
	{
		object[] values => values.OfType<string>().Where( x => x.Length > 0 ).ToArray(),
		string value when value.Length > 0 => [value],
		_ => [],
	};

	private static float Float( KVObject data, string key, float fallback = 0f ) => data[key] switch
	{
		null => fallback,
		object[] { Length: 1 } values => Number( values[0] ),
		object[] => fallback,
		var value => Number( value ),
	};

	private static bool Bool( KVObject data, string key ) => data[key] switch
	{
		null or object[] => false,
		bool value => value,
		var value => Number( value ) > 0f,
	};

	private static Vector3 Vector( KVObject data, string key )
	{
		var values = data[key] switch
		{
			object[] { Length: 3 } array => array.Select( Number ).ToArray(),
			string text => Numbers( text, 3 ),
			_ => [0f, 0f, 0f],
		};

		return new Vector3( values[0], values[1], values[2] );
	}

	/// <summary>
	/// A value as a number: text reads its first number, like atof (the longest numeric prefix of the first token).
	/// </summary>
	private static float Number( object value ) => value switch
	{
		string text => Numbers( text, 1 )[0],
		bool flag => flag ? 1f : 0f,
		_ => KVValue.ToFloat( value ),
	};

	/// <summary>
	/// The numbers in text split on brackets, commas and spaces; components past the last one repeat it.
	/// </summary>
	private static float[] Numbers( string text, int count )
	{
		var result = new float[count];
		var found = 0;

		foreach ( var token in text.Split( ['[', ']', ',', ' '], StringSplitOptions.RemoveEmptyEntries ) )
		{
			var value = 0f;
			for ( var length = token.Length; length > 0; length-- )
			{
				if ( float.TryParse( token.AsSpan( 0, length ), NumberStyles.Float, CultureInfo.InvariantCulture, out value ) )
					break;
			}

			result[found++] = value;
			if ( found == count )
				return result;
		}

		var last = found > 0 ? result[found - 1] : 0f;
		for ( var i = found; i < count; i++ )
			result[i] = last;

		return result;
	}
}
