using System;
using System.IO;

namespace Sandbox.Mounting.CS2;

public sealed partial class CS2Mount
{
	/// <summary>
	/// Every sound event of the game's soundevents/*.vsndevts files, by name (case doesn't matter), as they are written:
	/// the first file to define a name keeps it. Base events are merged in when an event loads.
	/// </summary>
	private readonly Dictionary<string, KVObject> _soundEvents = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// Registers the sounds (vsnd) and the sound events, each at <c>soundevents/name.vsndevt</c>.
	/// </summary>
	private void MountSounds( MountContext context )
	{
		_soundEvents.Clear();
		var sounds = 0;

		foreach ( var file in _files.Keys )
		{
			if ( file.EndsWith( ".vsnd_c", StringComparison.OrdinalIgnoreCase ) )
			{
				context.Add( ResourceType.Sound, file[..^2], new SoundLoader( file ) );
				sounds++;
			}
			else if ( file.StartsWith( "soundevents/", StringComparison.OrdinalIgnoreCase ) && file.EndsWith( ".vsndevts_c", StringComparison.OrdinalIgnoreCase ) )
			{
				try
				{
					if ( new ResourceFile( ReadFile( file ) ).ReadKeyValues( "DATA" ) is not { } events )
						continue;

					foreach ( var name in events.Keys )
					{
						if ( events[name] is KVObject soundEvent )
							_soundEvents.TryAdd( name, soundEvent );
					}
				}
				catch ( Exception e ) when ( e is InvalidDataException or NotSupportedException or IOException )
				{
					Log.Warning( $"Couldn't read the sound events in '{file}': {e.Message}" );
				}
			}
		}

		foreach ( var name in _soundEvents.Keys )
			context.Add( ResourceType.GameResource, $"soundevents/{name.ToLowerInvariant()}.vsndevt", new SoundEventLoader( name ) );

		Log.Info( $"Mounted {sounds} sounds and {_soundEvents.Count} sound events." );
	}

	/// <summary>
	/// A sound event with its base events merged in: a base is one event's name, or a list of fragments naming events
	/// in <c>event_name</c>, each giving only what the event doesn't have (a fragment's own type isn't inherited).
	/// </summary>
	internal KVObject GetSoundEvent( string name ) => _soundEvents.TryGetValue( name, out var soundEvent ) ? ResolveBase( soundEvent, 0 ) : null;

	private KVObject ResolveBase( KVObject soundEvent, int depth )
	{
		if ( depth > 8 || soundEvent["base"] is not { } baseValue )
			return soundEvent;

		var merged = new KVObject();
		foreach ( var key in soundEvent.Keys )
		{
			if ( !key.Equals( "base", StringComparison.OrdinalIgnoreCase ) )
				merged[key] = soundEvent[key];
		}

		void MergeBase( string baseName, bool inheritType )
		{
			if ( string.IsNullOrEmpty( baseName ) || !_soundEvents.TryGetValue( baseName, out var baseEvent ) )
				return;

			var resolved = ResolveBase( baseEvent, depth + 1 );
			foreach ( var key in resolved.Keys )
			{
				if ( !inheritType && key.Equals( "type", StringComparison.OrdinalIgnoreCase ) )
					continue;

				if ( !merged.ContainsKey( key ) )
					merged[key] = resolved[key];
			}
		}

		if ( baseValue is object[] fragments )
		{
			foreach ( var fragment in fragments.OfType<KVObject>() )
				MergeBase( fragment.GetString( "event_name" ), inheritType: false );
		}
		else
		{
			MergeBase( baseValue as string, inheritType: true );
		}

		return merged;
	}
}
